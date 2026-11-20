using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeManagementReportingService
{
    private static SalesCogsProjection ReadSalesAndCogs(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        List<SaleFact> sales = new();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    s.id,
                    s.subtotal_cents,
                    s.discount_cents,
                    s.fee_total_cents,
                    s.total_cents,
                    f.financial_decision_id,
                    f.gross_subtotal_cents,
                    f.line_discount_total_cents,
                    f.order_discount_total_cents,
                    f.fee_total_cents,
                    f.net_sale_total_cents,
                    d.result,
                    d.created_at_utc,
                    d.line_count
                FROM sales s
                LEFT JOIN cafe_sale_financial_decisions f
                    ON f.sale_id=s.id
                LEFT JOIN sale_ingredient_consumption_decisions d
                    ON d.sale_id=s.id
                WHERE s.status=$completed
                  AND s.sale_date >= $start
                  AND s.sale_date < $end
                ORDER BY s.sale_date,s.id;
                """;
            command.Parameters.AddWithValue(
                "$completed",
                Convert.ToInt32(SaleStatus.Completed));
            command.Parameters.AddWithValue("$start", Iso(start));
            command.Parameters.AddWithValue("$end", Iso(end));

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                bool hasFinancial =
                    !reader.IsDBNull(5);

                long recognizedRevenue =
                    hasFinancial
                        ? reader.GetInt64(10)
                        : reader.GetInt64(4);

                sales.Add(
                    new(
                        Guid.Parse(reader.GetString(0)),
                        hasFinancial
                            ? reader.GetInt64(6)
                            : reader.GetInt64(1),
                        hasFinancial
                            ? reader.GetInt64(7)
                            : reader.GetInt64(2),
                        hasFinancial
                            ? reader.GetInt64(8)
                            : 0,
                        hasFinancial
                            ? reader.GetInt64(9)
                            : reader.GetInt64(3),
                        recognizedRevenue,
                        hasFinancial,
                        reader.IsDBNull(11)
                            ? null
                            : reader.GetString(11),
                        reader.IsDBNull(12)
                            ? null
                            : ParseDate(reader.GetString(12)),
                        reader.IsDBNull(13)
                            ? null
                            : reader.GetInt32(13)));
            }
        }

        long refundCents =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(total_refund_cents),0)
                FROM cafe_sale_refunds
                WHERE state='Committed'
                  AND committed_at_utc >= $start
                  AND committed_at_utc < $end;
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end)));

        int voidedCount =
            checked((int)ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sales
                WHERE status=$voided
                  AND sale_date >= $start
                  AND sale_date < $end;
                """,
                ("$voided", Convert.ToInt32(SaleStatus.Voided)),
                ("$start", Iso(start)),
                ("$end", Iso(end))));

        long gross = sales.Sum(x => x.GrossSubtotalCents);
        long lineDiscount =
            sales.Sum(x => x.LineDiscountCents);
        long orderDiscount =
            sales.Sum(x => x.OrderDiscountCents);
        long fees =
            sales.Sum(x => x.FeeCents);
        long saleRevenue =
            sales.Sum(x => x.RecognizedRevenueCents);

        Dictionary<Guid, SaleItemCogs> itemCogs = new();
        Dictionary<string, SaleItemCogs> bindingCogs =
            new(StringComparer.Ordinal);
        HashSet<Guid> incompleteSales = new();
        HashSet<Guid> uncostedLines = new();
        List<string> reasons = new();
        int consumptionLines = 0;
        int costedLines = 0;
        long historicalCogs = 0;

        foreach (SaleFact sale in sales)
        {
            if (string.Equals(
                    sale.ConsumptionResult,
                    "NO_RECIPE_APPLIED",
                    StringComparison.Ordinal) &&
                sale.ConsumptionAtUtc is not null &&
                sale.ConsumptionLineCount == 0)
            {
                // The C2 decision proves no ingredient consumption was
                // invented, but C11 cannot infer authoritative zero cost for
                // a revenue-bearing Sale from absence of a recipe. Preserve
                // revenue and explicitly downgrade cost coverage.
                if (sale.RecognizedRevenueCents > 0)
                {
                    incompleteSales.Add(sale.SaleId);
                    reasons.Add(
                        "One or more revenue-bearing Sales have NO_RECIPE_APPLIED and therefore lack authoritative historical COGS evidence.");
                }

                continue;
            }

            if (!string.Equals(
                    sale.ConsumptionResult,
                    "APPLIED",
                    StringComparison.Ordinal) ||
                sale.ConsumptionAtUtc is null ||
                sale.ConsumptionLineCount is null ||
                sale.ConsumptionLineCount.Value <= 0)
            {
                if (sale.RecognizedRevenueCents > 0)
                {
                    incompleteSales.Add(sale.SaleId);
                    reasons.Add(
                        "One or more revenue-bearing Sales lack a valid immutable ingredient-consumption decision.");
                }

                continue;
            }

            int retainedConsumptionLineCount =
                checked((int)ScalarLong(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM sale_ingredient_consumption_lines
                    WHERE sale_id=$sale;
                    """,
                    ("$sale", sale.SaleId.ToString("D"))));

            List<ConsumptionLineFact> lines =
                ReadActiveConsumptionLines(
                    connection,
                    transaction,
                    sale.SaleId);

            // Zero active lines can be authoritative only when the immutable
            // decision's original line_count is fully retained and every
            // original line has factual RecipeConsumptionReversal evidence.
            bool saleComplete =
                retainedConsumptionLineCount ==
                    sale.ConsumptionLineCount.Value;

            if (!saleComplete)
            {
                incompleteSales.Add(sale.SaleId);
                reasons.Add(
                    "One or more APPLIED ingredient-consumption decisions do not retain the immutable decision line_count and therefore lack complete historical COGS evidence.");
            }

            foreach (ConsumptionLineFact line in lines)
            {
                consumptionLines++;

                AcquisitionBasis basis =
                    ReadAcquisitionBasis(
                        connection,
                        transaction,
                        "Ingredient",
                        line.IngredientId,
                        sale.ConsumptionAtUtc.Value);

                if (!basis.IsValid)
                {
                    saleComplete = false;
                    incompleteSales.Add(sale.SaleId);
                    uncostedLines.Add(line.ConsumptionLineId);
                    reasons.Add(
                        "One or more consumption lines lack a positive historical acquisition quantity/cost basis.");

                    string missingBindingKey =
                        PerformanceBindingKey(
                            line.SourceBindingType,
                            line.SourceBindingId);

                    if (bindingCogs.TryGetValue(
                            missingBindingKey,
                            out SaleItemCogs? missingBinding))
                    {
                        bindingCogs[missingBindingKey] =
                            missingBinding with { Complete = false };
                    }
                    else
                    {
                        bindingCogs[missingBindingKey] =
                            new(0, false);
                    }

                    continue;
                }

                long lineCost =
                    RoundWeightedCost(
                        line.QuantityConsumedX1000,
                        basis);

                historicalCogs =
                    checked(historicalCogs + lineCost);
                costedLines++;

                if (itemCogs.TryGetValue(
                        line.SaleItemId,
                        out SaleItemCogs? current))
                {
                    itemCogs[line.SaleItemId] =
                        current with
                        {
                            HistoricalCogsCents =
                                checked(
                                    current.HistoricalCogsCents +
                                    lineCost),
                            Complete =
                                current.Complete && basis.IsValid
                        };
                }
                else
                {
                    itemCogs[line.SaleItemId] =
                        new(lineCost, true);
                }

                string bindingKey =
                    PerformanceBindingKey(
                        line.SourceBindingType,
                        line.SourceBindingId);

                if (bindingCogs.TryGetValue(
                        bindingKey,
                        out SaleItemCogs? bindingCurrent))
                {
                    bindingCogs[bindingKey] =
                        bindingCurrent with
                        {
                            HistoricalCogsCents =
                                checked(
                                    bindingCurrent.HistoricalCogsCents +
                                    lineCost)
                        };
                }
                else
                {
                    bindingCogs[bindingKey] =
                        new(lineCost, true);
                }
            }

            if (!saleComplete)
            {
                foreach (ConsumptionLineFact line in lines)
                {
                    if (itemCogs.TryGetValue(
                            line.SaleItemId,
                            out SaleItemCogs? current))
                    {
                        itemCogs[line.SaleItemId] =
                            current with { Complete = false };
                    }
                }
            }
        }

        long affectedRevenue =
            sales
                .Where(x => incompleteSales.Contains(x.SaleId))
                .Sum(x => x.RecognizedRevenueCents);

        int completeSaleCount =
            sales.Count - incompleteSales.Count;

        CafeReportCostCoverage coverage = new(
            incompleteSales.Count == 0
                ? CafeReportCostCoverageStates.Complete
                : CafeReportCostCoverageStates.Incomplete,
            sales.Count,
            completeSaleCount,
            incompleteSales.Count,
            consumptionLines,
            costedLines,
            uncostedLines.Count,
            affectedRevenue,
            incompleteSales.OrderBy(x => x).ToArray(),
            uncostedLines.OrderBy(x => x).ToArray(),
            reasons.Distinct(StringComparer.Ordinal).ToArray());

        CafeReportSalesSummary summary = new(
            sales.Count,
            voidedCount,
            sales.Count(x => x.HasFinancialDecision),
            sales.Count(x => !x.HasFinancialDecision),
            gross,
            lineDiscount,
            orderDiscount,
            fees,
            saleRevenue,
            refundCents,
            checked(saleRevenue - refundCents));

        return new(
            summary,
            coverage,
            historicalCogs,
            itemCogs,
            bindingCogs);
    }

    private static List<ConsumptionLineFact>
        ReadActiveConsumptionLines(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid saleId)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                l.id,
                l.sale_item_id,
                l.ingredient_id,
                l.quantity_consumed_x1000,
                l.source_binding_type,
                l.source_binding_id
            FROM sale_ingredient_consumption_lines l
            WHERE l.sale_id=$sale
              AND NOT EXISTS(
                    SELECT 1
                    FROM ingredient_stock_movements r
                    WHERE r.movement_kind='RecipeConsumptionReversal'
                      AND r.original_consumption_line_id=l.id)
            ORDER BY l.id;
            """;
        command.Parameters.AddWithValue(
            "$sale",
            saleId.ToString("D"));

        List<ConsumptionLineFact> rows = new();

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(
                new(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetString(5)));
        }

        return rows;
    }

    private static AcquisitionBasis ReadAcquisitionBasis(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetType,
        Guid targetId,
        DateTimeOffset atUtc)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COALESCE(SUM(l.quantity_received_x1000),0),
                COALESCE(SUM(l.line_acquisition_cost_cents),0)
            FROM purchase_receipt_lines l
            INNER JOIN purchase_receipts r
                ON r.purchase_receipt_id=l.purchase_receipt_id
            WHERE l.stock_target_type=$type
              AND l.stock_target_id=$target
              AND r.received_at_utc <= $at;
            """;
        command.Parameters.AddWithValue("$type", targetType);
        command.Parameters.AddWithValue(
            "$target",
            targetId.ToString("D"));
        command.Parameters.AddWithValue(
            "$at",
            Iso(atUtc));

        using SqliteDataReader reader =
            command.ExecuteReader();
        reader.Read();

        long quantity =
            reader.GetInt64(0);
        long cost =
            reader.GetInt64(1);

        return new(
            quantity,
            cost,
            quantity > 0 && cost >= 0);
    }

    private static long RoundWeightedCost(
        long quantityX1000,
        AcquisitionBasis basis)
    {
        if (!basis.IsValid)
            throw new InvalidOperationException(
                "Historical acquisition basis is incomplete.");

        decimal exact =
            decimal.Multiply(
                quantityX1000,
                basis.NetCostCents) /
            basis.NetQuantityX1000;

        decimal rounded =
            Math.Round(
                exact,
                0,
                MidpointRounding.AwayFromZero);

        return checked((long)rounded);
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);

        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L);
    }

    private sealed record SaleFact(
        Guid SaleId,
        long GrossSubtotalCents,
        long LineDiscountCents,
        long OrderDiscountCents,
        long FeeCents,
        long RecognizedRevenueCents,
        bool HasFinancialDecision,
        string? ConsumptionResult,
        DateTimeOffset? ConsumptionAtUtc,
        int? ConsumptionLineCount);

    private static string PerformanceBindingKey(
        string bindingType,
        string bindingId) =>
        bindingType + "|" + bindingId;

    private sealed record ConsumptionLineFact(
        Guid ConsumptionLineId,
        Guid SaleItemId,
        Guid IngredientId,
        long QuantityConsumedX1000,
        string SourceBindingType,
        string SourceBindingId);
}

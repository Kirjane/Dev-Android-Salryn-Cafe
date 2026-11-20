using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeManagementReportingService
{
    private static IReadOnlyList<CafeReportPerformanceRow>
        ReadPerformance(
            SqliteConnection connection,
            SqliteTransaction transaction,
            DateTimeOffset start,
            DateTimeOffset end,
            IReadOnlyDictionary<Guid, SaleItemCogs> itemCogs,
            IReadOnlyDictionary<string, SaleItemCogs> bindingCogs)
    {
        Dictionary<string, PerformanceAccumulator> rows =
            new(StringComparer.Ordinal);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    si.id,
                    si.product_id,
                    si.product_name_snapshot,
                    si.qty_x1000,
                    si.unit_price_cents,
                    COALESCE(fd.total_discount_cents,si.discount_cents),
                    ol.order_revision_id,
                    ol.order_line_id,
                    rl.variant_id,
                    rl.variant_name_snapshot,
                    o.menu_publication_revision_id,
                    mri.category_id,
                    mrc.name
                FROM sale_items si
                INNER JOIN sales s
                    ON s.id=si.sale_id
                LEFT JOIN cafe_sale_financial_decisions f
                    ON f.sale_id=s.id
                LEFT JOIN cafe_sale_financial_line_discounts fd
                    ON fd.financial_decision_id=f.financial_decision_id
                   AND fd.sale_item_id=si.id
                LEFT JOIN sale_item_order_links ol
                    ON ol.sale_item_id=si.id
                LEFT JOIN cafe_order_revision_lines rl
                    ON rl.revision_id=ol.order_revision_id
                   AND rl.order_line_id=ol.order_line_id
                LEFT JOIN cafe_orders o
                    ON o.order_id=ol.order_id
                LEFT JOIN menu_revision_items mri
                    ON mri.revision_id=o.menu_publication_revision_id
                   AND mri.product_id=si.product_id
                LEFT JOIN menu_revision_categories mrc
                    ON mrc.revision_id=o.menu_publication_revision_id
                   AND mrc.category_id=mri.category_id
                WHERE s.status=$completed
                  AND s.sale_date >= $start
                  AND s.sale_date < $end
                ORDER BY s.sale_date,si.id;
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
                Guid saleItemId =
                    Guid.Parse(reader.GetString(0));
                string productId =
                    reader.GetString(1);
                string productName =
                    reader.GetString(2);
                long quantity =
                    reader.GetInt64(3);
                long gross =
                    RoundLineRevenue(
                        quantity,
                        reader.GetInt64(4));
                long discount =
                    reader.GetInt64(5);
                long net =
                    checked(gross - discount);

                SaleItemCogs? cogs =
                    itemCogs.TryGetValue(
                        saleItemId,
                        out SaleItemCogs? itemCost)
                        ? itemCost
                        : null;

                AddPerformance(
                    rows,
                    "Product",
                    productId,
                    productName,
                    quantity,
                    gross,
                    discount,
                    net,
                    cogs?.HistoricalCogsCents ?? 0,
                    cogs?.Complete == true);

                if (!reader.IsDBNull(8))
                {
                    string variantId =
                        reader.GetString(8);
                    string variantName =
                        reader.IsDBNull(9)
                            ? variantId
                            : reader.GetString(9);

                    SaleItemCogs? variantCogs =
                        bindingCogs.TryGetValue(
                            PerformanceBindingKey(
                                "Variant",
                                variantId),
                            out SaleItemCogs? found)
                            ? found
                            : null;

                    AddPerformance(
                        rows,
                        "Variant",
                        variantId,
                        variantName,
                        quantity,
                        gross,
                        discount,
                        net,
                        variantCogs?.HistoricalCogsCents ?? 0,
                        variantCogs?.Complete == true);
                }

                if (!reader.IsDBNull(11) &&
                    !reader.IsDBNull(12))
                {
                    string categoryId =
                        reader.GetString(11);
                    string categoryName =
                        reader.GetString(12);

                    AddPerformance(
                        rows,
                        "Category",
                        categoryId,
                        categoryName,
                        quantity,
                        gross,
                        discount,
                        net,
                        cogs?.HistoricalCogsCents ?? 0,
                        cogs?.Complete == true);
                }
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    m.option_id,
                    m.option_name_snapshot,
                    l.quantity_x1000,
                    m.price_delta_cents
                FROM cafe_order_revision_modifiers m
                INNER JOIN cafe_order_revision_lines l
                    ON l.revision_id=m.revision_id
                   AND l.order_line_id=m.order_line_id
                INNER JOIN sale_item_order_links ol
                    ON ol.order_revision_id=m.revision_id
                   AND ol.order_line_id=m.order_line_id
                INNER JOIN sale_items si
                    ON si.id=ol.sale_item_id
                INNER JOIN sales s
                    ON s.id=si.sale_id
                WHERE s.status=$completed
                  AND s.sale_date >= $start
                  AND s.sale_date < $end
                ORDER BY m.option_id,m.revision_id,m.order_line_id;
                """;
            command.Parameters.AddWithValue(
                "$completed",
                Convert.ToInt32(SaleStatus.Completed));
            command.Parameters.AddWithValue("$start", Iso(start));
            command.Parameters.AddWithValue("$end", Iso(end));

            Dictionary<string, bool> bindingCharged = new(
                StringComparer.Ordinal);

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                string optionId =
                    reader.GetString(0);
                string optionName =
                    reader.GetString(1);
                long quantity =
                    reader.GetInt64(2);
                long gross =
                    RoundLineRevenue(
                        quantity,
                        reader.GetInt64(3));

                string bindingKey =
                    PerformanceBindingKey(
                        "ModifierOption",
                        optionId);

                SaleItemCogs? optionCogs =
                    bindingCogs.TryGetValue(
                        bindingKey,
                        out SaleItemCogs? found)
                        ? found
                        : null;

                bool firstBindingOccurrence =
                    !bindingCharged.ContainsKey(bindingKey);
                bindingCharged[bindingKey] = true;

                AddPerformance(
                    rows,
                    "ModifierOption",
                    optionId,
                    optionName,
                    quantity,
                    gross,
                    0,
                    gross,
                    firstBindingOccurrence
                        ? optionCogs?.HistoricalCogsCents ?? 0
                        : 0,
                    optionCogs?.Complete == true);
            }
        }

        return rows.Values
            .Select(x => new CafeReportPerformanceRow(
                x.DimensionType,
                x.DimensionId,
                x.DisplayName,
                x.QuantityX1000,
                x.GrossSalesCents,
                x.DiscountCents,
                x.NetProductRevenueCents,
                x.HistoricalCogsCents,
                x.Complete
                    ? CafeReportCostCoverageStates.Complete
                    : CafeReportCostCoverageStates.Incomplete,
                checked(
                    x.NetProductRevenueCents -
                    x.HistoricalCogsCents)))
            .OrderByDescending(x => x.NetProductRevenueCents)
            .ThenBy(x => x.DimensionType, StringComparer.Ordinal)
            .ThenBy(x => x.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private static long RoundLineRevenue(
        long quantityX1000,
        long unitPriceCents)
    {
        decimal exact =
            decimal.Multiply(
                quantityX1000,
                unitPriceCents) /
            1000m;

        return checked(
            (long)Math.Round(
                exact,
                0,
                MidpointRounding.AwayFromZero));
    }

    private static void AddPerformance(
        Dictionary<string, PerformanceAccumulator> rows,
        string type,
        string id,
        string displayName,
        long quantity,
        long gross,
        long discount,
        long net,
        long cogs,
        bool complete)
    {
        string key =
            type + "|" + id;

        if (rows.TryGetValue(
                key,
                out PerformanceAccumulator? current))
        {
            rows[key] =
                current with
                {
                    QuantityX1000 =
                        checked(
                            current.QuantityX1000 +
                            quantity),
                    GrossSalesCents =
                        checked(
                            current.GrossSalesCents +
                            gross),
                    DiscountCents =
                        checked(
                            current.DiscountCents +
                            discount),
                    NetProductRevenueCents =
                        checked(
                            current.NetProductRevenueCents +
                            net),
                    HistoricalCogsCents =
                        checked(
                            current.HistoricalCogsCents +
                            cogs),
                    Complete =
                        current.Complete && complete
                };
            return;
        }

        rows[key] = new(
            type,
            id,
            displayName,
            quantity,
            gross,
            discount,
            net,
            cogs,
            complete);
    }

    private sealed record PerformanceAccumulator(
        string DimensionType,
        string DimensionId,
        string DisplayName,
        long QuantityX1000,
        long GrossSalesCents,
        long DiscountCents,
        long NetProductRevenueCents,
        long HistoricalCogsCents,
        bool Complete);
}

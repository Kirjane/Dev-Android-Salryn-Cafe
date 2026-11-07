using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Api.Local.Services;

public sealed class OwnerReportsApiService
{
    private static readonly TimeZoneInfo StoreTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Asia/Manila",
        TimeSpan.FromHours(8),
        "Philippine Standard Time",
        "Philippine Standard Time");
    private const string WeekRule = "Current calendar week, Monday 00:00 through next Monday 00:00 in Asia/Manila.";
    private const string OfficialBoundary = "Owner Reports are store operational reports only. Not an official/BIR/RMO report.";

    private readonly SalrynLocalComposition _composition;

    public OwnerReportsApiService(SalrynLocalComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public Task<Result<OwnerReportsResponse>> GetSummaryAsync(
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default,
        bool includeReceivables = true)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            DateTimeOffset now = TimeZoneInfo.ConvertTime(nowOverride ?? TimeProvider.System.GetUtcNow(), StoreTimeZone);
            DateTimeOffset todayStart = new(now.Year, now.Month, now.Day, 0, 0, 0, StoreTimeZone.BaseUtcOffset);
            DateTimeOffset todayEnd = todayStart.AddDays(1);
            int mondayOffset = ((int)todayStart.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            DateTimeOffset weekStart = todayStart.AddDays(-mondayOffset);
            DateTimeOffset weekEnd = weekStart.AddDays(7);
            DateTimeOffset monthStart = new(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            DateTimeOffset monthEnd = monthStart.AddMonths(1);

            using SqliteConnection connection = _composition.ConnectionFactory.CreateConnection();
            connection.Open();

            OwnerSalesPeriodSummary daily = ReadPeriodSummary(
                connection,
                "daily",
                "Today",
                todayStart,
                todayEnd);
            OwnerSalesPeriodSummary weekly = ReadPeriodSummary(
                connection,
                "weekly",
                "Current calendar week",
                weekStart,
                weekEnd);
            OwnerSalesPeriodSummary monthly = ReadPeriodSummary(
                connection,
                "monthly",
                "Current calendar month",
                monthStart,
                monthEnd);

            OwnerReportsResponse response = new(
                GeneratedAt: now,
                WeekRule: WeekRule,
                OfficialBoundary: OfficialBoundary,
                Daily: daily,
                Weekly: weekly,
                Monthly: monthly,
                TopProducts: ReadTopProducts(connection, monthStart, monthEnd),
                CashierTotals: ReadCashierTotals(connection, monthStart, monthEnd),
                CategoryTotals: ReadCategoryTotals(connection, monthStart, monthEnd),
                LowStock: ReadLowStock(connection),
                InventoryValue: ReadInventoryValue(connection),
                Receivables: includeReceivables
                    ? ReadReceivables(connection, monthStart, monthEnd, todayStart)
                    : null);

            return Task.FromResult(Result<OwnerReportsResponse>.Success(response));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Task.FromResult(Result<OwnerReportsResponse>.Fail(Error.Unexpected(
                "OwnerReports.ReadFailed",
                ex.Message)));
        }
    }

    private static OwnerSalesPeriodSummary ReadPeriodSummary(
        SqliteConnection connection,
        string periodKey,
        string label,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN status = $completedStatus THEN 1 ELSE 0 END) AS sales_count,
                COALESCE(SUM(CASE WHEN status = $completedStatus THEN total_cents ELSE 0 END), 0) AS sales_amount_cents,
                SUM(CASE WHEN status = $voidedStatus THEN 1 ELSE 0 END) AS voided_count,
                COALESCE(SUM(CASE WHEN status = $voidedStatus THEN total_cents ELSE 0 END), 0) AS voided_amount_cents
            FROM sales
            WHERE sale_date >= $rangeStart
              AND sale_date < $rangeEnd;
            """;
        AddRangeParameters(command, rangeStart, rangeEnd);
        command.Parameters.AddWithValue("$completedStatus", Convert.ToInt32(SaleStatus.Completed));
        command.Parameters.AddWithValue("$voidedStatus", Convert.ToInt32(SaleStatus.Voided));

        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();

        int salesCount = Convert.ToInt32(ReadLong(reader, "sales_count"));
        long salesAmountCents = ReadLong(reader, "sales_amount_cents");
        int voidedCount = Convert.ToInt32(ReadLong(reader, "voided_count"));
        long voidedAmountCents = ReadLong(reader, "voided_amount_cents");
        long averageSaleCents = salesCount > 0
            ? salesAmountCents / salesCount
            : 0;

        return new OwnerSalesPeriodSummary(
            periodKey,
            label,
            rangeStart,
            rangeEnd,
            salesCount,
            salesAmountCents,
            averageSaleCents,
            voidedCount,
            voidedAmountCents);
    }

    private static IReadOnlyList<OwnerTopProductRow> ReadTopProducts(
        SqliteConnection connection,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                si.product_id,
                si.product_name_snapshot,
                si.barcode_snapshot,
                COALESCE(SUM(si.qty_x1000), 0) AS quantity_x1000,
                COALESCE(SUM(si.line_total_cents), 0) AS sales_amount_cents
            FROM sale_items si
            INNER JOIN sales s ON s.id = si.sale_id
            WHERE s.status = $completedStatus
              AND s.sale_date >= $rangeStart
              AND s.sale_date < $rangeEnd
            GROUP BY si.product_id, si.product_name_snapshot, si.barcode_snapshot
            ORDER BY sales_amount_cents DESC, quantity_x1000 DESC, si.product_name_snapshot ASC
            LIMIT 10;
            """;
        AddRangeParameters(command, rangeStart, rangeEnd);
        command.Parameters.AddWithValue("$completedStatus", Convert.ToInt32(SaleStatus.Completed));

        using SqliteDataReader reader = command.ExecuteReader();
        List<OwnerTopProductRow> rows = new();
        while (reader.Read())
        {
            rows.Add(new OwnerTopProductRow(
                Guid.Parse(ReadString(reader, "product_id")),
                ReadString(reader, "product_name_snapshot"),
                ReadNullableString(reader, "barcode_snapshot"),
                FromX1000(ReadLong(reader, "quantity_x1000")),
                ReadLong(reader, "sales_amount_cents")));
        }

        return rows;
    }

    private static IReadOnlyList<OwnerCashierTotalRow> ReadCashierTotals(
        SqliteConnection connection,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.cashier_user_id,
                u.display_name AS cashier_display_name,
                SUM(CASE WHEN s.status = $completedStatus THEN 1 ELSE 0 END) AS sales_count,
                COALESCE(SUM(CASE WHEN s.status = $completedStatus THEN s.total_cents ELSE 0 END), 0) AS sales_amount_cents,
                SUM(CASE WHEN s.status = $voidedStatus THEN 1 ELSE 0 END) AS voided_count,
                COALESCE(SUM(CASE WHEN s.status = $voidedStatus THEN s.total_cents ELSE 0 END), 0) AS voided_amount_cents
            FROM sales s
            INNER JOIN users u ON u.id = s.cashier_user_id
            WHERE s.sale_date >= $rangeStart
              AND s.sale_date < $rangeEnd
            GROUP BY s.cashier_user_id, u.display_name
            ORDER BY sales_amount_cents DESC, cashier_display_name ASC;
            """;
        AddRangeParameters(command, rangeStart, rangeEnd);
        command.Parameters.AddWithValue("$completedStatus", Convert.ToInt32(SaleStatus.Completed));
        command.Parameters.AddWithValue("$voidedStatus", Convert.ToInt32(SaleStatus.Voided));

        using SqliteDataReader reader = command.ExecuteReader();
        List<OwnerCashierTotalRow> rows = new();
        while (reader.Read())
        {
            rows.Add(new OwnerCashierTotalRow(
                Guid.Parse(ReadString(reader, "cashier_user_id")),
                ReadString(reader, "cashier_display_name"),
                Convert.ToInt32(ReadLong(reader, "sales_count")),
                ReadLong(reader, "sales_amount_cents"),
                Convert.ToInt32(ReadLong(reader, "voided_count")),
                ReadLong(reader, "voided_amount_cents")));
        }

        return rows;
    }

    private static IReadOnlyList<OwnerCategoryTotalRow> ReadCategoryTotals(
        SqliteConnection connection,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(c.name, 'Uncategorized') AS category_name,
                COALESCE(SUM(si.qty_x1000), 0) AS quantity_x1000,
                COALESCE(SUM(si.line_total_cents), 0) AS sales_amount_cents
            FROM sale_items si
            INNER JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            LEFT JOIN categories c ON c.id = p.category_id
            WHERE s.status = $completedStatus
              AND s.sale_date >= $rangeStart
              AND s.sale_date < $rangeEnd
            GROUP BY COALESCE(c.name, 'Uncategorized')
            ORDER BY sales_amount_cents DESC, category_name ASC;
            """;
        AddRangeParameters(command, rangeStart, rangeEnd);
        command.Parameters.AddWithValue("$completedStatus", Convert.ToInt32(SaleStatus.Completed));

        using SqliteDataReader reader = command.ExecuteReader();
        List<OwnerCategoryTotalRow> rows = new();
        while (reader.Read())
        {
            rows.Add(new OwnerCategoryTotalRow(
                ReadString(reader, "category_name"),
                FromX1000(ReadLong(reader, "quantity_x1000")),
                ReadLong(reader, "sales_amount_cents")));
        }

        return rows;
    }

    private static IReadOnlyList<OwnerLowStockRow> ReadLowStock(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                p.id,
                p.name,
                p.barcode,
                u.name AS unit_name,
                p.stock_on_hand_x1000,
                p.reorder_level_x1000
            FROM products p
            INNER JOIN units u ON u.id = p.unit_id
            WHERE p.is_active = 1
              AND p.track_stock = 1
              AND p.reorder_level_x1000 > 0
              AND p.stock_on_hand_x1000 <= p.reorder_level_x1000
            ORDER BY p.name;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        List<OwnerLowStockRow> rows = new();
        while (reader.Read())
        {
            rows.Add(new OwnerLowStockRow(
                Guid.Parse(ReadString(reader, "id")),
                ReadString(reader, "name"),
                ReadNullableString(reader, "barcode"),
                ReadString(reader, "unit_name"),
                FromX1000(ReadLong(reader, "stock_on_hand_x1000")),
                FromX1000(ReadLong(reader, "reorder_level_x1000"))));
        }

        return rows;
    }

    private static OwnerInventoryValueSummary ReadInventoryValue(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                stock_on_hand_x1000,
                cost_price_cents
            FROM products
            WHERE is_active = 1
              AND track_stock = 1;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        int valuedProducts = 0;
        int missingCostProducts = 0;
        decimal estimatedValueCents = 0m;

        while (reader.Read())
        {
            long stockX1000 = Convert.ToInt64(reader.GetValue(0));
            if (reader.IsDBNull(1))
            {
                missingCostProducts++;
                continue;
            }

            long costCents = Convert.ToInt64(reader.GetValue(1));
            estimatedValueCents += Math.Round(stockX1000 * costCents / 1000m, 0, MidpointRounding.AwayFromZero);
            valuedProducts++;
        }

        if (valuedProducts == 0)
        {
            return new OwnerInventoryValueSummary(
                false,
                null,
                valuedProducts,
                missingCostProducts,
                "Inventory value unavailable because no active stock-tracked product has cost data.");
        }

        if (missingCostProducts > 0)
        {
            return new OwnerInventoryValueSummary(
                false,
                null,
                valuedProducts,
                missingCostProducts,
                "Inventory value unavailable because one or more active stock-tracked products are missing cost data.");
        }

        return new OwnerInventoryValueSummary(
            true,
            estimatedValueCents,
            valuedProducts,
            missingCostProducts,
            "Inventory value estimated from current stock on hand multiplied by product cost.");
    }

    private static OwnerReceivablesSummary ReadReceivables(
        SqliteConnection connection,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        DateTimeOffset todayStart)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              COALESCE((SELECT SUM(balance_due_cents) FROM sales
                        WHERE status=$completed AND sale_date >= $start AND sale_date < $end),0),
              COALESCE((SELECT SUM(cash_tender_cents - change_cents)
                        FROM (
                            SELECT s.id,
                                   SUM(CASE WHEN p.method=$cash THEN p.amount_cents ELSE 0 END) AS cash_tender_cents,
                                   s.change_cents
                            FROM sales s
                            INNER JOIN payments p ON p.sale_id=s.id
                            WHERE s.status=$completed
                              AND s.sale_date >= $start AND s.sale_date < $end
                            GROUP BY s.id, s.change_cents
                        )),0),
              COALESCE((SELECT SUM(amount_cents) FROM accounts_receivable_collections
                        WHERE voided_at IS NULL AND collected_at >= $start AND collected_at < $end),0),
              COALESCE((SELECT SUM(debit_cents-credit_cents) FROM accounts_receivable_entries),0),
              COALESCE((WITH allocation AS (
                            SELECT due_date, debit_cents,
                                   COALESCE(SUM(CASE WHEN due_date IS NOT NULL THEN debit_cents ELSE 0 END) OVER (
                                       PARTITION BY customer_id ORDER BY created_at, id
                                       ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS prior_debits,
                                   SUM(CASE WHEN entry_type = 'CollectionReversal'
                                            THEN -debit_cents ELSE credit_cents END)
                                       OVER (PARTITION BY customer_id) AS total_credits
                            FROM accounts_receivable_entries
                        )
                        SELECT SUM(MAX(0, debit_cents - MAX(0, total_credits - prior_debits)))
                        FROM allocation
                        WHERE debit_cents > 0 AND due_date IS NOT NULL AND due_date < $today),0);
            """;
        command.Parameters.AddWithValue("$completed", Convert.ToInt32(SaleStatus.Completed));
        command.Parameters.AddWithValue("$cash", Convert.ToInt32(PaymentMethod.Cash));
        command.Parameters.AddWithValue("$start", rangeStart.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$end", rangeEnd.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$today", DateOnly.FromDateTime(todayStart.DateTime).ToString("yyyy-MM-dd"));
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return new OwnerReceivablesSummary(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    private static void AddRangeParameters(SqliteCommand command, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        command.Parameters.AddWithValue("$rangeStart", rangeStart.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$rangeEnd", rangeEnd.ToUniversalTime().ToString("O"));
    }

    private static long ReadLong(SqliteDataReader reader, string columnName)
    {
        object value = reader.GetValue(reader.GetOrdinal(columnName));
        return value is DBNull ? 0 : Convert.ToInt64(value);
    }

    private static string ReadString(SqliteDataReader reader, string columnName)
    {
        return reader.GetString(reader.GetOrdinal(columnName));
    }

    private static string? ReadNullableString(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static decimal FromX1000(long quantityX1000)
    {
        return quantityX1000 / 1000m;
    }
}

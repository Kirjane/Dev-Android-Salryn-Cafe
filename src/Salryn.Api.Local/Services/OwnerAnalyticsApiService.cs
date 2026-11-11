using Microsoft.Data.Sqlite;
using System.Text;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Api.Local.Services;

public sealed class OwnerAnalyticsApiService
{
    public const int ExportRowLimit = 50_000;
    private readonly SalrynLocalComposition _composition;

    public OwnerAnalyticsApiService(SalrynLocalComposition composition) =>
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));

    public Result<OwnerAnalyticsOverview> GetOverview(OwnerAnalyticsRange range)
    {
        try
        {
            using SqliteConnection connection = Open();
            OwnerAnalyticsPeriod current = ReadPeriod(connection, range.StartUtc, range.EndUtc);
            OwnerAnalyticsPeriod prior = ReadPeriod(connection, range.PriorStartUtc, range.PriorEndUtc);
            OwnerMarginEstimate margin = ReadMargin(connection, range.StartUtc, range.EndUtc, current.SalesTotalCents);
            using SqliteCommand low = connection.CreateCommand();
            low.CommandText = "SELECT COUNT(*) FROM products WHERE is_active=1 AND track_stock=1 AND reorder_level_x1000>0 AND stock_on_hand_x1000<=reorder_level_x1000;";
            int lowStock = Convert.ToInt32(low.ExecuteScalar());
            return Result<OwnerAnalyticsOverview>.Success(new(Scope(range), current, prior, margin, lowStock));
        }
        catch (SqliteException ex)
        {
            return Result<OwnerAnalyticsOverview>.Fail(ReadFailure(ex));
        }
    }

    public Result<OwnerSalesTrendResponse> GetSalesTrend(OwnerAnalyticsRange range)
    {
        try
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                WITH cash_by_sale AS (
                    SELECT s.id,
                           MAX(0, COALESCE(SUM(CASE WHEN p.method=$cash THEN p.amount_cents ELSE 0 END),0)-s.change_cents) cash_cents
                    FROM sales s LEFT JOIN payments p ON p.sale_id=s.id
                    WHERE s.sale_date >= $start AND s.sale_date < $end
                    GROUP BY s.id, s.change_cents
                )
                SELECT date(datetime(s.sale_date, '+8 hours')) store_date,
                       COALESCE(SUM(CASE WHEN s.status=$completed THEN s.total_cents ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN s.status=$completed THEN c.cash_cents ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN s.status=$completed THEN s.balance_due_cents ELSE 0 END),0),
                       SUM(CASE WHEN s.status=$completed THEN 1 ELSE 0 END),
                       SUM(CASE WHEN s.status=$voided THEN 1 ELSE 0 END)
                FROM sales s LEFT JOIN cash_by_sale c ON c.id=s.id
                WHERE s.sale_date >= $start AND s.sale_date < $end
                GROUP BY store_date ORDER BY store_date;
                """;
            BindRange(command, range.StartUtc, range.EndUtc);
            command.Parameters.AddWithValue("$cash", (int)PaymentMethod.Cash);
            command.Parameters.AddWithValue("$completed", (int)SaleStatus.Completed);
            command.Parameters.AddWithValue("$voided", (int)SaleStatus.Voided);
            Dictionary<string, OwnerTrendPoint> values = new(StringComparer.Ordinal);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string date = reader.GetString(0);
                values[date] = new(date, reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt32(4), reader.GetInt32(5));
            }
            List<OwnerTrendPoint> points = new();
            for (DateOnly day = range.StartStoreDate; day < range.EndStoreDateExclusive; day = day.AddDays(1))
            {
                string key = day.ToString("yyyy-MM-dd");
                points.Add(values.TryGetValue(key, out OwnerTrendPoint? point) ? point : new(key, 0, 0, 0, 0, 0));
            }
            return Result<OwnerSalesTrendResponse>.Success(new(Scope(range), "store-day", points));
        }
        catch (SqliteException ex)
        {
            return Result<OwnerSalesTrendResponse>.Fail(ReadFailure(ex));
        }
    }

    public Result<OwnerPaymentMixResponse> GetPaymentMix(OwnerAnalyticsRange range)
    {
        try
        {
            using SqliteConnection connection = Open();
            OwnerAnalyticsPeriod period = ReadPeriod(connection, range.StartUtc, range.EndUtc);

            using SqliteCommand digitalCmd = connection.CreateCommand();
            digitalCmd.CommandText = """
                SELECT p.method, COALESCE(SUM(p.amount_cents), 0)
                FROM payments p
                INNER JOIN sales s ON s.id = p.sale_id
                WHERE s.status = $completed AND s.sale_date >= $start AND s.sale_date < $end
                  AND p.method IN ($gcash, $maya, $debit)
                GROUP BY p.method;
                """;
            BindRange(digitalCmd, range.StartUtc, range.EndUtc);
            digitalCmd.Parameters.AddWithValue("$completed", (int)SaleStatus.Completed);
            digitalCmd.Parameters.AddWithValue("$gcash", (int)PaymentMethod.GCash);
            digitalCmd.Parameters.AddWithValue("$maya", (int)PaymentMethod.Maya);
            digitalCmd.Parameters.AddWithValue("$debit", (int)PaymentMethod.Debit);

            long gcashTotal = 0;
            long mayaTotal = 0;
            long debitTotal = 0;

            using (SqliteDataReader reader = digitalCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    int m = reader.GetInt32(0);
                    long val = reader.GetInt64(1);
                    if (m == (int)PaymentMethod.GCash) gcashTotal = val;
                    else if (m == (int)PaymentMethod.Maya) mayaTotal = val;
                    else if (m == (int)PaymentMethod.Debit) debitTotal = val;
                }
            }

            return Result<OwnerPaymentMixResponse>.Success(new(Scope(range), new[]
            {
                new OwnerPaymentMixRow("Cash received on sales (net of change)", period.CashOnSalesCents, "period-flow"),
                new OwnerPaymentMixRow("GCash payments", gcashTotal, "period-flow"),
                new OwnerPaymentMixRow("Maya payments", mayaTotal, "period-flow"),
                new OwnerPaymentMixRow("Debit card payments", debitTotal, "period-flow"),
                new OwnerPaymentMixRow("Utang originated", period.CreditOriginatedCents, "period-flow")
            }));
        }
        catch (SqliteException ex)
        {
            return Result<OwnerPaymentMixResponse>.Fail(ReadFailure(ex));
        }
    }

    public Result<OwnerTopProductsResponse> GetTopProducts(OwnerAnalyticsRange range, int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 50);
        try
        {
            using SqliteConnection connection = Open();
            using SqliteCommand products = connection.CreateCommand();
            products.CommandText = """
                WITH ranked AS (
                    SELECT si.*, s.sale_date,
                           ROW_NUMBER() OVER (PARTITION BY si.product_id ORDER BY s.sale_date DESC, si.id DESC) rn
                    FROM sale_items si INNER JOIN sales s ON s.id=si.sale_id
                    WHERE s.status=$completed AND s.sale_date >= $start AND s.sale_date < $end
                )
                SELECT product_id,
                       MAX(CASE WHEN rn=1 THEN product_name_snapshot END),
                       MAX(CASE WHEN rn=1 THEN barcode_snapshot END),
                       SUM(qty_x1000), SUM(line_total_cents),
                       CASE WHEN SUM(CASE WHEN cost_price_cents IS NULL THEN 1 ELSE 0 END)=0
                            THEN SUM((qty_x1000*cost_price_cents+500)/1000) ELSE NULL END,
                       SUM(CASE WHEN cost_price_cents IS NULL THEN 1 ELSE 0 END)=0
                FROM ranked GROUP BY product_id
                ORDER BY SUM(line_total_cents) DESC, product_id LIMIT $limit;
                """;
            BindRange(products, range.StartUtc, range.EndUtc);
            products.Parameters.AddWithValue("$completed", (int)SaleStatus.Completed);
            products.Parameters.AddWithValue("$limit", limit);
            List<OwnerAnalyticsProductRow> productRows = new();
            using (SqliteDataReader reader = products.ExecuteReader())
            {
                while (reader.Read())
                    productRows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetInt64(3), reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.GetBoolean(6)));
            }

            using SqliteCommand categories = connection.CreateCommand();
            categories.CommandText = """
                SELECT COALESCE(c.name,'Uncategorized'), SUM(si.qty_x1000), SUM(si.line_total_cents)
                FROM sale_items si INNER JOIN sales s ON s.id=si.sale_id
                LEFT JOIN products p ON p.id=si.product_id LEFT JOIN categories c ON c.id=p.category_id
                WHERE s.status=$completed AND s.sale_date >= $start AND s.sale_date < $end
                GROUP BY COALESCE(c.name,'Uncategorized') ORDER BY SUM(si.line_total_cents) DESC LIMIT $limit;
                """;
            BindRange(categories, range.StartUtc, range.EndUtc);
            categories.Parameters.AddWithValue("$completed", (int)SaleStatus.Completed);
            categories.Parameters.AddWithValue("$limit", limit);
            List<OwnerAnalyticsCategoryRow> categoryRows = new();
            using (SqliteDataReader reader = categories.ExecuteReader())
                while (reader.Read()) categoryRows.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
            return Result<OwnerTopProductsResponse>.Success(new(
                Scope(range), productRows, categoryRows,
                "Product identity is grouped by stable product ID using the latest historical sale label. Category is the product's current classification. Line revenue excludes sale-level discount allocation and additive tax, so product/category rows are ranking bases and are not additive to completed sale totals."));
        }
        catch (SqliteException ex)
        {
            return Result<OwnerTopProductsResponse>.Fail(ReadFailure(ex));
        }
    }

    public Result<OwnerArAgingResponse> GetArAging(OwnerAnalyticsRange range)
    {
        try
        {
            using SqliteConnection connection = Open();
            DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(range.AsOfUtc, OwnerAnalyticsRange.StoreTimeZone).DateTime);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                WITH allocation AS (
                    SELECT customer_id,due_date,entry_type,
                           CASE WHEN entry_type<>'CollectionReversal' THEN debit_cents ELSE 0 END exposure_debit,
                           COALESCE(SUM(CASE WHEN entry_type<>'CollectionReversal' THEN debit_cents ELSE 0 END) OVER
                             (PARTITION BY customer_id ORDER BY created_at,id ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),0) prior_exposure_debits,
                           SUM(CASE WHEN entry_type='CollectionReversal' THEN -debit_cents ELSE credit_cents END)
                             OVER (PARTITION BY customer_id) net_credits
                    FROM accounts_receivable_entries
                ), exposure AS (
                    SELECT due_date, MAX(0,exposure_debit-MAX(0,net_credits-prior_exposure_debits)) remaining
                    FROM allocation WHERE exposure_debit>0
                )
                SELECT COALESCE(SUM(remaining),0),
                       COALESCE(SUM(CASE WHEN due_date < $today THEN remaining ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN due_date = $today THEN remaining ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN due_date > $today AND due_date <= $soon THEN remaining ELSE 0 END),0),
                       COALESCE(SUM(CASE WHEN due_date > $soon OR due_date IS NULL THEN remaining ELSE 0 END),0)
                FROM exposure;
                """;
            command.Parameters.AddWithValue("$today", today.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$soon", today.AddDays(7).ToString("yyyy-MM-dd"));
            long total, overdue, dueToday, dueSoon, later;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                reader.Read(); total=reader.GetInt64(0); overdue=reader.GetInt64(1); dueToday=reader.GetInt64(2); dueSoon=reader.GetInt64(3); later=reader.GetInt64(4);
            }
            using SqliteCommand collections = connection.CreateCommand();
            collections.CommandText = "SELECT method,COALESCE(SUM(amount_cents),0) FROM accounts_receivable_collections WHERE voided_at IS NULL AND collected_at >= $start AND collected_at < $end GROUP BY method ORDER BY method;";
            BindRange(collections, range.StartUtc, range.EndUtc);
            List<OwnerPaymentMixRow> methods = new(); long collected = 0;
            using (SqliteDataReader reader = collections.ExecuteReader())
                while (reader.Read()) { long amount=reader.GetInt64(1); collected += amount; methods.Add(new(reader.GetString(0), amount, "period-flow")); }
            return Result<OwnerArAgingResponse>.Success(new(Scope(range), total, collected, methods, new[]
            {
                new OwnerArAgingBucket("overdue","Overdue",overdue), new OwnerArAgingBucket("today","Due today",dueToday),
                new OwnerArAgingBucket("soon","Due in next 7 days",dueSoon), new OwnerArAgingBucket("later","Later / no due date",later)
            }, "Account-level FIFO aging estimate; this is not invoice-level allocation. Outstanding is an as-of stock. Collections are a separate period flow and are never added to sales."));
        }
        catch (SqliteException ex)
        {
            return Result<OwnerArAgingResponse>.Fail(ReadFailure(ex));
        }
    }

    public Result<byte[]> ExportSalesCsv(OwnerAnalyticsRange range)
    {
        try
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT receipt_no,sale_date,status,total_cents,balance_due_cents
                FROM sales WHERE sale_date >= $start AND sale_date < $end
                ORDER BY sale_date,id LIMIT $limit;
                """;
            BindRange(command,range.StartUtc,range.EndUtc); command.Parameters.AddWithValue("$limit",ExportRowLimit+1);
            StringBuilder csv=new("\uFEFFReceipt,Sale UTC,Status,Sale total cents,Utang originated cents\r\n"); int count=0;
            using SqliteDataReader reader=command.ExecuteReader();
            while(reader.Read())
            {
                if(++count>ExportRowLimit) return Result<byte[]>.Fail(Error.Validation("OwnerAnalytics.ExportTooLarge",$"Export exceeds {ExportRowLimit} rows; choose a smaller date range."));
                csv.Append(Csv(reader.GetString(0))).Append(',').Append(Csv(reader.GetString(1))).Append(',')
                   .Append(reader.GetInt32(2)).Append(',').Append(reader.GetInt64(3)).Append(',').Append(reader.GetInt64(4)).Append("\r\n");
            }
            return Result<byte[]>.Success(Encoding.UTF8.GetBytes(csv.ToString()));
        }
        catch(SqliteException ex){return Result<byte[]>.Fail(ReadFailure(ex));}
    }

    public Result<byte[]> ExportArCsv(OwnerAnalyticsRange range)
    {
        try
        {
            using SqliteConnection connection=Open(); using SqliteCommand command=connection.CreateCommand();
            command.CommandText="""
                SELECT c.full_name,c.phone,e.entry_type,e.debit_cents,e.credit_cents,e.due_date,e.created_at,e.note
                FROM accounts_receivable_entries e INNER JOIN customers c ON c.id=e.customer_id
                WHERE e.created_at >= $start AND e.created_at < $end
                ORDER BY e.created_at,e.id LIMIT $limit;
                """;
            BindRange(command,range.StartUtc,range.EndUtc);command.Parameters.AddWithValue("$limit",ExportRowLimit+1);
            StringBuilder csv=new("\uFEFFCustomer,Phone,Entry,Debit cents,Credit cents,Due date,Created UTC,Note\r\n");int count=0;
            using SqliteDataReader reader=command.ExecuteReader(); while(reader.Read())
            {
                if(++count>ExportRowLimit)return Result<byte[]>.Fail(Error.Validation("OwnerAnalytics.ExportTooLarge",$"Export exceeds {ExportRowLimit} rows; choose a smaller date range."));
                for(int i=0;i<8;i++){if(i>0)csv.Append(',');csv.Append(Csv(reader.IsDBNull(i)?string.Empty:Convert.ToString(reader.GetValue(i))??string.Empty));}csv.Append("\r\n");
            }
            return Result<byte[]>.Success(Encoding.UTF8.GetBytes(csv.ToString()));
        }
        catch(SqliteException ex){return Result<byte[]>.Fail(ReadFailure(ex));}
    }

    private SqliteConnection Open() { SqliteConnection connection=_composition.ConnectionFactory.CreateConnection(); connection.Open(); return connection; }

    private static OwnerAnalyticsPeriod ReadPeriod(SqliteConnection connection, DateTimeOffset start, DateTimeOffset end)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH cash_by_sale AS (
              SELECT s.id,MAX(0,COALESCE(SUM(CASE WHEN p.method=$cash THEN p.amount_cents ELSE 0 END),0)-s.change_cents) cash_cents
              FROM sales s LEFT JOIN payments p ON p.sale_id=s.id
              WHERE s.sale_date >= $start AND s.sale_date < $end GROUP BY s.id,s.change_cents)
            SELECT COALESCE(SUM(CASE WHEN s.status=$completed THEN 1 ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN s.status=$completed THEN s.total_cents ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN s.status=$completed THEN c.cash_cents ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN s.status=$completed THEN s.balance_due_cents ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN s.status=$voided THEN 1 ELSE 0 END),0)
            FROM sales s LEFT JOIN cash_by_sale c ON c.id=s.id
            WHERE s.sale_date >= $start AND s.sale_date < $end;
            """;
        BindRange(command,start,end); command.Parameters.AddWithValue("$cash",(int)PaymentMethod.Cash);
        command.Parameters.AddWithValue("$completed",(int)SaleStatus.Completed); command.Parameters.AddWithValue("$voided",(int)SaleStatus.Voided);
        using SqliteDataReader reader=command.ExecuteReader(); reader.Read();
        return new(reader.GetInt32(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetInt32(4));
    }

    private static OwnerMarginEstimate ReadMargin(SqliteConnection connection, DateTimeOffset start, DateTimeOffset end, long sales)
    {
        using SqliteCommand command=connection.CreateCommand();
        command.CommandText="""
            SELECT COUNT(*),COALESCE(SUM(CASE WHEN si.cost_price_cents IS NULL THEN 1 ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN si.cost_price_cents IS NOT NULL THEN (si.qty_x1000*si.cost_price_cents+500)/1000 ELSE 0 END),0)
            FROM sale_items si INNER JOIN sales s ON s.id=si.sale_id
            WHERE s.status=$completed AND s.sale_date >= $start AND s.sale_date < $end;
            """;
        BindRange(command,start,end); command.Parameters.AddWithValue("$completed",(int)SaleStatus.Completed);
        using SqliteDataReader reader=command.ExecuteReader(); reader.Read(); int count=reader.GetInt32(0); int missing=reader.GetInt32(1); long cogs=reader.GetInt64(2);
        bool available=count>0 && missing==0;
        return new(available,available?cogs:null,available?sales-cogs:null,count-missing,missing,
            available ? "Estimate: completed sale total after discounts and additive tax minus historical sale-item cost snapshots. Not net profit."
                      : "Unavailable until every completed sale item in the period has a historical cost snapshot. No current catalog-cost backfill is used.");
    }

    private static OwnerAnalyticsScope Scope(OwnerAnalyticsRange r) => new(r.AsOfUtc,OwnerAnalyticsRange.TimeZoneId,r.StartUtc,r.EndUtc,r.PriorStartUtc,r.PriorEndUtc,
        "Store-day dates in Asia/Manila; inclusive start and exclusive end.","All money fields are integer Philippine-peso cents.",
        "Completed sale totals are authoritative. Product/category line revenue is a disclosed non-additive ranking basis when order-level discount or additive tax exists.");
    private static void BindRange(SqliteCommand c,DateTimeOffset start,DateTimeOffset end){c.Parameters.AddWithValue("$start",start.ToString("O"));c.Parameters.AddWithValue("$end",end.ToString("O"));}
    private static Error ReadFailure(SqliteException ex)=>Error.Unexpected("OwnerAnalytics.ReadFailed",ex.Message);
    private static string Csv(string value)
    {
        string safe=value;
        if(safe.Length>0 && safe[0] is '=' or '+' or '-' or '@') safe="'"+safe;
        return "\""+safe.Replace("\"","\"\"")+"\"";
    }
}

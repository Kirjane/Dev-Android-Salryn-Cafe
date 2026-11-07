using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class OwnerAnalyticsW0W3RuntimeChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-owner-analytics-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            Require(composition.DatabaseInitialization.InitializeDatabase().IsSuccess, "Disposable analytics database initialization must succeed.");
            Seed(composition);
            OwnerAnalyticsApiService service = new(composition);
            var range = OwnerAnalyticsRange.Create(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
            Require(range.IsSuccess, "Manila range must bind.");
            Require(range.Value.StartUtc == new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero), "Manila midnight must map to prior UTC day 16:00.");
            Require(range.Value.EndUtc == new DateTimeOffset(2026, 11, 2, 16, 0, 0, TimeSpan.Zero), "Inclusive end date must become an exclusive next-midnight boundary.");
            Require(range.Value.PriorStartUtc == new DateTimeOffset(2026, 10, 29, 16, 0, 0, TimeSpan.Zero), "Comparison must be an equal preceding period.");
            Require(OwnerAnalyticsRange.Create(new DateOnly(2025, 1, 1), new DateOnly(2026, 11, 1)).IsFailure, "Ranges over 366 store days must be rejected.");

            long before = Count(composition, "sales");
            var overview = service.GetOverview(range.Value);
            Require(overview.IsSuccess, "Overview must execute.");
            Require(overview.Value.Current.CompletedSaleCount == 2 && overview.Value.Current.VoidedSaleCount == 1, "23:59/00:01 Manila sales and void count must reconcile.");
            Require(overview.Value.Current.SalesTotalCents == 30_000, "Completed sales total must exclude voids.");
            Require(overview.Value.Current.CashOnSalesCents == 8_000, "Cash on sales must be payment tender less change.");
            Require(overview.Value.Current.CreditOriginatedCents == 20_000, "Utang originated must remain period flow.");
            Require(overview.Value.Margin.IsAvailable && overview.Value.Margin.EstimatedCogsCents == 11_000 && overview.Value.Margin.EstimatedGrossMarginCents == 19_000, "Margin must use historical item cost snapshots.");
            var trend = service.GetSalesTrend(range.Value);
            Require(trend.IsSuccess && trend.Value.Points.Count == 2 && trend.Value.Points.Sum(x => x.SalesTotalCents) == overview.Value.Current.SalesTotalCents, "Daily trend must fill days and reconcile to overview.");
            Require(service.GetPaymentMix(range.Value).IsSuccess && service.GetTopProducts(range.Value, 10).IsSuccess, "Payment/product analytics must execute.");
            Require(Count(composition, "sales") == before, "Analytics reads must not mutate business tables.");
            byte[] csv = service.ExportSalesCsv(range.Value).Value;
            Require(csv.Length >= 3 && csv[0] == 0xEF && csv[1] == 0xBB && csv[2] == 0xBF, "CSV must carry a UTF-8 BOM.");
            Require(System.Text.Encoding.UTF8.GetString(csv).Contains("'=Historical Product",StringComparison.Ordinal),"CSV must neutralize spreadsheet-formula prefixes.");
            string endpoints=File.ReadAllText(Path.Combine(RepoRoot(),"src","Salryn.Api.Local","LocalOwnerAnalyticsEndpoints.cs"));
            Require(endpoints.Contains("RequireLoopbackOperatorRequest",StringComparison.Ordinal) && endpoints.Contains("LocalApiPermissions.ReportsView",StringComparison.Ordinal) && endpoints.Contains("LicenseFeatures.Reports",StringComparison.Ordinal),"Every analytics route must retain loopback, Reports.View, and signed Reports guards.");
            Require(endpoints.Contains("LocalApiPermissions.ReceivablesView",StringComparison.Ordinal) && endpoints.Contains("LicenseFeatures.ReceivablesUtangLedger",StringComparison.Ordinal),"AR analytics/export must add U1 permission and signed entitlement guards.");

            SeedScale(composition, 100_000);
            var scaleRange = OwnerAnalyticsRange.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30)).Value;
            Stopwatch timer = Stopwatch.StartNew();
            var scale = service.GetOverview(scaleRange);
            timer.Stop();
            Require(scale.IsSuccess && scale.Value.Current.CompletedSaleCount == 100_002, "100k-scale bounded query must return exact counts.");
            string plan = QueryPlan(composition, "SELECT COUNT(*) FROM sales WHERE sale_date >= '2026-09-30T16:00:00Z' AND sale_date < '2026-11-30T16:00:00Z'");
            Require(plan.Contains("idx_sales_sale_date", StringComparison.OrdinalIgnoreCase), "Scale query plan must use the sale-date index.");
            Guid pageCustomer=SeedPagination(composition,205,501);
            SqliteAccountsReceivableService arService=new(composition.ConnectionFactory);
            var customerPage=arService.SearchCustomersPageAsync(null,200,50).GetAwaiter().GetResult();
            Require(customerPage.IsSuccess && customerPage.Value.Total==205 && customerPage.Value.Items.Count==5 && !customerPage.Value.HasMore,"205-customer final page must be stable and complete.");
            var ledgerPage=arService.GetLedgerPageAsync(pageCustomer,500,100).GetAwaiter().GetResult();
            Require(ledgerPage.IsSuccess && ledgerPage.Value.Total==501 && ledgerPage.Value.Items.Count==1 && !ledgerPage.Value.HasMore,"501-entry final ledger page must be stable and complete.");
            Console.WriteLine($"Owner analytics W0-W3 PASS: Manila boundaries, cash/change/Utang/void semantics, historical margin, reconciliation, read-only proof, BOM CSV, 100k scale in {timer.ElapsedMilliseconds} ms; plan={plan}");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Seed(SalrynLocalComposition c)
    {
        using var x = c.ConnectionFactory.CreateConnection(); x.Open(); using var tx = x.BeginTransaction();
        string role = Scalar(x, tx, "SELECT id FROM roles LIMIT 1"); string unit = Scalar(x, tx, "SELECT id FROM units LIMIT 1");
        Exec(x, tx, "INSERT INTO users(id,username,display_name,password_hash,role_id,status,created_at) VALUES('00000000-0000-0000-0000-000000000099','analytics','Analytics','x',$role,1,'2026-01-01T00:00:00Z')", ("$role",role));
        Exec(x, tx, "INSERT INTO products(id,name,unit_id,cost_price_cents,selling_price_cents,stock_on_hand_x1000,reorder_level_x1000,track_stock,is_active,created_at) VALUES('00000000-0000-0000-0000-000000000098','=Historical Product',$unit,999999,15000,10000,1000,1,1,'2026-01-01T00:00:00Z')", ("$unit",unit));
        Sale(x,tx,"1","2026-11-01T15:59:00Z",10_000,10_000,2_000,2,4_000); // 23:59 Manila; ₱80 net cash
        Sale(x,tx,"2","2026-11-01T16:01:00Z",20_000,0,0,2,7_000,20_000); // 00:01 Manila; Utang
        Sale(x,tx,"3","2026-11-02T00:00:00Z",50_000,50_000,0,3,1_000); // void
        tx.Commit();
    }
    private static void Sale(SqliteConnection x, SqliteTransaction tx, string suffix, string at, long total, long paid, long change, int status, long cost, long due=0)
    {
        string id=$"10000000-0000-0000-0000-{suffix.PadLeft(12,'0')}";
        Exec(x,tx,"INSERT INTO sales(id,receipt_no,sale_date,cashier_user_id,subtotal_cents,discount_cents,tax_cents,total_cents,amount_paid_cents,change_cents,status,created_at,balance_due_cents) VALUES($id,$receipt,$at,'00000000-0000-0000-0000-000000000099',$total,0,0,$total,$paid,$change,$status,$at,$due)",( "$id",id),("$receipt","OA-"+suffix),("$at",at),("$total",total),("$paid",paid),("$change",change),("$status",status),("$due",due));
        Exec(x,tx,"INSERT INTO sale_items(id,sale_id,product_id,product_name_snapshot,unit_name_snapshot,qty_x1000,unit_price_cents,cost_price_cents,line_total_cents) VALUES($item,$id,'00000000-0000-0000-0000-000000000098','=Historical Product','pc',1000,$total,$cost,$total)",( "$item",Guid.NewGuid().ToString("D")),("$id",id),("$total",total),("$cost",cost));
        if(paid>0) Exec(x,tx,"INSERT INTO payments(id,sale_id,method,amount_cents,paid_at) VALUES($p,$id,1,$paid,$at)",( "$p",Guid.NewGuid().ToString("D")),("$id",id),("$paid",paid),("$at",at));
    }
    private static void SeedScale(SalrynLocalComposition c, int count) { using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText="""WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<$n) INSERT INTO sales(id,receipt_no,sale_date,cashier_user_id,subtotal_cents,total_cents,amount_paid_cents,change_cents,status,created_at,balance_due_cents) SELECT printf('20000000-0000-0000-%04x-%012x',i/65536,i), 'SCALE-'||i, '2026-11-10T00:00:00Z','00000000-0000-0000-0000-000000000099',100,100,0,0,2,'2026-11-10T00:00:00Z',0 FROM n;""";q.Parameters.AddWithValue("$n",count);q.ExecuteNonQuery(); }
    private static Guid SeedPagination(SalrynLocalComposition c,int customers,int entries){Guid target=Guid.Parse("30000000-0000-0000-0000-000000000001");using var x=c.ConnectionFactory.CreateConnection();x.Open();using var tx=x.BeginTransaction();for(int i=1;i<=customers;i++){Guid id=i==1?target:Guid.NewGuid();Exec(x,tx,"INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,$name,1,'00000000-0000-0000-0000-000000000099','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')",("$id",id.ToString("D")),("$name",$"Customer {i:D3}"));}for(int i=1;i<=entries;i++)Exec(x,tx,"INSERT INTO accounts_receivable_entries(id,customer_id,entry_type,debit_cents,credit_cents,balance_after_cents,created_by_user_id,created_at) VALUES($id,$customer,'AdjustmentDebit',1,0,$balance,'00000000-0000-0000-0000-000000000099',$at)",( "$id",Guid.NewGuid().ToString("D")),("$customer",target.ToString("D")),("$balance",i),("$at",$"2026-01-01T00:{i%60:D2}:{i%60:D2}.{i:D7}Z"));tx.Commit();return target;}
    private static long Count(SalrynLocalComposition c,string table){using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt64(q.ExecuteScalar());}
    private static string QueryPlan(SalrynLocalComposition c,string sql){using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText="EXPLAIN QUERY PLAN "+sql;using var r=q.ExecuteReader();var lines=new List<string>();while(r.Read())lines.Add(r.GetString(3));return string.Join(" | ",lines);}
    private static string Scalar(SqliteConnection x,SqliteTransaction tx,string sql){using var q=x.CreateCommand();q.Transaction=tx;q.CommandText=sql;return Convert.ToString(q.ExecuteScalar())!;}
    private static void Exec(SqliteConnection x,SqliteTransaction tx,string sql,params (string,object)[] p){using var q=x.CreateCommand();q.Transaction=tx;q.CommandText=sql;foreach(var v in p)q.Parameters.AddWithValue(v.Item1,v.Item2);q.ExecuteNonQuery();}
    private static string RepoRoot(){string? p=Directory.GetCurrentDirectory();while(p is not null&&!File.Exists(Path.Combine(p,"Salryn.sln")))p=Directory.GetParent(p)?.FullName;return p??throw new InvalidOperationException("Repository root not found.");}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}

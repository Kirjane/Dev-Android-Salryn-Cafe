using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosT1FullStoreRuntimeTortureChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-pos-t1-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            _ = Value(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "T1 Store", "T1 Trading", "Original Address", "09170000000", null, 80,
                "admin", "T1 Admin", "StrongAdmin123!")), "bootstrap");

            InMemoryLocalApiSessionStore sessionStore = new();
            LocalApiSessionService sessionService = new(composition.Authentication, sessionStore);
            LocalApiSession admin = Value(sessionService.LoginAsync(new LoginRequest("admin", "StrongAdmin123!"))
                .GetAwaiter().GetResult(), "login");
            Require(admin.Permissions.Contains(SalrynPermissions.SalesCreate), "Admin must be allowed to sell.");
            Require(admin.Permissions.Contains(SalrynPermissions.SalesVoid), "Admin must be allowed to void.");
            Require(RoleHas(composition, DefaultSeedData.CashierRoleId, SalrynPermissions.SalesCreate), "Cashier must be allowed to sell.");
            Require(!RoleHas(composition, DefaultSeedData.CashierRoleId, SalrynPermissions.ReportsView), "Cashier must not access owner reports.");
            Require(!RoleHas(composition, DefaultSeedData.CashierRoleId, SalrynPermissions.SettingsManage), "Cashier must not manage settings.");

            var categories = Value(composition.CatalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(), "categories");
            var brands = Value(composition.CatalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(), "brands");
            var units = Value(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            Product staple = Product("T1 Rice", "T1-RICE", "9981000000001", categories[0].Id, brands[0].Id, units[0].Id, 1000, 1500, 10000, 3000);
            RequireSuccess(composition.ProductWriteRepository.AddAsync(staple).GetAwaiter().GetResult(), "seed product");

            HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
            Require(host.ShiftService.OpenShiftAsync(admin.UserId, 100000).GetAwaiter().GetResult().Ok, "Shift must open.");

            long stockBeforeCart = Scalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", staple.Id);
            HostOperatorCheckoutResponse sale1 = Checkout(host, admin.UserId, staple.Id, 2000, 3000);
            Require(stockBeforeCart == 10000, "Preparing repeated scans must not change stock before checkout.");
            Require(Scalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", staple.Id) == 8000, "Two scanned units must reduce stock once at commit.");
            Guid sale1Id = Guid.Parse(sale1.SaleId!);
            string sale1Snapshot = Snapshot(composition, sale1Id);

            FastAddProductApiService fastAdd = new(composition);
            FastAddProductResponse added = Value(fastAdd.FastAddAsync(new FastAddProductRequest(
                "T1 Emergency Item", "9981000000002", units[0].Id, 25m, 5m), admin).GetAwaiter().GetResult(), "fast add");
            Result<FastAddProductResponse> duplicate = fastAdd.FastAddAsync(new FastAddProductRequest(
                "T1 Duplicate", "9981000000002", units[0].Id, 25m, 1m), admin).GetAwaiter().GetResult();
            Require(duplicate.IsFailure, "Duplicate barcode must fail.");
            Guid fastProductId = added.Product.Id;
            InventoryManagementApiService inventory = new(composition);
            _ = Value(inventory.StockInAsync(new StockInRequest(staple.Id, 3m, "T1 replenishment", "T1-IN"), admin).GetAwaiter().GetResult(), "stock in");
            _ = Value(inventory.AdjustAsync(new StockAdjustmentRequest(staple.Id, "remove", 1m, "T1 count correction", "T1-ADJ"), admin).GetAwaiter().GetResult(), "adjustment");
            Require(Scalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", staple.Id) == 10000, "Stock-In and adjustment math must reconcile.");

            HostOperatorCheckoutResponse sale2 = Checkout(host, admin.UserId, fastProductId, 2000, 5000);
            Require(Scalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", fastProductId) == 3000, "Fast Add product sale must reduce initial stock.");
            Guid sale2Id = Guid.Parse(sale2.SaleId!);
            string sale2Snapshot = Snapshot(composition, sale2Id);

            long beforePrintSales = Count(composition, "sales"), beforePrintPayments = Count(composition, "payments"), beforePrintMoves = Count(composition, "stock_movements");
            PosReceiptPrintResult print = host.ReceiptPrintService.PrintSaleReceiptAsync(sale2Id).GetAwaiter().GetResult();
            Require(print.Status == PosReceiptPrintStatuses.DisabledUnavailable, "Default printer must fail safely as disabled/unavailable.");
            Require(Count(composition, "sales") == beforePrintSales && Count(composition, "payments") == beforePrintPayments && Count(composition, "stock_movements") == beforePrintMoves, "Reprint must be side-effect free.");
            Require(Snapshot(composition, sale2Id) == sale2Snapshot, "Reprint must not rewrite the receipt snapshot.");

            SaleVoidApiService voids = new(composition);
            _ = Value(voids.VoidAsync(sale1Id, new SaleVoidRequest("T1 customer cancellation"), admin).GetAwaiter().GetResult(), "void");
            Require(Scalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", staple.Id) == 12000, "Void must restore sold stock through reversal.");
            Require(Snapshot(composition, sale1Id) == sale1Snapshot, "Void must preserve original receipt.");
            Require(voids.VoidAsync(sale1Id, new SaleVoidRequest("duplicate"), admin).GetAwaiter().GetResult().IsFailure, "Double void must fail.");

            SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
            IStoreSettingsReadRepository settingsReader = repositories.CreateStoreSettingsReadRepository();
            IStoreSettingsWriteRepository settingsWriter = repositories.CreateStoreSettingsWriteRepository();
            var settings = Value(settingsReader.GetCurrentAsync().GetAwaiter().GetResult(), "settings")!;
            RequireSuccess(settingsWriter.UpdateAsync(new StoreSettingsUpdateRequest(settings.Id, "T1 Updated Store", "T1 Trading", "Updated Address", "09170000000", settings.Tin, settings.CurrencyLabel, settings.ReceiptWidthMm, settings.TaxModeEnabled, settings.ShiftModeEnabled, "UPDATED HEADER", "UPDATED FOOTER", "Disabled")).GetAwaiter().GetResult(), "settings update");
            HostOperatorCheckoutResponse sale3 = Checkout(host, admin.UserId, staple.Id, 1000, 1500);
            Guid sale3Id = Guid.Parse(sale3.SaleId!);
            Require(Snapshot(composition, sale3Id).Contains("UPDATED HEADER", StringComparison.Ordinal), "New receipt must consume current settings.");
            Require(Snapshot(composition, sale1Id) == sale1Snapshot && Snapshot(composition, sale2Id) == sale2Snapshot, "Historical receipts must remain immutable after settings changes.");

            OwnerReportsResponse report = Value(new OwnerReportsApiService(composition).GetSummaryAsync(DateTimeOffset.UtcNow).GetAwaiter().GetResult(), "reports");
            Require(report.Daily.SalesCount == 2 && report.Daily.VoidedCount == 1, "Owner totals must separate two active sales from one void.");
            Require(report.Daily.SalesAmountCents == 6500 && report.Daily.VoidedAmountCents == 3000, "Owner amounts must exclude the void from active totals.");

            long finalSales = Count(composition, "sales"), finalPayments = Count(composition, "payments"), finalMoves = Count(composition, "stock_movements");
            Require(finalSales == 3 && finalPayments == 3, "Each completed checkout must create exactly one sale and payment.");
            Require(finalMoves == 7, "Expected ledger: Fast Add, Stock-In, Adjustment, three sales, and one void reversal.");
            ControlledBackupPackageService backup = new(composition.DbPaths);
            var package = Value(backup.Create(), "backup");
            Require(backup.Verify(package.BackupId).IsSuccess, "Activity backup manifest and hashes must verify.");
            Require(backup.List().IsSuccess, "Controlled backup list must succeed.");
            Require(Count(composition, "sales") == finalSales && Count(composition, "payments") == finalPayments && Count(composition, "stock_movements") == finalMoves, "Backup create/verify/list must not mutate store ledgers.");
            string databaseCopy = Path.Combine(composition.DbPaths.BackupPath, package.BackupId, "salryn.db");
            File.AppendAllText(databaseCopy, "tamper");
            Require(backup.Verify(package.BackupId).IsFailure, "Tampered backup must fail SHA-256 validation.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static HostOperatorCheckoutResponse Checkout(HostOperatorApiServices host, Guid userId, Guid productId, long quantityX1000, long cashCents)
    {
        HostOperatorCheckoutOperationResult result = host.CheckoutService.SubmitAsync(
            userId,
            "Torture Test Cashier",
            new HostOperatorCheckoutRequest(
                new[] { new HostOperatorCheckoutLineRequest(productId, quantityX1000) },
                new[] { new HostOperatorCheckoutPaymentRequest("Cash", cashCents) })).GetAwaiter().GetResult();
        Require(result.Ok && result.Checkout?.Status == HostOperatorCheckoutStatus.Completed, "Checkout must complete.");
        Require(result.Checkout!.ReceiptSnapshotSaved, "Checkout must persist a receipt snapshot.");
        return result.Checkout;
    }

    private static Product Product(string name, string sku, string barcode, Guid category, Guid brand, Guid unit, long cost, long price, long stock, long reorder) => new()
    {
        Id = Guid.NewGuid(), Name = name, Sku = sku, Barcode = Value(Barcode.Create(barcode), "barcode"), CategoryId = category,
        BrandId = brand, UnitId = unit, CostPrice = Money.FromCents(cost), SellingPrice = Money.FromCents(price),
        StockOnHand = Value(Quantity.FromX1000(stock), "stock"), ReorderLevel = Value(Quantity.FromX1000(reorder), "reorder"), TrackStock = true, IsActive = true
    };

    private static string Snapshot(SalrynLocalComposition c, Guid id) => Value(c.SaleReadRepository.FindSaleByIdAsync(id).GetAwaiter().GetResult(), "sale")?.ReceiptSnapshot
        ?? throw new InvalidOperationException("Receipt snapshot missing.");
    private static long Count(SalrynLocalComposition c, string table) { using var x = c.ConnectionFactory.CreateConnection(); x.Open(); using var q = x.CreateCommand(); q.CommandText = $"SELECT COUNT(*) FROM {table};"; return Convert.ToInt64(q.ExecuteScalar()); }
    private static long Scalar(SalrynLocalComposition c, string sql, Guid id) { using var x = c.ConnectionFactory.CreateConnection(); x.Open(); using var q = x.CreateCommand(); q.CommandText = sql; q.Parameters.AddWithValue("$id", id.ToString("D")); return Convert.ToInt64(q.ExecuteScalar()); }
    private static bool RoleHas(SalrynLocalComposition c, string role, string permission) { using var x = c.ConnectionFactory.CreateConnection(); x.Open(); using var q = x.CreateCommand(); q.CommandText = "SELECT COUNT(*) FROM role_permissions WHERE role_id=$r AND permission=$p;"; q.Parameters.AddWithValue("$r", role); q.Parameters.AddWithValue("$p", permission); return Convert.ToInt64(q.ExecuteScalar()) == 1; }
    private static T Value<T>(Result<T> result, string area) { if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.Error}"); return result.Value; }
    private static void RequireSuccess(Result result, string area) { if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.Error}"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

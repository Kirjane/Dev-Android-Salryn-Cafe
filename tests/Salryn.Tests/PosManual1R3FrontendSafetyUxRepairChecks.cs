using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosManual1R3FrontendSafetyUxRepairChecks
{
    public static void Run()
    {
        RunFrontendSourceGuards();
        RunReprintNoDuplicateBehaviorProof();
    }

    private static void RunFrontendSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string web = Path.Combine(root.FullName, "src", "Salryn.App.Web", "src");
        string app = Read(web, "App.tsx");
        string appShell = Read(web, "components", "AppShell.tsx");
        string navigation = Read(web, "navigation.ts");
        string checkout = Read(web, "pages", "CheckoutPage.tsx");
        string sales = Read(web, "pages", "SalesHistoryPage.tsx");
        string products = Read(web, "pages", "ProductsPage.tsx");
        string inventory = Read(web, "pages", "InventoryPage.tsx");
        string dashboard = Read(web, "pages", "DashboardPage.tsx");
        string reports = Read(web, "pages", "OwnerReportsPage.tsx");
        string settings = Read(web, "pages", "StoreSettingsPage.tsx");
        string users = Read(web, "pages", "UsersPage.tsx");
        string coreSmoke = Read(Path.Combine(root.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        RequireContains(coreSmoke, "PosManual1R3FrontendSafetyUxRepairChecks.Run();", "CoreSmoke must run MANUAL1-R3 frontend safety UX checks.");
        RequireContains(coreSmoke, "pos-manual1-r3-frontend-safety-ux-repair", "CoreSmoke must expose targeted MANUAL1-R3 gate.");

        RequireContains(checkout, "Saved under Sales & Receipts", "Checkout completion must keep receipt location discoverable.");
        RequireNotContains(checkout, "View Sales & Receipts", "Checkout completion must not offer route-away button that resets completion state.");
        RequireNotContains(app, "onOpenSalesHistory", "Checkout route-away prop must not be wired from App.");
        RequireContains(checkout, "Print Receipt", "Checkout completion must keep print attempt separate.");
        RequireContains(checkout, "New Sale (F5)", "Checkout completion must keep explicit new-sale action.");

        RequireContains(sales, "Receipt Snapshot Preview", "Sales & Receipts must show receipt-form snapshot preview.");
        RequireContains(sales, "max-h-80 overflow-y-auto", "Receipt snapshot preview must be bounded and readable.");
        RequireContains(sales, "saved snapshot only", "Reprint copy must explain persisted source.");
        RequireContains(sales, "does not create another sale, payment, or stock movement", "Reprint copy must explain no-duplicate boundary.");

        RequireContains(products, "Confirm product deactivation", "Product deactivate must require confirmation popup.");
        RequireContains(products, "hidden from the active sale flow", "Deactivate copy must explain inactive sale-flow effect.");
        RequireContains(products, "Historical sales remain preserved", "Deactivate copy must explain sales history preservation.");
        RequireContains(products, "does not delete past records", "Deactivate copy must explain no deletion.");
        RequireContains(products, "Product barcode fields accept manual barcode entry", "Product form must explain manual barcode entry.");

        RequireContains(inventory, "Fast Add currently starts from the cashier workstation", "Inventory must explain Fast Add location.");
        RequireContains(inventory, "keyboard-wedge barcode input", "Inventory must explain cashier scanner support.");
        RequireContains(inventory, "Inventory Fast Add or barcode-driven receiving", "Inventory must mark scanner receiving deferred.");
        RequireContains(inventory, "Inventory scans do not silently create products", "Inventory must not imply silent product creation.");
        RequireContains(inventory, "does not move the ledger search layout", "Stock movement filters must have stable-layout copy.");
        RequireContains(inventory, "Product lookup and quantity remain in the same fixed area", "Stock write dialog must stabilize lookup and quantity layout.");

        RequireContains(dashboard, "fetchOwnerReportsSummary", "Dashboard must use existing owner-report summary data when available.");
        RequireContains(dashboard, "session.isAuthenticated && canOpenReports", "Dashboard owner-report query must honor the existing report permission.");
        RequireContains(dashboard, "Loading owner report summary", "Dashboard must show owner-summary loading state.");
        RequireContains(dashboard, "Owner home summary", "Dashboard must present owner home summary section.");
        RequireContains(dashboard, "Voided Today", "Dashboard must surface voided count when available.");
        RequireContains(dashboard, "Open Detailed Reports", "Dashboard must keep detailed reports discoverable.");
        RequireContains(reports, "Detailed Reports", "Detailed report drilldown page must use renamed label.");
        RequireContains(reports, "Dashboard remains the owner home summary", "Detailed Reports must not compete with Dashboard as owner home.");
        RequireContains(navigation, "Detailed Reports", "Navigation must expose Detailed Reports.");
        RequireNotContains(navigation, "label: \"Owner Reports\"", "Navigation must not expose old Owner Reports label.");
        RequireNotContains(navigation, "label: \"Official Setup\"", "Official Setup must be hidden from standard navigation.");
        RequireNotContains(navigation, "label: \"Traditional Reporting\"", "Traditional Reporting must be hidden from standard navigation.");
        RequireContains(navigation, "Report Artifacts", "Production report route must be relabeled as Report Artifacts in nav.");
        RequireNotContains(navigation, "label: \"Production Reports\"", "Production Reports nav label must be removed.");

        RequireContains(appShell, "Advanced contains internal host tools only", "Advanced section must be internal-only framed.");
        RequireContains(appShell, "Host LAN Control is Coming Soon / internal preview only", "LAN must remain coming soon/internal preview.");
        RequireContains(appShell, "Report Artifacts are internal export proof only", "Report Artifacts must not look production-ready.");
        RequireContains(appShell, "Backup & Restore UI planned", "Advanced copy must honestly defer backup UI.");
        RequireContains(appShell, "backend foundation exists", "Advanced copy may mention existing foundation without fake UI.");
        RequireContains(appShell, "No restore/create/list controls are exposed here", "Advanced copy must not fake backup UI controls.");

        string operatorSource = string.Join("\n", app, appShell, navigation, checkout, sales, products, inventory, dashboard, reports, settings, users);
        foreach (string token in new[]
        {
            "POS-M1", "POS-M2", "POS-M3", "POS-M4", "POS-M5", "POS-M6", "POS-M7",
            "POS-R1", "POS-S1", "POS-T1", "runtime proof", "CoreSmoke", "milestone", "candidate branch"
        })
        {
            RequireNotContains(operatorSource, token, "Operator-facing frontend source must not expose engineering token: " + token);
        }

        foreach (string claim in new[]
        {
            "BIR ready", "BIR-ready", "BIR approved", "BIR-approved", "official receipt ready", "official invoice ready", "production eSales",
            "eSales production ready", "legal compliance guaranteed", "physical printer proven", "ESC/POS proven", "scanner hardware proven",
            "backup UI complete", "licensing final", "installer ready", "beta ready", "store ready", "client-ready LAN", "multi-terminal production"
        })
        {
            RequireNotContains(operatorSource, claim, "Operator-facing frontend source must not claim: " + claim);
        }
    }

    private static void RunReprintNoDuplicateBehaviorProof()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-manual1-r3-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            AdminBootstrapSummary admin = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "MANUAL1-R3 Store", null, null, null, null, 80, "admin", "MANUAL1-R3 Admin", "StrongAdmin123!")), "bootstrap");

            var categories = RequireValue(composition.CatalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(), "categories");
            var brands = RequireValue(composition.CatalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(), "brands");
            var units = RequireValue(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            DateTimeOffset saleTime = new(2026, 7, 14, 9, 30, 0, TimeSpan.Zero);
            Product product = new()
            {
                Id = Guid.NewGuid(),
                Sku = "MANUAL1-R3-REPRINT",
                Barcode = RequireValue(Barcode.Create("9890000000003"), "barcode"),
                Name = "MANUAL1-R3 Reprint Proof Product",
                CategoryId = categories[0].Id,
                BrandId = brands[0].Id,
                UnitId = units[0].Id,
                CostPrice = Money.FromCents(8000),
                SellingPrice = Money.FromCents(12000),
                StockOnHand = RequireValue(Quantity.FromX1000(5000), "initial stock"),
                ReorderLevel = Quantity.Zero,
                TrackStock = true,
                IsActive = true,
                CreatedAt = saleTime.AddHours(-1)
            };
            RequireSuccess(composition.ProductWriteRepository.AddAsync(product).GetAwaiter().GetResult(), "product");

            Guid saleId = Guid.NewGuid();
            Quantity sold = RequireValue(Quantity.FromX1000(2000), "sold quantity");
            const string snapshot = "SALRYN RECEIPT SNAPSHOT\nReceipt: MANUAL1-R3-000001\nTOTAL: PHP 240.00";
            Sale sale = new()
            {
                Id = saleId,
                ReceiptNumber = "MANUAL1-R3-000001",
                SaleDate = saleTime,
                CashierUserId = admin.AdminUserId,
                Subtotal = Money.FromCents(24000),
                Discount = Money.Zero,
                Tax = Money.Zero,
                Total = Money.FromCents(24000),
                AmountPaid = Money.FromCents(25000),
                Change = Money.FromCents(1000),
                Status = SaleStatus.Completed,
                ReceiptSnapshot = snapshot,
                CreatedAt = saleTime
            };
            SaleItem item = new()
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                BarcodeSnapshot = product.Barcode?.Value,
                UnitNameSnapshot = units[0].Name,
                Quantity = sold,
                UnitPrice = Money.FromCents(12000),
                CostPrice = product.CostPrice,
                Discount = Money.Zero,
                LineTotal = Money.FromCents(24000)
            };
            Payment payment = new()
            {
                Id = Guid.NewGuid(),
                SaleId = saleId,
                Method = PaymentMethod.Cash,
                Amount = Money.FromCents(25000),
                PaidAt = saleTime.AddSeconds(30)
            };
            StockMovement movement = new()
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                MovementType = StockMovementType.Sale,
                QuantityChange = sold,
                CostPrice = product.CostPrice,
                ReferenceType = "SALE",
                ReferenceId = saleId,
                Reason = "MANUAL1-R3 seeded checkout sale",
                UserId = admin.AdminUserId,
                CreatedAt = saleTime.AddSeconds(45)
            };

            RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
                sale,
                new[] { item },
                new[] { payment },
                new[] { movement }).GetAwaiter().GetResult(), "seed sale");

            long beforeSales = CountTable(composition, "sales");
            long beforePayments = CountTable(composition, "payments");
            long beforeMovements = CountTable(composition, "stock_movements");
            long beforeStock = ProductStock(composition, product.Id);
            string beforeSnapshot = ReceiptSnapshot(composition, saleId);

            PosReceiptPrintService print = new(
                composition.SaleReadRepository,
                composition.SettingsReadRepository,
                new NoOpReceiptPrinterAdapter());
            ReceiptPrintResult result = print.PrintSaleReceiptAsync(saleId).GetAwaiter().GetResult();

            Require(result.Success, "No-op persisted snapshot reprint should succeed.");
            Require(result.Source == ReceiptPrintSource.PersistedReceiptSnapshot, "Reprint must use persisted snapshot source.");
            Require(CountTable(composition, "sales") == beforeSales, "Reprint must not duplicate sales.");
            Require(CountTable(composition, "payments") == beforePayments, "Reprint must not duplicate payments.");
            Require(CountTable(composition, "stock_movements") == beforeMovements, "Reprint must not create stock movement.");
            Require(ProductStock(composition, product.Id) == beforeStock, "Reprint must not mutate product stock.");
            Require(ReceiptSnapshot(composition, saleId) == beforeSnapshot, "Reprint must not rewrite receipt snapshot.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static long CountTable(SalrynLocalComposition composition, string tableName)
    {
        if (tableName is not ("sales" or "payments" or "stock_movements"))
        {
            throw new InvalidOperationException("Unexpected table count request.");
        }

        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long ProductStock(SalrynLocalComposition composition, Guid productId)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $id;";
        command.Parameters.AddWithValue("$id", productId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReceiptSnapshot(SalrynLocalComposition composition, Guid saleId)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT receipt_snapshot FROM sales WHERE id = $id;";
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.ErrorDetail.Message}");
        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.ErrorDetail.Message}");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) current = current.Parent;
        return current ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message);
    }
}

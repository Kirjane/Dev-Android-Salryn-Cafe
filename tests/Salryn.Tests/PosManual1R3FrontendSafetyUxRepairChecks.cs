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
        RequireContains(products, "hidden from active sale flow", "Deactivate copy must explain inactive sale-flow effect.");
        RequireContains(products, "Historical sales remain preserved", "Deactivate copy must explain sales history preservation.");
        RequireContains(products, "delete past records", "Deactivate copy must explain no deletion.");
        RequireContains(products, "Create and manage product identity", "Product Catalog must use client-facing purpose copy.");

        RequireContains(inventory, "Change product quantity through a ledger entry", "Inventory must use client-facing purpose copy.");
        RequireContains(inventory, "Product Catalog defines the product. Stock Movements changes the quantity", "Inventory must explain recorded movements concisely.");
        RequireNotContains(inventory, "Barcode and Fast Add boundaries", "Inventory must not show a deferred-feature wall.");
        RequireContains(inventory, "Scan or enter the product barcode/SKU to select the product", "Stock write dialog must guide lookup clearly.");

        RequireContains(dashboard, "fetchOwnerReportsSummary", "Dashboard must use existing owner-report summary data when available.");
        RequireContains(dashboard, "session.isAuthenticated && canOpenReports", "Dashboard owner-report query must honor the existing report permission.");
        RequireContains(dashboard, "Loading owner report summary", "Dashboard must show owner-summary loading state.");
        RequireContains(dashboard, "Owner home summary", "Dashboard must present owner home summary section.");
        RequireContains(dashboard, "Voided Today", "Dashboard must surface voided count when available.");
        RequireContains(dashboard, "Open Reports", "Dashboard must keep Reports discoverable.");
        RequireContains(reports, "title=\"Reports\"", "Report drilldown page must use Reports label.");
        RequireNotContains(reports, "Dashboard remains the owner home summary", "Reports must not show internal product-boundary copy.");
        RequireContains(navigation, "label: \"Reports\"", "Navigation must expose Reports.");
        RequireNotContains(navigation, "label: \"Owner Reports\"", "Navigation must not expose old Owner Reports label.");
        RequireNotContains(navigation, "label: \"Official Setup\"", "Official Setup must be hidden from standard navigation.");
        RequireNotContains(navigation, "label: \"Traditional Reporting\"", "Traditional Reporting must be hidden from standard navigation.");
        RequireContains(navigation, "Report Artifacts", "Production report route must be relabeled as Report Artifacts in nav.");
        RequireNotContains(navigation, "label: \"Production Reports\"", "Production Reports nav label must be removed.");

        RequireContains(navigation, "VITE_SALRYN_INTERNAL_SURFACES", "Advanced surfaces must require explicit internal mode.");
        RequireContains(navigation, "label: \"Terminal Management\"", "Internal LAN navigation must use Terminal Management.");
        RequireNotContains(appShell, "Advanced contains internal host tools only", "Normal shell must not show an Advanced warning wall.");

        // Backup UI1: Backup & Restore is implemented and admin-gated (stale "planned" deferral copy is obsolete).
        string backupPage = Read(web, "pages", "BackupRestorePage.tsx");
        RequireContains(navigation, "id: \"backup-restore\", label: \"Backup & Restore\"", "Backup & Restore must appear as an implemented admin nav item.");
        RequireContains(navigation, "group: \"Admin\"", "Backup & Restore must remain under Admin tools.");
        RequireContains(appShell, "\"Backup & Restore\":", "AppShell must map the Backup & Restore nav label.");
        RequireNotContains(appShell, "item.id === \"backup-restore\"", "AppShell must not show an Admin badge on Backup & Restore.");
        RequireNotContains(appShell, "Backup & Restore UI planned", "Stale Backup UI deferral copy must not remain after Backup UI1.");
        RequireNotContains(appShell, "No restore/create/list controls are exposed here", "Stale Backup UI absence copy must not remain after Backup UI1.");
        RequireNotContains(backupPage, "not cloud backup", "Backup page must not show a cloud disclaimer wall.");
        RequireNotContains(backupPage, "installer migration proof", "Backup page must not show an installer disclaimer wall.");
        RequireContains(backupPage, "Restore replaces the current local database", "Backup page must keep the real restore warning.");

        string operatorSource = string.Join("\n", app, appShell, navigation, checkout, sales, products, inventory, dashboard, reports, settings, users, backupPage);
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
            "hardware support ready", "backup UI complete", "licensing final", "installer ready", "beta ready", "store ready", "client-ready LAN",
            "multi-terminal production ready", "multi-terminal production supported", "LAN production ready", "LAN production-ready"
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
                Reason = "MANUAL1-R3 sale before reprint proof",
                UserId = admin.AdminUserId,
                CreatedAt = saleTime.AddSeconds(30)
            };
            RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
                sale, new[] { item }, new[] { payment }, new[] { movement }).GetAwaiter().GetResult(), "completed sale");

            long beforeSales = CountTable(composition, "sales");
            long beforePayments = CountTable(composition, "payments");
            long beforeMovements = CountTable(composition, "stock_movements");
            long beforeStock = ProductStock(composition, product.Id);

            PosReceiptPrintService service = new(composition.SaleReadRepository, new NoOpReceiptPrinterAdapter("MANUAL1-R3-NoOpPrinter"));
            PosReceiptPrintResult result = service.PrintSaleReceiptAsync(saleId).GetAwaiter().GetResult();

            Require(result.Succeeded, "Reprint should succeed with no-op printer adapter.");
            Require(string.Equals(result.Source, PosReceiptPrintSources.PersistedReceiptSnapshot, StringComparison.Ordinal), "Reprint must use persisted snapshot source.");
            Require(CountTable(composition, "sales") == beforeSales, "Reprint must not duplicate or mutate sales.");
            Require(CountTable(composition, "payments") == beforePayments, "Reprint must not duplicate or mutate payments.");
            Require(CountTable(composition, "stock_movements") == beforeMovements, "Reprint must not duplicate or mutate stock movements.");
            Require(ProductStock(composition, product.Id) == beforeStock, "Reprint must not mutate stock quantity.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static long CountTable(SalrynLocalComposition composition, string tableName)
    {
        if (tableName is not ("sales" or "payments" or "stock_movements")) throw new InvalidOperationException("Unexpected table count request.");
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

    private static T RequireValue<T>(Result<T> result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.Error}");
        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{area}: {result.Error}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
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

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosR1OwnerReportsChecks
{
    public static void Run()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pos-r1-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            _ = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "POS-R1 Store", null, null, null, null, 80, "admin", "POS-R1 Admin", "StrongAdmin123!")), "bootstrap");

            InMemoryLocalApiSessionStore store = new();
            LocalApiSessionService sessions = new(composition.Authentication, store);
            LocalApiSession adminSession = RequireValue(sessions.LoginAsync(new LoginRequest("admin", "StrongAdmin123!"))
                .GetAwaiter().GetResult(), "admin login");
            Require(adminSession.Permissions.Contains(SalrynPermissions.ReportsView), "Admin session must carry Reports.View.");
            Require(adminSession.Permissions.Contains(SalrynPermissions.PayablesView), "Admin session must carry Payables.View.");
            Require(RoleHasPermission(composition, DefaultSeedData.ManagerRoleId, SalrynPermissions.ReportsView), "Manager role must carry Reports.View.");
            Require(RoleHasPermission(composition, DefaultSeedData.ManagerRoleId, SalrynPermissions.PayablesView), "Manager role must carry Payables.View.");
            Require(!RoleHasPermission(composition, DefaultSeedData.CashierRoleId, SalrynPermissions.ReportsView), "Cashier role must not carry Reports.View.");
            Require(!RoleHasPermission(composition, DefaultSeedData.CashierRoleId, SalrynPermissions.PayablesView), "Cashier role must not carry Payables.View.");

            Guid cashierUserId = Guid.NewGuid();
            InsertUser(composition, cashierUserId, "cashier-r1", "POS-R1 Cashier", DefaultSeedData.CashierRoleId);

            var categories = RequireValue(composition.CatalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(), "categories");
            var units = RequireValue(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            var brands = RequireValue(composition.CatalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(), "brands");

            Product apple = NewProduct(
                "POS-R1 Apple",
                "9910000000001",
                categories.First(category => category.Name == "General Merchandise").Id,
                brands[0].Id,
                units[0].Id,
                costCents: 1000,
                sellingCents: 1500,
                stockX1000: 20000,
                reorderX1000: 5000);
            Product banana = NewProduct(
                "POS-R1 Banana",
                "9910000000002",
                categories.First(category => category.Name == "Uncategorized").Id,
                brands[0].Id,
                units[0].Id,
                costCents: 1200,
                sellingCents: 2000,
                stockX1000: 20000,
                reorderX1000: 25000);
            RequireSuccess(composition.ProductWriteRepository.AddAsync(apple).GetAwaiter().GetResult(), "apple product");
            RequireSuccess(composition.ProductWriteRepository.AddAsync(banana).GetAwaiter().GetResult(), "banana product");

            DateTimeOffset now = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
            AddCompletedSale(composition, "POS-R1-000001", adminSession.UserId, apple, units[0].Name, quantityX1000: 2000, unitPriceCents: 1500, now.AddHours(-2));
            AddCompletedSale(composition, "POS-R1-000002", cashierUserId, banana, units[0].Name, quantityX1000: 1000, unitPriceCents: 2000, now.AddHours(-1));
            Guid voidSaleId = AddCompletedSale(composition, "POS-R1-000003", adminSession.UserId, apple, units[0].Name, quantityX1000: 1000, unitPriceCents: 1500, now.AddMinutes(-30));

            SaleVoidApiService voidService = new(composition);
            _ = RequireValue(voidService.VoidAsync(voidSaleId, new SaleVoidRequest("POS-R1 void should be separated from owner active totals."), adminSession)
                .GetAwaiter().GetResult(), "void sale");

            long beforeSales = CountTable(composition, "sales");
            long beforePayments = CountTable(composition, "payments");
            long beforeMovements = CountTable(composition, "stock_movements");
            long beforeAppleStock = ProductStock(composition, apple.Id);
            long beforeBananaStock = ProductStock(composition, banana.Id);

            OwnerReportsApiService service = new(composition);
            OwnerReportsResponse report = RequireValue(service.GetSummaryAsync(now).GetAwaiter().GetResult(), "owner reports");

            Require(report.Daily.SalesCount == 2, "Daily active sale count must exclude voided sales.");
            Require(report.Daily.SalesAmountCents == 5000, "Daily active amount must match completed non-voided sales.");
            Require(report.Daily.AverageSaleCents == 2500, "Daily average sale must be active amount divided by active count.");
            Require(report.Daily.VoidedCount == 1, "Daily voided sale count must be shown separately.");
            Require(report.Daily.VoidedAmountCents == 1500, "Daily voided amount must be shown separately.");
            Require(report.Weekly.SalesCount == 2 && report.Weekly.SalesAmountCents == 5000, "Weekly active totals must match seeded current-week sales.");
            Require(report.Monthly.SalesCount == 2 && report.Monthly.SalesAmountCents == 5000, "Monthly active totals must match seeded current-month sales.");

            Require(report.TopProducts.Count >= 2, "Top products must include completed sale products.");
            Require(report.TopProducts[0].ProductName == "POS-R1 Apple", "Top products must rank by active sales amount then quantity.");
            Require(report.TopProducts[0].QuantitySold == 2m, "Top products must exclude voided product quantity.");
            Require(report.TopProducts[0].SalesAmountCents == 3000, "Top products must exclude voided product amount.");

            OwnerCashierTotalRow adminRow = report.CashierTotals.Single(row => row.CashierDisplayName == "POS-R1 Admin");
            OwnerCashierTotalRow cashierRow = report.CashierTotals.Single(row => row.CashierDisplayName == "POS-R1 Cashier");
            Require(adminRow.SalesCount == 1 && adminRow.SalesAmountCents == 3000, "Admin cashier total must exclude voided sale from active amount.");
            Require(adminRow.VoidedCount == 1 && adminRow.VoidedAmountCents == 1500, "Admin cashier total must show voided sale separately.");
            Require(cashierRow.SalesCount == 1 && cashierRow.SalesAmountCents == 2000, "Cashier total must match seeded cashier sale.");

            Require(report.CategoryTotals.Any(row => row.CategoryName == "General Merchandise" && row.SalesAmountCents == 3000), "Category totals must include product category sales.");
            Require(report.CategoryTotals.Any(row => row.CategoryName == "Uncategorized" && row.SalesAmountCents == 2000), "Category totals must include Uncategorized sales.");
            Require(report.LowStock.Any(row => row.ProductName == "POS-R1 Banana"), "Low stock report must include tracked products at or below reorder threshold.");
            Require(report.InventoryValue.IsAvailable, "Inventory value must be available when all active tracked products have cost.");
            Require(report.InventoryValue.EstimatedValueCents == 40800m, "Inventory value must equal current stock multiplied by cost.");

            Require(CountTable(composition, "sales") == beforeSales, "Owner reports must not mutate sales.");
            Require(CountTable(composition, "payments") == beforePayments, "Owner reports must not mutate payments.");
            Require(CountTable(composition, "stock_movements") == beforeMovements, "Owner reports must not mutate stock movement ledger.");
            Require(ProductStock(composition, apple.Id) == beforeAppleStock, "Owner reports must not mutate apple stock.");
            Require(ProductStock(composition, banana.Id) == beforeBananaStock, "Owner reports must not mutate banana stock.");

            VerifyApAgingReportsBoundaryAsync(composition, adminSession.UserId, cashierUserId, apple.Id).GetAwaiter().GetResult();
            RunSourceGuards();
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static Product NewProduct(
        string name,
        string barcode,
        Guid categoryId,
        Guid brandId,
        Guid unitId,
        long costCents,
        long sellingCents,
        long stockX1000,
        long reorderX1000)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Sku = name.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToUpperInvariant(),
            Barcode = RequireValue(Barcode.Create(barcode), "barcode"),
            Name = name,
            CategoryId = categoryId,
            BrandId = brandId,
            UnitId = unitId,
            CostPrice = Money.FromCents(costCents),
            SellingPrice = Money.FromCents(sellingCents),
            StockOnHand = RequireValue(Quantity.FromX1000(stockX1000), "stock"),
            ReorderLevel = RequireValue(Quantity.FromX1000(reorderX1000), "reorder"),
            TrackStock = true,
            IsActive = true
        };
    }

    private static Guid AddCompletedSale(
        SalrynLocalComposition composition,
        string receiptNo,
        Guid cashierUserId,
        Product product,
        string unitName,
        long quantityX1000,
        long unitPriceCents,
        DateTimeOffset saleDate)
    {
        Guid saleId = Guid.NewGuid();
        Quantity quantity = RequireValue(Quantity.FromX1000(quantityX1000), "sale quantity");
        long lineTotalCents = checked(unitPriceCents * quantityX1000 / 1000);
        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = receiptNo,
            SaleDate = saleDate,
            CashierUserId = cashierUserId,
            Subtotal = Money.FromCents(lineTotalCents),
            Total = Money.FromCents(lineTotalCents),
            AmountPaid = Money.FromCents(lineTotalCents),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = receiptNo + " snapshot"
        };
        SaleItem item = new()
        {
            SaleId = saleId,
            ProductId = product.Id,
            ProductNameSnapshot = product.Name,
            BarcodeSnapshot = product.Barcode?.Value,
            UnitNameSnapshot = unitName,
            Quantity = quantity,
            UnitPrice = Money.FromCents(unitPriceCents),
            CostPrice = product.CostPrice,
            LineTotal = Money.FromCents(lineTotalCents)
        };
        Payment payment = new() { SaleId = saleId, Amount = Money.FromCents(lineTotalCents), PaidAt = saleDate };
        StockMovement movement = new()
        {
            ProductId = product.Id,
            MovementType = StockMovementType.Sale,
            QuantityChange = quantity,
            CostPrice = product.CostPrice,
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "POS-R1 owner reports seed sale",
            UserId = cashierUserId,
            CreatedAt = saleDate
        };

        RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
            sale, new[] { item }, new[] { payment }, new[] { movement }).GetAwaiter().GetResult(), receiptNo);
        return saleId;
    }

    private static void InsertUser(SalrynLocalComposition composition, Guid id, string username, string displayName, string roleId)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
            VALUES ($id, $username, $displayName, $passwordHash, $roleId, 1, $createdAt, NULL);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$passwordHash", "unused-pos-r1-test-hash");
        command.Parameters.AddWithValue("$roleId", roleId);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static bool RoleHasPermission(SalrynLocalComposition composition, string roleId, string permission)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM role_permissions
            WHERE role_id = $roleId
              AND permission = $permission;
            """;
        command.Parameters.AddWithValue("$roleId", roleId);
        command.Parameters.AddWithValue("$permission", permission);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static long CountTable(SalrynLocalComposition composition, string tableName)
    {
        if (tableName is not ("sales" or "payments" or "stock_movements"))
        {
            throw new InvalidOperationException("Unexpected POS-R1 table count request.");
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

    private static void RunSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string endpoints = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.Api.Local", "LocalOwnerReportsEndpoints.cs"));
        string service = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.Api.Local", "Services", "OwnerReportsApiService.cs"));
        string api = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "api", "ownerReports.ts"));
        string page = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "pages", "OwnerReportsPage.tsx"));
        string permissions = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "security", "permissions.ts"));
        string navigation = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "navigation.ts"));
        string salesApi = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "api", "sales.ts"));

        Require(endpoints.Contains("LocalApiPermissions.ReportsView", StringComparison.Ordinal), "Detailed reports endpoint must require Reports.View.");
        Require(service.Contains("WHERE s.status = $completedStatus", StringComparison.Ordinal), "Detailed reports totals must filter active completed sales.");
        Require(service.Contains("$voidedStatus", StringComparison.Ordinal), "Detailed reports must track voided sales separately.");
        Require(!api.Contains("method: \"POST\"", StringComparison.Ordinal), "Detailed reports web API client must remain read-only.");
        Require(page.Contains("title=\"Reports\"", StringComparison.Ordinal), "Frontend must expose Reports page title.");
        Require(!page.Contains("Dashboard remains the owner home summary", StringComparison.Ordinal), "Reports must not show internal product-boundary copy.");
        Require(!page.Contains("Not an official or regulatory filing", StringComparison.Ordinal), "Reports must not show regulatory disclaimer walls.");
        Require(page.Contains("Read-only", StringComparison.Ordinal), "Reports UI must remain visibly read-only.");
        Require(page.Contains("Financial snapshot", StringComparison.Ordinal), "Reports must expose a concise financial snapshot.");
        Require(page.Contains("Average ticket", StringComparison.Ordinal), "Reports must expose average completed-sale value.");
        Require(page.Contains("Cash realization", StringComparison.Ordinal), "Reports must distinguish sales cash realization from later collections.");
        Require(page.Contains("AR outstanding", StringComparison.Ordinal), "Reports must expose entitled AR stock separately from sales flow.");
        Require(page.Contains("row.value === 0 ? \"0%\"", StringComparison.Ordinal), "Zero-value bars must not render a misleading positive-width bar.");
        Require(page.Contains("will not substitute today&apos;s catalog cost", StringComparison.Ordinal), "Missing historical cost must remain fail-closed and operator-readable.");
        Require(!page.Contains("Coming soon", StringComparison.Ordinal), "Developer-only disabled nav copy must not be reintroduced to Detailed Reports.");
        Require(permissions.Contains("\"owner-reports\": [Permissions.ReportsView]", StringComparison.Ordinal), "Detailed reports direct page access must be gated by Reports.View.");
        Require(navigation.Contains("label: \"Reports\"", StringComparison.Ordinal), "Reports must be reachable from store navigation.");
        Require(!navigation.Contains("label: \"Owner Reports\"", StringComparison.Ordinal), "Old Owner Reports nav label must not remain.");
        Require(salesApi.Contains("T00:00:00+08:00", StringComparison.Ordinal), "Sales drill-down start date must use the Asia/Manila store-day boundary.");
        Require(salesApi.Contains("T23:59:59.9999999+08:00", StringComparison.Ordinal), "Sales drill-down end date must include the complete Asia/Manila store day.");
        Require(page.Contains("Permissions.PayablesView", StringComparison.Ordinal), "Reports page must gate AP aging query by Payables.View.");
        Require(page.Contains("session.data?.user?.userId", StringComparison.Ordinal), "Reports page must scope AP query cache key to authenticated user session.");
        Require(page.Contains("fetchApAgingReport", StringComparison.Ordinal), "Reports page must load AP aging from the AP aging endpoint.");
        Require(page.Contains("Accounts Payable aging", StringComparison.Ordinal), "Reports page must display Accounts Payable aging card.");
        Require(page.Contains("CURRENT outstanding AP — aging today", StringComparison.Ordinal), "Reports page must explicitly label AP aging as CURRENT outstanding AP — aging today.");
        Require(page.Contains("fetchApAgingReport()", StringComparison.Ordinal) && !page.Contains("fetchApAgingReport(to)", StringComparison.Ordinal),
            "Reports page must decouple AP aging query from historical report end date 'to'.");
        Require(!page.Contains("as of {to}", StringComparison.Ordinal), "Reports page must never label current AP balances as historical 'as of {to}'.");
        Require(page.Contains("AP analytics unavailable", StringComparison.Ordinal), "Reports page must render explicit AP unavailable notice when unauthorized or failed.");
        Require(page.Contains("!canViewPayables || ap.isError", StringComparison.Ordinal), "Reports page must fail closed when Payables.View is absent.");

        string apPage = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "pages", "AccountsPayablePage.tsx"));
        Require(apPage.Contains("fetchSupplierPayableLedger", StringComparison.Ordinal), "Dedicated AP workspace must load supplier payable subledger entries.");
        Require(apPage.Contains("Supplier Payable Subledger", StringComparison.Ordinal), "Dedicated AP workspace must display Supplier Payable Subledger.");
        Require(apPage.Contains("Invoice Reconciliation:", StringComparison.Ordinal), "Dedicated AP workspace must provide per-invoice reconciliation drilldown.");
        Require(apPage.Contains("Pending Approval (Not Deducted)", StringComparison.Ordinal), "Dedicated AP workspace must distinguish unsettled pending payment requests from posted debits.");
    }

    private static async Task VerifyApAgingReportsBoundaryAsync(
        SalrynLocalComposition composition,
        Guid adminUserId,
        Guid cashierUserId,
        Guid productId)
    {
        SqliteProcureToPayService p2p = new(composition.ConnectionFactory, TimeProvider.System);
        Guid locationId;
        await using (SqliteConnection conn = composition.ConnectionFactory.CreateConnection())
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
            locationId = Guid.Parse(Convert.ToString(await cmd.ExecuteScalarAsync()) ?? throw new InvalidOperationException("MAIN-STORE location missing."));
        }

        var supplier = RequireValue(await p2p.CreateSupplierAsync(
            new CreateLpiSupplierRequest("POSR1-SUP", "POS-R1 AP Aging Supplier", "Owner", "09170001234", null, "Manila", null, 15, 0, "Report aging seed", adminUserId)),
            "AP aging supplier");
        PurchaseOrderModel po = RequireValue(await p2p.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(supplier.Id, null, new DateOnly(2026, 11, 15), locationId, 15, 0, "Net 15", "Reports aging seed", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "POS-R1-APPLE", "POS-R1 Apple", "Resale", "pc", 10_000, 18_500, 0, 0, null) },
                adminUserId)),
            "AP aging PO");
        ReceivingReportModel rr = RequireValue(await p2p.CreateReceivingReportAsync(
            new CreateReceivingReportRequest(po.Id, locationId, "DR-RPT-1", "INV-RPT-1", null, new DateOnly(2026, 11, 5), true, null, null, "rpt-rr-1", "Accepted",
                new[] { new CreateReceivingReportLineInput(po.Lines.Single().Id, 10_000, 10_000, 0, null, null, null, null) }, adminUserId)),
            "AP aging RR");

        // Open posted invoice: 185_000 cents due 2026-11-20
        SupplierInvoiceModel openInvoice = RequireValue(await p2p.CreateSupplierInvoiceAsync(
            new CreateSupplierInvoiceRequest(supplier.Id, "SINV-RPT-OPEN", po.Id, rr.Id, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 20), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines.Single().Id, rr.Lines.Single().Id, productId, "POS-R1 Apple", 5_000, 18_500, 0) }, adminUserId)),
            "AP aging open invoice");
        Require(openInvoice.Status == "PostedOpen" && openInvoice.TotalInvoiceCents == 92_500, "Posted open invoice expected.");

        // Pending payment request (40_000 cents) MUST NOT reduce AP aging total
        SupplierPaymentModel pendingPayment = RequireValue(await p2p.RecordSupplierPaymentAsync(
            new RecordSupplierPaymentRequest(openInvoice.Id, new DateOnly(2026, 11, 18), 40_000, "BankTransfer", "OperatingBank", "PEND-RPT-1", null, "BPI", "Unsettled", "rpt-pay-1", adminUserId, null)),
            "AP aging pending payment");

        // Second unposted VarianceFlagged invoice MUST NOT enter AP aging open balance
        SupplierInvoiceModel flaggedInvoice = RequireValue(await p2p.CreateSupplierInvoiceAsync(
            new CreateSupplierInvoiceRequest(supplier.Id, "SINV-RPT-VAR", po.Id, rr.Id, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 20), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines.Single().Id, rr.Lines.Single().Id, productId, "POS-R1 Apple", 2_000, 21_000, 0) }, adminUserId)),
            "AP aging variance invoice");
        Require(flaggedInvoice.Status == "VarianceFlagged", "Variance invoice should remain unposted.");

        InMemoryLocalApiSessionStore sessionStore = new();
        LocalApiSession reportsOnlySession = sessionStore.Create(new AuthenticatedUserReadModel(
            Guid.NewGuid(), "reports-only", "Reports Only Viewer", "Custom", UserStatus.Active, new[] { SalrynPermissions.ReportsView }) { RoleId = Guid.Parse(DefaultSeedData.CashierRoleId) });
        LocalApiSession cashierSession = sessionStore.Create(new AuthenticatedUserReadModel(
            cashierUserId, "cashier-r1", "POS-R1 Cashier", "Cashier", UserStatus.Active, DefaultSeedData.CashierPermissions) { RoleId = Guid.Parse(DefaultSeedData.CashierRoleId) });
        LocalApiSession managerSession = sessionStore.Create(new AuthenticatedUserReadModel(
            adminUserId, "manager-r1", "POS-R1 Manager", "Manager", UserStatus.Active, DefaultSeedData.ManagerPermissions) { RoleId = Guid.Parse(DefaultSeedData.ManagerRoleId) });

        await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                SessionStore = sessionStore,
                LicenseService = new ReportsLicensedTestService(),
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false,
            }).ConfigureAwait(false);
        await app.StartAsync().ConfigureAwait(false);

        try
        {
            string address = app.Urls.First(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) && !u.EndsWith(":0", StringComparison.Ordinal));
            using HttpClient anon = new() { BaseAddress = new Uri(address) };
            using HttpClient reportsOnly = new() { BaseAddress = new Uri(address) };
            reportsOnly.DefaultRequestHeaders.Add("Cookie", $"{LocalApiSessionService.SessionCookieName}={reportsOnlySession.SessionId}");
            using HttpClient cashier = new() { BaseAddress = new Uri(address) };
            cashier.DefaultRequestHeaders.Add("Cookie", $"{LocalApiSessionService.SessionCookieName}={cashierSession.SessionId}");
            using HttpClient manager = new() { BaseAddress = new Uri(address) };
            manager.DefaultRequestHeaders.Add("Cookie", $"{LocalApiSessionService.SessionCookieName}={managerSession.SessionId}");

            using HttpResponseMessage anonResp = await anon.GetAsync("/api/lpi/ap-aging?asOfDate=2026-12-05");
            Require(anonResp.StatusCode == HttpStatusCode.Unauthorized, "Anonymous /api/lpi/ap-aging must return 401.");

            using HttpResponseMessage reportsResp = await reportsOnly.GetAsync("/api/lpi/ap-aging?asOfDate=2026-12-05");
            string reportsBody = await reportsResp.Content.ReadAsStringAsync();
            Require(reportsResp.StatusCode == HttpStatusCode.Forbidden &&
                    !reportsBody.Contains("totalOpenPayablesCents", StringComparison.Ordinal) &&
                    !reportsBody.Contains("POS-R1 AP Aging Supplier", StringComparison.Ordinal),
                "Reports.View without Payables.View must receive 403 and zero supplier AP data.");

            using HttpResponseMessage cashierResp = await cashier.GetAsync("/api/lpi/ap-aging?asOfDate=2026-12-05");
            Require(cashierResp.StatusCode == HttpStatusCode.Forbidden, "Cashier without Payables.View must receive 403 on /api/lpi/ap-aging.");

            using HttpResponseMessage managerResp = await manager.GetAsync("/api/lpi/ap-aging?asOfDate=2026-12-05");
            Require(managerResp.StatusCode == HttpStatusCode.OK, "Manager with Reports.View + Payables.View must receive 200 on /api/lpi/ap-aging.");
            using JsonDocument doc = JsonDocument.Parse(await managerResp.Content.ReadAsStringAsync());
            long total = doc.RootElement.GetProperty("totalOpenPayablesCents").GetInt64();
            long b1To30 = doc.RootElement.GetProperty("days1To30Cents").GetInt64();
            long bucketSum = doc.RootElement.GetProperty("currentBucketCents").GetInt64() + b1To30 +
                             doc.RootElement.GetProperty("days31To60Cents").GetInt64() +
                             doc.RootElement.GetProperty("days61To90Cents").GetInt64() +
                             doc.RootElement.GetProperty("daysOver90Cents").GetInt64();
            int varianceCount = doc.RootElement.GetProperty("varianceFlaggedInvoiceCount").GetInt32();

            await using SqliteConnection sql = composition.ConnectionFactory.CreateConnection();
            await sql.OpenAsync();
            await using var cmd = sql.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(credit_cents - debit_cents), 0) FROM supplier_payable_entries;";
            long sqlNetLedger = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            Require(total == 92_500 && b1To30 == 92_500 && bucketSum == total && varianceCount == 1 && sqlNetLedger == total,
                $"Manager AP aging JSON must reconcile to SQL net ledger 92_500 (total={total}, 1-30={b1To30}, variance={varianceCount}, sql={sqlNetLedger}).");

            // Now confirm a later settlement of the pending 40_000 payment and verify AP aging reflects current remaining balance (52_500),
            // even when an earlier asOfDate is passed (proving Option B current-AP semantics).
            Guid approverUserId = Guid.NewGuid();
            await using (SqliteConnection conn = composition.ConnectionFactory.CreateConnection())
            {
                await conn.OpenAsync();
                await using SqliteCommand ins = conn.CreateCommand();
                ins.CommandText = """
                    INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
                    VALUES ($id, $username, 'AP Approver', 'hash', $roleId, 'Active', $now, $now);
                    """;
                ins.Parameters.AddWithValue("$id", approverUserId.ToString("D"));
                ins.Parameters.AddWithValue("$username", $"ap-approver-{approverUserId:N}");
                ins.Parameters.AddWithValue("$roleId", DefaultSeedData.ManagerRoleId);
                ins.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await ins.ExecuteNonQueryAsync();
            }

            string manilaToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime).ToString("yyyy-MM-dd");
            _ = RequireValue(await p2p.ApproveSupplierPaymentAsync(
                new ApproveSupplierPaymentRequest(pendingPayment.Id, approverUserId, "YES", "Confirmed later settlement", manilaToday)),
                "approve later AP settlement");

            using HttpResponseMessage afterCurrentResp = await manager.GetAsync("/api/lpi/ap-aging");
            Require(afterCurrentResp.StatusCode == HttpStatusCode.OK, "Manager /api/lpi/ap-aging after settlement must return 200.");
            using JsonDocument afterCurrentDoc = JsonDocument.Parse(await afterCurrentResp.Content.ReadAsStringAsync());
            long afterCurrentTotal = afterCurrentDoc.RootElement.GetProperty("totalOpenPayablesCents").GetInt64();
            Require(afterCurrentTotal == 52_500,
                $"After settling 40_000, current AP aging total must drop from 92_500 to 52_500 (got {afterCurrentTotal}).");

            using HttpResponseMessage earlierDateResp = await manager.GetAsync("/api/lpi/ap-aging?asOfDate=2026-11-10");
            Require(earlierDateResp.StatusCode == HttpStatusCode.OK, "Manager /api/lpi/ap-aging with earlier date must return 200.");
            using JsonDocument earlierDoc = JsonDocument.Parse(await earlierDateResp.Content.ReadAsStringAsync());
            long earlierTotal = earlierDoc.RootElement.GetProperty("totalOpenPayablesCents").GetInt64();
            Require(earlierTotal == 52_500,
                $"AP aging endpoint reflects CURRENT remaining AP (52_500) rather than historical balance reconstruction (got {earlierTotal}).");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private sealed class ReportsLicensedTestService : ILocalLicenseService
    {
        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LocalLicenseValidationResult(
            LicenseStatus.Licensed,
            LicenseEdition.Local,
            LicenseFeatures.AllList,
            ExpiresAt: null,
            DaysRemaining: null,
            MachineMatched: true,
            Message: "POS-R1 disposable test license",
            SourcePath: "test",
            ValidationTimestamp: DateTimeOffset.UtcNow,
            LicenseType: LicenseType.Host));
        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) current = current.Parent;
        return current ?? throw new InvalidOperationException("Repository root not found.");
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
}

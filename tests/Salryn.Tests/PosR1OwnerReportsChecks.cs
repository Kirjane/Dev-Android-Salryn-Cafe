using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Seeding;
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
            Require(RoleHasPermission(composition, DefaultSeedData.ManagerRoleId, SalrynPermissions.ReportsView), "Manager role must carry Reports.View.");
            Require(!RoleHasPermission(composition, DefaultSeedData.CashierRoleId, SalrynPermissions.ReportsView), "Cashier role must not carry Reports.View.");

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

        Require(endpoints.Contains("LocalApiPermissions.ReportsView", StringComparison.Ordinal), "Owner reports endpoint must require Reports.View.");
        Require(service.Contains("WHERE s.status = $completedStatus", StringComparison.Ordinal), "Owner reports totals must filter active completed sales.");
        Require(service.Contains("$voidedStatus", StringComparison.Ordinal), "Owner reports must track voided sales separately.");
        Require(!api.Contains("method: \"POST\"", StringComparison.Ordinal), "Owner reports web API client must remain read-only.");
        Require(page.Contains("Owner Reports", StringComparison.Ordinal), "Frontend must expose Owner Reports page title.");
        Require(page.Contains("Not an official/BIR/RMO report", StringComparison.Ordinal), "Owner reports page must state non-official boundary.");
        Require(page.Contains("read-only", StringComparison.Ordinal), "Owner reports UI must state read-only boundary.");
        Require(!page.Contains("Coming soon", StringComparison.Ordinal), "Developer-only disabled nav copy must not be reintroduced.");
        Require(permissions.Contains("\"owner-reports\": [Permissions.ReportsView]", StringComparison.Ordinal), "Owner reports direct page access must be gated by Reports.View.");
        Require(navigation.Contains("Owner Reports", StringComparison.Ordinal), "Owner Reports must be reachable from store navigation.");
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

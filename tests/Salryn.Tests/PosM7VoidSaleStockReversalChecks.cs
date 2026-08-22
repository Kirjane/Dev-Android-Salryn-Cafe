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
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosM7VoidSaleStockReversalChecks
{
    public static void Run()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pos-m7-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            _ = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "POS-M7 Store", null, null, null, null, 80, "admin", "POS-M7 Admin", "StrongAdmin123!")), "bootstrap");

            InMemoryLocalApiSessionStore store = new();
            LocalApiSessionService sessions = new(composition.Authentication, store);
            LocalApiSession session = RequireValue(sessions.LoginAsync(new LoginRequest("admin", "StrongAdmin123!"))
                .GetAwaiter().GetResult(), "login");
            Require(session.Permissions.Contains(SalrynPermissions.SalesVoid), "Admin session must carry Sales.Void.");

            var categories = RequireValue(composition.CatalogReadRepository.GetCategoriesAsync().GetAwaiter().GetResult(), "categories");
            var brands = RequireValue(composition.CatalogReadRepository.GetBrandsAsync().GetAwaiter().GetResult(), "brands");
            var units = RequireValue(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            Product product = new()
            {
                Id = Guid.NewGuid(),
                Sku = "POS-M7-VOID",
                Barcode = RequireValue(Barcode.Create("9790000000007"), "barcode"),
                Name = "POS-M7 Void Product",
                CategoryId = categories[0].Id,
                BrandId = brands[0].Id,
                UnitId = units[0].Id,
                CostPrice = Money.FromCents(1000),
                SellingPrice = Money.FromCents(1500),
                StockOnHand = RequireValue(Quantity.FromX1000(5000), "initial quantity"),
                ReorderLevel = Quantity.Zero,
                TrackStock = true,
                IsActive = true
            };
            RequireSuccess(composition.ProductWriteRepository.AddAsync(product).GetAwaiter().GetResult(), "product");

            Guid saleId = Guid.NewGuid();
            Quantity sold = RequireValue(Quantity.FromX1000(2000), "sold quantity");
            const string snapshot = "POS-M7 immutable receipt snapshot";
            Sale sale = new()
            {
                Id = saleId,
                ReceiptNumber = "POS-M7-000001",
                CashierUserId = session.UserId,
                Subtotal = Money.FromCents(3000),
                Total = Money.FromCents(3000),
                AmountPaid = Money.FromCents(3000),
                Change = Money.Zero,
                Status = SaleStatus.Completed,
                ReceiptSnapshot = snapshot
            };
            SaleItem item = new()
            {
                SaleId = saleId,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                BarcodeSnapshot = product.Barcode?.Value,
                UnitNameSnapshot = units[0].Name,
                Quantity = sold,
                UnitPrice = Money.FromCents(1500),
                CostPrice = product.CostPrice,
                LineTotal = Money.FromCents(3000)
            };
            Payment payment = new() { SaleId = saleId, Amount = Money.FromCents(3000) };
            StockMovement saleMovement = new()
            {
                ProductId = product.Id,
                MovementType = StockMovementType.Sale,
                QuantityChange = sold,
                CostPrice = product.CostPrice,
                ReferenceType = "SALE",
                ReferenceId = saleId,
                Reason = "POS-M7 sale",
                UserId = session.UserId
            };
            RequireSuccess(composition.SaleWriteRepository.AddCompletedSaleAsync(
                sale, new[] { item }, new[] { payment }, new[] { saleMovement }).GetAwaiter().GetResult(), "completed sale");

            Require(GetScalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", product.Id) == 3000, "Sale must reduce stock after commit.");
            long paymentCount = GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", saleId);
            long saleCount = GetScalar(composition, "SELECT COUNT(*) FROM sales WHERE id=$id", saleId);

            SaleVoidApiService service = new(composition);
            Result<SaleVoidResponse> missingReason = service.VoidAsync(saleId, new SaleVoidRequest(" "), session).GetAwaiter().GetResult();
            Require(missingReason.IsFailure && missingReason.ErrorDetail.Code == "Sales.VoidReasonRequired", "Void without reason must fail.");

            SaleVoidResponse response = RequireValue(service.VoidAsync(
                saleId, new SaleVoidRequest("Customer cancelled completed sale"), session).GetAwaiter().GetResult(), "void");
            Require(response.Sale.Status == "Voided", "Void must mark sale voided.");
            Require(response.ReversalMovementCount == 1, "Tracked sale movement must create one reversal.");
            Require(response.Sale.ReceiptSnapshot == snapshot, "Void must preserve receipt snapshot.");
            Require(GetScalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", product.Id) == 5000, "Void must restore stock.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM stock_movements WHERE reference_id=$id AND movement_type=2", saleId) == 1, "Void must persist one VoidSale movement.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM payments WHERE sale_id=$id", saleId) == paymentCount, "Void must not duplicate payment.");
            Require(GetScalar(composition, "SELECT COUNT(*) FROM sales WHERE id=$id", saleId) == saleCount, "Void must not duplicate sale.");

            Result<SaleVoidResponse> duplicate = service.VoidAsync(
                saleId, new SaleVoidRequest("Second void"), session).GetAwaiter().GetResult();
            Require(duplicate.IsFailure && duplicate.ErrorDetail.Code == "Sales.NotVoidable", "Double void must fail.");
            Require(GetScalar(composition, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", product.Id) == 5000, "Failed double void must not change stock.");

            RunSourceGuards();
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static long GetScalar(SalrynLocalComposition composition, string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={composition.DbPaths.DatabasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void RunSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string endpoints = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.Api.Local", "LocalSaleVoidEndpoints.cs"));
        string page = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "pages", "SalesHistoryPage.tsx"));
        Require(endpoints.Contains("LocalApiPermissions.SalesVoid", StringComparison.Ordinal), "Void endpoint must require Sales.Void.");
        Require(page.Contains("Reprint blocked: VOIDED", StringComparison.Ordinal), "Voided sale reprint behavior must be explicit.");
        Require(page.Contains("Void reason", StringComparison.Ordinal), "Void UI must require a reason.");
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

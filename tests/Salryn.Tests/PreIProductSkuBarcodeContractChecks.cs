using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// PRE-I proof for product identity, active-barcode uniqueness, optional blank barcodes,
/// truthful SKU deferral, and deterministic stock/checkout barcode lookup.
/// </summary>
public static class PreIProductSkuBarcodeContractChecks
{
    private const string DuplicateMessage = "Barcode is already assigned to another active product.";

    public static void Run()
    {
        string root = FindRoot();
        AssertSourceContracts(root);
        RunBehavioralContract();
    }

    private static void AssertSourceContracts(string root)
    {
        string schema = Read(root, "src", "Salryn.Infrastructure", "Data", "Migrations", "InitialSchemaMigration.cs");
        string contracts = Read(root, "src", "Salryn.Api.Local", "Contracts", "ProductManagementContracts.cs");
        string service = Read(root, "src", "Salryn.Api.Local", "Services", "ProductManagementApiService.cs");
        string repository = Read(root, "src", "Salryn.Infrastructure", "Data", "Repositories", "Products", "SqliteProductWriteRepository.cs");
        string lookup = Read(root, "src", "Salryn.Infrastructure", "Data", "Repositories", "Products", "SqliteProductReadRepository.cs");
        string products = Read(root, "src", "Salryn.App.Web", "src", "pages", "ProductsPage.tsx");
        string inventory = Read(root, "src", "Salryn.App.Web", "src", "pages", "InventoryPage.tsx");
        string checkout = Read(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(schema, "CREATE UNIQUE INDEX idx_products_barcode_active", "Database active-barcode uniqueness index.");
        RequireContains(schema, "WHERE barcode IS NOT NULL AND is_active = 1", "Filtered active-barcode policy.");
        RequireContains(contracts, "string? Barcode", "Product write barcode must be optional.");
        RequireNotContains(contracts, "Sku", "Product write contract must not claim SKU assignment support.");
        RequireContains(service, "string.IsNullOrWhiteSpace(request.Barcode)", "Blank barcode validation path.");
        RequireContains(service, DuplicateMessage, "Friendly service duplicate message.");
        RequireContains(repository, DuplicateMessage, "Database race duplicate message.");
        RequireContains(lookup, "WHERE p.barcode = $barcode AND p.is_active = 1", "Exact active checkout lookup.");
        RequireContains(lookup, "LIMIT 1", "Deterministic exact lookup boundary.");

        RequireContains(products, "One barcode identifies one sellable item or package.", "Operator barcode policy.");
        RequireContains(products, "500 ml item, a 1.5 L", "Separate package-size records.");
        RequireContains(products, "separately sold 6-pack", "Separate bundle record.");
        RequireContains(products, "Barcode (optional)", "Blank barcode behavior is visible.");
        RequireContains(products, "Leave blank when the product has no printed barcode.", "Blank barcode guidance.");
        RequireContains(products, "Internal SKU/code creation and editing are deferred in this beta.", "SKU support truth.");
        RequireContains(products, "Existing SKU (read-only)", "Legacy SKU display truth.");
        RequireNotContains(products, "Search SKU, barcode", "Catalog search must not advertise unsupported SKU workflow.");
        RequireContains(products, "max-h-[36rem]", "Catalog results retain a bounded list.");
        RequireNotContains(products, "h-[calc(100vh-9rem)]", "Product profile must use natural page flow.");
        RequireOrder(products, "Current stock", "More details", "Core profile fields precede secondary details.");

        RequireContains(inventory, "Scan or enter the product barcode to select the product, then record the quantity movement.", "Stock barcode/quantity separation.");
        RequireContains(inventory, "No active product was found for that barcode.", "Stock not-found copy.");
        RequireNotContains(inventory, "barcode/SKU", "Stock lookup must not advertise unsupported SKU workflow.");
        RequireContains(checkout, "Barcode identifies the sellable item or package.", "Checkout barcode identity help.");
        RequireContains(checkout, "fetchProducts({ search: barcode, isActive: true", "Checkout exact active lookup request.");
        RequireNotContains(checkout, "Try SKU", "Checkout must not advertise unsupported SKU workflow.");
        RequireContains(coreSmoke, "PreIProductSkuBarcodeContractChecks.Run();", "Full CoreSmoke registration.");
        RequireContains(coreSmoke, "pre-i-product-sku-barcode-contract", "Focused CoreSmoke registration.");
    }

    private static void RunBehavioralContract()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pre-i-barcode-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            RequireTrue(
                composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                    "Barcode Store", "Barcode Trading", "1 Barcode St", "09170000000", null, 80,
                    "admin", "Barcode Admin", "StrongAdmin123!")).IsSuccess,
                "Bootstrap admin.");

            var units = RequireValue(composition.CatalogReadRepository.GetUnitsAsync().GetAwaiter().GetResult(), "units");
            ProductManagementApiService products = new(composition);
            Guid unitId = units[0].Id;

            var first = RequireValue(products.CreateAsync(Request("500 ml Item", "8800000000001", unitId)).GetAwaiter().GetResult(), "first product");
            Result<ProductManagementWriteResponse> duplicateCreate = products.CreateAsync(Request("Duplicate", "8800000000001", unitId)).GetAwaiter().GetResult();
            RequireDuplicate(duplicateCreate, "duplicate create");

            var second = RequireValue(products.CreateAsync(Request("1.5 L Item", "8800000000002", unitId)).GetAwaiter().GetResult(), "second product");
            Result<ProductManagementWriteResponse> duplicateUpdate = products.UpdateAsync(second.Product.Id, Request("1.5 L Item", "8800000000001", unitId)).GetAwaiter().GetResult();
            RequireDuplicate(duplicateUpdate, "duplicate update");

            var blank = RequireValue(products.CreateAsync(Request("No Printed Barcode", "   ", unitId)).GetAwaiter().GetResult(), "blank barcode product");
            RequireTrue(blank.Product.Barcode is null, "Whitespace barcode must persist as null.");

            InMemoryLocalApiSessionStore store = new();
            LocalApiSessionService sessions = new(composition.Authentication, store);
            LocalApiSession admin = RequireValue(sessions.LoginAsync(new LoginRequest("admin", "StrongAdmin123!")).GetAwaiter().GetResult(), "login");
            RequireTrue(
                products.DeactivateAsync(first.Product.Id, new ProductDeactivateRequest("replace inactive package record"), admin).GetAwaiter().GetResult().IsSuccess,
                "Deactivate original barcode owner.");

            var replacement = RequireValue(products.CreateAsync(Request("500 ml Item Replacement", "8800000000001", unitId)).GetAwaiter().GetResult(), "reuse inactive barcode");
            Barcode barcode = RequireValue(Barcode.Create("8800000000001"), "barcode value");
            var exact = RequireValue(composition.ProductReadRepository.FindByBarcodeAsync(barcode).GetAwaiter().GetResult(), "exact barcode lookup");
            RequireTrue(exact is not null && exact.Id == replacement.Product.Id, "Exact lookup must return the sole active barcode owner.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ProductManagementWriteRequest Request(string name, string? barcode, Guid unitId) =>
        new(name, barcode, unitId, null, null, 25m, 10m, 0m);

    private static void RequireDuplicate(Result<ProductManagementWriteResponse> result, string label)
    {
        RequireTrue(result.IsFailure, label + " must fail.");
        RequireTrue(result.ErrorDetail.Code == "Products.DuplicateBarcode", label + " code.");
        RequireTrue(result.ErrorDetail.Message == DuplicateMessage, label + " friendly message.");
    }

    private static T RequireValue<T>(Result<T> result, string label)
    {
        if (result.IsFailure) throw new InvalidOperationException(label + ": " + result.ErrorDetail.Message);
        return result.Value;
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireOrder(string source, string first, string second, string message)
    {
        int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        if (firstIndex < 0 || secondIndex <= firstIndex) throw new InvalidOperationException(message);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }
}

namespace Salryn.Tests;

public static class PosM4FastAddUnknownBarcodeInventoryChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string repo = repoRoot.FullName;
        string webSrc = Path.Combine(repo, "src", "Salryn.App.Web", "src");
        string apiLocal = Path.Combine(repo, "src", "Salryn.Api.Local");

        string checkoutPage = ReadRequiredFile(webSrc, "pages", "CheckoutPage.tsx");
        string productsApi = ReadRequiredFile(webSrc, "api", "products.ts");
        string fastAddClient = ReadRequiredFile(webSrc, "api", "fastAddProduct.ts");
        string receiptPrintApi = ReadRequiredFile(webSrc, "api", "receiptPrint.ts");
        string fastAddContracts = ReadRequiredFile(apiLocal, "Contracts", "FastAddProductContracts.cs");
        string fastAddService = ReadRequiredFile(apiLocal, "Services", "FastAddProductApiService.cs");
        string fastAddEndpoints = ReadRequiredFile(apiLocal, "LocalFastAddProductEndpoints.cs");
        string atomicFastAdd = ReadRequiredFile(repo, "src", "Salryn.Infrastructure", "Data", "Repositories", "Products", "SqliteFastAddProductWriteRepository.cs");
        string appFactory = ReadRequiredFile(apiLocal, "LocalApiApplicationFactory.cs");
        string composition = ReadRequiredFile(repo, "src", "Salryn.Composition.Local", "SalrynLocalComposition.cs");
        string readOnlyEndpoints = ReadRequiredFile(apiLocal, "ReadOnlyEndpointExtensions.cs");
        string coreSmoke = ReadRequiredFile(repo, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(coreSmoke, "PosM4FastAddUnknownBarcodeInventoryChecks.Run();", "CoreSmoke must run POS-M4 checks.");
        RequireContains(coreSmoke, "pos-m4-fast-add-unknown-barcode-inventory", "CoreSmoke must expose targeted POS-M4 check.");

        RequireContains(composition, "IProductWriteRepository ProductWriteRepository", "Composition must expose product write repository.");
        RequireContains(composition, "IInventoryWriteRepository InventoryWriteRepository", "Composition must expose inventory write repository.");
        RequireContains(composition, "repositories.CreateProductWriteRepository()", "Composition must wire product write repository.");
        RequireContains(composition, "repositories.CreateInventoryWriteRepository()", "Composition must wire inventory write repository.");

        RequireContains(fastAddContracts, "FastAddProductRequest", "Fast Add request contract must exist.");
        RequireContains(fastAddContracts, "ProductName", "Fast Add must require product name input.");
        RequireContains(fastAddContracts, "Barcode", "Fast Add must require barcode input.");
        RequireContains(fastAddContracts, "UnitId", "Fast Add must require unit input.");
        RequireContains(fastAddContracts, "SellingPrice", "Fast Add must require selling price input.");
        RequireContains(fastAddContracts, "InitialStockQuantity", "Fast Add must require initial stock quantity input.");

        RequireContains(fastAddEndpoints, "/api/products/fast-add", "Fast Add endpoint must be mapped under products fast-add.");
        RequireContains(fastAddEndpoints, "LocalApiPermissions.SalesCreate", "Fast Add must require sales-create permission.");
        RequireContains(fastAddEndpoints, "LocalApiPermissions.ProductsView", "Fast Add must require product permission boundary.");
        RequireContains(fastAddEndpoints, "LocalApiPermissions.ProductsFastAdd", "Fast Add must require narrow Products.FastAdd permission.");
        RequireNotContains(fastAddEndpoints, "LocalApiPermissions.InventoryView", "Fast Add must not demand broad Inventory.View.");
        RequireContains(fastAddEndpoints, "StatusCodes.Status500InternalServerError", "Unexpected failure must be typed HTTP 500.");
        RequireContains(fastAddEndpoints, "StatusCodes.Status409Conflict", "Duplicate barcode must return conflict status.");
        RequireContains(appFactory, "AddSingleton<FastAddProductApiService>()", "Fast Add service must be registered.");
        RequireContains(appFactory, "MapLocalFastAddProductEndpoints()", "Fast Add endpoints must be mapped.");

        RequireContains(fastAddService, "FastAdd.ProductNameRequired", "Fast Add must validate product name.");
        RequireContains(fastAddService, "FastAdd.BarcodeRequired", "Fast Add must validate barcode.");
        RequireContains(fastAddService, "FastAdd.UnitRequired", "Fast Add must validate unit.");
        RequireContains(fastAddService, "FastAdd.SellingPriceRequired", "Fast Add must validate selling price greater than zero.");
        RequireContains(fastAddService, "FastAdd.InitialStockQuantityInvalid", "Fast Add must validate initial stock quantity.");
        RequireContains(fastAddService, "SqliteFastAddProductWriteRepository", "Fast Add must use a dedicated atomic write repository.");
        RequireContains(fastAddService, "StockOnHand = Quantity.Zero", "Product starts at zero in the atomic write transaction.");
        RequireContains(fastAddService, "session.UserId", "Operator identity must be passed to atomic write.");
        RequireContains(atomicFastAdd, "Products.DuplicateBarcode", "Unique active barcode must conflict.");
        RequireContains(atomicFastAdd, "StockMovementType.StockIn", "Positive initial stock must produce one stock-in movement.");
        RequireContains(atomicFastAdd, "StockBucketSyncHelper.SyncAvailableStockBucket", "Initial stock must be synchronized in MAIN-STORE.");
        RequireContains(atomicFastAdd, "transaction.Commit();", "Commit must occur after all mutation and audit writes.");
        RequireContains(atomicFastAdd, "PRODUCT_CREATED", "Zero-stock and positive-stock creation must both be audited.");
        RequireContains(atomicFastAdd, "INSERT INTO audit_logs", "Creation audit must be inside transaction.");
        RequireContains(atomicFastAdd, "ReferenceType", "Stock-in movement must be traceable.");

        RequireContains(readOnlyEndpoints, "/api/products/catalog/units", "Checkout must have unit catalog read endpoint.");
        RequireContains(productsApi, "fetchProductUnits", "Product API client must expose units read path.");
        RequireNotContains(productsApi, "method: \"POST\"", "Products read API client must remain read-only.");
        RequireContains(fastAddClient, "/api/products/fast-add", "Fast Add POST client must be isolated.");
        RequireContains(fastAddClient, "method: \"POST\"", "Fast Add client must call controlled POST endpoint.");
        RequireContains(receiptPrintApi, "method: \"POST\"", "POS-M2 print client must remain isolated outside read-only sales API.");

        RequireContains(checkoutPage, "openFastAddForBarcode(barcode)", "Unknown barcode must open Fast Add flow.");
        RequireContains(checkoutPage, "Fast Add Unknown Barcode", "Fast Add modal must be visible in checkout.");
        RequireContains(checkoutPage, "value={fastAddBarcode}", "Fast Add barcode field must preserve unknown barcode.");
        RequireContains(checkoutPage, "readOnly", "Fast Add barcode must be locked or clearly preserved.");
        RequireContains(checkoutPage, "Product name is required.", "Fast Add UI must validate product name.");
        RequireContains(checkoutPage, "Selling price must be greater than zero.", "Fast Add UI must validate selling price.");
        RequireContains(checkoutPage, "Initial stock quantity cannot be negative.", "Fast Add UI must validate initial stock quantity.");
        RequireContains(checkoutPage, "fastAddUnknownBarcode", "Checkout must call isolated Fast Add API client.");
        RequireContains(checkoutPage, "addProduct(result.product)", "Successful Fast Add must add returned product to cart.");
        RequireContains(checkoutPage, "Duplicate barcode", "Checkout must surface duplicate barcode errors clearly.");
        RequireContains(checkoutPage, "describeFastAddBackendError(error)", "Fast Add permission failures must not claim cashier checkout is forbidden.");
        RequireContains(checkoutPage, "Stock changes only when checkout completes", "Fast Add must not claim sale stock reduction before checkout.");
        RequireContains(checkoutPage, "submitCheckout", "Checkout submit path must remain available.");
        RequireContains(checkoutPage, "fileReceiptStatus", "POS-M1 receipt file behavior must remain wired.");
        RequireContains(checkoutPage, "printReceiptForSale", "POS-M2 print behavior must remain wired.");
        RequireContains(checkoutPage, "exactBarcodeMatch", "POS-M3 exact barcode add must remain wired.");
        RequireContains(checkoutPage, "addProduct(match)", "POS-M3 existing barcode add must still reuse cart add.");

        RequireNotContains(checkoutPage, "method: \"POST\"", "Checkout must not perform direct POST calls.");
        RequireNotContains(checkoutPage, "Official", "POS-M4 must not add official/RMO/BIR UI claims.");
        RequireNotContains(checkoutPage, "Void Sale", "POS-M4 must not add void/refund behavior.");
        PilotCashierFastAddAtomicityChecks.Run();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;
        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

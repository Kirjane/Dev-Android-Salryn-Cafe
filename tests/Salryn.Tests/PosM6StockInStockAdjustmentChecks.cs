namespace Salryn.Tests;

public static class PosM6StockInStockAdjustmentChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string repo = repoRoot.FullName;
        string webSrc = Path.Combine(repo, "src", "Salryn.App.Web", "src");
        string apiLocal = Path.Combine(repo, "src", "Salryn.Api.Local");

        string inventoryPage = ReadRequiredFile(webSrc, "pages", "InventoryPage.tsx");
        string inventoryApi = ReadRequiredFile(webSrc, "api", "inventory.ts");
        string checkoutPage = ReadRequiredFile(webSrc, "pages", "CheckoutPage.tsx");
        string productsPage = ReadRequiredFile(webSrc, "pages", "ProductsPage.tsx");
        string contracts = ReadRequiredFile(apiLocal, "Contracts", "InventoryManagementContracts.cs");
        string service = ReadRequiredFile(apiLocal, "Services", "InventoryManagementApiService.cs");
        string endpoints = ReadRequiredFile(apiLocal, "LocalInventoryManagementEndpoints.cs");
        string fastAddEndpoints = ReadRequiredFile(apiLocal, "LocalFastAddProductEndpoints.cs");
        string permissionGuards = ReadRequiredFile(apiLocal, "Auth", "LocalApiPermissionGuards.cs");
        string writeRepo = ReadRequiredFile(repo, "src", "Salryn.Infrastructure", "Data", "Repositories", "Inventory", "SqliteInventoryWriteRepository.cs");
        string coreSmoke = ReadRequiredFile(repo, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(coreSmoke, "PosM6StockInStockAdjustmentChecks.Run();", "CoreSmoke must run POS-M6 checks.");
        RequireContains(coreSmoke, "pos-m6-stock-in-stock-adjustment", "CoreSmoke must expose targeted POS-M6 check.");

        RequireContains(permissionGuards, "InventoryAdjust", "Local API must expose Inventory.Adjust permission constant.");
        RequireContains(fastAddEndpoints, "MapLocalInventoryManagementEndpoints", "Local API must map POS-M6 inventory endpoints from an existing factory-mapped seam.");

        RequireContains(contracts, "StockInRequest", "POS-M6 must define Stock-In request contract.");
        RequireContains(contracts, "StockAdjustmentRequest", "POS-M6 must define Stock Adjustment request contract.");
        RequireContains(contracts, "StockWriteResponse", "POS-M6 must define stock write response contract.");

        RequireContains(endpoints, "/api/inventory/stock-in", "Stock-In endpoint must exist.");
        RequireContains(endpoints, "/api/inventory/adjustment", "Stock Adjustment endpoint must exist.");
        RequireContains(endpoints, "LocalApiPermissions.InventoryAdjust", "Stock writes must require Inventory.Adjust.");
        RequireContains(endpoints, "LocalApiPermissions.InventoryView", "Stock writes must preserve inventory view permission boundary.");
        RequireContains(endpoints, "LocalApiPermissions.ProductsView", "Stock writes must preserve product lookup permission boundary.");

        RequireContains(service, "StockInAsync", "Stock-In service method must exist.");
        RequireContains(service, "AdjustAsync", "Stock Adjustment service method must exist.");
        RequireContains(service, "StockMovementType.StockIn", "Stock-In must create StockIn movement.");
        RequireContains(service, "StockMovementType.AdjustmentAdd", "Adjustment must support add delta movement.");
        RequireContains(service, "StockMovementType.AdjustmentRemove", "Adjustment must support remove delta movement.");
        RequireContains(service, "InventoryWriteRepository", "POS-M6 stock writes must use inventory write repository.");
        RequireContains(service, "FindDetailByIdAsync", "POS-M6 must read product state before and after stock write.");
        RequireContains(service, "Inventory.QuantityRequired", "Stock write service must validate quantity.");
        RequireContains(service, "Inventory.ReasonRequired", "Stock write service must require explicit reason.");
        RequireContains(service, "ProductNotFoundOrInactive", "Stock write service must reject inactive product writes.");
        RequireContains(service, "NegativeStockNotAllowed", "Stock adjustment remove must not allow negative stock.");
        RequireContains(service, "No sale, payment, or receipt was created", "Stock write response must make no sale/payment/receipt side effects explicit.");
        RequireNotContains(service, "SaleWriteRepository", "POS-M6 stock service must not create or mutate sales.");
        RequireNotContains(service, "ReceiptSnapshot", "POS-M6 stock service must not touch receipt snapshots.");

        RequireContains(writeRepo, "InsertStockMovement", "Inventory repository must ledger every stock movement.");
        RequireContains(writeRepo, "StockMovementType.StockIn", "Inventory repository must support StockIn movement.");
        RequireContains(writeRepo, "StockMovementType.AdjustmentAdd", "Inventory repository must support AdjustmentAdd movement.");
        RequireContains(writeRepo, "StockMovementType.AdjustmentRemove", "Inventory repository must support AdjustmentRemove movement.");
        RequireContains(writeRepo, "ProductNotFoundOrInactive", "Inventory repository must reject inactive products.");

        RequireContains(inventoryApi, "submitStockIn", "Web inventory API must expose Stock-In write client.");
        RequireContains(inventoryApi, "submitStockAdjustment", "Web inventory API must expose Stock Adjustment write client.");
        RequireContains(inventoryApi, "method: \"POST\"", "Inventory write API client must use controlled POST endpoints.");

        RequireContains(inventoryPage, "Stock-In", "Stock Movements UI must expose Stock-In flow.");
        RequireContains(inventoryPage, "Stock Adjustment", "Stock Movements UI must expose Stock Adjustment flow.");
        RequireContains(inventoryPage, "Stock-In quantity must be greater than zero.", "Stock-In UI must validate positive quantity.");
        RequireContains(inventoryPage, "Adjustment reason is required.", "Adjustment UI must require reason.");
        RequireContains(inventoryPage, "AdjustmentAdd", "Movement ledger filter must show AdjustmentAdd.");
        RequireContains(inventoryPage, "AdjustmentRemove", "Movement ledger filter must show AdjustmentRemove.");
        RequireContains(inventoryPage, "fetchProducts", "Stock write UI must use existing product lookup.");
        RequireContains(inventoryPage, "isActive: true", "Stock write product lookup must exclude inactive products.");
        RequireContains(inventoryPage, "does not create sales, payments, receipts", "Stock UI must explain side-effect boundary.");

        RequireContains(productsPage, "Product Catalog", "POS-M5 Product Catalog must remain available.");
        RequireContains(productsPage, "Product writes do not change inventory quantity", "Product edit stock boundary must remain documented.");
        RequireContains(checkoutPage, "submitCheckout", "Checkout submit path must remain wired.");
        RequireContains(checkoutPage, "exactBarcodeMatch", "POS-M3 barcode checkout path must remain wired.");
        RequireContains(checkoutPage, "fastAddUnknownBarcode", "POS-M4 Fast Add path must remain wired.");
        RequireContains(checkoutPage, "fileReceiptStatus", "POS-M1 receipt file status must remain wired.");
        RequireContains(checkoutPage, "printReceiptForSale", "POS-M2 printer path must remain wired.");
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

namespace Salryn.Tests;

public static class PosM5ProductWriteManagementChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string repo = repoRoot.FullName;
        string apiLocal = Path.Combine(repo, "src", "Salryn.Api.Local");
        string webSrc = Path.Combine(repo, "src", "Salryn.App.Web", "src");

        string permissions = ReadRequiredFile(apiLocal, "Auth", "LocalApiPermissionGuards.cs");
        string contracts = ReadRequiredFile(apiLocal, "Contracts", "ProductManagementContracts.cs");
        string service = ReadRequiredFile(apiLocal, "Services", "ProductManagementApiService.cs");
        string endpoints = ReadRequiredFile(apiLocal, "LocalProductManagementEndpoints.cs");
        string appFactory = ReadRequiredFile(apiLocal, "LocalApiApplicationFactory.cs");
        string readContracts = ReadRequiredFile(apiLocal, "Contracts", "ReadOnlyContracts.cs");
        string mapper = ReadRequiredFile(apiLocal, "Services", "ReadModelMapper.cs");
        string productsApi = ReadRequiredFile(webSrc, "api", "products.ts");
        string productManagementApi = ReadRequiredFile(webSrc, "api", "productManagement.ts");
        string fastAddApi = ReadRequiredFile(webSrc, "api", "fastAddProduct.ts");
        string checkoutPage = ReadRequiredFile(webSrc, "pages", "CheckoutPage.tsx");
        string productsPage = ReadRequiredFile(webSrc, "pages", "ProductsPage.tsx");
        string coreSmoke = ReadRequiredFile(repo, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(coreSmoke, "PosM5ProductWriteManagementChecks.Run();", "CoreSmoke must run POS-M5 product write checks.");
        RequireContains(coreSmoke, "pos-m5-product-write-management", "CoreSmoke must expose targeted POS-M5 check.");

        RequireContains(permissions, "ProductsManage = \"Products.Manage\"", "Local API must expose Products.Manage permission.");
        RequireContains(contracts, "ProductManagementWriteRequest", "Product write request contract must exist.");
        RequireContains(contracts, "ProductDeactivateRequest", "Product deactivate request contract must exist.");
        RequireContains(endpoints, "/api/products/manage", "Product create endpoint must exist.");
        RequireContains(endpoints, "/api/products/{id:guid}/manage", "Product edit endpoint must exist.");
        RequireContains(endpoints, "/api/products/{id:guid}/deactivate", "Product deactivate endpoint must exist.");
        RequireContains(endpoints, "LocalApiPermissions.ProductsManage", "Product writes must require Products.Manage.");
        RequireContains(endpoints, "LocalApiPermissions.ProductsView", "Product writes must also preserve product view permission boundary.");
        RequireContains(appFactory, "AddSingleton<ProductManagementApiService>()", "Product management service must be registered.");
        RequireContains(appFactory, "MapLocalProductManagementEndpoints()", "Product management endpoints must be mapped.");

        RequireContains(service, "CreateAsync", "Product management service must create products.");
        RequireContains(service, "UpdateAsync", "Product management service must edit active products.");
        RequireContains(service, "DeactivateAsync", "Product management service must deactivate products.");
        RequireContains(service, "ProductWriteRepository", "Product management must use ProductWriteRepository.");
        RequireContains(service, "Products.DuplicateBarcode", "Duplicate active barcode must be blocked.");
        RequireContains(service, "Products.SellingPriceRequired", "Selling price validation must exist.");
        RequireContains(service, "Products.ReorderLevelInvalid", "Reorder level validation must exist.");
        RequireContains(service, "StockOnHand = Quantity.Zero", "Normal product create must not mutate inventory quantity.");
        RequireContains(service, "Inventory quantity was not changed", "Product create response must document no inventory mutation.");
        RequireContains(service, "Historical sale and receipt snapshots remain unchanged", "Product edit must preserve historical snapshot boundary.");
        RequireNotContains(service, "InventoryWriteRepository", "POS-M5 normal product management must not write inventory.");
        RequireNotContains(service, "StockMovementType.StockIn", "POS-M5 normal product create must not create StockIn movements.");

        RequireContains(readContracts, "Guid? CategoryId", "Product detail response must expose category ID for safe edits.");
        RequireContains(readContracts, "Guid? BrandId", "Product detail response must expose brand ID for safe edits.");
        RequireContains(readContracts, "Guid UnitId", "Product detail response must expose unit ID for safe edits.");
        RequireContains(mapper, "model.CategoryId", "Read model mapper must map category ID.");
        RequireContains(mapper, "model.BrandId", "Read model mapper must map brand ID.");
        RequireContains(mapper, "model.UnitId", "Read model mapper must map unit ID.");

        RequireContains(productsApi, "categoryId: string | null;", "Web product detail must expose category ID.");
        RequireContains(productsApi, "unitId: string;", "Web product detail must expose unit ID.");
        RequireNotContains(productsApi, "method: \"POST\"", "Read-only products API client must remain read-only.");
        RequireContains(productManagementApi, "createManagedProduct", "Focused product write client must create products.");
        RequireContains(productManagementApi, "updateManagedProduct", "Focused product write client must edit products.");
        RequireContains(productManagementApi, "deactivateManagedProduct", "Focused product write client must deactivate products.");
        RequireContains(productManagementApi, "method: \"POST\"", "Product write client must use controlled POST endpoints.");
        RequireContains(productManagementApi, "method: \"PUT\"", "Product write client must use controlled PUT endpoint for edit.");

        RequireContains(productsPage, "Create Product", "Product Catalog must expose create flow.");
        RequireContains(productsPage, "Edit Product", "Product Catalog must expose edit flow.");
        RequireContains(productsPage, "Deactivate Product", "Product Catalog must expose deactivate flow.");
        RequireContains(productsPage, "Product name is required.", "Product Catalog must validate required name.");
        RequireContains(productsPage, "Barcode is required.", "Product Catalog must validate required barcode.");
        RequireContains(productsPage, "Unit is required.", "Product Catalog must validate required unit.");
        RequireContains(productsPage, "Selling price must be greater than zero.", "Product Catalog must validate price.");
        RequireContains(productsPage, "Duplicate barcode", "Product Catalog must surface duplicate barcode failures.");
        RequireContains(productsPage, "Reactivation unavailable", "Reactivate must remain unavailable when repository support is absent.");
        RequireContains(productsPage, "Product writes do not change inventory quantity", "Product create/edit/deactivate must remain stock-neutral even when POS-M6 stock actions are visible.");
        RequireContains(productsPage, "never set stock quantity", "Product create/edit UI must explain that product writes do not directly set stock quantity.");
        RequireContains(productsPage, "Existing sales and receipt snapshots stay immutable", "Product Catalog must state snapshot boundary.");

        RequireContains(checkoutPage, "fetchProducts({ search: barcode, isActive: true", "Checkout barcode lookup must ignore inactive products.");
        RequireContains(checkoutPage, "addProduct(match)", "POS-M3 exact barcode add must remain wired.");
        RequireContains(fastAddApi, "/api/products/fast-add", "POS-M4 Fast Add client must remain wired.");
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

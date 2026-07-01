using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Products;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Paths;

internal static class DevelopmentPreLaunchSeedChecks
{
    private const string DevUsername = "admin";
    private const string DevPassword = "StrongAdmin123!";

    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== DEVELOPMENT PRE-LAUNCH SEED =====");

        string devRoot = Path.GetFullPath(Path.Combine(projectRoot, ".salryn-dev"));

        ResetDevelopmentRoot(devRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SalrynAppServices services = SalrynAppServices.Create(paths);

        RequireSuccess(
            services.DatabaseInitialization.InitializeDatabase(),
            "DevPreLaunch.InitializeDatabase");

        AuthenticatedUserReadModel admin = RequireSuccess(
            services.Authentication.CreateFirstAdminAsync(
                new FirstRunAdminSetupRequest(
                    DevUsername,
                    "SALRYN Admin",
                    DevPassword,
                    DevPassword)).GetAwaiter().GetResult(),
            "DevPreLaunch.CreateFirstAdmin");

        AuthenticatedUserReadModel login = RequireSuccess(
            services.Authentication.LoginAsync(
                new LoginRequest(
                    DevUsername,
                    DevPassword)).GetAwaiter().GetResult(),
            "DevPreLaunch.VerifyLogin");

        CurrentAppSession appSession = new(
            login.UserId,
            login.RoleId,
            login.Username,
            login.DisplayName,
            login.RoleName,
            login.Status,
            login.Permissions,
            DateTimeOffset.UtcNow);

        services.LocalHostSessionIdentityProvider.SetSession(appSession);

        FirstRunStatusSummary status = ReadFirstRunStatus(services);

        if (!status.IsDatabaseInitialized ||
            status.IsFirstRunRequired ||
            !status.HasUsers ||
            !status.HasStoreSettings)
        {
            throw new InvalidOperationException("DevPreLaunch first-run status verification failed.");
        }

        StoreSettingsReadModel? settings = RequireSuccess(
            services.StoreSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "DevPreLaunch.GetStoreSettings");

        if (settings is null ||
            !string.Equals(settings.CurrencyLabel, "PHP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DevPreLaunch store settings verification failed.");
        }

        ProductManagementPresenter productsPresenter = new(
            services.ProductReadRepository,
            services.ProductWriteRepository,
            services.CatalogReadRepository);

        ProductCreateFormViewModel createForm = productsPresenter
            .LoadCreateFormAsync()
            .GetAwaiter()
            .GetResult();

        if (createForm.HasError)
        {
            throw new InvalidOperationException(
                $"DevPreLaunch.LoadCreateForm failed: {createForm.ErrorCode} / {createForm.ErrorMessage}");
        }

        ProductCatalogOptionViewModel unitOption = createForm.Units.FirstOrDefault(x => x.Id.HasValue)
            ?? throw new InvalidOperationException("DevPreLaunch expected at least one unit.");

        Guid? categoryId = createForm.Categories.FirstOrDefault(x => x.Id.HasValue)?.Id;
        Guid? brandId = createForm.Brands.FirstOrDefault(x => x.Id.HasValue)?.Id;

        DevSeedProduct[] seedProducts =
        {
            new(
                Sku: "DEV-SEED-COFFEE",
                Barcode: "941000000001",
                Name: "Dev Coffee Sachet",
                CostPrice: "10.00",
                SellingPrice: "15.00",
                StockOnHand: "50",
                ExpectedStockX1000: 50000),

            new(
                Sku: "DEV-SEED-WATER",
                Barcode: "941000000002",
                Name: "Dev Bottled Water 500ml",
                CostPrice: "12.00",
                SellingPrice: "20.00",
                StockOnHand: "40",
                ExpectedStockX1000: 40000),

            new(
                Sku: "DEV-SEED-RICE",
                Barcode: "941000000003",
                Name: "Dev Rice 1kg",
                CostPrice: "50.00",
                SellingPrice: "58.00",
                StockOnHand: "25",
                ExpectedStockX1000: 25000)
        };

        foreach (DevSeedProduct product in seedProducts)
        {
            ProductCreateOperationResult createProduct = productsPresenter
                .CreateProductAsync(new ProductCreateRequest(
                    Sku: product.Sku,
                    Barcode: product.Barcode,
                    Name: product.Name,
                    Description: "Seeded for manual development app launch.",
                    CategoryId: categoryId,
                    BrandId: brandId,
                    UnitId: unitOption.Id!.Value,
                    SupplierId: null,
                    CostPrice: product.CostPrice,
                    SellingPrice: product.SellingPrice,
                    WholesalePrice: null,
                    StockOnHand: product.StockOnHand,
                    ReorderLevel: "5",
                    TrackStock: true,
                    AllowDiscount: true,
                    TaxClassification: ProductTaxClassification.Vatable,
                    Notes: "J20-4C dev pre-launch seed product."))
                .GetAwaiter()
                .GetResult();

            if (!createProduct.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"DevPreLaunch.CreateProduct failed for {product.Sku}: {createProduct.ErrorCode} / {createProduct.ErrorMessage}");
            }

            ProductLookupReadModel storedProduct = RequireProductByBarcode(
                services,
                product.Barcode,
                $"DevPreLaunch.Lookup.{product.Sku}");

            if (storedProduct.StockOnHand.ValueX1000 != product.ExpectedStockX1000)
            {
                throw new InvalidOperationException(
                    $"DevPreLaunch expected stock {product.ExpectedStockX1000} for {product.Sku}, got {storedProduct.StockOnHand.ValueX1000}.");
            }

            ProductLookupReadModel? checkoutProduct = RequireSuccess(
                services.CheckoutProductSearch.FindProductAsync(product.Barcode).GetAwaiter().GetResult(),
                $"DevPreLaunch.CheckoutSearch.{product.Sku}");

            if (checkoutProduct is null ||
                checkoutProduct.Id != storedProduct.Id)
            {
                throw new InvalidOperationException($"DevPreLaunch checkout search failed for {product.Sku}.");
            }
        }

        ProductManagementOperationResult productList = productsPresenter
            .SearchAsync("DEV-SEED")
            .GetAwaiter()
            .GetResult();

        if (!productList.IsSuccess ||
            productList.State.Products.Count < seedProducts.Length)
        {
            throw new InvalidOperationException("DevPreLaunch product list verification failed.");
        }

        CashierSessionReadModel? openSession = RequireSuccess(
            services.CashierSessions.FindOpenSessionByUserIdAsync(admin.UserId).GetAwaiter().GetResult(),
            "DevPreLaunch.NoOpenShift");

        if (openSession is not null)
        {
            throw new InvalidOperationException("DevPreLaunch must not leave an open cashier shift.");
        }

        Console.WriteLine($"Development database path: {paths.DatabasePath}");
        Console.WriteLine($"Login username: {DevUsername}");
        Console.WriteLine($"Login password: {DevPassword}");
        Console.WriteLine("Seeded products:");
        foreach (DevSeedProduct product in seedProducts)
        {
            Console.WriteLine($"- {product.Barcode} | {product.Name} | PHP {product.SellingPrice} | stock {product.StockOnHand}");
        }

        Console.WriteLine("Development pre-launch seed succeeded.");
        Console.WriteLine();
    }

    private static FirstRunStatusSummary ReadFirstRunStatus(SalrynAppServices services)
    {
        var status = RequireSuccess(
            services.DatabaseInitialization.CheckStatus(),
            "DevPreLaunch.CheckStatus")
            .FirstRunStatus;

        return new FirstRunStatusSummary(
            status.IsDatabaseInitialized,
            status.IsFirstRunRequired,
            status.HasUsers,
            status.HasStoreSettings);
    }

    private static ProductLookupReadModel RequireProductByBarcode(
        SalrynAppServices services,
        string barcode,
        string label)
    {
        Barcode barcodeValue = RequireValue(
            Barcode.Create(barcode),
            label + ".Barcode");

        ProductLookupReadModel? product = RequireSuccess(
            services.ProductReadRepository.FindByBarcodeAsync(barcodeValue).GetAwaiter().GetResult(),
            label);

        if (product is null)
        {
            throw new InvalidOperationException($"{label} product was not found.");
        }

        return product;
    }

    private static T RequireSuccess<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }

        return result.Value;
    }

    private static T RequireValue<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }

        return result.Value;
    }

    private static void ResetDevelopmentRoot(string devRoot)
    {
        string fullRoot = Path.GetFullPath(devRoot);

        if (!string.Equals(Path.GetFileName(fullRoot), ".salryn-dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to reset non-development root: {fullRoot}");
        }

        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private sealed record DevSeedProduct(
        string Sku,
        string Barcode,
        string Name,
        string CostPrice,
        string SellingPrice,
        string StockOnHand,
        long ExpectedStockX1000);

    private sealed record FirstRunStatusSummary(
        bool IsDatabaseInitialized,
        bool IsFirstRunRequired,
        bool HasUsers,
        bool HasStoreSettings);
}
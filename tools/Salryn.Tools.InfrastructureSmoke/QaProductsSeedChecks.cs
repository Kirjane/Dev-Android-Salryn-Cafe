using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Products;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

internal static class QaProductsSeedChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== QA PRODUCTS SEED =====");

        SalrynAppServices services = SalrynAppServices.CreateDevelopment(projectRoot);

        RequireSuccess(
            services.DatabaseInitialization.InitializeDatabase(),
            "QaProductsSeed.InitializeDatabase");

        var firstRunStatus = RequireSuccess(
                services.DatabaseInitialization.CheckStatus(),
                "QaProductsSeed.CheckStatus")
            .FirstRunStatus;

        if (firstRunStatus.IsFirstRunRequired ||
            !firstRunStatus.HasUsers ||
            !firstRunStatus.HasStoreSettings)
        {
            throw new InvalidOperationException(
                "QA product seed requires manual first-run admin setup first. Launch the app with --dev, create your own admin, close the app, then run qa-products-seed.");
        }

        StoreSettingsReadModel? settings = RequireSuccess(
            services.StoreSettingsReadRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "QaProductsSeed.GetStoreSettings");

        if (settings is null)
        {
            throw new InvalidOperationException("QA product seed requires store settings from manual first-run setup.");
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
                $"QaProductsSeed.LoadCreateForm failed: {createForm.ErrorCode} / {createForm.ErrorMessage}");
        }

        ProductCatalogOptionViewModel unitOption = createForm.Units.FirstOrDefault(x => x.Id.HasValue)
            ?? throw new InvalidOperationException("QA product seed expected at least one unit.");

        Guid? categoryId = createForm.Categories.FirstOrDefault(x => x.Id.HasValue)?.Id;
        Guid? brandId = createForm.Brands.FirstOrDefault(x => x.Id.HasValue)?.Id;

        QaSeedProduct[] seedProducts =
        {
            new(
                Sku: "QA-SEED-COFFEE",
                Barcode: "941000000001",
                Name: "QA Coffee Sachet",
                CostPrice: "10.00",
                SellingPrice: "15.00",
                StockOnHand: "50"),

            new(
                Sku: "QA-SEED-WATER",
                Barcode: "941000000002",
                Name: "QA Bottled Water 500ml",
                CostPrice: "12.00",
                SellingPrice: "20.00",
                StockOnHand: "40"),

            new(
                Sku: "QA-SEED-RICE",
                Barcode: "941000000003",
                Name: "QA Rice 1kg",
                CostPrice: "50.00",
                SellingPrice: "58.00",
                StockOnHand: "25")
        };

        foreach (QaSeedProduct product in seedProducts)
        {
            ProductLookupReadModel? existingProduct = FindProductByBarcode(
                services,
                product.Barcode,
                $"QaProductsSeed.ExistingLookup.{product.Sku}");

            if (existingProduct is not null)
            {
                Console.WriteLine($"Existing: {product.Barcode} | {existingProduct.Name}");
                continue;
            }

            ProductCreateOperationResult createProduct = productsPresenter
                .CreateProductAsync(new ProductCreateRequest(
                    Sku: product.Sku,
                    Barcode: product.Barcode,
                    Name: product.Name,
                    Description: "Seeded for QA checkout testing after manual first-run admin setup.",
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
                    Notes: "QA product seed. Does not create users or passwords."))
                .GetAwaiter()
                .GetResult();

            if (!createProduct.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"QaProductsSeed.CreateProduct failed for {product.Sku}: {createProduct.ErrorCode} / {createProduct.ErrorMessage}");
            }

            ProductLookupReadModel? storedProduct = FindProductByBarcode(
                services,
                product.Barcode,
                $"QaProductsSeed.VerifyLookup.{product.Sku}");

            if (storedProduct is null)
            {
                throw new InvalidOperationException($"QA product seed did not create {product.Sku}.");
            }

            Console.WriteLine($"Seeded: {product.Barcode} | {product.Name} | PHP {product.SellingPrice} | stock {product.StockOnHand}");
        }

        ProductManagementOperationResult productList = productsPresenter
            .SearchAsync("QA-SEED")
            .GetAwaiter()
            .GetResult();

        if (!productList.IsSuccess ||
            productList.State.Products.Count < seedProducts.Length)
        {
            throw new InvalidOperationException("QA product seed product list verification failed.");
        }

        Console.WriteLine("QA products seed succeeded.");
        Console.WriteLine();
    }

    private static ProductLookupReadModel? FindProductByBarcode(
        SalrynAppServices services,
        string barcode,
        string label)
    {
        Barcode barcodeValue = RequireValue(
            Barcode.Create(barcode),
            label + ".Barcode");

        return RequireSuccess(
            services.ProductReadRepository.FindByBarcodeAsync(barcodeValue).GetAwaiter().GetResult(),
            label);
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

    private sealed record QaSeedProduct(
        string Sku,
        string Barcode,
        string Name,
        string CostPrice,
        string SellingPrice,
        string StockOnHand);
}
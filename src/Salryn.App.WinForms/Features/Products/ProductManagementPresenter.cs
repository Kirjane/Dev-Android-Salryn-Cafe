using System.Globalization;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Products;

// Kirch: The presenter owns product screen behavior so WinForms controls stay thin.
public sealed class ProductManagementPresenter
{
    private const int DefaultPageSize = 50;

    private readonly IProductReadRepository _products;
    private readonly IProductWriteRepository? _productWrites;
    private readonly ICatalogReadRepository? _catalogs;
    private ProductManagementViewModel _state = ProductManagementViewModel.Empty();

    public ProductManagementPresenter(IProductReadRepository products)
        : this(products, productWrites: null, catalogs: null)
    {
    }

    public ProductManagementPresenter(
        IProductReadRepository products,
        IProductWriteRepository? productWrites,
        ICatalogReadRepository? catalogs)
    {
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _productWrites = productWrites;
        _catalogs = catalogs;
    }

    public ProductManagementViewModel State => _state;

    public Task<ProductManagementOperationResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(string.Empty, cancellationToken);
    }

    public async Task<ProductManagementOperationResult> SearchAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedSearchText = NormalizeSearchText(searchText);

        _state = _state with
        {
            SearchText = normalizedSearchText,
            IsLoading = true,
            CanSearch = false,
            StatusMessage = "Loading products...",
            HasError = false,
            ErrorCode = null,
            ErrorMessage = null
        };

        ProductSearchFilter filter = new(
            SearchText: normalizedSearchText,
            CategoryId: null,
            BrandId: null,
            IsActive: null,
            IncludeInactive: true,
            Page: PageRequest.FirstPage(DefaultPageSize));

        var result = await _products.SearchAsync(filter, cancellationToken);

        if (result.IsFailure)
        {
            _state = _state with
            {
                IsLoading = false,
                CanSearch = true,
                Products = Array.Empty<ProductManagementRowViewModel>(),
                TotalCount = 0,
                PageNumber = 1,
                PageSize = DefaultPageSize,
                StatusMessage = "Product search failed.",
                HasError = true,
                ErrorCode = result.ErrorDetail.Code,
                ErrorMessage = result.ErrorDetail.Message
            };

            return new ProductManagementOperationResult(
                IsSuccess: false,
                ErrorCode: result.ErrorDetail.Code,
                ErrorMessage: result.ErrorDetail.Message,
                State: _state);
        }

        PagedResult<ProductListItemReadModel> page = result.Value;
        ProductManagementRowViewModel[] rows = page.Items
            .Select(MapRow)
            .ToArray();

        _state = new ProductManagementViewModel(
            SearchText: normalizedSearchText,
            Products: rows,
            TotalCount: page.TotalCount,
            PageNumber: page.PageNumber,
            PageSize: page.PageSize,
            IsLoading: false,
            CanSearch: true,
            StatusMessage: BuildStatusMessage(rows.Length, page.TotalCount),
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);

        return new ProductManagementOperationResult(
            IsSuccess: true,
            ErrorCode: null,
            ErrorMessage: null,
            State: _state);
    }

    // Kirch: This loads dropdown catalogs before the Add Product form allows saving.
    public async Task<ProductCreateFormViewModel> LoadCreateFormAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_catalogs is null)
        {
            return CreateFormFailure(
                "Products.CatalogUnavailable",
                "Catalog services are not available.");
        }

        var unitsResult = await _catalogs.GetUnitsAsync(cancellationToken: cancellationToken);
        var categoriesResult = await _catalogs.GetCategoriesAsync(cancellationToken: cancellationToken);
        var brandsResult = await _catalogs.GetBrandsAsync(cancellationToken: cancellationToken);
        var suppliersResult = await _catalogs.GetSuppliersAsync(cancellationToken: cancellationToken);

        if (unitsResult.IsFailure)
        {
            return CreateFormFailure(unitsResult.ErrorDetail.Code, unitsResult.ErrorDetail.Message);
        }

        if (categoriesResult.IsFailure)
        {
            return CreateFormFailure(categoriesResult.ErrorDetail.Code, categoriesResult.ErrorDetail.Message);
        }

        if (brandsResult.IsFailure)
        {
            return CreateFormFailure(brandsResult.ErrorDetail.Code, brandsResult.ErrorDetail.Message);
        }

        if (suppliersResult.IsFailure)
        {
            return CreateFormFailure(suppliersResult.ErrorDetail.Code, suppliersResult.ErrorDetail.Message);
        }

        if (unitsResult.Value.Count == 0)
        {
            return CreateFormFailure(
                "Products.UnitRequired",
                "At least one active unit is required before adding products.");
        }

        return new ProductCreateFormViewModel(
            Units: unitsResult.Value.Select(MapUnitOption).ToArray(),
            Categories: WithNone(categoriesResult.Value.Select(MapCatalogOption).ToArray()),
            Brands: WithNone(brandsResult.Value.Select(MapCatalogOption).ToArray()),
            Suppliers: WithNone(suppliersResult.Value.Select(MapSupplierOption).ToArray()),
            StatusMessage: "Add product ready.",
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
    }

    // Kirch: This validates raw UI input, builds the Product model, then saves through the write repository.
    public async Task<ProductCreateOperationResult> CreateProductAsync(
        ProductCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_productWrites is null)
        {
            return CreateFailure(
                "Products.WriteUnavailable",
                "Product write services are not available.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return CreateFailure(
                "Products.NameRequired",
                "Product name is required.");
        }

        if (request.UnitId == Guid.Empty)
        {
            return CreateFailure(
                "Products.UnitRequired",
                "Product unit is required.");
        }

        Barcode? barcode = null;

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var barcodeResult = Barcode.Create(request.Barcode);

            if (barcodeResult.IsFailure)
            {
                return CreateFailure(
                    "Products.InvalidBarcode",
                    barcodeResult.ErrorDetail.Message);
            }

            barcode = barcodeResult.Value;
        }

        if (!TryParseMoney(request.SellingPrice, required: true, out Money? sellingPrice, out string? sellingPriceError))
        {
            return CreateFailure(
                "Products.InvalidSellingPrice",
                sellingPriceError ?? "Selling price is invalid.");
        }

        if (!TryParseMoney(request.CostPrice, required: false, out Money? costPrice, out string? costPriceError))
        {
            return CreateFailure(
                "Products.InvalidCostPrice",
                costPriceError ?? "Cost price is invalid.");
        }

        if (!TryParseMoney(request.WholesalePrice, required: false, out Money? wholesalePrice, out string? wholesalePriceError))
        {
            return CreateFailure(
                "Products.InvalidWholesalePrice",
                wholesalePriceError ?? "Wholesale price is invalid.");
        }

        if (!TryParseQuantity(request.StockOnHand, out Quantity stockOnHand, out string? stockError))
        {
            return CreateFailure(
                "Products.InvalidStockOnHand",
                stockError ?? "Stock on hand is invalid.");
        }

        if (!TryParseQuantity(request.ReorderLevel, out Quantity reorderLevel, out string? reorderError))
        {
            return CreateFailure(
                "Products.InvalidReorderLevel",
                reorderError ?? "Reorder level is invalid.");
        }

        Product product = new()
        {
            Sku = NormalizeOptionalText(request.Sku),
            Barcode = barcode,
            Name = request.Name.Trim(),
            Description = NormalizeOptionalText(request.Description),
            CategoryId = NormalizeOptionalId(request.CategoryId),
            BrandId = NormalizeOptionalId(request.BrandId),
            UnitId = request.UnitId,
            SupplierId = NormalizeOptionalId(request.SupplierId),
            CostPrice = costPrice,
            SellingPrice = sellingPrice!.Value,
            WholesalePrice = wholesalePrice,
            StockOnHand = stockOnHand,
            ReorderLevel = reorderLevel,
            TrackStock = request.TrackStock,
            AllowDiscount = request.AllowDiscount,
            IsActive = true,
            Notes = NormalizeOptionalText(request.Notes),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await _productWrites.AddAsync(product, cancellationToken);

        if (result.IsFailure)
        {
            return CreateFailure(
                result.ErrorDetail.Code,
                result.ErrorDetail.Message);
        }

        return new ProductCreateOperationResult(
            IsSuccess: true,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static ProductManagementRowViewModel MapRow(ProductListItemReadModel product)
    {
        return new ProductManagementRowViewModel(
            ProductId: product.Id,
            SkuText: TextOrDash(product.Sku),
            BarcodeText: TextOrDash(product.Barcode),
            ProductName: product.Name,
            UnitName: product.UnitName,
            CategoryText: TextOrDash(product.CategoryName),
            BrandText: TextOrDash(product.BrandName),
            SellingPriceText: product.SellingPrice.ToString(),
            CostPriceText: product.CostPrice?.ToString() ?? "--",
            StockOnHandText: product.TrackStock ? product.StockOnHand.ToString() : "Not tracked",
            ReorderLevelText: product.ReorderLevel.ToString(),
            TrackStockText: product.TrackStock ? "Tracked" : "Not tracked",
            StatusText: product.IsActive ? "Active" : "Inactive");
    }

    private static ProductCatalogOptionViewModel MapUnitOption(UnitReadModel unit)
    {
        string displayText = string.IsNullOrWhiteSpace(unit.Abbreviation)
            ? unit.Name
            : $"{unit.Name} ({unit.Abbreviation})";

        return new ProductCatalogOptionViewModel(unit.Id, displayText);
    }

    private static ProductCatalogOptionViewModel MapCatalogOption(CategoryReadModel category)
    {
        return new ProductCatalogOptionViewModel(category.Id, category.Name);
    }

    private static ProductCatalogOptionViewModel MapCatalogOption(BrandReadModel brand)
    {
        return new ProductCatalogOptionViewModel(brand.Id, brand.Name);
    }

    private static ProductCatalogOptionViewModel MapSupplierOption(SupplierReadModel supplier)
    {
        return new ProductCatalogOptionViewModel(supplier.Id, supplier.Name);
    }

    private static ProductCatalogOptionViewModel[] WithNone(ProductCatalogOptionViewModel[] options)
    {
        return new[] { new ProductCatalogOptionViewModel(null, "-- None --") }
            .Concat(options)
            .ToArray();
    }

    private static ProductCreateFormViewModel CreateFormFailure(
        string errorCode,
        string errorMessage)
    {
        return new ProductCreateFormViewModel(
            Units: Array.Empty<ProductCatalogOptionViewModel>(),
            Categories: Array.Empty<ProductCatalogOptionViewModel>(),
            Brands: Array.Empty<ProductCatalogOptionViewModel>(),
            Suppliers: Array.Empty<ProductCatalogOptionViewModel>(),
            StatusMessage: "Add product is not ready.",
            HasError: true,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);
    }

    private static ProductCreateOperationResult CreateFailure(
        string errorCode,
        string errorMessage)
    {
        return new ProductCreateOperationResult(
            IsSuccess: false,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);
    }

    // Kirch: This parser accepts user-friendly peso input while rejecting invalid or negative values.
    private static bool TryParseMoney(
        string? value,
        bool required,
        out Money? money,
        out string? errorMessage)
    {
        money = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errorMessage = "Amount is required.";
                return false;
            }

            return true;
        }

        string normalized = value
            .Trim()
            .Replace("PHP", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("₱", string.Empty)
            .Trim();

        bool parsed = decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out decimal amount) ||
            decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out amount);

        if (!parsed)
        {
            errorMessage = "Amount must be a valid number.";
            return false;
        }

        if (amount < 0)
        {
            errorMessage = "Amount cannot be negative.";
            return false;
        }

        try
        {
            money = Money.FromPesos(amount);
            return true;
        }
        catch (OverflowException)
        {
            errorMessage = "Amount is too large.";
            return false;
        }
    }

    // Kirch: This parser converts UI quantity text into the x1000 quantity value object safely.
    private static bool TryParseQuantity(
        string? value,
        out Quantity quantity,
        out string? errorMessage)
    {
        quantity = Quantity.Zero;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        bool parsed = decimal.TryParse(
                value.Trim(),
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out decimal amount) ||
            decimal.TryParse(
                value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out amount);

        if (!parsed)
        {
            errorMessage = "Quantity must be a valid number.";
            return false;
        }

        var result = Quantity.FromUnits(amount);

        if (result.IsFailure)
        {
            errorMessage = result.ErrorDetail.Message;
            return false;
        }

        quantity = result.Value;
        return true;
    }

    private static string NormalizeSearchText(string? searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            ? string.Empty
            : searchText.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static Guid? NormalizeOptionalId(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty
            ? value
            : null;
    }

    private static string TextOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "--"
            : value.Trim();
    }

    private static string BuildStatusMessage(int shownCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return "No products found.";
        }

        return $"Showing {shownCount} of {totalCount} products.";
    }
}


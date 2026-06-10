using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed class CheckoutDemoProductSearchBoundary : ICheckoutProductSearchBoundary
{
    private readonly IReadOnlyList<ProductLookupReadModel> _products =
    [
        CreateProduct(
            sku: "DEMO-COFFEE",
            barcode: "480000000001",
            name: "Coffee Sachet",
            unitName: "pc",
            sellingPriceCents: 1200,
            costPriceCents: 800,
            stockX1000: 25000,
            trackStock: true),

        CreateProduct(
            sku: "DEMO-SARDINES",
            barcode: "480000000002",
            name: "Sardines Can",
            unitName: "can",
            sellingPriceCents: 2800,
            costPriceCents: 2100,
            stockX1000: 18000,
            trackStock: true),

        CreateProduct(
            sku: "DEMO-RICE",
            barcode: "480000000003",
            name: "Rice 1kg",
            unitName: "kg",
            sellingPriceCents: 5800,
            costPriceCents: 5000,
            stockX1000: 100000,
            trackStock: true)
    ];

    public Task<Result<ProductLookupReadModel?>> FindProductAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalized = searchText.Trim();

        ProductLookupReadModel? product = _products.FirstOrDefault(x =>
            string.Equals(x.Barcode, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Sku, normalized, StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
    }

    private static ProductLookupReadModel CreateProduct(
        string sku,
        string barcode,
        string name,
        string unitName,
        long sellingPriceCents,
        long costPriceCents,
        long stockX1000,
        bool trackStock)
    {
        return new ProductLookupReadModel(
            Id: DeterministicId(sku),
            Sku: sku,
            Barcode: barcode,
            Name: name,
            UnitName: unitName,
            SellingPrice: Money.FromCents(sellingPriceCents),
            CostPrice: Money.FromCents(costPriceCents),
            StockOnHand: QuantityX1000(stockX1000),
            TrackStock: trackStock,
            AllowDiscount: true,
            IsActive: true);
    }

    private static Quantity QuantityX1000(long valueX1000)
    {
        Result<Quantity> result = Quantity.FromX1000(valueX1000);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }

        return result.Value;
    }

    private static Guid DeterministicId(string value)
    {
        byte[] bytes = new byte[16];
        byte[] source = System.Text.Encoding.UTF8.GetBytes(value);

        for (int i = 0; i < source.Length; i++)
        {
            bytes[i % bytes.Length] ^= source[i];
        }

        return new Guid(bytes);
    }
}

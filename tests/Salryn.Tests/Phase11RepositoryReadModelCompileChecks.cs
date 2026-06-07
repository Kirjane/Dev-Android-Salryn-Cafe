using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class Phase11RepositoryReadModelCompileChecks
{
    public static void Run()
    {
        PageRequest page = PageRequest.FirstPage();

        ProductSearchFilter productFilter = new(
            SearchText: "sample",
            CategoryId: null,
            BrandId: null,
            IsActive: true,
            IncludeInactive: false,
            Page: page);

        SaleHistoryFilter saleFilter = new(
            From: null,
            To: null,
            CashierUserId: null,
            Status: SaleStatus.Completed,
            ReceiptNo: null,
            Page: page);

        ProductListItemReadModel product = new(
            Id: Guid.NewGuid(),
            Sku: "SKU-001",
            Barcode: "123456789",
            Name: "Sample Product",
            UnitName: "Piece",
            CategoryName: "General Merchandise",
            BrandName: "Generic",
            SellingPrice: Money.FromCents(2500),
            CostPrice: Money.FromCents(1500),
            StockOnHand: Quantity.FromX1000(1000).Value,
            ReorderLevel: Quantity.FromX1000(5000).Value,
            TrackStock: true,
            IsActive: true);

        SaleHistoryItemReadModel sale = new(
            Id: Guid.NewGuid(),
            ReceiptNo: "SLRN-2026-000001",
            SaleDate: DateTimeOffset.UtcNow,
            CashierDisplayName: "Cashier",
            Total: Money.FromCents(2500),
            AmountPaid: Money.FromCents(3000),
            Change: Money.FromCents(500),
            Status: SaleStatus.Completed,
            VoidReason: null);

        PagedResult<ProductListItemReadModel> productPage = new(
            Items: new[] { product },
            TotalCount: 1,
            PageNumber: productFilter.Page.PageNumber,
            PageSize: productFilter.Page.PageSize);

        if (productPage.TotalCount != 1)
        {
            throw new InvalidOperationException("Paged product read model check failed.");
        }

        if (saleFilter.Status != SaleStatus.Completed)
        {
            throw new InvalidOperationException("Sale filter read model check failed.");
        }

        if (sale.Total.Cents != 2500)
        {
            throw new InvalidOperationException("Sale history read model check failed.");
        }
    }
}

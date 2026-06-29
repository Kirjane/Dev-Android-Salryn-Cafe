using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Contracts;
using Salryn.Core.ReadModels;

namespace Salryn.Api.Local.Services;

public sealed class DashboardSummaryService
{
    private readonly SalrynApiComposition _composition;

    public DashboardSummaryService(SalrynApiComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        List<string> warnings = [];
        DateTime today = DateTime.Today;
        DateTime tomorrow = today.AddDays(1);

        long todaySalesCents = 0;
        int todayTransactionCount = 0;
        int productCount = 0;
        int lowStockCount = 0;

        var salesResult = await _composition.SaleReadRepository.SearchSalesAsync(
            new SaleHistoryFilter(
                From: new DateTimeOffset(today),
                To: new DateTimeOffset(tomorrow),
                CashierUserId: null,
                Status: null,
                ReceiptNo: null,
                Page: PageRequest.FirstPage(200)),
            cancellationToken).ConfigureAwait(false);

        if (salesResult.IsSuccess)
        {
            todayTransactionCount = salesResult.Value.TotalCount;
            todaySalesCents = salesResult.Value.Items.Sum(item => item.Total.Cents);
        }
        else
        {
            warnings.Add($"sales: {salesResult.ErrorDetail.Message}");
        }

        var productsResult = await _composition.ProductReadRepository.SearchAsync(
            new ProductSearchFilter(
                SearchText: null,
                CategoryId: null,
                BrandId: null,
                IsActive: null,
                IncludeInactive: true,
                Page: PageRequest.FirstPage(1)),
            cancellationToken).ConfigureAwait(false);

        if (productsResult.IsSuccess)
        {
            productCount = productsResult.Value.TotalCount;
        }
        else
        {
            warnings.Add($"products: {productsResult.ErrorDetail.Message}");
        }

        var lowStockResult = await _composition.InventoryReadRepository
            .GetLowStockProductsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lowStockResult.IsSuccess)
        {
            lowStockCount = lowStockResult.Value.Count;
        }
        else
        {
            warnings.Add($"lowStock: {lowStockResult.ErrorDetail.Message}");
        }

        string status = warnings.Count == 0
            ? "ok"
            : "warning";

        return new DashboardSummaryResponse(
            TodaySalesTotal: todaySalesCents / 100m,
            TodayTransactionCount: todayTransactionCount,
            ProductCount: productCount,
            LowStockCount: lowStockCount,
            RefreshedAt: DateTimeOffset.Now,
            Status: status,
            Warnings: warnings);
    }
}

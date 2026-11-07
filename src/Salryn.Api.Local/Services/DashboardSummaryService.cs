using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;

namespace Salryn.Api.Local.Services;

public sealed class DashboardSummaryService
{
    private readonly SalrynLocalComposition _composition;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeZoneInfo StoreTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Asia/Manila",
        TimeSpan.FromHours(8),
        "Philippine Standard Time",
        "Philippine Standard Time");

    public DashboardSummaryService(SalrynLocalComposition composition, TimeProvider? timeProvider = null)
    {
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Contracts.DashboardSummaryResponse> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> warnings = [];
        DateTimeOffset storeNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), StoreTimeZone);
        DateTimeOffset today = new(
            storeNow.Year, storeNow.Month, storeNow.Day, 0, 0, 0, StoreTimeZone.BaseUtcOffset);
        DateTimeOffset tomorrow = today.AddDays(1);

        decimal todaySalesCents = 0;
        int todayTransactionCount = 0;
        int productCount = 0;
        int lowStockCount = 0;

        const int salesPageSize = 500;
        int salesPage = 1;
        while (true)
        {
            var salesResult = await _composition.SaleReadRepository.SearchSalesAsync(
                new Core.ReadModels.SaleHistoryFilter(
                    From: today,
                    To: tomorrow.AddTicks(-1),
                    CashierUserId: null,
                    Status: Core.Models.SaleStatus.Completed,
                    ReceiptNo: null,
                    Page: new Core.ReadModels.PageRequest(salesPage, salesPageSize)),
                cancellationToken).ConfigureAwait(false);

            if (salesResult.IsFailure)
            {
                warnings.Add($"sales: {salesResult.ErrorDetail.Message}");
                break;
            }

            todayTransactionCount = salesResult.Value.TotalCount;
            foreach (var sale in salesResult.Value.Items)
                todaySalesCents += sale.Total.Cents;

            if (salesPage * salesPageSize >= salesResult.Value.TotalCount)
                break;

            salesPage++;
        }

        var productsResult = await _composition.ProductReadRepository.SearchAsync(
            new Core.ReadModels.ProductSearchFilter(
                SearchText: null,
                CategoryId: null,
                BrandId: null,
                IsActive: null,
                IncludeInactive: true,
                Page: Core.ReadModels.PageRequest.FirstPage(1)),
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

        return new Contracts.DashboardSummaryResponse(
            TodaySalesTotal: todaySalesCents / 100m,
            TodayTransactionCount: todayTransactionCount,
            ProductCount: productCount,
            LowStockCount: lowStockCount,
            RefreshedAt: DateTimeOffset.Now,
            Status: status,
            Warnings: warnings);
    }
}

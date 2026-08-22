namespace Salryn.Api.Local.Contracts;

public sealed record DashboardSummaryResponse(
    decimal TodaySalesTotal,
    int TodayTransactionCount,
    int ProductCount,
    int LowStockCount,
    DateTimeOffset RefreshedAt,
    string Status,
    IReadOnlyList<string> Warnings);

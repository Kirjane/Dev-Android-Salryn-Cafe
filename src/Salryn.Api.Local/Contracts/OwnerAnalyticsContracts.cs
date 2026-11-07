namespace Salryn.Api.Local.Contracts;

public sealed record OwnerAnalyticsScope(
    DateTimeOffset AsOfUtc,
    string TimeZoneId,
    DateTimeOffset RangeStartUtc,
    DateTimeOffset RangeEndUtc,
    DateTimeOffset PriorRangeStartUtc,
    DateTimeOffset PriorRangeEndUtc,
    string RangeSemantics,
    string MoneySemantics,
    string ProductAttributionSemantics);

public sealed record OwnerAnalyticsOverview(
    OwnerAnalyticsScope Scope,
    OwnerAnalyticsPeriod Current,
    OwnerAnalyticsPeriod Prior,
    OwnerMarginEstimate Margin,
    int LowStockCount);

public sealed record OwnerAnalyticsPeriod(
    int CompletedSaleCount,
    long SalesTotalCents,
    long CashOnSalesCents,
    long CreditOriginatedCents,
    int VoidedSaleCount);

public sealed record OwnerMarginEstimate(
    bool IsAvailable,
    long? EstimatedCogsCents,
    long? EstimatedGrossMarginCents,
    int CostedItemCount,
    int MissingCostItemCount,
    string Basis);

public sealed record OwnerTrendPoint(
    string StoreDate,
    long SalesTotalCents,
    long CashOnSalesCents,
    long CreditOriginatedCents,
    int CompletedSaleCount,
    int VoidedSaleCount);

public sealed record OwnerSalesTrendResponse(
    OwnerAnalyticsScope Scope,
    string Bucket,
    IReadOnlyList<OwnerTrendPoint> Points);

public sealed record OwnerPaymentMixRow(string Method, long AmountCents, string Scope);
public sealed record OwnerPaymentMixResponse(OwnerAnalyticsScope Scope, IReadOnlyList<OwnerPaymentMixRow> Rows);

public sealed record OwnerAnalyticsProductRow(
    Guid ProductId,
    string HistoricalLabel,
    string? HistoricalBarcode,
    long QuantityX1000,
    long LineRevenueCents,
    long? EstimatedCogsCents,
    bool CostCoverageComplete);

public sealed record OwnerAnalyticsCategoryRow(string CurrentCategory, long QuantityX1000, long LineRevenueCents);
public sealed record OwnerTopProductsResponse(
    OwnerAnalyticsScope Scope,
    IReadOnlyList<OwnerAnalyticsProductRow> Products,
    IReadOnlyList<OwnerAnalyticsCategoryRow> Categories,
    string ClassificationDisclosure);

public sealed record OwnerArAgingBucket(string Key, string Label, long OutstandingCents);
public sealed record OwnerArAgingResponse(
    OwnerAnalyticsScope Scope,
    long OutstandingCents,
    long CollectionsInPeriodCents,
    IReadOnlyList<OwnerPaymentMixRow> CollectionsByMethod,
    IReadOnlyList<OwnerArAgingBucket> Buckets,
    string AllocationDisclosure);


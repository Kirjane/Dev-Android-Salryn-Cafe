namespace Salryn.Api.Local.Contracts;

public sealed record OwnerReportsResponse(
    DateTimeOffset GeneratedAt,
    string WeekRule,
    string OfficialBoundary,
    OwnerSalesPeriodSummary Daily,
    OwnerSalesPeriodSummary Weekly,
    OwnerSalesPeriodSummary Monthly,
    IReadOnlyList<OwnerTopProductRow> TopProducts,
    IReadOnlyList<OwnerCashierTotalRow> CashierTotals,
    IReadOnlyList<OwnerCategoryTotalRow> CategoryTotals,
    IReadOnlyList<OwnerLowStockRow> LowStock,
    OwnerInventoryValueSummary InventoryValue,
    OwnerReceivablesSummary? Receivables);

public sealed record OwnerReceivablesSummary(
    long CreditSalesCents,
    long CashCollectedOnSalesCents,
    long CollectionsCents,
    long OutstandingCents,
    long OverdueCents);

public sealed record OwnerSalesPeriodSummary(
    string PeriodKey,
    string Label,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    int SalesCount,
    long SalesAmountCents,
    long AverageSaleCents,
    int VoidedCount,
    long VoidedAmountCents);

public sealed record OwnerTopProductRow(
    Guid ProductId,
    string ProductName,
    string? Barcode,
    decimal QuantitySold,
    long SalesAmountCents);

public sealed record OwnerCashierTotalRow(
    Guid CashierUserId,
    string CashierDisplayName,
    int SalesCount,
    long SalesAmountCents,
    int VoidedCount,
    long VoidedAmountCents);

public sealed record OwnerCategoryTotalRow(
    string CategoryName,
    decimal QuantitySold,
    long SalesAmountCents);

public sealed record OwnerLowStockRow(
    Guid ProductId,
    string ProductName,
    string? Barcode,
    string UnitName,
    decimal StockOnHand,
    decimal ReorderLevel);

public sealed record OwnerInventoryValueSummary(
    bool IsAvailable,
    decimal? EstimatedValueCents,
    int ValuedProductCount,
    int MissingCostProductCount,
    string Message);

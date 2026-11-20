namespace Salryn.Sync.Contracts;

public sealed record SaleCommittedV1(
    SaleCommittedV1Sale Sale,
    IReadOnlyList<SaleCommittedV1Item> Items,
    IReadOnlyList<SaleCommittedV1Payment> Payments,
    IReadOnlyList<SaleCommittedV1StockMovement> StockMovements);

public sealed record SaleCommittedV1Sale(
    Guid Id,
    string ReceiptNumber,
    DateTimeOffset SaleDate,
    Guid CashierUserId,
    Guid CashierSessionId,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents,
    Guid? CustomerId,
    string PaymentStatus,
    long BalanceDueCents,
    DateOnly? ReceivableDueDate,
    string? ReceivableNote,
    string? ReceiptSnapshot,
    DateTimeOffset CreatedAt);

public sealed record SaleCommittedV1Item(
    Guid Id,
    Guid SaleId,
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long? CostPriceCents,
    long DiscountCents,
    long LineTotalCents);

public sealed record SaleCommittedV1Payment(
    Guid Id,
    Guid SaleId,
    string Method,
    long AmountCents,
    DateTimeOffset PaidAt);

public sealed record SaleCommittedV1StockMovement(
    Guid Id,
    Guid ProductId,
    string MovementType,
    long QuantityChangeX1000,
    long? CostPriceCents,
    string ReferenceType,
    Guid ReferenceId,
    string? Reason,
    Guid UserId,
    DateTimeOffset CreatedAt);

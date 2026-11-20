namespace Salryn.Sync.Contracts;

public sealed record SaleCommittedV2(
    SaleCommittedV1 SaleEvidence,
    Guid FinancialOperationId,
    Guid FinancialDecisionId,
    Guid? OrderId,
    Guid? OrderRevisionId,
    int? ExpectedOrderVersion,
    DateTimeOffset FinancialDecisionCreatedAtUtc,
    string FinancialDecisionHash,
    string? DiscountReason,
    long FeeTotalCents,
    IReadOnlyList<SaleCommittedV2LineDiscount> LineDiscounts,
    IReadOnlyList<SaleCommittedV2Fee> Fees);

public sealed record SaleCommittedV2LineDiscount(
    Guid SaleItemId,
    long LineDiscountCents,
    long OrderDiscountAllocatedCents,
    long TotalDiscountCents);

public sealed record SaleCommittedV2Fee(
    Guid FeeId,
    string FeeType,
    string DisplayLabelSnapshot,
    long AmountCents,
    Guid ActorUserId,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

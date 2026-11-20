namespace Salryn.Core.Models;

public static class CafeSaleFeeTypes
{
    public const string Service = "Service";
    public const string Delivery = "Delivery";

    public static bool IsSupportedForC5(string? value) =>
        string.Equals(value, Service, StringComparison.Ordinal);

    public static bool IsKnown(string? value) =>
        string.Equals(value, Service, StringComparison.Ordinal) ||
        string.Equals(value, Delivery, StringComparison.Ordinal);
}

public static class CafeRefundStates
{
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Committed = "Committed";

    public static bool IsTerminal(string? value) =>
        string.Equals(value, Rejected, StringComparison.Ordinal) ||
        string.Equals(value, Committed, StringComparison.Ordinal);

    public static bool IsKnown(string? value) =>
        string.Equals(value, Requested, StringComparison.Ordinal) ||
        string.Equals(value, Approved, StringComparison.Ordinal) ||
        IsTerminal(value);
}

public static class CafeRefundStockDispositions
{
    public const string Restock = "Restock";
    public const string DamagedNonRestockable = "DamagedNonRestockable";
    public const string ServiceNoStock = "ServiceNoStock";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Restock, StringComparison.Ordinal) ||
        string.Equals(value, DamagedNonRestockable, StringComparison.Ordinal) ||
        string.Equals(value, ServiceNoStock, StringComparison.Ordinal);
}

public static class CafeRefundSettlementMethods
{
    public const string Cash = "Cash";
    public const string AccountsReceivable = "AccountsReceivable";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Cash, StringComparison.Ordinal) ||
        string.Equals(value, AccountsReceivable, StringComparison.Ordinal);
}

public static class CafeRefundOperationTypes
{
    public const string Requested = "SaleRefundRequestedV1";
    public const string Resolved = "SaleRefundResolvedV1";
    public const string Committed = "SaleRefundCommittedV1";
}

public sealed record CafeSaleFinancialLineDecision(
    Guid SaleItemId,
    long LineDiscountCents,
    long OrderDiscountAllocatedCents)
{
    public long TotalDiscountCents =>
        checked(LineDiscountCents + OrderDiscountAllocatedCents);
}

public sealed record CafeSaleFeeDecision(
    Guid FeeId,
    string FeeType,
    string DisplayLabelSnapshot,
    long AmountCents,
    Guid ActorUserId,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record CafeSaleFinancialDecision(
    Guid FinancialDecisionId,
    Guid OperationId,
    Guid StoreId,
    Guid? OrderId,
    Guid? OrderRevisionId,
    int? ExpectedOrderVersion,
    Guid SaleId,
    Guid CashierSessionId,
    Guid ActorUserId,
    long GrossSubtotalCents,
    long LineDiscountTotalCents,
    long OrderDiscountTotalCents,
    long FeeTotalCents,
    long TaxCents,
    long NetSaleTotalCents,
    long CashTenderCents,
    long AccountsReceivableTenderCents,
    long AmountPaidCents,
    long BalanceDueCents,
    long ChangeCents,
    string? DiscountReason,
    string ContentHash,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<CafeSaleFinancialLineDecision> LineDecisions,
    IReadOnlyList<CafeSaleFeeDecision> Fees)
{
    public long BookingDepositAppliedCents { get; init; }
}

public sealed record CafeSaleFinancialLineInput(
    Guid SaleItemId,
    long GrossLineCents,
    long LineDiscountCents,
    int DisplayOrder);

public sealed record CafeSaleFeeMutation(
    Guid FeeId,
    string FeeType,
    string DisplayLabel,
    long AmountCents,
    string? Reason);

public sealed record CafeSaleFinancialDecisionRequest(
    Guid OperationId,
    Guid FinancialDecisionId,
    Guid StoreId,
    Guid? OrderId,
    Guid? OrderRevisionId,
    int? ExpectedOrderVersion,
    Guid SaleId,
    Guid CashierSessionId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<CafeSaleFinancialLineInput> Lines,
    long OrderDiscountCents,
    string? DiscountReason,
    IReadOnlyList<CafeSaleFeeMutation> Fees,
    long TaxCents,
    long CashTenderCents,
    long AccountsReceivableTenderCents)
{
    public long BookingDepositAppliedCents { get; init; }
}

public sealed record CafeRefundLineMutation(
    Guid RefundLineId,
    Guid SaleItemId,
    long QuantityX1000,
    string StockDisposition,
    string? OperatorNote);

public sealed record CafeRefundRequestMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid RefundId,
    Guid SaleId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    IReadOnlyList<CafeRefundLineMutation> Lines);

public sealed record CafeRefundResolutionMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid RefundId,
    Guid SaleId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    bool Approve,
    string? Reason);

public sealed record CafeRefundSettlementMutation(
    Guid SettlementPartId,
    string Method,
    long AmountCents);

public sealed record CafeRefundCommitMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid RefundId,
    Guid SaleId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    Guid CashierSessionId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<CafeRefundSettlementMutation> Settlements);

public sealed record CafeRefundLineSnapshot(
    Guid RefundLineId,
    Guid RefundId,
    Guid SaleItemId,
    Guid ProductId,
    string ProductNameSnapshot,
    long OriginalQuantityX1000,
    long RefundQuantityX1000,
    long FrozenUnitPriceCents,
    long FrozenDiscountCents,
    long RefundAmountCents,
    string StockDisposition,
    string? OperatorNote);

public sealed record CafeRefundSettlementSnapshot(
    Guid SettlementPartId,
    Guid RefundId,
    string Method,
    long AmountCents,
    Guid? CashMovementId,
    Guid? ReceivableEntryId,
    DateTimeOffset CreatedAtUtc);

public sealed record CafeRefundSummary(
    Guid RefundId,
    Guid SaleId,
    Guid StoreId,
    int Version,
    string State,
    string Reason,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionReason,
    Guid? CommittedByUserId,
    DateTimeOffset? CommittedAtUtc,
    long? TotalRefundCents,
    string RequestHash);

public sealed record CafeRefundDetail(
    CafeRefundSummary Refund,
    IReadOnlyList<CafeRefundLineSnapshot> Lines,
    IReadOnlyList<CafeRefundSettlementSnapshot> Settlements);

public sealed record CafeRefundMutationResult(
    Guid RefundId,
    int Version,
    string State,
    bool Idempotent);

public sealed record CafeRefundCommitResult(
    Guid RefundId,
    int Version,
    string State,
    long TotalRefundCents,
    bool Idempotent);

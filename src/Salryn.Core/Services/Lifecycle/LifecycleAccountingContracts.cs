using Salryn.Core.Models;
using Salryn.Core.Services.InvoiceOutput;

namespace Salryn.Core.Services.Lifecycle;

public enum LifecycleDocumentState
{
    Committed = 1,
    PartiallyRefunded = 2,
    FullyRefunded = 3,
    Voided = 4,
    RefundPendingSettlement = 5,
    Adjusted = 6,
    CancelledBeforeCommit = 7
}

public enum StockReturnDisposition
{
    Restock = 1,
    DamagedNonRestockable = 2,
    ServiceNoStock = 3
}

public enum RefundSettlementMethod
{
    Cash = 1,
    CardExternal = 2
}

public enum RefundSettlementStatus
{
    Completed = 1,
    PendingExternal = 2
}

public sealed record LifecycleRefundLineRequest(
    Guid SaleItemId,
    long QuantityX1000,
    StockReturnDisposition Disposition,
    string? OperatorNote = null,
    long? ExpectedRefundAmountCents = null);

public sealed record LifecycleRefundSettlementRequest(
    RefundSettlementMethod Method,
    long? ExpectedAmountCents = null,
    string? ExternalReference = null,
    bool ImmediateComplete = true);

/// <summary>M4-RC2 command envelope. Actor must be server-derived.</summary>
public sealed record LifecycleAccountingCommand(
    OfficialDocumentLifecycleAction Action,
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    Guid? SaleId,
    string Reason,
    Guid ActorUserId,
    string? TerminalId,
    string IdempotencyKey,
    IReadOnlyList<LifecycleRefundLineRequest>? RefundLines = null,
    LifecycleRefundSettlementRequest? Settlement = null,
    long? ExpectedTotalRefundCents = null,
    /// <summary>Test-only failure injection stage name; null in production.</summary>
    string? FailureInjectionStage = null);

public sealed record LifecycleRefundLineResult(
    Guid SaleItemId,
    int LineIndex,
    long QuantityX1000,
    long RefundAmountCents,
    long VatableBaseCents,
    long VatAmountCents,
    long VatExemptCents,
    long ZeroRatedCents,
    long SsptCents,
    long NonVatExemptCents,
    StockReturnDisposition Disposition);

public sealed record LifecycleTaxReversalTotals(
    long VatableBaseCents,
    long VatAmountCents,
    long VatExemptCents,
    long ZeroRatedCents,
    long SsptCents,
    long NonVatExemptCents,
    long GrossRefundCents);

public sealed record LifecycleAccountingResult(
    OfficialDocumentLifecycleAction Action,
    Guid OperationId,
    Guid EventId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OperationTimestamp,
    bool SequenceAdvanced,
    bool OriginalImmutable,
    string LifecycleState,
    int StateVersion,
    string Summary,
    LifecycleTaxReversalTotals? TaxReversal,
    IReadOnlyList<LifecycleRefundLineResult>? RefundLines,
    Guid? SettlementId,
    RefundSettlementStatus? SettlementStatus,
    Guid? AdjustmentDocumentId,
    string? AdjustmentDocumentNumber,
    string? AdjustmentSnapshotHash,
    InvoiceOutputPackage? Output);

public sealed record FrozenSnapshotLine(
    int LineIndex,
    Guid SaleItemId,
    Guid ProductId,
    string Description,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents,
    OfficialDocumentLineTaxClassification TaxClassification,
    long VatableBaseCents,
    long VatAmountCents,
    long VatExemptCents,
    long ZeroRatedCents,
    long SsptCents,
    long NonVatExemptCents,
    bool StockTracked);

public sealed record FrozenSnapshotDocument(
    string Schema,
    Guid JournalEntryId,
    Guid? SaleId,
    string OfficialDocumentNumber,
    string SnapshotPayloadJson,
    string SnapshotPayloadHash,
    long TotalCents,
    OfficialDocumentTaxBucketDraftTaxpayerMode TaxpayerMode,
    IReadOnlyList<FrozenSnapshotLine> Lines,
    long OriginalVatableBaseCents,
    long OriginalVatAmountCents,
    long OriginalVatExemptCents,
    long OriginalZeroRatedCents,
    long OriginalSsptCents,
    long OriginalNonVatExemptCents);

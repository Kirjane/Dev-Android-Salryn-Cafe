namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M9 source-sale read-only contracts.
/// Read operational sale data for dry-run planning only — never mutates sale/sequence/EJ/shift/inventory.
/// </summary>
public sealed record OfficialSourceSaleReadOnlyRequest(
    Guid SaleId,
    string? DocumentKindHint);

public sealed record OfficialSourceSaleReadOnlyLine(
    string SkuOrCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? TaxBucketCode);

public sealed record OfficialSourceSaleReadOnlyTender(
    string Method,
    decimal Amount,
    DateTimeOffset PaidAtUtc);

public sealed record OfficialSourceSaleReadOnlyTaxSummary(
    decimal Subtotal,
    decimal Discount,
    decimal TaxTotal,
    decimal GrandTotal,
    string CurrencyCode);

public sealed record OfficialSourceSaleReadOnlySnapshot(
    Guid SaleId,
    string OperationalReceiptNo,
    DateTimeOffset SaleDateUtc,
    string CashierDisplayName,
    string Status,
    string? VoidReason,
    IReadOnlyList<OfficialSourceSaleReadOnlyLine> Lines,
    IReadOnlyList<OfficialSourceSaleReadOnlyTender> Tenders,
    OfficialSourceSaleReadOnlyTaxSummary TaxSummary);

public sealed record OfficialSourceSaleReadOnlyBlocker(
    string Code,
    string Message);

public sealed record OfficialSourceSaleReadOnlyWarning(
    string Code,
    string Message);

public sealed record OfficialSourceSaleReadOnlyResult(
    int ContractVersion,
    bool DryRunOnly,
    bool NonOfficial,
    bool SourceSaleReadOnly,
    bool LiveOfficialPostingAllowed,
    bool OfficialCheckoutAllocationEnabled,
    bool CheckoutUsesAllocator,
    bool SequenceWillMutate,
    bool SalesWillMutate,
    bool EjWillAppendLive,
    bool OutputWillGenerate,
    bool OfficialReceiptIssued,
    bool ComplianceClaimed,
    OfficialSourceSaleReadOnlySnapshot? Snapshot,
    OfficialDryRunPostingRequest? PostingRequest,
    IReadOnlyList<OfficialSourceSaleReadOnlyBlocker> Blockers,
    IReadOnlyList<OfficialSourceSaleReadOnlyWarning> Warnings,
    string Summary);

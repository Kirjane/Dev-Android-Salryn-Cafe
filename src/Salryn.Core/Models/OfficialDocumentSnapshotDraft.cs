namespace Salryn.Core.Models;

// Kirch strict dev guide:
// J21-4A owns neutral official-document snapshot draft contracts only.
// OfficialDocumentSnapshotDraft is a data shape, not rendered output.
// No renderer, printer, checkout wiring, sequence allocation, database persistence,
// electronic journal, X/Z reading, VAT bucket engine, or accreditation-readiness claim belongs here.
public sealed record OfficialDocumentSnapshotDraft(
    OfficialDocumentSnapshotDraftKind DocumentKind,
    OfficialDocumentSnapshotDraftHeader Header,
    IReadOnlyList<OfficialDocumentSnapshotDraftLine> Lines,
    OfficialDocumentSnapshotDraftAmounts Amounts,
    IReadOnlyList<OfficialDocumentSnapshotDraftPayment> Payments,
    OfficialDocumentSnapshotDraftTaxMode TaxMode,
    IReadOnlyList<string> DeferredComplianceNotes,
    OfficialDocumentTaxBucketDraft? TaxBuckets = null,
    // TRAD-M3-RC1: optional buyer/statutory evidence frozen at post time (omit for historical V1 rows).
    OfficialDocumentSnapshotDraftBuyer? Buyer = null,
    OfficialDocumentSnapshotDraftStatutory? Statutory = null);

/// <summary>Optional buyer identity captured at official post. Never invented at render time.</summary>
public sealed record OfficialDocumentSnapshotDraftBuyer(
    string? RegisteredName,
    string? Tin,
    string? RegisteredAddress);

/// <summary>Optional statutory discount / withholding evidence when supplied at post.</summary>
public sealed record OfficialDocumentSnapshotDraftStatutory(
    string? DiscountCategory,
    string? GovernmentIdNumber,
    string? SignatureReference,
    long? WithholdingTaxCents);

// Kirch strict dev guide:
// Keep receipt/invoice distinction explicit in the draft contract.
// A later phase may map this to official wording only after rendering, journal, and output gates are ready.
public enum OfficialDocumentSnapshotDraftKind
{
    OfficialReceipt = 1,
    OfficialInvoice = 2
}

// Kirch strict dev guide:
// C03 header shape only.
// These fields describe future official-document requirements without rendering or persisting official output.
public sealed record OfficialDocumentSnapshotDraftHeader(
    string RegisteredTaxpayerName,
    string? BusinessName,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string RegisteredBusinessAddress,
    DateTimeOffset TransactionDateTime,
    string? DocumentNumber,
    string? MachineSerialNumber,
    string? MachineIdentificationNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string CashierIdentifier,
    string? TerminalIdentifier);

// Kirch strict dev guide:
// C04 item shape only.
// This mirrors sale item evidence fields without creating a receipt renderer.
public sealed record OfficialDocumentSnapshotDraftLine(
    string Description,
    string? Barcode,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents,
    // TRAD-M4-RC2 / Snapshot V3 optional immutable line-tax evidence (null = V1/V2 line shape).
    int? LineIndex = null,
    Guid? SaleItemId = null,
    Guid? ProductId = null,
    OfficialDocumentLineTaxClassification? TaxClassification = null,
    long? VatableBaseCents = null,
    long? VatAmountCents = null,
    long? VatExemptCents = null,
    long? ZeroRatedCents = null,
    long? SsptCents = null,
    long? NonVatExemptCents = null,
    bool? StockTracked = null);

// Kirch strict dev guide:
// C04 amount shape only.
// VAT/non-VAT bucket computation is intentionally deferred to a later C05 phase.
public sealed record OfficialDocumentSnapshotDraftAmounts(
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents);

// Kirch strict dev guide:
// C04 payment shape only.
// This does not trigger payment processing, receipt printing, or journal writes.
public sealed record OfficialDocumentSnapshotDraftPayment(
    PaymentMethod Method,
    long AmountCents,
    DateTimeOffset PaidAt);

// Kirch strict dev guide:
// C05 placeholder only.
// This records tax-mode intent and defers VATable, VAT-exempt, zero-rated, and non-VAT bucket logic.
public sealed record OfficialDocumentSnapshotDraftTaxMode(
    string TaxModeCode,
    bool VatBucketEngineDeferred,
    string? Notes);
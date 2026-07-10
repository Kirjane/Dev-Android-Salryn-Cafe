namespace Salryn.Core.Models;

// Kirch strict dev guide:
// J21-4C owns C05 tax bucket draft contracts only.
// These records describe VAT/non-VAT/exempt/zero-rated bucket shape.
// No calculator engine, renderer, printer, checkout wiring, sale snapshot write,
// database migration, sequence allocation, electronic journal, X/Z reading,
// or official output claim belongs here.
// TRAD-M1 expands taxpayer modes and non-VAT SSPT/exempt bucket kinds (shape only).
public sealed record OfficialDocumentTaxBucketDraft(
    OfficialDocumentTaxBucketDraftTaxpayerMode TaxpayerMode,
    IReadOnlyList<OfficialDocumentTaxBucketDraftBucket> Buckets,
    string? NonVatStatement,
    IReadOnlyList<string> DeferredComplianceNotes);

// Kirch strict dev guide:
// Taxpayer mode is draft metadata only.
// A later phase must decide how store/taxpayer configuration selects this value.
// TRAD-M1: NonVatPercentageTax is the primary name for value 2; NonVatRegistered remains a legacy alias.
public enum OfficialDocumentTaxBucketDraftTaxpayerMode
{
    Unknown = 0,
    VatRegistered = 1,
    /// <summary>Non-VAT percentage-tax / SSPT path (primary name for value 2).</summary>
    NonVatPercentageTax = 2,
    /// <summary>Legacy alias for NonVatPercentageTax (same value 2).</summary>
    NonVatRegistered = 2,
    NonVatExempt = 3
}

// Kirch strict dev guide:
// C05 bucket labels only.
// These values are not a computation engine and must not imply receipt output readiness.
// TRAD-M1 adds SsptSales and NonVatExemptSales for non-VAT mixed breakdowns.
public enum OfficialDocumentTaxBucketDraftKind
{
    VatableSales = 1,
    VatAmount = 2,
    VatExemptSales = 3,
    ZeroRatedSales = 4,
    /// <summary>Legacy non-VAT aggregate bucket (prefer SsptSales / NonVatExemptSales for new drafts).</summary>
    NonVatSales = 5,
    SsptSales = 6,
    NonVatExemptSales = 7
}

// Kirch strict dev guide:
// Bucket amount shape only.
// SalesAmountCents and TaxAmountCents are carried as evidence fields for a later engine.
// This contract must not calculate, format, render, print, or persist official output.
public sealed record OfficialDocumentTaxBucketDraftBucket(
    OfficialDocumentTaxBucketDraftKind Kind,
    long SalesAmountCents,
    long TaxAmountCents,
    string? Notes);

namespace Salryn.Core.Models;

// Kirch strict dev guide:
// J21-4C owns C05 tax bucket draft contracts only.
// These records describe VAT/non-VAT/exempt/zero-rated bucket shape.
// No calculator engine, renderer, printer, checkout wiring, sale snapshot write,
// database migration, sequence allocation, electronic journal, X/Z reading,
// or official output claim belongs here.
public sealed record OfficialDocumentTaxBucketDraft(
    OfficialDocumentTaxBucketDraftTaxpayerMode TaxpayerMode,
    IReadOnlyList<OfficialDocumentTaxBucketDraftBucket> Buckets,
    string? NonVatStatement,
    IReadOnlyList<string> DeferredComplianceNotes);

// Kirch strict dev guide:
// Taxpayer mode is draft metadata only.
// A later phase must decide how store/taxpayer configuration selects this value.
public enum OfficialDocumentTaxBucketDraftTaxpayerMode
{
    Unknown = 0,
    VatRegistered = 1,
    NonVatRegistered = 2
}

// Kirch strict dev guide:
// C05 bucket labels only.
// These values are not a computation engine and must not imply receipt output readiness.
public enum OfficialDocumentTaxBucketDraftKind
{
    VatableSales = 1,
    VatAmount = 2,
    VatExemptSales = 3,
    ZeroRatedSales = 4,
    NonVatSales = 5
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
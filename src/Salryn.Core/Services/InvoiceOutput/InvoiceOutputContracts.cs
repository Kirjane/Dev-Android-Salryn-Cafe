using Salryn.Core.Models;

namespace Salryn.Core.Services.InvoiceOutput;

public enum InvoiceOutputMode
{
    /// <summary>Always watermarked. May list missing optional fields. Never claims legal issuance.</summary>
    InternalDemo = 1,

    /// <summary>
    /// Stricter completeness. Still blocked from "unwatermarked production delivery" unless separate
    /// activation/authorization conditions are met (activation remains OFF by default).
    /// </summary>
    ProductionCapable = 2
}

public enum InvoiceOutputFormat
{
    Html = 1,
    ThermalText = 2
}

public enum InvoiceDocumentVariant
{
    VatAllVatable = 1,
    VatExemptOnly = 2,
    VatZeroRatedOnly = 3,
    VatMixed = 4,
    NonVatSsptOnly = 5,
    NonVatExemptOnly = 6,
    NonVatMixed = 7
}

public sealed record OfficialInvoiceSnapshotIdentity(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    string OfficialDocumentNumber,
    string DocumentKind,
    DateTimeOffset AppendedAt,
    string SnapshotPayloadJson,
    string SnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    bool HashVerified);

public sealed record InvoiceRenderLine(
    string Description,
    string? UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents);

public sealed record InvoiceRenderPayment(
    string Method,
    long AmountCents);

public sealed record InvoiceRenderTaxBucket(
    string Kind,
    long SalesAmountCents,
    long TaxAmountCents);

/// <summary>Renderer-facing model. Built only from immutable snapshot evidence.</summary>
public sealed record InvoiceRenderModel(
    string RendererContractVersion,
    InvoiceDocumentVariant Variant,
    string DocumentTitle,
    string RegistrationPhrase,
    string OfficialDocumentNumber,
    DateTimeOffset TransactionDateTime,
    string SellerRegisteredName,
    string? SellerBusinessName,
    string SellerTin,
    string SellerBranchCode,
    string SellerRegisteredAddress,
    string? MachineSerialNumber,
    string? MachineIdentificationNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string CashierIdentifier,
    string? TerminalIdentifier,
    string? BuyerNameSpace,
    string? BuyerTinSpace,
    string? BuyerAddressSpace,
    IReadOnlyList<InvoiceRenderLine> Lines,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents,
    IReadOnlyList<InvoiceRenderPayment> Payments,
    string TaxpayerMode,
    IReadOnlyList<InvoiceRenderTaxBucket> TaxBuckets,
    string? NonVatDisclaimer,
    IReadOnlyList<string> MissingOptionalFields,
    IReadOnlyList<string> BoundaryNotes,
    bool RequiresInternalWatermark,
    string InternalWatermark,
    string SnapshotPayloadHash);

public sealed record InvoiceOutputManifest(
    string PackageId,
    Guid SaleId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    string TaxpayerMode,
    string InvoiceVariant,
    string RendererName,
    string RendererVersion,
    string MediaFormat,
    string CanonicalContentSha256,
    InvoiceOutputMode OutputMode,
    bool InternalWatermarkApplied,
    bool ProductionActivationEnabled,
    bool LiveOfficialPostingAllowed,
    bool ComplianceClaimed,
    IReadOnlyList<string> MissingOptionalFields,
    IReadOnlyList<string> ValidationNotes,
    string NoComplianceClaimNote);

public sealed record InvoiceOutputArtifact(
    InvoiceOutputFormat Format,
    string ContentType,
    string FileNameHint,
    string Utf8Text,
    string ContentSha256);

public sealed record InvoiceOutputPackage(
    InvoiceRenderModel Model,
    InvoiceOutputManifest Manifest,
    IReadOnlyList<InvoiceOutputArtifact> Artifacts);

public sealed record InvoiceOutputRequest(
    Guid? SaleId,
    string? OfficialDocumentNumber,
    InvoiceOutputMode Mode,
    IReadOnlyList<InvoiceOutputFormat> Formats);

public static class InvoiceOutputConstants
{
    public const string RendererContractVersion = "TRAD-M3-INVOICE-RENDER-V1";
    public const string HtmlRendererName = "SalrynInvoiceHtmlRenderer";
    public const string ThermalRendererName = "SalrynInvoiceThermalTextRenderer";
    public const string RendererVersion = "2026-07-10";
    public const string InternalWatermark = "INTERNAL TEST — NOT AN OFFICIAL INVOICE";
    public const string NonVatInputTaxDisclaimer =
        "THIS DOCUMENT IS NOT VALID FOR CLAIM OF INPUT TAX.";
    public const string NoComplianceClaimNote =
        "Engineering invoice projection only. Not BIR accreditation, CoA, PTU, or legal issuance.";
    public const string SnapshotSchema = "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1";
    public const int ThermalWidth = 42;
}

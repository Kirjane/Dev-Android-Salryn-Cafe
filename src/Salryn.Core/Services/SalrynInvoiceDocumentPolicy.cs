using Salryn.Core.Models;

namespace Salryn.Core.Services;

/// <summary>
/// Invoice-first document policy for Traditional CRM/POS (EOPT / RR 7-2024 / RMC 77-2024 orientation).
/// Engineering policy spine only — not legal advice or accreditation.
/// </summary>
public static class SalrynInvoiceDocumentPolicy
{
    public const string PolicyVersion = "TRAD-M1-INVOICE-DOCUMENT-POLICY-2026-07-10";

    /// <summary>Primary sales document for goods and services under Invoice-first policy.</summary>
    public const string PrimarySalesDocumentKind = "OFFICIAL_INVOICE";

    /// <summary>Legacy / supplementary document kind still recognized in setup text.</summary>
    public const string SupplementaryReceiptDocumentKind = "OFFICIAL_RECEIPT";

    public const string VatInvoiceDisplayTitle = "VAT Invoice";

    public const string NonVatInvoiceDisplayTitle = "Non-VAT Invoice";

    /// <summary>
    /// Statutory non-VAT invoice threshold is CPI-adjustable under BIR circulars.
    /// This constant is documentation only and must not be buried as a permanent checkout gate.
    /// RMC 77-2024 orientation referenced ₱500; Salryn operational policy may be stricter.
    /// </summary>
    public const long ReferenceNonVatStatutoryThresholdCentavosOrientationOnly = 50_000;

    /// <summary>
    /// Salryn MSME operational policy for this Traditional spine: issue an invoice for every
    /// completed POS sale (VAT and non-VAT). Stricter than the non-VAT statutory minimum.
    /// </summary>
    public const bool IssueInvoiceForEveryCompletedPosSale = true;

    public const string AlwaysIssueOperationalPolicyNote =
        "Operational policy: issue Invoice for every completed POS sale (VAT and non-VAT). " +
        "Non-VAT statutory threshold is CPI-adjustable and is not hard-coded as a permanent checkout constant.";

    public const string InvoiceFirstBoundaryNote =
        "Invoice is the primary sales document; Official Receipt is supplementary under EOPT transition orientation.";

    public const string NoAccreditationClaimNote =
        "Invoice policy does not authorize official issuance without CoA/PTU and explicit activation.";

    public static string ResolveInvoiceDisplayTitle(TaxpayerRegistrationModeCodesProxy modeIsVat) =>
        modeIsVat == TaxpayerRegistrationModeCodesProxy.Vat
            ? VatInvoiceDisplayTitle
            : NonVatInvoiceDisplayTitle;

    public static string ResolveInvoiceDisplayTitleFromModeCode(string? vatRegistrationModeCode)
    {
        if (string.Equals(
                vatRegistrationModeCode?.Trim(),
                TaxpayerRegistrationModeCodes.VatRegistered,
                StringComparison.OrdinalIgnoreCase))
        {
            return VatInvoiceDisplayTitle;
        }

        return NonVatInvoiceDisplayTitle;
    }

    public static IReadOnlyList<string> BoundaryNotes { get; } =
        new[]
        {
            InvoiceFirstBoundaryNote,
            AlwaysIssueOperationalPolicyNote,
            NoAccreditationClaimNote
        };
}

/// <summary>Tiny discriminator to avoid enum import cycles in display title helper.</summary>
public enum TaxpayerRegistrationModeCodesProxy
{
    Vat = 1,
    NonVat = 2
}

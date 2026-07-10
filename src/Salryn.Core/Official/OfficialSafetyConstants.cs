namespace Salryn.Core.Official;

/// <summary>
/// Centralized internal-only safety strings and forced flag values for RMO engineering surfaces.
/// Not a compliance claim. Not legal issuance.
/// </summary>
public static class OfficialSafetyConstants
{
    public const string InternalWatermark =
        "INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE";

    public const string InternalOnlyNote =
        "Internal dry-run only. Not an official receipt or invoice.";

    public const string NoOfficialNumberIssued =
        "No official number is issued.";

    public const string CheckoutNotConnected =
        "Checkout is not connected to official numbering.";

    public const string LivePostingBlocked =
        "Live official posting is blocked.";

    public const string LegalClosureIncomplete =
        "Legal/source closure is incomplete; live official issuance remains blocked.";

    /// <summary>Forced safety flags for dry-run / report-preview / candidate responses.</summary>
    public static OfficialDryRunPackageFlags ForcedNonLivePackageFlags(bool sourceSaleReadOnly = false) =>
        new(
            DryRunOnly: true,
            NonOfficial: true,
            LiveOfficialPostingAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            SequenceWillMutate: false,
            SalesWillMutate: false,
            EjWillAppendLive: false,
            OutputWillGenerate: false,
            OfficialReceiptIssued: false,
            ComplianceClaimed: false,
            SourceSaleReadOnly: sourceSaleReadOnly);
}

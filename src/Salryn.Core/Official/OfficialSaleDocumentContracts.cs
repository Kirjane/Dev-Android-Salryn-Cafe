namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M3 versioned official-sale data spine.
/// Contracts only — not rendered output, not accreditation, not live posting.
/// </summary>
public static class OfficialContractVersions
{
    /// <summary>Initial M3 spine version for sale document, readiness, and journal placeholders.</summary>
    public const int M3 = 1;
}

/// <summary>
/// Distinguishes operational host POS numbers from BIR official accountable series numbers.
/// </summary>
public enum OfficialNumberingKind
{
    /// <summary>Day-to-day host checkout number (e.g. HostOperatorReceiptNumberAllocator). Not official.</summary>
    OperationalPosNumber = 1,

    /// <summary>Accountable official document series number (bir_official_document_sequences). Not allocated by M3.</summary>
    OfficialDocumentNumber = 2
}

public enum OfficialDocumentKind
{
    OfficialReceipt = 1,
    OfficialInvoice = 2
}

public sealed record OfficialSaleDocumentMarkers(
    bool IsTrainingMode,
    bool IsReprint,
    int ReprintCount,
    bool IsVoided,
    bool IsRefunded,
    bool IsCanceledBeforePost,
    bool IsSupplementaryDocument,
    bool IsNonOfficialOutput);

public sealed record OfficialSaleLineContract(
    string Description,
    string? Barcode,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents,
    string? TaxClassCode);

public sealed record OfficialSaleTaxBreakdownContract(
    string TaxModeCode,
    long VatableSalesCents,
    long VatAmountCents,
    long VatExemptSalesCents,
    long ZeroRatedSalesCents,
    long NonVatSalesCents,
    bool BucketEngineDeferred,
    string? Notes);

public sealed record OfficialSalePaymentContract(
    string PaymentMethodCode,
    long AmountCents,
    DateTimeOffset PaidAtUtc);

public sealed record OfficialSaleCustomerContract(
    string? Name,
    string? Address,
    string? TaxpayerIdentificationNumber,
    string? BusinessStyle);

public sealed record OfficialSaleDiscountContract(
    string DiscountTypeCode,
    long AmountCents,
    string? StatutoryProgramCode,
    string? Identifier);

public sealed record OfficialBusinessProfileSnapshot(
    string RegisteredTaxpayerName,
    string? BusinessName,
    string RegisteredAddress,
    string? BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string? VatRegistrationPhrase);

public sealed record OfficialMachineProfileSnapshot(
    Guid? BirMachineProfileId,
    string? MachineIdentificationNumber,
    string MachineSerialNumber,
    string? PosTerminalNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string SoftwareName,
    string SoftwareVersion,
    string? SoftwareBuild,
    string ComplianceMode,
    bool OfficialOutputEnabledInConfig,
    bool IsActive);

public sealed record OfficialReceiptProfileSnapshot(
    OfficialDocumentKind DocumentKind,
    string? SeriesPrefix,
    int? PaddingDigits,
    string? FooterLegalText,
    string? SupplementaryDisclaimerText,
    string? SupplierOfRecordFooterText);

/// <summary>
/// Canonical official sale document shape for future REPORTS-M1.
/// M3 does not generate or persist this for live checkout.
/// </summary>
public sealed record OfficialSaleDocumentContract(
    int ContractVersion,
    OfficialDocumentKind DocumentKind,
    OfficialNumberingKind NumberingKind,
    string? OperationalPosNumber,
    string? OfficialDocumentNumber,
    Guid? OfficialSequenceId,
    DateTimeOffset TransactionDateTimeUtc,
    string CashierIdentifier,
    string? TerminalIdentifier,
    OfficialBusinessProfileSnapshot? BusinessProfile,
    OfficialMachineProfileSnapshot? MachineProfile,
    OfficialReceiptProfileSnapshot? ReceiptProfile,
    IReadOnlyList<OfficialSaleLineContract> Lines,
    OfficialSaleTaxBreakdownContract? TaxBreakdown,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents,
    IReadOnlyList<OfficialSalePaymentContract> Payments,
    OfficialSaleCustomerContract? Customer,
    IReadOnlyList<OfficialSaleDiscountContract> Discounts,
    OfficialSaleDocumentMarkers Markers,
    string? ContentHashSha256,
    string? Notes);

public sealed record OfficialDocumentSeriesContract(
    int ContractVersion,
    Guid? SequenceId,
    Guid? BirMachineProfileId,
    OfficialDocumentKind DocumentKind,
    string SequenceScopeCode,
    string Prefix,
    int Year,
    int StartNumber,
    int CurrentNumber,
    int? EndNumber,
    int PaddingDigits,
    bool IsActive,
    bool IsExhausted);

public sealed record OfficialJournalEventContract(
    int ContractVersion,
    string EventTypeCode,
    DateTimeOffset OccurredAtUtc,
    string? OfficialDocumentNumber,
    string? OperationalPosNumber,
    Guid? SaleId,
    string? ActorUserId,
    string? TerminalIdentifier,
    string? PayloadJson,
    string? ContentHashSha256);

public sealed record OfficialAuditLinkContract(
    int ContractVersion,
    string OperationCode,
    DateTimeOffset OccurredAtUtc,
    string? ActorUserId,
    string? RoleCode,
    string? EntityType,
    string? EntityId,
    string? ReasonCode,
    string? CorrelationId);

public sealed record OfficialVoidRefundLinkContract(
    int ContractVersion,
    string CorrectionKindCode,
    string OriginalOfficialDocumentNumber,
    string? CorrectionDocumentNumber,
    string ReasonCode,
    string ActorUserId,
    DateTimeOffset OccurredAtUtc);

namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M4 host official setup contracts. Setup only — not official output, not compliance.
/// </summary>
public sealed record OfficialSetupBusinessProfileContract(
    string RegisteredTaxpayerName,
    string? BusinessName,
    string RegisteredAddress,
    string? BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string? VatRegistrationMode,
    string? ContactNote);

public sealed record OfficialSetupMachineProfileContract(
    Guid? ProfileId,
    string MachineSerialNumber,
    string? MachineIdentificationNumber,
    string? PosTerminalNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string? SoftwareVersion,
    string? SoftwareBuild,
    string ComplianceMode,
    bool OfficialOutputEnabledInConfig,
    bool IsActive);

public sealed record OfficialSetupReceiptProfileContract(
    Guid? ProfileId,
    string DocumentTitle,
    string DocumentKind,
    string? FooterLegalText,
    string? SupplementaryDisclaimerText,
    string? ReprintMarkerText,
    string? TrainingMarkerText,
    string? VoidRefundMarkerText,
    string? InvoiceOrTransitionMode,
    bool ScPwdPlaceholderEnabled,
    bool DryRunOnly);

public sealed record OfficialSetupDocumentSeriesContract(
    Guid? SeriesId,
    Guid? BirMachineProfileId,
    string DocumentKind,
    string Prefix,
    int Year,
    int StartNumber,
    int CurrentNumber,
    int? EndNumber,
    int PaddingDigits,
    bool IsActive,
    bool IsExhausted,
    bool DryRunOnly);

public sealed record OfficialSetupSnapshotContract(
    int ContractVersion,
    OfficialSetupBusinessProfileContract? Business,
    OfficialSetupMachineProfileContract? Machine,
    OfficialSetupReceiptProfileContract? Receipt,
    OfficialSetupDocumentSeriesContract? Series,
    bool LiveOfficialPostingAllowed,
    IReadOnlyList<string> Warnings,
    string Summary);

public sealed record OfficialSetupBusinessSaveRequest(
    string RegisteredTaxpayerName,
    string? BusinessName,
    string RegisteredAddress,
    string? BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string? VatRegistrationMode,
    string? ContactNote);

public sealed record OfficialSetupMachineSaveRequest(
    string MachineSerialNumber,
    string? MachineIdentificationNumber,
    string? PosTerminalNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string? SoftwareVersion,
    string? SoftwareBuild);

public sealed record OfficialSetupReceiptSaveRequest(
    string DocumentTitle,
    string DocumentKind,
    string? FooterLegalText,
    string? SupplementaryDisclaimerText,
    string? ReprintMarkerText,
    string? TrainingMarkerText,
    string? VoidRefundMarkerText,
    string? InvoiceOrTransitionMode,
    bool ScPwdPlaceholderEnabled);

public sealed record OfficialSetupSeriesSaveRequest(
    string DocumentKind,
    string Prefix,
    int Year,
    int StartNumber,
    int? EndNumber,
    int PaddingDigits);

namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M7 report/export data handoff contracts for future REPORTS-M1.
/// Data shape only — no Crystal/PDF/printer/file generation.
/// </summary>
public sealed record OfficialReportOutputReadinessContract(
    int ContractVersion,
    bool ReportEngineSelected,
    bool CrystalSelected,
    bool OutputGenerated,
    bool OfficialReceiptIssued,
    bool LiveOfficialPostingAllowed,
    bool ComplianceClaimed,
    string Summary);

public sealed record OfficialReceiptReportDataContract(
    int ContractVersion,
    bool DataShapeDefined,
    bool LiveDataBound,
    string Summary);

public sealed record OfficialXReadingReportDataContract(
    int ContractVersion,
    bool DataShapeDefined,
    bool LiveDataBound,
    string Summary);

public sealed record OfficialZReadingReportDataContract(
    int ContractVersion,
    bool DataShapeDefined,
    bool LiveDataBound,
    string Summary);

public sealed record OfficialJournalExportDataContract(
    int ContractVersion,
    bool DataShapeDefined,
    bool LiveDataBound,
    bool ExportReady,
    string Summary);

public sealed record OfficialVoidRefundReportDataContract(
    int ContractVersion,
    bool DataShapeDefined,
    bool LiveDataBound,
    string Summary);

public sealed record OfficialReportDataPackageContract(
    int ContractVersion,
    bool PackageDefined,
    OfficialReportOutputReadinessContract OutputReadiness,
    OfficialReceiptReportDataContract Receipt,
    OfficialXReadingReportDataContract XReading,
    OfficialZReadingReportDataContract ZReading,
    OfficialJournalExportDataContract JournalExport,
    OfficialVoidRefundReportDataContract VoidRefund,
    string Summary);

namespace Salryn.Core.Official;

/// <summary>
/// RMO-BIR-M8 internal report preview contracts.
/// Watermarked internal preview only — not legal output, not PDF/printer/Crystal.
/// </summary>
public enum OfficialReportPreviewFormat
{
    Json = 1,
    Html = 2,
    PlainText = 3,
    InternalOnly = 4
}

public sealed record OfficialReceiptPreviewContract(
    int ContractVersion,
    OfficialReportPreviewFormat Format,
    string Watermark,
    string DocumentKind,
    string? PreviewOfficialNumber,
    string? OperationalNumberHint,
    IReadOnlyList<string> HeaderLines,
    IReadOnlyList<string> BodyLines,
    IReadOnlyList<string> FooterLines,
    string PlainTextPreview,
    bool OfficialReceiptIssued);

public sealed record OfficialXReadingPreviewContract(
    int ContractVersion,
    OfficialReportPreviewFormat Format,
    string Watermark,
    string PlainTextPreview,
    bool ResetsCounters,
    string Summary);

public sealed record OfficialZReadingPreviewContract(
    int ContractVersion,
    OfficialReportPreviewFormat Format,
    string Watermark,
    string PlainTextPreview,
    bool ResetsCounters,
    string Summary);

public sealed record OfficialJournalExportPreviewContract(
    int ContractVersion,
    OfficialReportPreviewFormat Format,
    string Watermark,
    string PlainTextPreview,
    bool LiveAppended,
    bool ExportReady,
    string Summary);

public sealed record OfficialVoidRefundPreviewContract(
    int ContractVersion,
    OfficialReportPreviewFormat Format,
    string Watermark,
    string PlainTextPreview,
    bool LiveIntegrated,
    IReadOnlyList<string> PlaceholderReasonCodes,
    string Summary);

public sealed record OfficialReportPreviewPackageContract(
    int ContractVersion,
    string Watermark,
    bool DryRunOnly,
    bool NonOfficial,
    bool OfficialReceiptIssued,
    bool LiveOfficialPostingAllowed,
    bool OutputWillGenerate,
    bool ReportEngineSelected,
    bool ComplianceClaimed,
    OfficialReceiptPreviewContract Receipt,
    OfficialXReadingPreviewContract XReading,
    OfficialZReadingPreviewContract ZReading,
    OfficialJournalExportPreviewContract JournalExport,
    OfficialVoidRefundPreviewContract VoidRefund,
    string Summary);

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialAuditEvidenceReportKind
{
    AuditTrail = 1,
    ElectronicJournal = 2
}

public sealed record OfficialAuditEvidenceReportInput(
    OfficialAuditEvidenceReportKind ReportKind,
    string? ReportNumber,
    string? ReferenceNumber,
    DateTimeOffset GeneratedAt,
    string? TerminalId,
    Guid? CashierUserId,
    string? CashierIdentifier,
    string? ReportLabel,
    OfficialAuditEvidenceMachineProfile? MachineProfile,
    OfficialAuditEvidenceReportScope? Scope,
    OfficialAuditEvidenceEventSummary? EventSummary,
    IReadOnlyList<OfficialAuditEvidenceEventLine>? JournalEvidence,
    IReadOnlyList<OfficialAuditEvidenceLifecycleLine>? LifecycleTrace,
    IReadOnlyList<OfficialAuditEvidenceOutputLine>? OutputAndReprintTrace,
    IReadOnlyList<OfficialAuditEvidenceIntegrityIssue>? IntegrityIssues,
    OfficialAuditEvidenceReportEvidence? Evidence);

public sealed record OfficialAuditEvidenceReportScope(
    string? ScopeKind,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateOnly? BusinessDate,
    string? DateRange,
    string? TerminalId,
    Guid? CashierUserId,
    string? CashierIdentifier,
    Guid? SaleId,
    Guid? FirstJournalEntryId,
    Guid? LastJournalEntryId,
    int EventCount,
    int? CoveredSaleCount,
    int? CoveredOfficialDocumentCount);

public sealed record OfficialAuditEvidenceMachineProfile(
    string RegisteredTaxpayerName,
    string BusinessName,
    string RegisteredAddress,
    string BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string MachineIdentificationNumber,
    string MachineSerialNumber,
    string PosTerminalNumber,
    string AccreditationNumber,
    string PermitToUseNumber,
    string SoftwareName,
    string SoftwareVersion,
    string SoftwareBuild,
    string ComplianceMode);

public sealed record OfficialAuditEvidenceEventSummary(
    int TotalEventCount,
    int? SaleEventCount,
    int VoidEventCount,
    int CancelEventCount,
    int CorrectionEventCount,
    int ReprintEventCount,
    int CopyEventCount,
    int OutputEventCount,
    int IntegrityIssueCount);

public sealed record OfficialAuditEvidenceEventLine(
    int SequenceNumber,
    Guid JournalEntryId,
    Guid OriginalEventId,
    string EventKind,
    DateTimeOffset EventTimestamp,
    Guid? SaleId,
    string? OfficialDocumentNumber,
    string? CashierIdentifier,
    string? TerminalId,
    string SnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    bool SnapshotHashMatches);

public sealed record OfficialAuditEvidenceLifecycleLine(
    int SequenceNumber,
    Guid SaleId,
    string OfficialDocumentNumber,
    string LifecycleState,
    string NoticeCode,
    string NoticeText,
    int CorrectionCount,
    bool IsCoveredByFinalizedZReading,
    Guid? CoveringZReadingId,
    string? CorrectionNote);

public sealed record OfficialAuditEvidenceOutputLine(
    int SequenceNumber,
    string OfficialDocumentNumber,
    string OutputKind,
    string? DocumentKind,
    string CopyMode,
    string OutputMarker,
    bool IsCopyOrReprint,
    int PriorReprintEventCount,
    string? RenderFormat,
    string? ArtifactReference,
    string? ContentHash);

public sealed record OfficialAuditEvidenceIntegrityIssue(
    int IssueNumber,
    string Code,
    string Message,
    string? Severity,
    Guid? RelatedEventId,
    Guid? RelatedJournalEntryId);

public sealed record OfficialAuditEvidenceReportEvidence(
    string? SourceEvidenceHash,
    string? SnapshotPayloadHash,
    string HashAlgorithm,
    string GeneratedFrom);

public sealed record OfficialAuditEvidenceReportLayoutDocument(
    OfficialAuditEvidenceReportKind ReportKind,
    IReadOnlyList<OfficialAuditEvidenceReportLayoutSection> Sections);

public sealed record OfficialAuditEvidenceReportLayoutSection(
    string Name,
    IReadOnlyList<OfficialAuditEvidenceReportLayoutLine> Lines);

public sealed record OfficialAuditEvidenceReportLayoutLine(
    string Label,
    string Value);

public sealed record OfficialAuditEvidenceReportTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public sealed record OfficialAuditEvidenceReportPreviewArtifactPackage(
    OfficialAuditEvidenceReportKind ReportKind,
    string ArtifactLabel,
    string ArtifactReference,
    string ContentHash,
    int LineCount,
    OfficialAuditEvidenceReportTextRenderResult TextResult);

public sealed record OfficialAuditEvidenceReportPreviewResult(
    OfficialAuditEvidenceReportKind ReportKind,
    OfficialAuditEvidenceReportLayoutDocument LayoutDocument,
    OfficialAuditEvidenceReportTextRenderResult TextResult,
    OfficialAuditEvidenceReportPreviewArtifactPackage ArtifactPackage,
    string ArtifactLabel,
    string ArtifactReference,
    string ContentHash,
    int LineCount);

// Kirch: why - audit and electronic journal evidence output needs a deterministic Core report shape before file, printer, or template adapters exist.
public sealed class OfficialAuditEvidenceReportLayoutBuilder
{
    private const string PackageComputedMarker = "COMPUTED_IN_PREVIEW_ARTIFACT";

    public Result<OfficialAuditEvidenceReportLayoutDocument> Build(
        OfficialAuditEvidenceReportInput? input)
    {
        Result validation = ValidateInput(input);

        if (validation.IsFailure)
        {
            return Result<OfficialAuditEvidenceReportLayoutDocument>.Fail(validation.ErrorDetail);
        }

        OfficialAuditEvidenceReportInput report = input!;

        OfficialAuditEvidenceReportLayoutDocument document = new(
            report.ReportKind,
            new[]
            {
                BuildReportHeader(report),
                BuildMachineProfile(report.MachineProfile!),
                BuildScope(report.Scope!),
                BuildEventSummary(report.EventSummary!),
                BuildJournalEvidence(report.JournalEvidence!),
                BuildLifecycleTrace(report.LifecycleTrace!),
                BuildOutputAndReprintTrace(report.OutputAndReprintTrace!),
                BuildIntegrityIssues(report.IntegrityIssues!),
                BuildEvidenceFooter(report.Evidence!)
            });

        return Result<OfficialAuditEvidenceReportLayoutDocument>.Success(document);
    }

    public static Result ValidateInput(OfficialAuditEvidenceReportInput? input)
    {
        if (input is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.InputRequired",
                "Official audit/electronic journal evidence report input is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialAuditEvidenceReportKind), input.ReportKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.InvalidKind",
                "Official audit/electronic journal evidence report kind is invalid."));
        }

        if (input.GeneratedAt == default)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.HeaderRequired",
                "Official audit/electronic journal evidence report header is incomplete."));
        }

        if (input.CashierUserId.HasValue && input.CashierUserId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.InvalidCashierUserId",
                "Official audit/electronic journal evidence report cashier user id is invalid."));
        }

        Result machineProfileValidation = ValidateMachineProfile(input.MachineProfile);

        if (machineProfileValidation.IsFailure)
        {
            return machineProfileValidation;
        }

        Result scopeValidation = ValidateScope(input.Scope);

        if (scopeValidation.IsFailure)
        {
            return scopeValidation;
        }

        Result eventSummaryValidation = ValidateEventSummary(input.EventSummary);

        if (eventSummaryValidation.IsFailure)
        {
            return eventSummaryValidation;
        }

        Result journalEvidenceValidation = ValidateJournalEvidence(input.JournalEvidence);

        if (journalEvidenceValidation.IsFailure)
        {
            return journalEvidenceValidation;
        }

        Result lifecycleTraceValidation = ValidateLifecycleTrace(input.LifecycleTrace);

        if (lifecycleTraceValidation.IsFailure)
        {
            return lifecycleTraceValidation;
        }

        Result outputTraceValidation = ValidateOutputAndReprintTrace(input.OutputAndReprintTrace);

        if (outputTraceValidation.IsFailure)
        {
            return outputTraceValidation;
        }

        Result integrityIssuesValidation = ValidateIntegrityIssues(input.IntegrityIssues);

        if (integrityIssuesValidation.IsFailure)
        {
            return integrityIssuesValidation;
        }

        return ValidateEvidence(input.Evidence);
    }

    private static Result ValidateMachineProfile(
        OfficialAuditEvidenceMachineProfile? machineProfile)
    {
        if (machineProfile is null ||
            string.IsNullOrWhiteSpace(machineProfile.RegisteredTaxpayerName) ||
            string.IsNullOrWhiteSpace(machineProfile.RegisteredAddress) ||
            string.IsNullOrWhiteSpace(machineProfile.TaxpayerIdentificationNumber) ||
            string.IsNullOrWhiteSpace(machineProfile.BranchCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.MachineProfileRequired",
                "Official audit/electronic journal evidence report requires machine profile identity."));
        }

        return Result.Success();
    }

    private static Result ValidateScope(
        OfficialAuditEvidenceReportScope? scope)
    {
        if (scope is null ||
            scope.PeriodStart == default ||
            scope.PeriodEnd == default ||
            scope.PeriodEnd <= scope.PeriodStart ||
            scope.EventCount < 0 ||
            scope.CoveredSaleCount is < 0 ||
            scope.CoveredOfficialDocumentCount is < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.InvalidScope",
                "Official audit/electronic journal evidence report scope is invalid."));
        }

        if (scope.CashierUserId == Guid.Empty ||
            scope.SaleId == Guid.Empty ||
            scope.FirstJournalEntryId == Guid.Empty ||
            scope.LastJournalEntryId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.InvalidScopeId",
                "Official audit/electronic journal evidence report scope id is invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateEventSummary(
        OfficialAuditEvidenceEventSummary? summary)
    {
        if (summary is null ||
            summary.TotalEventCount < 0 ||
            summary.SaleEventCount is < 0 ||
            summary.VoidEventCount < 0 ||
            summary.CancelEventCount < 0 ||
            summary.CorrectionEventCount < 0 ||
            summary.ReprintEventCount < 0 ||
            summary.CopyEventCount < 0 ||
            summary.OutputEventCount < 0 ||
            summary.IntegrityIssueCount < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.InvalidEventSummary",
                "Official audit/electronic journal evidence report event summary is invalid."));
        }

        return Result.Success();
    }

    private static Result ValidateJournalEvidence(
        IReadOnlyList<OfficialAuditEvidenceEventLine>? events)
    {
        if (events is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.JournalEvidenceRequired",
                "Official audit/electronic journal evidence report requires journal evidence lines."));
        }

        HashSet<int> sequenceNumbers = new();

        foreach (OfficialAuditEvidenceEventLine line in events)
        {
            if (line.SequenceNumber <= 0 ||
                line.JournalEntryId == Guid.Empty ||
                line.OriginalEventId == Guid.Empty ||
                string.IsNullOrWhiteSpace(line.EventKind) ||
                line.EventTimestamp == default ||
                line.SaleId == Guid.Empty ||
                string.IsNullOrWhiteSpace(line.SnapshotPayloadHash) ||
                string.IsNullOrWhiteSpace(line.SnapshotHashAlgorithm))
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.InvalidJournalEvidenceLine",
                    "Official audit/electronic journal evidence report journal evidence line is invalid."));
            }

            if (!sequenceNumbers.Add(line.SequenceNumber))
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.DuplicateEventSequenceNumber",
                    "Official audit/electronic journal evidence report contains duplicate event sequence numbers."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateLifecycleTrace(
        IReadOnlyList<OfficialAuditEvidenceLifecycleLine>? lines)
    {
        if (lines is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.LifecycleTraceRequired",
                "Official audit/electronic journal evidence report requires lifecycle trace lines."));
        }

        HashSet<int> sequenceNumbers = new();

        foreach (OfficialAuditEvidenceLifecycleLine line in lines)
        {
            if (line.SequenceNumber <= 0 ||
                line.SaleId == Guid.Empty ||
                string.IsNullOrWhiteSpace(line.OfficialDocumentNumber) ||
                string.IsNullOrWhiteSpace(line.LifecycleState) ||
                string.IsNullOrWhiteSpace(line.NoticeCode) ||
                string.IsNullOrWhiteSpace(line.NoticeText) ||
                line.CorrectionCount < 0 ||
                line.CoveringZReadingId == Guid.Empty)
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.InvalidLifecycleTraceLine",
                    "Official audit/electronic journal evidence report lifecycle trace line is invalid."));
            }

            if (!sequenceNumbers.Add(line.SequenceNumber))
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.DuplicateLifecycleSequenceNumber",
                    "Official audit/electronic journal evidence report contains duplicate lifecycle sequence numbers."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateOutputAndReprintTrace(
        IReadOnlyList<OfficialAuditEvidenceOutputLine>? lines)
    {
        if (lines is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.OutputTraceRequired",
                "Official audit/electronic journal evidence report requires output and reprint trace lines."));
        }

        HashSet<int> sequenceNumbers = new();

        foreach (OfficialAuditEvidenceOutputLine line in lines)
        {
            if (line.SequenceNumber <= 0 ||
                string.IsNullOrWhiteSpace(line.OfficialDocumentNumber) ||
                string.IsNullOrWhiteSpace(line.OutputKind) ||
                string.IsNullOrWhiteSpace(line.CopyMode) ||
                string.IsNullOrWhiteSpace(line.OutputMarker) ||
                line.PriorReprintEventCount < 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.InvalidOutputTraceLine",
                    "Official audit/electronic journal evidence report output and reprint trace line is invalid."));
            }

            if (!sequenceNumbers.Add(line.SequenceNumber))
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.DuplicateOutputSequenceNumber",
                    "Official audit/electronic journal evidence report contains duplicate output sequence numbers."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateIntegrityIssues(
        IReadOnlyList<OfficialAuditEvidenceIntegrityIssue>? issues)
    {
        if (issues is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.IntegrityIssuesRequired",
                "Official audit/electronic journal evidence report requires integrity issue lines."));
        }

        HashSet<int> issueNumbers = new();

        foreach (OfficialAuditEvidenceIntegrityIssue issue in issues)
        {
            if (issue.IssueNumber <= 0 ||
                string.IsNullOrWhiteSpace(issue.Code) ||
                string.IsNullOrWhiteSpace(issue.Message) ||
                issue.RelatedEventId == Guid.Empty ||
                issue.RelatedJournalEntryId == Guid.Empty)
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.InvalidIntegrityIssue",
                    "Official audit/electronic journal evidence report integrity issue is invalid."));
            }

            if (!issueNumbers.Add(issue.IssueNumber))
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReport.DuplicateIntegrityIssueNumber",
                    "Official audit/electronic journal evidence report contains duplicate integrity issue numbers."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateEvidence(
        OfficialAuditEvidenceReportEvidence? evidence)
    {
        if (evidence is null ||
            string.IsNullOrWhiteSpace(evidence.HashAlgorithm) ||
            string.IsNullOrWhiteSpace(evidence.GeneratedFrom) ||
            (string.IsNullOrWhiteSpace(evidence.SourceEvidenceHash) &&
                string.IsNullOrWhiteSpace(evidence.SnapshotPayloadHash)))
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReport.EvidenceRequired",
                "Official audit/electronic journal evidence report evidence footer is required."));
        }

        return Result.Success();
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildReportHeader(
        OfficialAuditEvidenceReportInput input)
    {
        List<OfficialAuditEvidenceReportLayoutLine> lines = new();

        Add(lines, "ReportKind", input.ReportKind.ToString());
        AddOptional(lines, "ReportNumber", input.ReportNumber);
        AddOptional(lines, "ReferenceNumber", input.ReferenceNumber);
        Add(lines, "GeneratedAt", Format(input.GeneratedAt));
        AddOptional(lines, "TerminalId", input.TerminalId);
        AddOptional(lines, "CashierUserId", input.CashierUserId);
        AddOptional(lines, "CashierIdentifier", input.CashierIdentifier);
        Add(lines, "ReportLabel", Normalize(input.ReportLabel).Length == 0
            ? DefaultArtifactLabel(input.ReportKind)
            : Normalize(input.ReportLabel));

        return Section("ReportHeader", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildMachineProfile(
        OfficialAuditEvidenceMachineProfile profile)
    {
        List<OfficialAuditEvidenceReportLayoutLine> lines = new();

        Add(lines, "RegisteredTaxpayerName", profile.RegisteredTaxpayerName);
        Add(lines, "BusinessName", profile.BusinessName);
        Add(lines, "RegisteredAddress", profile.RegisteredAddress);
        Add(lines, "BranchAddress", profile.BranchAddress);
        Add(lines, "TaxpayerIdentificationNumber", profile.TaxpayerIdentificationNumber);
        Add(lines, "BranchCode", profile.BranchCode);
        Add(lines, "MachineIdentificationNumber", profile.MachineIdentificationNumber);
        Add(lines, "MachineSerialNumber", profile.MachineSerialNumber);
        Add(lines, "PosTerminalNumber", profile.PosTerminalNumber);
        Add(lines, "AccreditationNumber", profile.AccreditationNumber);
        Add(lines, "PermitToUseNumber", profile.PermitToUseNumber);
        Add(lines, "SoftwareName", profile.SoftwareName);
        Add(lines, "SoftwareVersion", profile.SoftwareVersion);
        Add(lines, "SoftwareBuild", profile.SoftwareBuild);
        Add(lines, "ComplianceMode", profile.ComplianceMode);

        return Section("MachineProfile", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildScope(
        OfficialAuditEvidenceReportScope scope)
    {
        List<OfficialAuditEvidenceReportLayoutLine> lines = new();

        AddOptional(lines, "ScopeKind", scope.ScopeKind);
        Add(lines, "PeriodStart", Format(scope.PeriodStart));
        Add(lines, "PeriodEnd", Format(scope.PeriodEnd));
        AddOptional(lines, "BusinessDate", scope.BusinessDate);
        AddOptional(lines, "DateRange", scope.DateRange);
        AddOptional(lines, "TerminalId", scope.TerminalId);
        AddOptional(lines, "CashierUserId", scope.CashierUserId);
        AddOptional(lines, "CashierIdentifier", scope.CashierIdentifier);
        AddOptional(lines, "SaleId", scope.SaleId);
        AddOptional(lines, "FirstJournalEntryId", scope.FirstJournalEntryId);
        AddOptional(lines, "LastJournalEntryId", scope.LastJournalEntryId);
        Add(lines, "EventCount", scope.EventCount);
        AddOptional(lines, "CoveredSaleCount", scope.CoveredSaleCount);
        AddOptional(lines, "CoveredOfficialDocumentCount", scope.CoveredOfficialDocumentCount);

        return Section("Scope", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildEventSummary(
        OfficialAuditEvidenceEventSummary summary)
    {
        List<OfficialAuditEvidenceReportLayoutLine> lines = new();

        Add(lines, "TotalEventCount", summary.TotalEventCount);
        AddOptional(lines, "SaleEventCount", summary.SaleEventCount);
        Add(lines, "VoidEventCount", summary.VoidEventCount);
        Add(lines, "CancelEventCount", summary.CancelEventCount);
        Add(lines, "CorrectionEventCount", summary.CorrectionEventCount);
        Add(lines, "ReprintEventCount", summary.ReprintEventCount);
        Add(lines, "CopyEventCount", summary.CopyEventCount);
        Add(lines, "OutputEventCount", summary.OutputEventCount);
        Add(lines, "IntegrityIssueCount", summary.IntegrityIssueCount);

        return Section("EventSummary", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildJournalEvidence(
        IReadOnlyList<OfficialAuditEvidenceEventLine> events)
    {
        OfficialAuditEvidenceReportLayoutLine[] lines = events
            .Select(line =>
            {
                List<(string Name, string Value)> fields = new()
                {
                    ("SequenceNumber", Format(line.SequenceNumber)),
                    ("JournalEntryId", Format(line.JournalEntryId)),
                    ("OriginalEventId", Format(line.OriginalEventId)),
                    ("EventKind", Normalize(line.EventKind)),
                    ("EventTimestamp", Format(line.EventTimestamp))
                };

                AddOptionalField(fields, "SaleId", line.SaleId);
                AddOptionalField(fields, "OfficialDocumentNumber", line.OfficialDocumentNumber);
                AddOptionalField(fields, "CashierIdentifier", line.CashierIdentifier);
                AddOptionalField(fields, "TerminalId", line.TerminalId);
                fields.Add(("SnapshotPayloadHash", Normalize(line.SnapshotPayloadHash)));
                fields.Add(("SnapshotHashAlgorithm", Normalize(line.SnapshotHashAlgorithm)));
                fields.Add(("SnapshotHashMatches", Format(line.SnapshotHashMatches)));

                return CompoundLine("JournalEvidence", fields);
            })
            .ToArray();

        return Section("JournalEvidence", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildLifecycleTrace(
        IReadOnlyList<OfficialAuditEvidenceLifecycleLine> trace)
    {
        OfficialAuditEvidenceReportLayoutLine[] lines = trace
            .Select(line =>
            {
                List<(string Name, string Value)> fields = new()
                {
                    ("SequenceNumber", Format(line.SequenceNumber)),
                    ("SaleId", Format(line.SaleId)),
                    ("OfficialDocumentNumber", Normalize(line.OfficialDocumentNumber)),
                    ("LifecycleState", Normalize(line.LifecycleState)),
                    ("NoticeCode", Normalize(line.NoticeCode)),
                    ("NoticeText", Normalize(line.NoticeText)),
                    ("CorrectionCount", Format(line.CorrectionCount)),
                    ("IsCoveredByFinalizedZReading", Format(line.IsCoveredByFinalizedZReading))
                };

                AddOptionalField(fields, "CoveringZReadingId", line.CoveringZReadingId);
                AddOptionalField(fields, "CorrectionNote", line.CorrectionNote);

                return CompoundLine("LifecycleTrace", fields);
            })
            .ToArray();

        return Section("LifecycleTrace", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildOutputAndReprintTrace(
        IReadOnlyList<OfficialAuditEvidenceOutputLine> trace)
    {
        OfficialAuditEvidenceReportLayoutLine[] lines = trace
            .Select(line =>
            {
                List<(string Name, string Value)> fields = new()
                {
                    ("SequenceNumber", Format(line.SequenceNumber)),
                    ("OfficialDocumentNumber", Normalize(line.OfficialDocumentNumber)),
                    ("OutputKind", Normalize(line.OutputKind))
                };

                AddOptionalField(fields, "DocumentKind", line.DocumentKind);
                fields.Add(("CopyMode", Normalize(line.CopyMode)));
                fields.Add(("OutputMarker", Normalize(line.OutputMarker)));
                fields.Add(("IsCopyOrReprint", Format(line.IsCopyOrReprint)));
                fields.Add(("PriorReprintEventCount", Format(line.PriorReprintEventCount)));
                AddOptionalField(fields, "RenderFormat", line.RenderFormat);
                AddOptionalField(fields, "ArtifactReference", line.ArtifactReference);
                AddOptionalField(fields, "ContentHash", line.ContentHash);

                return CompoundLine("OutputReprintTrace", fields);
            })
            .ToArray();

        return Section("OutputAndReprintTrace", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildIntegrityIssues(
        IReadOnlyList<OfficialAuditEvidenceIntegrityIssue> issues)
    {
        if (issues.Count == 0)
        {
            return Section(
                "IntegrityIssues",
                new[]
                {
                    new OfficialAuditEvidenceReportLayoutLine(
                        "IntegrityIssue",
                        "NO_INTEGRITY_ISSUES_REPORTED")
                });
        }

        OfficialAuditEvidenceReportLayoutLine[] lines = issues
            .Select(issue =>
            {
                List<(string Name, string Value)> fields = new()
                {
                    ("IssueNumber", Format(issue.IssueNumber)),
                    ("Code", Normalize(issue.Code)),
                    ("Message", Normalize(issue.Message))
                };

                AddOptionalField(fields, "Severity", issue.Severity);
                AddOptionalField(fields, "RelatedEventId", issue.RelatedEventId);
                AddOptionalField(fields, "RelatedJournalEntryId", issue.RelatedJournalEntryId);

                return CompoundLine("IntegrityIssue", fields);
            })
            .ToArray();

        return Section("IntegrityIssues", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection BuildEvidenceFooter(
        OfficialAuditEvidenceReportEvidence evidence)
    {
        List<OfficialAuditEvidenceReportLayoutLine> lines = new();

        AddOptional(lines, "SourceEvidenceHash", evidence.SourceEvidenceHash);
        AddOptional(lines, "SnapshotPayloadHash", evidence.SnapshotPayloadHash);
        Add(lines, "HashAlgorithm", evidence.HashAlgorithm);
        Add(lines, "GeneratedFrom", evidence.GeneratedFrom);
        Add(lines, "ContentHash", PackageComputedMarker);
        Add(lines, "ArtifactReference", PackageComputedMarker);
        Add(lines, "StateMutationMarker", "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION");

        return Section("EvidenceFooter", lines);
    }

    private static OfficialAuditEvidenceReportLayoutSection Section(
        string name,
        IReadOnlyList<OfficialAuditEvidenceReportLayoutLine> lines)
    {
        return new OfficialAuditEvidenceReportLayoutSection(name, lines.ToArray());
    }

    private static OfficialAuditEvidenceReportLayoutLine CompoundLine(
        string label,
        IReadOnlyList<(string Name, string Value)> fields)
    {
        return new OfficialAuditEvidenceReportLayoutLine(
            label,
            string.Join(" | ", fields.Select(static field => field.Name + "=" + Normalize(field.Value))));
    }

    private static void Add(
        List<OfficialAuditEvidenceReportLayoutLine> lines,
        string label,
        string? value)
    {
        lines.Add(new OfficialAuditEvidenceReportLayoutLine(label, Normalize(value)));
    }

    private static void Add(
        List<OfficialAuditEvidenceReportLayoutLine> lines,
        string label,
        int value)
    {
        Add(lines, label, Format(value));
    }

    private static void AddOptional(
        List<OfficialAuditEvidenceReportLayoutLine> lines,
        string label,
        string? value)
    {
        string normalized = Normalize(value);

        if (normalized.Length > 0)
        {
            Add(lines, label, normalized);
        }
    }

    private static void AddOptional(
        List<OfficialAuditEvidenceReportLayoutLine> lines,
        string label,
        Guid? value)
    {
        if (value.HasValue)
        {
            Add(lines, label, Format(value.Value));
        }
    }

    private static void AddOptional(
        List<OfficialAuditEvidenceReportLayoutLine> lines,
        string label,
        int? value)
    {
        if (value.HasValue)
        {
            Add(lines, label, Format(value.Value));
        }
    }

    private static void AddOptional(
        List<OfficialAuditEvidenceReportLayoutLine> lines,
        string label,
        DateOnly? value)
    {
        if (value.HasValue)
        {
            Add(lines, label, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }

    private static void AddOptionalField(
        List<(string Name, string Value)> fields,
        string name,
        string? value)
    {
        string normalized = Normalize(value);

        if (normalized.Length > 0)
        {
            fields.Add((name, normalized));
        }
    }

    private static void AddOptionalField(
        List<(string Name, string Value)> fields,
        string name,
        Guid? value)
    {
        if (value.HasValue)
        {
            fields.Add((name, Format(value.Value)));
        }
    }

    internal static string DefaultArtifactLabel(OfficialAuditEvidenceReportKind reportKind)
    {
        return reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "OFFICIAL_AUDIT_TRAIL_PREVIEW"
            : "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW";
    }

    private static string Format(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string Format(Guid value)
    {
        return value.ToString("D");
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

public sealed class OfficialAuditEvidenceReportTextRenderer
{
    private const string Title = "SALRYN OFFICIAL AUDIT/ELECTRONIC JOURNAL EVIDENCE LAYOUT PROOF";

    private static readonly IReadOnlyList<string> SectionOrder = new[]
    {
        "ReportHeader",
        "MachineProfile",
        "Scope",
        "EventSummary",
        "JournalEvidence",
        "LifecycleTrace",
        "OutputAndReprintTrace",
        "IntegrityIssues",
        "EvidenceFooter"
    };

    private static readonly IReadOnlyDictionary<string, string> SectionMarkers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReportHeader"] = "[REPORT]",
            ["MachineProfile"] = "[MACHINE]",
            ["Scope"] = "[SCOPE]",
            ["EventSummary"] = "[EVENT_SUMMARY]",
            ["JournalEvidence"] = "[JOURNAL_EVIDENCE]",
            ["LifecycleTrace"] = "[LIFECYCLE_TRACE]",
            ["OutputAndReprintTrace"] = "[OUTPUT_REPRINT_TRACE]",
            ["IntegrityIssues"] = "[INTEGRITY_ISSUES]",
            ["EvidenceFooter"] = "[EVIDENCE]"
        };

    public Result<OfficialAuditEvidenceReportTextRenderResult> Render(
        OfficialAuditEvidenceReportLayoutDocument? document)
    {
        Result validation = ValidateDocument(document);

        if (validation.IsFailure)
        {
            return Result<OfficialAuditEvidenceReportTextRenderResult>.Fail(validation.ErrorDetail);
        }

        List<string> lines = new()
        {
            Title
        };

        foreach (OfficialAuditEvidenceReportLayoutSection section in OrderedSections(document!))
        {
            lines.Add(SectionMarker(section.Name));

            foreach (OfficialAuditEvidenceReportLayoutLine line in section.Lines)
            {
                lines.Add(RenderLine(line));
            }
        }

        return Result<OfficialAuditEvidenceReportTextRenderResult>.Success(
            new OfficialAuditEvidenceReportTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static Result ValidateDocument(
        OfficialAuditEvidenceReportLayoutDocument? document)
    {
        if (document is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReportText.LayoutRequired",
                "Official audit/electronic journal evidence text rendering requires a layout document."));
        }

        if (!Enum.IsDefined(typeof(OfficialAuditEvidenceReportKind), document.ReportKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReportText.InvalidKind",
                "Official audit/electronic journal evidence text rendering found an invalid report kind."));
        }

        if (document.Sections is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialAuditEvidenceReportText.SectionsRequired",
                "Official audit/electronic journal evidence text rendering requires layout sections."));
        }

        HashSet<string> sectionNames = new(StringComparer.Ordinal);

        foreach (OfficialAuditEvidenceReportLayoutSection section in document.Sections)
        {
            if (section is null ||
                string.IsNullOrWhiteSpace(section.Name) ||
                section.Lines is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReportText.InvalidSection",
                    "Official audit/electronic journal evidence text rendering found an invalid layout section."));
            }

            if (!sectionNames.Add(section.Name))
            {
                return Result.Fail(Error.Validation(
                    "OfficialAuditEvidenceReportText.DuplicateSection",
                    "Official audit/electronic journal evidence text rendering found duplicate layout sections."));
            }

            foreach (OfficialAuditEvidenceReportLayoutLine line in section.Lines)
            {
                if (line is null || string.IsNullOrWhiteSpace(line.Label))
                {
                    return Result.Fail(Error.Validation(
                        "OfficialAuditEvidenceReportText.InvalidLine",
                        "Official audit/electronic journal evidence text rendering found an invalid layout line."));
                }
            }
        }

        return Result.Success();
    }

    private static IReadOnlyList<OfficialAuditEvidenceReportLayoutSection> OrderedSections(
        OfficialAuditEvidenceReportLayoutDocument document)
    {
        Dictionary<string, OfficialAuditEvidenceReportLayoutSection> sectionsByName =
            document.Sections.ToDictionary(
                static section => section.Name,
                static section => section,
                StringComparer.Ordinal);

        List<OfficialAuditEvidenceReportLayoutSection> ordered = new();

        foreach (string sectionName in SectionOrder)
        {
            if (sectionsByName.TryGetValue(sectionName, out OfficialAuditEvidenceReportLayoutSection? section))
            {
                ordered.Add(section);
            }
        }

        HashSet<string> knownSectionNames = new(SectionOrder, StringComparer.Ordinal);

        ordered.AddRange(document.Sections
            .Where(section => !knownSectionNames.Contains(section.Name))
            .OrderBy(static section => section.Name, StringComparer.Ordinal));

        return ordered;
    }

    private static string SectionMarker(string sectionName)
    {
        return SectionMarkers.TryGetValue(sectionName, out string? marker)
            ? marker
            : "[" + Normalize(sectionName).ToUpperInvariant() + "]";
    }

    private static string RenderLine(OfficialAuditEvidenceReportLayoutLine line)
    {
        string value = Normalize(line.Value);

        return value.Length == 0
            ? Normalize(line.Label) + ":"
            : Normalize(line.Label) + ": " + value;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

public sealed class OfficialAuditEvidenceReportPreviewBuilder
{
    private const string ArtifactReferencePrefix = "salryn://official-audit-evidence-preview/";

    private readonly OfficialAuditEvidenceReportLayoutBuilder _layoutBuilder;
    private readonly OfficialAuditEvidenceReportTextRenderer _textRenderer;

    public OfficialAuditEvidenceReportPreviewBuilder(
        OfficialAuditEvidenceReportLayoutBuilder? layoutBuilder = null,
        OfficialAuditEvidenceReportTextRenderer? textRenderer = null)
    {
        _layoutBuilder = layoutBuilder ?? new OfficialAuditEvidenceReportLayoutBuilder();
        _textRenderer = textRenderer ?? new OfficialAuditEvidenceReportTextRenderer();
    }

    public Result<OfficialAuditEvidenceReportPreviewResult> BuildPreview(
        OfficialAuditEvidenceReportInput? input,
        string? artifactLabel = null)
    {
        Result<OfficialAuditEvidenceReportLayoutDocument> layoutResult = _layoutBuilder.Build(input);

        if (layoutResult.IsFailure)
        {
            return Result<OfficialAuditEvidenceReportPreviewResult>.Fail(layoutResult.ErrorDetail);
        }

        Result<OfficialAuditEvidenceReportTextRenderResult> textResult =
            _textRenderer.Render(layoutResult.Value);

        if (textResult.IsFailure)
        {
            return Result<OfficialAuditEvidenceReportPreviewResult>.Fail(textResult.ErrorDetail);
        }

        string contentHash = ComputeContentHash(textResult.Value.Text);
        string referenceKind = layoutResult.Value.ReportKind.ToString();
        string artifactReference = ArtifactReferencePrefix + referenceKind + "/" + contentHash;
        string safeArtifactLabel = string.IsNullOrWhiteSpace(artifactLabel)
            ? OfficialAuditEvidenceReportLayoutBuilder.DefaultArtifactLabel(layoutResult.Value.ReportKind)
            : artifactLabel.Trim();

        OfficialAuditEvidenceReportPreviewArtifactPackage package = new(
            layoutResult.Value.ReportKind,
            safeArtifactLabel,
            artifactReference,
            contentHash,
            textResult.Value.Lines.Count,
            textResult.Value);

        return Result<OfficialAuditEvidenceReportPreviewResult>.Success(
            new OfficialAuditEvidenceReportPreviewResult(
                layoutResult.Value.ReportKind,
                layoutResult.Value,
                textResult.Value,
                package,
                safeArtifactLabel,
                artifactReference,
                contentHash,
                textResult.Value.Lines.Count));
    }

    private static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

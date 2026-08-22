using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialAuditEvidenceReportLayoutProofChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("2a111111-1111-1111-1111-111111111111");
    private static readonly Guid ScopeSaleId = Guid.Parse("2a111111-1111-1111-1111-111111111112");
    private static readonly Guid FirstJournalEntryId = Guid.Parse("2a111111-1111-1111-1111-111111111113");
    private static readonly Guid LastJournalEntryId = Guid.Parse("2a111111-1111-1111-1111-111111111114");
    private static readonly Guid SaleId1 = Guid.Parse("2a111111-1111-1111-1111-111111111115");
    private static readonly Guid SaleId2 = Guid.Parse("2a111111-1111-1111-1111-111111111116");
    private static readonly Guid SaleId3 = Guid.Parse("2a111111-1111-1111-1111-111111111117");
    private static readonly Guid JournalEntryId1 = Guid.Parse("2a111111-1111-1111-1111-111111111118");
    private static readonly Guid JournalEntryId2 = Guid.Parse("2a111111-1111-1111-1111-111111111119");
    private static readonly Guid JournalEntryId3 = Guid.Parse("2a111111-1111-1111-1111-11111111111a");
    private static readonly Guid OriginalEventId1 = Guid.Parse("2a111111-1111-1111-1111-11111111111b");
    private static readonly Guid OriginalEventId2 = Guid.Parse("2a111111-1111-1111-1111-11111111111c");
    private static readonly Guid OriginalEventId3 = Guid.Parse("2a111111-1111-1111-1111-11111111111d");
    private static readonly Guid CoveringZReadingId = Guid.Parse("2a111111-1111-1111-1111-11111111111e");

    private static readonly IReadOnlyList<string> RequiredSections = new[]
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

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string layoutText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialAuditEvidenceReportLayoutBuilder.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(layoutText, coreProjectText, coreSmokeText);
        VerifyAuditTrailBuildsAllSections();
        VerifyElectronicJournalBuildsAllSections();
        VerifyReportHeaders();
        VerifyMachineProfile();
        VerifyScope();
        VerifyEventSummary();
        VerifyJournalEvidenceOrder();
        VerifyLifecycleTraceOrder();
        VerifyOutputAndReprintTraceOrder();
        VerifyIntegrityIssues();
        VerifyEvidenceFooter();
        VerifyTextRendererMarkersAndDeterminism();
        VerifyPreviewArtifactBehavior();
        VerifyValidationFailures();
    }

    private static void VerifySourceGuards(
        string layoutText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(layoutText, "OfficialAuditEvidenceReportKind", "09J must expose audit/electronic journal report kind.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportInput", "09J must expose audit/electronic journal report input.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportScope", "09J must expose report scope.");
        RequireContains(layoutText, "OfficialAuditEvidenceMachineProfile", "09J must expose machine profile input.");
        RequireContains(layoutText, "OfficialAuditEvidenceEventLine", "09J must expose journal evidence event lines.");
        RequireContains(layoutText, "OfficialAuditEvidenceLifecycleLine", "09J must expose lifecycle trace lines.");
        RequireContains(layoutText, "OfficialAuditEvidenceOutputLine", "09J must expose output/reprint trace lines.");
        RequireContains(layoutText, "OfficialAuditEvidenceIntegrityIssue", "09J must expose integrity issues.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportLayoutDocument", "09J must expose layout document.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportLayoutSection", "09J must expose layout sections.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportLayoutLine", "09J must expose layout lines.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportLayoutBuilder", "09J must expose layout builder.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportTextRenderResult", "09J must expose text render result.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportTextRenderer", "09J must expose text renderer.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportPreviewArtifactPackage", "09J must expose preview artifact package.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportPreviewResult", "09J must expose preview result.");
        RequireContains(layoutText, "OfficialAuditEvidenceReportPreviewBuilder", "09J must expose preview builder.");
        RequireContains(layoutText, "SALRYN OFFICIAL AUDIT/ELECTRONIC JOURNAL EVIDENCE LAYOUT PROOF", "09J text renderer must include stable title.");
        RequireContains(layoutText, "NO_INTEGRITY_ISSUES_REPORTED", "09J integrity section must emit deterministic empty marker.");
        RequireContains(layoutText, "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION", "09J evidence footer must include no-state-mutation marker.");
        RequireContains(layoutText, "SHA256.HashData(Encoding.UTF8.GetBytes(text))", "09J preview must hash UTF-8 rendered text.");
        RequireContains(layoutText, "Convert.ToHexString(hash).ToLowerInvariant()", "09J preview must use lowercase SHA-256 hex.");
        RequireContains(layoutText, "salryn://official-audit-evidence-preview/", "09J preview must use stable audit/evidence artifact references.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialAuditEvidenceReportLayoutProofChecks.Run();", "CoreSmoke must run 09J audit/evidence layout proof checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialReadingReportLayoutProofChecks.Run();",
            StringComparison.Ordinal);
        int auditIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialAuditEvidenceReportLayoutProofChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || auditIndex < 0 || auditIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09J must run after 09I.");
        }

        string[] forbidden =
        {
            "CrystalDecisions",
            ".rpt",
            "System.Windows.Forms",
            "Salryn.Infrastructure",
            "Microsoft.Data.Sqlite",
            "Sqlite",
            "SQLite",
            "File.Write",
            "FileStream",
            "Process.Start",
            "ESC/POS",
            "EscPos",
            "PrintDocument",
            "PrinterSettings",
            "System.Drawing.Printing",
            "PdfDocument",
            "ExportToDisk",
            "GetTempPath",
            "GetTempFileName",
            "CompleteSale",
            "OfficialCheckout",
            "CheckoutCompletion",
            "ElectronicJournalAppend",
            "AppendOriginal",
            "CommitOriginal",
            "JournalAnchorInsert",
            "OfficialZReadingService",
            "IOfficialZReadingPersistence",
            "FinalizeAsync",
            "SaveAsync",
            "InsertZReading",
            "FiscalReset",
            "SELECT ",
            "INSERT ",
            "UPDATE ",
            "DELETE "
        };

        foreach (string marker in forbidden)
        {
            RequireDoesNotContain(layoutText, marker, $"09J layout proof must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09J must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09J must not add PDF packages to Core.");
    }

    private static void VerifyAuditTrailBuildsAllSections()
    {
        OfficialAuditEvidenceReportLayoutDocument layout =
            BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail));

        RequireEqual(OfficialAuditEvidenceReportKind.AuditTrail, layout.ReportKind, "09J AuditTrail layout must preserve report kind.");
        RequireSections(layout);
    }

    private static void VerifyElectronicJournalBuildsAllSections()
    {
        OfficialAuditEvidenceReportLayoutDocument layout =
            BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.ElectronicJournal));

        RequireEqual(OfficialAuditEvidenceReportKind.ElectronicJournal, layout.ReportKind, "09J ElectronicJournal layout must preserve report kind.");
        RequireSections(layout);
    }

    private static void VerifyReportHeaders()
    {
        OfficialAuditEvidenceReportLayoutSection auditHeader =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "ReportHeader");
        OfficialAuditEvidenceReportLayoutSection journalHeader =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.ElectronicJournal)), "ReportHeader");

        RequireLineValue(auditHeader, "ReportKind", "AuditTrail");
        RequireLineValue(auditHeader, "ReportNumber", "AT-2026-000001");
        RequireLineValue(auditHeader, "ReferenceNumber", "AUDIT-REF-2026-000001");
        RequireLineValue(auditHeader, "GeneratedAt", "2026-06-26T13:10:00.0000000+00:00");
        RequireLineValue(auditHeader, "TerminalId", "POS-AUDIT-01");
        RequireLineValue(auditHeader, "CashierUserId", CashierUserId.ToString("D"));
        RequireLineValue(auditHeader, "CashierIdentifier", "cashier-audit");
        RequireLineValue(auditHeader, "ReportLabel", "OFFICIAL_AUDIT_TRAIL_PREVIEW");

        RequireLineValue(journalHeader, "ReportKind", "ElectronicJournal");
        RequireLineValue(journalHeader, "ReportNumber", "EJ-2026-000001");
        RequireLineValue(journalHeader, "ReferenceNumber", "EJ-REF-2026-000001");
        RequireLineValue(journalHeader, "ReportLabel", "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW");
    }

    private static void VerifyMachineProfile()
    {
        OfficialAuditEvidenceReportLayoutSection machine =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "MachineProfile");

        RequireLineValue(machine, "RegisteredTaxpayerName", "KIRCH-NAIRU TRADING");
        RequireLineValue(machine, "BusinessName", "SALRYN TEST STORE");
        RequireLineValue(machine, "RegisteredAddress", "123 Sample Registered Address");
        RequireLineValue(machine, "BranchAddress", "456 Sample Branch Address");
        RequireLineValue(machine, "TaxpayerIdentificationNumber", "123-456-789-000");
        RequireLineValue(machine, "BranchCode", "001");
        RequireLineValue(machine, "MachineIdentificationNumber", "MIN-09J-001");
        RequireLineValue(machine, "MachineSerialNumber", "MSN-09J-001");
        RequireLineValue(machine, "PosTerminalNumber", "PTN-09J-001");
        RequireLineValue(machine, "AccreditationNumber", "ACC-09J-001");
        RequireLineValue(machine, "PermitToUseNumber", "PTU-09J-001");
        RequireLineValue(machine, "SoftwareName", "SALRYN");
        RequireLineValue(machine, "SoftwareVersion", "0.9J");
        RequireLineValue(machine, "SoftwareBuild", "4e1c4ca");
        RequireLineValue(machine, "ComplianceMode", "FoundationOnly");
    }

    private static void VerifyScope()
    {
        OfficialAuditEvidenceReportLayoutSection scope =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "Scope");

        RequireLineValue(scope, "ScopeKind", "BusinessDateRange");
        RequireLineValue(scope, "PeriodStart", "2026-06-26T00:00:00.0000000+00:00");
        RequireLineValue(scope, "PeriodEnd", "2026-06-27T00:00:00.0000000+00:00");
        RequireLineValue(scope, "BusinessDate", "2026-06-26");
        RequireLineValue(scope, "DateRange", "2026-06-26..2026-06-26");
        RequireLineValue(scope, "TerminalId", "POS-AUDIT-01");
        RequireLineValue(scope, "CashierUserId", CashierUserId.ToString("D"));
        RequireLineValue(scope, "CashierIdentifier", "cashier-audit");
        RequireLineValue(scope, "SaleId", ScopeSaleId.ToString("D"));
        RequireLineValue(scope, "FirstJournalEntryId", FirstJournalEntryId.ToString("D"));
        RequireLineValue(scope, "LastJournalEntryId", LastJournalEntryId.ToString("D"));
        RequireLineValue(scope, "EventCount", "3");
        RequireLineValue(scope, "CoveredSaleCount", "2");
        RequireLineValue(scope, "CoveredOfficialDocumentCount", "2");
    }

    private static void VerifyEventSummary()
    {
        OfficialAuditEvidenceReportLayoutSection summary =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "EventSummary");

        RequireLineValue(summary, "TotalEventCount", "3");
        RequireLineValue(summary, "SaleEventCount", "2");
        RequireLineValue(summary, "VoidEventCount", "1");
        RequireLineValue(summary, "CancelEventCount", "1");
        RequireLineValue(summary, "CorrectionEventCount", "1");
        RequireLineValue(summary, "ReprintEventCount", "1");
        RequireLineValue(summary, "CopyEventCount", "1");
        RequireLineValue(summary, "OutputEventCount", "2");
        RequireLineValue(summary, "IntegrityIssueCount", "2");
    }

    private static void VerifyJournalEvidenceOrder()
    {
        OfficialAuditEvidenceReportLayoutSection evidence =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.ElectronicJournal)), "JournalEvidence");

        RequireEqual(3, evidence.Lines.Count, "09J journal evidence must preserve input count.");
        RequireEqual("JournalEvidence", evidence.Lines[0].Label, "09J first journal evidence line label must be stable.");
        RequireEqual(
            $"SequenceNumber=1 | JournalEntryId={JournalEntryId1:D} | OriginalEventId={OriginalEventId1:D} | EventKind=ORIGINAL_SALE | EventTimestamp=2026-06-26T08:15:00.0000000+00:00 | SaleId={SaleId1:D} | OfficialDocumentNumber=OR-2026-000001 | CashierIdentifier=cashier-audit | TerminalId=POS-AUDIT-01 | SnapshotPayloadHash=SNAPSHOT-HASH-09J-1 | SnapshotHashAlgorithm=SHA-256 | SnapshotHashMatches=true",
            evidence.Lines[0].Value,
            "09J first journal evidence order must be preserved.");
        RequireEqual(
            $"SequenceNumber=2 | JournalEntryId={JournalEntryId2:D} | OriginalEventId={OriginalEventId2:D} | EventKind=VOID | EventTimestamp=2026-06-26T09:15:00.0000000+00:00 | SaleId={SaleId2:D} | OfficialDocumentNumber=OR-2026-000002 | CashierIdentifier=cashier-audit | TerminalId=POS-AUDIT-01 | SnapshotPayloadHash=SNAPSHOT-HASH-09J-2 | SnapshotHashAlgorithm=SHA-256 | SnapshotHashMatches=true",
            evidence.Lines[1].Value,
            "09J second journal evidence order must be preserved.");
        RequireEqual(
            $"SequenceNumber=3 | JournalEntryId={JournalEntryId3:D} | OriginalEventId={OriginalEventId3:D} | EventKind=REPRINT | EventTimestamp=2026-06-26T10:15:00.0000000+00:00 | SaleId={SaleId3:D} | OfficialDocumentNumber=OR-2026-000003 | CashierIdentifier=cashier-audit | TerminalId=POS-AUDIT-01 | SnapshotPayloadHash=SNAPSHOT-HASH-09J-3 | SnapshotHashAlgorithm=SHA-256 | SnapshotHashMatches=false",
            evidence.Lines[2].Value,
            "09J third journal evidence order must be preserved.");
    }

    private static void VerifyLifecycleTraceOrder()
    {
        OfficialAuditEvidenceReportLayoutSection lifecycle =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "LifecycleTrace");

        RequireEqual(3, lifecycle.Lines.Count, "09J lifecycle trace must preserve input count.");
        RequireEqual(
            $"SequenceNumber=1 | SaleId={SaleId1:D} | OfficialDocumentNumber=OR-2026-000001 | LifecycleState=Active | NoticeCode=OFFICIAL_ACTIVE | NoticeText=Original official document remains active. | CorrectionCount=0 | IsCoveredByFinalizedZReading=false",
            lifecycle.Lines[0].Value,
            "09J first lifecycle trace order must be preserved.");
        RequireEqual(
            $"SequenceNumber=2 | SaleId={SaleId2:D} | OfficialDocumentNumber=OR-2026-000002 | LifecycleState=Voided | NoticeCode=OFFICIAL_VOIDED | NoticeText=Official document has a void audit event. | CorrectionCount=1 | IsCoveredByFinalizedZReading=true | CoveringZReadingId={CoveringZReadingId:D} | CorrectionNote=Manager correction note 1.",
            lifecycle.Lines[1].Value,
            "09J second lifecycle trace order must be preserved.");
        RequireEqual(
            $"SequenceNumber=3 | SaleId={SaleId3:D} | OfficialDocumentNumber=OR-2026-000003 | LifecycleState=Cancelled | NoticeCode=OFFICIAL_CANCELLED | NoticeText=Official document has a cancel audit event. | CorrectionCount=2 | IsCoveredByFinalizedZReading=false | CorrectionNote=Manager correction note 2.",
            lifecycle.Lines[2].Value,
            "09J third lifecycle trace order must be preserved.");
    }

    private static void VerifyOutputAndReprintTraceOrder()
    {
        OfficialAuditEvidenceReportLayoutSection output =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "OutputAndReprintTrace");

        RequireEqual(3, output.Lines.Count, "09J output/reprint trace must preserve input count.");
        RequireEqual(
            "SequenceNumber=1 | OfficialDocumentNumber=OR-2026-000001 | OutputKind=OfficialReceipt | DocumentKind=OfficialReceipt | CopyMode=ORIGINAL | OutputMarker=ORIGINAL | IsCopyOrReprint=false | PriorReprintEventCount=0 | RenderFormat=Preview | ArtifactReference=salryn://official-text-preview/original | ContentHash=TEXT-HASH-1",
            output.Lines[0].Value,
            "09J first output/reprint trace order must be preserved.");
        RequireEqual(
            "SequenceNumber=2 | OfficialDocumentNumber=OR-2026-000002 | OutputKind=OfficialReceipt | DocumentKind=OfficialReceipt | CopyMode=COPY | OutputMarker=COPY | IsCopyOrReprint=true | PriorReprintEventCount=1 | RenderFormat=Preview | ArtifactReference=salryn://official-text-preview/copy | ContentHash=TEXT-HASH-2",
            output.Lines[1].Value,
            "09J second output/reprint trace order must be preserved.");
        RequireEqual(
            "SequenceNumber=3 | OfficialDocumentNumber=OR-2026-000003 | OutputKind=OfficialReceipt | DocumentKind=OfficialReceipt | CopyMode=COPY | OutputMarker=COPY/REPRINT | IsCopyOrReprint=true | PriorReprintEventCount=2 | RenderFormat=Preview | ArtifactReference=salryn://official-text-preview/reprint | ContentHash=TEXT-HASH-3",
            output.Lines[2].Value,
            "09J third output/reprint trace order must be preserved.");
    }

    private static void VerifyIntegrityIssues()
    {
        OfficialAuditEvidenceReportLayoutSection issues =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "IntegrityIssues");

        RequireEqual(2, issues.Lines.Count, "09J integrity issues must preserve input count.");
        RequireEqual(
            $"IssueNumber=1 | Code=SNAPSHOT_HASH_MISMATCH | Message=Snapshot hash mismatch detected for evidence proof. | Severity=Warning | RelatedEventId={OriginalEventId3:D} | RelatedJournalEntryId={JournalEntryId3:D}",
            issues.Lines[0].Value,
            "09J first integrity issue order must be preserved.");
        RequireEqual(
            $"IssueNumber=2 | Code=LIFECYCLE_NOTICE_REVIEW | Message=Lifecycle notice requires operator review. | Severity=Info | RelatedEventId={OriginalEventId2:D} | RelatedJournalEntryId={JournalEntryId2:D}",
            issues.Lines[1].Value,
            "09J second integrity issue order must be preserved.");

        OfficialAuditEvidenceReportInput emptyIssueInput = CreateInput(OfficialAuditEvidenceReportKind.AuditTrail) with
        {
            EventSummary = CreateEventSummary(totalEventCount: 3, integrityIssueCount: 0),
            IntegrityIssues = Array.Empty<OfficialAuditEvidenceIntegrityIssue>()
        };

        OfficialAuditEvidenceReportLayoutSection emptyIssues =
            RequireSection(BuildLayout(emptyIssueInput), "IntegrityIssues");

        RequireEqual(1, emptyIssues.Lines.Count, "09J empty integrity issues must emit exactly one deterministic line.");
        RequireEqual("NO_INTEGRITY_ISSUES_REPORTED", emptyIssues.Lines[0].Value, "09J empty integrity issues marker must be stable.");
    }

    private static void VerifyEvidenceFooter()
    {
        OfficialAuditEvidenceReportLayoutSection evidence =
            RequireSection(BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)), "EvidenceFooter");

        RequireLineValue(evidence, "SourceEvidenceHash", "SOURCE-EVIDENCE-HASH-09J");
        RequireLineValue(evidence, "SnapshotPayloadHash", "SNAPSHOT-HASH-09J-FOOTER");
        RequireLineValue(evidence, "HashAlgorithm", "SHA-256");
        RequireLineValue(evidence, "GeneratedFrom", "Official audit/electronic journal reporting input");
        RequireLineValue(evidence, "ContentHash", "COMPUTED_IN_PREVIEW_ARTIFACT");
        RequireLineValue(evidence, "ArtifactReference", "COMPUTED_IN_PREVIEW_ARTIFACT");
        RequireLineValue(evidence, "StateMutationMarker", "REPORT_LAYOUT_ONLY_NO_STATE_MUTATION");
    }

    private static void VerifyTextRendererMarkersAndDeterminism()
    {
        OfficialAuditEvidenceReportLayoutDocument layout = BuildLayout(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail));
        OfficialAuditEvidenceReportTextRenderer renderer = new();
        OfficialAuditEvidenceReportTextRenderResult first = RequireValue(renderer.Render(layout));
        OfficialAuditEvidenceReportTextRenderResult second = RequireValue(renderer.Render(layout));

        RequireEqual(first.Text, second.Text, "09J text renderer must be deterministic.");
        RequireDoesNotContain(first.Text, "\r", "09J text renderer must use stable line feeds.");

        foreach (string marker in new[]
        {
            "SALRYN OFFICIAL AUDIT/ELECTRONIC JOURNAL EVIDENCE LAYOUT PROOF",
            "[REPORT]",
            "[MACHINE]",
            "[SCOPE]",
            "[EVENT_SUMMARY]",
            "[JOURNAL_EVIDENCE]",
            "[LIFECYCLE_TRACE]",
            "[OUTPUT_REPRINT_TRACE]",
            "[INTEGRITY_ISSUES]",
            "[EVIDENCE]"
        })
        {
            RequireContains(first.Text, marker, $"09J text renderer must include marker {marker}.");
        }
    }

    private static void VerifyPreviewArtifactBehavior()
    {
        OfficialAuditEvidenceReportPreviewBuilder previewBuilder = new();
        OfficialAuditEvidenceReportPreviewResult first =
            RequireValue(previewBuilder.BuildPreview(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)));
        OfficialAuditEvidenceReportPreviewResult second =
            RequireValue(previewBuilder.BuildPreview(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail)));
        OfficialAuditEvidenceReportPreviewResult journalPreview =
            RequireValue(previewBuilder.BuildPreview(CreateInput(OfficialAuditEvidenceReportKind.ElectronicJournal)));
        OfficialAuditEvidenceReportPreviewResult changed =
            RequireValue(previewBuilder.BuildPreview(CreateInput(
                OfficialAuditEvidenceReportKind.AuditTrail,
                firstSnapshotHash: "SNAPSHOT-HASH-09J-CHANGED")));

        RequireEqual(OfficialAuditEvidenceReportKind.AuditTrail, first.ReportKind, "09J preview must preserve AuditTrail kind.");
        RequireEqual("OFFICIAL_AUDIT_TRAIL_PREVIEW", first.ArtifactLabel, "09J AuditTrail default artifact label must be stable.");
        RequireEqual("OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW", journalPreview.ArtifactLabel, "09J ElectronicJournal default artifact label must be stable.");
        RequireEqual(first.ContentHash, second.ContentHash, "09J content hash must be deterministic for same input.");
        RequireEqual(first.ArtifactReference, second.ArtifactReference, "09J artifact reference must be deterministic for same input.");
        RequireEqual(ExpectedHash(first.TextResult.Text), first.ContentHash, "09J content hash must be SHA-256 over UTF-8 rendered text.");
        RequireEqual(first.ContentHash.ToLowerInvariant(), first.ContentHash, "09J content hash must be lowercase.");
        RequireEqual(64, first.ContentHash.Length, "09J SHA-256 hash must be 64 lowercase hex characters.");
        RequireStartsWith(first.ArtifactReference, "salryn://official-audit-evidence-preview/", "09J artifact reference must use audit/evidence preview prefix.");
        RequireEqual("salryn://official-audit-evidence-preview/AuditTrail/" + first.ContentHash, first.ArtifactReference, "09J AuditTrail artifact reference must include kind and hash.");
        RequireEqual("salryn://official-audit-evidence-preview/ElectronicJournal/" + journalPreview.ContentHash, journalPreview.ArtifactReference, "09J ElectronicJournal artifact reference must include kind and hash.");
        RequireEqual(first.TextResult.Lines.Count, first.LineCount, "09J preview line count must match rendered text.");
        RequireEqual(first.TextResult, first.ArtifactPackage.TextResult, "09J package must include text result.");
        RequireEqual(first.LineCount, first.ArtifactPackage.LineCount, "09J package must include line count.");
        RequireEqual(first.ContentHash, first.ArtifactPackage.ContentHash, "09J package must include content hash.");
        RequireEqual(first.ArtifactReference, first.ArtifactPackage.ArtifactReference, "09J package must include artifact reference.");

        if (string.Equals(first.ContentHash, changed.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09J content hash must change when meaningful event/hash data changes.");
        }

        OfficialAuditEvidenceReportPreviewResult custom =
            RequireValue(previewBuilder.BuildPreview(
                CreateInput(OfficialAuditEvidenceReportKind.AuditTrail),
                "  CUSTOM_AUDIT_PREVIEW  "));

        RequireEqual("CUSTOM_AUDIT_PREVIEW", custom.ArtifactLabel, "09J custom artifact label must be normalized.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialAuditEvidenceReportLayoutBuilder builder = new();

        RequireFailureCode(
            builder.Build(null),
            "OfficialAuditEvidenceReport.InputRequired",
            "09J builder must reject null input.");

        RequireFailureCode(
            builder.Build(CreateInput((OfficialAuditEvidenceReportKind)999)),
            "OfficialAuditEvidenceReport.InvalidKind",
            "09J builder must reject invalid report kind.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail) with
            {
                EventSummary = CreateEventSummary(totalEventCount: -1, integrityIssueCount: 2)
            }),
            "OfficialAuditEvidenceReport.InvalidEventSummary",
            "09J builder must reject negative event counts.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail) with
            {
                JournalEvidence = new[]
                {
                    CreateJournalEvidence()[0],
                    CreateJournalEvidence()[1] with { SequenceNumber = 1 }
                }
            }),
            "OfficialAuditEvidenceReport.DuplicateEventSequenceNumber",
            "09J builder must reject duplicate event sequence numbers.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail) with
            {
                LifecycleTrace = new[]
                {
                    CreateLifecycleTrace()[0],
                    CreateLifecycleTrace()[1] with { SequenceNumber = 1 }
                }
            }),
            "OfficialAuditEvidenceReport.DuplicateLifecycleSequenceNumber",
            "09J builder must reject duplicate lifecycle sequence numbers.");

        RequireFailureCode(
            builder.Build(CreateInput(OfficialAuditEvidenceReportKind.AuditTrail) with
            {
                OutputAndReprintTrace = new[]
                {
                    CreateOutputAndReprintTrace()[0],
                    CreateOutputAndReprintTrace()[1] with { SequenceNumber = 1 }
                }
            }),
            "OfficialAuditEvidenceReport.DuplicateOutputSequenceNumber",
            "09J builder must reject duplicate output/reprint sequence numbers.");
    }

    private static OfficialAuditEvidenceReportInput CreateInput(
        OfficialAuditEvidenceReportKind kind,
        string firstSnapshotHash = "SNAPSHOT-HASH-09J-1")
    {
        return new OfficialAuditEvidenceReportInput(
            ReportKind: kind,
            ReportNumber: kind == OfficialAuditEvidenceReportKind.AuditTrail
                ? "AT-2026-000001"
                : "EJ-2026-000001",
            ReferenceNumber: kind == OfficialAuditEvidenceReportKind.AuditTrail
                ? "AUDIT-REF-2026-000001"
                : "EJ-REF-2026-000001",
            GeneratedAt: new DateTimeOffset(2026, 6, 26, 13, 10, 0, TimeSpan.Zero),
            TerminalId: "POS-AUDIT-01",
            CashierUserId,
            CashierIdentifier: "cashier-audit",
            ReportLabel: null,
            MachineProfile: CreateMachineProfile(),
            Scope: new OfficialAuditEvidenceReportScope(
                ScopeKind: "BusinessDateRange",
                PeriodStart: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                PeriodEnd: new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
                BusinessDate: new DateOnly(2026, 6, 26),
                DateRange: "2026-06-26..2026-06-26",
                TerminalId: "POS-AUDIT-01",
                CashierUserId,
                CashierIdentifier: "cashier-audit",
                SaleId: ScopeSaleId,
                FirstJournalEntryId,
                LastJournalEntryId,
                EventCount: 3,
                CoveredSaleCount: 2,
                CoveredOfficialDocumentCount: 2),
            EventSummary: CreateEventSummary(totalEventCount: 3, integrityIssueCount: 2),
            JournalEvidence: CreateJournalEvidence(firstSnapshotHash),
            LifecycleTrace: CreateLifecycleTrace(),
            OutputAndReprintTrace: CreateOutputAndReprintTrace(),
            IntegrityIssues: CreateIntegrityIssues(),
            Evidence: new OfficialAuditEvidenceReportEvidence(
                SourceEvidenceHash: "SOURCE-EVIDENCE-HASH-09J",
                SnapshotPayloadHash: "SNAPSHOT-HASH-09J-FOOTER",
                HashAlgorithm: "SHA-256",
                GeneratedFrom: "Official audit/electronic journal reporting input"));
    }

    private static OfficialAuditEvidenceMachineProfile CreateMachineProfile()
    {
        return new OfficialAuditEvidenceMachineProfile(
            RegisteredTaxpayerName: "KIRCH-NAIRU TRADING",
            BusinessName: "SALRYN TEST STORE",
            RegisteredAddress: "123 Sample Registered Address",
            BranchAddress: "456 Sample Branch Address",
            TaxpayerIdentificationNumber: "123-456-789-000",
            BranchCode: "001",
            MachineIdentificationNumber: "MIN-09J-001",
            MachineSerialNumber: "MSN-09J-001",
            PosTerminalNumber: "PTN-09J-001",
            AccreditationNumber: "ACC-09J-001",
            PermitToUseNumber: "PTU-09J-001",
            SoftwareName: "SALRYN",
            SoftwareVersion: "0.9J",
            SoftwareBuild: "4e1c4ca",
            ComplianceMode: "FoundationOnly");
    }

    private static OfficialAuditEvidenceEventSummary CreateEventSummary(
        int totalEventCount,
        int integrityIssueCount)
    {
        return new OfficialAuditEvidenceEventSummary(
            TotalEventCount: totalEventCount,
            SaleEventCount: 2,
            VoidEventCount: 1,
            CancelEventCount: 1,
            CorrectionEventCount: 1,
            ReprintEventCount: 1,
            CopyEventCount: 1,
            OutputEventCount: 2,
            IntegrityIssueCount: integrityIssueCount);
    }

    private static IReadOnlyList<OfficialAuditEvidenceEventLine> CreateJournalEvidence(
        string firstSnapshotHash = "SNAPSHOT-HASH-09J-1")
    {
        return new[]
        {
            new OfficialAuditEvidenceEventLine(
                SequenceNumber: 1,
                JournalEntryId: JournalEntryId1,
                OriginalEventId: OriginalEventId1,
                EventKind: "ORIGINAL_SALE",
                EventTimestamp: new DateTimeOffset(2026, 6, 26, 8, 15, 0, TimeSpan.Zero),
                SaleId: SaleId1,
                OfficialDocumentNumber: "OR-2026-000001",
                CashierIdentifier: "cashier-audit",
                TerminalId: "POS-AUDIT-01",
                SnapshotPayloadHash: firstSnapshotHash,
                SnapshotHashAlgorithm: "SHA-256",
                SnapshotHashMatches: true),
            new OfficialAuditEvidenceEventLine(
                SequenceNumber: 2,
                JournalEntryId: JournalEntryId2,
                OriginalEventId: OriginalEventId2,
                EventKind: "VOID",
                EventTimestamp: new DateTimeOffset(2026, 6, 26, 9, 15, 0, TimeSpan.Zero),
                SaleId: SaleId2,
                OfficialDocumentNumber: "OR-2026-000002",
                CashierIdentifier: "cashier-audit",
                TerminalId: "POS-AUDIT-01",
                SnapshotPayloadHash: "SNAPSHOT-HASH-09J-2",
                SnapshotHashAlgorithm: "SHA-256",
                SnapshotHashMatches: true),
            new OfficialAuditEvidenceEventLine(
                SequenceNumber: 3,
                JournalEntryId: JournalEntryId3,
                OriginalEventId: OriginalEventId3,
                EventKind: "REPRINT",
                EventTimestamp: new DateTimeOffset(2026, 6, 26, 10, 15, 0, TimeSpan.Zero),
                SaleId: SaleId3,
                OfficialDocumentNumber: "OR-2026-000003",
                CashierIdentifier: "cashier-audit",
                TerminalId: "POS-AUDIT-01",
                SnapshotPayloadHash: "SNAPSHOT-HASH-09J-3",
                SnapshotHashAlgorithm: "SHA-256",
                SnapshotHashMatches: false)
        };
    }

    private static IReadOnlyList<OfficialAuditEvidenceLifecycleLine> CreateLifecycleTrace()
    {
        return new[]
        {
            new OfficialAuditEvidenceLifecycleLine(
                SequenceNumber: 1,
                SaleId: SaleId1,
                OfficialDocumentNumber: "OR-2026-000001",
                LifecycleState: "Active",
                NoticeCode: "OFFICIAL_ACTIVE",
                NoticeText: "Original official document remains active.",
                CorrectionCount: 0,
                IsCoveredByFinalizedZReading: false,
                CoveringZReadingId: null,
                CorrectionNote: null),
            new OfficialAuditEvidenceLifecycleLine(
                SequenceNumber: 2,
                SaleId: SaleId2,
                OfficialDocumentNumber: "OR-2026-000002",
                LifecycleState: "Voided",
                NoticeCode: "OFFICIAL_VOIDED",
                NoticeText: "Official document has a void audit event.",
                CorrectionCount: 1,
                IsCoveredByFinalizedZReading: true,
                CoveringZReadingId,
                CorrectionNote: "Manager correction note 1."),
            new OfficialAuditEvidenceLifecycleLine(
                SequenceNumber: 3,
                SaleId: SaleId3,
                OfficialDocumentNumber: "OR-2026-000003",
                LifecycleState: "Cancelled",
                NoticeCode: "OFFICIAL_CANCELLED",
                NoticeText: "Official document has a cancel audit event.",
                CorrectionCount: 2,
                IsCoveredByFinalizedZReading: false,
                CoveringZReadingId: null,
                CorrectionNote: "Manager correction note 2.")
        };
    }

    private static IReadOnlyList<OfficialAuditEvidenceOutputLine> CreateOutputAndReprintTrace()
    {
        return new[]
        {
            new OfficialAuditEvidenceOutputLine(
                SequenceNumber: 1,
                OfficialDocumentNumber: "OR-2026-000001",
                OutputKind: "OfficialReceipt",
                DocumentKind: "OfficialReceipt",
                CopyMode: "ORIGINAL",
                OutputMarker: "ORIGINAL",
                IsCopyOrReprint: false,
                PriorReprintEventCount: 0,
                RenderFormat: "Preview",
                ArtifactReference: "salryn://official-text-preview/original",
                ContentHash: "TEXT-HASH-1"),
            new OfficialAuditEvidenceOutputLine(
                SequenceNumber: 2,
                OfficialDocumentNumber: "OR-2026-000002",
                OutputKind: "OfficialReceipt",
                DocumentKind: "OfficialReceipt",
                CopyMode: "COPY",
                OutputMarker: "COPY",
                IsCopyOrReprint: true,
                PriorReprintEventCount: 1,
                RenderFormat: "Preview",
                ArtifactReference: "salryn://official-text-preview/copy",
                ContentHash: "TEXT-HASH-2"),
            new OfficialAuditEvidenceOutputLine(
                SequenceNumber: 3,
                OfficialDocumentNumber: "OR-2026-000003",
                OutputKind: "OfficialReceipt",
                DocumentKind: "OfficialReceipt",
                CopyMode: "COPY",
                OutputMarker: "COPY/REPRINT",
                IsCopyOrReprint: true,
                PriorReprintEventCount: 2,
                RenderFormat: "Preview",
                ArtifactReference: "salryn://official-text-preview/reprint",
                ContentHash: "TEXT-HASH-3")
        };
    }

    private static IReadOnlyList<OfficialAuditEvidenceIntegrityIssue> CreateIntegrityIssues()
    {
        return new[]
        {
            new OfficialAuditEvidenceIntegrityIssue(
                IssueNumber: 1,
                Code: "SNAPSHOT_HASH_MISMATCH",
                Message: "Snapshot hash mismatch detected for evidence proof.",
                Severity: "Warning",
                RelatedEventId: OriginalEventId3,
                RelatedJournalEntryId: JournalEntryId3),
            new OfficialAuditEvidenceIntegrityIssue(
                IssueNumber: 2,
                Code: "LIFECYCLE_NOTICE_REVIEW",
                Message: "Lifecycle notice requires operator review.",
                Severity: "Info",
                RelatedEventId: OriginalEventId2,
                RelatedJournalEntryId: JournalEntryId2)
        };
    }

    private static OfficialAuditEvidenceReportLayoutDocument BuildLayout(
        OfficialAuditEvidenceReportInput input)
    {
        return RequireValue(new OfficialAuditEvidenceReportLayoutBuilder().Build(input));
    }

    private static void RequireSections(OfficialAuditEvidenceReportLayoutDocument document)
    {
        RequireEqual(RequiredSections.Count, document.Sections.Count, "09J layout must include exactly the required sections.");

        for (int index = 0; index < RequiredSections.Count; index++)
        {
            RequireEqual(RequiredSections[index], document.Sections[index].Name, "09J layout section order must be stable.");
        }
    }

    private static OfficialAuditEvidenceReportLayoutSection RequireSection(
        OfficialAuditEvidenceReportLayoutDocument document,
        string sectionName)
    {
        OfficialAuditEvidenceReportLayoutSection? section =
            document.Sections.FirstOrDefault(item => string.Equals(item.Name, sectionName, StringComparison.Ordinal));

        if (section is null)
        {
            throw new InvalidOperationException($"Expected 09J layout section {sectionName}.");
        }

        return section;
    }

    private static void RequireLineValue(
        OfficialAuditEvidenceReportLayoutSection section,
        string label,
        string expectedValue)
    {
        OfficialAuditEvidenceReportLayoutLine? line =
            section.Lines.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));

        if (line is null)
        {
            throw new InvalidOperationException($"Expected 09J layout line {section.Name}.{label}.");
        }

        RequireEqual(expectedValue, line.Value, $"Unexpected 09J layout line value for {section.Name}.{label}.");
    }

    private static string ExpectedHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static T RequireValue<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Expected 09J success, got {result.ErrorDetail.Code}: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireFailureCode<T>(
        Result<T> result,
        string expectedCode,
        string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireStartsWith(
        string text,
        string value,
        string message)
    {
        if (!text.StartsWith(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }
}

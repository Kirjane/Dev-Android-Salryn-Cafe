using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialPrintableDocumentDtoFoundationChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("25444444-4444-4444-4444-444444444441");
    private static readonly Guid SaleId = Guid.Parse("25444444-4444-4444-4444-444444444442");
    private static readonly Guid MachineProfileId = Guid.Parse("25444444-4444-4444-4444-444444444443");
    private static readonly Guid SequenceId = Guid.Parse("25444444-4444-4444-4444-444444444444");
    private static readonly Guid OriginalEventId = Guid.Parse("25444444-4444-4444-4444-444444444445");
    private static readonly Guid CashierUserId = Guid.Parse("25444444-4444-4444-4444-444444444446");
    private static readonly DateTimeOffset TransactionTimestamp = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string builderText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialPrintableDocumentBuilder.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(builderText, "OfficialPrintableDocument", "Printable DTO foundation must expose root document DTO.");
        RequireContains(builderText, "OfficialPrintableDocumentHeader", "Printable DTO foundation must expose header DTO.");
        RequireContains(builderText, "OfficialPrintableLineItem", "Printable DTO foundation must expose line item DTO.");
        RequireContains(builderText, "OfficialPrintablePayment", "Printable DTO foundation must expose payment DTO.");
        RequireContains(builderText, "OfficialPrintableTotals", "Printable DTO foundation must expose totals DTO.");
        RequireContains(builderText, "OfficialPrintableMachineProfile", "Printable DTO foundation must expose machine profile DTO.");
        RequireContains(builderText, "OfficialPrintableEvidenceFooter", "Printable DTO foundation must expose evidence footer DTO.");
        RequireContains(builderText, "OfficialPrintableLifecycleNotice", "Printable DTO foundation must expose lifecycle notice DTO.");
        RequireContains(builderText, "OfficialPrintableCopyInfo", "Printable DTO foundation must expose copy audit DTO.");
        RequireContains(builderText, "OfficialPrintableDocumentBuilder", "Printable DTO foundation must expose a builder.");
        RequireContains(builderText, "OfficialOutputSourceResult", "Printable DTO builder must consume the official output source.");
        RequireContains(builderText, "COPY/REPRINT", "Printable DTO builder must mark copy/reprint output.");
        RequireContains(builderText, "PriorReprintEventCount", "Printable DTO builder must carry reprint count.");
        RequireNotContains(builderText, "Sqlite", "Printable DTO builder must not query SQLite.");
        RequireNotContains(builderText, "Crystal", "Printable DTO builder must not wire report rendering.");
        RequireNotContains(builderText, "ESC/POS", "Printable DTO builder must not wire printer output.");
        RequireNotContains(builderText, "System.Windows.Forms", "Printable DTO builder must not wire UI.");
        RequireNotContains(builderText, "LocalHostCompleteSale", "Printable DTO builder must not wire Local Host complete-sale.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialPrintableDocumentDtoFoundationChecks.Run();", "CoreSmoke must run printable DTO foundation checks.");

        VerifyRuntimeDtoBehavior();
    }

    private static void VerifyRuntimeDtoBehavior()
    {
        OfficialPrintableDocumentBuilder builder = new();

        OfficialOutputSourceResult activeSource = CreateSource(OfficialDocumentLifecycleState.Active);
        Result<OfficialPrintableDocument> originalResult = builder.Build(activeSource);
        RequireSuccess(originalResult, "Active original printable DTO should build.");

        OfficialPrintableDocument original = originalResult.Value;
        RequireEqual("ORIGINAL", original.CopyAudit.OutputMarker, "Original printable DTO must be marked original.");
        RequireTrue(!original.CopyAudit.IsCopyOrReprint, "Original printable DTO must not be marked as copy.");
        RequireEqual("OR-2026-PRINT-001", original.Header.OfficialDocumentNumber, "Printable header must expose official number.");
        RequireEqual("Printable Item", original.LineItems[0].Description, "Printable DTO must include line item data.");
        RequireEqual(PaymentMethod.Cash, original.Payments[0].Method, "Printable DTO must include payment data.");
        RequireEqual(2500L, original.Totals.TotalCents, "Printable DTO must include totals.");
        RequireEqual("printable-hash", original.Evidence.SnapshotPayloadHash, "Printable evidence footer must include hash.");
        RequireEqual("ACTIVE", original.Lifecycle.NoticeCode, "Active source must produce active lifecycle notice.");

        OfficialOutputSourceResult reprintSource = CreateSource(
            OfficialDocumentLifecycleState.Active,
            reprintEventCount: 2);
        Result<OfficialPrintableDocument> reprintResult =
            builder.Build(reprintSource, OfficialPrintableDocumentCopyMode.Reprint);
        RequireSuccess(reprintResult, "Reprint printable DTO should build.");
        RequireEqual("COPY/REPRINT", reprintResult.Value.CopyAudit.OutputMarker, "Reprint printable DTO must be marked copy/reprint.");
        RequireTrue(reprintResult.Value.CopyAudit.IsCopyOrReprint, "Reprint printable DTO must flag copy output.");
        RequireEqual(2, reprintResult.Value.CopyAudit.PriorReprintEventCount, "Reprint printable DTO must carry reprint event count.");

        OfficialPrintableDocument voided =
            RequireValue(builder.Build(CreateSource(OfficialDocumentLifecycleState.Voided)), "Voided printable DTO should build.");
        RequireEqual("VOIDED", voided.Lifecycle.NoticeCode, "Voided source must include voided lifecycle notice.");

        OfficialPrintableDocument cancelled =
            RequireValue(builder.Build(CreateSource(OfficialDocumentLifecycleState.Cancelled)), "Cancelled printable DTO should build.");
        RequireEqual("CANCELLED", cancelled.Lifecycle.NoticeCode, "Cancelled source must include cancelled lifecycle notice.");

        OfficialPrintableDocument invalid =
            RequireValue(builder.Build(CreateSource(
                OfficialDocumentLifecycleState.Invalid,
                issueCodes: new[] { "OfficialDocumentLifecycle.MarkerTypeMismatch" })), "Invalid printable DTO should build with issues.");
        RequireEqual("INVALID", invalid.Lifecycle.NoticeCode, "Invalid source must include invalid lifecycle notice.");
        RequireEqual(1, invalid.Lifecycle.Issues.Count, "Invalid source must carry lifecycle issues.");

        OfficialPrintableDocument corrected =
            RequireValue(builder.Build(CreateSource(
                OfficialDocumentLifecycleState.Active,
                correctionNotes: new[] { "First correction.", "Second correction." })), "Corrected printable DTO should build.");
        RequireEqual(2, corrected.Lifecycle.CorrectionCount, "Printable lifecycle must include correction count.");
        RequireEqual("First correction.", corrected.Lifecycle.CorrectionNotes[0], "Correction notes must keep deterministic order.");
        RequireEqual("Second correction.", corrected.Lifecycle.CorrectionNotes[1], "Correction notes must keep deterministic order.");

        ExpectFailure(
            builder.Build(null),
            "OfficialPrintableDocument.SourceRequired");

        ExpectFailure(
            builder.Build(activeSource with
            {
                Evidence = activeSource.Evidence with { SnapshotHashMatches = false }
            }),
            "OfficialPrintableDocument.VerifiedEvidenceRequired");
    }

    private static OfficialOutputSourceResult CreateSource(
        OfficialDocumentLifecycleState state,
        int reprintEventCount = 0,
        IReadOnlyList<string>? correctionNotes = null,
        IReadOnlyList<string>? issueCodes = null)
    {
        IReadOnlyList<OfficialDocumentLifecycleIssue> issues =
            (issueCodes ?? Array.Empty<string>())
                .Select(static code => new OfficialDocumentLifecycleIssue(code, code))
                .ToArray();

        IReadOnlyList<OfficialDocumentLifecycleEventReadModel> events =
            Enumerable.Range(0, reprintEventCount)
                .Select(static index => new OfficialDocumentLifecycleEventReadModel(
                    EventId: Guid.NewGuid(),
                    EventType: "REPRINT",
                    OriginalMarker: false,
                    ReprintMarker: true,
                    VoidMarker: false,
                    RefundMarker: false,
                    CancelMarker: false,
                    CashierUserId: CashierUserId,
                    Reason: "Copy " + index.ToString(),
                    CreatedAt: TransactionTimestamp.AddMinutes(index),
                    TerminalId: "POS-PRINTABLE",
                    MetadataJson: null))
                .ToArray();

        OfficialOutputLifecycle lifecycle = new(
            State: state,
            CorrectionCount: correctionNotes?.Count ?? 0,
            CorrectionNotes: correctionNotes ?? Array.Empty<string>(),
            IsCoveredByFinalizedZReading: true,
            CoveringZReadingId: Guid.Parse("25444444-4444-4444-4444-444444449999"),
            Issues: issues,
            Events: events);

        return new OfficialOutputSourceResult(
            new OfficialOutputDocumentHeader(
                JournalEntryId,
                SaleId,
                MachineProfileId,
                SequenceId,
                ElectronicJournalDocumentKind.OfficialReceipt,
                "OR-2026-PRINT-001",
                TransactionTimestamp,
                CashierUserId,
                "Printable Cashier",
                TerminalId: "POS-PRINTABLE",
                SnapshotTerminalIdentifier: "POS-PRINTABLE"),
            new[]
            {
                new OfficialOutputLineItem(
                    "Printable Item",
                    "4800000000099",
                    "pc",
                    1000,
                    2500,
                    0,
                    2500)
            },
            new[]
            {
                new OfficialOutputPayment(
                    PaymentMethod.Cash,
                    2500,
                    TransactionTimestamp)
            },
            new OfficialOutputTotals(
                SubtotalCents: 2500,
                DiscountCents: 0,
                TaxCents: 0,
                TotalCents: 2500,
                AmountPaidCents: 2500,
                ChangeCents: 0,
                TaxModeCode: "VAT-DEFERRED",
                VatBucketEngineDeferred: true,
                TaxModeNotes: "Detailed tax bucket classification is deferred.",
                DeferredComplianceNotes: new[] { "Deferred tax bucket detail." }),
            new OfficialOutputMachineProfile(
                MachineProfileId,
                RegistrationScopeCode: "LOCAL_SINGLE_STORE",
                RegisteredTaxpayerName: "Printable Taxpayer",
                BusinessName: "Printable Store",
                RegisteredAddress: "Printable Registered Address",
                BranchAddress: "Printable Branch Address",
                TaxpayerIdentificationNumber: "000-333-444-555",
                BranchCode: "00001",
                MachineIdentificationNumber: "MIN-PRINTABLE",
                MachineSerialNumber: "SERIAL-PRINTABLE",
                PosTerminalNumber: "POS-001",
                AccreditationNumber: "ACC-PRINTABLE",
                PermitToUseNumber: "PTU-PRINTABLE",
                SoftwareName: "Salryn",
                SoftwareVersion: "TEST",
                SoftwareBuild: "SMOKE",
                ComplianceMode: "ACCREDITED"),
            new OfficialOutputEvidence(
                OriginalEventId,
                SnapshotPayloadJson: "{}",
                SnapshotPayloadHash: "printable-hash",
                RecomputedSnapshotPayloadHash: "printable-hash",
                SnapshotHashAlgorithm: "SHA-256",
                SnapshotHashMatches: true),
            lifecycle);
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

    private static OfficialPrintableDocument RequireValue(
        Result<OfficialPrintableDocument> result,
        string message)
    {
        RequireSuccess(result, message);

        return result.Value;
    }

    private static void ExpectFailure(Result<OfficialPrintableDocument> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected printable document failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected printable document validation code.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }
}

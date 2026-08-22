using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentLifecycleReadModelChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("25111111-1111-1111-1111-111111111111");
    private static readonly Guid SaleId = Guid.Parse("25111111-1111-1111-1111-111111111112");
    private static readonly Guid MachineProfileId = Guid.Parse("25111111-1111-1111-1111-111111111113");
    private static readonly Guid CashierUserId = Guid.Parse("25111111-1111-1111-1111-111111111114");
    private static readonly DateTimeOffset TransactionTimestamp = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventTimestamp = new(2026, 6, 25, 12, 1, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string coreText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentLifecycleReadService.cs");
        string sqliteText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialDocumentLifecycleReadService.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infrastructureSmokeProgramText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string infrastructureSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialDocumentLifecycleSmokeChecks.cs");

        RequireContains(coreText, "OfficialDocumentLifecycleState", "Lifecycle read model must expose lifecycle states.");
        RequireContains(coreText, "Active", "Lifecycle read model must support active state.");
        RequireContains(coreText, "Voided", "Lifecycle read model must support voided state.");
        RequireContains(coreText, "Cancelled", "Lifecycle read model must support cancelled state.");
        RequireContains(coreText, "Invalid", "Lifecycle read model must support invalid state.");
        RequireContains(coreText, "IOfficialDocumentLifecycleReadService", "Lifecycle read model must expose a Core read service contract.");
        RequireContains(coreText, "OfficialDocumentLifecycleCorrectionReadModel", "Lifecycle read model must expose correction notes.");
        RequireContains(coreText, "OfficialDocumentLifecycleIssue", "Lifecycle read model must expose fail-closed issues.");
        RequireContains(coreText, "ContradictoryTerminalEvents", "Lifecycle read model must detect contradictory terminal events.");
        RequireContains(coreText, "MarkerTypeMismatch", "Lifecycle read model must detect marker/type mismatches.");
        RequireContains(coreText, "CorrectionCount", "Lifecycle read model must summarize correction count.");
        RequireNotContains(coreText, "receipt_snapshot", "Lifecycle read model must not use sales.receipt_snapshot.");
        RequireNotContains(coreText, "Crystal", "Lifecycle read model must not wire report rendering.");
        RequireNotContains(coreText, "ESC/POS", "Lifecycle read model must not wire printer output.");
        RequireNotContains(coreText, "LocalHostCompleteSale", "Lifecycle read model must not wire Local Host complete-sale.");

        RequireContains(sqliteText, "bir_electronic_journal_entries", "SQLite lifecycle read must read journal entries.");
        RequireContains(sqliteText, "bir_electronic_journal_entry_events", "SQLite lifecycle read must read journal events.");
        RequireContains(sqliteText, "bir_z_readings", "SQLite lifecycle read must read Z-reading coverage.");
        RequireContains(sqliteText, "range_started_at <= $transactionTimestamp", "Z coverage must use half-open start semantics.");
        RequireContains(sqliteText, "range_ended_at > $transactionTimestamp", "Z coverage must use half-open end semantics.");
        RequireContains(sqliteText, "ORDER BY created_at ASC, id ASC", "Event trail must be deterministic.");
        RequireContains(sqliteText, "ReadTerminalId", "Lifecycle read must parse terminal id from metadata when available.");
        RequireNotContains(sqliteText, "INSERT INTO", "Lifecycle read must not insert rows.");
        RequireNotContains(sqliteText, "UPDATE ", "Lifecycle read must not update rows.");
        RequireNotContains(sqliteText, "DELETE ", "Lifecycle read must not delete rows.");
        RequireNotContains(sqliteText, "receipt_snapshot", "Lifecycle read must not use sales.receipt_snapshot.");

        RequireContains(appServicesText, "public IOfficialDocumentLifecycleReadService OfficialDocumentLifecycleRead { get; }", "AppServices must expose lifecycle read service.");
        RequireContains(appServicesText, "new SqliteOfficialDocumentLifecycleReadService(connectionFactory)", "AppServices must compose SQLite lifecycle read service.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentLifecycleReadModelChecks.Run();", "CoreSmoke must run lifecycle read checks.");
        RequireContains(infrastructureSmokeProgramText, "OfficialDocumentLifecycleSmokeChecks.Run(projectRoot);", "Infrastructure smoke command must run lifecycle proof checks.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentLifecycle.ContradictoryTerminalEvents", "Infrastructure smoke must prove contradictory terminal events.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentLifecycle.DuplicateVoidEvents", "Infrastructure smoke must prove duplicate void invalid state.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentLifecycle.DuplicateCancelEvents", "Infrastructure smoke must prove duplicate cancel invalid state.");
        RequireContains(infrastructureSmokeText, "IsCoveredByFinalizedZReading", "Infrastructure smoke must prove post-Z coverage flag.");

        VerifyRuntimeValidationAndStateDerivation();
    }

    private static void VerifyRuntimeValidationAndStateDerivation()
    {
        OfficialDocumentLifecycleReadRequest validRequest = new(
            JournalEntryId,
            OfficialDocumentNumber: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null);

        RequireSuccess(OfficialDocumentLifecycleReadService.ValidateRequest(validRequest), "Lifecycle validation should accept a journal entry lookup.");

        OfficialDocumentLifecycleReadRequest normalized =
            OfficialDocumentLifecycleReadService.NormalizeRequest(validRequest with
            {
                OfficialDocumentNumber = "  OR-2026-000001  ",
                RegistrationScopeCode = "  LOCAL_SINGLE_STORE  "
            });

        RequireEqual("OR-2026-000001", normalized.OfficialDocumentNumber, "Lifecycle document number must be normalized.");
        RequireEqual("LOCAL_SINGLE_STORE", normalized.RegistrationScopeCode, "Lifecycle scope must be normalized.");

        ExpectValidationFailure(
            OfficialDocumentLifecycleReadService.ValidateRequest(validRequest with
            {
                JournalEntryId = null,
                OfficialDocumentNumber = null
            }),
            "OfficialDocumentLifecycle.DocumentReferenceRequired");

        ExpectValidationFailure(
            OfficialDocumentLifecycleReadService.ValidateRequest(validRequest with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                DocumentKind = null
            }),
            "OfficialDocumentLifecycle.DocumentKindRequired");

        ExpectValidationFailure(
            OfficialDocumentLifecycleReadService.ValidateRequest(validRequest with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }),
            "OfficialDocumentLifecycle.MachineProfileOrScopeRequired");

        ExpectValidationFailure(
            OfficialDocumentLifecycleReadService.ValidateRequest(validRequest with
            {
                JournalEntryId = Guid.Empty
            }),
            "OfficialDocumentLifecycle.InvalidJournalEntryId");

        OfficialDocumentLifecycleResult active =
            OfficialDocumentLifecycleReadService.BuildResult(
                CreateSource(),
                new[] { CreateEvent("ORIGINAL", originalMarker: true) },
                new OfficialDocumentLifecycleZReadingCoverage(false, null, null));

        RequireEqual(OfficialDocumentLifecycleState.Active, active.Summary.State, "ORIGINAL-only lifecycle must be active.");
        RequireEqual(0, active.Summary.CorrectionCount, "Active lifecycle without correction must have zero corrections.");

        OfficialDocumentLifecycleResult voided =
            OfficialDocumentLifecycleReadService.BuildResult(
                CreateSource(),
                new[]
                {
                    CreateEvent("ORIGINAL", originalMarker: true),
                    CreateEvent("CORRECTION", reason: "Correct cashier note."),
                    CreateEvent("VOID", voidMarker: true)
                },
                new OfficialDocumentLifecycleZReadingCoverage(true, Guid.Parse("25111111-1111-1111-1111-111111119999"), EventTimestamp));

        RequireEqual(OfficialDocumentLifecycleState.Voided, voided.Summary.State, "VOID event must derive voided lifecycle.");
        RequireEqual(1, voided.Summary.CorrectionCount, "Correction count must survive voided lifecycle.");
        RequireTrue(voided.Summary.IsCoveredByFinalizedZReading, "Lifecycle read must expose Z-reading coverage.");

        OfficialDocumentLifecycleResult contradictory =
            OfficialDocumentLifecycleReadService.BuildResult(
                CreateSource(),
                new[]
                {
                    CreateEvent("ORIGINAL", originalMarker: true),
                    CreateEvent("VOID", voidMarker: true),
                    CreateEvent("CANCEL", cancelMarker: true)
                },
                new OfficialDocumentLifecycleZReadingCoverage(false, null, null));

        RequireEqual(OfficialDocumentLifecycleState.Invalid, contradictory.Summary.State, "VOID+CANCEL lifecycle must be invalid.");
        RequireContains(
            string.Join('|', contradictory.Issues.Select(issue => issue.Code)),
            "OfficialDocumentLifecycle.ContradictoryTerminalEvents",
            "Contradictory lifecycle must include issue code.");

        OfficialDocumentLifecycleResult markerMismatch =
            OfficialDocumentLifecycleReadService.BuildResult(
                CreateSource(),
                new[] { CreateEvent("ORIGINAL", originalMarker: false) },
                new OfficialDocumentLifecycleZReadingCoverage(false, null, null));

        RequireEqual(OfficialDocumentLifecycleState.Invalid, markerMismatch.Summary.State, "Marker/type mismatch must be invalid.");
        RequireContains(
            string.Join('|', markerMismatch.Issues.Select(issue => issue.Code)),
            "OfficialDocumentLifecycle.MarkerTypeMismatch",
            "Marker/type mismatch must include issue code.");
    }

    private static OfficialDocumentLifecycleSource CreateSource()
    {
        return new OfficialDocumentLifecycleSource(
            JournalEntryId,
            SaleId,
            MachineProfileId,
            RegistrationScopeCode: "LOCAL_SINGLE_STORE",
            ElectronicJournalDocumentKind.OfficialReceipt,
            "OR-2026-000001",
            TransactionTimestamp,
            CashierUserId,
            "POS-LIFECYCLE");
    }

    private static OfficialDocumentLifecycleEventReadModel CreateEvent(
        string eventType,
        bool originalMarker = false,
        bool reprintMarker = false,
        bool voidMarker = false,
        bool refundMarker = false,
        bool cancelMarker = false,
        string? reason = null)
    {
        return new OfficialDocumentLifecycleEventReadModel(
            Guid.NewGuid(),
            eventType,
            originalMarker,
            reprintMarker,
            voidMarker,
            refundMarker,
            cancelMarker,
            CashierUserId,
            reason,
            EventTimestamp,
            TerminalId: null,
            MetadataJson: null);
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

    private static void ExpectValidationFailure(Result result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected lifecycle validation failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected lifecycle validation code.");
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

    private static void RequireSuccess(Result result, string message)
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

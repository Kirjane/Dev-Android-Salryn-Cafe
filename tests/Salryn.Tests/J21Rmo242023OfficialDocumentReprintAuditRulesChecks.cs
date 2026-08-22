using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentReprintAuditRulesChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("25333333-3333-3333-3333-333333333331");
    private static readonly Guid SaleId = Guid.Parse("25333333-3333-3333-3333-333333333332");
    private static readonly Guid MachineProfileId = Guid.Parse("25333333-3333-3333-3333-333333333333");
    private static readonly Guid ActorUserId = Guid.Parse("25333333-3333-3333-3333-333333333334");
    private static readonly Guid EventId = Guid.Parse("25333333-3333-3333-3333-333333333335");
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string coreText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentReprintService.cs");
        string sqliteText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialDocumentReprintService.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infrastructureSmokeProgramText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string infrastructureSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialDocumentReprintSmokeChecks.cs");

        RequireContains(coreText, "IOfficialDocumentReprintService", "Reprint audit rules must expose a Core service contract.");
        RequireContains(coreText, "IOfficialDocumentReprintPersistence", "Reprint audit rules must keep SQLite behind a persistence port.");
        RequireContains(coreText, "OfficialDocumentLifecycleResult", "Reprint audit rules must consume lifecycle state.");
        RequireContains(coreText, "InvalidLifecycleState", "Reprint audit rules must block invalid lifecycle state.");
        RequireContains(coreText, "OriginalEventMissing", "Reprint audit rules must surface missing ORIGINAL event.");
        RequireContains(coreText, "ReasonRequired", "Reprint audit rules must require a reason.");
        RequireContains(coreText, "ActorUserRequired", "Reprint audit rules must require an actor.");
        RequireNotContains(coreText, "Crystal", "Reprint audit rules must not wire report rendering.");
        RequireNotContains(coreText, "ESC/POS", "Reprint audit rules must not wire printer output.");
        RequireNotContains(coreText, "LocalHostCompleteSale", "Reprint audit rules must not wire Local Host complete-sale.");
        RequireNotContains(coreText, "receipt_snapshot", "Reprint audit rules must not use saved sale receipt text.");

        RequireContains(sqliteText, "'REPRINT'", "SQLite reprint service must append REPRINT events.");
        RequireContains(sqliteText, "reprint_marker", "SQLite reprint service must set reprint marker.");
        RequireContains(sqliteText, "OfficialEvidenceMissing", "SQLite reprint service must require snapshot hash evidence.");
        RequireContains(sqliteText, "OriginalEventMissing", "SQLite reprint service must require ORIGINAL event evidence.");
        RequireContains(sqliteText, "never advances numbering", "SQLite reprint service must document no-numbering behavior.");
        RequireNotContains(sqliteText, "UPDATE ", "SQLite reprint service must not update rows.");
        RequireNotContains(sqliteText, "DELETE ", "SQLite reprint service must not delete rows.");
        RequireNotContains(sqliteText, "INSERT INTO sales", "SQLite reprint service must not create sales.");
        RequireNotContains(sqliteText, "bir_official_document_sequences", "SQLite reprint service must not touch official sequences.");

        RequireContains(appServicesText, "public IOfficialDocumentReprintService OfficialDocumentReprints { get; }", "AppServices must expose official document reprints.");
        RequireContains(appServicesText, "new OfficialDocumentReprintService(", "AppServices must compose reprint service.");
        RequireContains(appServicesText, "new SqliteOfficialDocumentReprintService(connectionFactory)", "AppServices must compose SQLite reprint persistence.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentReprintAuditRulesChecks.Run();", "CoreSmoke must run reprint audit rule checks.");
        RequireContains(infrastructureSmokeProgramText, "OfficialDocumentReprintSmokeChecks.Run(projectRoot);", "Infrastructure smoke command must run reprint proof checks.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentReprint.InvalidLifecycleState", "Infrastructure smoke must prove invalid lifecycle blocks reprint.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentReprint.OriginalEventMissing", "Infrastructure smoke must prove missing ORIGINAL blocks reprint.");
        RequireContains(infrastructureSmokeText, "Post-Z reprint", "Infrastructure smoke must prove post-Z reprint stays allowed.");

        VerifyRuntimeValidationAndLifecycleGuard();
    }

    private static void VerifyRuntimeValidationAndLifecycleGuard()
    {
        RecordingLifecycle lifecycle = new(CreateLifecycle(OfficialDocumentLifecycleState.Active));
        RecordingPersistence persistence = new();
        OfficialDocumentReprintService service = new(
            lifecycle,
            persistence,
            () => EventId,
            () => CreatedAt);

        Result<OfficialDocumentReprintResult> result =
            service.AppendAsync(CreateValidRequest() with
            {
                Reason = "  Customer requested copy.  ",
                OfficialDocumentNumber = "  OR-2026-000001  ",
                RegistrationScopeCode = "  LOCAL_SINGLE_STORE  ",
                TerminalId = "  POS-1  "
            }).GetAwaiter().GetResult();

        RequireSuccess(result, "Reprint runtime append should succeed.");
        RequireEqual(EventId, result.Value.EventId, "Reprint event id must come from id factory.");
        RequireEqual(CreatedAt, result.Value.CreatedAt, "Reprint timestamp must come from clock.");
        RequireEqual("Customer requested copy.", persistence.LastRequest?.Request.Reason, "Reprint reason must be trimmed.");
        RequireEqual("OR-2026-000001", persistence.LastRequest?.Request.OfficialDocumentNumber, "Reprint document number must be trimmed.");
        RequireEqual("LOCAL_SINGLE_STORE", persistence.LastRequest?.Request.RegistrationScopeCode, "Reprint scope must be trimmed.");
        RequireEqual("POS-1", persistence.LastRequest?.Request.TerminalId, "Reprint terminal metadata must be trimmed.");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                JournalEntryId = null,
                OfficialDocumentNumber = null
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.DocumentReferenceRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                DocumentKind = null
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.DocumentKindRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.MachineProfileOrScopeRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                Reason = " "
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.ReasonRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                Reason = new string('x', OfficialDocumentReprintService.MaxReasonLength + 1)
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.ReasonTooLong");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                ActorUserId = Guid.Empty
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.ActorUserRequired");

        OfficialDocumentReprintService invalidService = new(
            new RecordingLifecycle(CreateInvalidLifecycle("OfficialDocumentLifecycle.ContradictoryTerminalEvents")),
            new RecordingPersistence(),
            () => EventId,
            () => CreatedAt);

        ExpectFailure(
            invalidService.AppendAsync(CreateValidRequest()).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.InvalidLifecycleState");

        OfficialDocumentReprintService missingOriginalService = new(
            new RecordingLifecycle(CreateInvalidLifecycle("OfficialDocumentLifecycle.OriginalEventMissing")),
            new RecordingPersistence(),
            () => EventId,
            () => CreatedAt);

        ExpectFailure(
            missingOriginalService.AppendAsync(CreateValidRequest()).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.OriginalEventMissing");
    }

    private static OfficialDocumentReprintRequest CreateValidRequest()
    {
        return new OfficialDocumentReprintRequest(
            JournalEntryId,
            OfficialDocumentNumber: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            "Valid reprint reason.",
            ActorUserId,
            "POS-1");
    }

    private static OfficialDocumentLifecycleResult CreateLifecycle(
        OfficialDocumentLifecycleState state)
    {
        List<OfficialDocumentLifecycleEventReadModel> events = new()
        {
            CreateEvent("ORIGINAL", originalMarker: true)
        };

        if (state == OfficialDocumentLifecycleState.Voided)
        {
            events.Add(CreateEvent("VOID", voidMarker: true));
        }
        else if (state == OfficialDocumentLifecycleState.Cancelled)
        {
            events.Add(CreateEvent("CANCEL", cancelMarker: true));
        }

        return OfficialDocumentLifecycleReadService.BuildResult(
            CreateSource(),
            events,
            new OfficialDocumentLifecycleZReadingCoverage(false, null, null));
    }

    private static OfficialDocumentLifecycleResult CreateInvalidLifecycle(string issueCode)
    {
        IReadOnlyList<OfficialDocumentLifecycleEventReadModel> events =
            issueCode == "OfficialDocumentLifecycle.OriginalEventMissing"
                ? Array.Empty<OfficialDocumentLifecycleEventReadModel>()
                : new[]
                {
                    CreateEvent("ORIGINAL", originalMarker: true),
                    CreateEvent("VOID", voidMarker: true),
                    CreateEvent("CANCEL", cancelMarker: true)
                };

        return OfficialDocumentLifecycleReadService.BuildResult(
            CreateSource(),
            events,
            new OfficialDocumentLifecycleZReadingCoverage(false, null, null));
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
            CreatedAt,
            ActorUserId,
            "POS-1");
    }

    private static OfficialDocumentLifecycleEventReadModel CreateEvent(
        string eventType,
        bool originalMarker = false,
        bool voidMarker = false,
        bool cancelMarker = false)
    {
        return new OfficialDocumentLifecycleEventReadModel(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            OriginalMarker: originalMarker,
            ReprintMarker: false,
            VoidMarker: voidMarker,
            RefundMarker: false,
            CancelMarker: cancelMarker,
            CashierUserId: ActorUserId,
            Reason: null,
            CreatedAt: CreatedAt,
            TerminalId: "POS-1",
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

    private static void ExpectFailure(Result<OfficialDocumentReprintResult> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected reprint failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected reprint validation code.");
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

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    private sealed class RecordingLifecycle : IOfficialDocumentLifecycleReadService
    {
        private readonly OfficialDocumentLifecycleResult _result;

        public RecordingLifecycle(OfficialDocumentLifecycleResult result)
        {
            _result = result;
        }

        public Task<Result<OfficialDocumentLifecycleResult>> ReadAsync(
            OfficialDocumentLifecycleReadRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result<OfficialDocumentLifecycleResult>.Success(_result));
        }
    }

    private sealed class RecordingPersistence : IOfficialDocumentReprintPersistence
    {
        public OfficialDocumentReprintPersistenceRequest? LastRequest { get; private set; }

        public Task<Result<OfficialDocumentReprintResult>> AppendAsync(
            OfficialDocumentReprintPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;

            return Task.FromResult(Result<OfficialDocumentReprintResult>.Success(
                new OfficialDocumentReprintResult(
                    request.EventId,
                    request.Request.JournalEntryId ?? JournalEntryId,
                    request.Request.OfficialDocumentNumber ?? "OR-2026-000001",
                    request.Request.Reason ?? string.Empty,
                    request.Request.ActorUserId,
                    request.CreatedAt)));
        }
    }
}

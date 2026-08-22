using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentAuditEventFoundationChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("24111111-1111-1111-1111-111111111111");
    private static readonly Guid EventId = Guid.Parse("24111111-1111-1111-1111-111111111112");
    private static readonly Guid ActorUserId = Guid.Parse("24111111-1111-1111-1111-111111111113");
    private static readonly Guid MachineProfileId = Guid.Parse("24111111-1111-1111-1111-111111111114");
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string coreText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentAuditEventService.cs");
        string sqliteText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialDocumentAuditEventService.cs");
        string migrationText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "BirElectronicJournalCorrectionEventSchemaMigration.cs");
        string factoryText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infrastructureSmokeProgramText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string infrastructureSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "OfficialDocumentAuditEventSmokeChecks.cs");

        RequireContains(coreText, "OfficialDocumentAuditEventKind", "Audit event foundation must expose event kinds.");
        RequireContains(coreText, "Void", "Audit event foundation must support void.");
        RequireContains(coreText, "Cancel", "Audit event foundation must support cancel.");
        RequireContains(coreText, "Correction", "Audit event foundation must support correction.");
        RequireContains(coreText, "IOfficialDocumentAuditEventService", "Audit event foundation must expose a Core service contract.");
        RequireContains(coreText, "IOfficialDocumentAuditEventPersistence", "Audit event foundation must keep SQLite behind a persistence port.");
        RequireContains(coreText, "ReasonRequired", "Audit event foundation must require reasons.");
        RequireContains(coreText, "MaxReasonLength", "Audit event foundation must bound reason length.");
        RequireNotContains(coreText, "receipt_snapshot", "Audit event foundation must not use sales.receipt_snapshot.");
        RequireNotContains(coreText, "LocalHostCompleteSale", "Audit event foundation must not wire Local Host complete-sale.");
        RequireNotContains(coreText, "Crystal", "Audit event foundation must not wire report rendering.");
        RequireNotContains(coreText, "ESC/POS", "Audit event foundation must not wire printer output.");

        RequireContains(sqliteText, "OriginalEventMissing", "SQLite audit event service must require ORIGINAL source event.");
        RequireContains(sqliteText, "OfficialEvidenceMissing", "SQLite audit event service must require snapshot hash evidence.");
        RequireContains(sqliteText, "AlreadyVoided", "SQLite audit event service must reject duplicate void.");
        RequireContains(sqliteText, "AlreadyCancelled", "SQLite audit event service must reject duplicate cancel.");
        RequireContains(sqliteText, "TerminalEventAlreadyExists", "SQLite audit event service must reject void/cancel conflicts.");
        RequireContains(sqliteText, "PostZReadingChangeDeferred", "SQLite audit event service must block post-Z changes.");
        RequireContains(sqliteText, "range_started_at <= $transactionTimestamp", "Post-Z guard must use half-open Z-reading start semantics.");
        RequireContains(sqliteText, "range_ended_at > $transactionTimestamp", "Post-Z guard must use half-open Z-reading end semantics.");
        RequireContains(sqliteText, "audit events are appended beside the ORIGINAL event", "SQLite audit event service must document append-only behavior.");
        RequireNotContains(sqliteText, "UPDATE bir_electronic_journal_entries", "Audit event service must not update journal entries.");
        RequireNotContains(sqliteText, "DELETE FROM bir_electronic_journal_entries", "Audit event service must not delete journal entries.");
        RequireNotContains(sqliteText, "UPDATE sales", "Audit event service must not update sales.");
        RequireNotContains(sqliteText, "UPDATE products", "Audit event service must not update stock.");

        RequireContains(migrationText, "public int Version => 7;", "Correction event migration must be version 7.");
        RequireContains(migrationText, "CORRECTION", "Correction event migration must allow CORRECTION event rows.");
        RequireContains(migrationText, "NEW.event_type IN ('VOID', 'REFUND', 'CANCEL', 'CORRECTION')", "Correction event migration must require correction reasons.");
        RequireContains(migrationText, "instead of adding a parallel audit table", "Correction event migration must reuse the existing journal event table.");
        RequireContains(factoryText, "new BirElectronicJournalCorrectionEventSchemaMigration()", "Database initialization must register correction event migration.");
        RequireContains(appServicesText, "public IOfficialDocumentAuditEventService OfficialDocumentAuditEvents { get; }", "AppServices must expose audit events through the Core contract.");
        RequireContains(appServicesText, "new OfficialDocumentAuditEventService(", "AppServices must compose official document audit event service.");
        RequireContains(appServicesText, "new SqliteOfficialDocumentAuditEventService(connectionFactory)", "AppServices must compose SQLite audit event persistence.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentAuditEventFoundationChecks.Run();", "CoreSmoke must run audit event foundation checks.");
        RequireContains(infrastructureSmokeProgramText, "OfficialDocumentAuditEventSmokeChecks.Run(projectRoot);", "Infrastructure smoke command must run audit event proof checks.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentAuditEvent.PostZReadingChangeDeferred", "Infrastructure smoke must prove post-Z block.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentAuditEvent.AlreadyVoided", "Infrastructure smoke must prove duplicate void rejection.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentAuditEvent.AlreadyCancelled", "Infrastructure smoke must prove duplicate cancel rejection.");
        RequireContains(infrastructureSmokeText, "OfficialDocumentAuditEvent.TerminalEventAlreadyExists", "Infrastructure smoke must prove terminal event conflict rejection.");

        VerifyRuntimeValidationAndNormalization();
    }

    private static void VerifyRuntimeValidationAndNormalization()
    {
        RecordingPersistence persistence = new();
        OfficialDocumentAuditEventService service = new(
            persistence,
            () => EventId,
            () => CreatedAt);

        Result<OfficialDocumentAuditEventResult> result =
            service.AppendAsync(CreateValidRequest() with
            {
                Reason = "  Customer requested correction before close.  ",
                OfficialDocumentNumber = "  OR-2026-000001  ",
                RegistrationScopeCode = "  LOCAL_SINGLE_STORE  ",
                TerminalId = "  POS-1  "
            }).GetAwaiter().GetResult();

        RequireSuccess(result, "Audit event runtime append should succeed.");
        RequireEqual(EventId, result.Value.EventId, "Audit event id must come from id factory.");
        RequireEqual(CreatedAt, result.Value.CreatedAt, "Audit event timestamp must come from clock.");
        RequireEqual("Customer requested correction before close.", persistence.LastRequest?.Request.Reason, "Audit event reason must be trimmed.");
        RequireEqual("OR-2026-000001", persistence.LastRequest?.Request.OfficialDocumentNumber, "Audit event document number must be trimmed.");
        RequireEqual("LOCAL_SINGLE_STORE", persistence.LastRequest?.Request.RegistrationScopeCode, "Audit event scope must be trimmed.");
        RequireEqual("POS-1", persistence.LastRequest?.Request.TerminalId, "Audit event terminal metadata must be trimmed.");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                JournalEntryId = null,
                OfficialDocumentNumber = null
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.DocumentReferenceRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                DocumentKind = null
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.DocumentKindRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                OfficialDocumentNumber = "OR-2026-000001",
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.MachineProfileOrScopeRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                EventKind = (OfficialDocumentAuditEventKind)999
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.UnsupportedEventKind");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                Reason = " "
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.ReasonRequired");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                Reason = new string('x', OfficialDocumentAuditEventService.MaxReasonLength + 1)
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.ReasonTooLong");

        ExpectFailure(
            service.AppendAsync(CreateValidRequest() with
            {
                ActorUserId = Guid.Empty
            }).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.ActorUserRequired");
    }

    private static OfficialDocumentAuditEventRequest CreateValidRequest()
    {
        return new OfficialDocumentAuditEventRequest(
            JournalEntryId,
            OfficialDocumentNumber: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            OfficialDocumentAuditEventKind.Correction,
            "Valid audit reason.",
            ActorUserId,
            "POS-1");
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

    private static void ExpectFailure(Result<OfficialDocumentAuditEventResult> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected audit event failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected audit event validation code.");
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

    private sealed class RecordingPersistence : IOfficialDocumentAuditEventPersistence
    {
        public OfficialDocumentAuditEventPersistenceRequest? LastRequest { get; private set; }

        public Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
            OfficialDocumentAuditEventPersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;

            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Success(
                new OfficialDocumentAuditEventResult(
                    request.EventId,
                    request.Request.JournalEntryId ?? JournalEntryId,
                    request.Request.OfficialDocumentNumber ?? "OR-2026-000001",
                    request.Request.EventKind,
                    request.Request.Reason ?? string.Empty,
                    request.CreatedAt)));
        }
    }
}

using Salryn.App.WinForms.Features.OfficialDocuments;
using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.App.WinForms.Features.OfficialReadings;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class PrelanHarden3OfficialOperatorSurfaceChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string shellPresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "ShellPresenter.cs");
        string shellForm = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string appServices = Read(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string lifecyclePresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialDocuments", "OfficialDocumentLifecyclePresenter.cs");
        string lifecycleForm = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialDocuments", "OfficialDocumentLifecycleForm.cs");
        string readingsPresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialReadings", "OfficialReadingsPresenter.cs");
        string readingsForm = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialReadings", "OfficialReadingsForm.cs");
        string workflowForm = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowForm.cs");
        string workflowBoundary = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(shellPresenter, "ShellModuleKeys.OfficialDocuments", "Shell must expose official documents module.");
        RequireContains(shellPresenter, "ShellModuleKeys.OfficialReadings", "Shell must expose official readings module.");
        RequireContains(shellPresenter, "ShellModuleKeys.OfficialEvidenceExport", "Shell must expose official evidence export module.");
        RequireContains(shellPresenter, "SalrynPermissions.OfficialDocumentReprint", "Official documents module must be permission-gated.");
        RequireContains(shellPresenter, "SalrynPermissions.OfficialReadingView", "Official readings module must be permission-gated.");
        RequireContains(shellPresenter, "SalrynPermissions.OfficialEvidenceExport", "Official evidence export module must be permission-gated.");

        RequireContains(shellForm, "OpenOfficialDocuments()", "Shell must route official documents surface.");
        RequireContains(shellForm, "OpenOfficialReadings()", "Shell must route official readings surface.");
        RequireContains(shellForm, "OpenOfficialEvidenceExport()", "Shell must route official evidence export surface.");
        RequireContains(shellForm, "OfficialDocumentLifecycleForm", "Shell must open lifecycle form through AppServices.");
        RequireContains(shellForm, "OfficialReadingsForm", "Shell must open readings form through AppServices.");
        RequireContains(shellForm, "OfficialOutputTextFileWorkflowForm", "Evidence export must use trusted text-file workflow form.");

        RequireContains(appServices, "OfficialDocumentLifecycleOperations", "AppServices must wire lifecycle operation boundary.");
        RequireContains(appServices, "new OfficialDocumentLifecycleOperationBoundary", "AppServices must compose lifecycle operation boundary.");

        RequireContains(lifecyclePresenter, "IOfficialDocumentLifecycleOperationBoundary", "Lifecycle presenter must use lifecycle operation boundary.");
        RequireContains(lifecyclePresenter, "AppendLifecycleEventAsync", "Lifecycle presenter must append lifecycle events through boundary.");
        RequireContains(lifecyclePresenter, "OfficialDocumentAuditEvent.ReasonRequired", "Lifecycle presenter must require reason.");
        RequireContains(lifecyclePresenter, "_session.UserId", "Lifecycle presenter must require actor user id.");
        RequireContains(lifecyclePresenter, "SalrynPermissions.OfficialDocumentReprint", "Lifecycle presenter must fail closed on reprint permission.");

        RequireContains(readingsPresenter, "IOfficialXReadingService", "Readings presenter must use X-reading service.");
        RequireContains(readingsPresenter, "IOfficialZReadingService", "Readings presenter must use Z-reading service.");
        RequireContains(readingsPresenter, "AdjustedSummary", "Readings presenter must surface enriched X-reading adjusted summary.");
        RequireContains(readingsPresenter, "OfficialZReadingEnrichmentPayload", "Readings presenter must surface enriched Z persistence.");

        RequireContains(workflowForm, "SalrynPermissions.OfficialOutputRun", "Evidence export form must reference export permissions.");
        RequireContains(workflowForm, "SalrynPermissions.OfficialEvidenceExport", "Evidence export form must reference evidence export permission.");
        RequireContains(workflowBoundary, "OfficialOutputWorkflowPermissions.CallerCanRunExportWorkflow", "Workflow boundary must accept evidence export permission.");
        RequireContains(coreSmoke, "PrelanHarden3OfficialOperatorSurfaceChecks.Run();", "CoreSmoke must run PRELAN-HARDEN-3 guard.");

        foreach (string uiSource in new[] { lifecycleForm, lifecyclePresenter, readingsForm, readingsPresenter, workflowForm })
        {
            RequireNotContains(uiSource, "Microsoft.Data.Sqlite", "Official operator UI must not use raw SQL.");
            RequireNotContains(uiSource, "SqliteConnection", "Official operator UI must not open SQLite directly.");
        }

        foreach (string marker in ForbiddenOutputMarkers())
        {
            RequireNotContains(lifecycleForm, marker, "Lifecycle surface must not introduce forbidden output marker: " + marker);
            RequireNotContains(readingsForm, marker, "Readings surface must not introduce forbidden output marker: " + marker);
        }

        foreach (string marker in ForbiddenLanMarkers())
        {
            RequireNotContains(shellPresenter, marker, "PRELAN-HARDEN-3 must not introduce LAN marker: " + marker);
            RequireNotContains(shellForm, marker, "PRELAN-HARDEN-3 must not introduce LAN marker: " + marker);
            RequireNotContains(appServices, marker, "PRELAN-HARDEN-3 must not introduce LAN marker: " + marker);
        }

        RequireLifecycleBoundaryPermissionFailsClosed();
        RequireLifecycleReasonRequired();
        RequireEvidenceExportPermissionHelper();
    }

    private static void RequireLifecycleBoundaryPermissionFailsClosed()
    {
        FakeAuditEvents fake = new();
        OfficialDocumentLifecycleOperationBoundary boundary = new(fake);

        Result<OfficialDocumentAuditEventResult> denied = boundary.AppendLifecycleEventAsync(
            new OfficialDocumentLifecycleOperationRequest(
                CreateAuditRequest(OfficialDocumentAuditEventKind.Void),
                () => new[] { SalrynPermissions.OfficialDocumentRefund }))
            .GetAwaiter()
            .GetResult();

        if (denied.IsSuccess ||
            !string.Equals(denied.ErrorDetail.Code, "OfficialDocumentLifecycleOperation.PermissionDenied", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Lifecycle boundary must fail closed without matching permission.");
        }
    }

    private static void RequireLifecycleReasonRequired()
    {
        OfficialDocumentAuditEventRequest request = CreateAuditRequest(OfficialDocumentAuditEventKind.Void) with
        {
            Reason = "   "
        };

        Result validation = OfficialDocumentAuditEventService.ValidateRequest(request);

        if (validation.IsSuccess ||
            !string.Equals(validation.ErrorDetail.Code, "OfficialDocumentAuditEvent.ReasonRequired", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Lifecycle action must require reason.");
        }

        CurrentAppSession session = CreateSession(new[]
        {
            SalrynPermissions.OfficialDocumentReprint
        });

        OfficialDocumentLifecyclePresenter presenter = new(
            new EmptyLifecycleRead(),
            new OfficialDocumentLifecycleOperationBoundary(new FakeAuditEvents()),
            new FakeReprints(),
            new EmptyOutputSourceRead(),
            session);

        OfficialDocumentLifecycleOperationResult reprintResult =
            presenter.ReprintAsync("   ").GetAwaiter().GetResult();

        if (reprintResult.IsSuccess ||
            !string.Equals(reprintResult.ErrorCode, "OfficialDocumentReprint.ReasonRequired", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Lifecycle presenter must require reason for reprint.");
        }
    }

    private static void RequireEvidenceExportPermissionHelper()
    {
        CurrentAppSession exportSession = CreateSession(new[]
        {
            SalrynPermissions.OfficialEvidenceExport
        });

        if (!OfficialOutputWorkflowPermissions.CanRunExportWorkflow(exportSession))
        {
            throw new InvalidOperationException("Evidence export permission helper must allow OfficialEvidence.Export.");
        }

        if (!OfficialOutputWorkflowPermissions.CallerCanRunExportWorkflow(exportSession.Permissions))
        {
            throw new InvalidOperationException("Evidence export caller permission helper must allow OfficialEvidence.Export.");
        }
    }

    private static OfficialDocumentAuditEventRequest CreateAuditRequest(OfficialDocumentAuditEventKind kind)
    {
        return new OfficialDocumentAuditEventRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "OR-0001",
            ElectronicJournalDocumentKind.OfficialReceipt,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "SCOPE-1",
            kind,
            "Controlled reason",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "TERM-1");
    }

    private static CurrentAppSession CreateSession(IReadOnlyList<string> permissions)
    {
        return new CurrentAppSession(
            userId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            roleId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            username: "operator",
            displayName: "Operator",
            roleName: "Admin",
            status: UserStatus.Active,
            permissions: permissions,
            startedAt: DateTimeOffset.UtcNow);
    }

    private static IEnumerable<string> ForbiddenOutputMarkers()
    {
        yield return "CrystalReports";
        yield return "ESC/POS";
        yield return "EscPos";
        yield return "System.Drawing.Printing";
        yield return "PdfSharp";
        yield return "iText";
    }

    private static IEnumerable<string> ForbiddenLanMarkers()
    {
        yield return "SignalR";
        yield return "WebSocket";
        yield return "TcpListener";
        yield return "HttpListener";
        yield return "pairing token";
        yield return "TerminalRegistration";
    }

    private sealed class FakeAuditEvents : IOfficialDocumentAuditEventService
    {
        public Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
            OfficialDocumentAuditEventRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Success(
                new OfficialDocumentAuditEventResult(
                    Guid.NewGuid(),
                    request.JournalEntryId ?? Guid.Empty,
                    request.OfficialDocumentNumber ?? string.Empty,
                    request.EventKind,
                    request.Reason ?? string.Empty,
                    DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeReprints : IOfficialDocumentReprintService
    {
        public Task<Result<OfficialDocumentReprintResult>> AppendAsync(
            OfficialDocumentReprintRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<OfficialDocumentReprintResult>.Success(
                new OfficialDocumentReprintResult(
                    Guid.NewGuid(),
                    request.JournalEntryId ?? Guid.Empty,
                    request.OfficialDocumentNumber ?? string.Empty,
                    request.Reason ?? string.Empty,
                    request.ActorUserId,
                    DateTimeOffset.UtcNow)));
        }
    }

    private sealed class EmptyLifecycleRead : IOfficialDocumentLifecycleReadService
    {
        public Task<Result<OfficialDocumentLifecycleResult>> ReadAsync(
            OfficialDocumentLifecycleReadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleResult>.Fail(Error.NotFound(
                "OfficialDocumentLifecycle.JournalEntryNotFound",
                "Not found.")));
        }
    }

    private sealed class EmptyOutputSourceRead : IOfficialOutputSourceReadService
    {
        public Task<Result<OfficialOutputSourceResult>> ReadAsync(
            OfficialOutputSourceReadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<OfficialOutputSourceResult>.Fail(Error.NotFound(
                "OfficialOutputSource.JournalEntryNotFound",
                "Not found.")));
        }
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

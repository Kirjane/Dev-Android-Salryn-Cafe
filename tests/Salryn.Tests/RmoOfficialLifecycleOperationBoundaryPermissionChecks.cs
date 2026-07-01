using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class RmoOfficialLifecycleOperationBoundaryPermissionChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ActorUserId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string boundarySource = Read(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "OfficialOutput",
            "OfficialDocumentLifecycleOperationBoundary.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(boundarySource, "request.AuditRequest.EventKind", "Boundary must derive permission from submitted audit event kind.");
        RequireNotContains(boundarySource, "request.EventKind", "Boundary must not use a separate outer event kind authority.");
        RequireContains(coreSmoke, "RmoOfficialLifecycleOperationBoundaryPermissionChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run lifecycle permission boundary checks.");

        FakeOfficialDocumentAuditEventService fake = new();
        OfficialDocumentLifecycleOperationBoundary boundary = new(fake);

        Result<OfficialDocumentAuditEventResult> voidPermissionRefundAudit =
            await boundary.AppendLifecycleEventAsync(new OfficialDocumentLifecycleOperationRequest(
                CreateAuditRequest(OfficialDocumentAuditEventKind.Refund),
                () => new[] { SalrynPermissions.OfficialDocumentVoid }));

        RequireFailureCode(voidPermissionRefundAudit, "OfficialDocumentLifecycleOperation.PermissionDenied", "Void permission must not authorize Refund audit append.");
        RequireEqual(0, fake.AppendCallCount, "Audit service must not be called on permission mismatch.");

        fake.Reset();

        Result<OfficialDocumentAuditEventResult> refundPermissionVoidAudit =
            await boundary.AppendLifecycleEventAsync(new OfficialDocumentLifecycleOperationRequest(
                CreateAuditRequest(OfficialDocumentAuditEventKind.Void),
                () => new[] { SalrynPermissions.OfficialDocumentRefund }));

        RequireFailureCode(refundPermissionVoidAudit, "OfficialDocumentLifecycleOperation.PermissionDenied", "Refund permission must not authorize Void audit append.");
        RequireEqual(0, fake.AppendCallCount, "Audit service must not be called on cross-kind permission mismatch.");

        fake.Reset();

        Result<OfficialDocumentAuditEventResult> matchedRefund =
            await boundary.AppendLifecycleEventAsync(new OfficialDocumentLifecycleOperationRequest(
                CreateAuditRequest(OfficialDocumentAuditEventKind.Refund),
                () => new[] { SalrynPermissions.OfficialDocumentRefund }));

        RequireSuccess(matchedRefund, "Matched Refund permission and audit kind should succeed.");
        RequireEqual(1, fake.AppendCallCount, "Audit service must be called once for matched permission and audit kind.");
        RequireEqual(OfficialDocumentAuditEventKind.Refund, fake.LastRequest!.EventKind, "Submitted audit kind must be Refund.");
    }

    private static OfficialDocumentAuditEventRequest CreateAuditRequest(OfficialDocumentAuditEventKind eventKind)
    {
        return new OfficialDocumentAuditEventRequest(
            JournalEntryId,
            null,
            null,
            null,
            null,
            eventKind,
            "Lifecycle boundary permission test reason.",
            ActorUserId,
            "POS-TEST");
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

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }

    private static void RequireNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Unexpected: " + needle);
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + " " + result.Error);
        }
    }

    private static void RequireFailureCode<T>(Result<T> result, string code, string message)
    {
        if (result.IsSuccess || !string.Equals(result.ErrorDetail.Code, code, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Expected code " + code + " but got " + result.Error);
        }
    }

    private static void RequireEqual(int expected, int actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(message + $" Expected {expected}, got {actual}.");
        }
    }

    private static void RequireEqual(OfficialDocumentAuditEventKind expected, OfficialDocumentAuditEventKind actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(message + $" Expected {expected}, got {actual}.");
        }
    }

    private sealed class FakeOfficialDocumentAuditEventService : IOfficialDocumentAuditEventService
    {
        public int AppendCallCount { get; private set; }

        public OfficialDocumentAuditEventRequest? LastRequest { get; private set; }

        public Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
            OfficialDocumentAuditEventRequest request,
            CancellationToken cancellationToken = default)
        {
            AppendCallCount++;
            LastRequest = request;

            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Success(
                new OfficialDocumentAuditEventResult(
                    Guid.NewGuid(),
                    request.JournalEntryId!.Value,
                    "OR-TEST-000001",
                    request.EventKind,
                    request.Reason!,
                    DateTimeOffset.UtcNow)));
        }

        public void Reset()
        {
            AppendCallCount = 0;
            LastRequest = null;
        }
    }
}

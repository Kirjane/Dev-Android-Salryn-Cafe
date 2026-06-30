using System.Security.Cryptography;
using System.Text;
using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services;

// Kirch: why - RMO-A6.1 boundary-level proof. The UI boundary lives in App.WinForms (net10.0-windows) and cannot be
// constructed from the net10.0 test project, so genuine boundary instantiation with fake recorders runs here.
internal static class OfficialOutputTextFileWorkflowUiBoundaryAuditSmokeChecks
{
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 26, 19, 30, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("eeeeeeee-ffff-aaaa-bbbb-cccccccccccc");
    private const string TerminalId = "POS-UI-BOUNDARY-AUDIT";

    private const string HostileAuditMessage =
        "SQLite Error 19: FOREIGN KEY constraint failed at C:\\Users\\kirch\\Salryn\\.salryn-dev\\data\\salryn.db";

    private static readonly string[] HostileTokens =
    {
        "SQLite",
        "FOREIGN KEY",
        "C:\\",
        ".salryn-dev",
        "salryn.db",
        "stack trace",
        "Exception"
    };

    public static void Run()
    {
        Console.WriteLine("===== OFFICIAL OUTPUT TEXT-FILE UI BOUNDARY AUDIT SMOKE =====");

        VerifySuccessRecordsAuditThroughBoundary();
        VerifyDuplicateConflictRecordsAuditThroughBoundary();
        VerifyPermissionDeniedRecordsAuditAndBlocks();
        VerifyPreOutputAuditFailureIsSanitized();
        VerifyPostOutputAuditFailureIsSanitized();

        Console.WriteLine("Official output text-file UI boundary audit smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void VerifySuccessRecordsAuditThroughBoundary()
    {
        OfficialOutputExportPackage package = CreatePackage();
        FakeAuditRecorder recorder = new(Result.Success());
        FakeWorkflowGateway workflow = new(SuccessResult());

        OfficialOutputTextFileWorkflowUiBoundary boundary = new(
            new FakeReadinessGateway(available: true),
            workflow,
            new FakePackageSupplyBoundary(Result<OfficialOutputExportPackage>.Success(package)),
            recorder);

        OfficialOutputTextFileWorkflowUiCommandResult result =
            boundary.TryRunAsync(BuildRequest(withPermission: true)).GetAwaiter().GetResult();

        Require(result.Succeeded, "Boundary success run must succeed.");
        Require(workflow.Ran, "Boundary success run must reach the workflow gateway.");
        Require(recorder.Recorded.Count == 1, "Boundary success run must record exactly one audit event.");

        OfficialOutputTextFileWorkflowAuditRecordRequest recorded = recorder.Recorded[0];
        Require(recorded.Succeeded, "Recorded success audit request must mark success.");
        Require(
            ResolveAction(recorded) == OfficialOutputTextFileWorkflowAuditActions.RunSucceeded,
            "Boundary success run must record OFFICIAL_OUTPUT_TEXT_FILE_RUN_SUCCEEDED.");
        Require(recorded.UserId == UserId, "Recorded success audit request must carry the requesting user id.");
        Require(
            string.Equals(recorded.TerminalId, TerminalId, StringComparison.Ordinal),
            "Recorded success audit request must carry the terminal id.");
        Require(
            string.Equals(recorded.PackageHash, package.PackageHash, StringComparison.Ordinal),
            "Recorded success audit request must carry the package hash.");
    }

    private static void VerifyDuplicateConflictRecordsAuditThroughBoundary()
    {
        OfficialOutputExportPackage package = CreatePackage();
        FakeAuditRecorder recorder = new(Result.Success());
        FakeWorkflowGateway workflow = new(FailureResult(
            "OfficialTextFileOutput.OutputPackageAlreadyExists",
            "Duplicate official output package run was refused."));

        OfficialOutputTextFileWorkflowUiBoundary boundary = new(
            new FakeReadinessGateway(available: true),
            workflow,
            new FakePackageSupplyBoundary(Result<OfficialOutputExportPackage>.Success(package)),
            recorder);

        OfficialOutputTextFileWorkflowUiCommandResult result =
            boundary.TryRunAsync(BuildRequest(withPermission: true)).GetAwaiter().GetResult();

        Require(!result.Succeeded, "Boundary duplicate conflict run must not succeed.");
        Require(recorder.Recorded.Count == 1, "Boundary duplicate conflict run must record exactly one audit event.");
        Require(
            ResolveAction(recorder.Recorded[0]) == OfficialOutputTextFileWorkflowAuditActions.OutputPackageAlreadyExists,
            "Boundary duplicate conflict run must record OFFICIAL_OUTPUT_TEXT_FILE_OUTPUT_PACKAGE_ALREADY_EXISTS.");
    }

    private static void VerifyPermissionDeniedRecordsAuditAndBlocks()
    {
        OfficialOutputExportPackage package = CreatePackage();
        FakeAuditRecorder recorder = new(Result.Success());
        FakeWorkflowGateway workflow = new(SuccessResult());

        OfficialOutputTextFileWorkflowUiBoundary boundary = new(
            new FakeReadinessGateway(available: true),
            workflow,
            new FakePackageSupplyBoundary(Result<OfficialOutputExportPackage>.Success(package)),
            recorder);

        OfficialOutputTextFileWorkflowUiCommandResult result =
            boundary.TryRunAsync(BuildRequest(withPermission: false)).GetAwaiter().GetResult();

        Require(!result.Succeeded, "Boundary permission-denied run must not succeed.");
        Require(!result.AttemptedExecution, "Boundary permission-denied run must not attempt execution.");
        Require(!workflow.Ran, "Boundary permission-denied run must not reach the workflow gateway.");
        Require(recorder.Recorded.Count == 1, "Boundary permission-denied run must record exactly one audit event.");
        Require(
            ResolveAction(recorder.Recorded[0]) == OfficialOutputTextFileWorkflowAuditActions.PermissionDenied,
            "Boundary permission-denied run must record OFFICIAL_OUTPUT_TEXT_FILE_PERMISSION_DENIED.");
    }

    private static void VerifyPreOutputAuditFailureIsSanitized()
    {
        OfficialOutputExportPackage package = CreatePackage();
        FakeAuditRecorder recorder = new(Result.Fail(Error.Unexpected("Audit.WriteFailed", HostileAuditMessage)));
        FakeWorkflowGateway workflow = new(SuccessResult());

        OfficialOutputTextFileWorkflowUiBoundary boundary = new(
            new FakeReadinessGateway(available: true),
            workflow,
            new FakePackageSupplyBoundary(Result<OfficialOutputExportPackage>.Success(package)),
            recorder);

        // Permission-denied path keeps this pre-output: no official-output files are generated.
        OfficialOutputTextFileWorkflowUiCommandResult result =
            boundary.TryRunAsync(BuildRequest(withPermission: false)).GetAwaiter().GetResult();

        Require(!result.Succeeded, "Pre-output audit failure must not succeed.");
        Require(!workflow.Ran, "Pre-output audit failure must not reach the workflow gateway.");
        RequireControlledAuditFailure(result, "Pre-output audit failure");
        RequireSanitized(result, "Pre-output audit failure");
    }

    private static void VerifyPostOutputAuditFailureIsSanitized()
    {
        OfficialOutputExportPackage package = CreatePackage();
        FakeAuditRecorder recorder = new(Result.Fail(Error.Unexpected("Audit.WriteFailed", HostileAuditMessage)));
        FakeWorkflowGateway workflow = new(SuccessResult());

        OfficialOutputTextFileWorkflowUiBoundary boundary = new(
            new FakeReadinessGateway(available: true),
            workflow,
            new FakePackageSupplyBoundary(Result<OfficialOutputExportPackage>.Success(package)),
            recorder);

        OfficialOutputTextFileWorkflowUiCommandResult result =
            boundary.TryRunAsync(BuildRequest(withPermission: true)).GetAwaiter().GetResult();

        Require(workflow.Ran, "Post-output audit failure scenario must have reached the workflow gateway.");
        Require(!result.Succeeded, "Post-output audit failure must report a non-success result.");
        RequireControlledAuditFailure(result, "Post-output audit failure");
        RequireSanitized(result, "Post-output audit failure");
        Require(result.RuntimeOutputRootPath is null, "Post-output audit failure must not echo a runtime output root path.");
    }

    private static void RequireControlledAuditFailure(
        OfficialOutputTextFileWorkflowUiCommandResult result,
        string label)
    {
        Require(
            string.Equals(result.StatusCode, OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode, StringComparison.Ordinal),
            label + " must use the controlled audit-failure status code.");
        Require(
            string.Equals(result.ErrorCode, OfficialOutputTextFileWorkflowAuditFailure.AuditRecordFailedCode, StringComparison.Ordinal),
            label + " must use the controlled audit-failure error code.");
    }

    private static void RequireSanitized(
        OfficialOutputTextFileWorkflowUiCommandResult result,
        string label)
    {
        string surface = string.Join(
            "\n",
            result.StatusCode,
            result.StatusMessage,
            result.ErrorCode ?? string.Empty,
            result.ErrorMessage ?? string.Empty,
            result.RuntimeOutputRootPath ?? string.Empty);

        foreach (string token in HostileTokens)
        {
            if (surface.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + " must not expose hostile token: " + token);
            }
        }

        if (surface.Contains("rollback", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(label + " must not claim rollback.");
        }

        if (surface.Contains("deleted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(label + " must not claim deletion.");
        }
    }

    private static OfficialOutputTextFileWorkflowUiTriggerRequest BuildRequest(bool withPermission)
    {
        string outputRoot = Path.Combine(
            Path.GetTempPath(),
            "salryn-ui-boundary-audit-" + Guid.NewGuid().ToString("N"));

        return new OfficialOutputTextFileWorkflowUiTriggerRequest(
            OutputRootPath: outputRoot,
            RequestedAt: Timestamp,
            RequestedByUserId: UserId,
            TerminalId: TerminalId,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            SourceSelectionRequest: null,
            CallerPermissions: withPermission
                ? new[] { SalrynPermissions.OfficialOutputRun }
                : Array.Empty<string>());
    }

    private static AppOfficialOutputTextFileWorkflowResult SuccessResult()
    {
        return new AppOfficialOutputTextFileWorkflowResult(
            Succeeded: true,
            Verified: true,
            StatusCode: "AppOfficialOutputTextFileWorkflow.Verified",
            StatusMessage: "Official output text-file workflow completed.",
            ErrorCode: null,
            ErrorMessage: null,
            References: null,
            RuntimeOutputRootPath: Path.Combine(Path.GetTempPath(), "salryn-ui-boundary-audit-output"));
    }

    private static AppOfficialOutputTextFileWorkflowResult FailureResult(string code, string message)
    {
        return AppOfficialOutputTextFileWorkflowResult.Failure(Error.Conflict(code, message));
    }

    private static string ResolveAction(OfficialOutputTextFileWorkflowAuditRecordRequest request)
    {
        return OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
            request.Succeeded,
            request.ReadinessBlocked,
            request.ErrorCode);
    }

    private static OfficialOutputExportPackage CreatePackage()
    {
        IReadOnlyList<OfficialOutputExportPayload> payloads = new[]
        {
            Payload(OfficialOutputArtifactFamily.ReceiptInvoice, "OFFICIAL_TEXT_PREVIEW", "salryn://official-text-preview/receipt", "receipt-line-1\nreceipt-line-2"),
            Payload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_X_READING_PREVIEW", "salryn://official-reading-preview/XReading/x", "x-reading-line"),
            Payload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_Z_READING_PREVIEW", "salryn://official-reading-preview/ZReading/z", "z-reading-line"),
            Payload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_AUDIT_TRAIL_PREVIEW", "salryn://official-audit-evidence-preview/AuditTrail/audit", "audit-line"),
            Payload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW", "salryn://official-audit-evidence-preview/ElectronicJournal/ej", "electronic-journal-line")
        };

        OfficialOutputArtifactManifestPackage manifest = Value(new OfficialOutputArtifactManifestBuilder().Build(
            payloads.Select(payload => new OfficialOutputArtifactManifestEntry(
                payload.ArtifactFamily,
                payload.ArtifactLabel,
                payload.ArtifactReference,
                payload.ContentHash,
                payload.HashAlgorithm,
                payload.LineCount,
                "Preview",
                Source(payload),
                null)).ToArray(),
            Timestamp));

        return Value(new OfficialOutputExportPackageBuilder().Build(manifest, payloads));
    }

    private static OfficialOutputExportPayload Payload(
        OfficialOutputArtifactFamily family,
        string label,
        string reference,
        string text)
    {
        return new OfficialOutputExportPayload(family, label, reference, Hash(text), "SHA-256", text.Split('\n').Length, text);
    }

    private static OfficialOutputArtifactManifestSource Source(OfficialOutputExportPayload payload)
    {
        return new OfficialOutputArtifactManifestSource(
            payload.ArtifactFamily + "Preview",
            payload.ArtifactReference,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OfficialReceipt" : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "OR-2026-000001" : null,
            null,
            null,
            null,
            "POS-SMOKE",
            "cashier-smoke",
            new DateOnly(2026, 6, 26),
            null,
            null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? false : null,
            payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice ? "Active" : null,
            Timestamp,
            null);
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static T Value<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }

        return result.Value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeReadinessGateway : IAppOfficialOutputTextFileWorkflowReadinessGateway
    {
        private readonly bool _available;

        public FakeReadinessGateway(bool available)
        {
            _available = available;
        }

        public AppOfficialOutputTextFileWorkflowReadinessResult CheckReadiness(
            AppOfficialOutputTextFileWorkflowReadinessRequest? request)
        {
            AppOfficialOutputTextFileWorkflowReadinessState state = new(
                DirectGatewayAvailable: true,
                LocalHostGatewayAvailable: true,
                LocalHostRuntimeHolderAvailable: true,
                LocalHostRuntimeStartupSucceeded: true,
                RequestContextProviderAvailable: true,
                SessionIdentityProviderAvailable: true,
                AuthenticatedAppIdentityAvailable: true,
                RequestContextCanBeCreated: true,
                OutputRootSupplied: true,
                TrustedExportPackageSupplied: true,
                TextFileOnly: true);

            return _available
                ? AppOfficialOutputTextFileWorkflowReadinessResult.Available(state)
                : AppOfficialOutputTextFileWorkflowReadinessResult.Unavailable(
                    state,
                    new[] { new AppOfficialOutputTextFileWorkflowReadinessIssue("Readiness.Blocked", "blocked") });
        }
    }

    private sealed class FakeWorkflowGateway : IAppOfficialOutputTextFileWorkflowGateway
    {
        private readonly AppOfficialOutputTextFileWorkflowResult _result;

        public FakeWorkflowGateway(AppOfficialOutputTextFileWorkflowResult result)
        {
            _result = result;
        }

        public bool Ran { get; private set; }

        public Task<AppOfficialOutputTextFileWorkflowResult> RunAsync(
            AppOfficialOutputTextFileWorkflowRequest? request,
            CancellationToken cancellationToken = default)
        {
            Ran = true;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakePackageSupplyBoundary : IOfficialOutputTextFilePackageSupplyBoundary
    {
        private readonly Result<OfficialOutputExportPackage> _result;

        public FakePackageSupplyBoundary(Result<OfficialOutputExportPackage> result)
        {
            _result = result;
        }

        public OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary() => Summary();

        public OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary(
            OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest) => Summary();

        public Result<OfficialOutputExportPackage> TryGetPackage() => _result;

        public Result<OfficialOutputExportPackage> TryGetPackage(
            OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest) => _result;

        public Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Summary());

        public Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
            OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
            CancellationToken cancellationToken = default) => Task.FromResult(Summary());

        public Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(_result);

        public Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
            OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
            CancellationToken cancellationToken = default) => Task.FromResult(_result);

        private OfficialOutputTextFilePackageSupplySummary Summary()
        {
            return _result.IsSuccess
                ? OfficialOutputTextFilePackageSupplySummary.Available(_result.Value)
                : OfficialOutputTextFilePackageSupplySummary.Unavailable(
                    _result.ErrorDetail.Code,
                    _result.ErrorDetail.Message);
        }
    }

    private sealed class FakeAuditRecorder : IOfficialOutputTextFileWorkflowAuditRecorder
    {
        private readonly Result _result;

        public FakeAuditRecorder(Result result)
        {
            _result = result;
        }

        public List<OfficialOutputTextFileWorkflowAuditRecordRequest> Recorded { get; } = new();

        public Task<Result> RecordRunAttemptAsync(
            OfficialOutputTextFileWorkflowAuditRecordRequest request,
            CancellationToken cancellationToken = default)
        {
            Recorded.Add(request);
            return Task.FromResult(_result);
        }
    }
}

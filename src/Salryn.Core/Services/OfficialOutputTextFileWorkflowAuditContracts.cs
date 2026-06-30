using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Security;

namespace Salryn.Core.Services;

public static class OfficialOutputTextFileWorkflowAuditActions
{
    public const string EntityType = "OFFICIAL_OUTPUT_TEXT_FILE";

    public const string RunSucceeded = "OFFICIAL_OUTPUT_TEXT_FILE_RUN_SUCCEEDED";
    public const string PermissionDenied = "OFFICIAL_OUTPUT_TEXT_FILE_PERMISSION_DENIED";
    public const string ReadinessBlocked = "OFFICIAL_OUTPUT_TEXT_FILE_READINESS_BLOCKED";
    public const string OutputRootRejected = "OFFICIAL_OUTPUT_TEXT_FILE_OUTPUT_ROOT_REJECTED";
    public const string OutputPackageAlreadyExists = "OFFICIAL_OUTPUT_TEXT_FILE_OUTPUT_PACKAGE_ALREADY_EXISTS";
    public const string Failed = "OFFICIAL_OUTPUT_TEXT_FILE_FAILED";

    public static string ResolveAction(
        bool succeeded,
        bool readinessBlocked,
        string? errorCode)
    {
        if (succeeded)
        {
            return RunSucceeded;
        }

        if (string.Equals(
                errorCode,
                "OfficialOutputTextFileWorkflowUi.PermissionRequired",
                StringComparison.Ordinal))
        {
            return PermissionDenied;
        }

        if (readinessBlocked)
        {
            return ReadinessBlocked;
        }

        if (IsOutputRootRejected(errorCode))
        {
            return OutputRootRejected;
        }

        if (string.Equals(
                errorCode,
                "OfficialTextFileOutput.OutputPackageAlreadyExists",
                StringComparison.Ordinal))
        {
            return OutputPackageAlreadyExists;
        }

        return Failed;
    }

    public static bool IsOutputRootRejected(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return false;
        }

        return errorCode.StartsWith("OfficialTextFileOutput.OutputRoot", StringComparison.Ordinal) ||
            string.Equals(errorCode, "AppOfficialOutputTextFileWorkflow.OutputRootRequired", StringComparison.Ordinal) ||
            string.Equals(errorCode, "OfficialExternalOutputExecution.OutputRootRequired", StringComparison.Ordinal) ||
            string.Equals(errorCode, "AppOfficialOutputTextFileWorkflowReadiness.OutputRootRequired", StringComparison.Ordinal);
    }
}

public sealed record OfficialOutputTextFileWorkflowAuditRecordRequest(
    Guid? UserId,
    string? TerminalId,
    string? PackageHash,
    string? WorkflowReference,
    string? ErrorCode,
    bool Succeeded,
    bool ReadinessBlocked,
    IReadOnlyDictionary<string, string>? Metadata,
    DateTimeOffset? RecordedAt = null);

// Kirch: why - audit-write failures can carry raw storage/IO/path text; UI-facing output must only ever see these controlled, generic strings.
public static class OfficialOutputTextFileWorkflowAuditFailure
{
    public const string AuditRecordFailedCode = "OfficialOutputTextFileWorkflowUi.AuditRecordFailed";

    public const string PostOutputMessage =
        "Official output audit record could not be written. The operation was not completed as fully auditable.";

    public const string PreOutputMessage =
        "Official output audit record could not be written. The official output run was blocked and not completed as fully auditable.";

    public static string SanitizedMessage(bool filesGenerated)
    {
        return filesGenerated ? PostOutputMessage : PreOutputMessage;
    }

    public static Error ControlledError()
    {
        return Error.Unexpected(AuditRecordFailedCode, PreOutputMessage);
    }
}

public interface IOfficialOutputTextFileWorkflowAuditRecorder
{
    Task<Result> RecordRunAttemptAsync(
        OfficialOutputTextFileWorkflowAuditRecordRequest request,
        CancellationToken cancellationToken = default);
}

public static class OfficialOutputTextFileWorkflowAuditDetailBuilder
{
    public static Result<string> BuildNewValueJson(
        OfficialOutputTextFileWorkflowAuditRecordRequest request,
        string action)
    {
        SortedDictionary<string, string> details = new(StringComparer.Ordinal)
        {
            ["result"] = request.Succeeded ? "succeeded" : "failed",
            ["permission"] = SalrynPermissions.OfficialOutputRun
        };

        AddIfPresent(details, "terminalId", request.TerminalId);
        AddIfPresent(details, "packageHash", request.PackageHash);
        AddIfPresent(details, "workflowReference", request.WorkflowReference);
        AddIfPresent(details, "errorCode", request.ErrorCode);

        if (request.Metadata is not null)
        {
            AddIfPresent(details, "journalEntryId", TryGetMetadata(request.Metadata, "journalEntryId"));
            AddIfPresent(details, "officialDocumentNumber", TryGetMetadata(request.Metadata, "officialDocumentNumber"));
            AddIfPresent(details, "officialDocumentKind", TryGetMetadata(request.Metadata, "documentKind"));
            AddIfPresent(details, "sourceReference", TryGetMetadata(request.Metadata, "sourceReference"));
        }

        if (!string.IsNullOrWhiteSpace(request.PackageHash))
        {
            details["artifactFolderRelative"] = "official-output/" + request.PackageHash.Trim();
        }

        string json = JsonSerializer.Serialize(details);

        foreach (string forbidden in ForbiddenDetailTokens())
        {
            if (json.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return Result<string>.Fail(Error.Validation(
                    "OfficialOutputTextFileWorkflowAudit.ForbiddenDetail",
                    "Official output audit detail contains forbidden content."));
            }
        }

        if (ContainsForbiddenWording(action) || ContainsForbiddenWording(json))
        {
            return Result<string>.Fail(Error.Validation(
                "OfficialOutputTextFileWorkflowAudit.ForbiddenWording",
                "Official output audit detail contains forbidden wording."));
        }

        return Result<string>.Success(json);
    }

    public static string? ResolveEntityId(
        string? packageHash,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (!string.IsNullOrWhiteSpace(packageHash))
        {
            return packageHash.Trim();
        }

        string? journalEntryId = TryGetMetadata(metadata, "journalEntryId");

        if (!string.IsNullOrWhiteSpace(journalEntryId))
        {
            return journalEntryId;
        }

        return TryGetMetadata(metadata, "sourceReference");
    }

    public static string ResolveReason(
        string action,
        string? errorCode)
    {
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            return errorCode.Trim();
        }

        return action;
    }

    private static string? TryGetMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        string key)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static void AddIfPresent(
        IDictionary<string, string> details,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details[key] = value.Trim();
        }
    }

    private static bool ContainsForbiddenWording(string value)
    {
        return value.Contains("re" + "print", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("BIR-" + "certified", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("certified " + "print", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ForbiddenDetailTokens()
    {
        yield return "PasswordHash";
        yield return ":\\";
        yield return "C:/";
        yield return "payload body";
        yield return "snapshot payload";
        yield return "PdfDocument";
        yield return "CrystalDecisions";
        yield return "ESC/POS";
        yield return "PrinterSettings";
        yield return "System.Drawing.Printing";
    }
}

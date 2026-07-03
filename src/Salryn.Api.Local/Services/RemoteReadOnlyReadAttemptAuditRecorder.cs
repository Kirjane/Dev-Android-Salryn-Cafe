using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Api.Local.Services;

public sealed class RemoteReadOnlyReadAttemptAuditRecorder
{
    public const string EntityType = "RemoteReadOnlyLan";

    public const string AllowedAction = "RemoteReadAttempt.Allowed";

    public const string DeniedAction = "RemoteReadAttempt.Denied";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAuditWriteRepository _auditWriteRepository;

    public RemoteReadOnlyReadAttemptAuditRecorder(IAuditWriteRepository auditWriteRepository)
    {
        _auditWriteRepository = auditWriteRepository
            ?? throw new ArgumentNullException(nameof(auditWriteRepository));
    }

    public Task<Result> RecordAllowedAsync(
        string requestedOperation,
        string requestedCategory,
        Guid terminalId,
        string remoteOrigin,
        string? failureReason = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(
            AllowedAction,
            requestedOperation,
            requestedCategory,
            terminalId,
            remoteOrigin,
            failureReason,
            cancellationToken);

    public Task<Result> RecordDeniedAsync(
        string requestedOperation,
        string requestedCategory,
        Guid terminalId,
        string remoteOrigin,
        string failureReason,
        CancellationToken cancellationToken = default) =>
        RecordAsync(
            DeniedAction,
            requestedOperation,
            requestedCategory,
            terminalId,
            remoteOrigin,
            failureReason,
            cancellationToken);

    private async Task<Result> RecordAsync(
        string action,
        string requestedOperation,
        string requestedCategory,
        Guid terminalId,
        string remoteOrigin,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        string safeJson = JsonSerializer.Serialize(
            new
            {
                requestedOperation,
                requestedCategory,
                terminalId = terminalId == Guid.Empty ? null : terminalId.ToString(),
                remoteOrigin = Sanitize(remoteOrigin),
                decision = action == AllowedAction ? "allowed" : "denied",
                failureReason = Sanitize(failureReason),
                readOnlyOnly = true,
                mutationAllowed = false,
                officialWriteAllowed = false,
                officialSequenceAllowed = false
            },
            JsonOptions);

        AuditLog auditLog = new()
        {
            Action = action,
            EntityType = EntityType,
            EntityId = terminalId == Guid.Empty ? null : terminalId.ToString(),
            NewValueJson = safeJson,
            Reason = Sanitize(failureReason),
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await _auditWriteRepository.AddAsync(auditLog, cancellationToken).ConfigureAwait(false);
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        if (trimmed.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("token", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("password", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(".db", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return "[redacted]";
        }

        return trimmed.Length <= 256 ? trimmed : trimmed[..256];
    }
}

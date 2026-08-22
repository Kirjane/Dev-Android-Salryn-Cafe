using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Best-effort operator audit writes after successful committed actions.
/// Failures never fail the business mutation.
/// </summary>
public static class OperatorAuditTrail
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Task TryWriteAsync(
        SqliteConnectionFactory connectionFactory,
        Guid? userId,
        string action,
        string entityType,
        string? entityId,
        object? newValue = null,
        object? oldValue = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        IAuditWriteRepository repository = new SqliteRepositoryFactory(connectionFactory)
            .CreateAuditWriteRepository();
        return TryWriteAsync(repository, userId, action, entityType, entityId, newValue, oldValue, reason, cancellationToken);
    }

    public static async Task TryWriteAsync(
        IAuditWriteRepository auditWriteRepository,
        Guid? userId,
        string action,
        string entityType,
        string? entityId,
        object? newValue = null,
        object? oldValue = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (auditWriteRepository is null || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(entityType))
        {
            return;
        }

        try
        {
            AuditLog auditLog = new()
            {
                UserId = userId is { } id && id != Guid.Empty ? id : null,
                Action = action.Trim(),
                EntityType = entityType.Trim(),
                EntityId = string.IsNullOrWhiteSpace(entityId) ? null : entityId.Trim(),
                NewValueJson = SerializeSafe(newValue),
                OldValueJson = SerializeSafe(oldValue),
                Reason = SanitizeReason(reason),
                CreatedAt = DateTimeOffset.UtcNow
            };

            Result result = await auditWriteRepository
                .AddAsync(auditLog, cancellationToken)
                .ConfigureAwait(false);
            _ = result;
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static string? SerializeSafe(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            string json = JsonSerializer.Serialize(value, JsonOptions);
            if (json.Length > 4000)
            {
                return json[..4000];
            }

            return json;
        }
        catch
        {
            return null;
        }
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        string trimmed = reason.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}

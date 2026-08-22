using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteAuditWriteRepository :
    SqliteRepositoryBase,
    IAuditWriteRepository
{
    public SqliteAuditWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> AddAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (auditLog is null)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "AuditLog.Required",
                "Audit log entry is required.")));
        }

        if (string.IsNullOrWhiteSpace(auditLog.Action))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "AuditLog.ActionRequired",
                "Audit log action is required.")));
        }

        if (string.IsNullOrWhiteSpace(auditLog.EntityType))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "AuditLog.EntityTypeRequired",
                "Audit log entity type is required.")));
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO audit_logs
                    (id, user_id, action, entity_type, entity_id, old_value_json, new_value_json, reason, created_at)
                VALUES
                    ($id, $userId, $action, $entityType, $entityId, $oldValueJson, $newValueJson, $reason, $createdAt);
                """;

            command.Parameters.AddWithValue("$id", auditLog.Id.ToString("D"));
            command.Parameters.AddWithValue(
                "$userId",
                auditLog.UserId.HasValue ? auditLog.UserId.Value.ToString("D") : DBNull.Value);
            command.Parameters.AddWithValue("$action", auditLog.Action.Trim());
            command.Parameters.AddWithValue("$entityType", auditLog.EntityType.Trim());
            command.Parameters.AddWithValue(
                "$entityId",
                string.IsNullOrWhiteSpace(auditLog.EntityId) ? DBNull.Value : auditLog.EntityId.Trim());
            command.Parameters.AddWithValue(
                "$oldValueJson",
                string.IsNullOrWhiteSpace(auditLog.OldValueJson) ? DBNull.Value : auditLog.OldValueJson);
            command.Parameters.AddWithValue(
                "$newValueJson",
                string.IsNullOrWhiteSpace(auditLog.NewValueJson) ? DBNull.Value : auditLog.NewValueJson);
            command.Parameters.AddWithValue(
                "$reason",
                string.IsNullOrWhiteSpace(auditLog.Reason) ? DBNull.Value : auditLog.Reason.Trim());
            command.Parameters.AddWithValue("$createdAt", auditLog.CreatedAt.ToString("O"));

            command.ExecuteNonQuery();

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected(
                "Audit.WriteFailed",
                ex));
        }
    }
}

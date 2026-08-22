using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteUserWriteRepository :
    SqliteRepositoryBase,
    IUserWriteRepository
{
    public SqliteUserWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> CreateAsync(
        UserAdminCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateCreate(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            if (!RoleExists(connection, transaction, request.RoleId))
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.Validation(
                    "Users.RoleRequired",
                    "A valid role is required.")));
            }

            if (UsernameExists(connection, transaction, request.Username.Trim(), exceptUserId: null))
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.Conflict(
                    "Users.UsernameExists",
                    "Username is already used by another account.")));
            }

            Guid userId = Guid.NewGuid();
            string now = DateTimeOffset.UtcNow.ToString("O");

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO users
                        (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
                    VALUES
                        ($id, $username, $displayName, $passwordHash, $roleId, $status, $createdAt, NULL);
                    """;

                command.Parameters.AddWithValue("$id", userId.ToString("D"));
                command.Parameters.AddWithValue("$username", request.Username.Trim());
                command.Parameters.AddWithValue("$displayName", request.DisplayName.Trim());
                command.Parameters.AddWithValue("$passwordHash", request.PasswordHash);
                command.Parameters.AddWithValue("$roleId", request.RoleId.ToString("D"));
                command.Parameters.AddWithValue("$status", (int)request.Status);
                command.Parameters.AddWithValue("$createdAt", now);

                command.ExecuteNonQuery();
            }

            InsertAuditLog(
                connection,
                transaction,
                request.CreatedByUserId,
                "USER_CREATED",
                userId,
                "User account created.",
                now);

            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Users.CreateFailed", ex));
        }
    }

    public Task<Result> UpdateAsync(
        UserAdminUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateUpdate(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            if (!RoleExists(connection, transaction, request.RoleId))
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.Validation(
                    "Users.RoleRequired",
                    "A valid role is required.")));
            }

            if (UsernameExists(connection, transaction, request.Username.Trim(), request.UserId))
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.Conflict(
                    "Users.UsernameExists",
                    "Username is already used by another account.")));
            }

            string now = DateTimeOffset.UtcNow.ToString("O");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE users
                SET
                    username = $username,
                    display_name = $displayName,
                    role_id = $roleId,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;

            command.Parameters.AddWithValue("$id", request.UserId.ToString("D"));
            command.Parameters.AddWithValue("$username", request.Username.Trim());
            command.Parameters.AddWithValue("$displayName", request.DisplayName.Trim());
            command.Parameters.AddWithValue("$roleId", request.RoleId.ToString("D"));
            command.Parameters.AddWithValue("$updatedAt", now);

            int affectedRows = command.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.NotFound(
                    "Users.NotFound",
                    "User account was not found.")));
            }

            InsertAuditLog(
                connection,
                transaction,
                request.UpdatedByUserId,
                "USER_UPDATED",
                request.UserId,
                "User account updated.",
                now);

            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Users.UpdateFailed", ex));
        }
    }

    public Task<Result> ResetPasswordAsync(
        UserAdminPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.UserId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Users.InvalidUserId",
                "User ID is required.")));
        }

        if (request.ResetByUserId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Users.CurrentUserRequired",
                "A current admin user is required.")));
        }

        if (string.IsNullOrWhiteSpace(request.PasswordHash))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Users.PasswordHashRequired",
                "Password hash is required.")));
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            string now = DateTimeOffset.UtcNow.ToString("O");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE users
                SET
                    password_hash = $passwordHash,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;

            command.Parameters.AddWithValue("$id", request.UserId.ToString("D"));
            command.Parameters.AddWithValue("$passwordHash", request.PasswordHash);
            command.Parameters.AddWithValue("$updatedAt", now);

            int affectedRows = command.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.NotFound(
                    "Users.NotFound",
                    "User account was not found.")));
            }

            InsertAuditLog(
                connection,
                transaction,
                request.ResetByUserId,
                "USER_PASSWORD_RESET",
                request.UserId,
                "User password reset by admin.",
                now);

            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Users.ResetPasswordFailed", ex));
        }
    }

    public Task<Result> SetStatusAsync(
        UserAdminStatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.UserId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Users.InvalidUserId",
                "User ID is required.")));
        }

        if (request.ChangedByUserId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Users.CurrentUserRequired",
                "A current admin user is required.")));
        }

        if (request.UserId == request.ChangedByUserId && request.Status != UserStatus.Active)
        {
            return Task.FromResult(Result.Fail(Error.Forbidden(
                "Users.SelfDeactivateBlocked",
                "The current user cannot deactivate their own account.")));
        }

        if (request.Status == UserStatus.Inactive && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Users.DeactivateReasonRequired",
                "A reason is required when deactivating a user.")));
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            string now = DateTimeOffset.UtcNow.ToString("O");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE users
                SET
                    status = $status,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;

            command.Parameters.AddWithValue("$id", request.UserId.ToString("D"));
            command.Parameters.AddWithValue("$status", (int)request.Status);
            command.Parameters.AddWithValue("$updatedAt", now);

            int affectedRows = command.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();
                return Task.FromResult(Result.Fail(Error.NotFound(
                    "Users.NotFound",
                    "User account was not found.")));
            }

            InsertAuditLog(
                connection,
                transaction,
                request.ChangedByUserId,
                request.Status == UserStatus.Active ? "USER_REACTIVATED" : "USER_DEACTIVATED",
                request.UserId,
                request.Reason,
                now);

            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Users.SetStatusFailed", ex));
        }
    }

    private static Result ValidateCreate(UserAdminCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Fail(Error.Validation("Users.UsernameRequired", "Username is required."));
        }

        if (request.Username.Trim().Length < 3)
        {
            return Result.Fail(Error.Validation("Users.UsernameTooShort", "Username must be at least 3 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result.Fail(Error.Validation("Users.DisplayNameRequired", "Display name is required."));
        }

        if (request.RoleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.RoleRequired", "Role is required."));
        }

        if (string.IsNullOrWhiteSpace(request.PasswordHash))
        {
            return Result.Fail(Error.Validation("Users.PasswordHashRequired", "Password hash is required."));
        }

        if (request.CreatedByUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.CurrentUserRequired", "A current admin user is required."));
        }

        return Result.Success();
    }

    private static Result ValidateUpdate(UserAdminUpdateRequest request)
    {
        if (request.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.InvalidUserId", "User ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Fail(Error.Validation("Users.UsernameRequired", "Username is required."));
        }

        if (request.Username.Trim().Length < 3)
        {
            return Result.Fail(Error.Validation("Users.UsernameTooShort", "Username must be at least 3 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result.Fail(Error.Validation("Users.DisplayNameRequired", "Display name is required."));
        }

        if (request.RoleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.RoleRequired", "Role is required."));
        }

        if (request.UpdatedByUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.CurrentUserRequired", "A current admin user is required."));
        }

        return Result.Success();
    }

    private static bool RoleExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid roleId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(1)
            FROM roles
            WHERE id = $roleId;
            """;

        command.Parameters.AddWithValue("$roleId", roleId.ToString("D"));

        return (long)command.ExecuteScalar()! > 0;
    }

    private static bool UsernameExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string username,
        Guid? exceptUserId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = exceptUserId.HasValue
            ? """
                SELECT COUNT(1)
                FROM users
                WHERE lower(username) = lower($username)
                  AND id <> $exceptUserId;
                """
            : """
                SELECT COUNT(1)
                FROM users
                WHERE lower(username) = lower($username);
                """;

        command.Parameters.AddWithValue("$username", username.Trim());

        if (exceptUserId.HasValue)
        {
            command.Parameters.AddWithValue("$exceptUserId", exceptUserId.Value.ToString("D"));
        }

        return (long)command.ExecuteScalar()! > 0;
    }

    private static void InsertAuditLog(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorUserId,
        string action,
        Guid targetUserId,
        string? reason,
        string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_logs
                (id, user_id, action, entity_type, entity_id, old_value_json, new_value_json, reason, created_at)
            VALUES
                ($id, $userId, $action, 'users', $entityId, NULL, NULL, $reason, $createdAt);
            """;

        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$entityId", targetUserId.ToString("D"));
        command.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim());
        command.Parameters.AddWithValue("$createdAt", now);

        command.ExecuteNonQuery();
    }
}

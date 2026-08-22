using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteUserPermissionReadRepository :
    SqliteRepositoryBase,
    IUserPermissionReadRepository
{
    public SqliteUserPermissionReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Kirch: Loads a user and the permissions attached to the user's role for shell routing.
    public Task<Result<UserPermissionReadModel?>> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Task.FromResult(Result<UserPermissionReadModel?>.Fail(Error.Validation(
                "Users.InvalidUserId",
                "User ID is required when reading permissions.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    u.id,
                    u.role_id,
                    u.username,
                    u.display_name,
                    r.name AS role_name,
                    u.status,
                    rp.permission
                FROM users u
                INNER JOIN roles r ON r.id = u.role_id
                LEFT JOIN role_permissions rp ON rp.role_id = r.id
                WHERE u.id = $userId
                ORDER BY rp.permission;
                """;

            command.Parameters.AddWithValue("$userId", userId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();

            Guid readUserId = Guid.Empty;
            Guid roleId = Guid.Empty;
            string username = string.Empty;
            string displayName = string.Empty;
            string roleName = string.Empty;
            UserStatus status = UserStatus.Inactive;
            List<string> permissions = new();

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (readUserId == Guid.Empty)
                {
                    readUserId = Guid.Parse(reader.GetString(0));
                    roleId = Guid.Parse(reader.GetString(1));
                    username = reader.GetString(2);
                    displayName = reader.GetString(3);
                    roleName = reader.GetString(4);
                    status = (UserStatus)reader.GetInt32(5);
                }

                if (!reader.IsDBNull(6))
                {
                    string permission = reader.GetString(6).Trim();

                    if (!string.IsNullOrWhiteSpace(permission))
                    {
                        permissions.Add(permission);
                    }
                }
            }

            if (readUserId == Guid.Empty)
            {
                return Task.FromResult(Result<UserPermissionReadModel?>.Success(null));
            }

            UserPermissionReadModel profile = new(
                readUserId,
                roleId,
                username,
                displayName,
                roleName,
                status,
                permissions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item)
                    .ToArray());

            return Task.FromResult(Result<UserPermissionReadModel?>.Success(profile));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<UserPermissionReadModel?>(
                "Users.FindPermissionsFailed",
                ex));
        }
    }
}
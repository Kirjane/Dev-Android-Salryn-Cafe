using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteUserReadRepository :
    SqliteRepositoryBase,
    IUserReadRepository
{
    public SqliteUserReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<UserReadModel?>> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return FindSingleAsync(
            "Users.FindByIdFailed",
            "u.id = $value",
            userId.ToString("D"),
            cancellationToken);
    }

    public Task<Result<UserReadModel?>> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        return FindSingleAsync(
            "Users.FindByUsernameFailed",
            "u.username = $value",
            username.Trim(),
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<UserReadModel>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = UserSelectSql + """
                ORDER BY u.display_name;
                """;

            using var reader = command.ExecuteReader();

            List<UserReadModel> users = new();

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                users.Add(MapUser(reader));
            }

            IReadOnlyList<UserReadModel> result = users;

            return Task.FromResult(Result<IReadOnlyList<UserReadModel>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<IReadOnlyList<UserReadModel>>(
                "Users.GetAllFailed",
                ex));
        }
    }

    private Task<Result<UserReadModel?>> FindSingleAsync(
        string errorCode,
        string whereClause,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = UserSelectSql + $"""
                WHERE {whereClause}
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$value", value);

            using var reader = command.ExecuteReader();

            UserReadModel? user = reader.Read()
                ? MapUser(reader)
                : null;

            return Task.FromResult(Result<UserReadModel?>.Success(user));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<UserReadModel?>(errorCode, ex));
        }
    }

    private static UserReadModel MapUser(SqliteDataReader reader)
    {
        return new UserReadModel(
            reader.GetGuidValue("id"),
            reader.GetStringValue("username"),
            reader.GetStringValue("display_name"),
            reader.GetStringValue("role_name"),
            reader.GetEnumValue<UserStatus>("status"),
            reader.GetDateTimeOffsetValue("created_at"));
    }

    private const string UserSelectSql = """
        SELECT
            u.id,
            u.username,
            u.display_name,
            r.name AS role_name,
            u.status,
            u.created_at
        FROM users u
        INNER JOIN roles r ON r.id = u.role_id
        """;
}

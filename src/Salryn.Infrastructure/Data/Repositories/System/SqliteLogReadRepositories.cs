using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteAuditReadRepository :
    SqliteRepositoryBase,
    IAuditReadRepository
{
    public SqliteAuditReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<PagedResult<AuditLogReadModel>>> SearchAuditLogsAsync(
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountRows(connection, "audit_logs");
            IReadOnlyList<AuditLogReadModel> items = ReadAuditLogs(
                connection,
                page,
                cancellationToken);

            PagedResult<AuditLogReadModel> result = new(
                items,
                totalCount,
                page.PageNumber,
                page.PageSize);

            return Task.FromResult(Result<PagedResult<AuditLogReadModel>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<PagedResult<AuditLogReadModel>>(
                "Audit.SearchFailed",
                ex));
        }
    }

    private static IReadOnlyList<AuditLogReadModel> ReadAuditLogs(
        SqliteConnection connection,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                a.id,
                a.user_id,
                u.display_name AS user_display_name,
                a.action,
                a.entity_type,
                a.entity_id,
                a.reason,
                a.created_at
            FROM audit_logs a
            LEFT JOIN users u ON u.id = a.user_id
            ORDER BY a.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;

        command.Parameters.AddWithValue("$limit", Math.Max(1, page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, page.Offset));

        using var reader = command.ExecuteReader();

        List<AuditLogReadModel> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? userId = reader.GetNullableString("user_id");

            items.Add(new AuditLogReadModel(
                reader.GetGuidValue("id"),
                string.IsNullOrWhiteSpace(userId) ? null : Guid.Parse(userId),
                reader.GetNullableString("user_display_name"),
                reader.GetStringValue("action"),
                reader.GetStringValue("entity_type"),
                reader.GetNullableString("entity_id"),
                reader.GetNullableString("reason"),
                reader.GetDateTimeOffsetValue("created_at")));
        }

        return items;
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }
}

public sealed class SqliteErrorLogReadRepository :
    SqliteRepositoryBase,
    IErrorLogReadRepository
{
    public SqliteErrorLogReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<PagedResult<ErrorLogReadModel>>> SearchErrorLogsAsync(
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountRows(connection, "error_logs");
            IReadOnlyList<ErrorLogReadModel> items = ReadErrorLogs(
                connection,
                page,
                cancellationToken);

            PagedResult<ErrorLogReadModel> result = new(
                items,
                totalCount,
                page.PageNumber,
                page.PageSize);

            return Task.FromResult(Result<PagedResult<ErrorLogReadModel>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<PagedResult<ErrorLogReadModel>>(
                "ErrorLogs.SearchFailed",
                ex));
        }
    }

    private static IReadOnlyList<ErrorLogReadModel> ReadErrorLogs(
        SqliteConnection connection,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                e.id,
                e.user_id,
                u.display_name AS user_display_name,
                e.area,
                e.message,
                e.technical_detail,
                e.created_at
            FROM error_logs e
            LEFT JOIN users u ON u.id = e.user_id
            ORDER BY e.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;

        command.Parameters.AddWithValue("$limit", Math.Max(1, page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, page.Offset));

        using var reader = command.ExecuteReader();

        List<ErrorLogReadModel> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? userId = reader.GetNullableString("user_id");

            items.Add(new ErrorLogReadModel(
                reader.GetGuidValue("id"),
                string.IsNullOrWhiteSpace(userId) ? null : Guid.Parse(userId),
                reader.GetNullableString("user_display_name"),
                reader.GetStringValue("area"),
                reader.GetStringValue("message"),
                reader.GetNullableString("technical_detail"),
                reader.GetDateTimeOffsetValue("created_at")));
        }

        return items;
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }
}

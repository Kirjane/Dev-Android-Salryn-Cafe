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
        AuditLogSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountAuditLogs(connection, filter);
            IReadOnlyList<AuditLogReadModel> items = ReadAuditLogs(
                connection,
                filter,
                cancellationToken);

            PagedResult<AuditLogReadModel> result = new(
                items,
                totalCount,
                filter.Page.PageNumber,
                filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<AuditLogReadModel>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<PagedResult<AuditLogReadModel>>(
                "Audit.SearchFailed",
                ex));
        }
    }

    private static int CountAuditLogs(
        SqliteConnection connection,
        AuditLogSearchFilter filter)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM audit_logs a
            LEFT JOIN users u ON u.id = a.user_id
            WHERE ($from IS NULL OR a.created_at >= $from)
              AND ($to IS NULL OR a.created_at <= $to)
              AND ($search IS NULL OR (
                    a.action LIKE $searchLike
                    OR a.entity_type LIKE $searchLike
                    OR IFNULL(a.entity_id, '') LIKE $searchLike
                    OR IFNULL(u.display_name, '') LIKE $searchLike
                    OR IFNULL(a.reason, '') LIKE $searchLike));
            """;

        AddAuditFilterParameters(command, filter);

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }

    private static IReadOnlyList<AuditLogReadModel> ReadAuditLogs(
        SqliteConnection connection,
        AuditLogSearchFilter filter,
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
            WHERE ($from IS NULL OR a.created_at >= $from)
              AND ($to IS NULL OR a.created_at <= $to)
              AND ($search IS NULL OR (
                    a.action LIKE $searchLike
                    OR a.entity_type LIKE $searchLike
                    OR IFNULL(a.entity_id, '') LIKE $searchLike
                    OR IFNULL(u.display_name, '') LIKE $searchLike
                    OR IFNULL(a.reason, '') LIKE $searchLike))
            ORDER BY a.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;

        AddAuditFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", Math.Max(1, filter.Page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Page.Offset));

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

    private static void AddAuditFilterParameters(
        SqliteCommand command,
        AuditLogSearchFilter filter)
    {
        string? search = filter.Search.Normalized;

        if (string.IsNullOrWhiteSpace(search))
        {
            search = null;
        }

        command.Parameters.AddWithValue("$from", filter.From?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$to", filter.To?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$search", search ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$searchLike", search is null ? DBNull.Value : $"%{search}%");
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
        ErrorLogSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountErrorLogs(connection, filter);
            IReadOnlyList<ErrorLogReadModel> items = ReadErrorLogs(
                connection,
                filter,
                cancellationToken);

            PagedResult<ErrorLogReadModel> result = new(
                items,
                totalCount,
                filter.Page.PageNumber,
                filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<ErrorLogReadModel>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<PagedResult<ErrorLogReadModel>>(
                "ErrorLogs.SearchFailed",
                ex));
        }
    }

    private static int CountErrorLogs(
        SqliteConnection connection,
        ErrorLogSearchFilter filter)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM error_logs e
            LEFT JOIN users u ON u.id = e.user_id
            WHERE ($from IS NULL OR e.created_at >= $from)
              AND ($to IS NULL OR e.created_at <= $to)
              AND ($search IS NULL OR (
                    e.area LIKE $searchLike
                    OR e.message LIKE $searchLike
                    OR IFNULL(e.technical_detail, '') LIKE $searchLike
                    OR IFNULL(u.display_name, '') LIKE $searchLike));
            """;

        AddErrorFilterParameters(command, filter);

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }

    private static IReadOnlyList<ErrorLogReadModel> ReadErrorLogs(
        SqliteConnection connection,
        ErrorLogSearchFilter filter,
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
            WHERE ($from IS NULL OR e.created_at >= $from)
              AND ($to IS NULL OR e.created_at <= $to)
              AND ($search IS NULL OR (
                    e.area LIKE $searchLike
                    OR e.message LIKE $searchLike
                    OR IFNULL(e.technical_detail, '') LIKE $searchLike
                    OR IFNULL(u.display_name, '') LIKE $searchLike))
            ORDER BY e.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;

        AddErrorFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", Math.Max(1, filter.Page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Page.Offset));

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

    private static void AddErrorFilterParameters(
        SqliteCommand command,
        ErrorLogSearchFilter filter)
    {
        string? search = filter.Search.Normalized;

        if (string.IsNullOrWhiteSpace(search))
        {
            search = null;
        }

        command.Parameters.AddWithValue("$from", filter.From?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$to", filter.To?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$search", search ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$searchLike", search is null ? DBNull.Value : $"%{search}%");
    }
}

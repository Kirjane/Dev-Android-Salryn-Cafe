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

    public const int DefaultExportMaxRows = 25000;

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

    public Task<Result<IReadOnlyList<AuditLogExportRow>>> ExportAuditLogsAsync(
        AuditLogExportFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            int maxRows = Math.Clamp(filter.MaxRows, 1, DefaultExportMaxRows);
            AuditLogSearchFilter searchFilter = new(
                new PageRequest(1, maxRows),
                filter.From,
                filter.To,
                filter.Search);

            IReadOnlyList<AuditLogExportRow> items = ReadAuditExportRows(
                connection,
                searchFilter,
                maxRows,
                cancellationToken);

            return Task.FromResult(Result<IReadOnlyList<AuditLogExportRow>>.Success(items));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<IReadOnlyList<AuditLogExportRow>>(
                "Audit.ExportFailed",
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
            ORDER BY a.created_at DESC, a.id DESC
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

    private static IReadOnlyList<AuditLogExportRow> ReadAuditExportRows(
        SqliteConnection connection,
        AuditLogSearchFilter filter,
        int maxRows,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                a.user_id,
                u.display_name AS user_display_name,
                a.action,
                a.entity_type,
                a.entity_id,
                a.reason,
                a.new_value_json,
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
            ORDER BY a.created_at DESC, a.id DESC
            LIMIT $limit;
            """;

        AddAuditFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", maxRows);

        using var reader = command.ExecuteReader();
        List<AuditLogExportRow> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            items.Add(new AuditLogExportRow(
                reader.GetDateTimeOffsetValue("created_at"),
                reader.GetNullableString("user_display_name"),
                reader.GetStringValue("action"),
                reader.GetStringValue("entity_type"),
                reader.GetNullableString("entity_id"),
                reader.GetNullableString("reason"),
                SanitizeAuditDetails(reader.GetNullableString("new_value_json"))));
        }

        return items;
    }

    /// <summary>
    /// Strips or blanks details that look like secrets / unsafe payloads before CSV export.
    /// Public for focused export gates and formatter reuse.
    /// </summary>
    public static string? SanitizeAuditDetails(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string value = raw.Trim();
        if (value.Length > 2000)
        {
            value = value[..2000];
        }

        // Never export secret-like payloads in CSV details.
        if (value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains("private key", StringComparison.OrdinalIgnoreCase)
            || value.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || value.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            || value.Contains("LicenseJson", StringComparison.OrdinalIgnoreCase)
            || value.Contains("licenseJson", StringComparison.OrdinalIgnoreCase)
            || value.Contains("signedLicense", StringComparison.OrdinalIgnoreCase)
            || value.Contains("signature", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("stack", StringComparison.OrdinalIgnoreCase)
            || value.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || value.Contains("backupContents", StringComparison.OrdinalIgnoreCase)
            || value.Contains("backupBytes", StringComparison.OrdinalIgnoreCase)
            || value.Contains("backup_blob", StringComparison.OrdinalIgnoreCase)
            || value.Contains(":line ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
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

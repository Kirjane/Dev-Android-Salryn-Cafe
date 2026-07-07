using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteErrorLogWriteRepository :
    SqliteRepositoryBase,
    IErrorLogWriteRepository
{
    public SqliteErrorLogWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> AddAsync(
        ErrorLog errorLog,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (errorLog is null)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "ErrorLog.Required",
                "Error log entry is required.")));
        }

        if (string.IsNullOrWhiteSpace(errorLog.Area))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "ErrorLog.AreaRequired",
                "Error log area is required.")));
        }

        if (string.IsNullOrWhiteSpace(errorLog.Message))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "ErrorLog.MessageRequired",
                "Error log message is required.")));
        }

        if (ContainsSecretMarker(errorLog.Message) ||
            ContainsSecretMarker(errorLog.TechnicalDetail))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "ErrorLog.SecretMarkerRejected",
                "Error log entries must not include secrets or credentials.")));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO error_logs
                    (id, user_id, area, message, technical_detail, created_at)
                VALUES
                    ($id, $userId, $area, $message, $technicalDetail, $createdAt);
                """;

            command.Parameters.AddWithValue("$id", errorLog.Id.ToString("D"));
            command.Parameters.AddWithValue(
                "$userId",
                errorLog.UserId.HasValue ? errorLog.UserId.Value.ToString("D") : DBNull.Value);
            command.Parameters.AddWithValue("$area", errorLog.Area.Trim());
            command.Parameters.AddWithValue("$message", TrimToMax(errorLog.Message, 512));
            command.Parameters.AddWithValue(
                "$technicalDetail",
                string.IsNullOrWhiteSpace(errorLog.TechnicalDetail)
                    ? DBNull.Value
                    : TrimToMax(errorLog.TechnicalDetail, 1024));
            command.Parameters.AddWithValue("$createdAt", errorLog.CreatedAt.ToString("O"));

            command.ExecuteNonQuery();

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected(
                "ErrorLog.WriteFailed",
                ex));
        }
    }

    private static bool ContainsSecretMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || value.Contains("salryn.sid", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimToMax(string value, int maxLength)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
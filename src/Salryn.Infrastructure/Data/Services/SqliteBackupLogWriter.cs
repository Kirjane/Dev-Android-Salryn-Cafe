using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Services;

internal static class SqliteBackupLogWriter
{
    public static void TryWrite(
        SqliteConnectionFactory connectionFactory,
        BackupLog backupLog)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(backupLog);

        try
        {
            Guid operatorUserId = ResolveOperatorUserId(connectionFactory);

            if (operatorUserId == Guid.Empty)
            {
                return;
            }

            using SqliteConnection connection = connectionFactory.CreateConnection();
            connection.Open();
            SqlitePragma.Apply(connection);

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO backup_logs
                    (id, file_path, status, file_size_bytes, created_by_user_id, created_at, error_message)
                VALUES
                    ($id, $filePath, $status, $fileSizeBytes, $createdByUserId, $createdAt, $errorMessage);
                """;

            command.Parameters.AddWithValue("$id", backupLog.Id.ToString("D"));
            command.Parameters.AddWithValue("$filePath", backupLog.FilePath.Trim());
            command.Parameters.AddWithValue("$status", (int)backupLog.Status);
            command.Parameters.AddWithValue(
                "$fileSizeBytes",
                backupLog.FileSizeBytes.HasValue ? backupLog.FileSizeBytes.Value : DBNull.Value);
            command.Parameters.AddWithValue("$createdByUserId", operatorUserId.ToString("D"));
            command.Parameters.AddWithValue("$createdAt", backupLog.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue(
                "$errorMessage",
                string.IsNullOrWhiteSpace(backupLog.ErrorMessage) ? DBNull.Value : backupLog.ErrorMessage.Trim());

            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static Guid ResolveOperatorUserId(SqliteConnectionFactory connectionFactory)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();
        SqlitePragma.Apply(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM users
            ORDER BY created_at
            LIMIT 1;
            """;

        object? scalar = command.ExecuteScalar();

        return scalar is string userIdText && Guid.TryParse(userIdText, out Guid userId)
            ? userId
            : Guid.Empty;
    }
}
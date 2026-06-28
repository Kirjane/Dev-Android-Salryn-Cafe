using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteBackupRecoveryService : IBackupRecoveryService
{
    public const string RestoreConfirmationText = "RESTORE SALRYN BACKUP";

    private static readonly string[] CoreTables =
    {
        "roles",
        "users",
        "store_settings",
        "products",
        "sales"
    };

    private readonly DbPaths _dbPaths;

    public SqliteBackupRecoveryService(DbPaths dbPaths)
    {
        _dbPaths = dbPaths ?? throw new ArgumentNullException(nameof(dbPaths));
    }

    public Result<IReadOnlyList<BackupFileSummary>> ListBackups()
    {
        try
        {
            Directory.CreateDirectory(_dbPaths.BackupPath);

            IReadOnlyList<BackupFileSummary> backups = Directory
                .EnumerateFiles(_dbPaths.BackupPath, "*.db", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new BackupFileSummary(
                    file.FullName,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime()))
                .ToArray();

            return Result<IReadOnlyList<BackupFileSummary>>.Success(backups);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<BackupFileSummary>>.Fail(Error.Failure(
                "Backup.ListFailed",
                "Backup files could not be listed: " + ex.Message));
        }
    }

    public Result<BackupVerificationSummary> VerifyBackup(string backupFilePath)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath))
        {
            return Result<BackupVerificationSummary>.Fail(Error.Validation(
                "Backup.FileRequired",
                "Select a backup file before verification."));
        }

        try
        {
            FileInfo backupFile = new(backupFilePath);

            if (!backupFile.Exists)
            {
                return Result<BackupVerificationSummary>.Fail(Error.NotFound(
                    "Backup.FileNotFound",
                    "Selected backup file was not found."));
            }

            using SqliteConnection connection = CreateReadOnlyConnection(backupFile.FullName);
            connection.Open();

            HashSet<string> tableNames = ReadTableNames(connection);

            bool coreTablesExist = CoreTables.All(tableNames.Contains);
            bool appMetadataExists = tableNames.Contains("app_metadata");
            bool migrationMetadataExists = tableNames.Contains("schema_migrations");

            BackupVerificationSummary summary = new(
                backupFile.FullName,
                backupFile.Length,
                new DateTimeOffset(backupFile.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
                coreTablesExist,
                appMetadataExists,
                migrationMetadataExists,
                TryCountRows(connection, tableNames, "users"),
                TryCountRows(connection, tableNames, "products"),
                TryCountRows(connection, tableNames, "sales"));

            return Result<BackupVerificationSummary>.Success(summary);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Result<BackupVerificationSummary>.Fail(Error.Failure(
                "Backup.VerifyFailed",
                "Backup verification failed: " + ex.Message));
        }
    }

    public Result<BackupRestoreResult> RestoreVerifiedBackup(
        BackupVerificationSummary verifiedBackup,
        string confirmationText)
    {
        if (verifiedBackup is null)
        {
            return Result<BackupRestoreResult>.Fail(Error.Validation(
                "Backup.VerifiedBackupRequired",
                "Verify a backup before restore."));
        }

        if (!verifiedBackup.IsRestorable)
        {
            return Result<BackupRestoreResult>.Fail(Error.Validation(
                "Backup.NotRestorable",
                "Selected backup did not pass minimum SALRYN database shape verification."));
        }

        if (!string.Equals(confirmationText?.Trim(), RestoreConfirmationText, StringComparison.Ordinal))
        {
            return Result<BackupRestoreResult>.Fail(Error.Validation(
                "Backup.RestoreConfirmationRequired",
                "Type RESTORE SALRYN BACKUP to confirm restore."));
        }

        Result<BackupVerificationSummary> freshVerification = VerifyBackup(verifiedBackup.FilePath);

        if (freshVerification.IsFailure)
        {
            return Result<BackupRestoreResult>.Fail(freshVerification.ErrorDetail);
        }

        if (!freshVerification.Value.IsRestorable)
        {
            return Result<BackupRestoreResult>.Fail(Error.Validation(
                "Backup.NotRestorable",
                "Selected backup no longer passes minimum SALRYN database shape verification."));
        }

        try
        {
            string liveDatabasePath = _dbPaths.DatabasePath;

            if (!File.Exists(liveDatabasePath))
            {
                return Result<BackupRestoreResult>.Fail(Error.NotFound(
                    "Backup.LiveDatabaseNotFound",
                    "Live database file was not found."));
            }

            Directory.CreateDirectory(_dbPaths.BackupPath);
            string safetyBackupPath = CreateSafetyBackupPath();

            SqliteConnectionFactory.ClearAllPools();
            File.Copy(liveDatabasePath, safetyBackupPath, overwrite: false);

            if (!CanOpenLiveDatabaseExclusively(liveDatabasePath))
            {
                return Result<BackupRestoreResult>.Fail(Error.Failure(
                    "Backup.RestoreRequiresRestart",
                    "Safety backup was created, but the live database could not be opened exclusively. " +
                    "Restore requires app restart or a manual operator step. Safety backup: " + safetyBackupPath));
            }

            File.Copy(freshVerification.Value.FilePath, liveDatabasePath, overwrite: true);

            return Result<BackupRestoreResult>.Success(new BackupRestoreResult(
                freshVerification.Value.FilePath,
                safetyBackupPath,
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result<BackupRestoreResult>.Fail(Error.Failure(
                "Backup.RestoreFailed",
                "Restore failed before replacing the live database safely: " + ex.Message));
        }
    }

    private static SqliteConnection CreateReadOnlyConnection(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        };

        return new SqliteConnection(builder.ToString());
    }

    private static HashSet<string> ReadTableNames(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table';
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        HashSet<string> tableNames = new(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    private static long? TryCountRows(
        SqliteConnection connection,
        HashSet<string> tableNames,
        string tableName)
    {
        if (!tableNames.Contains(tableName))
        {
            return null;
        }

        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(1) FROM {tableName};";
            return (long)command.ExecuteScalar()!;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private string CreateSafetyBackupPath()
    {
        string fileName = "salryn-pre-restore-" +
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") +
            ".db";

        return Path.Combine(_dbPaths.BackupPath, fileName);
    }

    private static bool CanOpenLiveDatabaseExclusively(string liveDatabasePath)
    {
        try
        {
            using FileStream stream = File.Open(
                liveDatabasePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

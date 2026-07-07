using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteBackupRecoveryService : IBackupRecoveryService
{
    public const string RestoreConfirmationText = "RESTORE SALRYN BACKUP";

    private readonly DbPaths _dbPaths;
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteBackupRecoveryService(DbPaths dbPaths)
    {
        _dbPaths = dbPaths ?? throw new ArgumentNullException(nameof(dbPaths));
        _connectionFactory = new SqliteConnectionFactory(dbPaths);
    }

    public Result<string> CreateBackup()
    {
        try
        {
            string sourcePath = _dbPaths.DatabasePath;
            string backupFolder = _dbPaths.BackupPath;

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return Result<string>.Fail(Error.Failure(
                    "Backup.DatabaseNotFound",
                    "Live database file was not found."));
            }

            Directory.CreateDirectory(backupFolder);

            string fileName = "salryn-backup-" +
                DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") +
                ".db";
            string destinationPath = Path.Combine(backupFolder, fileName);

            if (File.Exists(destinationPath))
            {
                return Result<string>.Fail(Error.Conflict(
                    "Backup.DestinationExists",
                    "Backup destination already exists. Wait a moment and try again."));
            }

            SqliteDatabaseBackupUtility.CreateOnlineBackup(sourcePath, destinationPath);

            FileInfo backupFile = new(destinationPath);
            SqliteBackupLogWriter.TryWrite(
                _connectionFactory,
                new BackupLog
                {
                    FilePath = backupFile.FullName,
                    Status = JobStatus.Completed,
                    FileSizeBytes = backupFile.Length,
                    CreatedAt = DateTimeOffset.UtcNow,
                });

            return Result<string>.Success(backupFile.FullName);
        }
        catch (UnauthorizedAccessException ex)
        {
            WriteFailedBackupLog(ex.Message);
            return Result<string>.Fail(Error.Failure(
                "Backup.AccessDenied",
                "Backup access failed: " + ex.Message));
        }
        catch (IOException ex)
        {
            WriteFailedBackupLog(ex.Message);
            return Result<string>.Fail(Error.Failure(
                "Backup.CopyFailed",
                "Backup failed: " + ex.Message));
        }
        catch (SqliteException ex)
        {
            WriteFailedBackupLog(ex.Message);
            return Result<string>.Fail(Error.Failure(
                "Backup.SqliteFailed",
                "Backup failed: " + ex.Message));
        }
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
            int? backupMigrationVersion = ReadMaxMigrationVersion(connection);
            int supportedMigrationVersion = SalrynDatabaseMigrationCatalog.SupportedMigrationVersion;
            int effectiveMigrationVersion = backupMigrationVersion ?? 0;

            bool legacyCoreTablesExist = SalrynDatabaseMigrationCatalog.LegacyCoreTablesExist(tableNames);
            IReadOnlyList<string> missingCriticalTables =
                SalrynDatabaseMigrationCatalog.GetMissingCriticalTables(effectiveMigrationVersion, tableNames);
            bool criticalTablesExist = effectiveMigrationVersion > 0 && missingCriticalTables.Count == 0;
            bool migrationVersionCompatible = effectiveMigrationVersion > 0
                && effectiveMigrationVersion <= supportedMigrationVersion;

            BackupVerificationSummary summary = new(
                backupFile.FullName,
                backupFile.Length,
                new DateTimeOffset(backupFile.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
                legacyCoreTablesExist,
                tableNames.Contains("app_metadata"),
                tableNames.Contains("schema_migrations"),
                TryCountRows(connection, tableNames, "users"),
                TryCountRows(connection, tableNames, "products"),
                TryCountRows(connection, tableNames, "sales"),
                backupMigrationVersion,
                supportedMigrationVersion,
                criticalTablesExist,
                migrationVersionCompatible,
                missingCriticalTables);

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
            string message = verifiedBackup.HasFutureMigrationVersion
                ? "Selected backup was created by a newer app version and cannot be restored safely."
                : "Selected backup did not pass SALRYN database shape and migration verification.";

            return Result<BackupRestoreResult>.Fail(Error.Validation(
                "Backup.NotRestorable",
                message));
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
                "Selected backup no longer passes SALRYN database shape and migration verification."));
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
            SqliteDatabaseBackupUtility.CreateOnlineBackup(liveDatabasePath, safetyBackupPath);

            if (!CanOpenLiveDatabaseExclusively(liveDatabasePath))
            {
                SqliteBackupLogWriter.TryWrite(
                    _connectionFactory,
                    new BackupLog
                    {
                        FilePath = verifiedBackup.FilePath,
                        Status = JobStatus.Failed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ErrorMessage = "Restore blocked because live database could not be opened exclusively.",
                    });

                return Result<BackupRestoreResult>.Fail(Error.Failure(
                    "Backup.RestoreRequiresRestart",
                    "Safety backup was created, but the live database could not be opened exclusively. " +
                    "Restore requires app restart or a manual operator step. Safety backup: " + safetyBackupPath));
            }

            SqliteDatabaseBackupUtility.RemoveWalShmSidecars(liveDatabasePath);
            File.Copy(freshVerification.Value.FilePath, liveDatabasePath, overwrite: true);
            SqliteDatabaseBackupUtility.RemoveWalShmSidecars(liveDatabasePath);

            SqliteBackupLogWriter.TryWrite(
                _connectionFactory,
                new BackupLog
                {
                    FilePath = freshVerification.Value.FilePath,
                    Status = JobStatus.Completed,
                    FileSizeBytes = freshVerification.Value.SizeBytes,
                    CreatedAt = DateTimeOffset.UtcNow,
                });

            return Result<BackupRestoreResult>.Success(new BackupRestoreResult(
                freshVerification.Value.FilePath,
                safetyBackupPath,
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
        {
            SqliteBackupLogWriter.TryWrite(
                _connectionFactory,
                new BackupLog
                {
                    FilePath = verifiedBackup.FilePath,
                    Status = JobStatus.Failed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ErrorMessage = ex.Message,
                });

            return Result<BackupRestoreResult>.Fail(Error.Failure(
                "Backup.RestoreFailed",
                "Restore failed before replacing the live database safely: " + ex.Message));
        }
    }

    private void WriteFailedBackupLog(string message)
    {
        SqliteBackupLogWriter.TryWrite(
            _connectionFactory,
            new BackupLog
            {
                FilePath = _dbPaths.BackupPath,
                Status = JobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow,
                ErrorMessage = message,
            });
    }

    private static SqliteConnection CreateReadOnlyConnection(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
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

    private static int? ReadMaxMigrationVersion(SqliteConnection connection)
    {
        using SqliteCommand existsCommand = connection.CreateCommand();
        existsCommand.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = 'schema_migrations'
            LIMIT 1;
            """;

        if (existsCommand.ExecuteScalar() is null)
        {
            return null;
        }

        using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = """
            SELECT MAX(version)
            FROM schema_migrations;
            """;

        object? scalar = versionCommand.ExecuteScalar();

        if (scalar is null or DBNull)
        {
            return null;
        }

        return Convert.ToInt32(scalar);
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
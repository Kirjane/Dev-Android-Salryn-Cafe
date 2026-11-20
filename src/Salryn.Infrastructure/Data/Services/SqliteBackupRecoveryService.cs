using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Initialization;

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

            var sourceVerification = VerifyBackup(sourcePath);
            if (sourceVerification.IsFailure || !sourceVerification.Value.IsRestorable)
            {
                return Result<string>.Fail(Error.Validation(
                    "Backup.SourceRejected", "Source database must pass Cafe lineage and schema verification before backup."));
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

            SchemaLineageClassification lineage = SchemaLineageClassifier.Classify(connection);
            if (lineage.Kind is not (SchemaLineageKind.SalrynCafe or SchemaLineageKind.YzaV39Legacy))
            {
                return Result<BackupVerificationSummary>.Fail(Error.Validation(
                    "Backup.LineageRejected", "Only verified Salryn Cafe or legacy Cafe Yza backups may be restored."));
            }

            using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
                {
                    return Result<BackupVerificationSummary>.Fail(Error.Validation(
                        "Backup.IntegrityFailed", "Backup SQLite integrity verification failed."));
                }
            }

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

            // Never let a writable open recover or replace a wrong-product destination.
            var liveVerification = VerifyBackup(liveDatabasePath);
            if (liveVerification.IsFailure || !liveVerification.Value.IsRestorable)
            {
                return Result<BackupRestoreResult>.Fail(Error.Validation(
                    "Backup.DestinationRejected", "Live destination must pass Cafe lineage and integrity verification."));
            }

            // Keep source evidence immutable. Upgrade a separate disposable restore
            // candidate before touching the live destination or creating its safety copy.
            var stagePaths = DbPaths.CreateDevelopment(Path.Combine(_dbPaths.BackupPath,
                "restore-stage-" + Guid.NewGuid().ToString("N")));
            stagePaths.EnsureDirectories();
            // Snapshot through SQLite so committed WAL rows are included without
            // checkpointing or modifying the retained source evidence.
            using (var source = CreateReadOnlyConnection(freshVerification.Value.FilePath))
            using (var stage = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = stagePaths.DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString()))
            {
                source.Open();
                stage.Open();
                source.BackupDatabase(stage);
            }
            var stagedInitialization = DefaultDatabaseInitializationFactory.Create(stagePaths).InitializeDatabase();
            if (stagedInitialization.IsFailure)
            {
                return Result<BackupRestoreResult>.Fail(stagedInitialization.ErrorDetail);
            }
            SqliteConnectionFactory.ClearAllPools();
            CheckpointLiveDatabase(stagePaths.DatabasePath);
            SqliteConnectionFactory.ClearAllPools();
            var stagedVerification = VerifyBackup(stagePaths.DatabasePath);
            if (stagedVerification.IsFailure || !stagedVerification.Value.IsRestorable
                || stagedVerification.Value.BackupMigrationVersion != CafeMigrationCatalog.CurrentCafeSchemaVersion)
            {
                return Result<BackupRestoreResult>.Fail(Error.Validation(
                    "Backup.StageRejected", "Migrated restore candidate did not pass Cafe schema verification."));
            }

            Directory.CreateDirectory(_dbPaths.BackupPath);
            string safetyBackupPath = CreateSafetyBackupPath();

            SqliteConnectionFactory.ClearAllPools();
            SqliteDatabaseBackupUtility.CreateOnlineBackup(liveDatabasePath, safetyBackupPath);

            // After online backup, release pooled handles and checkpoint WAL so exclusive
            // replacement can proceed in-process on isolated hosts (QA1 / single-process restore).
            SqliteConnectionFactory.ClearAllPools();
            CheckpointLiveDatabase(liveDatabasePath);
            SqliteConnectionFactory.ClearAllPools();

            // Keep denial of new readers/writers through the atomic replacement.
            // Delete sharing permits the OS rename primitive, not SQLite access.
            using (var exclusion = File.Open(liveDatabasePath, FileMode.Open,
                FileAccess.ReadWrite, FileShare.Delete))
            {
                string walPath = liveDatabasePath + "-wal";
                if (File.Exists(walPath) && new FileInfo(walPath).Length > 0)
                {
                    return Result<BackupRestoreResult>.Fail(Error.Failure(
                        "Backup.RestoreRequiresRestart", "Live WAL is not empty; close all writers before restore."));
                }
                SqliteDatabaseBackupUtility.RemoveWalShmSidecars(liveDatabasePath);
                string replacedOriginal = safetyBackupPath + ".replaced-original";
                File.Replace(stagePaths.DatabasePath, liveDatabasePath, replacedOriginal);
            }

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

    private static void CheckpointLiveDatabase(string liveDatabasePath)
    {
        try
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = liveDatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private,
            };

            using SqliteConnection connection = new(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Best-effort; exclusive open still decides restore safety.
        }
        catch (IOException)
        {
        }
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

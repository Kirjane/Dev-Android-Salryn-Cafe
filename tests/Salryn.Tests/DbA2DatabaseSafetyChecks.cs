using Microsoft.Data.Sqlite;
using Salryn.Api.Local.RemotePos;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class DbA2DatabaseSafetyChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        RunSourceGuards(projectRoot);
        RunBackupUtilityChecks(projectRoot);
        RunNumberingBoundaryChecks();
        RunIdempotencyDocumentationChecks(projectRoot);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string recoveryService = ReadRequiredFile(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteBackupRecoveryService.cs");
        string backupUtility = ReadRequiredFile(projectRoot, "src", "Salryn.Infrastructure", "Data", "SqliteDatabaseBackupUtility.cs");
        string migrationCatalog = ReadRequiredFile(projectRoot, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs");
        string backupLogWriter = ReadRequiredFile(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteBackupLogWriter.cs");
        string errorLogWriteRepo = ReadRequiredFile(projectRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteErrorLogWriteRepository.cs");
        string repositoryFactory = ReadRequiredFile(projectRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "SqliteRepositoryFactory.cs");
        string appServices = ReadRequiredFile(projectRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string remoteAllocator = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosReceiptNumberAllocator.cs");
        string remoteCheckout = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(backupUtility, "BackupDatabase", "DB-A2 backup utility must use SQLite online backup API.");
        RequireContains(backupUtility, "RemoveWalShmSidecars", "DB-A2 backup utility must manage WAL/SHM sidecars.");
        RequireContains(recoveryService, "CreateBackup()", "DB-A2 recovery service must expose backup creation.");
        RequireContains(recoveryService, "SqliteDatabaseBackupUtility.CreateOnlineBackup", "DB-A2 recovery must use WAL-safe online backup.");
        RequireContains(recoveryService, "SqliteDatabaseBackupUtility.RemoveWalShmSidecars", "DB-A2 restore must clear stale WAL/SHM sidecars.");
        RequireContains(recoveryService, "ReadMaxMigrationVersion", "DB-A2 verification must read schema_migrations version.");
        RequireContains(recoveryService, "HasFutureMigrationVersion", "DB-A2 restore must reject future migration versions.");
        RequireContains(recoveryService, "SqliteBackupLogWriter.TryWrite", "DB-A2 must write backup_logs rows.");
        RequireContains(migrationCatalog, "bir_official_document_sequences", "DB-A2 verification catalog must include BIR tables.");
        RequireContains(migrationCatalog, "salryn_terminal_registrations", "DB-A2 verification catalog must include LAN tables.");
        RequireContains(migrationCatalog, "backup_logs", "DB-A2 verification catalog must include backup_logs.");
        RequireContains(backupLogWriter, "INSERT INTO backup_logs", "DB-A2 backup log writer must persist backup_logs.");
        RequireContains(errorLogWriteRepo, "IErrorLogWriteRepository", "DB-A2 must implement error log write repository.");
        RequireContains(errorLogWriteRepo, "INSERT INTO error_logs", "DB-A2 error log repository must insert into error_logs.");
        RequireContains(repositoryFactory, "CreateErrorLogWriteRepository", "DB-A2 factory must expose error log write repository.");
        RequireContains(appServices, "CreateBackupRecoveryService(this).CreateBackup()", "DB-A2 app backup must route through recovery service.");
        RequireNotContains(appServices, "File.Copy(sourcePath, destinationPath", "DB-A2 app backup must not use raw File.Copy.");
        RequireNotContains(remoteAllocator, "bir_official_document_sequences", "Remote POS allocator must not touch official BIR sequences.");
        RequireNotContains(remoteCheckout, "bir_official_document_sequences", "Remote POS checkout must not touch official BIR sequences.");
        RequireContains(coreSmoke, "DbA2DatabaseSafetyChecks.Run();", "CoreSmoke must wire DB-A2 checks.");
    }

    private static void RunBackupUtilityChecks(string projectRoot)
    {
        string tempRoot = Path.Combine(projectRoot, ".salryn-db-a2-check");
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }

        Directory.CreateDirectory(tempRoot);

        DbPaths dbPaths = DbPaths.CreateDevelopment(tempRoot);
        string sourcePath = dbPaths.DatabasePath;
        string backupPath = Path.Combine(dbPaths.BackupPath, "db-a2-check-backup.db");

        try
        {
            CreateInitializedDatabase(dbPaths);
            SqliteBackupRecoveryService recovery = new(dbPaths);

            Result<string> backupResult = recovery.CreateBackup();
            RequireTrue(backupResult.IsSuccess, "DB-A2 backup creation must succeed for initialized database.");
            backupPath = backupResult.Value;
            RequireTrue(File.Exists(backupPath), "DB-A2 online backup must create backup file.");
            RequireFalse(File.Exists(backupPath + "-wal"), "DB-A2 online backup should not leave backup WAL sidecar.");

            Result<BackupVerificationSummary> verification = recovery.VerifyBackup(backupPath);
            RequireTrue(verification.IsSuccess, "DB-A2 backup verification must succeed for initialized database.");
            RequireTrue(verification.Value.CriticalTablesExist, "DB-A2 backup verification must require critical tables.");
            RequireTrue(verification.Value.MigrationVersionCompatible, "DB-A2 backup verification must accept supported migration version.");
            RequireTrue(verification.Value.IsRestorable, "DB-A2 initialized backup must be restorable.");

            string futureBackupPath = Path.Combine(dbPaths.BackupPath, "db-a2-check-future.db");
            File.Copy(backupPath, futureBackupPath, overwrite: true);
            // Must exceed SalrynDatabaseMigrationCatalog.SupportedMigrationVersion (currently 16).
            const int futureVersion = 18;
            using (SqliteConnection connection = OpenReadWrite(futureBackupPath))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO schema_migrations (version, name, applied_at)
                    VALUES ($version, 'future-test', $appliedAt);
                    """;
                command.Parameters.AddWithValue("$version", futureVersion);
                command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            Result<BackupVerificationSummary> futureVerification = recovery.VerifyBackup(futureBackupPath);
            RequireTrue(futureVerification.IsSuccess, "DB-A2 must verify future-version backups without throwing.");
            RequireTrue(futureVerification.Value.HasFutureMigrationVersion, "DB-A2 must detect future migration versions.");
            RequireFalse(futureVerification.Value.IsRestorable, "DB-A2 must reject future migration version restores.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void RunNumberingBoundaryChecks()
    {
        RemotePosReceiptNumberAllocator allocator = new();
        string receipt = allocator.AllocateNext();
        RequireTrue(receipt.StartsWith("RPOS-", StringComparison.Ordinal), "Remote POS receipt numbers must use RPOS prefix.");
        RequireFalse(receipt.Contains("bir_official", StringComparison.OrdinalIgnoreCase), "Remote POS receipts must stay non-official.");
    }

    private static void RunIdempotencyDocumentationChecks(string projectRoot)
    {
        string remoteCheckout = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs");
        RequireContains(remoteCheckout, "_idempotency", "Remote POS idempotency must remain in-memory.");
        RequireContains(remoteCheckout, "sales.receipt_no UNIQUE", "Remote POS must document sales.receipt_no UNIQUE backstop.");
        RequireContains(remoteCheckout, "memoize the committed success IMMEDIATELY", "Remote POS must memoize committed checkout before side effects.");
    }

    private static void CreateInitializedDatabase(DbPaths dbPaths)
    {
        if (File.Exists(dbPaths.DatabasePath))
        {
            File.Delete(dbPaths.DatabasePath);
        }

        dbPaths.EnsureDirectories();

        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.Create(dbPaths);

        Result<DatabaseInitializationSummary> initResult = orchestrator.InitializeDatabase();
        RequireTrue(initResult.IsSuccess, "DB-A2 test database initialization must succeed.");
    }

    private static SqliteConnection OpenReadWrite(string databasePath)
    {
        SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        SqlitePragma.Apply(connection);
        return connection;
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;
        foreach (string part in pathParts)
        {
            path = Path.Combine(path, part);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
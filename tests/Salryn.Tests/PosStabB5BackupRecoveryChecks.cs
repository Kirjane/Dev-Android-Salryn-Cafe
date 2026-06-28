namespace Salryn.Tests;

public static class PosStabB5BackupRecoveryChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string backupFormText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Backup",
            "BackupForm.cs");
        string appBridgeText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "BackupRecoveryAppServices.cs");
        string recoveryServiceText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteBackupRecoveryService.cs");
        string initialSchemaText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Migrations",
            "InitialSchemaMigration.cs");
        string defaultInitializationText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Initialization",
            "DefaultDatabaseInitializationFactory.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(backupFormText, "_backupsGrid", "B5 Backup UI must list local backup files.");
        RequireContains(backupFormText, "RefreshBackups", "B5 Backup UI must expose backup list refresh.");
        RequireContains(backupFormText, "VerifySelectedBackup", "B5 Backup UI must verify selected backup files.");
        RequireContains(backupFormText, "RestoreVerifiedBackup", "B5 Backup UI must route restore through guarded boundary.");
        RequireContains(backupFormText, "_verifiedBackup", "B5 Backup UI must track verified backup state before restore.");
        RequireContains(backupFormText, "RESTORE SALRYN BACKUP", "B5 Backup UI must require typed restore confirmation.");
        RequireContains(backupFormText, "_backupRecovery.ListBackups()", "B5 Backup UI must list backups through recovery boundary.");
        RequireContains(backupFormText, "_backupRecovery.VerifyBackup", "B5 Backup UI must verify backups through recovery boundary.");
        RequireContains(backupFormText, "_backupRecovery.RestoreVerifiedBackup", "B5 Backup UI must restore through recovery boundary.");

        RequireNoDirectSql(backupFormText, "Backup form");
        RequireNoForbiddenMarkers(backupFormText, "Backup form");

        RequireContains(appBridgeText, "CreateBackupRecoveryService", "B5 AppServices bridge must expose backup recovery service creation.");
        RequireContains(appBridgeText, "SqliteBackupRecoveryService", "B5 AppServices bridge must keep recovery implementation outside UI controls.");

        RequireContains(recoveryServiceText, "IBackupRecoveryService", "B5 Infrastructure must define backup recovery boundary.");
        RequireContains(recoveryServiceText, "ListBackups", "B5 recovery boundary must list backup files.");
        RequireContains(recoveryServiceText, "VerifyBackup", "B5 recovery boundary must verify selected backups.");
        RequireContains(recoveryServiceText, "SqliteOpenMode.ReadOnly", "B5 backup verification must open backup databases read-only.");
        RequireContains(recoveryServiceText, "CoreTables", "B5 recovery verification must check minimum SALRYN DB shape.");
        RequireContains(recoveryServiceText, "app_metadata", "B5 recovery summary must report app metadata availability.");
        RequireContains(recoveryServiceText, "schema_migrations", "B5 recovery summary must report migration metadata availability.");
        RequireContains(recoveryServiceText, "TryCountRows(connection, tableNames, \"users\")", "B5 recovery summary must safely read user count.");
        RequireContains(recoveryServiceText, "TryCountRows(connection, tableNames, \"products\")", "B5 recovery summary must safely read product count.");
        RequireContains(recoveryServiceText, "TryCountRows(connection, tableNames, \"sales\")", "B5 recovery summary must safely read sale count.");
        RequireContains(recoveryServiceText, "RestoreConfirmationText", "B5 restore must require typed confirmation.");
        RequireContains(recoveryServiceText, "VerifiedBackupRequired", "B5 restore must require verified backup state.");
        RequireContains(recoveryServiceText, "CreateSafetyBackupPath", "B5 restore must create a current DB safety backup first.");
        RequireContains(recoveryServiceText, "File.Copy(liveDatabasePath, safetyBackupPath, overwrite: false)", "B5 restore must create a non-overwriting safety backup before replacement.");
        RequireContains(recoveryServiceText, "CanOpenLiveDatabaseExclusively", "B5 restore must refuse unsafe live DB replacement.");
        RequireContains(recoveryServiceText, "RestoreRequiresRestart", "B5 restore must report app restart/manual step when live DB cannot be safely replaced.");
        RequireContains(recoveryServiceText, "File.Copy(freshVerification.Value.FilePath, liveDatabasePath, overwrite: true)", "B5 live DB overwrite must only occur inside guarded restore boundary.");
        RequireDoesNotContain(recoveryServiceText, "File.Delete", "B5 recovery must not destructively delete database files.");
        RequireDoesNotContain(recoveryServiceText, "DROP TABLE", "B5 recovery must not change schema.");
        RequireDoesNotContain(recoveryServiceText, "CREATE TABLE", "B5 recovery service must not add schema migration behavior.");
        RequireNoForbiddenMarkers(recoveryServiceText, "Backup recovery service");

        RequireContains(initialSchemaText, "CREATE TABLE backup_logs", "B5 guard should read the existing initial schema only.");
        RequireDoesNotContain(defaultInitializationText, "BackupRecovery", "B5 must not add backup recovery as a migration.");
        RequireContains(coreSmokeText, "PosStabB5BackupRecoveryChecks.Run();", "CoreSmoke must run POS-STAB-B5 backup recovery checks.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(
        DirectoryInfo repoRoot,
        params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireNoDirectSql(string text, string surface)
    {
        RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"B5 {surface} must not reference SQLite directly.");
        RequireDoesNotContain(text, "SqliteConnection", $"B5 {surface} must not create SQLite connections.");
        RequireDoesNotContain(text, "INSERT ", $"B5 {surface} must not issue direct insert SQL.");
        RequireDoesNotContain(text, "UPDATE ", $"B5 {surface} must not issue direct update SQL.");
        RequireDoesNotContain(text, "DELETE ", $"B5 {surface} must not issue direct delete SQL.");
    }

    private static void RequireNoForbiddenMarkers(string text, string surface)
    {
        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(text, marker, $"B5 {surface} must not introduce forbidden marker: {marker}");
        }
    }

    private static string[] ForbiddenMarkers()
    {
        return new[]
        {
            "R" + "MO",
            "official output",
            "P" + "DF",
            "Cry" + "stal",
            "." + "rpt",
            "ESC" + "/" + "POS",
            "raw " + "USB",
            "scan" + "ner",
            "hard" + "ware",
            "cloud",
            "network backup"
        };
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

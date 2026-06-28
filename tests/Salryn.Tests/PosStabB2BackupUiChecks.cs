namespace Salryn.Tests;

public static class PosStabB2BackupUiChecks
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
        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");
        string appServicesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            backupFormText,
            "Database Backup",
            "B2-A backup UI must expose a real database backup screen.");
        RequireContains(
            backupFormText,
            "_services.CreateLocalDatabaseBackup()",
            "B2-A backup UI must delegate backup creation to AppServices.");
        RequireDoesNotContain(
            backupFormText,
            "Restore",
            "B2-A must remain backup-only and not add restore behavior.");
        RequireDoesNotContain(
            backupFormText,
            "Salryn.Infrastructure",
            "B2-A backup UI must not reference Infrastructure directly.");
        RequireDoesNotContain(
            backupFormText,
            "new SqliteConnection",
            "B2-A backup UI must not open SQLite connections directly.");
        RequireDoesNotContain(
            backupFormText,
            "DELETE ",
            "B2-A backup UI must not issue direct delete SQL.");
        RequireDoesNotContain(
            backupFormText,
            "UPDATE ",
            "B2-A backup UI must not issue direct update SQL.");
        RequireDoesNotContain(
            backupFormText,
            "INSERT ",
            "B2-A backup UI must not issue direct insert SQL.");
        RequireDoesNotContain(
            backupFormText,
            "ESC/POS",
            "B2-A backup UI must not touch hardware output.");
        RequireDoesNotContain(
            backupFormText,
            "Crystal",
            "B2-A backup UI must not touch reporting engines.");

        RequireContains(
            shellText,
            "using Salryn.App.WinForms.Features.Backup;",
            "B2-A Shell must reference the backup feature form.");
        RequireContains(
            shellText,
            "module.Key == ShellModuleKeys.Backup",
            "B2-A Shell must route the Backup module.");
        RequireContains(
            shellText,
            "using BackupForm backupForm = new(_services);",
            "B2-A Shell must open the real Backup form through AppServices.");

        RequireContains(
            appServicesText,
            "public Result<string> CreateLocalDatabaseBackup()",
            "B2-A AppServices must expose the controlled backup creation boundary.");
        RequireContains(
            appServicesText,
            "DbPaths.DatabasePath",
            "B2-A AppServices backup boundary must use the composed app database path.");
        RequireContains(
            appServicesText,
            "DbPaths.BackupPath",
            "B2-A AppServices backup boundary must default to the composed app backup path.");
        RequireContains(
            appServicesText,
            "salryn-backup-",
            "B2-A AppServices backup boundary must create operator-readable backup file names.");
        RequireContains(
            appServicesText,
            "File.Copy(sourcePath, destinationPath, overwrite: false)",
            "B2-A AppServices backup boundary must not silently overwrite existing backups.");
        RequireContains(
            appServicesText,
            "SqliteConnectionFactory.ClearAllPools();",
            "B2-A AppServices backup boundary must clear SQLite pools before copying the database file.");

        RequireContains(
            coreSmokeText,
            "PosStabB2BackupUiChecks.Run();",
            "CoreSmoke must run POS-STAB-B2 backup UI checks.");
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
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

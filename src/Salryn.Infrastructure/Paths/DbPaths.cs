using Salryn.Core.Common;

namespace Salryn.Infrastructure.Paths;

public sealed class DbPaths
{
    private DbPaths(string rootPath)
    {
        RootPath = rootPath;
        DataPath = Path.Combine(rootPath, "data");
        BackupPath = Path.Combine(rootPath, "backups");
        ExportPath = Path.Combine(rootPath, "exports");
        LogPath = Path.Combine(rootPath, "logs");
        ReceiptPath = Path.Combine(rootPath, "receipts");
        ConfigPath = Path.Combine(rootPath, "config");
        DatabasePath = Path.Combine(DataPath, SalrynProductIdentity.DatabaseFileName);
    }

    public string RootPath { get; }

    public string DataPath { get; }

    public string BackupPath { get; }

    public string ExportPath { get; }

    public string LogPath { get; }

    public string ReceiptPath { get; }

    public string ConfigPath { get; }

    public string DatabasePath { get; }

    public static DbPaths CreateProduction()
    {
        return new DbPaths(SalrynProductIdentity.GetProgramDataRoot());
    }

    public static DbPaths CreateDevelopment(string projectRoot)
    {
        return new DbPaths(Path.Combine(projectRoot, SalrynProductIdentity.DevelopmentRootFolderName));
    }

    public Result ValidateProductionWritability()
    {
        try
        {
            EnsureDirectories();

            string probePath = Path.Combine(
                DataPath,
                ".salryn-cafe-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");

            File.WriteAllText(probePath, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(probePath);

            if (File.Exists(DatabasePath))
            {
                using FileStream databaseProbe = File.Open(
                    DatabasePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Result.Fail(Error.Failure(
                "DatabasePath.ProductionNotWritable",
                "Salryn Cafe production database folder or database file is not writable. " +
                "Database path: " + DatabasePath + ". " +
                "Run Salryn Cafe with permissions to write to ProgramData\\SalrynCafe, fix the folder permissions, " +
                "or use --dev only for development."));
        }
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(BackupPath);
        Directory.CreateDirectory(ExportPath);
        Directory.CreateDirectory(LogPath);
        Directory.CreateDirectory(ReceiptPath);
        Directory.CreateDirectory(ConfigPath);
    }
}

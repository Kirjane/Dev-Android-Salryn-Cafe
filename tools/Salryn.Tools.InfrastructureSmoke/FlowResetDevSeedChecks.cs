using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

internal static class FlowResetDevSeedChecks
{
    private const string DevAdminUsername = "admin@salryn.local";
    private const string DevAdminPassword = "DevAdmin123!";
    private const string DevManagerUsername = "manager@salryn.local";
    private const string DevManagerPassword = "DevManager123!";
    private const string DevCashierUsername = "cashier@salryn.local";
    private const string DevCashierPassword = "DevCashier123!";
    private const string DevAuditUsername = "audit@salryn.local";
    private const string DevAuditPassword = "DevAudit123!";

    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== FLOW-RESET DEV DATABASE SEED (DEV ONLY) =====");

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        EnsureDevelopmentOnly(paths);

        ArchiveExistingDatabase(paths);
        ResetDevelopmentRoot(paths.RootPath);

        SalrynAppServices services = SalrynAppServices.CreateDevelopment(projectRoot);
        RequireSuccess(services.DatabaseInitialization.InitializeDatabase(), "FlowReset.InitializeDatabase");

        AuthenticatedUserReadModel admin = RequireSuccess(
            services.Authentication.CreateFirstAdminAsync(
                new FirstRunAdminSetupRequest(
                    DevAdminUsername,
                    "DEV Admin",
                    DevAdminPassword,
                    DevAdminPassword)).GetAwaiter().GetResult(),
            "FlowReset.CreateAdmin");

        IUserWriteRepository userWrites = services.Repositories.CreateUserWriteRepository();
        CreateDevUser(userWrites, admin.UserId, DevManagerUsername, "DEV Manager", Guid.Parse(DefaultSeedData.ManagerRoleId), DevManagerPassword);
        CreateDevUser(userWrites, admin.UserId, DevCashierUsername, "DEV Cashier", Guid.Parse(DefaultSeedData.CashierRoleId), DevCashierPassword);
        CreateDevUser(userWrites, admin.UserId, DevAuditUsername, "DEV Audit", Guid.Parse(DefaultSeedData.ManagerRoleId), DevAuditPassword);

        RequireSuccess(
            services.Authentication.LoginAsync(new LoginRequest(DevCashierUsername, DevCashierPassword)).GetAwaiter().GetResult(),
            "FlowReset.VerifyCashierLogin");

        Console.WriteLine();
        Console.WriteLine("DEV ONLY credentials (do not use in production):");
        Console.WriteLine($"- Admin:   {DevAdminUsername} / {DevAdminPassword}");
        Console.WriteLine($"- Manager: {DevManagerUsername} / {DevManagerPassword}");
        Console.WriteLine($"- Cashier: {DevCashierUsername} / {DevCashierPassword}");
        Console.WriteLine($"- Audit:   {DevAuditUsername} / {DevAuditPassword}");
        Console.WriteLine($"Database: {paths.DatabasePath}");
        Console.WriteLine("Flow-reset dev seed succeeded.");
        Console.WriteLine();
    }

    private static void EnsureDevelopmentOnly(DbPaths paths)
    {
        string fullRoot = Path.GetFullPath(paths.RootPath);
        if (!fullRoot.Contains(".salryn-dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to reset non-development database root: {fullRoot}");
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (fullRoot.StartsWith(programData, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to reset production ProgramData database path.");
        }
    }

    private static void ArchiveExistingDatabase(DbPaths paths)
    {
        if (!File.Exists(paths.DatabasePath))
        {
            return;
        }

        Directory.CreateDirectory(paths.BackupPath);
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string archivePath = Path.Combine(paths.BackupPath, $"salryn-dev-{stamp}.sqlite.bak");
        File.Copy(paths.DatabasePath, archivePath, overwrite: false);
        Console.WriteLine($"Archived existing database to: {archivePath}");
    }

    private static void ResetDevelopmentRoot(string devRoot)
    {
        string fullRoot = Path.GetFullPath(devRoot);
        if (!string.Equals(Path.GetFileName(fullRoot), ".salryn-dev", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to reset non-development root: {fullRoot}");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private static void CreateDevUser(
        IUserWriteRepository userWrites,
        Guid actorUserId,
        string username,
        string displayName,
        Guid roleId,
        string password)
    {
        Result<string> hash = SalrynPasswordHasher.HashPassword(password);
        RequireSuccess(hash, $"FlowReset.Hash.{username}");

        Result created = userWrites.CreateAsync(
            new UserAdminCreateRequest(username, displayName, roleId, hash.Value, actorUserId)).GetAwaiter().GetResult();
        RequireSuccess(created, $"FlowReset.CreateUser.{username}");
    }

    private static void RequireSuccess(Result result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }
    }

    private static T RequireSuccess<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.Error}");
        }

        return result.Value;
    }
}
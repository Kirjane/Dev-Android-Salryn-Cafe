using Microsoft.Data.Sqlite;
using Salryn.Core.Deployment;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class LanM1DeploymentRoleAndStoreIdentityChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string shellPresenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "ShellPresenter.cs");
        string shellForm = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string appServices = Read(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string permissions = Read(repoRoot, "src", "Salryn.Core", "Security", "SalrynPermissions.cs");
        string seedData = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Seeding", "DefaultSeedData.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string installationModels = Read(repoRoot, "src", "Salryn.Core", "Deployment", "InstallationRoleModels.cs");

        RequireContains(installationModels, "HostServer", "Installation roles must include HostServer.");
        RequireContains(installationModels, "AllInOne", "Installation roles must include AllInOne.");
        RequireContains(installationModels, "ClientTerminal", "Installation roles must include ClientTerminal.");

        RequireContains(shellPresenter, "ShellModuleKeys.SalrynStore", "Shell must expose Salryn Store module.");
        RequireContains(shellPresenter, "\"Salryn Store\"", "Shell module title must be Salryn Store.");
        RequireContains(shellPresenter, "SalrynPermissions.StoreDeploymentManage", "Salryn Store must be permission-gated.");
        RequireContains(shellForm, "OpenSalrynStore()", "Shell must route Salryn Store surface.");
        RequireContains(shellForm, "SalrynStoreForm", "Shell must open Salryn Store form.");

        RequireContains(permissions, "StoreDeploymentManage", "StoreDeployment.Manage permission must exist.");
        RequireContains(seedData, "StoreDeploymentManage", "Admin seed must include StoreDeployment.Manage.");

        RequireContains(appServices, "IDeploymentIdentityService", "AppServices must expose deployment identity service.");
        RequireContains(appServices, "SalrynStoreHostAuthorityGuard", "AppServices must expose store host authority guard.");
        RequireContains(appServices, "StoreHostAuthority", "AppServices must wire store host authority guard.");

        RequireContains(coreSmoke, "LanM1DeploymentRoleAndStoreIdentityChecks.Run();", "CoreSmoke must run LAN-M1 guard.");

        foreach (string marker in ForbiddenTransportMarkers())
        {
            RequireNotContains(shellPresenter, marker, "LAN-M1 must not introduce transport marker: " + marker);
            RequireNotContains(shellForm, marker, "LAN-M1 must not introduce transport marker: " + marker);
            RequireNotContains(appServices, marker, "LAN-M1 must not introduce transport marker: " + marker);
        }

        VerifyRuntimeDeploymentIdentityRules(repoRoot);
    }

    private static void VerifyRuntimeDeploymentIdentityRules(DirectoryInfo repoRoot)
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "salryn-lan-m1-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths dbPaths = DbPaths.CreateDevelopment(projectRoot);
            DatabaseInitializationOrchestrator orchestrator = DefaultDatabaseInitializationFactory.Create(dbPaths);

            if (orchestrator.InitializeDatabase().IsFailure)
            {
                throw new InvalidOperationException("LAN-M1 runtime check failed to initialize database.");
            }

            SqliteDeploymentIdentityRepository repository = new(new SqliteConnectionFactory(dbPaths));
            DeploymentIdentityService service = new(repository);

            DeploymentIdentitySnapshot? defaultSnapshot = service.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Existing install must default to deployment identity.");

            if (defaultSnapshot.Host.InstallationRole != InstallationRole.AllInOne)
            {
                throw new InvalidOperationException("Existing install must default to AllInOne.");
            }

            if (!defaultSnapshot.Authority.IsDatabaseAuthority ||
                !defaultSnapshot.Authority.IsJournalAuthority)
            {
                throw new InvalidOperationException("AllInOne default must retain store host authority.");
            }

            if (defaultSnapshot.Store.StoreId == defaultSnapshot.Host.HostId ||
                defaultSnapshot.Store.StoreId == defaultSnapshot.Terminal.TerminalId ||
                defaultSnapshot.Host.HostId == defaultSnapshot.Terminal.TerminalId)
            {
                throw new InvalidOperationException("StoreId, HostId, and TerminalId must be distinct.");
            }

            SalrynStoreHostAuthorityGuard authorityGuard = new(() => defaultSnapshot);

            if (authorityGuard.RequireJournalAuthority().IsFailure)
            {
                throw new InvalidOperationException("AllInOne must pass journal authority checks.");
            }

            DeploymentIdentitySnapshot clientSnapshot = service.ConfigureClientTerminalAsync(
                new ClientTerminalConfigurationRequest(
                    StoreName: "Client Store",
                    TerminalName: "Terminal-01",
                    HostAddress: "192.168.1.20",
                    HostPort: 58161,
                    PairingCredentialLabel: "pairing-label-placeholder")).GetAwaiter().GetResult().Value;

            if (clientSnapshot.Authority.IsDatabaseAuthority ||
                clientSnapshot.Authority.IsJournalAuthority ||
                !clientSnapshot.Authority.IsClientOnly)
            {
                throw new InvalidOperationException("ClientTerminal must not hold store host authority.");
            }

            authorityGuard = new SalrynStoreHostAuthorityGuard(() => clientSnapshot);

            if (authorityGuard.RequireJournalAuthority().IsSuccess)
            {
                throw new InvalidOperationException("ClientTerminal must fail journal authority checks.");
            }

            if (service.ConfigureClientTerminalAsync(
                new ClientTerminalConfigurationRequest(
                    StoreName: "Client Store",
                    TerminalName: "Terminal-01",
                    HostAddress: " ",
                    HostPort: 58161,
                    PairingCredentialLabel: "pairing-label-placeholder")).GetAwaiter().GetResult().IsSuccess)
            {
                throw new InvalidOperationException("ClientTerminal must require HostAddress.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort temp cleanup for guard runtime verification.
                }
            }
        }
    }

    private static IEnumerable<string> ForbiddenTransportMarkers()
    {
        yield return "HttpListener";
        yield return "TcpListener";
        yield return "SignalR";
        yield return "WebSocket";
        yield return "shared SQLite over network";
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

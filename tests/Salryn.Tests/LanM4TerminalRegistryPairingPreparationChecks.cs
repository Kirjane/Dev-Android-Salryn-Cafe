using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Security;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class LanM4TerminalRegistryPairingPreparationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string models = Read(repoRoot, "src", "Salryn.Core", "TerminalRegistry", "TerminalRegistryModels.cs");
        string migration = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "TerminalRegistrySchemaMigration.cs");
        string registryService = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalRegistryService.cs");
        string pairingService = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalPairingPreparationService.cs");
        string hasher = Read(repoRoot, "src", "Salryn.Core", "TerminalRegistry", "TerminalPairingSecretHasher.cs");
        string commandNames = Read(repoRoot, "src", "Salryn.Core", "Host", "SalrynHostCommandNames.cs");
        string handlers = Read(repoRoot, "src", "Salryn.Host.Local", "HostCommands", "Handlers", "TerminalRegistryHostCommandHandlers.cs");
        string presenter = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "SalrynStore", "SalrynStorePresenter.cs");
        string form = Read(repoRoot, "src", "Salryn.App.WinForms", "Features", "SalrynStore", "SalrynStoreForm.cs");
        string permissions = Read(repoRoot, "src", "Salryn.Core", "Security", "SalrynPermissions.cs");
        string seed = Read(repoRoot, "src", "Salryn.Infrastructure", "Data", "Seeding", "DefaultSeedData.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = Read(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string handoff = Read(repoRoot, "00-HANDOFF", "LAN_M4_TERMINAL_REGISTRY_PAIRING_PREPARATION.md");

        RequireContains(models, "SalrynTerminalRegistration", "Terminal registry model must exist.");
        RequireContains(models, "TerminalRegistrationStatus", "Terminal statuses must exist.");
        RequireContains(models, "PendingPairing", "PendingPairing status must exist.");
        RequireContains(models, "Active", "Active status must exist.");
        RequireContains(models, "Disabled", "Disabled status must exist.");
        RequireContains(models, "Revoked", "Revoked status must exist.");
        RequireContains(models, "PairingInviteStatus", "Pairing statuses must exist.");
        RequireContains(models, "Issued", "Issued pairing status must exist.");
        RequireContains(models, "Consumed", "Consumed pairing status must exist.");
        RequireContains(models, "Expired", "Expired pairing status must exist.");
        RequireContains(models, "PairingCodeHash", "Pairing hash field must exist.");

        RequireContains(migration, "salryn_terminal_registrations", "Terminal registry table must exist.");
        RequireContains(migration, "salryn_terminal_pairing_invites", "Pairing invite table must exist.");
        RequireContains(migration, "pairing_code_hash", "Pairing hash column must be persisted.");

        RequireContains(hasher, "HashPairingCode", "Pairing hash helper must exist.");
        RequireContains(hasher, "GeneratePairingCode", "Pairing code generator must exist.");
        RequireNotContains(migration, "pairing_code TEXT", "Raw pairing code must not be persisted.");

        RequireContains(registryService, "IsTerminalAcceptedForHostCommandsAsync", "Terminal acceptance guard must exist.");
        RequireContains(pairingService, "InviteConsumed", "Consumed pairing invite must fail validation.");
        RequireContains(pairingService, "InviteRevoked", "Revoked pairing invite must fail validation.");
        RequireContains(pairingService, "InviteExpired", "Expired pairing invite must fail validation.");

        RequireContains(commandNames, SalrynHostCommandNames.TerminalRegistryList, "Terminal registry list command must exist.");
        RequireContains(commandNames, SalrynHostCommandNames.TerminalPairingIssue, "Terminal pairing issue command must exist.");
        RequireContains(handlers, "TerminalPairingIssueHostCommandHandler", "Pairing issue host handler must exist.");

        RequireContains(permissions, SalrynPermissions.StoreTerminalView, "StoreTerminal.View permission must exist.");
        RequireContains(permissions, SalrynPermissions.StoreTerminalPairingIssue, "StoreTerminal.PairingIssue permission must exist.");
        RequireContains(seed, SalrynPermissions.StoreTerminalPairingIssue, "Admin seed must include pairing issue permission.");
        RequireContains(seed, SalrynPermissions.StoreTerminalView, "Manager seed must include terminal view permission.");

        string cashierPermissionBlock = string.Join('\n', DefaultSeedData.CashierPermissions);
        RequireNotContains(cashierPermissionBlock, SalrynPermissions.StoreTerminalPairingIssue, "Cashier must not receive pairing issue permission.");

        RequireContains(presenter, "IssuePairingInviteAsync", "Salryn Store presenter must issue pairing invites.");
        RequireContains(form, "Cross-PC LAN client connection is not enabled yet", "Salryn Store must warn about no cross-PC LAN.");

        RequireContains(coreSmoke, "LanM4TerminalRegistryPairingPreparationChecks.Run();", "CoreSmoke must run LAN-M4 guard.");
        RequireContains(infraSmoke, "terminal-registry-pairing", "Infrastructure smoke must include terminal-registry-pairing.");
        RequireContains(handoff, "No cross-PC LAN", "Handoff must document no cross-PC LAN.");

        foreach (string marker in ForbiddenMarkers())
        {
            RequireNotContains(migration, marker, "Migration must not introduce forbidden marker: " + marker);
            RequireNotContains(handlers, marker, "Handlers must not introduce forbidden marker: " + marker);
            RequireNotContains(form, marker, "Salryn Store form must not introduce forbidden marker: " + marker);
        }

        VerifyRuntimePermissionAndAuthorityRules();
    }

    private static void VerifyRuntimePermissionAndAuthorityRules()
    {
        MachineAuthority clientAuthority = InstallationRoleAuthorityRules.ResolveAuthority(InstallationRole.ClientTerminal);
        SalrynStoreHostAuthorityGuard clientGuard = new(() => new DeploymentIdentitySnapshot(
            Store: new DeploymentStoreIdentity(Guid.NewGuid(), "Store", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Host: new DeploymentHostIdentity(Guid.NewGuid(), InstallationRole.ClientTerminal, "client", false, false, false, false),
            Terminal: new DeploymentTerminalIdentity(Guid.NewGuid(), "terminal", InstallationRole.ClientTerminal, false, DateTimeOffset.UtcNow),
            ClientConnection: new ClientHostConnectionConfig("192.168.1.1", 58161, "label", null, ClientHostConnectionStatus.TransportNotImplemented),
            Authority: clientAuthority,
            IsConfigured: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        if (clientGuard.RequireDatabaseAuthority().IsSuccess)
        {
            throw new InvalidOperationException("ClientTerminal must fail database authority for pairing issue.");
        }

        if (!DefaultSeedData.AdminPermissions.Contains(SalrynPermissions.StoreTerminalPairingIssue))
        {
            throw new InvalidOperationException("Admin must be able to issue pairing invites.");
        }

        if (DefaultSeedData.CashierPermissions.Contains(SalrynPermissions.StoreTerminalPairingIssue))
        {
            throw new InvalidOperationException("Cashier must not issue pairing invites.");
        }

        string rawCode = TerminalPairingSecretHasher.GeneratePairingCode();
        string hash = TerminalPairingSecretHasher.HashPairingCode(rawCode);
        if (string.IsNullOrWhiteSpace(hash) || hash == rawCode)
        {
            throw new InvalidOperationException("Pairing hash must differ from raw pairing code.");
        }
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "QR pairing";
        yield return "network discovery";
        yield return "0.0.0.0";
        yield return "HttpListener";
        yield return "shared SQLite over network";
        yield return "SignalR";
        yield return "WebSocket";
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

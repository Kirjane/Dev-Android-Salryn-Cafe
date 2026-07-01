using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class LanM5ControlledRemoteTransportGateChecks
{
    private static readonly Guid ActiveTerminalId = Guid.Parse("55555555-0000-0000-0000-000000000001");
    private static readonly Guid PendingTerminalId = Guid.Parse("55555555-0000-0000-0000-000000000002");
    private static readonly Guid DisabledTerminalId = Guid.Parse("55555555-0000-0000-0000-000000000003");
    private static readonly Guid RevokedTerminalId = Guid.Parse("55555555-0000-0000-0000-000000000004");

    public static void Run()
    {
        LanRemoteTransportConfig defaults = LanRemoteTransportConfig.CreateDefault(DateTimeOffset.UtcNow);

        Require(!defaults.RemoteTransportEnabled, "LAN-M5 default remote transport must be disabled.");
        Require(defaults.BindMode == LanRemoteTransportBindMode.LoopbackOnly, "LAN-M5 default bind mode must be loopback-only.");
        Require(SalrynLanRemoteTransportGate.ValidateConfig(defaults).IsSuccess, "Default config must validate.");
        Require(SalrynLanRemoteTransportGate.ValidateConfig(defaults with { BindAddress = "0.0.0.0" }).IsFailure, "0.0.0.0 must be rejected.");
        Require(SalrynLanRemoteTransportGate.ValidateConfig(defaults with
        {
            BindMode = LanRemoteTransportBindMode.ExplicitLanAddress,
            BindAddress = "192.168.1.10"
        }).IsFailure, "LAN IP bind requires explicit remote enable.");

        SalrynLanRemoteTransportGate disabledGate = CreateGate(defaults);
        RequireFailure(disabledGate, "192.168.1.50", ActiveTerminalId, SalrynHostCommandNames.DeploymentStatus, "Remote disabled must reject remote origin.");

        LanRemoteTransportConfig enabled = defaults with
        {
            RemoteTransportEnabled = true,
            BindMode = LanRemoteTransportBindMode.ExplicitLanAddress,
            BindAddress = "192.168.1.10"
        };

        SalrynLanRemoteTransportGate enabledGate = CreateGate(enabled);
        RequireFailure(enabledGate, "192.168.1.50", Guid.Parse("55555555-0000-0000-0000-000000000099"), SalrynHostCommandNames.DeploymentStatus, "Unknown terminal must be rejected.");
        RequireFailure(enabledGate, "192.168.1.50", PendingTerminalId, SalrynHostCommandNames.DeploymentStatus, "Pending terminal must be rejected.");
        RequireFailure(enabledGate, "192.168.1.50", DisabledTerminalId, SalrynHostCommandNames.DeploymentStatus, "Disabled terminal must be rejected.");
        RequireFailure(enabledGate, "192.168.1.50", RevokedTerminalId, SalrynHostCommandNames.DeploymentStatus, "Revoked terminal must be rejected.");
        RequireSuccess(enabledGate, "192.168.1.50", ActiveTerminalId, SalrynHostCommandNames.DeploymentStatus, "Active terminal should pass for safe status read.");
        RequireFailure(enabledGate, "192.168.1.50", ActiveTerminalId, SalrynHostCommandNames.OfficialCheckoutComplete, "Official checkout must be rejected remotely.");
        RequireFailure(enabledGate, "192.168.1.50", ActiveTerminalId, SalrynHostCommandNames.OfficialLifecycleAppend, "Lifecycle command must be rejected remotely.");
        RequireFailure(enabledGate, "192.168.1.50", ActiveTerminalId, SalrynHostCommandNames.OfficialReadingFinalizeZ, "Z finalize must be rejected remotely.");
        RequireFailure(enabledGate, "192.168.1.50", ActiveTerminalId, SalrynHostCommandNames.TerminalRegistryDisable, "Admin mutation must be rejected remotely.");
        RequireSuccess(enabledGate, SalrynHostCommandLoopbackRules.LoopbackHost, ActiveTerminalId, SalrynHostCommandNames.OfficialCheckoutComplete, "Loopback transport must remain intact.");

        RequireNoForbiddenMarkers();
        RequireCheckStatusReadOnly();
    }

    private static SalrynLanRemoteTransportGate CreateGate(LanRemoteTransportConfig config) =>
        new(
            _ => Task.FromResult(Result<LanRemoteTransportConfig>.Success(config)),
            (terminalId, _) => Task.FromResult(Result<SalrynTerminalRegistration?>.Success(ResolveTerminal(terminalId))));

    private static SalrynTerminalRegistration? ResolveTerminal(Guid terminalId)
    {
        TerminalRegistrationStatus? status =
            terminalId == ActiveTerminalId ? TerminalRegistrationStatus.Active :
            terminalId == PendingTerminalId ? TerminalRegistrationStatus.PendingPairing :
            terminalId == DisabledTerminalId ? TerminalRegistrationStatus.Disabled :
            terminalId == RevokedTerminalId ? TerminalRegistrationStatus.Revoked :
            null;

        return status.HasValue
            ? new SalrynTerminalRegistration(
                terminalId,
                Guid.Parse("55555555-1000-0000-0000-000000000001"),
                Guid.Parse("55555555-2000-0000-0000-000000000001"),
                "LAN-M5 terminal",
                TerminalRegistrationRole.CashierClient,
                status.Value,
                DateTimeOffset.UtcNow,
                null,
                status == TerminalRegistrationStatus.Disabled ? DateTimeOffset.UtcNow : null,
                status == TerminalRegistrationStatus.Revoked ? DateTimeOffset.UtcNow : null,
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                null)
            : null;
    }

    private static void RequireSuccess(
        SalrynLanRemoteTransportGate gate,
        string origin,
        Guid terminalId,
        string commandName,
        string message)
    {
        Result result = Authorize(gate, origin, terminalId, commandName);
        Require(result.IsSuccess, message);
    }

    private static void RequireFailure(
        SalrynLanRemoteTransportGate gate,
        string origin,
        Guid terminalId,
        string commandName,
        string message)
    {
        Result result = Authorize(gate, origin, terminalId, commandName);
        Require(result.IsFailure, message);
    }

    private static Result Authorize(
        SalrynLanRemoteTransportGate gate,
        string origin,
        Guid terminalId,
        string commandName)
    {
        if (!SalrynHostCommandCategoryCatalog.TryResolve(commandName, out HostCommandCategory category))
        {
            throw new InvalidOperationException("Command must resolve for LAN-M5 gate check: " + commandName);
        }

        return gate.AuthorizeAsync(new LanRemoteTransportGateRequest(origin, terminalId, commandName, category))
            .GetAwaiter()
            .GetResult();
    }

    private static void RequireNoForbiddenMarkers()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string[] reviewedFiles =
        {
            Path.Combine(root.FullName, "src", "Salryn.Core", "Host", "LanRemoteTransportModels.cs"),
            Path.Combine(root.FullName, "src", "Salryn.Core", "Host", "SalrynLanRemoteTransportGate.cs"),
            Path.Combine(root.FullName, "src", "Salryn.Host.Local", "HostCommands", "SalrynHostCommandRemoteTransport.cs"),
            Path.Combine(root.FullName, "src", "Salryn.App.WinForms", "Features", "SalrynStore", "SalrynStoreForm.cs")
        };

        foreach (string text in reviewedFiles.Select(File.ReadAllText))
        {
            Require(!text.Contains("shared SQLite over network", StringComparison.OrdinalIgnoreCase), "Shared SQLite marker must not be introduced.");
            Require(!text.Contains("QR", StringComparison.OrdinalIgnoreCase), "QR marker must not be introduced.");
            Require(!text.Contains("network discovery", StringComparison.OrdinalIgnoreCase), "Network discovery marker must not be introduced.");
            Require(!text.Contains("remote cashier checkout", StringComparison.OrdinalIgnoreCase), "Remote cashier checkout UI must not be introduced.");
            Require(!text.Contains("mobile/cloud", StringComparison.OrdinalIgnoreCase), "Mobile/cloud endpoint marker must not be introduced.");
        }
    }

    private static void RequireCheckStatusReadOnly()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-lanm5-status-" + Guid.NewGuid().ToString("N"));

        try
        {
            DatabaseInitializationOrchestrator orchestrator = DefaultDatabaseInitializationFactory.CreateDevelopment(tempRoot);
            Require(orchestrator.InitializeDatabase().IsSuccess, "Database initialization must succeed for CheckStatus read-only proof.");

            string databasePath = DbPaths.CreateDevelopment(tempRoot).DatabasePath;
            long before = CountAppliedMigrations(databasePath);
            Require(orchestrator.CheckStatus().IsSuccess, "CheckStatus must succeed.");
            long after = CountAppliedMigrations(databasePath);

            Require(before == after, "CheckStatus must remain read-only and must not mutate migration state.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static long CountAppliedMigrations(string databasePath)
    {
        using SqliteConnection connection = new("Data Source=" + databasePath);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        return Convert.ToInt64(command.ExecuteScalar());
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public static class LanM5GateChecks
{
    public static void Run() => LanM5ControlledRemoteTransportGateChecks.Run();
}

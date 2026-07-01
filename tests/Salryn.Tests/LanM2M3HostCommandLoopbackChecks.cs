using Salryn.App.WinForms.Features.Checkout;
using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.App.WinForms.HostCommands;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Host.Local.HostCommands;

namespace Salryn.Tests;

public static class LanM2M3HostCommandLoopbackChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string commandModels = Read(repoRoot, "src", "Salryn.Core", "Host", "SalrynHostCommandModels.cs");
        string authorityGuard = Read(repoRoot, "src", "Salryn.Core", "Host", "SalrynHostCommandAuthorityGuard.cs");
        string dispatcher = Read(repoRoot, "src", "Salryn.Host.Local", "HostCommands", "HostCommandDispatcher.cs");
        string loopbackTransport = Read(repoRoot, "src", "Salryn.Host.Local", "HostCommands", "SalrynHostCommandLoopbackTransport.cs");
        string checkoutHandler = Read(repoRoot, "src", "Salryn.Host.Local", "HostCommands", "Handlers", "OfficialCheckoutHostCommandHandler.cs");
        string lifecycleHandler = Read(repoRoot, "src", "Salryn.Host.Local", "HostCommands", "Handlers", "OfficialLifecycleHostCommandHandler.cs");
        string readingHandlers = Read(repoRoot, "src", "Salryn.Host.Local", "HostCommands", "Handlers", "OfficialReadingHostCommandHandlers.cs");
        string executorAdapters = Read(repoRoot, "src", "Salryn.App.WinForms", "HostCommands", "HostCommandExecutorAdapters.cs");
        string appServices = Read(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = Read(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string handoff = Read(repoRoot, "00-HANDOFF", "LAN_M2_M3_HOST_COMMAND_LOOPBACK.md");

        RequireContains(commandModels, "SalrynHostCommandEnvelope", "Host command envelope must exist.");
        RequireContains(commandModels, "CommandName", "Host command envelope must include command name.");
        RequireContains(commandModels, "RequestId", "Host command envelope must include request id.");
        RequireContains(commandModels, "StoreId", "Host command envelope must include store id.");
        RequireContains(commandModels, "TerminalId", "Host command envelope must include terminal id.");
        RequireContains(commandModels, "UserId", "Host command envelope must include user id.");
        RequireContains(commandModels, "TimestampUtc", "Host command envelope must include timestamp.");
        RequireContains(commandModels, "SalrynHostCommandResult", "Host command result must exist.");

        RequireContains(authorityGuard, "RequireLocalLoopbackAllowed", "Host command authority guard must enforce loopback.");
        RequireContains(authorityGuard, "RequireAuthorities", "Host command authority guard must enforce command authorities.");
        RequireContains(authorityGuard, "ClientTerminalLoopbackDisabled", "Client terminal loopback must be disabled.");

        RequireContains(dispatcher, "IHostCommandDispatcher", "Host command dispatcher must exist.");
        RequireContains(dispatcher, "UnknownCommand", "Dispatcher must reject unknown commands.");
        RequireContains(dispatcher, "HostCommandEnvelopeValidator", "Dispatcher must validate envelopes.");

        RequireContains(loopbackTransport, "SalrynHostCommandLoopbackTransport", "Loopback transport must exist.");
        RequireContains(loopbackTransport, "RequireLocalLoopbackAllowed", "Loopback transport must reject remote origins.");

        RequireContains(checkoutHandler, "IOfficialCheckoutHostCommandExecutor", "Official checkout handler must use executor port.");
        RequireContains(checkoutHandler, "OfficialSequence", "Official checkout handler must require sequence authority.");
        RequireContains(lifecycleHandler, "IOfficialLifecycleHostCommandExecutor", "Lifecycle handler must use executor port.");
        RequireContains(readingHandlers, "OfficialReadingFinalizeZ", "Z finalize host command must exist.");

        RequireContains(executorAdapters, "IOfficialCheckoutCompletionBoundary", "Official checkout executor must wrap checkout boundary.");
        RequireContains(executorAdapters, "IOfficialDocumentLifecycleOperationBoundary", "Lifecycle executor must wrap lifecycle boundary.");
        RequireContains(executorAdapters, "IOfficialZReadingService", "Z finalize executor must use official Z service.");

        RequireContains(appServices, "HostCommands", "AppServices must expose host command loopback surface.");
        RequireContains(appServices, "HostCommandComposition.Create", "AppServices must compose host commands.");

        RequireContains(coreSmoke, "LanM2M3HostCommandLoopbackChecks.Run();", "CoreSmoke must run LAN-M2/M3 guard.");
        RequireContains(infraSmoke, "host-command-loopback", "Infrastructure smoke must include host-command-loopback.");
        RequireContains(handoff, "No cross-PC LAN", "Handoff must document no cross-PC LAN.");

        foreach (string marker in ForbiddenTransportMarkers())
        {
            RequireNotContains(loopbackTransport, marker, "Loopback transport must not introduce forbidden marker: " + marker);
            RequireNotContains(dispatcher, marker, "Dispatcher must not introduce forbidden marker: " + marker);
            RequireNotContains(appServices, marker, "AppServices must not introduce forbidden marker: " + marker);
        }

        RequireTrue(SalrynHostCommandLoopbackRules.IsForbiddenBindAddress("0.0.0.0"), "0.0.0.0 bind must be forbidden.");
        RequireFalse(SalrynHostCommandLoopbackRules.IsLoopbackOrigin("192.168.1.10"), "LAN IP origin must be rejected.");
        RequireTrue(SalrynHostCommandLoopbackRules.IsLoopbackOrigin("127.0.0.1"), "Loopback origin must be accepted.");

        VerifyRuntimeAuthorityRules();
    }

    private static void VerifyRuntimeAuthorityRules()
    {
        MachineAuthority clientAuthority = InstallationRoleAuthorityRules.ResolveAuthority(InstallationRole.ClientTerminal);
        SalrynStoreHostAuthorityGuard storeGuard = new(() => new DeploymentIdentitySnapshot(
            Store: new DeploymentStoreIdentity(Guid.NewGuid(), "Store", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Host: new DeploymentHostIdentity(Guid.NewGuid(), InstallationRole.ClientTerminal, "client", false, false, false, false),
            Terminal: new DeploymentTerminalIdentity(Guid.NewGuid(), "terminal", InstallationRole.ClientTerminal, false, DateTimeOffset.UtcNow),
            ClientConnection: new ClientHostConnectionConfig("192.168.1.1", 58161, "label", null, ClientHostConnectionStatus.TransportNotImplemented),
            Authority: clientAuthority,
            IsConfigured: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        if (storeGuard.RequireJournalAuthority().IsSuccess)
        {
            throw new InvalidOperationException("ClientTerminal must fail journal authority.");
        }

        MachineAuthority allInOneAuthority = InstallationRoleAuthorityRules.ResolveAuthority(InstallationRole.AllInOne);
        SalrynStoreHostAuthorityGuard allInOneGuard = new(() => new DeploymentIdentitySnapshot(
            Store: new DeploymentStoreIdentity(Guid.NewGuid(), "Store", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Host: new DeploymentHostIdentity(Guid.NewGuid(), InstallationRole.AllInOne, "host", true, true, true, true),
            Terminal: new DeploymentTerminalIdentity(Guid.NewGuid(), "terminal", InstallationRole.AllInOne, true, DateTimeOffset.UtcNow),
            ClientConnection: new ClientHostConnectionConfig(null, null, null, null, ClientHostConnectionStatus.NotConfigured),
            Authority: allInOneAuthority,
            IsConfigured: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        if (allInOneGuard.RequireDatabaseAuthority().IsFailure)
        {
            throw new InvalidOperationException("AllInOne must pass database authority.");
        }

        SalrynHostCommandAuthorityGuard commandGuard = new(allInOneGuard);

        if (commandGuard.RequireLocalLoopbackAllowed("192.168.0.5", null).IsSuccess)
        {
            throw new InvalidOperationException("Remote origin must fail loopback guard.");
        }
    }

    private static IEnumerable<string> ForbiddenTransportMarkers()
    {
        yield return "HttpListener";
        yield return "TcpListener";
        yield return "SignalR";
        yield return "WebSocket";
        yield return "0.0.0.0";
        yield return "pairing token";
        yield return "QR pairing";
        yield return "network discovery";
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

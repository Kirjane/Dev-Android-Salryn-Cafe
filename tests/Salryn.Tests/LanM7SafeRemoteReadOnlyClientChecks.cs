namespace Salryn.Tests;

public static class LanM7SafeRemoteReadOnlyClientChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string models = Read(root, "src", "Salryn.Core", "RemoteClients", "RemoteReadOnlyClientModels.cs");
        string serviceContract = Read(root, "src", "Salryn.Core", "Services", "IRemoteReadOnlyClientService.cs");
        string remoteTransportContract = Read(root, "src", "Salryn.Core", "Host", "IRemoteHostCommandTransport.cs");
        string serviceImplementation = Read(root, "src", "Salryn.App.WinForms", "HostCommands", "RemoteReadOnlyClientService.cs");
        string hostNames = Read(root, "src", "Salryn.Core", "Host", "SalrynHostCommandNames.cs");
        string hostPorts = Read(root, "src", "Salryn.Core", "Host", "HostCommandExecutorPorts.cs");
        string gateModels = Read(root, "src", "Salryn.Core", "Host", "LanRemoteTransportModels.cs");
        string remoteTransport = Read(root, "src", "Salryn.Host.Local", "HostCommands", "SalrynHostCommandRemoteTransport.cs");
        string controlledRemoteTransport = Read(root, "src", "Salryn.Host.Local", "HostCommands", "ControlledRemoteHostCommandTransport.cs");
        string handlers = Read(root, "src", "Salryn.Host.Local", "HostCommands", "Handlers", "RemoteReadOnlyClientHostCommandHandlers.cs");
        string composition = Read(root, "src", "Salryn.Host.Local", "HostCommands", "RemoteReadOnlyHostCommandComposition.cs");
        string executors = Read(root, "src", "Salryn.App.WinForms", "HostCommands", "RemoteReadOnlyHostCommandExecutors.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "LanRemoteTransportAppServices.cs");
        string handshake = Read(root, "src", "Salryn.Infrastructure", "Data", "Services", "RemoteClientHandshakeService.cs");
        string smokeProgram = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string smoke = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "SafeRemoteReadOnlyClientSmokeChecks.cs");

        Require(models.Contains("RemoteReadOnlyClientContext", StringComparison.Ordinal), "Remote read-only client context model must exist.");
        Require(models.Contains("RemoteReadOnlyClientRequest", StringComparison.Ordinal), "Remote read-only client request model must exist.");
        Require(models.Contains("RemoteReadOnlyClientResult", StringComparison.Ordinal), "Remote read-only client result model must exist.");
        Require(models.Contains("RemoteReadOnlyClientCapability", StringComparison.Ordinal), "Remote read-only client capability model must exist.");
        Require(models.Contains("RemoteReadOnlyClientStatus", StringComparison.Ordinal), "Remote read-only client status model must exist.");
        Require(models.Contains("StatusRead", StringComparison.Ordinal), "Status read capability must exist.");
        Require(models.Contains("ProductRead", StringComparison.Ordinal), "Product read capability must exist.");
        Require(models.Contains("DashboardRead", StringComparison.Ordinal), "Dashboard read capability must exist.");
        Require(models.Contains("TerminalStatusRead", StringComparison.Ordinal), "Terminal status read capability must exist.");
        Require(models.Contains("CheckoutMutation", StringComparison.Ordinal), "Checkout mutation must be explicitly outside safe capabilities.");
        Require(models.Contains("SalesMutation", StringComparison.Ordinal), "Sales mutation must be explicitly outside safe capabilities.");
        Require(models.Contains("InventoryMutation", StringComparison.Ordinal), "Inventory mutation must be explicitly outside safe capabilities.");
        Require(models.Contains("OfficialJournalMutation", StringComparison.Ordinal), "Official journal mutation must be explicitly outside safe capabilities.");
        Require(models.Contains("OfficialSequenceMutation", StringComparison.Ordinal), "Official sequence mutation must be explicitly outside safe capabilities.");
        Require(models.Contains("AdminMutation", StringComparison.Ordinal), "Admin mutation must be explicitly outside safe capabilities.");
        Require(models.Contains("BackupMutation", StringComparison.Ordinal), "Backup mutation must be explicitly outside safe capabilities.");

        Require(serviceContract.Contains("IRemoteReadOnlyClientService", StringComparison.Ordinal), "Remote read-only client service contract must exist.");
        Require(serviceContract.Contains("GetRemoteClientStatusAsync", StringComparison.Ordinal), "Client status read operation must exist.");
        Require(serviceContract.Contains("GetDeploymentStatusAsync", StringComparison.Ordinal), "Deployment status read operation must exist.");
        Require(serviceContract.Contains("LookupProductAsync", StringComparison.Ordinal), "Product lookup read operation must exist.");
        Require(serviceContract.Contains("GetDashboardSummaryAsync", StringComparison.Ordinal), "Dashboard summary read operation must exist.");
        Require(serviceContract.Contains("GetTerminalStatusAsync", StringComparison.Ordinal), "Terminal status read operation must exist.");

        Require(remoteTransportContract.Contains("IRemoteHostCommandTransport", StringComparison.Ordinal), "Core remote host command transport abstraction must exist.");
        Require(remoteTransportContract.Contains("SalrynRemoteHostCommandEnvelope", StringComparison.Ordinal), "Core remote host command transport abstraction must use remote envelopes.");
        Require(remoteTransportContract.Contains("SalrynHostCommandResult", StringComparison.Ordinal), "Core remote host command transport abstraction must return host command results.");
        Require(serviceImplementation.Contains("IRemoteHostCommandTransport", StringComparison.Ordinal), "Remote read-only client service must depend on the Core remote transport abstraction.");
        Require(!serviceImplementation.Contains("Salryn.Host.Local", StringComparison.Ordinal), "Remote read-only client service must not reference Host.Local.");
        Require(!serviceImplementation.Contains("SalrynHostCommandRemoteTransport", StringComparison.Ordinal), "Remote read-only client service must not depend on the Host.Local concrete transport.");
        Require(serviceImplementation.Contains("HandshakeSessionRequired", StringComparison.Ordinal), "Remote read-only client service must require handshake session.");
        Require(serviceImplementation.Contains("CreateConnection", StringComparison.Ordinal) == false, "Remote read-only client service must not open SQLite connections.");
        Require(serviceImplementation.Contains("CreateTerminalRegistryRepository", StringComparison.Ordinal) == false, "Remote read-only client service must not use repositories directly.");
        Require(serviceImplementation.Contains("Sqlite", StringComparison.Ordinal) == false, "Remote read-only client service must not reference SQLite.");
        Require(controlledRemoteTransport.Contains("IRemoteHostCommandTransport", StringComparison.Ordinal), "Host.Local bridge must implement the Core remote transport abstraction.");
        Require(controlledRemoteTransport.Contains("SalrynHostCommandRemoteTransport", StringComparison.Ordinal), "Host.Local bridge must delegate to the concrete remote transport inside Host.Local.");
        Require(appServices.Contains("ControlledRemoteHostCommandTransport", StringComparison.Ordinal), "Composition root must adapt Host.Local concrete remote transport to the Core abstraction.");

        Require(hostNames.Contains("remote.client.status", StringComparison.Ordinal), "Remote client status host command must exist.");
        Require(hostNames.Contains("remote.deployment.status", StringComparison.Ordinal), "Remote deployment status host command must exist.");
        Require(hostNames.Contains("remote.products.lookup", StringComparison.Ordinal), "Remote product lookup host command must exist.");
        Require(hostNames.Contains("remote.dashboard.summary", StringComparison.Ordinal), "Remote dashboard summary host command must exist.");
        Require(hostNames.Contains("remote.terminal.status", StringComparison.Ordinal), "Remote terminal status host command must exist.");

        Require(hostPorts.Contains("IRemoteClientStatusHostCommandExecutor", StringComparison.Ordinal), "Remote client status executor port must exist.");
        Require(hostPorts.Contains("IRemoteDashboardSummaryHostCommandExecutor", StringComparison.Ordinal), "Remote dashboard executor port must exist.");
        Require(hostPorts.Contains("IRemoteTerminalStatusHostCommandExecutor", StringComparison.Ordinal), "Remote terminal status executor port must exist.");
        Require(handlers.Contains("RemoteClientStatusHostCommandHandler", StringComparison.Ordinal), "Remote client status handler must exist.");
        Require(handlers.Contains("RemoteDeploymentStatusHostCommandHandler", StringComparison.Ordinal), "Remote deployment status handler must exist.");
        Require(handlers.Contains("RemoteProductLookupHostCommandHandler", StringComparison.Ordinal), "Remote product lookup handler must exist.");
        Require(handlers.Contains("RemoteDashboardSummaryHostCommandHandler", StringComparison.Ordinal), "Remote dashboard handler must exist.");
        Require(handlers.Contains("RemoteTerminalStatusHostCommandHandler", StringComparison.Ordinal), "Remote terminal status handler must exist.");
        Require(composition.Contains("RemoteReadOnlyHostCommandComposition", StringComparison.Ordinal), "Remote read-only host composition must exist.");
        Require(executors.Contains("RemoteClientStatusHostCommandExecutor", StringComparison.Ordinal), "Remote client status executor must exist.");
        Require(executors.Contains("RemoteDashboardSummaryHostCommandExecutor", StringComparison.Ordinal), "Remote dashboard executor must exist.");
        Require(executors.Contains("RemoteTerminalStatusHostCommandExecutor", StringComparison.Ordinal), "Remote terminal status executor must exist.");

        Require(gateModels.Contains("DashboardRead", StringComparison.Ordinal), "Remote gate must categorize dashboard reads.");
        Require(gateModels.Contains("TerminalStatusRead", StringComparison.Ordinal), "Remote gate must categorize terminal status reads.");
        Require(remoteTransport.Contains("ValidateHandshakeSessionAsync", StringComparison.Ordinal), "Remote transport must validate handshake sessions.");
        Require(remoteTransport.Contains("HostCommandCategory.DashboardRead", StringComparison.Ordinal), "Remote transport must allow dashboard read only after handshake.");
        Require(remoteTransport.Contains("HostCommandCategory.TerminalStatusRead", StringComparison.Ordinal), "Remote transport must allow terminal status read only after handshake.");
        Require(handshake.Contains("RemoteClientAllowedCategory.DashboardRead", StringComparison.Ordinal), "Handshake must advertise dashboard read capability.");
        Require(handshake.Contains("RemoteClientAllowedCategory.TerminalStatusRead", StringComparison.Ordinal), "Handshake must advertise terminal status read capability.");
        Require(appServices.Contains("CreateRemoteReadOnlyClientService", StringComparison.Ordinal), "AppServices must expose remote read-only client service factory.");

        Require(smokeProgram.Contains("safe-remote-readonly-client", StringComparison.Ordinal), "Infrastructure smoke command must be registered.");
        Require(smoke.Contains("GetRemoteClientStatusAsync", StringComparison.Ordinal), "LAN-M7 smoke must exercise client status read.");
        Require(smoke.Contains("GetDeploymentStatusAsync", StringComparison.Ordinal), "LAN-M7 smoke must exercise deployment status read.");
        Require(smoke.Contains("LookupProductAsync", StringComparison.Ordinal), "LAN-M7 smoke must exercise product read.");
        Require(smoke.Contains("GetDashboardSummaryAsync", StringComparison.Ordinal), "LAN-M7 smoke must exercise dashboard read.");
        Require(smoke.Contains("GetTerminalStatusAsync", StringComparison.Ordinal), "LAN-M7 smoke must exercise terminal status read.");
        Require(smoke.Contains("OfficialCheckoutComplete", StringComparison.Ordinal), "LAN-M7 smoke must keep official checkout rejected.");
        Require(smoke.Contains("TerminalRegistryDisable", StringComparison.Ordinal), "LAN-M7 smoke must keep admin mutation rejected.");

        RequireNoForbiddenMarkers(root);
        LanM5ControlledRemoteTransportGateChecks.Run();
        LanM6PairingActivationRemoteHandshakeChecks.Run();
    }

    private static void RequireNoForbiddenMarkers(DirectoryInfo root)
    {
        string[] sourceFiles = Directory
            .EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("bin", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("obj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (string file in sourceFiles)
        {
            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(root.FullName, file);
            Require(!text.Contains("shared SQLite over network", StringComparison.OrdinalIgnoreCase), "Shared SQLite network marker must not be introduced: " + relative);
            Require(!text.Contains("QR pairing", StringComparison.OrdinalIgnoreCase), "QR pairing marker must not be introduced: " + relative);
            Require(!text.Contains("network discovery", StringComparison.OrdinalIgnoreCase), "Network discovery marker must not be introduced: " + relative);
            Require(!text.Contains("remote checkout button", StringComparison.OrdinalIgnoreCase), "Remote checkout UI marker must not be introduced: " + relative);
            Require(!text.Contains("mobile/cloud", StringComparison.OrdinalIgnoreCase), "Mobile/cloud endpoint marker must not be introduced: " + relative);
            Require(!text.Contains("remote.sales", StringComparison.OrdinalIgnoreCase), "Remote sales mutation command must not be introduced: " + relative);
            Require(!text.Contains("remote.inventory", StringComparison.OrdinalIgnoreCase), "Remote inventory mutation command must not be introduced: " + relative);
        }
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = Path.Combine(new[] { root.FullName }.Concat(parts).ToArray());
        Require(File.Exists(path), "Expected file does not exist: " + path);
        return File.ReadAllText(path);
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

using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Tests;

public static class LanM6PairingActivationRemoteHandshakeChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string models = Read(root, "src", "Salryn.Core", "TerminalRegistry", "TerminalPairingActivationHandshakeModels.cs");
        string serviceContracts = Read(root, "src", "Salryn.Core", "Services", "ITerminalPairingActivationHandshakeServices.cs");
        string migration = Read(root, "src", "Salryn.Infrastructure", "Data", "Migrations", "TerminalCredentialHandshakeSchemaMigration.cs");
        string migrationFactory = Read(root, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs");
        string credentialRepository = Read(root, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteTerminalCredentialRepository.cs");
        string activationService = Read(root, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalPairingActivationService.cs");
        string handshakeService = Read(root, "src", "Salryn.Infrastructure", "Data", "Services", "RemoteClientHandshakeService.cs");
        string hostNames = Read(root, "src", "Salryn.Core", "Host", "SalrynHostCommandNames.cs");
        string hostPorts = Read(root, "src", "Salryn.Core", "Host", "HostCommandExecutorPorts.cs");
        string gateModels = Read(root, "src", "Salryn.Core", "Host", "LanRemoteTransportModels.cs");
        string gate = Read(root, "src", "Salryn.Core", "Host", "SalrynLanRemoteTransportGate.cs");
        string remoteTransport = Read(root, "src", "Salryn.Host.Local", "HostCommands", "SalrynHostCommandRemoteTransport.cs");
        string handlers = Read(root, "src", "Salryn.Host.Local", "HostCommands", "Handlers", "TerminalPairingActivationHandshakeHostCommandHandlers.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "LanRemoteTransportAppServices.cs");
        string infrastructureProgram = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string infrastructureSmoke = Read(root, "tools", "Salryn.Tools.InfrastructureSmoke", "PairingActivationHandshakeSmokeChecks.cs");

        Require(models.Contains("TerminalPairingActivationRequest", StringComparison.Ordinal), "LAN-M6 activation request model must exist.");
        Require(models.Contains("TerminalPairingActivationResult", StringComparison.Ordinal), "LAN-M6 activation result model must exist.");
        Require(models.Contains("RemoteClientHandshakeRequest", StringComparison.Ordinal), "LAN-M6 handshake request model must exist.");
        Require(models.Contains("RemoteClientHandshakeResult", StringComparison.Ordinal), "LAN-M6 handshake result model must exist.");
        Require(models.Contains("RemoteTerminalCredential", StringComparison.Ordinal), "LAN-M6 remote terminal credential model must exist.");
        Require(models.Contains("StatusRead = 1", StringComparison.Ordinal), "LAN-M6 allowed remote categories must include status read.");
        Require(models.Contains("ProductRead = 2", StringComparison.Ordinal), "LAN-M6 allowed remote categories must include product read.");
        Require(models.Contains("RandomNumberGenerator.GetBytes(32)", StringComparison.Ordinal), "Terminal credential generation must be high entropy.");
        Require(models.Contains("HashTerminalCredential", StringComparison.Ordinal), "Terminal credential hashing must exist.");

        Require(serviceContracts.Contains("ITerminalPairingActivationService", StringComparison.Ordinal), "Pairing activation service contract must exist.");
        Require(serviceContracts.Contains("IRemoteClientHandshakeService", StringComparison.Ordinal), "Remote client handshake service contract must exist.");
        Require(serviceContracts.Contains("ValidateHandshakeSessionAsync", StringComparison.Ordinal), "Remote handshake session validation contract must exist.");

        Require(migration.Contains("Version => 12", StringComparison.Ordinal), "LAN-M6 migration must be version 12.");
        Require(migration.Contains("salryn_terminal_credentials", StringComparison.Ordinal), "Terminal credential persistence table must exist.");
        Require(migration.Contains("credential_hash", StringComparison.Ordinal), "Credential hash column must exist.");
        Require(migration.Contains("last_used_at_utc", StringComparison.Ordinal), "Credential last-used column must exist.");
        Require(migration.Contains("revoked_at_utc", StringComparison.Ordinal), "Credential revocation column must exist.");
        Require(migration.Contains("salryn_remote_client_handshakes", StringComparison.Ordinal), "Remote client handshake table must exist.");
        Require(!migration.Contains("raw_pairing", StringComparison.OrdinalIgnoreCase), "Raw pairing secret must not be persisted.");
        Require(!migration.Contains("raw_terminal", StringComparison.OrdinalIgnoreCase), "Raw terminal credential must not be persisted.");
        Require(migrationFactory.Contains("new TerminalCredentialHandshakeSchemaMigration()", StringComparison.Ordinal), "LAN-M6 migration must be registered.");

        Require(credentialRepository.Contains("UpsertCredentialAsync", StringComparison.Ordinal), "Terminal credential write path must exist.");
        Require(credentialRepository.Contains("GetCredentialByTerminalIdAsync", StringComparison.Ordinal), "Terminal credential read path must exist.");
        Require(credentialRepository.Contains("UpdateCredentialLastUsedAsync", StringComparison.Ordinal), "Terminal credential last-used update must exist.");
        Require(credentialRepository.Contains("InsertHandshakeSessionAsync", StringComparison.Ordinal), "Handshake session persistence must exist.");
        Require(!credentialRepository.Contains("RawTerminalCredential", StringComparison.Ordinal), "Credential repository must not store raw terminal credentials.");
        Require(!credentialRepository.Contains("RawPairingCode", StringComparison.Ordinal), "Credential repository must not store raw pairing codes.");

        Require(activationService.Contains("ValidatePairingCodeAsync", StringComparison.Ordinal), "Activation must validate pairing code through existing service.");
        Require(activationService.Contains("SaveActivationAtomicallyAsync", StringComparison.Ordinal), "Activation must persist invite consumption, terminal registration, and credential atomically.");
        Require(activationService.Contains("pairing_status = $consumedStatus", StringComparison.Ordinal), "Activation must consume pairing invite.");
        Require(activationService.Contains("GenerateTerminalCredential", StringComparison.Ordinal), "Activation must issue one-time credential.");
        Require(activationService.Contains("HashTerminalCredential", StringComparison.Ordinal), "Activation must persist only terminal credential hash.");
        Require(activationService.Contains("TerminalRegistrationStatus.Active", StringComparison.Ordinal), "Activation must create active terminal registration.");
        Require(activationService.Contains("RemoteTransportEnabled", StringComparison.Ordinal), "Activation must require remote transport enabled.");

        Require(handshakeService.Contains("TerminalRegistrationStatus.PendingPairing", StringComparison.Ordinal), "Pending terminal must fail handshake.");
        Require(handshakeService.Contains("TerminalRegistrationStatus.Disabled", StringComparison.Ordinal), "Disabled terminal must fail handshake.");
        Require(handshakeService.Contains("TerminalRegistrationStatus.Revoked", StringComparison.Ordinal), "Revoked terminal must fail handshake.");
        Require(handshakeService.Contains("InvalidCredential", StringComparison.Ordinal), "Invalid credential must fail handshake.");
        Require(handshakeService.Contains("RemoteTransportEnabled", StringComparison.Ordinal), "Handshake must require remote transport enabled.");
        Require(handshakeService.Contains("SafeRemoteCategories", StringComparison.Ordinal), "Handshake must return safe categories only.");

        Require(hostNames.Contains("terminal.pairing.activate", StringComparison.Ordinal), "Pairing activation host command must exist.");
        Require(hostNames.Contains("terminal.remote.handshake", StringComparison.Ordinal), "Remote handshake host command must exist.");
        Require(hostPorts.Contains("TerminalPairingActivationHostCommandPayload", StringComparison.Ordinal), "Activation host payload must exist.");
        Require(hostPorts.Contains("RemoteClientHandshakeHostCommandPayload", StringComparison.Ordinal), "Handshake host payload must exist.");
        Require(handlers.Contains("TerminalPairingActivationHostCommandHandler", StringComparison.Ordinal), "Activation host command handler must exist.");
        Require(handlers.Contains("RemoteClientHandshakeHostCommandHandler", StringComparison.Ordinal), "Handshake host command handler must exist.");

        Require(gateModels.Contains("PairingActivation", StringComparison.Ordinal), "Remote transport categories must include pairing activation.");
        Require(gateModels.Contains("RemoteHandshake", StringComparison.Ordinal), "Remote transport categories must include remote handshake.");
        Require(gateModels.Contains("HandshakeSessionId", StringComparison.Ordinal), "Remote envelope must carry handshake session id.");
        Require(gate.Contains("PairingActivation", StringComparison.Ordinal), "Gate must allow pairing activation through controlled remote path.");
        Require(remoteTransport.Contains("ValidateHandshakeSessionAsync", StringComparison.Ordinal), "Remote transport must validate handshake sessions for safe commands.");
        Require(remoteTransport.Contains("HostCommandCategory.StatusRead or HostCommandCategory.ProductRead or HostCommandCategory.DashboardRead or HostCommandCategory.TerminalStatusRead", StringComparison.Ordinal), "Remote transport must allow only safe remote read categories after handshake.");
        Require(remoteTransport.Contains("RemoteHandshake.CategoryBlocked", StringComparison.Ordinal), "Remote transport must fail closed on mutation categories after handshake.");
        Require(remoteTransport.Contains("Only safe status, product, dashboard, and terminal status reads", StringComparison.Ordinal), "Remote transport must block remote mutation categories after handshake.");
        Require(appServices.Contains("CreateTerminalPairingActivationService", StringComparison.Ordinal), "AppServices must expose activation service factory.");
        Require(appServices.Contains("CreateRemoteClientHandshakeService", StringComparison.Ordinal), "AppServices must expose handshake service factory.");

        Require(infrastructureProgram.Contains("pairing-activation-handshake", StringComparison.Ordinal), "InfrastructureSmoke command must be registered.");
        Require(infrastructureSmoke.Contains("ActivatePairingAsync", StringComparison.Ordinal), "Infrastructure smoke must exercise activation.");
        Require(infrastructureSmoke.Contains("HandshakeAsync", StringComparison.Ordinal), "Infrastructure smoke must exercise handshake.");
        Require(infrastructureSmoke.Contains("OfficialCheckoutComplete", StringComparison.Ordinal), "Infrastructure smoke must reject official checkout remotely.");
        Require(infrastructureSmoke.Contains("OfficialReadingFinalizeZ", StringComparison.Ordinal), "Infrastructure smoke must reject Z finalize remotely.");
        Require(infrastructureSmoke.Contains("TerminalRegistryDisable", StringComparison.Ordinal), "Infrastructure smoke must reject admin mutation remotely.");

        RequireNoForbiddenLanExpansionMarkers(root);
        LanM5ControlledRemoteTransportGateChecks.Run();
    }

    private static void RequireNoForbiddenLanExpansionMarkers(DirectoryInfo root)
    {
        string[] sourceFiles = Directory
            .EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (string file in sourceFiles)
        {
            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(root.FullName, file);
            Require(!text.Contains("shared SQLite over network", StringComparison.OrdinalIgnoreCase), "Shared SQLite network marker must not be introduced: " + relative);
            Require(!text.Contains("QR pairing", StringComparison.OrdinalIgnoreCase), "QR pairing marker must not be introduced: " + relative);
            Require(!text.Contains("network discovery", StringComparison.OrdinalIgnoreCase), "Network discovery marker must not be introduced: " + relative);
            Require(!text.Contains("remote checkout button", StringComparison.OrdinalIgnoreCase), "Remote checkout UI marker must not be introduced: " + relative);
            Require(!text.Contains("remote admin button", StringComparison.OrdinalIgnoreCase), "Remote admin UI marker must not be introduced: " + relative);
            Require(!text.Contains("mobile/cloud", StringComparison.OrdinalIgnoreCase), "Mobile/cloud endpoint marker must not be introduced: " + relative);
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

public static class LanM6PairingActivationHandshakeChecks
{
    public static void Run() => LanM6PairingActivationRemoteHandshakeChecks.Run();
}

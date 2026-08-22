using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Host.Local.HostCommands;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories.System;

internal static class PairingActivationHandshakeSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== PAIRING ACTIVATION HANDSHAKE SMOKE CHECKS =====");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pairing-handshake-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);
            Result<DatabaseInitializationSummary> initResult = services.DatabaseInitialization.InitializeDatabase();
            Require(initResult.IsSuccess, "Database initialization must succeed.");

            DeploymentIdentitySnapshot identity = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity is required for LAN-M6 smoke.");

            TerminalRegistryActorContext actor = new(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                new[]
                {
                    SalrynPermissions.StoreTerminalView,
                    SalrynPermissions.StoreTerminalManage,
                    SalrynPermissions.StoreTerminalPairingIssue,
                    SalrynPermissions.StoreTerminalPairingRevoke
                });

            ILanRemoteTransportConfigService config = services.CreateLanRemoteTransportConfigService();
            Result<TerminalPairingActivationResult> blockedActivation = services.CreateTerminalPairingActivationService()
                .ActivatePairingAsync(CreateActivationRequest(
                    Guid.NewGuid(),
                    identity,
                    "invalid-before-enable",
                    "before-enable"))
                .GetAwaiter()
                .GetResult();
            Require(blockedActivation.IsSuccess && !blockedActivation.Value.Succeeded, "Remote disabled must block activation.");

            Result<LanRemoteTransportConfig> enabled = config.SaveAsync(new LanRemoteTransportConfigUpdateRequest(
                true,
                LanRemoteTransportBindMode.ExplicitLanAddress,
                "192.168.1.10",
                LanRemoteTransportConfig.DefaultPort,
                true,
                true,
                true,
                false,
                false,
                actor.ActorUserId)).GetAwaiter().GetResult();
            Require(enabled.IsSuccess, "Explicit remote transport enable must succeed.");

            Result<PairingInviteCreationResult> inviteResult = services.TerminalPairingPreparation
                .CreatePairingInviteAsync(
                    "LAN-M6 smoke client",
                    TerminalRegistrationRole.CashierClient,
                    actor,
                    TimeSpan.FromMinutes(30))
                .GetAwaiter()
                .GetResult();
            Require(inviteResult.IsSuccess, "Pairing invite creation must succeed.");

            PairingInviteCreationResult invite = inviteResult.Value;
            ITerminalPairingActivationService activation = services.CreateTerminalPairingActivationService();
            TerminalPairingActivationRequest activationRequest = CreateActivationRequest(
                invite.Invite.PairingId,
                identity,
                invite.PairingCode,
                "LAN-M6 activated client");

            TerminalPairingActivationResult activationResult = activation
                .ActivatePairingAsync(activationRequest)
                .GetAwaiter()
                .GetResult()
                .Value;
            Require(activationResult.Succeeded, "Valid pairing activation must succeed.");
            Require(activationResult.TerminalId.HasValue, "Activation must return terminal id.");
            Require(!string.IsNullOrWhiteSpace(activationResult.OneTimeTerminalCredential), "Activation must return one-time terminal credential.");

            Guid terminalId = activationResult.TerminalId.GetValueOrDefault();
            string rawCredential = activationResult.OneTimeTerminalCredential!;

            TerminalPairingActivationResult replay = activation
                .ActivatePairingAsync(activationRequest)
                .GetAwaiter()
                .GetResult()
                .Value;
            Require(!replay.Succeeded, "Consumed pairing invite replay must fail.");

            SqliteTerminalCredentialRepository credentialRepository = services.Repositories.CreateTerminalCredentialRepository();
            RemoteTerminalCredential credential = credentialRepository
                .GetCredentialByTerminalIdAsync(terminalId)
                .GetAwaiter()
                .GetResult()
                .Value
                ?? throw new InvalidOperationException("Terminal credential row must exist after activation.");
            Require(!string.Equals(credential.CredentialHash, rawCredential, StringComparison.Ordinal), "Raw credential must not be stored.");
            Require(string.Equals(credential.CredentialHash, TerminalCredentialSecretHasher.HashTerminalCredential(rawCredential), StringComparison.Ordinal), "Credential hash must match generated credential.");

            IRemoteClientHandshakeService handshakeService = services.CreateRemoteClientHandshakeService();
            RemoteClientHandshakeResult invalidHandshake = handshakeService
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    terminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    "wrong-credential",
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "lan-m6-smoke-client",
                    "lan-m6-smoke-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;
            Require(!invalidHandshake.Succeeded, "Invalid credential must fail handshake.");

            RemoteClientHandshakeResult handshake = handshakeService
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    terminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    rawCredential,
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "lan-m6-smoke-client",
                    "lan-m6-smoke-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;
            Require(handshake.Succeeded && handshake.HandshakeSessionId.HasValue, "Valid active terminal credential must handshake.");
            Guid handshakeSessionId = handshake.HandshakeSessionId.GetValueOrDefault();
            Require(handshake.AllowedRemoteCategories.Contains(RemoteClientAllowedCategory.StatusRead), "Handshake must allow status reads.");
            Require(handshake.AllowedRemoteCategories.Contains(RemoteClientAllowedCategory.ProductRead), "Handshake must allow product reads.");

            SalrynHostCommandRemoteTransport remote = services.CreateLanRemoteHostCommandTransport();
            RequireRemoteStatusSuccess(remote, identity, terminalId, handshakeSessionId);
            RequireRemoteFailure(remote, identity, terminalId, SalrynHostCommandNames.OfficialCheckoutComplete, "Official checkout command must be rejected after handshake.");
            RequireRemoteFailure(remote, identity, terminalId, SalrynHostCommandNames.OfficialLifecycleAppend, "Lifecycle command must be rejected after handshake.");
            RequireRemoteFailure(remote, identity, terminalId, SalrynHostCommandNames.OfficialReadingFinalizeZ, "Z finalize command must be rejected after handshake.");
            RequireRemoteFailure(remote, identity, terminalId, SalrynHostCommandNames.TerminalRegistryDisable, "Admin mutation must be rejected after handshake.");

            SalrynTerminalRegistration terminal = services.Repositories.CreateTerminalRegistryRepository()
                .GetByIdAsync(terminalId)
                .GetAwaiter()
                .GetResult()
                .Value
                ?? throw new InvalidOperationException("Activated terminal must exist.");
            SalrynTerminalRegistration disabled = terminal with
            {
                Status = TerminalRegistrationStatus.Disabled,
                DisabledAtUtc = DateTimeOffset.UtcNow
            };
            Require(services.Repositories.CreateTerminalRegistryRepository().UpsertAsync(disabled).GetAwaiter().GetResult().IsSuccess, "Disable seed must succeed.");
            RemoteClientHandshakeResult disabledHandshake = handshakeService
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    terminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    rawCredential,
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "lan-m6-smoke-client",
                    "lan-m6-smoke-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;
            Require(!disabledHandshake.Succeeded, "Disabled terminal must fail handshake.");

            Require(config.DisableAsync(actor.ActorUserId).GetAwaiter().GetResult().IsSuccess, "Remote disable must succeed.");
            RemoteClientHandshakeResult remoteDisabledHandshake = handshakeService
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    terminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    rawCredential,
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "lan-m6-smoke-client",
                    "lan-m6-smoke-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;
            Require(!remoteDisabledHandshake.Succeeded, "Remote disabled must block handshake.");

            Console.WriteLine("Pairing activation handshake smoke checks succeeded.");
            Console.WriteLine();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static TerminalPairingActivationRequest CreateActivationRequest(
        Guid pairingId,
        DeploymentIdentitySnapshot identity,
        string rawPairingCode,
        string terminalName) =>
        new(
            pairingId,
            identity.Store.StoreId,
            identity.Host.HostId,
            terminalName,
            TerminalRegistrationRole.CashierClient,
            rawPairingCode,
            "lan-m6-smoke-client",
            "lan-m6-smoke-fingerprint",
            DateTimeOffset.UtcNow,
            "192.168.1.50");

    private static void RequireRemoteStatusSuccess(
        SalrynHostCommandRemoteTransport remote,
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        Guid handshakeSessionId)
    {
        SalrynHostCommandResult<DeploymentStatusHostCommandResponse> result = remote
            .SendAsync<object?, DeploymentStatusHostCommandResponse>(new SalrynRemoteHostCommandEnvelope<object?>(
                "192.168.1.50",
                terminalId,
                CreateEnvelope(identity, terminalId, SalrynHostCommandNames.DeploymentStatus, null),
                handshakeSessionId))
            .GetAwaiter()
            .GetResult();

        Require(result.Succeeded && result.Response is not null, "Safe remote status read must succeed after handshake.");
    }

    private static void RequireRemoteFailure(
        SalrynHostCommandRemoteTransport remote,
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        string commandName,
        string message)
    {
        SalrynHostCommandResult<object?> result = remote
            .SendAsync<object?, object?>(new SalrynRemoteHostCommandEnvelope<object?>(
                "192.168.1.50",
                terminalId,
                CreateEnvelope(identity, terminalId, commandName, null)))
            .GetAwaiter()
            .GetResult();

        Require(!result.Succeeded, message);
    }

    private static SalrynHostCommandEnvelope<object?> CreateEnvelope(
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        string commandName,
        object? payload) =>
        new(
            commandName,
            Guid.NewGuid(),
            identity.Store.StoreId,
            identity.Host.HostId,
            terminalId,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            null,
            DateTimeOffset.UtcNow,
            InstallationRole.ClientTerminal,
            InstallationRole.ClientTerminal,
            payload);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

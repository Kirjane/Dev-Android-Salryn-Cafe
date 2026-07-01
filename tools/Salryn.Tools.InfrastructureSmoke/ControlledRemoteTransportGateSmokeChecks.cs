using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;
using Salryn.Host.Local.HostCommands;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories.System;

internal static class ControlledRemoteTransportGateSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== CONTROLLED REMOTE TRANSPORT GATE SMOKE CHECKS =====");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-controlled-remote-transport-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);
            Result<DatabaseInitializationSummary> initResult = services.DatabaseInitialization.InitializeDatabase();

            if (initResult.IsFailure)
            {
                throw new InvalidOperationException("Controlled remote transport smoke failed to initialize database.");
            }

            DeploymentIdentitySnapshot identity = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity is required for controlled remote transport smoke.");

            ILanRemoteTransportConfigService config = services.CreateLanRemoteTransportConfigService();
            SalrynHostCommandRemoteTransport remote = services.CreateLanRemoteHostCommandTransport();
            SqliteTerminalRegistryRepository terminalRegistry = services.Repositories.CreateTerminalRegistryRepository();
            SqliteTerminalCredentialRepository terminalCredentials = services.Repositories.CreateTerminalCredentialRepository();

            Guid activeTerminalId = Guid.Parse("66666666-0000-0000-0000-000000000001");
            Guid pendingTerminalId = Guid.Parse("66666666-0000-0000-0000-000000000002");
            Guid disabledTerminalId = Guid.Parse("66666666-0000-0000-0000-000000000003");
            Guid revokedTerminalId = Guid.Parse("66666666-0000-0000-0000-000000000004");

            UpsertTerminal(terminalRegistry, identity, activeTerminalId, TerminalRegistrationStatus.Active);
            UpsertTerminal(terminalRegistry, identity, pendingTerminalId, TerminalRegistrationStatus.PendingPairing);
            UpsertTerminal(terminalRegistry, identity, disabledTerminalId, TerminalRegistrationStatus.Disabled);
            UpsertTerminal(terminalRegistry, identity, revokedTerminalId, TerminalRegistrationStatus.Revoked);

            SalrynHostCommandEnvelope<object?> loopbackEnvelope = CreateEnvelope(identity, activeTerminalId, SalrynHostCommandNames.DeploymentStatus, null);
            SalrynHostCommandResult<DeploymentStatusHostCommandResponse> loopback = services.HostCommands.Transport
                .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                    SalrynHostCommandLoopbackRules.LoopbackHost,
                    loopbackEnvelope)
                .GetAwaiter()
                .GetResult();

            Require(loopback.Succeeded && loopback.Response is not null, "Loopback transport must still work.");

            RequireRemoteFailure(remote, identity, activeTerminalId, SalrynHostCommandNames.DeploymentStatus, "Remote disabled must reject remote origin.");

            Result<LanRemoteTransportConfig> invalid = config.SaveAsync(new LanRemoteTransportConfigUpdateRequest(
                true,
                LanRemoteTransportBindMode.ExplicitLanAddress,
                "0.0.0.0",
                LanRemoteTransportConfig.DefaultPort,
                true,
                true,
                true,
                false,
                false,
                Guid.Parse("00000000-0000-0000-0000-000000000001"))).GetAwaiter().GetResult();

            Require(invalid.IsFailure, "0.0.0.0 must be rejected.");

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
                Guid.Parse("00000000-0000-0000-0000-000000000001"))).GetAwaiter().GetResult();

            Require(enabled.IsSuccess, "Remote enabled explicit LAN config must save.");

            string rawCredential = TerminalCredentialSecretHasher.GenerateTerminalCredential();
            RemoteTerminalCredential credential = new(
                activeTerminalId,
                identity.Store.StoreId,
                identity.Host.HostId,
                TerminalCredentialSecretHasher.HashTerminalCredential(rawCredential),
                DateTimeOffset.UtcNow,
                LastUsedAtUtc: null,
                RevokedAtUtc: null,
                CreatedFromPairingId: null,
                CreatedByUserId: Guid.Parse("00000000-0000-0000-0000-000000000001"));

            Require(terminalCredentials.UpsertCredentialAsync(credential).GetAwaiter().GetResult().IsSuccess, "Terminal credential seed must succeed.");

            RemoteClientHandshakeResult handshake = services.CreateRemoteClientHandshakeService()
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    activeTerminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    rawCredential,
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "controlled-remote-smoke",
                    "controlled-remote-smoke-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;

            Require(handshake.Succeeded && handshake.HandshakeSessionId.HasValue, "Remote handshake must succeed for active credential.");
            Guid handshakeSessionId = handshake.HandshakeSessionId.GetValueOrDefault();

            RequireRemoteSuccess(remote, identity, activeTerminalId, SalrynHostCommandNames.DeploymentStatus, handshakeSessionId);
            RequireRemoteFailure(remote, identity, activeTerminalId, SalrynHostCommandNames.OfficialCheckoutComplete, "Official mutation must stay blocked remotely.");
            RequireRemoteFailure(remote, identity, pendingTerminalId, SalrynHostCommandNames.DeploymentStatus, "Pending terminal must be rejected.");
            RequireRemoteFailure(remote, identity, disabledTerminalId, SalrynHostCommandNames.DeploymentStatus, "Disabled terminal must be rejected.");
            RequireRemoteFailure(remote, identity, revokedTerminalId, SalrynHostCommandNames.DeploymentStatus, "Revoked terminal must be rejected.");
            RequireRemoteFailure(remote, identity, Guid.Parse("66666666-0000-0000-0000-000000000099"), SalrynHostCommandNames.DeploymentStatus, "Unknown terminal must be rejected.");

            Console.WriteLine("Controlled remote transport gate smoke checks succeeded.");
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

    private static void RequireRemoteSuccess(
        SalrynHostCommandRemoteTransport remote,
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        string commandName,
        Guid handshakeSessionId)
    {
        SalrynHostCommandResult<DeploymentStatusHostCommandResponse> result = remote
            .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                new SalrynRemoteHostCommandEnvelope<object?>(
                    "192.168.1.50",
                    terminalId,
                    CreateEnvelope(identity, terminalId, commandName, null),
                    handshakeSessionId))
            .GetAwaiter()
            .GetResult();

        Require(result.Succeeded && result.Response is not null, "Remote enabled active terminal must allow safe status/read command after handshake.");
    }

    private static void RequireRemoteFailure(
        SalrynHostCommandRemoteTransport remote,
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        string commandName,
        string message)
    {
        SalrynHostCommandResult<DeploymentStatusHostCommandResponse> result = remote
            .SendAsync<object?, DeploymentStatusHostCommandResponse>(
                new SalrynRemoteHostCommandEnvelope<object?>(
                    "192.168.1.50",
                    terminalId,
                    CreateEnvelope(identity, terminalId, commandName, null)))
            .GetAwaiter()
            .GetResult();

        Require(!result.Succeeded, message);
    }

    private static void UpsertTerminal(
        SqliteTerminalRegistryRepository repository,
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        TerminalRegistrationStatus status)
    {
        SalrynTerminalRegistration registration = new(
            terminalId,
            identity.Store.StoreId,
            identity.Host.HostId,
            "LAN-M5 smoke terminal",
            TerminalRegistrationRole.CashierClient,
            status,
            DateTimeOffset.UtcNow,
            null,
            status == TerminalRegistrationStatus.Disabled ? DateTimeOffset.UtcNow : null,
            status == TerminalRegistrationStatus.Revoked ? DateTimeOffset.UtcNow : null,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            null);

        Require(repository.UpsertAsync(registration).GetAwaiter().GetResult().IsSuccess, "Terminal registration seed must succeed.");
    }

    private static SalrynHostCommandEnvelope<object?> CreateEnvelope(
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        string commandName,
        object? payload)
    {
        return new SalrynHostCommandEnvelope<object?>(
            CommandName: commandName,
            RequestId: Guid.NewGuid(),
            StoreId: identity.Store.StoreId,
            HostId: identity.Host.HostId,
            TerminalId: terminalId,
            UserId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            SessionId: null,
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceMachineRole: InstallationRole.ClientTerminal,
            InstallationRole: InstallationRole.ClientTerminal,
            Payload: payload);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

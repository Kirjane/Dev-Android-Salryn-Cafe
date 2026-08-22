using Salryn.Api.Local.Services;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;
using Salryn.Host.Local.HostCommands;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories.System;

internal static class LanM14EndToEndRemoteReadOnlyLanClosureSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M14 END-TO-END REMOTE READ-ONLY LAN CLOSURE SMOKE CHECKS =====");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-lan-m14-closure-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);
            Result<DatabaseInitializationSummary> initResult = services.DatabaseInitialization.InitializeDatabase();

            if (initResult.IsFailure)
            {
                throw new InvalidOperationException("LAN-M14 closure smoke failed to initialize database.");
            }

            DeploymentIdentitySnapshot identity = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity is required for LAN-M14 closure smoke.");

            ILanRemoteTransportConfigService config = services.CreateLanRemoteTransportConfigService();
            SalrynHostCommandRemoteTransport remote = services.CreateLanRemoteHostCommandTransport();
            SqliteTerminalRegistryRepository terminalRegistry = services.Repositories.CreateTerminalRegistryRepository();
            SqliteTerminalCredentialRepository terminalCredentials = services.Repositories.CreateTerminalCredentialRepository();

            Result<LanRemoteTransportConfig> defaultConfig = config.GetCurrentAsync().GetAwaiter().GetResult();
            Require(defaultConfig.IsSuccess, "Default LAN remote transport config must load.");
            Require(!defaultConfig.Value.RemoteTransportEnabled, "Remote transport must be disabled by default.");

            IReadOnlyList<string> disabledUrls = LocalApiLanUrlResolver.ResolveListenUrls(
                "http://127.0.0.1:5299",
                defaultConfig.Value);
            Require(disabledUrls.Count == 1, "LocalApiLanUrlResolver must return only localhost when disabled.");
            Require(disabledUrls[0] == "http://127.0.0.1:5299", "LocalApiLanUrlResolver must keep 127.0.0.1:5299 when disabled.");

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

            Require(enabled.IsSuccess, "Explicit 192.168.1.10 LAN config must save.");

            IReadOnlyList<string> enabledUrls = LocalApiLanUrlResolver.ResolveListenUrls(
                "http://127.0.0.1:5299",
                enabled.Value);
            Require(enabledUrls.Count == 2, "LocalApiLanUrlResolver must add explicit LAN URL when enabled.");
            Require(enabledUrls.Contains("http://192.168.1.10:5299", StringComparer.OrdinalIgnoreCase), "LocalApiLanUrlResolver must include explicit LAN listen URL.");

            IReadOnlyList<string> ephemeralHarnessUrls = LocalApiLanUrlResolver.ResolveListenUrls(
                "http://127.0.0.1:0",
                enabled.Value);
            Require(ephemeralHarnessUrls.Count == 1, "Ephemeral loopback test harness URLs must not add a persisted LAN listener.");
            Require(ephemeralHarnessUrls[0] == "http://127.0.0.1:0", "Ephemeral loopback test harness URL must be preserved.");

            Guid activeTerminalId = Guid.Parse("77777777-0000-0000-0000-000000000001");
            UpsertTerminal(terminalRegistry, identity, activeTerminalId, TerminalRegistrationStatus.Active);

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
                    "lan-m14-closure-smoke",
                    "lan-m14-closure-smoke-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;

            Require(handshake.Succeeded && handshake.HandshakeSessionId.HasValue, "Pairing handshake must succeed for active credential.");
            Guid handshakeSessionId = handshake.HandshakeSessionId.GetValueOrDefault();

            RequireRemoteSuccess(remote, identity, activeTerminalId, SalrynHostCommandNames.DeploymentStatus, handshakeSessionId);
            RequireRemoteFailure(remote, identity, activeTerminalId, SalrynHostCommandNames.OfficialCheckoutComplete, "Official checkout must stay blocked after handshake.");

            Console.WriteLine("LAN-M14 end-to-end remote read-only LAN closure smoke checks succeeded.");
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

        Require(result.Succeeded && result.Response is not null, "Enabled active terminal must allow safe read command after handshake.");
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
            "LAN-M14 closure smoke terminal",
            TerminalRegistrationRole.CashierClient,
            status,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
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

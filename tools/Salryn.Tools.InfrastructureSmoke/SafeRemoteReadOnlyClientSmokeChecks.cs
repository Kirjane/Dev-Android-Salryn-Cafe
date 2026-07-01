using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.RemoteClients;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data.Initialization;

internal static class SafeRemoteReadOnlyClientSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== SAFE REMOTE READ-ONLY CLIENT SMOKE CHECKS =====");

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-safe-remote-readonly-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);
            Require(services.DatabaseInitialization.InitializeDatabase().IsSuccess, "Database initialization must succeed.");

            DeploymentIdentitySnapshot identity = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity is required for LAN-M7 smoke.");

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
            Require(config.SaveAsync(new LanRemoteTransportConfigUpdateRequest(
                true,
                LanRemoteTransportBindMode.ExplicitLanAddress,
                "192.168.1.10",
                LanRemoteTransportConfig.DefaultPort,
                true,
                true,
                true,
                false,
                false,
                actor.ActorUserId)).GetAwaiter().GetResult().IsSuccess,
                "Explicit remote transport enable must succeed.");

            PairingInviteCreationResult invite = services.TerminalPairingPreparation
                .CreatePairingInviteAsync(
                    "LAN-M7 read-only smoke client",
                    TerminalRegistrationRole.CashierClient,
                    actor,
                    TimeSpan.FromMinutes(30))
                .GetAwaiter()
                .GetResult()
                .Value;

            TerminalPairingActivationResult activation = services.CreateTerminalPairingActivationService()
                .ActivatePairingAsync(new TerminalPairingActivationRequest(
                    invite.Invite.PairingId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    "LAN-M7 read-only activated client",
                    TerminalRegistrationRole.CashierClient,
                    invite.PairingCode,
                    "lan-m7-readonly-client",
                    "lan-m7-readonly-fingerprint",
                    DateTimeOffset.UtcNow,
                    "192.168.1.50"))
                .GetAwaiter()
                .GetResult()
                .Value;

            Require(activation.Succeeded && activation.TerminalId.HasValue, "Pairing activation must succeed.");
            Guid terminalId = activation.TerminalId.GetValueOrDefault();

            RemoteClientHandshakeResult handshake = services.CreateRemoteClientHandshakeService()
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    terminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    activation.OneTimeTerminalCredential!,
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "lan-m7-readonly-client",
                    "lan-m7-readonly-fingerprint"))
                .GetAwaiter()
                .GetResult()
                .Value;

            Require(handshake.Succeeded && handshake.HandshakeSessionId.HasValue, "Remote handshake must succeed.");
            Require(handshake.AllowedRemoteCategories.Contains(RemoteClientAllowedCategory.StatusRead), "Handshake must allow status read.");
            Require(handshake.AllowedRemoteCategories.Contains(RemoteClientAllowedCategory.ProductRead), "Handshake must allow product read.");
            Require(handshake.AllowedRemoteCategories.Contains(RemoteClientAllowedCategory.DashboardRead), "Handshake must allow dashboard read.");
            Require(handshake.AllowedRemoteCategories.Contains(RemoteClientAllowedCategory.TerminalStatusRead), "Handshake must allow terminal status read.");

            RemoteReadOnlyClientContext context = new(
                terminalId,
                identity.Store.StoreId,
                identity.Host.HostId,
                handshake.HandshakeSessionId.GetValueOrDefault(),
                "192.168.1.50",
                DateTimeOffset.UtcNow,
                "lan-m7-readonly-client",
                "lan-m7-readonly-fingerprint");

            IRemoteReadOnlyClientService readOnly = services.CreateRemoteReadOnlyClientService();
            Require(readOnly.GetRemoteClientStatusAsync(context).GetAwaiter().GetResult().Value.Succeeded, "Client status read must succeed.");
            Require(readOnly.GetDeploymentStatusAsync(context).GetAwaiter().GetResult().Value.Succeeded, "Deployment status read must succeed.");
            Require(readOnly.LookupProductAsync(new RemoteReadOnlyClientRequest<ProductLookupHostCommandRequest>(
                    Guid.NewGuid(),
                    context,
                    new ProductLookupHostCommandRequest("779900000001")))
                .GetAwaiter().GetResult().Value.Succeeded,
                "Product lookup read must succeed.");
            Require(readOnly.GetDashboardSummaryAsync(context).GetAwaiter().GetResult().Value.Succeeded, "Dashboard read must succeed.");
            Require(readOnly.GetTerminalStatusAsync(context).GetAwaiter().GetResult().Value.Succeeded, "Terminal status read must succeed.");

            Require(!readOnly.GetRemoteClientStatusAsync(context with { HandshakeSessionId = Guid.Empty }).GetAwaiter().GetResult().Value.Succeeded,
                "Missing session must fail.");
            Require(!readOnly.GetRemoteClientStatusAsync(context with { HandshakeSessionId = Guid.NewGuid() }).GetAwaiter().GetResult().Value.Succeeded,
                "Invalid session must fail.");

            RequireRemoteFailure(services, identity, terminalId, SalrynHostCommandNames.OfficialCheckoutComplete, "Official checkout must stay blocked.");
            RequireRemoteFailure(services, identity, terminalId, SalrynHostCommandNames.TerminalRegistryDisable, "Admin command must stay blocked.");

            SalrynTerminalRegistration terminal = services.Repositories.CreateTerminalRegistryRepository()
                .GetByIdAsync(terminalId)
                .GetAwaiter().GetResult().Value!;
            Require(services.Repositories.CreateTerminalRegistryRepository()
                .UpsertAsync(terminal with { Status = TerminalRegistrationStatus.Disabled, DisabledAtUtc = DateTimeOffset.UtcNow })
                .GetAwaiter().GetResult().IsSuccess,
                "Disabled terminal seed must succeed.");
            Require(!readOnly.GetRemoteClientStatusAsync(context).GetAwaiter().GetResult().Value.Succeeded,
                "Disabled terminal must fail read-only client status.");

            Require(config.DisableAsync(actor.ActorUserId).GetAwaiter().GetResult().IsSuccess, "Remote disable must succeed.");
            Require(!readOnly.GetDeploymentStatusAsync(context).GetAwaiter().GetResult().Value.Succeeded,
                "Remote disabled must block read-only operations.");

            Console.WriteLine("Safe remote read-only client smoke checks succeeded.");
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

    private static void RequireRemoteFailure(
        SalrynAppServices services,
        DeploymentIdentitySnapshot identity,
        Guid terminalId,
        string commandName,
        string message)
    {
        var result = services.CreateLanRemoteHostCommandTransport()
            .SendAsync<object?, object?>(new SalrynRemoteHostCommandEnvelope<object?>(
                "192.168.1.50",
                terminalId,
                new SalrynHostCommandEnvelope<object?>(
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
                    null)))
            .GetAwaiter()
            .GetResult();

        Require(!result.Succeeded, message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

using Salryn.App.WinForms.HostCommands;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Services;
using Salryn.Host.Local.HostCommands;

namespace Salryn.App.WinForms.Composition;

public static class LanRemoteTransportAppServices
{
    public static ILanRemoteTransportConfigService CreateLanRemoteTransportConfigService(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new LanRemoteTransportConfigService(services.ConnectionFactory);
    }

    public static ITerminalPairingActivationService CreateTerminalPairingActivationService(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ILanRemoteTransportConfigService config = services.CreateLanRemoteTransportConfigService();
        return new TerminalPairingActivationService(
            services.TerminalPairingPreparation,
            services.ConnectionFactory,
            services.Repositories.CreateTerminalRegistryRepository(),
            services.Repositories.CreateTerminalCredentialRepository(),
            config);
    }

    public static IRemoteClientHandshakeService CreateRemoteClientHandshakeService(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ILanRemoteTransportConfigService config = services.CreateLanRemoteTransportConfigService();
        return new RemoteClientHandshakeService(
            services.Repositories.CreateTerminalRegistryRepository(),
            services.Repositories.CreateTerminalCredentialRepository(),
            config);
    }

    public static IRemoteReadOnlyClientService CreateRemoteReadOnlyClientService(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new RemoteReadOnlyClientService(
            new ControlledRemoteHostCommandTransport(services.CreateLanRemoteHostCommandTransport()));
    }

    public static ILanRemoteTransportGate CreateLanRemoteTransportGate(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ILanRemoteTransportConfigService config = services.CreateLanRemoteTransportConfigService();

        return new SalrynLanRemoteTransportGate(
            cancellationToken => config.GetCurrentAsync(cancellationToken),
            (terminalId, cancellationToken) => services.Repositories
                .CreateTerminalRegistryRepository()
                .GetByIdAsync(terminalId, cancellationToken));
    }

    public static SalrynHostCommandRemoteTransport CreateLanRemoteHostCommandTransport(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ITerminalPairingActivationService activation = services.CreateTerminalPairingActivationService();
        IRemoteClientHandshakeService handshake = services.CreateRemoteClientHandshakeService();
        SalrynHostCommandAuthorityGuard hostCommandAuthority = new(services.StoreHostAuthority);

        IHostCommandDispatcher remoteDispatcher = RemoteReadOnlyHostCommandComposition.CreateDispatcher(
            hostCommandAuthority,
            () => ResolveDeploymentIdentitySnapshot(services),
            HostCommandExecutorAdapters.CreateDeploymentStatusExecutor(services.DeploymentIdentity),
            HostCommandExecutorAdapters.CreateProductLookupExecutor(services.ProductReadRepository),
            HostCommandExecutorAdapters.CreateTerminalPairingActivationExecutor(activation),
            HostCommandExecutorAdapters.CreateRemoteClientHandshakeExecutor(handshake),
            new RemoteClientStatusHostCommandExecutor(),
            new RemoteDashboardSummaryHostCommandExecutor(),
            new RemoteTerminalStatusHostCommandExecutor(services.TerminalRegistry),
            services.TerminalRegistry);

        return new SalrynHostCommandRemoteTransport(
            remoteDispatcher,
            services.CreateLanRemoteTransportGate(),
            handshake);
    }

    private static DeploymentIdentitySnapshot? ResolveDeploymentIdentitySnapshot(SalrynAppServices services)
    {
        var result = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult();
        return result.IsSuccess ? result.Value : null;
    }
}

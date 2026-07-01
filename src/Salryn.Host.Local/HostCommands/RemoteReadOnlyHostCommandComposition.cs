using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Host.Local.HostCommands.Handlers;

namespace Salryn.Host.Local.HostCommands;

public static class RemoteReadOnlyHostCommandComposition
{
    public static IHostCommandDispatcher CreateDispatcher(
        SalrynHostCommandAuthorityGuard authorityGuard,
        Func<DeploymentIdentitySnapshot?> identityProvider,
        IDeploymentStatusHostCommandExecutor deploymentStatusExecutor,
        IProductLookupHostCommandExecutor productLookupExecutor,
        ITerminalPairingActivationHostCommandExecutor terminalPairingActivationExecutor,
        IRemoteClientHandshakeHostCommandExecutor remoteClientHandshakeExecutor,
        IRemoteClientStatusHostCommandExecutor remoteClientStatusExecutor,
        IRemoteDashboardSummaryHostCommandExecutor remoteDashboardSummaryExecutor,
        IRemoteTerminalStatusHostCommandExecutor remoteTerminalStatusExecutor,
        ITerminalRegistryService terminalRegistryService)
    {
        IHostCommandHandler[] handlers =
        {
            new DeploymentStatusHostCommandHandler(deploymentStatusExecutor, identityProvider),
            new ProductLookupHostCommandHandler(productLookupExecutor),
            new TerminalPairingActivationHostCommandHandler(terminalPairingActivationExecutor),
            new RemoteClientHandshakeHostCommandHandler(remoteClientHandshakeExecutor),
            new RemoteClientStatusHostCommandHandler(remoteClientStatusExecutor),
            new RemoteDeploymentStatusHostCommandHandler(deploymentStatusExecutor, identityProvider),
            new RemoteProductLookupHostCommandHandler(productLookupExecutor),
            new RemoteDashboardSummaryHostCommandHandler(remoteDashboardSummaryExecutor),
            new RemoteTerminalStatusHostCommandHandler(remoteTerminalStatusExecutor)
        };

        return new HostCommandDispatcher(
            handlers,
            authorityGuard,
            identityProvider,
            terminalRegistryService);
    }
}

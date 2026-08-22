using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Host.Local.HostCommands.Handlers;

namespace Salryn.Host.Local.HostCommands;

public static class HostCommandComposition
{
    public static SalrynHostCommandLoopbackSurface Create(
        SalrynHostCommandAuthorityGuard authorityGuard,
        Func<DeploymentIdentitySnapshot?> identityProvider,
        IDeploymentStatusHostCommandExecutor deploymentStatusExecutor,
        IProductLookupHostCommandExecutor productLookupExecutor,
        IOfficialCheckoutHostCommandExecutor officialCheckoutExecutor,
        IOfficialLifecycleHostCommandExecutor officialLifecycleExecutor,
        IOfficialReadingPreviewXHostCommandExecutor officialPreviewXExecutor,
        IOfficialReadingFinalizeZHostCommandExecutor officialFinalizeZExecutor,
        IBackupAuthorityHostCommandExecutor backupAuthorityExecutor,
        ITerminalRegistryListHostCommandExecutor terminalRegistryListExecutor,
        ITerminalRegistryDisableHostCommandExecutor terminalRegistryDisableExecutor,
        ITerminalRegistryRevokeHostCommandExecutor terminalRegistryRevokeExecutor,
        ITerminalPairingIssueHostCommandExecutor terminalPairingIssueExecutor,
        ITerminalPairingRevokeHostCommandExecutor terminalPairingRevokeExecutor,
        ITerminalPairingListHostCommandExecutor terminalPairingListExecutor,
        ITerminalRegistryService terminalRegistryService,
        ITerminalPairingActivationHostCommandExecutor? terminalPairingActivationExecutor = null,
        IRemoteClientHandshakeHostCommandExecutor? remoteClientHandshakeExecutor = null)
    {
        List<IHostCommandHandler> handlers = new()
        {
            new DeploymentStatusHostCommandHandler(deploymentStatusExecutor, identityProvider),
            new ProductLookupHostCommandHandler(productLookupExecutor),
            new OfficialCheckoutHostCommandHandler(officialCheckoutExecutor),
            new OfficialLifecycleHostCommandHandler(officialLifecycleExecutor),
            new OfficialReadingPreviewXHostCommandHandler(officialPreviewXExecutor),
            new OfficialReadingFinalizeZHostCommandHandler(officialFinalizeZExecutor),
            new BackupAuthorityHostCommandHandler(backupAuthorityExecutor),
            new TerminalRegistryListHostCommandHandler(terminalRegistryListExecutor),
            new TerminalRegistryDisableHostCommandHandler(terminalRegistryDisableExecutor),
            new TerminalRegistryRevokeHostCommandHandler(terminalRegistryRevokeExecutor),
            new TerminalPairingIssueHostCommandHandler(terminalPairingIssueExecutor),
            new TerminalPairingRevokeHostCommandHandler(terminalPairingRevokeExecutor),
            new TerminalPairingListHostCommandHandler(terminalPairingListExecutor)
        };

        if (terminalPairingActivationExecutor is not null)
        {
            handlers.Add(new TerminalPairingActivationHostCommandHandler(terminalPairingActivationExecutor));
        }

        if (remoteClientHandshakeExecutor is not null)
        {
            handlers.Add(new RemoteClientHandshakeHostCommandHandler(remoteClientHandshakeExecutor));
        }

        HostCommandDispatcher dispatcher = new(
            handlers,
            authorityGuard,
            identityProvider,
            terminalRegistryService);
        SalrynHostCommandLoopbackTransport transport = new(dispatcher, authorityGuard, identityProvider);

        return new SalrynHostCommandLoopbackSurface(dispatcher, transport);
    }
}

public sealed record SalrynHostCommandLoopbackSurface(
    IHostCommandDispatcher Dispatcher,
    SalrynHostCommandLoopbackTransport Transport);

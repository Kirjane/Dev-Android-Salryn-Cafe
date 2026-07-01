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

        SalrynHostCommandLoopbackSurface remoteSurface = HostCommandComposition.Create(
            hostCommandAuthority,
            () => ResolveDeploymentIdentitySnapshot(services),
            HostCommandExecutorAdapters.CreateDeploymentStatusExecutor(services.DeploymentIdentity),
            HostCommandExecutorAdapters.CreateProductLookupExecutor(services.ProductReadRepository),
            HostCommandExecutorAdapters.CreateOfficialCheckoutExecutor(services.OfficialCheckoutCompletion),
            HostCommandExecutorAdapters.CreateOfficialLifecycleExecutor(services.OfficialDocumentLifecycleOperations),
            HostCommandExecutorAdapters.CreateOfficialPreviewXExecutor(services.OfficialXReading),
            HostCommandExecutorAdapters.CreateOfficialFinalizeZExecutor(services.OfficialZReading),
            HostCommandExecutorAdapters.CreateBackupAuthorityExecutor(),
            HostCommandExecutorAdapters.CreateTerminalRegistryListExecutor(services.TerminalRegistry),
            HostCommandExecutorAdapters.CreateTerminalRegistryDisableExecutor(services.TerminalRegistry),
            HostCommandExecutorAdapters.CreateTerminalRegistryRevokeExecutor(services.TerminalRegistry),
            HostCommandExecutorAdapters.CreateTerminalPairingIssueExecutor(services.TerminalPairingPreparation),
            HostCommandExecutorAdapters.CreateTerminalPairingRevokeExecutor(services.TerminalPairingPreparation),
            HostCommandExecutorAdapters.CreateTerminalPairingListExecutor(services.TerminalPairingPreparation),
            services.TerminalRegistry,
            HostCommandExecutorAdapters.CreateTerminalPairingActivationExecutor(activation),
            HostCommandExecutorAdapters.CreateRemoteClientHandshakeExecutor(handshake));

        return new SalrynHostCommandRemoteTransport(
            remoteSurface.Dispatcher,
            services.CreateLanRemoteTransportGate(),
            handshake);
    }

    private static DeploymentIdentitySnapshot? ResolveDeploymentIdentitySnapshot(SalrynAppServices services)
    {
        var result = services.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult();
        return result.IsSuccess ? result.Value : null;
    }
}

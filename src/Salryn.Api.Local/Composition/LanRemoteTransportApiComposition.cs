using Salryn.Api.Local.HostCommands;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Host.Local.HostCommands;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local.Composition;

public sealed class LanRemoteTransportApiServices
{
    public LanRemoteTransportApiServices(
        ILanRemoteTransportConfigService config,
        IDeploymentIdentityService deploymentIdentity,
        ITerminalRegistryService terminalRegistry,
        ITerminalPairingPreparationService terminalPairingPreparation,
        ITerminalPairingActivationService terminalPairingActivation,
        IRemoteClientHandshakeService handshake,
        ILanRemoteTransportGate gate,
        IRemoteReadOnlyClientService remoteReadOnlyClient,
        IAuditWriteRepository auditWriteRepository,
        RemoteReadOnlyReadAttemptAuditRecorder readAttemptAudit)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        DeploymentIdentity = deploymentIdentity ?? throw new ArgumentNullException(nameof(deploymentIdentity));
        TerminalRegistry = terminalRegistry ?? throw new ArgumentNullException(nameof(terminalRegistry));
        TerminalPairingPreparation = terminalPairingPreparation
            ?? throw new ArgumentNullException(nameof(terminalPairingPreparation));
        TerminalPairingActivation = terminalPairingActivation
            ?? throw new ArgumentNullException(nameof(terminalPairingActivation));
        Handshake = handshake ?? throw new ArgumentNullException(nameof(handshake));
        Gate = gate ?? throw new ArgumentNullException(nameof(gate));
        RemoteReadOnlyClient = remoteReadOnlyClient
            ?? throw new ArgumentNullException(nameof(remoteReadOnlyClient));
        AuditWriteRepository = auditWriteRepository
            ?? throw new ArgumentNullException(nameof(auditWriteRepository));
        ReadAttemptAudit = readAttemptAudit ?? throw new ArgumentNullException(nameof(readAttemptAudit));
    }

    public ILanRemoteTransportConfigService Config { get; }

    public IDeploymentIdentityService DeploymentIdentity { get; }

    public ITerminalRegistryService TerminalRegistry { get; }

    public ITerminalPairingPreparationService TerminalPairingPreparation { get; }

    public ITerminalPairingActivationService TerminalPairingActivation { get; }

    public IRemoteClientHandshakeService Handshake { get; }

    public ILanRemoteTransportGate Gate { get; }

    public IRemoteReadOnlyClientService RemoteReadOnlyClient { get; }

    public IAuditWriteRepository AuditWriteRepository { get; }

    public RemoteReadOnlyReadAttemptAuditRecorder ReadAttemptAudit { get; }
}

public static class LanRemoteTransportApiComposition
{
    public static LanRemoteTransportApiServices Create(SalrynLocalComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);

        LanRemoteTransportConfigService configService = new(composition.ConnectionFactory);
        DeploymentIdentityService deploymentIdentity = new(repositories.CreateDeploymentIdentityRepository());

        SalrynStoreHostAuthorityGuard storeHostAuthority = new(() =>
        {
            Result<DeploymentIdentitySnapshot?> identityResult = deploymentIdentity
                .GetCurrentAsync()
                .GetAwaiter()
                .GetResult();

            return identityResult.IsSuccess ? identityResult.Value : null;
        });

        TerminalRegistryService terminalRegistry = new(
            repositories.CreateTerminalRegistryRepository(),
            deploymentIdentity,
            storeHostAuthority);

        TerminalPairingPreparationService terminalPairingPreparation = new(
            repositories.CreateTerminalPairingInviteRepository(),
            deploymentIdentity,
            storeHostAuthority);

        TerminalPairingActivationService terminalPairingActivation = new(
            terminalPairingPreparation,
            composition.ConnectionFactory,
            repositories.CreateTerminalRegistryRepository(),
            repositories.CreateTerminalCredentialRepository(),
            configService);

        RemoteClientHandshakeService handshake = new(
            repositories.CreateTerminalRegistryRepository(),
            repositories.CreateTerminalCredentialRepository(),
            configService);

        SalrynLanRemoteTransportGate gate = new(
            cancellationToken => configService.GetCurrentAsync(cancellationToken),
            (terminalId, cancellationToken) => repositories
                .CreateTerminalRegistryRepository()
                .GetByIdAsync(terminalId, cancellationToken));

        SalrynHostCommandAuthorityGuard hostCommandAuthority = new(storeHostAuthority);

        IHostCommandDispatcher remoteDispatcher = RemoteReadOnlyHostCommandComposition.CreateDispatcher(
            hostCommandAuthority,
            () => ResolveDeploymentIdentitySnapshot(deploymentIdentity),
            new ApiDeploymentStatusHostCommandExecutor(deploymentIdentity),
            new ApiProductLookupHostCommandExecutor(composition.ProductReadRepository),
            new ApiTerminalPairingActivationHostCommandExecutor(terminalPairingActivation),
            new ApiRemoteClientHandshakeHostCommandExecutor(handshake),
            new ApiRemoteClientStatusHostCommandExecutor(),
            new ApiRemoteDashboardSummaryHostCommandExecutor(),
            new ApiRemoteTerminalStatusHostCommandExecutor(terminalRegistry),
            terminalRegistry);

        SalrynHostCommandRemoteTransport remoteTransport = new(remoteDispatcher, gate, handshake);
        ControlledRemoteHostCommandTransport controlledTransport = new(remoteTransport);
        RemoteReadOnlyClientService remoteReadOnlyClient = new(controlledTransport);

        IAuditWriteRepository auditWriteRepository = repositories.CreateAuditWriteRepository();
        RemoteReadOnlyReadAttemptAuditRecorder readAttemptAudit = new(auditWriteRepository);

        return new LanRemoteTransportApiServices(
            configService,
            deploymentIdentity,
            terminalRegistry,
            terminalPairingPreparation,
            terminalPairingActivation,
            handshake,
            gate,
            remoteReadOnlyClient,
            auditWriteRepository,
            readAttemptAudit);
    }

    private static DeploymentIdentitySnapshot? ResolveDeploymentIdentitySnapshot(
        IDeploymentIdentityService deploymentIdentity)
    {
        Result<DeploymentIdentitySnapshot?> result = deploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult();
        return result.IsSuccess ? result.Value : null;
    }
}

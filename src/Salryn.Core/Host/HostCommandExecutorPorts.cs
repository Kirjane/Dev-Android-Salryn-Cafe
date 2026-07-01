using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;
using Salryn.Core.RemoteClients;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Core.Host;

public sealed record DeploymentStatusHostCommandResponse(
    bool IsConfigured,
    string InstallationRole,
    Guid StoreId,
    Guid HostId,
    Guid TerminalId);

public sealed record ProductLookupHostCommandRequest(string LookupCode);

public sealed record ProductLookupHostCommandResponse(
    bool Found,
    Guid? ProductId,
    string? Name,
    string? Barcode);

public sealed record OfficialCheckoutHostCommandPayload(
    PosCheckoutApplicationRequest CheckoutRequest);

public sealed record OfficialLifecycleHostCommandPayload(
    OfficialDocumentAuditEventRequest AuditRequest,
    IReadOnlyCollection<string> Permissions);

public sealed record OfficialReadingPreviewXHostCommandPayload(
    OfficialXReadingRequest Request);

public sealed record OfficialReadingFinalizeZHostCommandPayload(
    OfficialZReadingRequest Request);

public sealed record BackupAuthorityProbeHostCommandResponse(bool BackupAuthorityGranted);

public sealed record TerminalRegistryListHostCommandResponse(
    IReadOnlyList<SalrynTerminalRegistration> Terminals);

public sealed record TerminalRegistryDisableHostCommandPayload(
    TerminalRegistryActorContext Actor,
    Guid TerminalId);

public sealed record TerminalRegistryRevokeHostCommandPayload(
    TerminalRegistryActorContext Actor,
    Guid TerminalId);

public sealed record TerminalPairingIssueHostCommandPayload(
    TerminalRegistryActorContext Actor,
    string TargetTerminalName,
    TerminalRegistrationRole TargetTerminalRole);

public sealed record TerminalPairingRevokeHostCommandPayload(
    TerminalRegistryActorContext Actor,
    Guid PairingId);

public sealed record TerminalPairingListHostCommandResponse(
    IReadOnlyList<SalrynTerminalPairingInvite> Invites);

public sealed record TerminalPairingIssueHostCommandResponse(
    SalrynTerminalPairingInvite Invite,
    string PairingCode);

public sealed record TerminalPairingActivationHostCommandPayload(
    TerminalPairingActivationRequest Request);

public sealed record RemoteClientHandshakeHostCommandPayload(
    RemoteClientHandshakeRequest Request);

public interface IDeploymentStatusHostCommandExecutor
{
    Task<Result<DeploymentStatusHostCommandResponse>> ExecuteAsync(
        DeploymentIdentitySnapshot? identity,
        CancellationToken cancellationToken = default);
}

public interface IProductLookupHostCommandExecutor
{
    Task<Result<ProductLookupHostCommandResponse>> ExecuteAsync(
        ProductLookupHostCommandRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOfficialCheckoutHostCommandExecutor
{
    Task<Result> ExecuteAsync(
        OfficialCheckoutHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IOfficialLifecycleHostCommandExecutor
{
    Task<Result<OfficialDocumentAuditEventResult>> ExecuteAsync(
        OfficialLifecycleHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IOfficialReadingPreviewXHostCommandExecutor
{
    Task<Result<OfficialXReadingResult>> ExecuteAsync(
        OfficialReadingPreviewXHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IOfficialReadingFinalizeZHostCommandExecutor
{
    Task<Result<OfficialZReadingResult>> ExecuteAsync(
        OfficialReadingFinalizeZHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IBackupAuthorityHostCommandExecutor
{
    Task<Result<BackupAuthorityProbeHostCommandResponse>> ExecuteAsync(
        CancellationToken cancellationToken = default);
}

public interface ITerminalRegistryListHostCommandExecutor
{
    Task<Result<TerminalRegistryListHostCommandResponse>> ExecuteAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);
}

public interface ITerminalRegistryDisableHostCommandExecutor
{
    Task<Result<SalrynTerminalRegistration>> ExecuteAsync(
        TerminalRegistryDisableHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ITerminalRegistryRevokeHostCommandExecutor
{
    Task<Result<SalrynTerminalRegistration>> ExecuteAsync(
        TerminalRegistryRevokeHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ITerminalPairingIssueHostCommandExecutor
{
    Task<Result<TerminalPairingIssueHostCommandResponse>> ExecuteAsync(
        TerminalPairingIssueHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ITerminalPairingRevokeHostCommandExecutor
{
    Task<Result<SalrynTerminalPairingInvite>> ExecuteAsync(
        TerminalPairingRevokeHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ITerminalPairingListHostCommandExecutor
{
    Task<Result<TerminalPairingListHostCommandResponse>> ExecuteAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default);
}

public interface ITerminalPairingActivationHostCommandExecutor
{
    Task<Result<TerminalPairingActivationResult>> ExecuteAsync(
        TerminalPairingActivationHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IRemoteClientHandshakeHostCommandExecutor
{
    Task<Result<RemoteClientHandshakeResult>> ExecuteAsync(
        RemoteClientHandshakeHostCommandPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IRemoteClientStatusHostCommandExecutor
{
    Task<Result<RemoteClientStatusHostCommandResponse>> ExecuteAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);
}

public interface IRemoteDashboardSummaryHostCommandExecutor
{
    Task<Result<RemoteDashboardSummaryHostCommandResponse>> ExecuteAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);
}

public interface IRemoteTerminalStatusHostCommandExecutor
{
    Task<Result<RemoteTerminalStatusHostCommandResponse>> ExecuteAsync(
        Guid terminalId,
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);
}

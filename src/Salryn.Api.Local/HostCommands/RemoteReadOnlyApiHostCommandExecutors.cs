using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.ReadModels;
using Salryn.Core.RemoteClients;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.HostCommands;

internal sealed class ApiDeploymentStatusHostCommandExecutor : IDeploymentStatusHostCommandExecutor
{
    private readonly IDeploymentIdentityService _deploymentIdentity;

    public ApiDeploymentStatusHostCommandExecutor(IDeploymentIdentityService deploymentIdentity)
    {
        _deploymentIdentity = deploymentIdentity ?? throw new ArgumentNullException(nameof(deploymentIdentity));
    }

    public async Task<Result<DeploymentStatusHostCommandResponse>> ExecuteAsync(
        DeploymentIdentitySnapshot? identity,
        CancellationToken cancellationToken = default)
    {
        identity ??= (await _deploymentIdentity.GetCurrentAsync(cancellationToken).ConfigureAwait(false)).Value;

        if (identity is null)
        {
            return Result<DeploymentStatusHostCommandResponse>.Success(
                new DeploymentStatusHostCommandResponse(
                    IsConfigured: false,
                    InstallationRole: "NotConfigured",
                    StoreId: Guid.Empty,
                    HostId: Guid.Empty,
                    TerminalId: Guid.Empty));
        }

        return Result<DeploymentStatusHostCommandResponse>.Success(
            new DeploymentStatusHostCommandResponse(
                identity.IsConfigured,
                identity.Host.InstallationRole.ToString(),
                identity.Store.StoreId,
                identity.Host.HostId,
                identity.Terminal.TerminalId));
    }
}

internal sealed class ApiProductLookupHostCommandExecutor : IProductLookupHostCommandExecutor
{
    private readonly IProductReadRepository _productReadRepository;

    public ApiProductLookupHostCommandExecutor(IProductReadRepository productReadRepository)
    {
        _productReadRepository = productReadRepository
            ?? throw new ArgumentNullException(nameof(productReadRepository));
    }

    public async Task<Result<ProductLookupHostCommandResponse>> ExecuteAsync(
        ProductLookupHostCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LookupCode))
        {
            return Result<ProductLookupHostCommandResponse>.Fail(Error.Validation(
                "HostCommand.ProductLookupCodeRequired",
                "Product lookup code is required."));
        }

        Result<Barcode> barcodeResult = Barcode.Create(request.LookupCode);

        if (barcodeResult.IsFailure)
        {
            return Result<ProductLookupHostCommandResponse>.Fail(barcodeResult.ErrorDetail);
        }

        Result<ProductLookupReadModel?> productResult = await _productReadRepository
            .FindByBarcodeAsync(barcodeResult.Value, cancellationToken)
            .ConfigureAwait(false);

        if (productResult.IsFailure)
        {
            return Result<ProductLookupHostCommandResponse>.Fail(productResult.ErrorDetail);
        }

        ProductLookupReadModel? product = productResult.Value;

        if (product is null)
        {
            return Result<ProductLookupHostCommandResponse>.Success(
                new ProductLookupHostCommandResponse(false, null, null, null));
        }

        return Result<ProductLookupHostCommandResponse>.Success(
            new ProductLookupHostCommandResponse(
                true,
                product.Id,
                product.Name,
                product.Barcode));
    }
}

internal sealed class ApiTerminalPairingActivationHostCommandExecutor : ITerminalPairingActivationHostCommandExecutor
{
    private readonly ITerminalPairingActivationService _terminalPairingActivation;

    public ApiTerminalPairingActivationHostCommandExecutor(ITerminalPairingActivationService terminalPairingActivation)
    {
        _terminalPairingActivation = terminalPairingActivation
            ?? throw new ArgumentNullException(nameof(terminalPairingActivation));
    }

    public Task<Result<TerminalPairingActivationResult>> ExecuteAsync(
        TerminalPairingActivationHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _terminalPairingActivation.ActivatePairingAsync(payload.Request, cancellationToken);
}

internal sealed class ApiRemoteClientHandshakeHostCommandExecutor : IRemoteClientHandshakeHostCommandExecutor
{
    private readonly IRemoteClientHandshakeService _remoteClientHandshake;

    public ApiRemoteClientHandshakeHostCommandExecutor(IRemoteClientHandshakeService remoteClientHandshake)
    {
        _remoteClientHandshake = remoteClientHandshake
            ?? throw new ArgumentNullException(nameof(remoteClientHandshake));
    }

    public Task<Result<RemoteClientHandshakeResult>> ExecuteAsync(
        RemoteClientHandshakeHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _remoteClientHandshake.HandshakeAsync(payload.Request, cancellationToken);
}

internal sealed class ApiRemoteClientStatusHostCommandExecutor : IRemoteClientStatusHostCommandExecutor
{
    public Task<Result<RemoteClientStatusHostCommandResponse>> ExecuteAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RemoteClientStatusHostCommandResponse response = new(
            RemoteReadOnlyClientStatus.Ready,
            context.TerminalId,
            context.StoreId,
            context.HostId,
            RemoteReadOnlyClientCapabilityCatalog.SafeCapabilities,
            "LAN remote read-only client can use status, product, dashboard, and terminal status reads only.");

        return Task.FromResult(Result<RemoteClientStatusHostCommandResponse>.Success(response));
    }
}

internal sealed class ApiRemoteDashboardSummaryHostCommandExecutor : IRemoteDashboardSummaryHostCommandExecutor
{
    public Task<Result<RemoteDashboardSummaryHostCommandResponse>> ExecuteAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RemoteDashboardSummaryHostCommandResponse response = new(
            DateTimeOffset.UtcNow,
            context.TerminalId,
            "LAN hybrid bridge safe read-only dashboard",
            RemoteReadOnlyClientCapabilityCatalog.SafeCapabilities,
            "Dashboard read is limited to safe remote-client capability/status information.");

        return Task.FromResult(Result<RemoteDashboardSummaryHostCommandResponse>.Success(response));
    }
}

internal sealed class ApiRemoteTerminalStatusHostCommandExecutor : IRemoteTerminalStatusHostCommandExecutor
{
    private readonly ITerminalRegistryService _terminalRegistry;

    public ApiRemoteTerminalStatusHostCommandExecutor(ITerminalRegistryService terminalRegistry)
    {
        _terminalRegistry = terminalRegistry ?? throw new ArgumentNullException(nameof(terminalRegistry));
    }

    public async Task<Result<RemoteTerminalStatusHostCommandResponse>> ExecuteAsync(
        Guid terminalId,
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TerminalRegistryActorContext actor = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new[] { SalrynPermissions.StoreTerminalView });

        Result<SalrynTerminalRegistration?> terminalResult = await _terminalRegistry
            .GetTerminalAsync(terminalId, actor, cancellationToken)
            .ConfigureAwait(false);

        if (terminalResult.IsFailure)
        {
            return Result<RemoteTerminalStatusHostCommandResponse>.Fail(terminalResult.ErrorDetail);
        }

        SalrynTerminalRegistration? terminal = terminalResult.Value;
        if (terminal is null)
        {
            return Result<RemoteTerminalStatusHostCommandResponse>.Fail(Error.NotFound(
                "RemoteTerminalStatus.TerminalUnknown",
                "Remote terminal is not registered."));
        }

        if (terminal.StoreId != context.StoreId || terminal.HostId != context.HostId)
        {
            return Result<RemoteTerminalStatusHostCommandResponse>.Fail(Error.Forbidden(
                "RemoteTerminalStatus.ScopeMismatch",
                "Remote terminal does not match the requested store or host."));
        }

        return Result<RemoteTerminalStatusHostCommandResponse>.Success(
            new RemoteTerminalStatusHostCommandResponse(
                terminal.TerminalId,
                terminal.StoreId,
                terminal.HostId,
                terminal.TerminalName,
                terminal.TerminalRole,
                terminal.Status,
                terminal.RegisteredAtUtc,
                terminal.LastSeenAtUtc));
    }
}

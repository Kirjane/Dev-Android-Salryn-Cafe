using Salryn.App.WinForms.Features.Checkout;
using Salryn.App.WinForms.Features.OfficialOutput;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.HostCommands;

public static class HostCommandExecutorAdapters
{
    public static IDeploymentStatusHostCommandExecutor CreateDeploymentStatusExecutor(
        IDeploymentIdentityService deploymentIdentity) =>
        new DeploymentStatusHostCommandExecutor(deploymentIdentity);

    public static IProductLookupHostCommandExecutor CreateProductLookupExecutor(
        IProductReadRepository productReadRepository) =>
        new ProductLookupHostCommandExecutor(productReadRepository);

    public static IOfficialCheckoutHostCommandExecutor CreateOfficialCheckoutExecutor(
        IOfficialCheckoutCompletionBoundary officialCheckoutCompletion) =>
        new OfficialCheckoutHostCommandExecutor(officialCheckoutCompletion);

    public static IOfficialLifecycleHostCommandExecutor CreateOfficialLifecycleExecutor(
        IOfficialDocumentLifecycleOperationBoundary lifecycleOperations) =>
        new OfficialLifecycleHostCommandExecutor(lifecycleOperations);

    public static IOfficialReadingPreviewXHostCommandExecutor CreateOfficialPreviewXExecutor(
        IOfficialXReadingService officialXReading) =>
        new OfficialReadingPreviewXHostCommandExecutor(officialXReading);

    public static IOfficialReadingFinalizeZHostCommandExecutor CreateOfficialFinalizeZExecutor(
        IOfficialZReadingService officialZReading) =>
        new OfficialReadingFinalizeZHostCommandExecutor(officialZReading);

    public static IBackupAuthorityHostCommandExecutor CreateBackupAuthorityExecutor() =>
        new BackupAuthorityHostCommandExecutor();

    public static ITerminalRegistryListHostCommandExecutor CreateTerminalRegistryListExecutor(
        ITerminalRegistryService terminalRegistry) =>
        new TerminalRegistryListHostCommandExecutor(terminalRegistry);

    public static ITerminalRegistryDisableHostCommandExecutor CreateTerminalRegistryDisableExecutor(
        ITerminalRegistryService terminalRegistry) =>
        new TerminalRegistryDisableHostCommandExecutor(terminalRegistry);

    public static ITerminalRegistryRevokeHostCommandExecutor CreateTerminalRegistryRevokeExecutor(
        ITerminalRegistryService terminalRegistry) =>
        new TerminalRegistryRevokeHostCommandExecutor(terminalRegistry);

    public static ITerminalPairingIssueHostCommandExecutor CreateTerminalPairingIssueExecutor(
        ITerminalPairingPreparationService terminalPairingPreparation) =>
        new TerminalPairingIssueHostCommandExecutor(terminalPairingPreparation);

    public static ITerminalPairingRevokeHostCommandExecutor CreateTerminalPairingRevokeExecutor(
        ITerminalPairingPreparationService terminalPairingPreparation) =>
        new TerminalPairingRevokeHostCommandExecutor(terminalPairingPreparation);

    public static ITerminalPairingListHostCommandExecutor CreateTerminalPairingListExecutor(
        ITerminalPairingPreparationService terminalPairingPreparation) =>
        new TerminalPairingListHostCommandExecutor(terminalPairingPreparation);

    public static ITerminalPairingActivationHostCommandExecutor CreateTerminalPairingActivationExecutor(
        ITerminalPairingActivationService terminalPairingActivation) =>
        new TerminalPairingActivationHostCommandExecutor(terminalPairingActivation);

    public static IRemoteClientHandshakeHostCommandExecutor CreateRemoteClientHandshakeExecutor(
        IRemoteClientHandshakeService remoteClientHandshake) =>
        new RemoteClientHandshakeHostCommandExecutor(remoteClientHandshake);
}

internal sealed class DeploymentStatusHostCommandExecutor : IDeploymentStatusHostCommandExecutor
{
    private readonly IDeploymentIdentityService _deploymentIdentity;

    public DeploymentStatusHostCommandExecutor(IDeploymentIdentityService deploymentIdentity)
    {
        _deploymentIdentity = deploymentIdentity;
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

internal sealed class ProductLookupHostCommandExecutor : IProductLookupHostCommandExecutor
{
    private readonly IProductReadRepository _productReadRepository;

    public ProductLookupHostCommandExecutor(IProductReadRepository productReadRepository)
    {
        _productReadRepository = productReadRepository;
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

internal sealed class OfficialCheckoutHostCommandExecutor : IOfficialCheckoutHostCommandExecutor
{
    private readonly IOfficialCheckoutCompletionBoundary _officialCheckoutCompletion;

    public OfficialCheckoutHostCommandExecutor(IOfficialCheckoutCompletionBoundary officialCheckoutCompletion)
    {
        _officialCheckoutCompletion = officialCheckoutCompletion;
    }

    public Task<Result> ExecuteAsync(
        OfficialCheckoutHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _officialCheckoutCompletion.CompleteSaleAsync(payload.CheckoutRequest, cancellationToken);
}

internal sealed class OfficialLifecycleHostCommandExecutor : IOfficialLifecycleHostCommandExecutor
{
    private readonly IOfficialDocumentLifecycleOperationBoundary _lifecycleOperations;

    public OfficialLifecycleHostCommandExecutor(IOfficialDocumentLifecycleOperationBoundary lifecycleOperations)
    {
        _lifecycleOperations = lifecycleOperations;
    }

    public async Task<Result<OfficialDocumentAuditEventResult>> ExecuteAsync(
        OfficialLifecycleHostCommandPayload payload,
        CancellationToken cancellationToken = default)
    {
        Result<OfficialDocumentAuditEventResult> result = await _lifecycleOperations
            .AppendLifecycleEventAsync(
                new OfficialDocumentLifecycleOperationRequest(
                    payload.AuditRequest,
                    () => payload.Permissions),
                cancellationToken)
            .ConfigureAwait(false);

        return result;
    }
}

internal sealed class OfficialReadingPreviewXHostCommandExecutor : IOfficialReadingPreviewXHostCommandExecutor
{
    private readonly IOfficialXReadingService _officialXReading;

    public OfficialReadingPreviewXHostCommandExecutor(IOfficialXReadingService officialXReading)
    {
        _officialXReading = officialXReading;
    }

    public Task<Result<OfficialXReadingResult>> ExecuteAsync(
        OfficialReadingPreviewXHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _officialXReading.ReadAsync(payload.Request, cancellationToken);
}

internal sealed class OfficialReadingFinalizeZHostCommandExecutor : IOfficialReadingFinalizeZHostCommandExecutor
{
    private readonly IOfficialZReadingService _officialZReading;

    public OfficialReadingFinalizeZHostCommandExecutor(IOfficialZReadingService officialZReading)
    {
        _officialZReading = officialZReading;
    }

    public Task<Result<OfficialZReadingResult>> ExecuteAsync(
        OfficialReadingFinalizeZHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _officialZReading.FinalizeAsync(payload.Request, cancellationToken);
}

internal sealed class BackupAuthorityHostCommandExecutor : IBackupAuthorityHostCommandExecutor
{
    public Task<Result<BackupAuthorityProbeHostCommandResponse>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<BackupAuthorityProbeHostCommandResponse>.Success(
            new BackupAuthorityProbeHostCommandResponse(true)));
    }
}

internal sealed class TerminalRegistryListHostCommandExecutor : ITerminalRegistryListHostCommandExecutor
{
    private readonly ITerminalRegistryService _terminalRegistry;

    public TerminalRegistryListHostCommandExecutor(ITerminalRegistryService terminalRegistry)
    {
        _terminalRegistry = terminalRegistry;
    }

    public async Task<Result<TerminalRegistryListHostCommandResponse>> ExecuteAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<SalrynTerminalRegistration>> result = await _terminalRegistry
            .ListTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<TerminalRegistryListHostCommandResponse>.Fail(result.ErrorDetail);
        }

        return Result<TerminalRegistryListHostCommandResponse>.Success(
            new TerminalRegistryListHostCommandResponse(result.Value));
    }
}

internal sealed class TerminalRegistryDisableHostCommandExecutor : ITerminalRegistryDisableHostCommandExecutor
{
    private readonly ITerminalRegistryService _terminalRegistry;

    public TerminalRegistryDisableHostCommandExecutor(ITerminalRegistryService terminalRegistry)
    {
        _terminalRegistry = terminalRegistry;
    }

    public Task<Result<SalrynTerminalRegistration>> ExecuteAsync(
        TerminalRegistryDisableHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _terminalRegistry.DisableTerminalAsync(payload.TerminalId, payload.Actor, cancellationToken);
}

internal sealed class TerminalRegistryRevokeHostCommandExecutor : ITerminalRegistryRevokeHostCommandExecutor
{
    private readonly ITerminalRegistryService _terminalRegistry;

    public TerminalRegistryRevokeHostCommandExecutor(ITerminalRegistryService terminalRegistry)
    {
        _terminalRegistry = terminalRegistry;
    }

    public Task<Result<SalrynTerminalRegistration>> ExecuteAsync(
        TerminalRegistryRevokeHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _terminalRegistry.RevokeTerminalAsync(payload.TerminalId, payload.Actor, cancellationToken);
}

internal sealed class TerminalPairingIssueHostCommandExecutor : ITerminalPairingIssueHostCommandExecutor
{
    private readonly ITerminalPairingPreparationService _terminalPairingPreparation;

    public TerminalPairingIssueHostCommandExecutor(ITerminalPairingPreparationService terminalPairingPreparation)
    {
        _terminalPairingPreparation = terminalPairingPreparation;
    }

    public async Task<Result<TerminalPairingIssueHostCommandResponse>> ExecuteAsync(
        TerminalPairingIssueHostCommandPayload payload,
        CancellationToken cancellationToken = default)
    {
        Result<PairingInviteCreationResult> result = await _terminalPairingPreparation
            .CreatePairingInviteAsync(
                payload.TargetTerminalName,
                payload.TargetTerminalRole,
                payload.Actor,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<TerminalPairingIssueHostCommandResponse>.Fail(result.ErrorDetail);
        }

        return Result<TerminalPairingIssueHostCommandResponse>.Success(
            new TerminalPairingIssueHostCommandResponse(result.Value.Invite, result.Value.PairingCode));
    }
}

internal sealed class TerminalPairingRevokeHostCommandExecutor : ITerminalPairingRevokeHostCommandExecutor
{
    private readonly ITerminalPairingPreparationService _terminalPairingPreparation;

    public TerminalPairingRevokeHostCommandExecutor(ITerminalPairingPreparationService terminalPairingPreparation)
    {
        _terminalPairingPreparation = terminalPairingPreparation;
    }

    public Task<Result<SalrynTerminalPairingInvite>> ExecuteAsync(
        TerminalPairingRevokeHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _terminalPairingPreparation.RevokePairingInviteAsync(payload.PairingId, payload.Actor, cancellationToken);
}

internal sealed class TerminalPairingListHostCommandExecutor : ITerminalPairingListHostCommandExecutor
{
    private readonly ITerminalPairingPreparationService _terminalPairingPreparation;

    public TerminalPairingListHostCommandExecutor(ITerminalPairingPreparationService terminalPairingPreparation)
    {
        _terminalPairingPreparation = terminalPairingPreparation;
    }

    public async Task<Result<TerminalPairingListHostCommandResponse>> ExecuteAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<SalrynTerminalPairingInvite>> result = await _terminalPairingPreparation
            .ListActiveInvitesAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<TerminalPairingListHostCommandResponse>.Fail(result.ErrorDetail);
        }

        return Result<TerminalPairingListHostCommandResponse>.Success(
            new TerminalPairingListHostCommandResponse(result.Value));
    }
}

internal sealed class TerminalPairingActivationHostCommandExecutor : ITerminalPairingActivationHostCommandExecutor
{
    private readonly ITerminalPairingActivationService _terminalPairingActivation;

    public TerminalPairingActivationHostCommandExecutor(ITerminalPairingActivationService terminalPairingActivation)
    {
        _terminalPairingActivation = terminalPairingActivation;
    }

    public Task<Result<TerminalPairingActivationResult>> ExecuteAsync(
        TerminalPairingActivationHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _terminalPairingActivation.ActivatePairingAsync(payload.Request, cancellationToken);
}

internal sealed class RemoteClientHandshakeHostCommandExecutor : IRemoteClientHandshakeHostCommandExecutor
{
    private readonly IRemoteClientHandshakeService _remoteClientHandshake;

    public RemoteClientHandshakeHostCommandExecutor(IRemoteClientHandshakeService remoteClientHandshake)
    {
        _remoteClientHandshake = remoteClientHandshake;
    }

    public Task<Result<RemoteClientHandshakeResult>> ExecuteAsync(
        RemoteClientHandshakeHostCommandPayload payload,
        CancellationToken cancellationToken = default) =>
        _remoteClientHandshake.HandshakeAsync(payload.Request, cancellationToken);
}

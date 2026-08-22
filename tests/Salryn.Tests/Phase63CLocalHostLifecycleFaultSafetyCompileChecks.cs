using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.Models;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Dispatch;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63C Local Host lifecycle and fault safety without transport or real POS handlers.
public static class Phase63CLocalHostLifecycleFaultSafetyCompileChecks
{
    // Kirch: This smoke check proves stopped dispatch blocking, handler fault wrapping, and faulted state behavior.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        HostRequestEnvelope<LocalHostProductLookupRequest> validRequest = CreateRequest(profile.Store.TenantId, profile.Store.StoreId);

        LocalHostRuntime stoppedRuntime = new(profile);
        HostResponseEnvelope<LocalHostProductLookupResponse> stoppedResponse = await stoppedRuntime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(validRequest);

        if (stoppedResponse.IsSuccess
            || stoppedResponse.Error is null
            || stoppedResponse.Error.Code != LocalHostDispatchErrorCodes.RuntimeNotRunning)
        {
            throw new InvalidOperationException("Stopped Local Host runtime should reject dispatch cleanly.");
        }

        LocalHostRuntime runningRuntime = new(profile);
        runningRuntime.Start(DateTimeOffset.UtcNow);

        bool rejectedRuntimeRegistration = false;

        try
        {
            runningRuntime.RegisterHandler(
                HostOperationNames.ProductLookup,
                new StableProductLookupHandler());
        }
        catch (InvalidOperationException)
        {
            rejectedRuntimeRegistration = true;
        }

        if (!rejectedRuntimeRegistration)
        {
            throw new InvalidOperationException("Local Host runtime should reject handler registration while running.");
        }

        LocalHostRuntime faultRuntime = new(profile);
        faultRuntime.RegisterHandler(
            HostOperationNames.ProductLookup,
            new FaultingProductLookupHandler());
        faultRuntime.Start(DateTimeOffset.UtcNow);

        HostResponseEnvelope<LocalHostProductLookupResponse> faultResponse = await faultRuntime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(validRequest);

        if (faultResponse.IsSuccess
            || faultResponse.Error is null
            || faultResponse.Error.Code != LocalHostDispatchErrorCodes.HandlerFailure)
        {
            throw new InvalidOperationException("Local Host runtime should wrap handler exceptions as clean Host errors.");
        }

        LocalHostRuntimeSnapshot faultSnapshot = faultRuntime.GetSnapshot();

        if (faultSnapshot.State != LocalHostRuntimeState.Faulted)
        {
            throw new InvalidOperationException("Local Host runtime should enter faulted state after handler failure.");
        }

        HostResponseEnvelope<LocalHostProductLookupResponse> alreadyFaultedResponse = await faultRuntime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(validRequest);

        if (alreadyFaultedResponse.IsSuccess
            || alreadyFaultedResponse.Error is null
            || alreadyFaultedResponse.Error.Code != LocalHostDispatchErrorCodes.RuntimeAlreadyFaulted)
        {
            throw new InvalidOperationException("Faulted Local Host runtime should reject future dispatch until stopped.");
        }

        faultRuntime.Stop();

        if (faultRuntime.GetSnapshot().State != LocalHostRuntimeState.Stopped)
        {
            throw new InvalidOperationException("Stopping a faulted Local Host runtime should reset it to stopped state.");
        }

        LocalHostOperationDispatcher emptyIssueDispatcher = new(
            new EmptyIssueBoundaryValidator(),
            new LocalHostHandlerRegistry());

        HostResponseEnvelope<LocalHostProductLookupResponse> emptyIssueResponse = await emptyIssueDispatcher.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(validRequest);

        if (emptyIssueResponse.IsSuccess
            || emptyIssueResponse.Error is null
            || emptyIssueResponse.Error.Code != LocalHostDispatchErrorCodes.BoundaryValidationFailed)
        {
            throw new InvalidOperationException("Dispatcher should handle invalid boundary results with no issues.");
        }
    }

    // Kirch: Creates a valid local host profile for lifecycle and fault safety smoke checks.
    private static StoreDeploymentProfile CreateProfile()
    {
        return new StoreDeploymentProfile(
            Store: new StoreIdentity(
                TenantId: Guid.NewGuid(),
                StoreId: Guid.NewGuid()),
            Host: new StoreHostIdentity(
                HostId: Guid.NewGuid(),
                MachineName: "field-node",
                Role: SalrynMachineRole.StandaloneHost),
            Mode: SalrynDeploymentMode.Standalone,
            Endpoint: null,
            Terminal: null);
    }

    // Kirch: Creates a product lookup query request with full authenticated terminal identity.
    private static HostRequestEnvelope<LocalHostProductLookupRequest> CreateRequest(Guid tenantId, Guid storeId)
    {
        return new HostRequestEnvelope<LocalHostProductLookupRequest>(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Query,
            OperationName: HostOperationNames.ProductLookup,
            Context: new HostRequestContext(
                TenantId: tenantId,
                StoreId: storeId,
                UserId: Guid.NewGuid(),
                RoleId: Guid.NewGuid(),
                CashierSessionId: null,
                TerminalId: Guid.NewGuid(),
                DeviceKey: "field-node-terminal",
                RequestedAt: DateTimeOffset.UtcNow),
            Payload: new LocalHostProductLookupRequest("sample-payload"));
    }

    // Kirch: Stable sample handler exists only to prove runtime registration rules.
    private sealed class StableProductLookupHandler : IHostOperationHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>
    {
        public Task<HostResponseEnvelope<LocalHostProductLookupResponse>> HandleAsync(
            HostRequestEnvelope<LocalHostProductLookupRequest> request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HostResponseEnvelope<LocalHostProductLookupResponse>(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: new LocalHostProductLookupResponse(
                    ProductId: Guid.NewGuid(),
                    Sku: null,
                    Barcode: request.Payload.BarcodeOrSku,
                    DisplayName: request.Payload.BarcodeOrSku,
                    UnitName: "piece",
                    SellingPriceCents: 100,
                    CostPriceCents: null,
                    StockOnHandX1000: 1000,
                    TrackStock: true,
                    AllowDiscount: true,
                    TaxClassification: (int)ProductTaxClassification.Vatable,
                    IsActive: true),
                Error: null));
        }
    }

    // Kirch: Faulting sample handler exists only to prove exception wrapping and faulted runtime state.
    private sealed class FaultingProductLookupHandler : IHostOperationHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>
    {
        public Task<HostResponseEnvelope<LocalHostProductLookupResponse>> HandleAsync(
            HostRequestEnvelope<LocalHostProductLookupRequest> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("sample handler failure");
        }
    }

    // Kirch: Boundary validator intentionally returns no issues to prove defensive fallback behavior.
    private sealed class EmptyIssueBoundaryValidator : IHostRequestBoundaryValidator
    {
        public HostBoundaryValidationResult Validate<TPayload>(HostRequestEnvelope<TPayload>? request)
        {
            return HostBoundaryValidationResult.Invalid(Array.Empty<HostBoundaryValidationIssue>());
        }
    }
}

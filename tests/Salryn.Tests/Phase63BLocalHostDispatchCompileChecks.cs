using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Dispatch;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63B Local Host dispatch skeleton without HTTP, sockets, or real POS handlers.
public static class Phase63BLocalHostDispatchCompileChecks
{
    // Kirch: This smoke check proves boundary failure, missing handler failure, and registered handler success.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        LocalHostRuntime runtime = new(profile);

        runtime.RegisterHandler(
            HostOperationNames.ProductLookup,
            new SampleProductLookupHandler());

        runtime.Start(DateTimeOffset.UtcNow);

        HostRequestEnvelope<LocalHostProductLookupRequest> invalidRequest = CreateRequest(
            tenantId: Guid.Empty,
            storeId: Guid.Empty);

        HostResponseEnvelope<LocalHostProductLookupResponse> invalidResponse = await runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(invalidRequest);

        if (invalidResponse.IsSuccess
            || invalidResponse.Error is null
            || invalidResponse.Error.Code != "HostBoundary.MissingStoreIdentity")
        {
            throw new InvalidOperationException("Local Host dispatch should fail malformed requests at the Core boundary.");
        }

        LocalHostRuntime runtimeWithoutHandler = new(profile);
        runtimeWithoutHandler.Start(DateTimeOffset.UtcNow);

        HostRequestEnvelope<LocalHostProductLookupRequest> validRequestWithoutHandler = CreateRequest(
            tenantId: profile.Store.TenantId,
            storeId: profile.Store.StoreId);

        HostResponseEnvelope<LocalHostProductLookupResponse> missingHandlerResponse = await runtimeWithoutHandler.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(validRequestWithoutHandler);

        if (missingHandlerResponse.IsSuccess
            || missingHandlerResponse.Error is null
            || missingHandlerResponse.Error.Code != LocalHostDispatchErrorCodes.HandlerNotRegistered)
        {
            throw new InvalidOperationException("Local Host dispatch should fail cleanly when no handler is registered.");
        }

        HostResponseEnvelope<LocalHostProductLookupResponse> handledResponse = await runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(validRequestWithoutHandler);

        if (!handledResponse.IsSuccess || handledResponse.Payload?.DisplayName != "handled:sample-payload")
        {
            throw new InvalidOperationException("Local Host dispatch should call a registered in-process handler.");
        }
    }

    // Kirch: Creates a valid local host profile for in-process dispatch smoke checks.
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

    // Kirch: Sample handler exists only to prove local dispatch wiring; it is not a real POS handler.
    private sealed class SampleProductLookupHandler : IHostOperationHandler<LocalHostProductLookupRequest, LocalHostProductLookupResponse>
    {
        public Task<HostResponseEnvelope<LocalHostProductLookupResponse>> HandleAsync(
            HostRequestEnvelope<LocalHostProductLookupRequest> request,
            CancellationToken cancellationToken = default)
        {
            HostResponseEnvelope<LocalHostProductLookupResponse> response = new(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: new LocalHostProductLookupResponse($"handled:{request.Payload.BarcodeOrSku}"),
                Error: null);

            return Task.FromResult(response);
        }
    }
}

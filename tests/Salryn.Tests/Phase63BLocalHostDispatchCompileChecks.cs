using Salryn.Core.Common;
using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
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
        runtime.Start(DateTimeOffset.UtcNow);

        HostRequestEnvelope<string> invalidRequest = CreateRequest(
            tenantId: Guid.Empty,
            storeId: Guid.Empty);

        HostResponseEnvelope<string> invalidResponse = await runtime.DispatchAsync<string, string>(invalidRequest);

        if (invalidResponse.IsSuccess
            || invalidResponse.Error is null
            || invalidResponse.Error.Code != "HostBoundary.MissingStoreIdentity")
        {
            throw new InvalidOperationException("Local Host dispatch should fail malformed requests at the Core boundary.");
        }

        HostRequestEnvelope<string> validRequestWithoutHandler = CreateRequest(
            tenantId: profile.Store.TenantId,
            storeId: profile.Store.StoreId);

        HostResponseEnvelope<string> missingHandlerResponse = await runtime.DispatchAsync<string, string>(validRequestWithoutHandler);

        if (missingHandlerResponse.IsSuccess
            || missingHandlerResponse.Error is null
            || missingHandlerResponse.Error.Code != LocalHostDispatchErrorCodes.HandlerNotRegistered)
        {
            throw new InvalidOperationException("Local Host dispatch should fail cleanly when no handler is registered.");
        }

        runtime.RegisterHandler(
            HostOperationNames.ProductLookup,
            new SampleProductLookupHandler());

        HostResponseEnvelope<string> handledResponse = await runtime.DispatchAsync<string, string>(validRequestWithoutHandler);

        if (!handledResponse.IsSuccess || handledResponse.Payload != "handled:sample-payload")
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
    private static HostRequestEnvelope<string> CreateRequest(Guid tenantId, Guid storeId)
    {
        return new HostRequestEnvelope<string>(
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
            Payload: "sample-payload");
    }

    // Kirch: Sample handler exists only to prove local dispatch wiring; it is not a real POS handler.
    private sealed class SampleProductLookupHandler : IHostOperationHandler<string, string>
    {
        public Task<HostResponseEnvelope<string>> HandleAsync(
            HostRequestEnvelope<string> request,
            CancellationToken cancellationToken = default)
        {
            HostResponseEnvelope<string> response = new(
                RequestId: request.RequestId,
                IsSuccess: true,
                Payload: $"handled:{request.Payload}",
                Error: null);

            return Task.FromResult(response);
        }
    }
}

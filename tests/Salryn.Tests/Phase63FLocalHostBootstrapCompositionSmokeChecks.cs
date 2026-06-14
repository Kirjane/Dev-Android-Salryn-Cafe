using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Composition;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63F Local Host bootstrap composition without transport or real POS handlers.
public static class Phase63FLocalHostBootstrapCompositionSmokeChecks
{
    // Kirch: This smoke check proves composed runtime, placeholder bootstrap, and safe dispatch behavior.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();
        LocalHostBootstrapComposer composer = new();

        LocalHostBootstrapCompositionResult result = composer.Compose(
            profile,
            operationCatalog,
            contractMap);

        if (!LocalHostBootstrapCompositionRules.IsCompositionValid(result))
        {
            throw new InvalidOperationException("Local Host bootstrap composition should be valid after placeholder handler registration.");
        }

        if (result.Snapshot.State != LocalHostRuntimeState.Stopped)
        {
            throw new InvalidOperationException("Composed Local Host runtime should remain stopped until explicitly started.");
        }

        if (result.Snapshot.RegisteredOperationCount != operationCatalog.Descriptors.Count
            || result.Snapshot.RegisteredContractCount != operationCatalog.Descriptors.Count
            || result.Snapshot.RegisteredHandlerCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Composed Local Host runtime should align operation, contract, and handler counts.");
        }

        if (!result.BootstrapResult.RegisteredOperationNames.Contains(HostOperationNames.ProductLookup))
        {
            throw new InvalidOperationException("Composed Local Host bootstrap should include product lookup.");
        }

        result.Runtime.Start(DateTimeOffset.UtcNow);

        HostRequestEnvelope<LocalHostProductLookupRequest> request = CreateProductLookupRequest(
            tenantId: profile.Store.TenantId,
            storeId: profile.Store.StoreId);

        HostResponseEnvelope<LocalHostProductLookupResponse> response = await result.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(request);

        if (response.IsSuccess
            || response.Error is null
            || response.Error.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Composed Local Host runtime should dispatch to placeholder not-implemented handlers.");
        }

        if (result.Runtime.GetSnapshot().State != LocalHostRuntimeState.Running)
        {
            throw new InvalidOperationException("Placeholder dispatch should keep composed Local Host runtime running.");
        }
    }

    // Kirch: Creates a valid local host profile for composition smoke checks.
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

    // Kirch: Creates a product lookup request that exercises the composed placeholder dispatch path.
    private static HostRequestEnvelope<LocalHostProductLookupRequest> CreateProductLookupRequest(Guid tenantId, Guid storeId)
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
}

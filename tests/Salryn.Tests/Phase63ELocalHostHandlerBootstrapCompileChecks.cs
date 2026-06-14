using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63E Local Host handler bootstrap without real POS business logic.
public static class Phase63ELocalHostHandlerBootstrapCompileChecks
{
    // Kirch: This smoke check proves default placeholder handler registration and safe not-implemented dispatch.
    public static async Task RunAsync()
    {
        StoreDeploymentProfile profile = CreateProfile();
        LocalHostRuntime runtime = new(profile);
        LocalHostHandlerBootstrapper bootstrapper = new();

        LocalHostHandlerBootstrapResult result = bootstrapper.RegisterDefaultHandlers(runtime);
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();

        if (result.RegisteredHandlerCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Local Host bootstrap should register one placeholder handler per default operation.");
        }

        if (runtime.GetSnapshot().RegisteredHandlerCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Local Host runtime should expose all bootstrapped placeholder handlers.");
        }

        if (!result.RegisteredOperationNames.Contains(HostOperationNames.ProductLookup))
        {
            throw new InvalidOperationException("Local Host bootstrap should include product lookup placeholder registration.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        HostRequestEnvelope<LocalHostProductLookupRequest> request = CreateProductLookupRequest(
            tenantId: profile.Store.TenantId,
            storeId: profile.Store.StoreId);

        HostResponseEnvelope<LocalHostProductLookupResponse> response = await runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>(request);

        if (response.IsSuccess
            || response.Error is null
            || response.Error.Code != LocalHostHandlerBootstrapErrorCodes.HandlerNotImplemented)
        {
            throw new InvalidOperationException("Bootstrapped placeholder handler should return clean not-implemented response.");
        }

        if (runtime.GetSnapshot().State != LocalHostRuntimeState.Running)
        {
            throw new InvalidOperationException("Not-implemented placeholder handler should not fault the Local Host runtime.");
        }

        LocalHostRuntime runningRuntime = new(profile);
        runningRuntime.Start(DateTimeOffset.UtcNow);

        bool rejectedRunningBootstrap = false;

        try
        {
            bootstrapper.RegisterDefaultHandlers(runningRuntime);
        }
        catch (InvalidOperationException)
        {
            rejectedRunningBootstrap = true;
        }

        if (!rejectedRunningBootstrap)
        {
            throw new InvalidOperationException("Local Host bootstrap should obey stopped-only handler registration.");
        }
    }

    // Kirch: Creates a valid local host profile for bootstrap smoke checks.
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

    // Kirch: Creates a product lookup request that hits the bootstrapped placeholder handler.
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

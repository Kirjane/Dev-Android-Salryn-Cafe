using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Runtime;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63 Local Host runtime foundation compiles and routes validation through Core.
public static class Phase63LocalHostRuntimeCompileChecks
{
    // Kirch: This smoke check proves the local Host runtime starts, stops, snapshots, and validates safely.
    public static void Run()
    {
        StoreDeploymentProfile profile = new(
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

        LocalHostRuntime runtime = new(profile);

        LocalHostRuntimeSnapshot stoppedSnapshot = runtime.GetSnapshot();

        if (stoppedSnapshot.State != LocalHostRuntimeState.Stopped)
        {
            throw new InvalidOperationException("Local Host runtime should start in stopped state.");
        }

        if (stoppedSnapshot.RegisteredOperationCount <= 0)
        {
            throw new InvalidOperationException("Local Host runtime should expose registered Host operations.");
        }

        runtime.Start(DateTimeOffset.UtcNow);

        LocalHostRuntimeSnapshot runningSnapshot = runtime.GetSnapshot();

        if (runningSnapshot.State != LocalHostRuntimeState.Running || !runningSnapshot.StartedAt.HasValue)
        {
            throw new InvalidOperationException("Local Host runtime should enter running state with a start time.");
        }

        HostRequestEnvelope<string> malformedCheckout = new(
            RequestId: Guid.NewGuid(),
            OperationKind: HostOperationKind.Command,
            OperationName: HostOperationNames.CompleteSale,
            Context: new HostRequestContext(
                TenantId: Guid.Empty,
                StoreId: Guid.Empty,
                UserId: null,
                RoleId: null,
                CashierSessionId: null,
                TerminalId: null,
                DeviceKey: null,
                RequestedAt: DateTimeOffset.UtcNow),
            Payload: "malformed-checkout");

        HostBoundaryValidationResult validationResult = runtime.Validate(malformedCheckout);

        if (validationResult.IsValid
            || !validationResult.Issues.Any(issue => issue.Reason == HostBoundaryFailureReason.MissingStoreIdentity))
        {
            throw new InvalidOperationException("Local Host runtime should validate requests through the Core boundary.");
        }

        runtime.Stop();

        LocalHostRuntimeSnapshot stoppedAgainSnapshot = runtime.GetSnapshot();

        if (stoppedAgainSnapshot.State != LocalHostRuntimeState.Stopped || stoppedAgainSnapshot.StartedAt.HasValue)
        {
            throw new InvalidOperationException("Local Host runtime should stop and clear start time.");
        }

        StoreDeploymentProfile invalidClientProfile = profile with
        {
            Host = new StoreHostIdentity(
                HostId: Guid.NewGuid(),
                MachineName: "client-node",
                Role: SalrynMachineRole.CashierClient)
        };

        if (LocalHostRuntimeRules.IsHostProfileValid(invalidClientProfile))
        {
            throw new InvalidOperationException("Client machine roles must not be accepted as Local Host owners.");
        }
    }
}

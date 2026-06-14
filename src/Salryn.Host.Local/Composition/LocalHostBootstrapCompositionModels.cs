using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Host.Local.Composition;

// Kirch: Captures a fully composed local in-process Host runtime before transport or real handlers exist.
public sealed record LocalHostBootstrapCompositionResult(
    LocalHostRuntime Runtime,
    LocalHostHandlerBootstrapResult BootstrapResult,
    LocalHostRuntimeSnapshot Snapshot);

// Kirch: Validates Local Host bootstrap composition without touching network, UI, or persistence.
public static class LocalHostBootstrapCompositionRules
{
    // Kirch: A composed Local Host is valid only when operation, contract, and handler counts line up.
    public static bool IsCompositionValid(LocalHostBootstrapCompositionResult? result)
    {
        if (result is null)
        {
            return false;
        }

        return result.Snapshot.State == LocalHostRuntimeState.Stopped
            && result.Snapshot.RegisteredOperationCount > 0
            && result.Snapshot.RegisteredContractCount == result.Snapshot.RegisteredOperationCount
            && result.Snapshot.RegisteredHandlerCount == result.Snapshot.RegisteredOperationCount
            && result.BootstrapResult.RegisteredHandlerCount == result.Snapshot.RegisteredHandlerCount
            && result.BootstrapResult.RegisteredOperationNames.Count == result.Snapshot.RegisteredHandlerCount;
    }
}

// Kirch: Composes Local Host runtime and placeholder handler bootstrap in one controlled local step.
public sealed class LocalHostBootstrapComposer
{
    private readonly LocalHostHandlerBootstrapper _bootstrapper;

    // Kirch: Creates the composer with the existing placeholder bootstrapper only.
    public LocalHostBootstrapComposer(LocalHostHandlerBootstrapper? bootstrapper = null)
    {
        _bootstrapper = bootstrapper ?? new LocalHostHandlerBootstrapper();
    }

    // Kirch: Builds a stopped Local Host runtime and registers placeholder handlers through runtime guards.
    public LocalHostBootstrapCompositionResult Compose(
        StoreDeploymentProfile profile,
        HostOperationCatalog? operationCatalog = null,
        LocalHostOperationContractMap? contractMap = null)
    {
        LocalHostRuntime runtime = new(
            profile,
            operationCatalog,
            contractMap);

        LocalHostHandlerBootstrapResult bootstrapResult = _bootstrapper.RegisterDefaultHandlers(runtime);
        LocalHostRuntimeSnapshot snapshot = runtime.GetSnapshot();

        LocalHostBootstrapCompositionResult result = new(
            Runtime: runtime,
            BootstrapResult: bootstrapResult,
            Snapshot: snapshot);

        if (!LocalHostBootstrapCompositionRules.IsCompositionValid(result))
        {
            throw new InvalidOperationException("Invalid Local Host bootstrap composition.");
        }

        return result;
    }
}

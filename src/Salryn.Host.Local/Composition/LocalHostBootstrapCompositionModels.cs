using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Core.Repositories;
using Salryn.Host.Local.Bootstrap;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Runtime;

namespace Salryn.Host.Local.Composition;

// Kirch: Captures a fully composed local in-process Host runtime before client-facing composition exists.
public sealed record LocalHostBootstrapCompositionResult(
    LocalHostRuntime Runtime,
    LocalHostHandlerBootstrapResult BootstrapResult,
    LocalHostRuntimeSnapshot Snapshot);

// Kirch: Validates Local Host bootstrap composition without touching UI or persistence.
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

// Kirch: Composes Local Host runtime and handler bootstrap in controlled local steps.
public sealed class LocalHostBootstrapComposer
{
    private readonly LocalHostHandlerBootstrapper _bootstrapper;

    // Kirch: Creates the composer with the existing bootstrapper.
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
        LocalHostRuntime runtime = CreateRuntime(
            profile,
            operationCatalog,
            contractMap);

        LocalHostHandlerBootstrapResult bootstrapResult = _bootstrapper.RegisterDefaultHandlers(runtime);

        return CreateCheckedResult(runtime, bootstrapResult);
    }

    // Kirch: Builds a stopped Local Host runtime with ProductLookup using the Core product repository contract.
    public LocalHostBootstrapCompositionResult ComposeWithProductLookup(
        StoreDeploymentProfile profile,
        IProductReadRepository products,
        HostOperationCatalog? operationCatalog = null,
        LocalHostOperationContractMap? contractMap = null)
    {
        ArgumentNullException.ThrowIfNull(products);

        LocalHostRuntime runtime = CreateRuntime(
            profile,
            operationCatalog,
            contractMap);

        LocalHostHandlerBootstrapResult bootstrapResult = _bootstrapper.RegisterDefaultHandlersWithProductLookup(
            runtime,
            products);

        return CreateCheckedResult(runtime, bootstrapResult);
    }

    // Kirch: Creates the runtime while keeping composition construction centralized.
    private static LocalHostRuntime CreateRuntime(
        StoreDeploymentProfile profile,
        HostOperationCatalog? operationCatalog,
        LocalHostOperationContractMap? contractMap)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new LocalHostRuntime(
            profile,
            operationCatalog,
            contractMap);
    }

    // Kirch: Captures and validates the composed runtime result before returning it.
    private static LocalHostBootstrapCompositionResult CreateCheckedResult(
        LocalHostRuntime runtime,
        LocalHostHandlerBootstrapResult bootstrapResult)
    {
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

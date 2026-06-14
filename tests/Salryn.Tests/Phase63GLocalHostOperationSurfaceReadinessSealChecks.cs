using Salryn.Core.Contracts;
using Salryn.Core.Deployment;
using Salryn.Core.Hosting;
using Salryn.Host.Local.Composition;
using Salryn.Host.Local.Contracts;
using Salryn.Host.Local.Readiness;

namespace Salryn.Tests;

// Kirch: Verifies Phase 63G Local Host operation surface readiness seal without real POS handlers.
public static class Phase63GLocalHostOperationSurfaceReadinessSealChecks
{
    // Kirch: This smoke check proves the local Host operation surface is aligned and sealable.
    public static void Run()
    {
        StoreDeploymentProfile profile = CreateProfile();
        HostOperationCatalog operationCatalog = HostOperationCatalog.CreateDefault();
        LocalHostOperationContractMap contractMap = LocalHostOperationContractMap.CreateDefault();
        LocalHostBootstrapComposer composer = new();
        LocalHostOperationSurfaceReadinessInspector inspector = new();

        LocalHostBootstrapCompositionResult composition = composer.Compose(
            profile,
            operationCatalog,
            contractMap);

        LocalHostOperationSurfaceReadinessReport readyReport = inspector.Inspect(
            composition,
            operationCatalog,
            contractMap);

        if (!readyReport.IsReady)
        {
            throw new InvalidOperationException("Local Host operation surface should be ready after default composition.");
        }

        if (readyReport.ExpectedOperationCount != operationCatalog.Descriptors.Count
            || readyReport.ReadyOperationCount != operationCatalog.Descriptors.Count)
        {
            throw new InvalidOperationException("Local Host operation surface readiness count should match the Host operation catalog.");
        }

        if (readyReport.Issues.Count != 0)
        {
            throw new InvalidOperationException("Ready Local Host operation surface should not report readiness issues.");
        }

        if (!readyReport.ReadyOperationNames.Contains(HostOperationNames.ProductLookup)
            || !readyReport.ReadyOperationNames.Contains(HostOperationNames.CompleteSale)
            || !readyReport.ReadyOperationNames.Contains(HostOperationNames.BackupHealth))
        {
            throw new InvalidOperationException("Readiness seal should include important Local Host operation names.");
        }

        LocalHostOperationContractMap incompleteContractMap = new(new[]
        {
            new LocalHostOperationContractDescriptor(
                OperationName: HostOperationNames.Login,
                OperationKind: HostOperationKind.Command,
                PayloadType: typeof(LocalHostLoginRequest),
                ResponseType: typeof(LocalHostLoginResponse))
        });

        LocalHostOperationSurfaceReadinessReport missingContractReport = inspector.Inspect(
            composition,
            operationCatalog,
            incompleteContractMap);

        if (missingContractReport.IsReady)
        {
            throw new InvalidOperationException("Readiness seal should reject an incomplete Local Host operation contract map.");
        }

        if (!missingContractReport.Issues.Any(issue =>
            issue.OperationName == HostOperationNames.ProductLookup
            && issue.Reason == LocalHostOperationSurfaceReadinessFailureReason.MissingContract))
        {
            throw new InvalidOperationException("Readiness seal should report missing operation contracts.");
        }

        LocalHostOperationSurfaceReadinessReport missingCompositionReport = inspector.Inspect(
            composition: null,
            operationCatalog,
            contractMap);

        if (missingCompositionReport.IsReady
            || !missingCompositionReport.Issues.Any(issue =>
                issue.Reason == LocalHostOperationSurfaceReadinessFailureReason.MissingComposition))
        {
            throw new InvalidOperationException("Readiness seal should reject a missing Local Host composition.");
        }
    }

    // Kirch: Creates a valid local host profile for operation surface readiness seal checks.
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
}

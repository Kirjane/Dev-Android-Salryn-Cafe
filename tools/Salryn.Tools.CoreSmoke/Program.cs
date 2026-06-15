using Salryn.Tests;

namespace Salryn.Tools.CoreSmoke;

// Kirch: Runs compile-check safety methods as executable smoke checks without adding a test framework package.
public static class Program
{
    // Kirch: Executes the Host/client boundary smoke checks that dotnet test only compiles today.
    public static void Main()
    {
        Phase61BStoreDeploymentCompileChecks.Run();
        Phase61CHostClientContractsCompileChecks.Run();
        Phase61DBackupMobileReadinessCompileChecks.Run();
        Phase62AHostBoundaryCompileChecks.Run();
        Phase62BHostOperationRegistrationCompileChecks.Run();
        Phase62CDescriptorAwareBoundaryCompileChecks.Run();
        Phase62EHostBoundaryRuntimeSafetyCompileChecks.Run();
        Phase63LocalHostRuntimeCompileChecks.Run();
        Phase63BLocalHostDispatchCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63CLocalHostLifecycleFaultSafetyCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63DLocalHostOperationContractMappingCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63ELocalHostHandlerBootstrapCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63FLocalHostBootstrapCompositionSmokeChecks.RunAsync().GetAwaiter().GetResult();
        Phase63GLocalHostOperationSurfaceReadinessSealChecks.Run();

        Phase64ALocalHostProductLookupHandlerCompileChecks.RunAsync().GetAwaiter().GetResult();

        Phase64BLocalHostProductLookupRuntimeRegistrationChecks.RunAsync().GetAwaiter().GetResult();

        Phase64CLocalHostProductLookupCompositionChecks.RunAsync().GetAwaiter().GetResult();

        Phase64DLocalHostProductLookupCompositionReadinessSealChecks.RunAsync().GetAwaiter().GetResult();

        Phase65AAppCompositionBoundaryPlanningChecks.Run();

        Phase65BAppLocalHostCompositionReferenceSliceChecks.Run();

        Phase65CAppLocalHostCompositionCreationGuardChecks.Run();

        Phase65DAppLocalHostCompositionReadinessSealChecks.Run();

        Phase65EAppLocalHostCompositionHolderSliceChecks.Run();

        Phase65FAppLocalHostCompositionHolderReadinessSealChecks.Run();

        Console.WriteLine("SALRYN Core smoke checks passed.");
    }
}

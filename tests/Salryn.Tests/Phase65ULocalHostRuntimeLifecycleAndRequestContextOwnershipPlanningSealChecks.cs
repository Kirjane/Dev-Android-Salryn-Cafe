namespace Salryn.Tests;

public static class Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string runtimeText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Runtime", "LocalHostRuntimeModels.cs");
        string hostOperationText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Hosting", "HostOperationRegistrationModels.cs");
        string hostBoundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Hosting", "HostBoundaryModels.cs");
        string hostContractsText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Contracts", "HostClientContracts.cs");
        string phase65UText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65U_LOCAL_HOST_RUNTIME_LIFECYCLE_AND_REQUEST_CONTEXT_OWNERSHIP_PLANNING_SEAL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(runtimeText, "public void Start(DateTimeOffset startedAt)", "Runtime activation API must remain explicit.");
        RequireContains(runtimeText, "if (currentState != LocalHostRuntimeState.Running)", "Runtime dispatch must remain gated by Running state.");
        RequireContains(runtimeText, "must be running before dispatch", "Stopped runtime dispatch must remain unavailable.");

        RequireContains(hostOperationText, "OperationName: HostOperationNames.ProductLookup", "ProductLookup operation descriptor must remain registered.");
        RequireContains(hostOperationText, "RequiresStoreIdentity: true", "ProductLookup must require store identity.");
        RequireContains(hostOperationText, "RequiresTerminalIdentity: true", "ProductLookup must require terminal identity.");
        RequireContains(hostOperationText, "RequiresAuthenticatedUser: true", "ProductLookup must require authenticated user identity.");
        RequireContains(hostOperationText, "RequiresCashierSession: false", "ProductLookup must not require cashier session.");

        RequireContains(hostBoundaryText, "MissingStoreIdentity", "Boundary must enforce store identity.");
        RequireContains(hostBoundaryText, "MissingTerminalIdentity", "Boundary must enforce terminal identity.");
        RequireContains(hostBoundaryText, "MissingAuthenticatedUserIdentity", "Boundary must enforce authenticated user identity.");

        RequireContains(hostContractsText, "public sealed record HostRequestContext", "Host request context contract must remain.");
        RequireContains(hostContractsText, "Guid TenantId", "Context must carry tenant identity.");
        RequireContains(hostContractsText, "Guid StoreId", "Context must carry store identity.");
        RequireContains(hostContractsText, "Guid? UserId", "Context must carry optional user identity.");
        RequireContains(hostContractsText, "Guid? RoleId", "Context must carry optional role identity.");
        RequireContains(hostContractsText, "Guid? TerminalId", "Context must carry optional terminal identity.");
        RequireContains(hostContractsText, "string? DeviceKey", "Context must carry optional device key.");

        RequireContains(holderText, "Holds a validated Local Host composition result without starting or consuming runtime behavior.", "Passive holder purpose must remain.");
        RequireNotContains(holderText, ".Start(", "Passive holder must not start runtime.");
        RequireNotContains(holderText, ".Stop(", "Passive holder must not stop runtime.");

        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "AppServices must still own holder reference.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider);", "AppServices must still wire holder-aware adapter.");
        RequireNotContains(appServicesText, "new HostRequestContext(", "AppServices must not directly build HostRequestContext.");
        RequireNotContains(appServicesText, "CreateStoppedRuntimeBridgeContext()", "AppServices must not own placeholder request context creation.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not start runtime.");
        RequireNotContains(appServicesText, ".Stop(", "AppServices must not stop runtime.");

        RequireNotContains(adapterText, "CreateStoppedRuntimeBridgeContext()", "Adapter placeholder context must be removed after provider consumption.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T dispatch bridge must remain.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");

        RequireContains(phase65UText, "A separate request context provider should own HostRequestContext creation later.", "65U must define future context owner.");
        RequireContains(coreSmokeText, "Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks.Run();", "CoreSmoke must still run 65U.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
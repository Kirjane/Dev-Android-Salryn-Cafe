namespace Salryn.Tests;

// Kirch: Seals the 65U architecture decision before any Local Host runtime activation or request identity wiring is implemented.
public static class Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string runtimeText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Runtime", "LocalHostRuntimeModels.cs");
        string compositionText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Composition", "LocalHostBootstrapCompositionModels.cs");
        string hostOperationText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Hosting", "HostOperationRegistrationModels.cs");
        string hostBoundaryText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Hosting", "HostBoundaryModels.cs");
        string hostContractsText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Contracts", "HostClientContracts.cs");
        string deploymentText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Deployment", "StoreHostDeploymentModels.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string phase65TText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65T_APP_LOCAL_HOST_PRODUCT_LOOKUP_DISPATCH_BRIDGE.md");
        string phase65UText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65U_LOCAL_HOST_RUNTIME_LIFECYCLE_AND_REQUEST_CONTEXT_OWNERSHIP_PLANNING_SEAL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(runtimeText, "public void Start(DateTimeOffset startedAt)", "Runtime Start API must exist for future lifecycle work.");
        RequireContains(runtimeText, "if (currentState != LocalHostRuntimeState.Running)", "Runtime dispatch must remain gated by Running state.");
        RequireContains(runtimeText, "Local Host runtime must be running before dispatch.", "Runtime must still fail closed when stopped.");
        RequireContains(runtimeText, "Local Host handlers can only be registered while runtime is stopped.", "Handler registration must remain stopped-state only.");
        RequireContains(runtimeText, "RuntimeAlreadyFaulted", "Runtime faulted guard must remain.");
        RequireContains(runtimeText, "RuntimeNotRunning", "Runtime stopped dispatch guard must remain.");

        RequireContains(compositionText, "result.Snapshot.State == LocalHostRuntimeState.Stopped", "Composition validity must remain stopped-runtime based.");
        RequireContains(compositionText, "ComposeWithProductLookup", "Explicit ProductLookup composition path must remain.");
        RequireContains(compositionText, "CreateCheckedResult", "Composition must remain guarded before holder use.");

        RequireContains(holderText, "Holds a validated Local Host composition result without starting or consuming runtime behavior.", "Passive holder purpose must remain.");
        RequireContains(holderText, "LocalHostBootstrapCompositionRules.IsCompositionValid(composition)", "Holder must keep stopped-composition validation.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must keep read-only composition.");
        RequireNotContains(holderText, ".Start(", "Passive holder must not start runtime.");
        RequireNotContains(holderText, "Runtime.Start", "Passive holder must not start runtime.");

        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "AppServices must still own the holder reference.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "AppServices must still create holder through guarded factory.");
        RequireContains(appServicesText, "LocalHostProductLookupGateway =", "AppServices must still own the gateway reference.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder);", "AppServices must still wire holder-aware adapter.");
        RequireContains(appServicesText, "Terminal: null", "Current standalone profile still lacks terminal identity, so request context cannot be completed here yet.");
        RequireNotContains(appServicesText, ".Start(", "65U must not start runtime in AppServices.");
        RequireNotContains(appServicesText, "Runtime.Start", "65U must not start runtime in AppServices.");
        RequireNotContains(appServicesText, "StartLocalHost", "65U must not add lifecycle activation method to AppServices yet.");
        RequireNotContains(appServicesText, "HostRequestContext", "65U must not make AppServices build request context yet.");

        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T dispatch bridge must remain.");
        RequireContains(adapterText, "CreateStoppedRuntimeBridgeContext()", "65T placeholder context must remain isolated until request context provider exists.");
        RequireContains(adapterText, "Guid.Empty", "65T placeholder identity must remain obvious before replacement.");
        RequireNotContains(adapterText, ".Start(", "Adapter must never start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "Adapter must never start runtime.");
        RequireNotContains(adapterText, "StartLocalHost", "Adapter must not own lifecycle.");
        RequireNotContains(adapterText, "SalrynLocalHostRuntimeLifecycle", "Adapter must not own lifecycle object.");

        RequireContains(hostOperationText, "OperationName: HostOperationNames.ProductLookup", "ProductLookup descriptor must remain in catalog.");
        RequireContains(hostOperationText, "RequiresStoreIdentity: true", "Host operations must require store identity.");
        RequireContains(hostOperationText, "RequiresTerminalIdentity: true", "ProductLookup planning depends on terminal identity.");
        RequireContains(hostOperationText, "RequiresAuthenticatedUser: true", "ProductLookup planning depends on authenticated user identity.");
        RequireContains(hostOperationText, "RequiresCashierSession: false", "ProductLookup must not require cashier session.");

        RequireContains(hostBoundaryText, "MissingStoreIdentity", "Boundary must enforce store identity.");
        RequireContains(hostBoundaryText, "MissingTerminalIdentity", "Boundary must enforce terminal identity.");
        RequireContains(hostBoundaryText, "MissingAuthenticatedUserIdentity", "Boundary must enforce authenticated user identity.");
        RequireContains(hostBoundaryText, "HostClientContractRules.HasStoreIdentity(context)", "Boundary must validate tenant and store identity through Core contract rules.");
        RequireContains(hostBoundaryText, "MissingStoreIdentity", "Boundary must report missing store identity.");
        RequireContains(hostBoundaryText, "HostClientContractRules.HasTerminalIdentity(context)", "Boundary must validate terminal identity through Core contract rules.");
        RequireContains(hostBoundaryText, "HostClientContractRules.HasAuthenticatedCommandIdentity(context)", "Boundary must validate authenticated user identity through Core contract rules.");

        RequireContains(hostContractsText, "public sealed record HostRequestContext", "Host request context contract must remain.");
        RequireContains(hostContractsText, "Guid TenantId", "Context must carry tenant identity.");
        RequireContains(hostContractsText, "Guid StoreId", "Context must carry store identity.");
        RequireContains(hostContractsText, "Guid? UserId", "Context must carry optional user identity.");
        RequireContains(hostContractsText, "Guid? RoleId", "Context must carry optional role identity.");
        RequireContains(hostContractsText, "Guid? TerminalId", "Context must carry optional terminal identity.");
        RequireContains(hostContractsText, "string? DeviceKey", "Context must carry optional device key.");

        RequireContains(deploymentText, "public sealed record TerminalIdentity", "Deployment model must retain terminal identity.");
        RequireContains(deploymentText, "Guid TerminalId", "Terminal identity must carry terminal id.");
        RequireContains(deploymentText, "string DeviceKey", "Terminal identity must carry device key.");
        RequireContains(deploymentText, "TerminalIdentity? Terminal", "Deployment profile must retain optional terminal identity.");

        string[] programShellForbidden =
        {
            "CompositionLocalHostProductLookupGatewayAdapter",
            "IAppLocalHostProductLookupGateway",
            "TryLookupByBarcodeAsync",
            "LocalHostProductLookup",
            "ProductLookup",
            "HostRequestContext",
            "Runtime.Start",
            ".Start(",
            "SalrynLocalHostRuntimeLifecycle",
            "SalrynLocalHostRequestContext"
        };

        foreach (string marker in programShellForbidden)
        {
            RequireNotContains(programText, marker, $"Program must remain free of 65U marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65U marker: {marker}");
        }

        RequireContains(phase65TText, "Phase 65U may plan Local Host runtime lifecycle activation or request identity context", "65T must point to 65U planning.");
        RequireContains(phase65UText, "Status: Planning seal only.", "65U handoff must be planning-only.");
        RequireContains(phase65UText, "Runtime activation must not belong to the adapter.", "65U must explicitly reject adapter lifecycle ownership.");
        RequireContains(phase65UText, "Runtime activation must not belong to the passive holder.", "65U must explicitly reject passive holder lifecycle ownership.");
        RequireContains(phase65UText, "A separate lifecycle holder or lifecycle service should own Start and Stop later.", "65U must define future lifecycle owner.");
        RequireContains(phase65UText, "A separate request context provider should own HostRequestContext creation later.", "65U must define future context owner.");
        RequireContains(phase65UText, "No runtime start was added.", "65U must preserve no runtime start.");
        RequireContains(phase65UText, "No production source was changed.", "65U must remain tests/handoff/CoreSmoke only.");

        RequireContains(coreSmokeText, "Phase65TAppLocalHostProductLookupDispatchBridgeChecks.Run();", "CoreSmoke must still run 65T.");
        RequireContains(coreSmokeText, "Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks.Run();", "CoreSmoke must run 65U.");

        int index65T = coreSmokeText.IndexOf("Phase65TAppLocalHostProductLookupDispatchBridgeChecks.Run();", StringComparison.Ordinal);
        int index65U = coreSmokeText.IndexOf("Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks.Run();", StringComparison.Ordinal);

        if (index65T < 0 || index65U < 0 || index65U < index65T)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65U must run after Phase 65T.");
        }
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
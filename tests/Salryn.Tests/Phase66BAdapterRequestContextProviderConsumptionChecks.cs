namespace Salryn.Tests;

public static class Phase66BAdapterRequestContextProviderConsumptionChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string providerText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string lifecycleText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66B_ADAPTER_REQUEST_CONTEXT_PROVIDER_CONSUMPTION.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(adapterText, "private readonly SalrynLocalHostRequestContextProvider? requestContextProvider;", "Adapter must hold the passive request context provider.");
        RequireContains(adapterText, "SalrynLocalHostRequestContextProvider requestContextProvider", "Adapter must expose provider-aware constructor.");
        RequireContains(adapterText, "this.requestContextProvider = requestContextProvider;", "Adapter must store provider.");
        RequireContains(adapterText, "requestProvider.TryCreateAuthenticatedContext(", "Adapter must consume provider for context creation.");
        RequireNotContains(adapterText, "userId: null", "Adapter must not use null user identity after 66D.");
        RequireNotContains(adapterText, "roleId: null", "Adapter must not use null role identity after 66D.");
        RequireContains(adapterText, "cashierSessionId: null", "Adapter must not invent cashier session identity.");
        RequireContains(adapterText, "out HostRequestContext? context", "Adapter must receive provider-created context.");
        RequireContains(adapterText, "Context: context", "Adapter must pass provider-created context into Host envelope.");
        RequireContains(adapterText, "request context is not available from the passive provider yet", "Adapter must return LocalHostUnavailable when provider cannot create context.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "Adapter must keep existing dispatch bridge.");
        RequireContains(adapterText, "RequestId: Guid.NewGuid()", "Adapter may still create request envelope id.");

        RequireNotContains(adapterText, "CreateStoppedRuntimeBridgeContext", "Adapter placeholder context factory must be removed.");
        RequireNotContains(adapterText, "TenantId: Guid.Empty", "Adapter must not fake tenant identity.");
        RequireNotContains(adapterText, "StoreId: Guid.Empty", "Adapter must not fake store identity.");
        RequireNotContains(adapterText, "UserId: null,", "Adapter must not place null user directly into HostRequestContext.");
        RequireNotContains(adapterText, "RoleId: null,", "Adapter must not place null role directly into HostRequestContext.");
        RequireNotContains(adapterText, "TerminalId: null", "Adapter must not fake missing terminal identity.");
        RequireNotContains(adapterText, "DeviceKey: null", "Adapter must not fake missing device key.");
        RequireNotContains(adapterText, "new HostRequestContext(", "Adapter must not construct HostRequestContext directly.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");
        RequireNotContains(adapterText, "HttpClient", "Adapter must not add HTTP transport.");
        RequireNotContains(adapterText, "Socket", "Adapter must not add socket transport.");
        RequireNotContains(adapterText, "Tcp", "Adapter must not add TCP transport.");
        RequireNotContains(adapterText, "Udp", "Adapter must not add UDP transport.");

        RequireContains(providerText, "public bool TryCreateAuthenticatedContext(", "Provider must remain the context owner.");
        RequireContains(providerText, "context = new HostRequestContext(", "Provider must own HostRequestContext creation.");
        RequireContains(providerText, "TenantId: localHostProfile.Store.TenantId", "Provider must use real profile tenant.");
        RequireContains(providerText, "StoreId: localHostProfile.Store.StoreId", "Provider must use real profile store.");
        RequireContains(providerText, "TerminalId: terminal.TerminalId", "Provider must use real profile terminal id.");
        RequireContains(providerText, "DeviceKey: terminal.DeviceKey", "Provider must use real profile device key.");
        RequireNotContains(providerText, "CurrentAppSession", "Provider must not directly depend on UI session.");
        RequireNotContains(providerText, "CheckoutSessionContext", "Provider must not directly depend on checkout session.");

        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "AppServices must pass holder and provider into adapter.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder);", "AppServices must not use old holder-only adapter constructor.");
        RequireContains(appServicesText, "new SalrynLocalHostRequestContextProvider(localHostProfile);", "AppServices must still own provider creation.");
        RequireContains(appServicesText, "new TerminalIdentity(", "AppServices must still have standalone terminal identity.");
        RequireNotContains(appServicesText, "Terminal: null", "AppServices must not regress terminal identity.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not start runtime.");
        RequireNotContains(appServicesText, ".Stop(", "AppServices must not stop runtime.");
        RequireNotContains(appServicesText, "DispatchAsync", "AppServices must not dispatch.");

        RequireContains(lifecycleText, "GetSnapshot()", "Lifecycle holder must remain passive.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStart(DateTimeOffset startedAt)", "Lifecycle holder must expose controlled runtime start after 66G1.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStop()", "Lifecycle holder must expose controlled runtime stop after 66G1.");
        RequireNotContains(lifecycleText, "DispatchAsync", "Lifecycle holder must not dispatch.");

        foreach (string marker in new[]
        {
            "SalrynLocalHostRequestContextProvider",
            "SalrynLocalHostRuntimeLifecycleHolder",
            "HostRequestContext",
            "TerminalIdentity",
            "LocalHostProductLookup",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop(",
            "DispatchAsync"
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of marker: {marker}");
        }

        RequireContains(handoffText, "Status: Adapter request context provider consumption only.", "66B handoff must mark adapter context consumption only.");
        RequireContains(handoffText, "No runtime activation was added.", "66B handoff must block runtime activation.");
        RequireContains(handoffText, "No Program or Shell behavior was changed.", "66B handoff must protect Program and Shell.");
        RequireContains(handoffText, "The adapter now fails closed when the provider cannot create context.", "66B handoff must document fail-closed behavior.");

        RequireContains(coreSmokeText, "Phase65ZIdentityBoundaryWiringChecks.Run();", "CoreSmoke must still run 65Z.");
        RequireContains(coreSmokeText, "Phase66BAdapterRequestContextProviderConsumptionChecks.Run();", "CoreSmoke must run 66B.");

        int index65Z = coreSmokeText.IndexOf("Phase65ZIdentityBoundaryWiringChecks.Run();", StringComparison.Ordinal);
        int index66B = coreSmokeText.IndexOf("Phase66BAdapterRequestContextProviderConsumptionChecks.Run();", StringComparison.Ordinal);

        if (index65Z < 0 || index66B < 0 || index66B < index65Z)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66B must run after Phase 65Z.");
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
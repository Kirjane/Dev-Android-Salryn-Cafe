namespace Salryn.Tests;

public static class Phase65XPassiveLocalHostProvidersAppServicesWiringChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string providerText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string lifecycleHolderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65X_PASSIVE_LOCAL_HOST_PROVIDERS_APPSERVICES_WIRING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(providerText, "public sealed class SalrynLocalHostRequestContextProvider", "65X must add request context provider.");
        RequireContains(providerText, "private readonly StoreDeploymentProfile localHostProfile;", "Provider must depend on deployment profile.");
        RequireContains(providerText, "public StoreDeploymentProfile Profile => localHostProfile;", "Provider must expose profile read-only.");
        RequireContains(providerText, "public bool HasStoreIdentity =>", "Provider must expose store readiness.");
        RequireContains(providerText, "public bool HasTerminalIdentity =>", "Provider must expose terminal readiness.");
        RequireContains(providerText, "public bool CanCreateAuthenticatedContext(Guid? userId, Guid? roleId)", "Provider must require supplied user and role identity.");
        RequireContains(providerText, "public bool TryCreateAuthenticatedContext(", "Provider must expose guarded context creation.");
        RequireContains(providerText, "out HostRequestContext? context", "Provider must return context through guarded try pattern.");
        RequireContains(providerText, "TerminalIdentity? terminal = localHostProfile.Terminal;", "Provider must use deployment terminal only.");
        RequireContains(providerText, "context = new HostRequestContext(", "Provider is the only new App composition type allowed to create HostRequestContext.");
        RequireContains(providerText, "UserId: userId.GetValueOrDefault()", "Provider must use supplied user id.");
        RequireContains(providerText, "RoleId: roleId.GetValueOrDefault()", "Provider must use supplied role id.");
        RequireContains(providerText, "CashierSessionId: NormalizeOptionalIdentity(cashierSessionId)", "Provider must normalize optional cashier session.");
        RequireContains(providerText, "TerminalId: terminal.TerminalId", "Provider must use deployment terminal id.");
        RequireContains(providerText, "DeviceKey: terminal.DeviceKey", "Provider must use deployment device key.");

        foreach (string forbidden in new[]
        {
            ".Start(",
            "Runtime.Start",
            ".Stop(",
            "Runtime.Stop",
            "DispatchAsync",
            "Microsoft.Data.Sqlite",
            "HttpClient",
            "Socket",
            "Tcp",
            "Udp",
            "CurrentAppSession",
            "CheckoutSessionContext",
            "Guid.Parse(\"",
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333"
        })
        {
            RequireNotContains(providerText, forbidden, $"Provider must not contain forbidden marker: {forbidden}");
        }

        RequireContains(lifecycleHolderText, "localHostCompositionHolder.Composition.Runtime.GetSnapshot();", "Lifecycle holder must remain snapshot-only.");
        RequireContains(lifecycleHolderText, "public SalrynLocalHostRuntimeLifecycleResult TryStart(DateTimeOffset startedAt)", "Lifecycle holder must expose controlled runtime start after 66G1.");
        RequireContains(lifecycleHolderText, "public SalrynLocalHostRuntimeLifecycleResult TryStop()", "Lifecycle holder must expose controlled runtime stop after 66G1.");
        RequireNotContains(lifecycleHolderText, "DispatchAsync", "Lifecycle holder must not dispatch.");

        RequireContains(appServicesText, "public SalrynLocalHostRuntimeLifecycleHolder LocalHostRuntimeLifecycleHolder { get; }", "AppServices must expose passive lifecycle holder.");
        RequireContains(appServicesText, "public SalrynLocalHostRequestContextProvider LocalHostRequestContextProvider { get; }", "AppServices must expose passive request context provider.");
        RequireContains(appServicesText, "LocalHostRuntimeLifecycleHolder =", "AppServices must assign lifecycle holder.");
        RequireContains(appServicesText, "new SalrynLocalHostRuntimeLifecycleHolder(LocalHostProductLookupCompositionHolder);", "Lifecycle holder must be wired from passive holder.");
        RequireContains(appServicesText, "LocalHostRequestContextProvider =", "AppServices must assign request context provider.");
        RequireContains(appServicesText, "new SalrynLocalHostRequestContextProvider(localHostProfile);", "Request context provider must be wired from deployment profile.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "Adapter wiring must remain holder-based.");
        RequireNotContains(appServicesText, "new HostRequestContext(", "AppServices must not directly create HostRequestContext.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not start runtime.");
        RequireNotContains(appServicesText, ".Stop(", "AppServices must not stop runtime.");
        RequireNotContains(appServicesText, "DispatchAsync", "AppServices must not dispatch.");

        int holderIndex = appServicesText.IndexOf("LocalHostProductLookupCompositionHolder =", StringComparison.Ordinal);
        int lifecycleIndex = appServicesText.IndexOf("LocalHostRuntimeLifecycleHolder =", StringComparison.Ordinal);
        int providerIndex = appServicesText.IndexOf("LocalHostRequestContextProvider =", StringComparison.Ordinal);
        int gatewayIndex = appServicesText.IndexOf("LocalHostProductLookupGateway =", StringComparison.Ordinal);

        if (holderIndex < 0 || lifecycleIndex < 0 || providerIndex < 0 || gatewayIndex < 0)
        {
            throw new InvalidOperationException("AppServices provider wiring markers are incomplete.");
        }

        if (!(holderIndex < lifecycleIndex && lifecycleIndex < providerIndex && providerIndex < gatewayIndex))
        {
            throw new InvalidOperationException("AppServices provider wiring order is invalid.");
        }

        RequireContains(adapterText, "SalrynLocalHostRequestContextProvider", "Adapter must consume request context provider after 66B.");
        RequireNotContains(adapterText, "CreateStoppedRuntimeBridgeContext()", "Adapter placeholder context must be removed after provider consumption.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");

        foreach (string marker in new[]
        {
            "SalrynLocalHostRequestContextProvider",
            "SalrynLocalHostRuntimeLifecycleHolder",
            "LocalHostProductLookup",
            "HostRequestContext",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop(",
            "DispatchAsync"
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of 65X marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65X marker: {marker}");
        }

        RequireContains(handoffText, "Status: Combined passive wiring only.", "65X handoff must mark passive wiring status.");
        RequireContains(handoffText, "No runtime activation was added.", "65X handoff must block activation.");
        RequireContains(handoffText, "No adapter consumption of the request context provider was added.", "65X handoff must block adapter consumption.");
        RequireContains(handoffText, "No Program or Shell behavior was changed.", "65X handoff must protect Program and Shell.");

        RequireContains(coreSmokeText, "Phase65VLocalHostRuntimeLifecycleHolderShellChecks.Run();", "CoreSmoke must still run 65V.");
        RequireContains(coreSmokeText, "Phase65XPassiveLocalHostProvidersAppServicesWiringChecks.Run();", "CoreSmoke must run 65X.");

        int index65V = coreSmokeText.IndexOf("Phase65VLocalHostRuntimeLifecycleHolderShellChecks.Run();", StringComparison.Ordinal);
        int index65X = coreSmokeText.IndexOf("Phase65XPassiveLocalHostProvidersAppServicesWiringChecks.Run();", StringComparison.Ordinal);

        if (index65V < 0 || index65X < 0 || index65X < index65V)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65X must run after Phase 65V.");
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
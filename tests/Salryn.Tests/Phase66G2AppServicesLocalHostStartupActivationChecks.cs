namespace Salryn.Tests;

public static class Phase66G2AppServicesLocalHostStartupActivationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string lifecycleText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string requestProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string sessionProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostSessionIdentityProvider.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G2_APPSERVICES_LOCAL_HOST_STARTUP_ACTIVATION.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(appServicesText, "public SalrynLocalHostRuntimeLifecycleResult LocalHostRuntimeStartupResult { get; }", "AppServices must expose startup result.");
        RequireContains(appServicesText, "LocalHostRuntimeLifecycleHolder.TryStart(DateTimeOffset.UtcNow);", "AppServices must start runtime through lifecycle holder.");
        RequireContains(appServicesText, "if (!LocalHostRuntimeStartupResult.IsSuccess)", "AppServices must fail closed on startup failure.");
        RequireContains(appServicesText, "Local Host runtime failed to start during app service composition", "AppServices must report startup failure.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "Adapter wiring must remain unchanged.");

        RequireNotContains(appServicesText, "Composition.Runtime.Start", "AppServices must not directly call runtime Start.");
        RequireNotContains(appServicesText, "Runtime.Start", "AppServices must not directly call Runtime.Start.");
        RequireNotContains(appServicesText, "Runtime.Stop", "AppServices must not stop runtime.");
        RequireNotContains(appServicesText, "DispatchAsync", "AppServices must not dispatch.");
        RequireNotContains(appServicesText, "HttpClient", "AppServices must not add HTTP.");
        RequireNotContains(appServicesText, "Socket", "AppServices must not add sockets.");
        RequireNotContains(appServicesText, "Tcp", "AppServices must not add TCP.");
        RequireNotContains(appServicesText, "Udp", "AppServices must not add UDP.");

        RequireContains(lifecycleText, "localHostCompositionHolder.Composition.Runtime.Start(startedAt);", "Lifecycle holder remains the only app boundary directly calling Start.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStop()", "Lifecycle holder still owns controlled stop.");

        RequireNotContains(adapterText, "TryStart", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, "TryStop", "Adapter must not stop runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");
        RequireContains(adapterText, "DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "Adapter remains dispatch-only.");

        RequireNotContains(requestProviderText, "TryStart", "Request provider must not start runtime.");
        RequireNotContains(requestProviderText, ".Start(", "Request provider must not start runtime.");
        RequireNotContains(sessionProviderText, "TryStart", "Session provider must not start runtime.");
        RequireNotContains(sessionProviderText, ".Start(", "Session provider must not start runtime.");

        foreach (string marker in new[]
        {
            "TryStart",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop(",
            "DispatchAsync",
            "LocalHostProductLookup"
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of runtime marker: {marker}");
        }

        RequireNotContains(shellText, "TryStart", "Shell must not start runtime.");
        RequireNotContains(shellText, "Runtime.Start", "Shell must not start runtime.");
        RequireNotContains(shellText, "Runtime.Stop", "Shell must not stop runtime.");
        RequireNotContains(shellText, "DispatchAsync", "Shell must not dispatch.");
        RequireContains(shellText, "_services.LocalHostSessionIdentityProvider.SetSession(session);", "Shell may only publish session.");

        RequireContains(handoffText, "Status: AppServices startup activation only.", "66G2 handoff must mark scope.");
        RequireContains(handoffText, "Runtime now starts during AppServices composition through TryStart.", "66G2 handoff must document startup.");
        RequireContains(handoffText, "No transport was added.", "66G2 handoff must block transport.");

        RequireContains(coreSmokeText, "Phase66G1LocalHostRuntimeLifecycleActivationChecks.Run();", "CoreSmoke must still run 66G1.");
        RequireContains(coreSmokeText, "Phase66G2AppServicesLocalHostStartupActivationChecks.Run();", "CoreSmoke must run 66G2.");
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
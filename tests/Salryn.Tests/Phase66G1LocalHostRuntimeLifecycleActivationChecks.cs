namespace Salryn.Tests;

public static class Phase66G1LocalHostRuntimeLifecycleActivationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string lifecycleText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string requestProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string sessionProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostSessionIdentityProvider.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string runtimeText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Runtime", "LocalHostRuntimeModels.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G1_LOCAL_HOST_RUNTIME_LIFECYCLE_ACTIVATION.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(lifecycleText, "public sealed record SalrynLocalHostRuntimeLifecycleResult", "66G1 must add lifecycle result record.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStart(DateTimeOffset startedAt)", "Lifecycle holder must expose controlled start.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStop()", "Lifecycle holder must expose controlled stop.");
        RequireContains(lifecycleText, "localHostCompositionHolder.Composition.Runtime.Start(startedAt);", "Lifecycle holder must be the only App boundary that can start runtime.");
        RequireContains(lifecycleText, "localHostCompositionHolder.Composition.Runtime.Stop();", "Lifecycle holder must be the only App boundary that can stop runtime.");
        RequireContains(lifecycleText, "before.State == LocalHostRuntimeState.Faulted", "TryStart must fail closed before starting a faulted runtime.");
        RequireContains(lifecycleText, "catch (InvalidOperationException exception)", "Lifecycle holder must translate runtime lifecycle exceptions into result state.");
        RequireContains(lifecycleText, "after.State != LocalHostRuntimeState.Running", "TryStart must verify running state after activation.");
        RequireContains(lifecycleText, "after.State != LocalHostRuntimeState.Stopped", "TryStop must verify stopped state after stop.");
        RequireContains(lifecycleText, "localHostCompositionHolder.Composition.Runtime.GetSnapshot();", "Lifecycle holder must continue exposing live runtime snapshots.");
        RequireNotContains(lifecycleText, "DispatchAsync", "Lifecycle holder must not dispatch.");
        RequireNotContains(lifecycleText, "HostRequestContext", "Lifecycle holder must not build request context.");
        RequireNotContains(lifecycleText, "HttpClient", "Lifecycle holder must not add HTTP transport.");
        RequireNotContains(lifecycleText, "Socket", "Lifecycle holder must not add socket transport.");
        RequireNotContains(lifecycleText, "Tcp", "Lifecycle holder must not add TCP transport.");
        RequireNotContains(lifecycleText, "Udp", "Lifecycle holder must not add UDP transport.");
        RequireNotContains(lifecycleText, "Microsoft.Data.Sqlite", "Lifecycle holder must not access SQLite.");

        RequireContains(runtimeText, "public void Start(DateTimeOffset startedAt)", "Runtime start API must remain synchronous and explicit.");
        RequireContains(runtimeText, "public void Stop()", "Runtime stop API must remain synchronous and explicit.");
        RequireContains(runtimeText, "throw new InvalidOperationException(\"Faulted Local Host runtime must be stopped before restart.\");", "Runtime must still protect faulted restart.");

        RequireContains(appServicesText, "new SalrynLocalHostRuntimeLifecycleHolder(LocalHostProductLookupCompositionHolder);", "AppServices must still create lifecycle holder.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not automatically start runtime in 66G1.");
        RequireNotContains(appServicesText, ".Stop(", "AppServices must not automatically stop runtime in 66G1.");
        RequireNotContains(appServicesText, "DispatchAsync", "AppServices must not dispatch.");

        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");
        RequireNotContains(adapterText, "SalrynLocalHostRuntimeLifecycleHolder", "Adapter must not own lifecycle holder.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "Adapter remains dispatch-only.");

        RequireNotContains(requestProviderText, ".Start(", "Request context provider must not start runtime.");
        RequireNotContains(requestProviderText, ".Stop(", "Request context provider must not stop runtime.");
        RequireNotContains(sessionProviderText, ".Start(", "Session identity provider must not start runtime.");
        RequireNotContains(sessionProviderText, ".Stop(", "Session identity provider must not stop runtime.");

        foreach (string marker in new[]
        {
            "SalrynLocalHostRuntimeLifecycleHolder",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop(",
            "DispatchAsync",
            "LocalHostProductLookup"
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of lifecycle marker: {marker}");
        }

        RequireNotContains(shellText, "Runtime.Start", "Shell must not start runtime.");
        RequireNotContains(shellText, "Runtime.Stop", "Shell must not stop runtime.");
        RequireNotContains(shellText, "DispatchAsync", "Shell must not dispatch.");
        RequireContains(shellText, "_services.LocalHostSessionIdentityProvider.SetSession(session);", "Shell may only keep passive session publish.");

        RequireContains(handoffText, "Status: Controlled lifecycle activation boundary only.", "66G1 handoff must mark controlled lifecycle boundary only.");
        RequireContains(handoffText, "No automatic runtime activation was added.", "66G1 handoff must block automatic startup.");
        RequireContains(handoffText, "Program remains clean.", "66G1 handoff must protect Program.");
        RequireContains(handoffText, "Adapters and providers still do not start or stop runtime.", "66G1 handoff must protect adapter/provider boundaries.");

        RequireContains(coreSmokeText, "Phase66G0AuthenticatedRoleIdPropagationChecks.Run();", "CoreSmoke must still run 66G0.");
        RequireContains(coreSmokeText, "Phase66G1LocalHostRuntimeLifecycleActivationChecks.Run();", "CoreSmoke must run 66G1.");

        int index66G0 = coreSmokeText.IndexOf("Phase66G0AuthenticatedRoleIdPropagationChecks.Run();", StringComparison.Ordinal);
        int index66G1 = coreSmokeText.IndexOf("Phase66G1LocalHostRuntimeLifecycleActivationChecks.Run();", StringComparison.Ordinal);

        if (index66G0 < 0 || index66G1 < 0 || index66G1 < index66G0)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G1 must run after Phase 66G0.");
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
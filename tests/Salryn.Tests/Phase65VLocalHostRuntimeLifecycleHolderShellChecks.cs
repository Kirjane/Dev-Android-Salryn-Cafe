namespace Salryn.Tests;

public static class Phase65VLocalHostRuntimeLifecycleHolderShellChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string lifecycleHolderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string passiveHolderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string runtimeText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Runtime", "LocalHostRuntimeModels.cs");
        string phase65VText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65V_LOCAL_HOST_RUNTIME_LIFECYCLE_HOLDER_SHELL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(lifecycleHolderText, "public sealed class SalrynLocalHostRuntimeLifecycleHolder", "65V must keep lifecycle holder shell.");
        RequireContains(lifecycleHolderText, "private readonly SalrynLocalHostCompositionHolder localHostCompositionHolder;", "65V holder must depend on passive holder.");
        RequireContains(lifecycleHolderText, "localHostCompositionHolder.Composition.Runtime.GetSnapshot();", "65V holder must observe runtime snapshot only.");
        RequireContains(lifecycleHolderText, "public LocalHostRuntimeState State => Snapshot.State;", "65V holder must expose state.");
        RequireContains(lifecycleHolderText, "public bool IsRunning => State == LocalHostRuntimeState.Running;", "65V holder must expose running status.");
        RequireContains(lifecycleHolderText, "public bool IsStopped => State == LocalHostRuntimeState.Stopped;", "65V holder must expose stopped status.");
        RequireContains(lifecycleHolderText, "public bool IsFaulted => State == LocalHostRuntimeState.Faulted;", "65V holder must expose faulted status.");

        RequireNotContains(lifecycleHolderText, ".Start(", "65V holder shell must not activate runtime.");
        RequireNotContains(lifecycleHolderText, "Runtime.Start", "65V holder shell must not activate runtime.");
        RequireNotContains(lifecycleHolderText, ".Stop(", "65V holder shell must not stop runtime.");
        RequireNotContains(lifecycleHolderText, "Runtime.Stop", "65V holder shell must not stop runtime.");
        RequireNotContains(lifecycleHolderText, "DispatchAsync", "65V holder shell must not dispatch.");
        RequireNotContains(lifecycleHolderText, "HostRequestContext", "65V holder shell must not build request context.");

        RequireContains(passiveHolderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Passive holder must remain read-only.");
        RequireNotContains(passiveHolderText, ".Start(", "Passive holder must not activate runtime.");
        RequireNotContains(passiveHolderText, ".Stop(", "Passive holder must not stop runtime.");

        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider);", "Existing adapter wiring must remain.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not activate runtime.");
        RequireNotContains(appServicesText, ".Stop(", "AppServices must not stop runtime.");

        RequireNotContains(adapterText, "SalrynLocalHostRuntimeLifecycleHolder", "Adapter must not own lifecycle holder.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not activate runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");

        RequireContains(runtimeText, "public LocalHostRuntimeSnapshot GetSnapshot()", "Runtime snapshot API must remain.");
        RequireContains(runtimeText, "public void Start(DateTimeOffset startedAt)", "Runtime activation API exists but must remain controlled.");
        RequireContains(runtimeText, "public void Stop()", "Runtime stop API exists but must remain controlled.");

        foreach (string marker in new[]
        {
            "SalrynLocalHostRuntimeLifecycleHolder",
            "LocalHostRuntime",
            "LocalHostProductLookup",
            "ProductLookup",
            "HostRequestContext",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop("
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of marker: {marker}");
        }

        RequireContains(phase65VText, "Status: Shell only.", "65V handoff must mark shell-only status.");
        RequireContains(phase65VText, "No runtime activation was added.", "65V handoff must block activation.");
        RequireContains(phase65VText, "No runtime stop was added.", "65V handoff must block stop.");
        RequireContains(coreSmokeText, "Phase65VLocalHostRuntimeLifecycleHolderShellChecks.Run();", "CoreSmoke must still run 65V.");
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
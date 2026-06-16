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
        string phase65UText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65U_LOCAL_HOST_RUNTIME_LIFECYCLE_AND_REQUEST_CONTEXT_OWNERSHIP_PLANNING_SEAL.md");
        string phase65VText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65V_LOCAL_HOST_RUNTIME_LIFECYCLE_HOLDER_SHELL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(lifecycleHolderText, "public sealed class SalrynLocalHostRuntimeLifecycleHolder", "65V must add lifecycle holder shell.");
        RequireContains(lifecycleHolderText, "private readonly SalrynLocalHostCompositionHolder localHostCompositionHolder;", "65V holder must depend on passive holder.");
        RequireContains(lifecycleHolderText, "public SalrynLocalHostRuntimeLifecycleHolder(", "65V holder must expose constructor.");
        RequireContains(lifecycleHolderText, "SalrynLocalHostCompositionHolder localHostCompositionHolder", "65V holder constructor must accept passive holder.");
        RequireContains(lifecycleHolderText, "ArgumentNullException.ThrowIfNull(localHostCompositionHolder);", "65V holder must fail closed on null passive holder.");
        RequireContains(lifecycleHolderText, "public LocalHostRuntimeSnapshot Snapshot =>", "65V holder must expose snapshot.");
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
        RequireNotContains(lifecycleHolderText, "Microsoft.Data.Sqlite", "65V holder shell must not touch SQLite.");
        RequireNotContains(lifecycleHolderText, "HttpClient", "65V holder shell must not add transport.");
        RequireNotContains(lifecycleHolderText, "Tcp", "65V holder shell must not add transport.");
        RequireNotContains(lifecycleHolderText, "Udp", "65V holder shell must not add transport.");
        RequireNotContains(lifecycleHolderText, "Socket", "65V holder shell must not add transport.");

        RequireContains(passiveHolderText, "Holds a validated Local Host composition result without starting or consuming runtime behavior.", "Passive holder purpose must remain.");
        RequireContains(passiveHolderText, "LocalHostBootstrapCompositionRules.IsCompositionValid(composition)", "Passive holder must still validate composition.");
        RequireContains(passiveHolderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Passive holder must remain read-only.");
        RequireNotContains(passiveHolderText, ".Start(", "Passive holder must not activate runtime.");
        RequireNotContains(passiveHolderText, ".Stop(", "Passive holder must not stop runtime.");

        RequireNotContains(appServicesText, "SalrynLocalHostRuntimeLifecycleHolder", "65V must not wire lifecycle holder into AppServices yet.");
        RequireNotContains(appServicesText, ".Start(", "65V must not activate runtime in AppServices.");
        RequireNotContains(appServicesText, ".Stop(", "65V must not stop runtime in AppServices.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder);", "Existing adapter wiring must remain.");

        RequireNotContains(adapterText, "SalrynLocalHostRuntimeLifecycleHolder", "Adapter must not own lifecycle holder.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not activate runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T dispatch bridge must remain.");

        RequireContains(runtimeText, "public LocalHostRuntimeSnapshot GetSnapshot()", "Runtime snapshot API must remain.");
        RequireContains(runtimeText, "public void Start(DateTimeOffset startedAt)", "Runtime activation API exists but must remain unwired by 65V.");
        RequireContains(runtimeText, "public void Stop()", "Runtime stop API exists but must remain unwired by 65V.");

        string[] forbiddenProgramShellMarkers =
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
        };

        foreach (string marker in forbiddenProgramShellMarkers)
        {
            RequireNotContains(programText, marker, $"Program must remain free of 65V marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65V marker: {marker}");
        }

        RequireContains(phase65UText, "Phase 65V may add a Local Host lifecycle holder or lifecycle service shell under App.WinForms Composition, but it must not start runtime yet.", "65U must authorize 65V shell shape.");
        RequireContains(phase65VText, "Status: Shell only.", "65V handoff must mark shell-only status.");
        RequireContains(phase65VText, "No runtime activation was added.", "65V handoff must block activation.");
        RequireContains(phase65VText, "No runtime stop was added.", "65V handoff must block stop.");
        RequireContains(phase65VText, "No AppServices wiring was added.", "65V handoff must keep wiring separate.");
        RequireContains(phase65VText, "No request context provider was added.", "65V handoff must keep context separate.");
        RequireContains(phase65VText, "SalrynLocalHostRuntimeLifecycleHolder", "65V handoff must name the new shell.");

        RequireContains(coreSmokeText, "Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks.Run();", "CoreSmoke must still run 65U.");
        RequireContains(coreSmokeText, "Phase65VLocalHostRuntimeLifecycleHolderShellChecks.Run();", "CoreSmoke must run 65V.");

        int index65U = coreSmokeText.IndexOf("Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks.Run();", StringComparison.Ordinal);
        int index65V = coreSmokeText.IndexOf("Phase65VLocalHostRuntimeLifecycleHolderShellChecks.Run();", StringComparison.Ordinal);

        if (index65U < 0 || index65V < 0 || index65V < index65U)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65V must run after Phase 65U.");
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
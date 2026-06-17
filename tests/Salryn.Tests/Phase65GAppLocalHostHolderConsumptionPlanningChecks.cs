namespace Salryn.Tests;

// Kirch: Plans Local Host holder consumption without adding the consumer yet.
public static class Phase65GAppLocalHostHolderConsumptionPlanningChecks
{
    // Kirch: Proves 65G is planning-only and that future consumption remains explicitly gated.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string holderText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynLocalHostCompositionHolder.cs");

        string appServicesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");

        string programText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Program.cs");

        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");

        string appProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Salryn.App.WinForms.csproj");

        string hostProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Salryn.Host.Local.csproj");

        string phase65FText = ReadRequiredFile(
            repoRoot,
            "tests",
            "Salryn.Tests",
            "Phase65FAppLocalHostCompositionHolderReadinessSealChecks.cs");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "PHASE_65G_APP_LOCAL_HOST_HOLDER_CONSUMPTION_PLANNING.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Core\\Salryn.Core.csproj\" />",
            "App.WinForms must keep Core reference.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Infrastructure\\Salryn.Infrastructure.csproj\" />",
            "App.WinForms must keep Infrastructure reference.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Host.Local\\Salryn.Host.Local.csproj\" />",
            "App.WinForms must keep controlled Host.Local reference.");

        RequireContains(
            hostProjectText,
            "..\\Salryn.Core\\Salryn.Core.csproj",
            "Host.Local must still reference Core.");

        RequireNotContains(
            hostProjectText,
            "Salryn.Infrastructure",
            "Host.Local must not reference Infrastructure.");

        RequireNotContains(
            hostProjectText,
            "Salryn.App.WinForms",
            "Host.Local must not reference App.WinForms.");

        RequireContains(
            holderText,
            "public sealed class SalrynLocalHostCompositionHolder",
            "Holder class must remain sealed.");

        RequireContains(
            holderText,
            "public SalrynLocalHostCompositionHolder(LocalHostBootstrapCompositionResult composition)",
            "Holder must still receive LocalHostBootstrapCompositionResult explicitly.");

        RequireContains(
            holderText,
            "ArgumentNullException.ThrowIfNull(composition);",
            "Holder must still reject null composition.");

        RequireContains(
            holderText,
            "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))",
            "Holder must still validate held composition.");

        RequireContains(
            holderText,
            "throw new InvalidOperationException(\"Invalid held app Local Host composition.\");",
            "Holder must still fail closed for invalid composition.");

        RequireContains(
            holderText,
            "public LocalHostBootstrapCompositionResult Composition { get; }",
            "Holder Composition must remain read-only.");

        RequireNotContains(
            holderText,
            "set;",
            "Holder must not expose mutable setters.");

        RequireNotContains(
            holderText,
            "init;",
            "Holder must not expose init setters.");

        RequireContains(
            appServicesText,
            "public SalrynLocalHostCompositionHolder CreateLocalHostProductLookupCompositionHolder(StoreDeploymentProfile profile)",
            "AppServices holder factory must remain available.");

        RequireContains(
            appServicesText,
            "CreateLocalHostProductLookupComposition(profile);",
            "Holder factory must still use guarded composition.");

        RequireContains(
            appServicesText,
            "return new SalrynLocalHostCompositionHolder(composition);",
            "Holder factory must still return the passive holder.");

        RequireExactlyOne(
            appServicesText,
            "CreateLocalHostProductLookupCompositionHolder(StoreDeploymentProfile profile)",
            "AppServices holder factory signature");

        RequireExactlyOne(
            appServicesText,
            "return new SalrynLocalHostCompositionHolder(composition);",
            "AppServices holder factory return");

        RequireContains(
            phase65FText,
            "Phase65FAppLocalHostCompositionHolderReadinessSealChecks",
            "Phase 65F readiness seal checks must remain present.");

        RequireContains(
            phase65FText,
            "Phase 65F must run after Phase 65E",
            "Phase 65F must still seal order after 65E.");

        RequireNotContains(
            programText,
            "SalrynLocalHostCompositionHolder",
            "Program must not receive the holder in planning phase 65G.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupCompositionHolder",
            "Program must not call holder factory in planning phase 65G.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupComposition",
            "Program must not create Local Host composition in planning phase 65G.");

        RequireNotContains(
            programText,
            "LocalHostBootstrapCompositionResult",
            "Program must not receive Local Host composition result in planning phase 65G.");

        RequireNotContains(
            programText,
            "ProductLookup",
            "Program must not add ProductLookup startup behavior in planning phase 65G.");

        RequireNotContains(
            shellText,
            "SalrynLocalHostCompositionHolder",
            "Shell must not receive the holder in planning phase 65G.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupCompositionHolder",
            "Shell must not call holder factory in planning phase 65G.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupComposition",
            "Shell must not create Local Host composition in planning phase 65G.");

        RequireNotContains(
            shellText,
            "LocalHostBootstrapCompositionResult",
            "Shell must not receive Local Host composition result in planning phase 65G.");

        RequireNotContains(
            shellText,
            "ProductLookup",
            "Shell must not add ProductLookup UI behavior in planning phase 65G.");

        RequireNoRuntimeStartOrDispatch(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartOrDispatch(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartOrDispatch(programText, "Program");
        RequireNoRuntimeStartOrDispatch(shellText, "Shell");

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);

        RequireContains(
            handoffText,
            "Planning only",
            "65G handoff must state planning-only scope.");

        RequireContains(
            handoffText,
            "No production source code was changed",
            "65G handoff must preserve no-production-source-change rule.");

        RequireContains(
            handoffText,
            "Future holder consumption must be introduced by a separate explicit wiring phase",
            "65G handoff must gate future holder consumption behind a separate phase.");

        RequireContains(
            handoffText,
            "Program must remain free of holder creation in 65G",
            "65G handoff must keep Program free of holder creation.");

        RequireContains(
            handoffText,
            "Shell must remain free of holder storage in 65G",
            "65G handoff must keep Shell free of holder storage.");

        RequireContains(
            handoffText,
            "No Local Host runtime was started",
            "65G handoff must preserve no runtime start.");

        RequireContains(
            handoffText,
            "No dispatch was performed",
            "65G handoff must preserve no dispatch.");

        RequireContains(
            handoffText,
            "No transport was implemented",
            "65G handoff must preserve no transport.");

        RequireContains(
            handoffText,
            "Phase 65H - App Local Host Holder Startup Wiring Slice",
            "65G handoff must point to the next explicit wiring slice.");

        RequireContains(
            coreSmokeText,
            "Phase65FAppLocalHostCompositionHolderReadinessSealChecks.Run();",
            "CoreSmoke must still run Phase 65F.");

        RequireContains(
            coreSmokeText,
            "Phase65GAppLocalHostHolderConsumptionPlanningChecks.Run();",
            "CoreSmoke must run Phase 65G.");

        int index65F = coreSmokeText.IndexOf(
            "Phase65FAppLocalHostCompositionHolderReadinessSealChecks.Run();",
            StringComparison.Ordinal);

        int index65G = coreSmokeText.IndexOf(
            "Phase65GAppLocalHostHolderConsumptionPlanningChecks.Run();",
            StringComparison.Ordinal);

        if (index65F < 0 || index65G < 0 || index65G < index65F)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65G must run after Phase 65F.");
        }
    }

    // Kirch: Finds the repository root for holder consumption planning checks.
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

    // Kirch: Reads a required repository file.
    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    // Kirch: Requires a source marker to exist.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Requires a forbidden source marker to be absent.
    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Requires a marker to appear once only.
    private static void RequireExactlyOne(string text, string value, string label)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        if (count != 1)
        {
            throw new InvalidOperationException($"{label} must appear exactly once. Actual count: {count}.");
        }
    }

    // Kirch: Keeps 65G planning-only by rejecting runtime activation, dispatch, and transport markers.
    private static void RequireNoRuntimeStartOrDispatch(string text, string owner)
    {
        string[] forbiddenPatterns =
        {
            ".Start(",
            "Runtime.Start",
            "DispatchAsync<",
            "TcpClient",
            "TcpListener",
            "HttpClient",
            "UdpClient",
            "System.Net.Sockets",
            "System.Net.Http",
            "LanDiscovery"
        };

        foreach (string forbiddenPattern in forbiddenPatterns)
        {
            if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{owner} must not start runtime, dispatch, or add transport behavior in 65G. Found {forbiddenPattern}");
            }
        }
    }

    // Kirch: Ensures Host.Local wiring remains inside App.WinForms Composition only.
    private static void AssertNoLocalHostOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(
            repoRoot.FullName,
            "src",
            "Salryn.App.WinForms");

        string compositionRoot = Path.Combine(
            appRoot,
            "Composition");

        string[] forbiddenPatterns =
        {
            "Salryn.Host.Local",
            "LocalHostBootstrapComposer",
            "LocalHostBootstrapCompositionResult",
            "LocalHostBootstrapCompositionRules",
            "SalrynLocalHostCompositionHolder",
            "CreateLocalHostProductLookupCompositionHolder",
            "CreateLocalHostProductLookupComposition",
            "CreateUncheckedLocalHostProductLookupComposition",
            "ComposeWithProductLookup"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(compositionRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string forbiddenPattern in forbiddenPatterns)
            {
                if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Local Host wiring leaked outside App.WinForms composition root: {file} contains {forbiddenPattern}");
                }
            }
        }
    }

    // Kirch: Ensures Infrastructure wiring remains inside App.WinForms Composition only.
    private static void AssertNoInfrastructureOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(
            repoRoot.FullName,
            "src",
            "Salryn.App.WinForms");

        string compositionRoot = Path.Combine(
            appRoot,
            "Composition");

        string[] forbiddenPatterns =
        {
            "Salryn.Infrastructure",
            "Microsoft.Data.Sqlite",
            "SqliteConnectionFactory",
            "SqliteRepositoryFactory",
            "DefaultDatabaseInitializationFactory"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(compositionRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string forbiddenPattern in forbiddenPatterns)
            {
                if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Infrastructure wiring leaked outside App.WinForms composition root: {file} contains {forbiddenPattern}");
                }
            }
        }
    }
}

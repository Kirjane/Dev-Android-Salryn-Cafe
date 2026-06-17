namespace Salryn.Tests;

// Kirch: Verifies passive startup wiring of the Local Host holder through AppServices only.
public static class Phase65HAppLocalHostHolderStartupWiringSliceChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65H_APP_LOCAL_HOST_HOLDER_STARTUP_WIRING_SLICE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(appServicesText, "StoreDeploymentProfile localHostProfile)", "AppServices constructor must receive the Local Host startup profile.");
        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "AppServices must assign the startup holder.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "Startup holder wiring must use the guarded holder factory.");
        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "AppServices must expose the passive startup holder.");
        RequireContains(appServicesText, "private static StoreDeploymentProfile CreateStandaloneLocalHostProfile()", "AppServices must create a standalone startup profile internally.");
        RequireContains(appServicesText, "SalrynMachineRole.StandaloneHost", "Startup profile must use a host-capable standalone role.");
        RequireContains(appServicesText, "SalrynDeploymentMode.Standalone", "Startup profile must use standalone mode.");
        RequireContains(appServicesText, "Endpoint: null", "Startup profile must not introduce network endpoint wiring.");
        RequireContains(appServicesText, "new TerminalIdentity(", "Startup profile must not introduce client terminal wiring.");
        RequireContains(appServicesText, "Environment.MachineName", "Startup profile must identify the local machine.");
        RequireContains(appServicesText, "CreateStandaloneLocalHostProfile());", "AppServices factory must pass the startup profile into the constructor.");

        RequireExactlyOne(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "AppServices startup holder property");
        RequireExactlyOne(appServicesText, "LocalHostProductLookupCompositionHolder =", "AppServices startup holder assignment");
        RequireExactlyOne(appServicesText, "private static StoreDeploymentProfile CreateStandaloneLocalHostProfile()", "AppServices standalone profile factory");
        RequireExactlyOne(appServicesText, "CreateStandaloneLocalHostProfile());", "AppServices factory startup profile argument");

        RequireNotContains(appServicesText, "Guid.NewGuid", "Startup profile must not generate random identity on every boot.");
        RequireNoRuntimeStartOrDispatch(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartOrDispatch(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartOrDispatch(programText, "Program");
        RequireNoRuntimeStartOrDispatch(shellText, "Shell");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Holder must remain sealed.");
        RequireContains(holderText, "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))", "Holder must still validate composition.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder composition must remain read-only.");

        RequireNotContains(programText, "SalrynLocalHostCompositionHolder", "Program must not receive the holder in Phase 65H.");
        RequireNotContains(programText, "CreateLocalHostProductLookupCompositionHolder", "Program must not create the holder in Phase 65H.");
        RequireNotContains(programText, "CreateLocalHostProductLookupComposition", "Program must not create Local Host composition in Phase 65H.");
        RequireNotContains(programText, "ProductLookup", "Program must not add ProductLookup behavior in Phase 65H.");

        RequireNotContains(shellText, "SalrynLocalHostCompositionHolder", "Shell must not receive or store the holder in Phase 65H.");
        RequireNotContains(shellText, "CreateLocalHostProductLookupCompositionHolder", "Shell must not create the holder in Phase 65H.");
        RequireNotContains(shellText, "CreateLocalHostProductLookupComposition", "Shell must not create Local Host composition in Phase 65H.");
        RequireNotContains(shellText, "ProductLookup", "Shell must not add ProductLookup UI behavior in Phase 65H.");

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(handoffText, "Startup wiring only", "65H handoff must state startup wiring only.");
        RequireContains(handoffText, "No Program startup behavior was changed", "65H handoff must preserve Program behavior.");
        RequireContains(handoffText, "No Shell routing behavior was changed", "65H handoff must preserve Shell behavior.");
        RequireContains(handoffText, "No Local Host runtime was started", "65H handoff must preserve no runtime start.");
        RequireContains(handoffText, "No dispatch was performed", "65H handoff must preserve no dispatch.");
        RequireContains(handoffText, "No transport was implemented", "65H handoff must preserve no transport.");

        RequireContains(coreSmokeText, "Phase65GAppLocalHostHolderConsumptionPlanningChecks.Run();", "CoreSmoke must still run 65G.");
        RequireContains(coreSmokeText, "Phase65HAppLocalHostHolderStartupWiringSliceChecks.Run();", "CoreSmoke must run 65H.");

        int index65G = coreSmokeText.IndexOf("Phase65GAppLocalHostHolderConsumptionPlanningChecks.Run();", StringComparison.Ordinal);
        int index65H = coreSmokeText.IndexOf("Phase65HAppLocalHostHolderStartupWiringSliceChecks.Run();", StringComparison.Ordinal);

        if (index65G < 0 || index65H < 0 || index65H < index65G)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65H must run after Phase 65G.");
        }

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);
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

    private static void RequireExactlyOne(string text, string value, string label)
    {
        int count = 0;
        int index = 0;

        while (true)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);

            if (index < 0)
            {
                break;
            }

            count++;
            index += value.Length;
        }

        if (count != 1)
        {
            throw new InvalidOperationException($"{label} must appear exactly once. Actual count: {count}");
        }
    }

    private static void RequireNoRuntimeStartOrDispatch(string text, string fileLabel)
    {
        string[] forbidden =
        {
            ".Start(",
            "Runtime.Start",
            "DispatchAsync<",
            "TcpClient",
            "TcpListener",
            "UdpClient",
            "HttpClient",
            "System.Net.Sockets",
            "System.Net.Http",
            "LanDiscovery"
        };

        foreach (string marker in forbidden)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{fileLabel} must not contain runtime/dispatch/transport marker: {marker}");
            }
        }
    }

    private static void AssertNoLocalHostOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(repoRoot.FullName, "src", "Salryn.App.WinForms");

        string[] forbidden =
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
            string normalized = file.Replace('\\', '/');

            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/Composition/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string marker in forbidden)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Host.Local marker leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }

    private static void AssertNoInfrastructureOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(repoRoot.FullName, "src", "Salryn.App.WinForms");

        string[] forbidden =
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
            string normalized = file.Replace('\\', '/');

            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/Composition/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string marker in forbidden)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Infrastructure marker leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}

namespace Salryn.Tests;

// Kirch: Plans the future holder consumption boundary without implementing consumption behavior.
public static class Phase65JAppLocalHostHolderConsumptionBoundaryPlanningChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65HText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65H_APP_LOCAL_HOST_HOLDER_STARTUP_WIRING_SLICE.md");
        string phase65IText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65I_APP_LOCAL_HOST_HOLDER_STARTUP_WIRING_READINESS_SEAL.md");
        string phase65JText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65J_APP_LOCAL_HOST_HOLDER_CONSUMPTION_BOUNDARY_PLANNING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "65H holder property must remain available at the composition root.");
        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "65H holder assignment must remain.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "65H holder creation must remain guarded.");
        RequireContains(appServicesText, "CreateStandaloneLocalHostProfile());", "65H standalone startup profile injection must remain.");
        RequireContains(appServicesText, "SalrynMachineRole.StandaloneHost", "Startup profile must remain standalone host.");
        RequireContains(appServicesText, "SalrynDeploymentMode.Standalone", "Startup profile must remain standalone mode.");
        RequireContains(appServicesText, "Endpoint: null", "Startup profile must not introduce endpoint wiring.");
        RequireContains(appServicesText, "new TerminalIdentity(", "Startup profile must not introduce terminal wiring.");

        RequireExactlyOne(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "65H holder property");
        RequireExactlyOne(appServicesText, "LocalHostProductLookupCompositionHolder =", "65H holder assignment");
        RequireExactlyOne(appServicesText, "CreateStandaloneLocalHostProfile());", "65H standalone profile call");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Holder must remain sealed.");
        RequireContains(holderText, "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))", "Holder must remain validating.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must remain read-only.");

        RequireNoRuntimeStartDispatchOrTransport(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartDispatchOrTransport(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartDispatchOrTransport(programText, "Program");
        RequireNoRuntimeStartDispatchOrTransport(shellText, "Shell");

        string[] programShellForbidden =
        {
            "SalrynLocalHostCompositionHolder",
            "CreateLocalHostProductLookupCompositionHolder",
            "CreateLocalHostProductLookupComposition",
            "LocalHostBootstrapCompositionResult",
            "LocalHostBootstrapComposer",
            "ComposeWithProductLookup",
            "ProductLookup"
        };

        foreach (string marker in programShellForbidden)
        {
            RequireNotContains(programText, marker, $"Program must remain free of holder consumption marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of holder consumption marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65HText, "Status: Startup wiring only.", "65H must remain a startup wiring slice.");
        RequireContains(phase65IText, "Status: Readiness seal.", "65I must remain a readiness seal.");

        RequireContains(phase65JText, "Status: Boundary planning only.", "65J handoff must state boundary planning only.");
        RequireContains(phase65JText, "No production source behavior was changed", "65J must not change production source behavior.");
        RequireContains(phase65JText, "No Program startup behavior was changed", "65J must preserve Program boundary.");
        RequireContains(phase65JText, "No Shell routing behavior was changed", "65J must preserve Shell boundary.");
        RequireContains(phase65JText, "No Local Host runtime was started", "65J must preserve no runtime start.");
        RequireContains(phase65JText, "No dispatch was performed", "65J must preserve no dispatch.");
        RequireContains(phase65JText, "No transport was implemented", "65J must preserve no transport.");
        RequireContains(phase65JText, "Approved future consumption boundary", "65J must define approved future consumption boundary.");
        RequireContains(phase65JText, "Rejected consumption boundaries", "65J must define rejected boundaries.");
        RequireContains(phase65JText, "A future implementation phase must be explicit", "65J must require explicit future implementation approval.");
        RequireContains(phase65JText, "Phase 65K", "65J must point to a separate future phase.");

        RequireContains(coreSmokeText, "Phase65IAppLocalHostHolderStartupWiringReadinessSealChecks.Run();", "CoreSmoke must still run 65I.");
        RequireContains(coreSmokeText, "Phase65JAppLocalHostHolderConsumptionBoundaryPlanningChecks.Run();", "CoreSmoke must run 65J.");

        int index65I = coreSmokeText.IndexOf("Phase65IAppLocalHostHolderStartupWiringReadinessSealChecks.Run();", StringComparison.Ordinal);
        int index65J = coreSmokeText.IndexOf("Phase65JAppLocalHostHolderConsumptionBoundaryPlanningChecks.Run();", StringComparison.Ordinal);

        if (index65I < 0 || index65J < 0 || index65J < index65I)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65J must run after Phase 65I.");
        }

        AssertNoAppHostLocalLeakOutsideComposition(repoRoot);
        AssertNoAppInfrastructureLeakOutsideComposition(repoRoot);
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

    private static void RequireNoRuntimeStartDispatchOrTransport(string text, string label)
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
                throw new InvalidOperationException($"{label} must not contain runtime/dispatch/transport marker: {marker}");
            }
        }
    }

    private static void AssertNoAppHostLocalLeakOutsideComposition(DirectoryInfo repoRoot)
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

    private static void AssertNoAppInfrastructureLeakOutsideComposition(DirectoryInfo repoRoot)
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

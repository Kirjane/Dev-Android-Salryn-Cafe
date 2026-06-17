namespace Salryn.Tests;

// Kirch: Plans the adapter contract boundary without implementing the adapter or consuming the holder.
public static class Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65JText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65J_APP_LOCAL_HOST_HOLDER_CONSUMPTION_BOUNDARY_PLANNING.md");
        string phase65KText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65K_APP_LOCAL_HOST_HOLDER_CONSUMPTION_ADAPTER_CONTRACT_PLANNING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "65H holder property must remain available at the composition root.");
        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "65H holder assignment must remain.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "65H holder creation must remain guarded.");
        RequireContains(appServicesText, "CreateStandaloneLocalHostProfile());", "65H standalone startup profile injection must remain.");
        RequireContains(appServicesText, "Endpoint: null", "Startup profile must still have no endpoint wiring.");
        RequireContains(appServicesText, "new TerminalIdentity(", "Startup profile must still have no terminal wiring.");

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

        string[] forbiddenProgramShellMarkers =
        {
            "SalrynLocalHostCompositionHolder",
            "LocalHostBootstrapCompositionResult",
            "LocalHostBootstrapComposer",
            "ComposeWithProductLookup",
            "IAppLocalHostProductLookupGateway",
            "AppLocalHostProductLookupGateway",
            "ProductLookup"
        };

        foreach (string marker in forbiddenProgramShellMarkers)
        {
            RequireNotContains(programText, marker, $"Program must remain free of adapter/holder marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of adapter/holder marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65JText, "Status: Boundary planning only.", "65J must remain boundary planning only.");
        RequireContains(phase65JText, "The only approved conceptual direction is future consumption through an explicit App.WinForms Composition adapter/gateway.", "65J approved conceptual direction must remain.");

        RequireContains(phase65KText, "Status: Adapter contract planning only.", "65K handoff must state adapter contract planning only.");
        RequireContains(phase65KText, "No production source behavior was changed", "65K must not change production source behavior.");
        RequireContains(phase65KText, "No adapter interface was added to production source", "65K must not add a production adapter interface.");
        RequireContains(phase65KText, "No adapter implementation was added", "65K must not add adapter implementation.");
        RequireContains(phase65KText, "No Program startup behavior was changed", "65K must preserve Program boundary.");
        RequireContains(phase65KText, "No Shell routing behavior was changed", "65K must preserve Shell boundary.");
        RequireContains(phase65KText, "No Local Host runtime was started", "65K must preserve no runtime start.");
        RequireContains(phase65KText, "No dispatch was performed", "65K must preserve no dispatch.");
        RequireContains(phase65KText, "No transport was implemented", "65K must preserve no transport.");
        RequireContains(phase65KText, "Planned adapter contract shape", "65K must define planned contract shape.");
        RequireContains(phase65KText, "IAppLocalHostProductLookupGateway", "65K must name the planned future gateway contract.");
        RequireContains(phase65KText, "TryLookupByBarcodeAsync", "65K must name the planned future lookup method.");
        RequireContains(phase65KText, "read-only", "65K must preserve read-only contract intent.");
        RequireContains(phase65KText, "stopped-runtime by default", "65K must preserve stopped-runtime default.");
        RequireContains(phase65KText, "Phase 65L", "65K must point to a separate future phase.");

        RequireContains(coreSmokeText, "Phase65JAppLocalHostHolderConsumptionBoundaryPlanningChecks.Run();", "CoreSmoke must still run 65J.");
        RequireContains(coreSmokeText, "Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks.Run();", "CoreSmoke must run 65K.");

        int index65J = coreSmokeText.IndexOf("Phase65JAppLocalHostHolderConsumptionBoundaryPlanningChecks.Run();", StringComparison.Ordinal);
        int index65K = coreSmokeText.IndexOf("Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks.Run();", StringComparison.Ordinal);

        if (index65J < 0 || index65K < 0 || index65K < index65J)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65K must run after Phase 65J.");
        }

        AssertProductionAdapterImplementationStillAbsent(repoRoot);
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

    private static void AssertProductionAdapterImplementationStillAbsent(DirectoryInfo repoRoot)
    {
        string srcRoot = Path.Combine(repoRoot.FullName, "src");

        string[] forbidden =
        {
            "class AppLocalHostProductLookupGateway",
            "sealed class AppLocalHostProductLookupGateway"
        };

        foreach (string file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');

            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string marker in forbidden)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Production adapter implementation marker was found in {file}: {marker}");
                }
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
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string normalized = file.Replace('\\', '/');

            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/Composition/", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Features/Checkout/CheckoutLocalHostProductSearchBoundary.cs", StringComparison.OrdinalIgnoreCase))
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
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string normalized = file.Replace('\\', '/');

            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/Composition/", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Features/Checkout/CheckoutLocalHostProductSearchBoundary.cs", StringComparison.OrdinalIgnoreCase))
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

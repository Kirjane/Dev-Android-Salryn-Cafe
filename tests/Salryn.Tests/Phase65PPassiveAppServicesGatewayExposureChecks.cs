namespace Salryn.Tests;

// Kirch: Verifies passive AppServices gateway exposure without holder consumption, dispatch, runtime start, transport, Program, Shell, or UI behavior changes.
public static class Phase65PPassiveAppServicesGatewayExposureChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65OText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65O_APP_SERVICES_GATEWAY_REGISTRATION_BOUNDARY_PLANNING.md");
        string phase65PText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65P_PASSIVE_APP_SERVICES_GATEWAY_EXPOSURE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "Gateway contract must remain.");
        RequireContains(contractText, "Task<AppLocalHostProductLookupResult>", "Gateway method must remain result-based.");
        RequireContains(contractText, "AppLocalHostProductLookupRequest", "Gateway request must remain.");

        RequireContains(adapterText, "public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway", "Inert adapter shell must remain.");
        RequireContains(adapterText, "LocalHostUnavailable", "Adapter must remain unavailable until holder-aware wiring is approved.");
        RequireNotContains(adapterText, "SalrynLocalHostCompositionHolder", "65P adapter must not consume holder.");
        RequireNotContains(adapterText, "LocalHostBootstrapCompositionResult", "65P adapter must not expose local host internals.");
        RequireNotContains(adapterText, "DispatchAsync", "65P adapter must not dispatch.");
        RequireNotContains(adapterText, ".Start(", "65P adapter must not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65P adapter must not start runtime.");
        RequireNotContains(adapterText, "TcpClient", "65P adapter must not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65P adapter must not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65P adapter must not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65P adapter must not add transport.");
        RequireNotContains(adapterText, "System.Net.Sockets", "65P adapter must not add sockets.");
        RequireNotContains(adapterText, "System.Net.Http", "65P adapter must not add HTTP.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "Passive holder must remain exposed.");
        RequireContains(appServicesText, "// Kirch: Read-only passive gateway exposure for future ProductLookup wiring; not connected to UI behavior yet.", "65P must keep Kirch passive property comment.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "65P must expose passive gateway property.");
        RequireContains(appServicesText, "// Kirch: Passive ProductLookup gateway boundary; kept separate from holder-aware wiring.", "65P must keep Kirch constructor boundary comment.");
        RequireContains(appServicesText, "IAppLocalHostProductLookupGateway localHostProductLookupGateway", "65P must inject passive gateway through constructor boundary.");
        RequireContains(appServicesText, "// Kirch: Stores the inert gateway only; this does not consume the holder, dispatch, start runtime, or add transport.", "65P must keep Kirch assignment comment.");
        RequireContains(appServicesText, "LocalHostProductLookupGateway = localHostProductLookupGateway;", "65P must assign passive gateway property.");
        RequireContains(appServicesText, "// Kirch: Creates only the inert adapter shell; holder-aware lookup remains a later approved phase.", "65P must keep Kirch adapter instantiation comment.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter()", "65P must instantiate inert adapter in AppServices composition.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "Holder creation must remain guarded.");

        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder", "65P must not pass holder into adapter.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(CreateLocalHostProductLookupCompositionHolder", "65P must not pass created holder into adapter.");
        RequireNotContains(appServicesText, "LocalHostProductLookupGateway.TryLookupByBarcodeAsync", "65P must not consume gateway.");
        RequireNotContains(appServicesText, "DispatchAsync", "65P AppServices must not dispatch.");
        RequireNotContains(appServicesText, ".Start(", "65P AppServices must not start runtime.");
        RequireNotContains(appServicesText, "Runtime.Start", "65P AppServices must not start runtime.");

        string[] programShellForbidden =
        {
            "CompositionLocalHostProductLookupGatewayAdapter",
            "IAppLocalHostProductLookupGateway",
            "AppLocalHostProductLookupRequest",
            "AppLocalHostProductLookupProduct",
            "AppLocalHostProductLookupResult",
            "TryLookupByBarcodeAsync",
            "SalrynLocalHostCompositionHolder",
            "LocalHostBootstrapCompositionResult",
            "LocalHostBootstrapComposer",
            "ComposeWithProductLookup",
            "ProductLookup"
        };

        foreach (string marker in programShellForbidden)
        {
            RequireNotContains(programText, marker, $"Program must remain free of 65P marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65P marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65OText, "Status: AppServices gateway registration boundary planning only.", "65O handoff must remain sealed.");
        RequireContains(phase65PText, "Status: Passive AppServices gateway exposure only.", "65P handoff must state passive exposure only.");
        RequireContains(phase65PText, "Production source change: passive gateway exposure only.", "65P must acknowledge production source exposure.");
        RequireContains(phase65PText, "No holder was consumed", "65P must not consume holder.");
        RequireContains(phase65PText, "No Program startup behavior was changed", "65P must preserve Program.");
        RequireContains(phase65PText, "No Shell routing behavior was changed", "65P must preserve Shell.");
        RequireContains(phase65PText, "No checkout UI behavior was changed", "65P must preserve checkout.");
        RequireContains(phase65PText, "No product management UI behavior was changed", "65P must preserve product management.");
        RequireContains(phase65PText, "No Local Host runtime was started", "65P must preserve no runtime start.");
        RequireContains(phase65PText, "No dispatch was performed", "65P must preserve no dispatch.");
        RequireContains(phase65PText, "No transport was implemented", "65P must preserve no transport.");
        RequireContains(phase65PText, "Phase 65Q", "65P must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65OAppServicesGatewayRegistrationBoundaryPlanningChecks.Run();", "CoreSmoke must still run 65O.");
        RequireContains(coreSmokeText, "Phase65PPassiveAppServicesGatewayExposureChecks.Run();", "CoreSmoke must run 65P.");

        int index65O = coreSmokeText.IndexOf("Phase65OAppServicesGatewayRegistrationBoundaryPlanningChecks.Run();", StringComparison.Ordinal);
        int index65P = coreSmokeText.IndexOf("Phase65PPassiveAppServicesGatewayExposureChecks.Run();", StringComparison.Ordinal);

        if (index65O < 0 || index65P < 0 || index65P < index65O)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65P must run after Phase 65O.");
        }

        AssertNoRuntimeDispatchOrTransportLeak(repoRoot);
        AssertNoFeatureLayerGatewayConsumption(repoRoot);
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

    private static void AssertNoRuntimeDispatchOrTransportLeak(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(repoRoot.FullName, "src", "Salryn.App.WinForms");

        string[] forbidden =
        {
            "DispatchAsync",
            ".Start(",
            "Runtime.Start",
            "TcpClient",
            "TcpListener",
            "UdpClient",
            "HttpClient",
            "System.Net.Sockets",
            "System.Net.Http",
            "LanDiscovery"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
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
                    throw new InvalidOperationException($"65P detected runtime/dispatch/transport leak in {file}: {marker}");
                }
            }
        }
    }

    private static void AssertNoFeatureLayerGatewayConsumption(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(repoRoot.FullName, "src", "Salryn.App.WinForms");

        string[] forbidden =
        {
            "IAppLocalHostProductLookupGateway",
            "CompositionLocalHostProductLookupGatewayAdapter",
            "AppLocalHostProductLookupRequest",
            "AppLocalHostProductLookupProduct",
            "AppLocalHostProductLookupResult",
            "TryLookupByBarcodeAsync"
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
                    throw new InvalidOperationException($"65P gateway leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}

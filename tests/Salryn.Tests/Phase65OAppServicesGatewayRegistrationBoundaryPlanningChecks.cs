namespace Salryn.Tests;

// Kirch: Plans the AppServices gateway registration boundary without changing production app source.
public static class Phase65OAppServicesGatewayRegistrationBoundaryPlanningChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65NText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65N_APP_LOCAL_HOST_PRODUCT_LOOKUP_GATEWAY_ADAPTER_SHELL_BEHAVIOR_SEAL.md");
        string phase65OText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65O_APP_SERVICES_GATEWAY_REGISTRATION_BOUNDARY_PLANNING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "Gateway contract must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupRequest", "Gateway request contract must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupResult", "Gateway result contract must remain.");

        RequireContains(adapterText, "public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway", "Adapter shell must remain.");
        RequireContains(adapterText, "LocalHostUnavailable", "Adapter shell must remain inert until wiring is approved.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "Passive holder must remain available.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "Holder creation must remain guarded.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter()", "65P may instantiate the inert adapter through AppServices.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "65P may expose the passive gateway through AppServices.");

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
            RequireNotContains(programText, marker, $"Program must remain free of 65O marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65O marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65NText, "Status: Adapter shell behavior seal only.", "65N handoff must remain sealed.");
        RequireContains(phase65OText, "Status: AppServices gateway registration boundary planning only.", "65O handoff must state planning only.");
        RequireContains(phase65OText, "No production app source was changed", "65O must not change production app source.");
        RequireContains(phase65OText, "Future registration boundary: App.WinForms Composition only", "65O must name the future registration boundary.");
        RequireContains(phase65OText, "No adapter was registered in SalrynAppServices", "65O must not register adapter.");
        RequireContains(phase65OText, "No holder was consumed", "65O must not consume holder.");
        RequireContains(phase65OText, "No Program startup behavior was changed", "65O must preserve Program.");
        RequireContains(phase65OText, "No Shell routing behavior was changed", "65O must preserve Shell.");
        RequireContains(phase65OText, "No Local Host runtime was started", "65O must preserve no runtime start.");
        RequireContains(phase65OText, "No dispatch was performed", "65O must preserve no dispatch.");
        RequireContains(phase65OText, "No transport was implemented", "65O must preserve no transport.");
        RequireContains(phase65OText, "Phase 65P", "65O must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65NAppLocalHostProductLookupGatewayAdapterShellBehaviorSealChecks.Run();", "CoreSmoke must still run 65N.");
        RequireContains(coreSmokeText, "Phase65OAppServicesGatewayRegistrationBoundaryPlanningChecks.Run();", "CoreSmoke must run 65O.");

        int index65N = coreSmokeText.IndexOf("Phase65NAppLocalHostProductLookupGatewayAdapterShellBehaviorSealChecks.Run();", StringComparison.Ordinal);
        int index65O = coreSmokeText.IndexOf("Phase65OAppServicesGatewayRegistrationBoundaryPlanningChecks.Run();", StringComparison.Ordinal);

        if (index65N < 0 || index65O < 0 || index65O < index65N)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65O must run after Phase 65N.");
        }

        AssertNoRuntimeDispatchOrTransportLeak(repoRoot);
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
                    throw new InvalidOperationException($"65O detected runtime/dispatch/transport leak in {file}: {marker}");
                }
            }
        }
    }
}

namespace Salryn.Tests;

// Kirch: Planning seal only. This guards the future holder-aware gateway wiring boundary without allowing holder consumption yet.
public static class Phase65QHolderAwareGatewayWiringPlanningChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65PText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65P_PASSIVE_APP_SERVICES_GATEWAY_EXPOSURE.md");
        string phase65QText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65Q_HOLDER_AWARE_GATEWAY_WIRING_PLANNING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "Gateway contract must remain.");
        RequireContains(contractText, "TryLookupByBarcodeAsync", "Gateway method must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupRequest", "Gateway request must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupResult", "Gateway result must remain.");

        RequireContains(adapterText, "public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway", "Adapter shell must remain.");
        RequireContains(adapterText, "LocalHostUnavailable", "Adapter must remain inert and unavailable.");
        RequireContains(adapterText, "SalrynLocalHostCompositionHolder localHostCompositionHolder", "65R may add holder-aware constructor boundary shell without dispatch.");
        RequireNotContains(adapterText, "LocalHostBootstrapCompositionResult", "65Q planning must not expose local host internals in adapter yet.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T may dispatch through holder runtime bridge while preserving no runtime start.");
        RequireNotContains(adapterText, ".Start(", "65Q planning must not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65Q planning must not start runtime.");
        RequireNotContains(adapterText, "TcpClient", "65Q planning must not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65Q planning must not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65Q planning must not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65Q planning must not add transport.");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Passive holder must remain.");
        RequireContains(holderText, "LocalHostBootstrapCompositionResult", "Holder may retain passive bootstrap result.");
        RequireNotContains(holderText, "DispatchAsync", "Holder must remain passive and must not dispatch.");
        RequireNotContains(holderText, ".Start(", "Holder must remain passive and must not start runtime.");
        RequireNotContains(holderText, "Runtime.Start", "Holder must remain passive and must not start runtime.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "AppServices must still expose passive holder.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "AppServices must still expose passive gateway.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "AppServices must still create passive holder.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "65S may wire passive holder into adapter shell.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter()", "65S AppServices must not use parameterless adapter wiring.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(CreateLocalHostProductLookupCompositionHolder", "65S must not inline holder creation into adapter wiring.");
        RequireNotContains(appServicesText, "LocalHostProductLookupGateway.TryLookupByBarcodeAsync", "65Q planning must not consume gateway.");
        RequireNotContains(appServicesText, "DispatchAsync", "65Q planning must not dispatch.");
        RequireNotContains(appServicesText, ".Start(", "65Q planning must not start runtime.");
        RequireNotContains(appServicesText, "Runtime.Start", "65Q planning must not start runtime.");

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
            RequireNotContains(programText, marker, $"Program must remain free of 65Q marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65Q marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65PText, "Status: Passive AppServices gateway exposure only.", "65P handoff must remain sealed.");
        RequireContains(phase65QText, "Status: Holder-aware gateway wiring planning only.", "65Q handoff must state planning only.");
        RequireContains(phase65QText, "No production source behavior was changed", "65Q must not change production behavior.");
        RequireContains(phase65QText, "No holder was consumed", "65Q must not consume holder.");
        RequireContains(phase65QText, "No holder-aware adapter was implemented", "65Q must not implement holder-aware adapter.");
        RequireContains(phase65QText, "No runtime was started", "65Q must not start runtime.");
        RequireContains(phase65QText, "No dispatch was performed", "65Q must not dispatch.");
        RequireContains(phase65QText, "No transport was implemented", "65Q must not add transport.");
        RequireContains(phase65QText, "Phase 65R", "65Q must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65PPassiveAppServicesGatewayExposureChecks.Run();", "CoreSmoke must still run 65P.");
        RequireContains(coreSmokeText, "Phase65QHolderAwareGatewayWiringPlanningChecks.Run();", "CoreSmoke must run 65Q.");

        int index65P = coreSmokeText.IndexOf("Phase65PPassiveAppServicesGatewayExposureChecks.Run();", StringComparison.Ordinal);
        int index65Q = coreSmokeText.IndexOf("Phase65QHolderAwareGatewayWiringPlanningChecks.Run();", StringComparison.Ordinal);

        if (index65P < 0 || index65Q < 0 || index65Q < index65P)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65Q must run after Phase 65P.");
        }

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
                    throw new InvalidOperationException($"65Q gateway leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}

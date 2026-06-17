namespace Salryn.Tests;

// Kirch: Verifies adapter-to-holder ProductLookup dispatch bridge without runtime start, transport, Program, Shell, or UI behavior.
public static class Phase65TAppLocalHostProductLookupDispatchBridgeChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65SText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65S_APPSERVICES_HOLDER_AWARE_ADAPTER_WIRING_SHELL.md");
        string phase65TText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65T_APP_LOCAL_HOST_PRODUCT_LOOKUP_DISPATCH_BRIDGE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "Gateway contract must remain.");
        RequireContains(contractText, "TryLookupByBarcodeAsync", "Gateway lookup method must remain.");

        RequireContains(adapterText, "using Salryn.Core.Contracts;", "65T adapter must use Core Host envelope contracts.");
        RequireContains(adapterText, "using Salryn.Host.Local.Contracts;", "65T adapter must use Host.Local ProductLookup request/response contracts.");
        RequireContains(adapterText, "SalrynLocalHostCompositionHolder? holder = localHostCompositionHolder;", "65T must consume only the stored holder reference.");
        RequireContains(adapterText, "HostRequestEnvelope<LocalHostProductLookupRequest>", "65T must build a Host request envelope.");
        RequireContains(adapterText, "HostOperationKind.Query", "65T ProductLookup must remain a query.");
        RequireContains(adapterText, "HostOperationNames.ProductLookup", "65T must dispatch only ProductLookup.");
        RequireContains(adapterText, "new LocalHostProductLookupRequest(barcode)", "65T must pass normalized barcode into Host.Local request.");
        RequireNotContains(adapterText, "CreateStoppedRuntimeBridgeContext()", "Adapter placeholder context must be removed after provider consumption.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T must dispatch through held in-process runtime.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.FoundProduct(", "65T must map success.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.NotFound(barcode)", "65T must map not found.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.LocalHostUnavailable(", "65T must map unavailable.");

        RequireNotContains(adapterText, ".Start(", "65T adapter must not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65T adapter must not start runtime.");
        RequireNotContains(adapterText, "TcpClient", "65T adapter must not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65T adapter must not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65T adapter must not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65T adapter must not add transport.");
        RequireNotContains(adapterText, "System.Net.Sockets", "65T adapter must not add sockets.");
        RequireNotContains(adapterText, "System.Net.Http", "65T adapter must not add HTTP.");
        RequireNotContains(adapterText, "Microsoft.Data.Sqlite", "65T adapter must not reference SQLite.");
        RequireNotContains(adapterText, "Sqlite", "65T adapter must not mention SQLite.");

        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must remain read-only.");
        RequireNotContains(holderText, "DispatchAsync", "Holder must not dispatch.");
        RequireNotContains(holderText, ".Start(", "Holder must not start runtime.");

        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "AppServices must still wire holder-aware adapter.");
        RequireContains(appServicesText, "// Kirch: Wires the holder-aware adapter through AppServices only; runtime start, Program, Shell, UI, and transport remain blocked.", "AppServices must keep 65T boundary comment.");
        RequireNotContains(appServicesText, "DispatchAsync", "AppServices must not dispatch.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not start runtime.");
        RequireNotContains(appServicesText, "Runtime.Start", "AppServices must not start runtime.");

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
            RequireNotContains(programText, marker, $"Program must remain free of 65T marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65T marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65SText, "Phase 65T may introduce adapter-to-holder ProductLookup dispatch behavior", "65S must point to 65T.");
        RequireContains(phase65TText, "Status: App Local Host ProductLookup dispatch bridge only.", "65T handoff must state dispatch bridge only.");
        RequireContains(phase65TText, "No Local Host runtime was started", "65T must preserve no runtime start.");
        RequireContains(phase65TText, "No transport was implemented", "65T must preserve no transport.");
        RequireContains(phase65TText, "No Program startup behavior was changed", "65T must preserve Program.");
        RequireContains(phase65TText, "No Shell routing behavior was changed", "65T must preserve Shell.");

        RequireContains(coreSmokeText, "Phase65SAppServicesHolderAwareAdapterWiringShellChecks.Run();", "CoreSmoke must still run 65S.");
        RequireContains(coreSmokeText, "Phase65TAppLocalHostProductLookupDispatchBridgeChecks.Run();", "CoreSmoke must run 65T.");

        int index65S = coreSmokeText.IndexOf("Phase65SAppServicesHolderAwareAdapterWiringShellChecks.Run();", StringComparison.Ordinal);
        int index65T = coreSmokeText.IndexOf("Phase65TAppLocalHostProductLookupDispatchBridgeChecks.Run();", StringComparison.Ordinal);

        if (index65S < 0 || index65T < 0 || index65T < index65S)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65T must run after Phase 65S.");
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
                    throw new InvalidOperationException($"65T gateway leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}
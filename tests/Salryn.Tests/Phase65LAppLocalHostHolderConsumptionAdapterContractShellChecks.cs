namespace Salryn.Tests;

// Kirch: Verifies the production adapter contract shell exists without implementation or behavior wiring.
public static class Phase65LAppLocalHostHolderConsumptionAdapterContractShellChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string phase65KCheckText = ReadRequiredFile(repoRoot, "tests", "Salryn.Tests", "Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65KText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65K_APP_LOCAL_HOST_HOLDER_CONSUMPTION_ADAPTER_CONTRACT_PLANNING.md");
        string phase65LText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65L_APP_LOCAL_HOST_HOLDER_CONSUMPTION_ADAPTER_CONTRACT_SHELL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "namespace Salryn.App.WinForms.Composition;", "65L contract must live in App.WinForms Composition.");
        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "65L must add the planned gateway contract.");
        RequireContains(contractText, "TryLookupByBarcodeAsync", "65L must add the planned lookup method.");
        RequireContains(contractText, "Task<AppLocalHostProductLookupResult>", "65L method must return the planned result type.");
        RequireContains(contractText, "CancellationToken cancellationToken", "65L method must support cancellation.");
        RequireContains(contractText, "public sealed record AppLocalHostProductLookupRequest", "65L must add the request shell.");
        RequireContains(contractText, "public sealed record AppLocalHostProductLookupProduct", "65L must add the product payload shell.");
        RequireContains(contractText, "public sealed record AppLocalHostProductLookupResult", "65L must add the result shell.");
        RequireContains(contractText, "FoundProduct", "65L result must define found factory.");
        RequireContains(contractText, "NotFound", "65L result must define not-found factory.");
        RequireContains(contractText, "InvalidBarcode", "65L result must define invalid-barcode failure.");
        RequireContains(contractText, "LocalHostUnavailable", "65L result must define local-host unavailable failure.");

        RequireNotContains(contractText, "class AppLocalHostProductLookupGateway", "65L must not add adapter implementation.");
        RequireNotContains(contractText, "Salryn.Host.Local", "65L contract must not reference Host.Local.");
        RequireNotContains(contractText, "LocalHostBootstrapCompositionResult", "65L contract must not expose holder composition internals.");
        RequireNotContains(contractText, "Salryn.Infrastructure", "65L contract must not reference Infrastructure.");
        RequireNotContains(contractText, "Microsoft.Data.Sqlite", "65L contract must not reference SQLite.");
        RequireNotContains(contractText, "Sqlite", "65L contract must not mention SQLite.");
        RequireNotContains(contractText, "DispatchAsync", "65L contract must not dispatch.");
        RequireNotContains(contractText, ".Start(", "65L contract must not start runtime.");

        RequireContains(phase65KCheckText, "AssertProductionAdapterImplementationStillAbsent(repoRoot);", "65K check must be forward-compatible after 65L.");
        RequireNotContains(phase65KCheckText, "AssertNoProductionAdapterContractExists(repoRoot);", "65K must no longer block the 65L contract shell.");
        RequireNotContains(phase65KCheckText, "\"interface IAppLocalHostProductLookupGateway\"", "65K must no longer forbid the contract interface after 65L.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "Holder property must remain available.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "Holder creation must remain guarded.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "65P may expose the passive gateway through AppServices.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "65S may wire the passive holder-aware adapter through AppServices without dispatch.");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Holder must remain sealed.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must remain read-only.");

        string[] programShellForbidden =
        {
            "IAppLocalHostProductLookupGateway",
            "AppLocalHostProductLookupGateway",
            "AppLocalHostProductLookupRequest",
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
            RequireNotContains(programText, marker, $"Program must remain free of 65L contract/holder marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65L contract/holder marker: {marker}");
        }

        RequireNoRuntimeStartDispatchOrTransport(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartDispatchOrTransport(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartDispatchOrTransport(programText, "Program");
        RequireNoRuntimeStartDispatchOrTransport(shellText, "Shell");

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65KText, "Status: Adapter contract planning only.", "65K must remain adapter contract planning.");
        RequireContains(phase65LText, "Status: Production adapter contract shell only.", "65L handoff must state contract shell only.");
        RequireContains(phase65LText, "Production source change: contract shell only.", "65L must acknowledge production source contract addition.");
        RequireContains(phase65LText, "No adapter implementation was added", "65L must not add implementation.");
        RequireContains(phase65LText, "No adapter was registered in SalrynAppServices", "65L must not register the adapter.");
        RequireContains(phase65LText, "No holder was consumed", "65L must not consume holder.");
        RequireContains(phase65LText, "No Program startup behavior was changed", "65L must preserve Program.");
        RequireContains(phase65LText, "No Shell routing behavior was changed", "65L must preserve Shell.");
        RequireContains(phase65LText, "No Local Host runtime was started", "65L must preserve no runtime start.");
        RequireContains(phase65LText, "No dispatch was performed", "65L must preserve no dispatch.");
        RequireContains(phase65LText, "No transport was implemented", "65L must preserve no transport.");
        RequireContains(phase65LText, "Phase 65M", "65L must point to a separate future phase.");

        RequireContains(coreSmokeText, "Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks.Run();", "CoreSmoke must still run 65K.");
        RequireContains(coreSmokeText, "Phase65LAppLocalHostHolderConsumptionAdapterContractShellChecks.Run();", "CoreSmoke must run 65L.");

        int index65K = coreSmokeText.IndexOf("Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks.Run();", StringComparison.Ordinal);
        int index65L = coreSmokeText.IndexOf("Phase65LAppLocalHostHolderConsumptionAdapterContractShellChecks.Run();", StringComparison.Ordinal);

        if (index65K < 0 || index65L < 0 || index65L < index65K)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65L must run after Phase 65K.");
        }

        AssertNoGatewayContractLeakOutsideComposition(repoRoot);
        AssertNoProductionAdapterImplementationExists(repoRoot);
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

    private static void AssertNoGatewayContractLeakOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(repoRoot.FullName, "src", "Salryn.App.WinForms");

        string[] forbidden =
        {
            "IAppLocalHostProductLookupGateway",
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
                    throw new InvalidOperationException($"65L contract marker leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }

    private static void AssertNoProductionAdapterImplementationExists(DirectoryInfo repoRoot)
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
                    throw new InvalidOperationException($"65L must not add adapter implementation. Found in {file}: {marker}");
                }
            }
        }
    }
}

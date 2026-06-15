namespace Salryn.Tests;

// Kirch: Verifies the production adapter implementation shell exists without registration or behavior wiring.
public static class Phase65MAppLocalHostHolderConsumptionAdapterImplementationShellChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65M_APP_LOCAL_HOST_HOLDER_CONSUMPTION_ADAPTER_IMPLEMENTATION_SHELL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "65L contract must remain.");
        RequireContains(contractText, "TryLookupByBarcodeAsync", "65L lookup method must remain.");

        RequireContains(adapterText, "namespace Salryn.App.WinForms.Composition;", "65M adapter must live in App.WinForms Composition.");
        RequireContains(adapterText, "public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway", "65M must add adapter implementation shell.");
        RequireContains(adapterText, "TryLookupByBarcodeAsync", "65M adapter must implement lookup method.");
        RequireContains(adapterText, "Task<AppLocalHostProductLookupResult>", "65M adapter must return contract result type.");
        RequireContains(adapterText, "CancellationToken cancellationToken", "65M adapter must preserve cancellation boundary.");
        RequireContains(adapterText, "Task.FromCanceled<AppLocalHostProductLookupResult>", "65M adapter must acknowledge cancellation without dispatch.");
        RequireContains(adapterText, "InvalidBarcode", "65M adapter shell must handle invalid barcode locally.");
        RequireContains(adapterText, "LocalHostUnavailable", "65M adapter shell must remain unavailable until wiring is explicit.");
        RequireContains(adapterText, "Task.FromResult", "65M adapter shell must remain inert and local.");

        RequireContains(adapterText, "SalrynLocalHostCompositionHolder localHostCompositionHolder", "65R may add holder-aware constructor boundary shell without dispatch.");
        RequireNotContains(adapterText, "LocalHostBootstrapCompositionResult", "65M must not expose holder composition internals.");
        RequireNotContains(adapterText, "LocalHostBootstrapComposer", "65M must not compose local host directly.");
        RequireNotContains(adapterText, "ComposeWithProductLookup", "65M must not compose ProductLookup directly.");
        RequireNotContains(adapterText, "DispatchAsync", "65M must not dispatch.");
        RequireNotContains(adapterText, ".Start(", "65M must not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65M must not start runtime.");
        RequireNotContains(adapterText, "Salryn.Host.Local", "65M adapter shell must not reference Host.Local directly.");
        RequireNotContains(adapterText, "Salryn.Infrastructure", "65M adapter shell must not reference Infrastructure.");
        RequireNotContains(adapterText, "Microsoft.Data.Sqlite", "65M adapter shell must not reference SQLite.");
        RequireNotContains(adapterText, "Sqlite", "65M adapter shell must not mention SQLite.");
        RequireNotContains(adapterText, "TcpClient", "65M adapter shell must not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65M adapter shell must not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65M adapter shell must not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65M adapter shell must not add transport.");
        RequireNotContains(adapterText, "System.Net.Sockets", "65M adapter shell must not add sockets.");
        RequireNotContains(adapterText, "System.Net.Http", "65M adapter shell must not add HTTP.");
        RequireNotContains(adapterText, "LanDiscovery", "65M adapter shell must not add LAN discovery.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "Passive holder must remain available.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "Holder creation must remain guarded.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter()", "65P may instantiate the inert adapter through AppServices.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "65P may expose the passive gateway through AppServices.");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Holder must remain sealed.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must remain read-only.");

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
            RequireNotContains(programText, marker, $"Program must remain free of 65M marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65M marker: {marker}");
        }

        RequireNoRuntimeStartDispatchOrTransport(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartDispatchOrTransport(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartDispatchOrTransport(programText, "Program");
        RequireNoRuntimeStartDispatchOrTransport(shellText, "Shell");

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(handoffText, "Status: Production adapter implementation shell only.", "65M handoff must state implementation shell only.");
        RequireContains(handoffText, "No adapter was registered in SalrynAppServices", "65M must not register adapter.");
        RequireContains(handoffText, "No holder was consumed", "65M must not consume holder.");
        RequireContains(handoffText, "No Program startup behavior was changed", "65M must preserve Program.");
        RequireContains(handoffText, "No Shell routing behavior was changed", "65M must preserve Shell.");
        RequireContains(handoffText, "No Local Host runtime was started", "65M must preserve no runtime start.");
        RequireContains(handoffText, "No dispatch was performed", "65M must preserve no dispatch.");
        RequireContains(handoffText, "No transport was implemented", "65M must preserve no transport.");
        RequireContains(handoffText, "Phase 65N", "65M must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65LAppLocalHostHolderConsumptionAdapterContractShellChecks.Run();", "CoreSmoke must still run 65L.");
        RequireContains(coreSmokeText, "Phase65MAppLocalHostHolderConsumptionAdapterImplementationShellChecks.Run();", "CoreSmoke must run 65M.");

        int index65L = coreSmokeText.IndexOf("Phase65LAppLocalHostHolderConsumptionAdapterContractShellChecks.Run();", StringComparison.Ordinal);
        int index65M = coreSmokeText.IndexOf("Phase65MAppLocalHostHolderConsumptionAdapterImplementationShellChecks.Run();", StringComparison.Ordinal);

        if (index65L < 0 || index65M < 0 || index65M < index65L)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65M must run after Phase 65L.");
        }

        AssertAdapterShellConfinedToComposition(repoRoot);
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
            "DispatchAsync",
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

    private static void AssertAdapterShellConfinedToComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(repoRoot.FullName, "src", "Salryn.App.WinForms");

        string[] forbidden =
        {
            "CompositionLocalHostProductLookupGatewayAdapter"
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
                    throw new InvalidOperationException($"65M adapter shell leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}

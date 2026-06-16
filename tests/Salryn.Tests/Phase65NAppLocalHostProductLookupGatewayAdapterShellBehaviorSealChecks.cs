namespace Salryn.Tests;

// Kirch: Seals the inert adapter shell behavior before any AppServices registration or holder consumption.
public static class Phase65NAppLocalHostProductLookupGatewayAdapterShellBehaviorSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "AppLocalHostProductLookupGatewayContracts.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65MText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65M_APP_LOCAL_HOST_HOLDER_CONSUMPTION_ADAPTER_IMPLEMENTATION_SHELL.md");
        string phase65NText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65N_APP_LOCAL_HOST_PRODUCT_LOOKUP_GATEWAY_ADAPTER_SHELL_BEHAVIOR_SEAL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "65L gateway contract must remain.");
        RequireContains(contractText, "TryLookupByBarcodeAsync", "65L gateway method must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupResult", "65L result contract must remain.");

        RequireContains(adapterText, "public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway", "65M adapter shell must remain.");
        RequireContains(adapterText, "public Task<AppLocalHostProductLookupResult> TryLookupByBarcodeAsync", "65M adapter method must remain.");
        RequireContains(adapterText, "if (cancellationToken.IsCancellationRequested)", "65N must preserve cancellation branch.");
        RequireContains(adapterText, "Task.FromCanceled<AppLocalHostProductLookupResult>(cancellationToken)", "65N must preserve canceled task result.");
        RequireContains(adapterText, "request is null || string.IsNullOrWhiteSpace(request.Barcode)", "65N must preserve invalid barcode guard.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.InvalidBarcode(\"Barcode is required.\")", "65N must preserve invalid barcode result.");
        RequireContains(adapterText, "AppLocalHostProductLookupResult.LocalHostUnavailable", "65N must preserve local-host-unavailable result.");
        RequireContains(adapterText, "not wired to the passive holder yet", "65N must preserve not-yet-wired behavior message.");
        RequireContains(adapterText, "Task.FromResult", "65N adapter must remain local and inert.");

        RequireContains(adapterText, "SalrynLocalHostCompositionHolder localHostCompositionHolder", "65R may add holder-aware constructor boundary shell without dispatch.");
        RequireNotContains(adapterText, "LocalHostBootstrapCompositionResult", "65N adapter must still not expose composition internals.");
        RequireNotContains(adapterText, "LocalHostBootstrapComposer", "65N adapter must still not compose local host directly.");
        RequireNotContains(adapterText, "ComposeWithProductLookup", "65N adapter must still not compose ProductLookup directly.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T may dispatch through holder runtime bridge while preserving no runtime start.");
        RequireNotContains(adapterText, ".Start(", "65N adapter must still not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65N adapter must still not start runtime.");
        RequireContains(adapterText, "using Salryn.Host.Local.Contracts;", "65T may reference Host.Local contracts for ProductLookup dispatch bridge.");
        RequireNotContains(adapterText, "Salryn.Infrastructure", "65N adapter must still not reference Infrastructure.");
        RequireNotContains(adapterText, "Microsoft.Data.Sqlite", "65N adapter must still not reference SQLite.");
        RequireNotContains(adapterText, "Sqlite", "65N adapter must still not mention SQLite.");
        RequireNotContains(adapterText, "TcpClient", "65N adapter must still not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65N adapter must still not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65N adapter must still not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65N adapter must still not add transport.");
        RequireNotContains(adapterText, "System.Net.Sockets", "65N adapter must still not add sockets.");
        RequireNotContains(adapterText, "System.Net.Http", "65N adapter must still not add HTTP.");
        RequireNotContains(adapterText, "LanDiscovery", "65N adapter must still not add LAN discovery.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "Passive holder must remain available.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "Holder creation must remain guarded.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder);", "65S may wire the passive holder-aware adapter through AppServices without dispatch.");
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
            RequireNotContains(programText, marker, $"Program must remain free of 65N marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65N marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65MText, "Status: Production adapter implementation shell only.", "65M handoff must remain sealed.");
        RequireContains(phase65NText, "Status: Adapter shell behavior seal only.", "65N handoff must state behavior seal only.");
        RequireContains(phase65NText, "No production app source was changed", "65N must not change production app source.");
        RequireContains(phase65NText, "No adapter was registered in SalrynAppServices", "65N must not register adapter.");
        RequireContains(phase65NText, "No holder was consumed", "65N must not consume holder.");
        RequireContains(phase65NText, "No Program startup behavior was changed", "65N must preserve Program.");
        RequireContains(phase65NText, "No Shell routing behavior was changed", "65N must preserve Shell.");
        RequireContains(phase65NText, "No Local Host runtime was started", "65N must preserve no runtime start.");
        RequireContains(phase65NText, "No dispatch was performed", "65N must preserve no dispatch.");
        RequireContains(phase65NText, "No transport was implemented", "65N must preserve no transport.");
        RequireContains(phase65NText, "Phase 65O", "65N must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65MAppLocalHostHolderConsumptionAdapterImplementationShellChecks.Run();", "CoreSmoke must still run 65M.");
        RequireContains(coreSmokeText, "Phase65NAppLocalHostProductLookupGatewayAdapterShellBehaviorSealChecks.Run();", "CoreSmoke must run 65N.");

        int index65M = coreSmokeText.IndexOf("Phase65MAppLocalHostHolderConsumptionAdapterImplementationShellChecks.Run();", StringComparison.Ordinal);
        int index65N = coreSmokeText.IndexOf("Phase65NAppLocalHostProductLookupGatewayAdapterShellBehaviorSealChecks.Run();", StringComparison.Ordinal);

        if (index65M < 0 || index65N < 0 || index65N < index65M)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65N must run after Phase 65M.");
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
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Composition/CompositionLocalHostProductLookupGatewayAdapter.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string marker in forbidden)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"65N detected runtime/dispatch/transport leak in {file}: {marker}");
                }
            }
        }
    }
}

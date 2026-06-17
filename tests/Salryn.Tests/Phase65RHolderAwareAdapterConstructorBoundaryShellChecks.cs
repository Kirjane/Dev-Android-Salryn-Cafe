namespace Salryn.Tests;

// Kirch: Verifies holder-aware adapter constructor boundary shell only; no dispatch, no runtime start, no transport, no AppServices holder wiring, no UI behavior.
public static class Phase65RHolderAwareAdapterConstructorBoundaryShellChecks
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
        string phase65QText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65Q_HOLDER_AWARE_GATEWAY_WIRING_PLANNING.md");
        string phase65RText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65R_HOLDER_AWARE_ADAPTER_CONSTRUCTOR_BOUNDARY_SHELL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "public interface IAppLocalHostProductLookupGateway", "Gateway contract must remain.");
        RequireContains(contractText, "TryLookupByBarcodeAsync", "Gateway method must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupRequest", "Gateway request must remain.");
        RequireContains(contractText, "AppLocalHostProductLookupResult", "Gateway result must remain.");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Passive holder must remain.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must remain read-only.");

        RequireContains(adapterText, "public sealed class CompositionLocalHostProductLookupGatewayAdapter : IAppLocalHostProductLookupGateway", "Adapter shell must remain.");
        RequireContains(adapterText, "private readonly SalrynLocalHostCompositionHolder? localHostCompositionHolder;", "65R must add passive holder field.");
        RequireContains(adapterText, "public CompositionLocalHostProductLookupGatewayAdapter()", "65R must preserve parameterless constructor.");
        RequireContains(adapterText, "public CompositionLocalHostProductLookupGatewayAdapter(", "65R must add holder-aware constructor boundary.");
        RequireContains(adapterText, "SalrynLocalHostCompositionHolder localHostCompositionHolder", "65R must accept holder through constructor boundary.");
        RequireContains(adapterText, "ArgumentNullException.ThrowIfNull(localHostCompositionHolder);", "65R must guard holder argument.");
        RequireContains(adapterText, "this.localHostCompositionHolder = localHostCompositionHolder;", "65R may store passive holder.");
        RequireContains(adapterText, "LocalHostUnavailable", "Adapter must remain unavailable until dispatch is approved.");
        RequireContains(adapterText, "not wired to holder dispatch yet", "Adapter message must remain honest about no dispatch.");

        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T may access holder composition only through runtime dispatch bridge.");
        RequireNotContains(adapterText, ".Composition.DispatchAsync", "65R must not dispatch through holder composition.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "65T may dispatch through holder runtime bridge while preserving no runtime start.");
        RequireNotContains(adapterText, ".Start(", "65R must not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65R must not start runtime.");
        RequireNotContains(adapterText, "TcpClient", "65R must not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65R must not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65R must not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65R must not add transport.");
        RequireNotContains(adapterText, "System.Net.Sockets", "65R must not add sockets.");
        RequireNotContains(adapterText, "System.Net.Http", "65R must not add HTTP.");
        RequireNotContains(adapterText, "Microsoft.Data.Sqlite", "65R must not reference SQLite.");
        RequireNotContains(adapterText, "Sqlite", "65R must not mention SQLite.");

        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "65S may wire holder into adapter through AppServices boundary without dispatch.");
        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "AppServices must still expose passive holder.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "AppServices must still expose passive gateway.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "AppServices must still create passive holder.");

        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter()", "65S AppServices must not use parameterless adapter wiring.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(CreateLocalHostProductLookupCompositionHolder", "65S must not wire newly created holder inline into adapter.");
        RequireNotContains(appServicesText, "LocalHostProductLookupGateway.TryLookupByBarcodeAsync", "65R must not consume gateway.");
        RequireNotContains(appServicesText, "DispatchAsync", "65R AppServices must not dispatch.");
        RequireNotContains(appServicesText, ".Start(", "65R AppServices must not start runtime.");
        RequireNotContains(appServicesText, "Runtime.Start", "65R AppServices must not start runtime.");

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
            RequireNotContains(programText, marker, $"Program must remain free of 65R marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65R marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65QText, "Status: Holder-aware gateway wiring planning only.", "65Q handoff must remain sealed.");
        RequireContains(phase65RText, "Status: Holder-aware adapter constructor boundary shell only.", "65R handoff must state constructor boundary shell only.");
        RequireContains(phase65RText, "Production source change: adapter constructor boundary shell only.", "65R must acknowledge adapter source change.");
        RequireContains(phase65RText, "No holder dispatch was performed", "65R must not dispatch holder.");
        RequireContains(phase65RText, "No AppServices holder wiring was changed", "65R must not wire AppServices holder.");
        RequireContains(phase65RText, "No runtime was started", "65R must not start runtime.");
        RequireContains(phase65RText, "No dispatch was performed", "65R must not dispatch.");
        RequireContains(phase65RText, "No transport was implemented", "65R must not add transport.");
        RequireContains(phase65RText, "Phase 65S", "65R must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65QHolderAwareGatewayWiringPlanningChecks.Run();", "CoreSmoke must still run 65Q.");
        RequireContains(coreSmokeText, "Phase65RHolderAwareAdapterConstructorBoundaryShellChecks.Run();", "CoreSmoke must run 65R.");

        int index65Q = coreSmokeText.IndexOf("Phase65QHolderAwareGatewayWiringPlanningChecks.Run();", StringComparison.Ordinal);
        int index65R = coreSmokeText.IndexOf("Phase65RHolderAwareAdapterConstructorBoundaryShellChecks.Run();", StringComparison.Ordinal);

        if (index65Q < 0 || index65R < 0 || index65R < index65Q)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65R must run after Phase 65Q.");
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
                    throw new InvalidOperationException($"65R gateway leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}

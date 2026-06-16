namespace Salryn.Tests;

// Kirch: Verifies AppServices holder-aware adapter wiring shell only; no lookup dispatch, runtime start, transport, Program, Shell, or UI behavior.
public static class Phase65SAppServicesHolderAwareAdapterWiringShellChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65RText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65R_HOLDER_AWARE_ADAPTER_CONSTRUCTOR_BOUNDARY_SHELL.md");
        string phase65SText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65S_APPSERVICES_HOLDER_AWARE_ADAPTER_WIRING_SHELL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(adapterText, "public CompositionLocalHostProductLookupGatewayAdapter()", "Adapter parameterless constructor must remain for compatibility.");
        RequireContains(adapterText, "public CompositionLocalHostProductLookupGatewayAdapter(", "Adapter holder-aware constructor must remain.");
        RequireContains(adapterText, "SalrynLocalHostCompositionHolder localHostCompositionHolder", "Adapter must accept passive holder.");
        RequireContains(adapterText, "this.localHostCompositionHolder = localHostCompositionHolder;", "Adapter may store passive holder.");
        RequireContains(adapterText, "LocalHostUnavailable", "Adapter must remain unavailable until dispatch is approved.");
        RequireContains(adapterText, "not wired to holder dispatch yet", "Adapter must remain honest about no dispatch.");

        RequireNotContains(adapterText, "localHostCompositionHolder.Composition", "65S adapter must not access holder composition.");
        RequireNotContains(adapterText, ".Composition.DispatchAsync", "65S adapter must not dispatch through holder composition.");
        RequireNotContains(adapterText, "DispatchAsync", "65S adapter must not dispatch.");
        RequireNotContains(adapterText, ".Start(", "65S adapter must not start runtime.");
        RequireNotContains(adapterText, "Runtime.Start", "65S adapter must not start runtime.");
        RequireNotContains(adapterText, "TcpClient", "65S adapter must not add transport.");
        RequireNotContains(adapterText, "TcpListener", "65S adapter must not add transport.");
        RequireNotContains(adapterText, "UdpClient", "65S adapter must not add transport.");
        RequireNotContains(adapterText, "HttpClient", "65S adapter must not add transport.");
        RequireNotContains(adapterText, "System.Net.Sockets", "65S adapter must not add sockets.");
        RequireNotContains(adapterText, "System.Net.Http", "65S adapter must not add HTTP.");
        RequireNotContains(adapterText, "Microsoft.Data.Sqlite", "65S adapter must not reference SQLite.");
        RequireNotContains(adapterText, "Sqlite", "65S adapter must not mention SQLite.");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Passive holder must remain.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder must remain read-only.");
        RequireNotContains(holderText, "DispatchAsync", "Holder must remain passive.");
        RequireNotContains(holderText, ".Start(", "Holder must not start runtime.");
        RequireNotContains(holderText, "Runtime.Start", "Holder must not start runtime.");

        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "AppServices must expose passive holder.");
        RequireContains(appServicesText, "public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }", "AppServices must expose passive gateway.");
        RequireContains(appServicesText, "// Kirch: Creates the passive holder before gateway wiring; this still does not start runtime, dispatch, or add transport.", "65S holder creation comment must remain.");
        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "65S must assign holder property.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "65S must create passive holder through guarded method.");
        RequireContains(appServicesText, "// Kirch: Wires only the passive holder-aware adapter shell; lookup dispatch remains a later approved phase.", "65S holder-aware gateway wiring comment must remain.");
        RequireContains(appServicesText, "LocalHostProductLookupGateway =", "65S must assign gateway property.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder);", "65S must wire passive holder into adapter shell.");

        int holderIndex = appServicesText.IndexOf("LocalHostProductLookupCompositionHolder =", StringComparison.Ordinal);
        int gatewayIndex = appServicesText.IndexOf("LocalHostProductLookupGateway =", StringComparison.Ordinal);

        if (holderIndex < 0 || gatewayIndex < 0 || gatewayIndex < holderIndex)
        {
            throw new InvalidOperationException("65S AppServices must create holder before wiring gateway.");
        }

        RequireNotContains(appServicesText, "IAppLocalHostProductLookupGateway localHostProductLookupGateway", "65S must not inject inert gateway into constructor.");
        RequireNotContains(appServicesText, "LocalHostProductLookupGateway = localHostProductLookupGateway;", "65S must not assign inert gateway parameter.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter()", "65S AppServices must not use parameterless adapter wiring.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(CreateLocalHostProductLookupCompositionHolder", "65S must not inline holder creation into adapter wiring.");
        RequireNotContains(appServicesText, "LocalHostProductLookupGateway.TryLookupByBarcodeAsync", "65S must not consume gateway.");
        RequireNotContains(appServicesText, "DispatchAsync", "65S AppServices must not dispatch.");
        RequireNotContains(appServicesText, ".Start(", "65S AppServices must not start runtime.");
        RequireNotContains(appServicesText, "Runtime.Start", "65S AppServices must not start runtime.");

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
            RequireNotContains(programText, marker, $"Program must remain free of 65S marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of 65S marker: {marker}");
        }

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65RText, "Status: Holder-aware adapter constructor boundary shell only.", "65R handoff must remain sealed.");
        RequireContains(phase65SText, "Status: AppServices holder-aware adapter wiring shell only.", "65S handoff must state wiring shell only.");
        RequireContains(phase65SText, "Production source change: AppServices holder-aware adapter wiring shell only.", "65S must acknowledge production source change.");
        RequireContains(phase65SText, "No holder dispatch was performed", "65S must not dispatch holder.");
        RequireContains(phase65SText, "No runtime was started", "65S must not start runtime.");
        RequireContains(phase65SText, "No dispatch was performed", "65S must not dispatch.");
        RequireContains(phase65SText, "No transport was implemented", "65S must not add transport.");
        RequireContains(phase65SText, "Phase 65T", "65S must point to next separate phase.");

        RequireContains(coreSmokeText, "Phase65RHolderAwareAdapterConstructorBoundaryShellChecks.Run();", "CoreSmoke must still run 65R.");
        RequireContains(coreSmokeText, "Phase65SAppServicesHolderAwareAdapterWiringShellChecks.Run();", "CoreSmoke must run 65S.");

        int index65R = coreSmokeText.IndexOf("Phase65RHolderAwareAdapterConstructorBoundaryShellChecks.Run();", StringComparison.Ordinal);
        int index65S = coreSmokeText.IndexOf("Phase65SAppServicesHolderAwareAdapterWiringShellChecks.Run();", StringComparison.Ordinal);

        if (index65R < 0 || index65S < 0 || index65S < index65R)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65S must run after Phase 65R.");
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
                    throw new InvalidOperationException($"65S gateway leaked outside App.WinForms Composition: {file} contains {marker}");
                }
            }
        }
    }
}
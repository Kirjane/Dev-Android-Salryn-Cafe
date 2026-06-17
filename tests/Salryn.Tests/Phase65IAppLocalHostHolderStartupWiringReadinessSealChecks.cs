namespace Salryn.Tests;

// Kirch: Seals Phase 65H startup holder wiring readiness before any consumption phase.
public static class Phase65IAppLocalHostHolderStartupWiringReadinessSealChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string holderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostCompositionHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string appProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Salryn.App.WinForms.csproj");
        string hostProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");
        string phase65HText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65H_APP_LOCAL_HOST_HOLDER_STARTUP_WIRING_SLICE.md");
        string phase65IText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65I_APP_LOCAL_HOST_HOLDER_STARTUP_WIRING_READINESS_SEAL.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(appServicesText, "StoreDeploymentProfile localHostProfile)", "65H constructor profile parameter must remain.");
        RequireContains(appServicesText, "LocalHostProductLookupCompositionHolder =", "65H holder assignment must remain.");
        RequireContains(appServicesText, "CreateLocalHostProductLookupCompositionHolder(localHostProfile);", "65H holder assignment must use guarded holder factory.");
        RequireContains(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "65H holder property must remain.");
        RequireContains(appServicesText, "private static StoreDeploymentProfile CreateStandaloneLocalHostProfile()", "65H standalone profile factory must remain.");
        RequireContains(appServicesText, "Guid.Parse(\"11111111-1111-1111-1111-111111111111\")", "Startup TenantId must remain deterministic.");
        RequireContains(appServicesText, "Guid.Parse(\"22222222-2222-2222-2222-222222222222\")", "Startup StoreId must remain deterministic.");
        RequireContains(appServicesText, "Guid.Parse(\"33333333-3333-3333-3333-333333333333\")", "Startup HostId must remain deterministic.");
        RequireContains(appServicesText, "Environment.MachineName", "Startup profile must remain local-machine identified.");
        RequireContains(appServicesText, "SalrynMachineRole.StandaloneHost", "Startup profile must remain standalone host.");
        RequireContains(appServicesText, "SalrynDeploymentMode.Standalone", "Startup profile must remain standalone mode.");
        RequireContains(appServicesText, "Endpoint: null", "Startup profile must not introduce endpoint wiring.");
        RequireContains(appServicesText, "new TerminalIdentity(", "Startup profile must not introduce terminal wiring.");
        RequireContains(appServicesText, "CreateStandaloneLocalHostProfile());", "AppServices factory must keep startup profile injection.");

        RequireExactlyOne(appServicesText, "public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }", "65H holder property");
        RequireExactlyOne(appServicesText, "LocalHostProductLookupCompositionHolder =", "65H holder assignment");
        RequireExactlyOne(appServicesText, "private static StoreDeploymentProfile CreateStandaloneLocalHostProfile()", "65H standalone profile factory");
        RequireExactlyOne(appServicesText, "CreateStandaloneLocalHostProfile());", "65H standalone profile factory call");

        RequireNotContains(appServicesText, "Guid.NewGuid", "Startup wiring must not generate random identities.");
        RequireNoRuntimeStartDispatchOrTransport(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartDispatchOrTransport(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartDispatchOrTransport(programText, "Program");
        RequireNoRuntimeStartDispatchOrTransport(shellText, "Shell");

        RequireContains(holderText, "public sealed class SalrynLocalHostCompositionHolder", "Holder must remain sealed.");
        RequireContains(holderText, "ArgumentNullException.ThrowIfNull(composition);", "Holder must still reject null composition.");
        RequireContains(holderText, "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))", "Holder must still validate composition.");
        RequireContains(holderText, "throw new InvalidOperationException(\"Invalid held app Local Host composition.\");", "Holder must still fail closed.");
        RequireContains(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "Holder composition must remain read-only.");
        RequireExactlyOne(holderText, "public LocalHostBootstrapCompositionResult Composition { get; }", "holder read-only composition property");

        string[] programShellForbidden =
        {
            "SalrynLocalHostCompositionHolder",
            "CreateLocalHostProductLookupCompositionHolder",
            "CreateLocalHostProductLookupComposition",
            "LocalHostBootstrapCompositionResult",
            "LocalHostBootstrapComposer",
            "ComposeWithProductLookup",
            "ProductLookup"
        };

        foreach (string marker in programShellForbidden)
        {
            RequireNotContains(programText, marker, $"Program must remain free of {marker}.");
            RequireNotContains(shellText, marker, $"Shell must remain free of {marker}.");
        }

        RequireContains(appProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "App.WinForms must still reference Core.");
        RequireContains(appProjectText, "..\\Salryn.Infrastructure\\Salryn.Infrastructure.csproj", "App.WinForms must still reference Infrastructure.");
        RequireContains(appProjectText, "..\\Salryn.Host.Local\\Salryn.Host.Local.csproj", "App.WinForms must still reference Host.Local.");

        RequireContains(hostProjectText, "..\\Salryn.Core\\Salryn.Core.csproj", "Host.Local must still reference Core.");
        RequireNotContains(hostProjectText, "Salryn.Infrastructure", "Host.Local must not reference Infrastructure.");
        RequireNotContains(hostProjectText, "Salryn.App.WinForms", "Host.Local must not reference App.WinForms.");

        RequireContains(phase65HText, "Status: Startup wiring only.", "65H handoff must remain startup wiring only.");
        RequireContains(phase65HText, "No Program startup behavior was changed", "65H handoff must preserve Program boundary.");
        RequireContains(phase65HText, "No Shell routing behavior was changed", "65H handoff must preserve Shell boundary.");
        RequireContains(phase65HText, "No Local Host runtime was started", "65H handoff must preserve no runtime start.");
        RequireContains(phase65HText, "No dispatch was performed", "65H handoff must preserve no dispatch.");
        RequireContains(phase65HText, "No transport was implemented", "65H handoff must preserve no transport.");

        RequireContains(phase65IText, "Status: Readiness seal.", "65I handoff must state readiness seal.");
        RequireContains(phase65IText, "No production source behavior was changed", "65I handoff must state no production behavior change.");
        RequireContains(phase65IText, "No Program startup behavior was changed", "65I handoff must preserve Program boundary.");
        RequireContains(phase65IText, "No Shell routing behavior was changed", "65I handoff must preserve Shell boundary.");
        RequireContains(phase65IText, "No Local Host runtime was started", "65I handoff must preserve no runtime start.");
        RequireContains(phase65IText, "No dispatch was performed", "65I handoff must preserve no dispatch.");
        RequireContains(phase65IText, "No transport was implemented", "65I handoff must preserve no transport.");
        RequireContains(phase65IText, "Phase 65J", "65I handoff must point to a separate future phase.");

        RequireContains(coreSmokeText, "Phase65HAppLocalHostHolderStartupWiringSliceChecks.Run();", "CoreSmoke must still run 65H.");
        RequireContains(coreSmokeText, "Phase65IAppLocalHostHolderStartupWiringReadinessSealChecks.Run();", "CoreSmoke must run 65I.");

        int index65H = coreSmokeText.IndexOf("Phase65HAppLocalHostHolderStartupWiringSliceChecks.Run();", StringComparison.Ordinal);
        int index65I = coreSmokeText.IndexOf("Phase65IAppLocalHostHolderStartupWiringReadinessSealChecks.Run();", StringComparison.Ordinal);

        if (index65H < 0 || index65I < 0 || index65I < index65H)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65I must run after Phase 65H.");
        }

        AssertAppHostLocalWiringStillConfinedToComposition(repoRoot);
        AssertAppInfrastructureWiringStillConfinedToComposition(repoRoot);
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

    private static void AssertAppHostLocalWiringStillConfinedToComposition(DirectoryInfo repoRoot)
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
                || normalized.Contains("/Composition/", StringComparison.OrdinalIgnoreCase))
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

    private static void AssertAppInfrastructureWiringStillConfinedToComposition(DirectoryInfo repoRoot)
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
                || normalized.Contains("/Composition/", StringComparison.OrdinalIgnoreCase))
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

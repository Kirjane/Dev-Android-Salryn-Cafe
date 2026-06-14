namespace Salryn.Tests;

// Kirch: Verifies the app Local Host composition holder slice stays holder-only and composition-root confined.
public static class Phase65EAppLocalHostCompositionHolderSliceChecks
{
    // Kirch: Proves the holder validates composition without Program, Shell, runtime, dispatch, transport, or UI behavior.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string holderText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynLocalHostCompositionHolder.cs");

        string appServicesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");

        string programText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Program.cs");

        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");

        string hostProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Salryn.Host.Local.csproj");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            holderText,
            "using Salryn.Host.Local.Composition;",
            "Holder must import only Host.Local composition models.");

        RequireContains(
            holderText,
            "public sealed class SalrynLocalHostCompositionHolder",
            "Holder class must exist and stay sealed.");

        RequireContains(
            holderText,
            "public SalrynLocalHostCompositionHolder(LocalHostBootstrapCompositionResult composition)",
            "Holder must accept the Local Host composition result.");

        RequireContains(
            holderText,
            "ArgumentNullException.ThrowIfNull(composition);",
            "Holder must reject null composition.");

        RequireContains(
            holderText,
            "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))",
            "Holder must validate composition with Host.Local composition rules.");

        RequireContains(
            holderText,
            "throw new InvalidOperationException(\"Invalid held app Local Host composition.\");",
            "Holder must fail closed when composition is invalid.");

        RequireContains(
            holderText,
            "public LocalHostBootstrapCompositionResult Composition { get; }",
            "Holder must expose only a read-only Composition property.");

        RequireNotContains(
            holderText,
            "Composition { get; set; }",
            "Holder Composition property must not be mutable.");

        RequireNotContains(
            holderText,
            "Composition { get; init; }",
            "Holder Composition property must not be init-mutable.");

        RequireContains(
            appServicesText,
            "public SalrynLocalHostCompositionHolder CreateLocalHostProductLookupCompositionHolder(StoreDeploymentProfile profile)",
            "App services must expose holder creation through the composition root.");

        RequireContains(
            appServicesText,
            "LocalHostBootstrapCompositionResult composition =",
            "Holder factory must capture the guarded composition.");

        RequireContains(
            appServicesText,
            "CreateLocalHostProductLookupComposition(profile);",
            "Holder factory must use the existing guarded composition method.");

        RequireContains(
            appServicesText,
            "return new SalrynLocalHostCompositionHolder(composition);",
            "Holder factory must return the controlled holder.");

        RequireNotContains(
            programText,
            "SalrynLocalHostCompositionHolder",
            "Program must not receive or create the Local Host composition holder in 65E.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupCompositionHolder",
            "Program must not call the holder factory in 65E.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupComposition",
            "Program must not create Local Host composition in 65E.");

        RequireNotContains(
            programText,
            "LocalHostBootstrapCompositionResult",
            "Program must not receive Local Host composition in 65E.");

        RequireNotContains(
            programText,
            "ProductLookup",
            "Program must not add ProductLookup startup behavior in 65E.");

        RequireNotContains(
            shellText,
            "SalrynLocalHostCompositionHolder",
            "Shell must not receive or store the Local Host composition holder in 65E.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupCompositionHolder",
            "Shell must not call the holder factory in 65E.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupComposition",
            "Shell must not create Local Host composition in 65E.");

        RequireNotContains(
            shellText,
            "LocalHostBootstrapCompositionResult",
            "Shell must not receive Local Host composition in 65E.");

        RequireNotContains(
            shellText,
            "ProductLookup",
            "Shell must not add ProductLookup UI behavior in 65E.");

        RequireContains(
            hostProjectText,
            "..\\Salryn.Core\\Salryn.Core.csproj",
            "Host.Local must still reference Core.");

        RequireNotContains(
            hostProjectText,
            "Salryn.Infrastructure",
            "Host.Local must not reference Infrastructure.");

        RequireNotContains(
            hostProjectText,
            "Salryn.App.WinForms",
            "Host.Local must not reference App.WinForms.");

        RequireContains(
            coreSmokeText,
            "Phase65DAppLocalHostCompositionReadinessSealChecks.Run();",
            "CoreSmoke must still run Phase 65D.");

        RequireContains(
            coreSmokeText,
            "Phase65EAppLocalHostCompositionHolderSliceChecks.Run();",
            "CoreSmoke must run Phase 65E.");

        if (coreSmokeText.IndexOf("Phase65EAppLocalHostCompositionHolderSliceChecks.Run();", StringComparison.Ordinal)
            < coreSmokeText.IndexOf("Phase65DAppLocalHostCompositionReadinessSealChecks.Run();", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65E must run after Phase 65D.");
        }

        RequireNoRuntimeStartOrDispatch(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartOrDispatch(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartOrDispatch(programText, "Program");
        RequireNoRuntimeStartOrDispatch(shellText, "Shell");

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);
    }

    // Kirch: Finds the repository root for holder-only composition checks.
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

    // Kirch: Reads a required repository file.
    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    // Kirch: Requires a source marker to exist.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Requires a forbidden source marker to be absent.
    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Keeps Phase 65E holder-only by rejecting runtime activation, dispatch, and transport markers.
    private static void RequireNoRuntimeStartOrDispatch(string text, string owner)
    {
        string[] forbiddenPatterns =
        {
            ".Start(",
            "Runtime.Start",
            "DispatchAsync<",
            "TcpClient",
            "TcpListener",
            "HttpClient",
            "UdpClient",
            "System.Net.Sockets",
            "System.Net.Http",
            "LanDiscovery"
        };

        foreach (string forbiddenPattern in forbiddenPatterns)
        {
            if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{owner} must not start runtime, dispatch, or add transport behavior in 65E. Found {forbiddenPattern}");
            }
        }
    }

    // Kirch: Ensures Host.Local wiring remains inside App.WinForms Composition only.
    private static void AssertNoLocalHostOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(
            repoRoot.FullName,
            "src",
            "Salryn.App.WinForms");

        string compositionRoot = Path.Combine(
            appRoot,
            "Composition");

        string[] forbiddenPatterns =
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
            if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(compositionRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string forbiddenPattern in forbiddenPatterns)
            {
                if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Local Host wiring leaked outside App.WinForms composition root: {file} contains {forbiddenPattern}");
                }
            }
        }
    }

    // Kirch: Ensures Infrastructure wiring remains inside App.WinForms Composition only.
    private static void AssertNoInfrastructureOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(
            repoRoot.FullName,
            "src",
            "Salryn.App.WinForms");

        string compositionRoot = Path.Combine(
            appRoot,
            "Composition");

        string[] forbiddenPatterns =
        {
            "Salryn.Infrastructure",
            "Microsoft.Data.Sqlite",
            "SqliteConnectionFactory",
            "SqliteRepositoryFactory",
            "DefaultDatabaseInitializationFactory"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(compositionRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string forbiddenPattern in forbiddenPatterns)
            {
                if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Infrastructure wiring leaked outside App.WinForms composition root: {file} contains {forbiddenPattern}");
                }
            }
        }
    }
}

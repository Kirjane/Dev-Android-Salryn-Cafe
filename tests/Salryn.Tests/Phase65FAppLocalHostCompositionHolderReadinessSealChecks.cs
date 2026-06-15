namespace Salryn.Tests;

// Kirch: Seals the app Local Host composition holder before any Program or Shell consumer is added.
public static class Phase65FAppLocalHostCompositionHolderReadinessSealChecks
{
    // Kirch: Proves the holder path is passive, guarded, unique, composition-confined, and still unused by runtime/UI.
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

        string appProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Salryn.App.WinForms.csproj");

        string hostProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Salryn.Host.Local.csproj");

        string phase65EText = ReadRequiredFile(
            repoRoot,
            "tests",
            "Salryn.Tests",
            "Phase65EAppLocalHostCompositionHolderSliceChecks.cs");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Core\\Salryn.Core.csproj\" />",
            "App.WinForms must keep the Core reference.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Infrastructure\\Salryn.Infrastructure.csproj\" />",
            "App.WinForms must keep Infrastructure reference for composition root wiring.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Host.Local\\Salryn.Host.Local.csproj\" />",
            "App.WinForms must keep the controlled Host.Local reference.");

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
            holderText,
            "public sealed class SalrynLocalHostCompositionHolder",
            "Holder class must exist and remain sealed.");

        RequireContains(
            holderText,
            "public SalrynLocalHostCompositionHolder(LocalHostBootstrapCompositionResult composition)",
            "Holder must receive the composed Local Host result explicitly.");

        RequireContains(
            holderText,
            "ArgumentNullException.ThrowIfNull(composition);",
            "Holder must reject null composition.");

        RequireContains(
            holderText,
            "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(composition))",
            "Holder must validate held composition.");

        RequireContains(
            holderText,
            "throw new InvalidOperationException(\"Invalid held app Local Host composition.\");",
            "Holder must fail closed for invalid composition.");

        RequireContains(
            holderText,
            "public LocalHostBootstrapCompositionResult Composition { get; }",
            "Holder Composition must be read-only.");

        RequireNotContains(
            holderText,
            "set;",
            "Holder must not expose mutable setters.");

        RequireNotContains(
            holderText,
            "init;",
            "Holder must not expose init setters.");

        RequireNotContains(
            holderText,
            "Start(",
            "Holder must not start Local Host runtime.");

        RequireNotContains(
            holderText,
            "DispatchAsync",
            "Holder must not dispatch Local Host operations.");

        RequireContains(
            appServicesText,
            "public SalrynLocalHostCompositionHolder CreateLocalHostProductLookupCompositionHolder(StoreDeploymentProfile profile)",
            "AppServices must expose the holder factory.");

        RequireContains(
            appServicesText,
            "CreateLocalHostProductLookupComposition(profile);",
            "Holder factory must use the existing guarded composition method.");

        RequireContains(
            appServicesText,
            "return new SalrynLocalHostCompositionHolder(composition);",
            "Holder factory must return the passive holder.");

        RequireContains(
            appServicesText,
            "private LocalHostBootstrapCompositionResult CreateUncheckedLocalHostProductLookupComposition(StoreDeploymentProfile profile)",
            "Unchecked raw Local Host composition must remain private.");

        RequireExactlyOne(
            appServicesText,
            "CreateLocalHostProductLookupCompositionHolder(StoreDeploymentProfile profile)",
            "AppServices holder factory signature");

        RequireExactlyOne(
            appServicesText,
            "return new SalrynLocalHostCompositionHolder(composition);",
            "AppServices holder return");

        RequireExactlyOne(
            holderText,
            "public sealed class SalrynLocalHostCompositionHolder",
            "Holder class declaration");

        RequireExactlyOne(
            holderText,
            "public LocalHostBootstrapCompositionResult Composition { get; }",
            "Holder Composition property");

        RequireContains(
            phase65EText,
            "Phase65EAppLocalHostCompositionHolderSliceChecks",
            "65E holder slice checks must remain present.");

        RequireContains(
            phase65EText,
            "AssertNoLocalHostOutsideComposition(repoRoot);",
            "65E must still guard Host.Local composition confinement.");

        RequireContains(
            phase65EText,
            "AssertNoInfrastructureOutsideComposition(repoRoot);",
            "65E must still guard Infrastructure composition confinement.");

        RequireNotContains(
            programText,
            "SalrynLocalHostCompositionHolder",
            "Program must not receive the Local Host holder in 65F.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupCompositionHolder",
            "Program must not call the holder factory in 65F.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupComposition",
            "Program must not create Local Host composition in 65F.");

        RequireNotContains(
            programText,
            "LocalHostBootstrapCompositionResult",
            "Program must not receive Local Host composition result in 65F.");

        RequireNotContains(
            programText,
            "ProductLookup",
            "Program must not add ProductLookup startup behavior in 65F.");

        RequireNotContains(
            shellText,
            "SalrynLocalHostCompositionHolder",
            "Shell must not receive the Local Host holder in 65F.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupCompositionHolder",
            "Shell must not call the holder factory in 65F.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupComposition",
            "Shell must not create Local Host composition in 65F.");

        RequireNotContains(
            shellText,
            "LocalHostBootstrapCompositionResult",
            "Shell must not receive Local Host composition result in 65F.");

        RequireNotContains(
            shellText,
            "ProductLookup",
            "Shell must not add ProductLookup UI behavior in 65F.");

        RequireNoRuntimeStartOrDispatch(holderText, "SalrynLocalHostCompositionHolder");
        RequireNoRuntimeStartOrDispatch(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartOrDispatch(programText, "Program");
        RequireNoRuntimeStartOrDispatch(shellText, "Shell");

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);

        RequireContains(
            coreSmokeText,
            "Phase65EAppLocalHostCompositionHolderSliceChecks.Run();",
            "CoreSmoke must still run Phase 65E.");

        RequireContains(
            coreSmokeText,
            "Phase65FAppLocalHostCompositionHolderReadinessSealChecks.Run();",
            "CoreSmoke must run Phase 65F.");

        int index65E = coreSmokeText.IndexOf(
            "Phase65EAppLocalHostCompositionHolderSliceChecks.Run();",
            StringComparison.Ordinal);

        int index65F = coreSmokeText.IndexOf(
            "Phase65FAppLocalHostCompositionHolderReadinessSealChecks.Run();",
            StringComparison.Ordinal);

        if (index65E < 0 || index65F < 0 || index65F < index65E)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65F must run after Phase 65E.");
        }
    }

    // Kirch: Finds the repository root for holder readiness seal checks.
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

    // Kirch: Requires a marker to appear once only.
    private static void RequireExactlyOne(string text, string value, string label)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        if (count != 1)
        {
            throw new InvalidOperationException($"{label} must appear exactly once. Actual count: {count}.");
        }
    }

    // Kirch: Keeps Phase 65F seal-only by rejecting runtime activation, dispatch, and transport markers.
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
                    $"{owner} must not start runtime, dispatch, or add transport behavior in 65F. Found {forbiddenPattern}");
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

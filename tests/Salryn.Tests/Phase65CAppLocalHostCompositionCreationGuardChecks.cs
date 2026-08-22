namespace Salryn.Tests;

// Kirch: Verifies the app composition root guards Local Host composition creation before future runtime use.
public static class Phase65CAppLocalHostCompositionCreationGuardChecks
{
    // Kirch: Proves 65C adds app-level composition validation without starting runtime or touching shell behavior.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Salryn.App.WinForms.csproj");

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

        string hostComposerText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Composition",
            "LocalHostBootstrapCompositionModels.cs");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Host.Local\\Salryn.Host.Local.csproj\" />",
            "65C requires the controlled App.WinForms to Host.Local reference from 65B.");

        RequireContains(
            appServicesText,
            "public LocalHostBootstrapCompositionResult CreateLocalHostProductLookupComposition(StoreDeploymentProfile profile)",
            "App services must keep the controlled Local Host ProductLookup composition method.");

        RequireContains(
            appServicesText,
            "ArgumentNullException.ThrowIfNull(profile);",
            "App Local Host composition must keep the profile guard.");

        RequireContains(
            appServicesText,
            "LocalHostBootstrapCompositionResult result =",
            "App Local Host composition must capture the composed result before returning it.");

        RequireContains(
            appServicesText,
            "CreateUncheckedLocalHostProductLookupComposition(profile);",
            "App Local Host composition must isolate raw composition behind a guarded public method.");

        RequireContains(
            appServicesText,
            "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(result))",
            "App Local Host composition must validate the composition result.");

        RequireContains(
            appServicesText,
            "throw new InvalidOperationException(\"Invalid app Local Host ProductLookup composition.\");",
            "App Local Host composition must fail closed on invalid composition.");

        RequireContains(
            appServicesText,
            "return result;",
            "App Local Host composition must return only the validated result.");

        RequireContains(
            appServicesText,
            "private LocalHostBootstrapCompositionResult CreateUncheckedLocalHostProductLookupComposition(StoreDeploymentProfile profile)",
            "Raw Local Host composition must remain private to the app composition root.");

        RequireContains(
            appServicesText,
            "LocalHostBootstrapComposer composer = new();",
            "Raw Local Host composer construction must remain inside the composition root.");

        RequireContains(
            appServicesText,
            "return composer.ComposeWithProductLookup(",
            "Raw Local Host composition must keep using the explicit ProductLookup composition path.");

        RequireContains(
            appServicesText,
            "ProductReadRepository,",
            "Raw Local Host composition must still use the existing product repository contract.");

        RequireContains(
            appServicesText,
            "new OfficialExternalOutputTextFileWorkflowRunner()",
            "10D raw Local Host composition may include the app-owned official output text-file workflow runner through the Core port.");

        RequireContains(
            hostComposerText,
            "public static bool IsCompositionValid(LocalHostBootstrapCompositionResult? result)",
            "Host.Local composition rule must remain available.");

        RequireContains(
            hostComposerText,
            "result.Snapshot.State == LocalHostRuntimeState.Stopped",
            "Host.Local composition rule must require stopped runtime state.");

        RequireContains(
            hostComposerText,
            "result.Snapshot.RegisteredContractCount == result.Snapshot.RegisteredOperationCount",
            "Host.Local composition rule must require operation/contract alignment.");

        RequireContains(
            hostComposerText,
            "result.Snapshot.RegisteredHandlerCount == result.Snapshot.RegisteredOperationCount",
            "Host.Local composition rule must require operation/handler alignment.");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupComposition",
            "65C must not call Local Host composition from Program.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupComposition",
            "65C must not call Local Host composition from Shell.");

        RequireNotContains(
            shellText,
            "LocalHostBootstrapComposer",
            "65C must not wire Local Host directly into Shell.");

        RequireNotContains(
            shellText,
            "ComposeWithProductLookup",
            "65C must not add ProductLookup shell behavior.");

        RequireNoRuntimeStartOrDispatch(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartOrDispatch(programText, "Program");
        RequireNoRuntimeStartOrDispatch(shellText, "Shell");

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);
    }

    // Kirch: Finds the repository root for text-based guard checks.
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

    // Kirch: Requires a source marker to be absent.
    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Keeps 65C creation-only by rejecting runtime activation or dispatch markers.
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
            "System.Net.Sockets"
        };

        foreach (string forbiddenPattern in forbiddenPatterns)
        {
            if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{owner} must not start runtime, dispatch, or add transport behavior in 65C. Found {forbiddenPattern}");
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
            "ComposeWithProductLookup",
            "CreateLocalHostProductLookupComposition"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
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

    // Kirch: Ensures Infrastructure wiring still stays inside the WinForms composition root.
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
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
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

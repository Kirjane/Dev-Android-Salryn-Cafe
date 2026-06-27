namespace Salryn.Tests;

// Kirch: Verifies the first controlled App.WinForms reference to Salryn.Host.Local.
public static class Phase65BAppLocalHostCompositionReferenceSliceChecks
{
    // Kirch: Proves App.WinForms can reference Host.Local only through SalrynAppServices and without shell behavior.
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

        string hostProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Salryn.Host.Local.csproj");

        string hostComposerText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Composition",
            "LocalHostBootstrapCompositionModels.cs");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Core\\Salryn.Core.csproj\" />",
            "App.WinForms must still reference Core.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Infrastructure\\Salryn.Infrastructure.csproj\" />",
            "App.WinForms must still reference Infrastructure for composition root wiring.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Host.Local\\Salryn.Host.Local.csproj\" />",
            "65B must add the explicit App.WinForms to Host.Local project reference.");

        RequireContains(
            appServicesText,
            "using Salryn.Core.Deployment;",
            "App services must accept StoreDeploymentProfile for controlled Local Host composition.");

        RequireContains(
            appServicesText,
            "using Salryn.Host.Local.Composition;",
            "Only the app composition root may import Host.Local composition.");

        RequireContains(
            appServicesText,
            "public LocalHostBootstrapCompositionResult CreateLocalHostProductLookupComposition(StoreDeploymentProfile profile)",
            "App services must expose a controlled Local Host ProductLookup composition method.");

        RequireContains(
            appServicesText,
            "ArgumentNullException.ThrowIfNull(profile);",
            "Local Host composition method must guard the deployment profile.");

        RequireContains(
            appServicesText,
            "LocalHostBootstrapComposer composer = new();",
            "App services must construct the Local Host composer in the composition root only.");

        RequireContains(
            appServicesText,
            "return composer.ComposeWithProductLookup(",
            "App services must call the explicit ProductLookup composition path.");

        RequireContains(
            appServicesText,
            "ProductReadRepository,",
            "App services must pass the existing Core product repository contract.");

        RequireContains(
            appServicesText,
            "new OfficialExternalOutputTextFileWorkflowRunner()",
            "10D App services may pass the app-owned official output text-file workflow runner into Host.Local through the Core port.");

        RequireContains(
            hostComposerText,
            "public LocalHostBootstrapCompositionResult ComposeWithProductLookup(",
            "Host.Local explicit ProductLookup composition path must remain available.");

        RequireContains(
            hostComposerText,
            "IProductReadRepository products",
            "Host.Local composition must still depend on the Core repository contract.");

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

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupComposition",
            "65B must not create Local Host composition from Program yet.");

        RequireNotContains(
            programText,
            "LocalHostBootstrapComposer",
            "65B must not wire Local Host directly into Program.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupComposition",
            "65B must not use Local Host composition from the shell yet.");

        RequireNotContains(
            shellText,
            "LocalHostBootstrapComposer",
            "65B must not wire Local Host directly into the shell.");

        RequireNotContains(
            shellText,
            "ComposeWithProductLookup",
            "65B must not dispatch ProductLookup through shell behavior yet.");

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);
    }

    // Kirch: Finds the repository root for text-based boundary checks.
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

    // Kirch: Ensures Host.Local does not leak into Program, shell, forms, or feature code.
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

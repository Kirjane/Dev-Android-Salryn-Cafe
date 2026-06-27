namespace Salryn.Tests;

// Kirch: Plans the app composition boundary before and after the controlled Host.Local reference slice.
public static class Phase65AAppCompositionBoundaryPlanningChecks
{
    // Kirch: Keeps the 65A planning guard valid after 65B by allowing only the planned composition-root reference.
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
            "<ProjectReference Include=\"..\\Salryn.Core\\Salryn.Core.csproj\" />",
            "App.WinForms must keep its Core reference.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Infrastructure\\Salryn.Infrastructure.csproj\" />",
            "App.WinForms composition root must still be allowed to wire Infrastructure.");

        RequireContains(
            appProjectText,
            "<UseWindowsForms>true</UseWindowsForms>",
            "App.WinForms must remain the WinForms project.");

        RequireContains(
            appServicesText,
            "public sealed class SalrynAppServices",
            "App composition root must remain SalrynAppServices.");

        RequireContains(
            appServicesText,
            "public IProductReadRepository ProductReadRepository { get; }",
            "App composition root must expose the Core product read repository for Local Host composition.");

        RequireContains(
            appServicesText,
            "repositories.CreateProductReadRepository();",
            "App composition root must create ProductReadRepository through the repository factory.");

        RequireContains(
            hostComposerText,
            "public LocalHostBootstrapCompositionResult ComposeWithProductLookup(",
            "Host.Local explicit ProductLookup composition path must exist before app wiring.");

        RequireContains(
            hostComposerText,
            "IProductReadRepository products",
            "Host.Local explicit ProductLookup composition must accept the Core product read repository contract.");

        bool appReferencesHostLocal = appProjectText.Contains("Salryn.Host.Local", StringComparison.Ordinal);

        if (appReferencesHostLocal)
        {
            RequireContains(
                appServicesText,
                "using Salryn.Host.Local.Composition;",
                "After 65B, only the app composition root may reference Host.Local composition.");

            RequireContains(
                appServicesText,
                "CreateLocalHostProductLookupComposition",
                "After 65B, app services must expose a controlled Local Host composition method.");

            RequireContains(
                appServicesText,
                "LocalHostBootstrapComposer composer = new();",
                "After 65B, app services must construct the Local Host composer only inside the composition root.");

            RequireContains(
                appServicesText,
                "return composer.ComposeWithProductLookup(",
                "After 65B, app services must use the explicit ProductLookup composition path.");

            RequireContains(
                appServicesText,
                "ProductReadRepository,",
                "After 65B, Local Host ProductLookup composition must use the existing ProductReadRepository.");

            RequireContains(
                appServicesText,
                "new OfficialExternalOutputTextFileWorkflowRunner()",
                "10D Local Host composition must receive the app-owned official output text-file workflow runner through the Core port.");
        }
        else
        {
            RequireNotContains(
                appServicesText,
                "LocalHostBootstrapComposer",
                "Before 65B, SalrynAppServices must not wire Local Host composition.");

            RequireNotContains(
                appServicesText,
                "ComposeWithProductLookup",
                "Before 65B, SalrynAppServices must not call ProductLookup Local Host composition.");
        }

        RequireContains(
            programText,
            "SalrynAppLaunchModeResolver.CreateServices(args)",
            "Program must still create app services through the app launch resolver and composition root.");

        RequireContains(
            programText,
            "services.DatabaseInitialization.InitializeDatabase();",
            "Program must still initialize the local database through app services.");

        RequireContains(
            shellText,
            "private readonly SalrynAppServices _services;",
            "Shell must still receive the app composition root instead of constructing dependencies directly.");

        RequireContains(
            shellText,
            "_services.ProductReadRepository",
            "Product management must still receive product repositories from the composition root.");

        RequireContains(
            shellText,
            "_services.CheckoutProductSearch",
            "Checkout must still receive product search through the composition root.");

        RequireContains(
            shellText,
            "_services.PosCheckoutApplication",
            "Checkout completion must still receive application service through the composition root.");

        AssertNoInfrastructureOutsideComposition(repoRoot);
        AssertNoLocalHostOutsideComposition(repoRoot);
    }

    // Kirch: Keeps text-based planning checks stable regardless of the current process directory.
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

    // Kirch: Reads a required source file from the repository root.
    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    // Kirch: Fails when a required planning marker is missing.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Fails when a forbidden planning marker appears too early.
    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Ensures WinForms feature code does not directly touch Infrastructure outside the composition root.
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

    // Kirch: Ensures Host.Local appears only in the WinForms composition root after the reference slice.
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
}

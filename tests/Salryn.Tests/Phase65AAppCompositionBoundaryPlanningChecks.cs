namespace Salryn.Tests;

// Kirch: Plans the app composition boundary before wiring Local Host into WinForms.
public static class Phase65AAppCompositionBoundaryPlanningChecks
{
    // Kirch: Verifies the current app composition root is ready for a future explicit Local Host wiring slice.
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

        RequireNotContains(
            appProjectText,
            "Salryn.Host.Local",
            "Phase 65A is planning-only and must not add the Host.Local project reference yet.");

        RequireContains(
            appServicesText,
            "public sealed class SalrynAppServices",
            "App composition root must remain SalrynAppServices.");

        RequireContains(
            appServicesText,
            "using Salryn.Infrastructure.Data;",
            "Only the app composition root should know Infrastructure data wiring.");

        RequireContains(
            appServicesText,
            "public IProductReadRepository ProductReadRepository { get; }",
            "App composition root must expose the Core product read repository for future Local Host composition.");

        RequireContains(
            appServicesText,
            "IProductReadRepository productReadRepository =",
            "App composition root must create the product read repository once.");

        RequireContains(
            appServicesText,
            "repositories.CreateProductReadRepository();",
            "App composition root must create ProductReadRepository through the repository factory.");

        RequireContains(
            appServicesText,
            "checkoutProductSearch =",
            "Existing checkout product search boundary must remain wired.");

        RequireContains(
            appServicesText,
            "new CheckoutProductRepositorySearchBoundary(productReadRepository)",
            "Checkout search must keep using the existing product repository boundary.");

        RequireNotContains(
            appServicesText,
            "LocalHostBootstrapComposer",
            "Phase 65A must not wire Local Host composition into app services yet.");

        RequireNotContains(
            appServicesText,
            "ComposeWithProductLookup",
            "Phase 65A must not call ProductLookup Local Host composition yet.");

        RequireContains(
            hostComposerText,
            "public LocalHostBootstrapCompositionResult ComposeWithProductLookup(",
            "Host.Local explicit ProductLookup composition path must exist before app wiring planning.");

        RequireContains(
            hostComposerText,
            "IProductReadRepository products",
            "Host.Local explicit ProductLookup composition must accept the Core product read repository contract.");

        RequireContains(
            hostComposerText,
            "_bootstrapper.RegisterDefaultHandlersWithProductLookup(",
            "Host.Local explicit ProductLookup composition must use the controlled bootstrap path.");

        RequireContains(
            programText,
            "SalrynAppServices services = SalrynAppServices.CreateProduction();",
            "Program must still create app services through the composition root.");

        RequireContains(
            programText,
            "services.DatabaseInitialization.InitializeDatabase();",
            "Program must still initialize the local database through app services.");

        RequireContains(
            programText,
            "new SalrynShellForm(",
            "Program must still pass composition services into the shell.");

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
}

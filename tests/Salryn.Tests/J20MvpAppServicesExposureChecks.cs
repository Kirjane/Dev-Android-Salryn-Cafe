namespace Salryn.Tests;

public static class J20MvpAppServicesExposureChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string repositoryFactoryText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "SqliteRepositoryFactory.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "J20_1_MVP_APP_SERVICES_EXPOSURE.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(repositoryFactoryText, "CreateSaleReadRepository()", "Repository factory must already expose sale read repository creation.");
        RequireContains(repositoryFactoryText, "CreateInventoryReadRepository()", "Repository factory must already expose inventory read repository creation.");
        RequireContains(repositoryFactoryText, "CreateInventoryWriteRepository()", "Repository factory must already expose inventory write repository creation.");
        RequireContains(repositoryFactoryText, "CreateStoreSettingsReadRepository()", "Repository factory must already expose store settings read repository creation.");

        RequireContains(appServicesText, "public ISaleReadRepository SaleReadRepository { get; }", "AppServices must expose sale read repository for Sales History MVP.");
        RequireContains(appServicesText, "public IInventoryReadRepository InventoryReadRepository { get; }", "AppServices must expose inventory read repository for Inventory/Dashboard MVP.");
        RequireContains(appServicesText, "public IInventoryWriteRepository InventoryWriteRepository { get; }", "AppServices must expose inventory write repository for stock adjustment MVP.");
        RequireContains(appServicesText, "public IStoreSettingsReadRepository StoreSettingsReadRepository { get; }", "AppServices must expose store settings read repository for Settings/Receipt MVP.");

        RequireContains(appServicesText, "ISaleReadRepository saleReadRepository", "AppServices constructor must receive sale read repository.");
        RequireContains(appServicesText, "IInventoryReadRepository inventoryReadRepository", "AppServices constructor must receive inventory read repository.");
        RequireContains(appServicesText, "IInventoryWriteRepository inventoryWriteRepository", "AppServices constructor must receive inventory write repository.");
        RequireContains(appServicesText, "IStoreSettingsReadRepository storeSettingsReadRepository", "AppServices constructor must receive store settings read repository.");

        RequireContains(appServicesText, "SaleReadRepository = saleReadRepository;", "AppServices must assign sale read repository.");
        RequireContains(appServicesText, "InventoryReadRepository = inventoryReadRepository;", "AppServices must assign inventory read repository.");
        RequireContains(appServicesText, "InventoryWriteRepository = inventoryWriteRepository;", "AppServices must assign inventory write repository.");
        RequireContains(appServicesText, "StoreSettingsReadRepository = storeSettingsReadRepository;", "AppServices must assign store settings read repository.");

        RequireContains(appServicesText, "repositories.CreateSaleReadRepository()", "AppServices factory must create sale read repository.");
        RequireContains(appServicesText, "repositories.CreateInventoryReadRepository()", "AppServices factory must create inventory read repository.");
        RequireContains(appServicesText, "repositories.CreateInventoryWriteRepository()", "AppServices factory must create inventory write repository.");
        RequireContains(appServicesText, "repositories.CreateStoreSettingsReadRepository()", "AppServices factory must create store settings read repository.");

        RequireContains(shellText, "OpenProducts()", "J20-1 must preserve existing Products shell route.");
        RequireContains(shellText, "OpenCheckout()", "J20-1 must preserve existing Checkout shell route.");
        RequireNotContains(shellText, "OpenInventory()", "J20-1 must not add UI shell routing yet.");
        RequireNotContains(shellText, "OpenSalesHistory()", "J20-1 must not add UI shell routing yet.");
        RequireNotContains(shellText, "OpenSettings()", "J20-1 must not add UI shell routing yet.");

        RequireContains(handoffText, "Status: June 20 MVP backend exposure only.", "J20-1 handoff must mark backend exposure only.");
        RequireContains(handoffText, "No UI screens were added in this phase.", "J20-1 handoff must preserve UI scope.");
        RequireContains(handoffText, "MVP screens must still consume backend through AppServices", "J20-1 handoff must document composition boundary.");

        RequireContains(coreSmokeText, "J20MvpAppServicesExposureChecks.Run();", "CoreSmoke must run J20 MVP AppServices exposure checks.");
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
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

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
}

namespace Salryn.Tests;

public static class ManualM1FirstRunDevSeedRepairChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());
        string apiRoot = Path.Combine(root, "src", "Salryn.Api.Local");
        string infrastructureRoot = Path.Combine(root, "src", "Salryn.Infrastructure");
        string webRoot = Path.Combine(root, "src", "Salryn.App.Web", "src");
        string devToolRoot = Path.Combine(root, "tools", "Salryn.Tools.DevData");

        string bootstrapper = ReadRequiredFile(Path.Combine(infrastructureRoot, "Data", "Bootstrap", "FirstRunBootstrapper.cs"));
        string orchestrator = ReadRequiredFile(Path.Combine(infrastructureRoot, "Data", "Initialization", "DatabaseInitializationOrchestrator.cs"));
        string bootstrapEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalBootstrapEndpoints.cs"));
        string factory = ReadRequiredFile(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));
        string bootstrapApi = ReadRequiredFile(Path.Combine(webRoot, "api", "bootstrap.ts"));
        string bootstrapPage = ReadRequiredFile(Path.Combine(webRoot, "pages", "BootstrapSetupPage.tsx"));
        string devToolProject = ReadRequiredFile(Path.Combine(devToolRoot, "Salryn.Tools.DevData.csproj"));
        string devTool = ReadRequiredFile(Path.Combine(devToolRoot, "Program.cs"));

        AssertContains(bootstrapper, "CreateFirstAdmin", "Existing FirstRunBootstrapper must create the first admin.");
        AssertContains(bootstrapper, "Bootstrap.AlreadyCompleted", "FirstRunBootstrapper must fail closed after first-run is completed.");
        AssertContains(bootstrapper, "Admin password must be at least 8 characters", "FirstRunBootstrapper must validate password length.");
        AssertContains(orchestrator, "CreateFirstAdmin(AdminBootstrapRequest request)", "DatabaseInitializationOrchestrator must expose first-admin creation without duplicating hasher logic.");

        AssertContains(bootstrapEndpoints, "/api/bootstrap/status", "Bootstrap status endpoint must exist.");
        AssertContains(bootstrapEndpoints, "/api/bootstrap/first-admin", "First admin endpoint must exist.");
        AssertContains(bootstrapEndpoints, "LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", "Bootstrap endpoints must remain local-loopback only.");
        AssertContains(bootstrapEndpoints, "composition.DatabaseInitialization.CreateFirstAdmin", "Endpoint must reuse existing first-run bootstrapper path.");
        AssertContains(factory, "app.MapLocalBootstrapEndpoints(composition)", "Application factory must map bootstrap endpoints.");

        AssertContains(bootstrapApi, "fetchBootstrapStatus", "Frontend must fetch first-run status.");
        AssertContains(bootstrapApi, "createFirstAdmin", "Frontend must call first-admin endpoint.");
        AssertContains(app, "BootstrapSetupPage", "App must route first-run DBs to setup page.");
        AssertContains(app, "firstRunRequired", "App must branch on first-run required status.");
        AssertContains(app, "window.history.replaceState(null, \"\", \"/bootstrap\")", "Fresh DB should land on /bootstrap route.");
        AssertContains(bootstrapPage, "Create admin and complete setup", "Bootstrap page must expose completion button.");
        AssertContains(bootstrapPage, "Confirm password", "Bootstrap page must require password confirmation.");
        AssertContains(bootstrapPage, "Receipt width", "Bootstrap page must collect receipt width.");

        AssertContains(devToolProject, "Salryn.Tools.DevData", "Dev data tool project must exist.");
        AssertContains(devTool, "reset-db", "Dev tool must include reset-db command.");
        AssertContains(devTool, "--confirm-dev-reset", "Dev reset must require confirmation flag.");
        AssertContains(devTool, "manual-backups", "Dev reset must back up before reset.");
        AssertContains(devTool, ".salryn-dev", "Dev tool must target .salryn-dev only.");
        AssertContains(devTool, "ProgramData", "Dev tool must refuse ProgramData production paths.");
        AssertContains(devTool, "seed-general-store", "Dev tool must include seed-general-store command.");
        AssertContains(devTool, "--confirm-dev-seed", "Dev seed must require confirmation flag.");
        AssertContains(devTool, "Complete first-run admin setup before seeding dev products.", "Dev seed must fail before first-run setup is complete.");
        AssertContains(devTool, "RequiredCount = 1000", "Dev seed must target exactly 1000 products.");
        AssertContains(devTool, "Unique SKUs", "Dev seed must report unique SKU count.");
        AssertContains(devTool, "Unique names", "Dev seed must report unique name count.");
        AssertContains(devTool, "Unique barcodes", "Dev seed must report unique barcode count.");
        AssertContains(devTool, "Duplicate", "Dev seed must reject duplicates.");
        AssertContains(devTool, "CostPriceCents >= p.SellingPriceCents", "Dev seed must enforce cost below selling price.");
        AssertContains(devTool, "GI wire", "Dev seed must contain realistic hardware items.");
        AssertContains(devTool, "PVC elbow", "Dev seed must contain realistic plumbing items.");
        AssertContains(devTool, "Sardines", "Dev seed must contain realistic general merchandise items.");
        AssertContains(devTool, "No product images", "Dev seed must explicitly avoid product images.");
        AssertNotContains(devTool, "image_url", "Dev seed must not add image_url fields.");
        AssertNotContains(devTool, "imagePath", "Dev seed must not add imagePath fields.");
        AssertNotContains(devTool, "media_id", "Dev seed must not add media identifier fields.");
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file not found: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

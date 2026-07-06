namespace Salryn.Tests;

public static class LanM9M13RemoteReadOnlyLanUsabilityChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string webRoot = Path.Combine(root.FullName, "src", "Salryn.App.Web");
        string apiRoot = Path.Combine(root.FullName, "src", "Salryn.Api.Local");

        string packageJson = Read(webRoot, "package.json");
        string app = Read(webRoot, "src", "App.tsx");
        string nav = Read(webRoot, "src", "navigation.ts");
        string landing = Read(webRoot, "src", "pages", "RemoteClientLandingPage.tsx");
        string page = Read(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string webApi = Read(webRoot, "src", "api", "remoteReadOnlyClient.ts");
        string hook = Read(webRoot, "src", "hooks", "useRemoteReadOnlyClient.ts");
        string localProgram = Read(apiRoot, "Program.cs")
            + "\n"
            + Read(apiRoot, "LocalApiApplicationFactory.cs");
        string endpoints = Read(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string service = Read(apiRoot, "Services", "RemoteReadOnlyApiService.cs");
        string contracts = Read(apiRoot, "Contracts", "RemoteReadOnlyApiContracts.cs");

        Require(packageJson.Contains("react", StringComparison.OrdinalIgnoreCase), "React/WebView must remain the operator target.");
        Require(packageJson.Contains("vite", StringComparison.OrdinalIgnoreCase), "Vite frontend marker missing.");
        Require(!nav.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Host navigation must not include Remote Read-Only Client.");
        Require(!nav.Contains("remote-readonly", StringComparison.Ordinal), "Host navigation must not route to remote-readonly.");
        Require(landing.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Remote client landing must include Remote Read-Only Client.");
        Require(app.Contains("type RemoteClientMode = \"landing\" | \"remote-pos\" | \"remote-readonly\"", StringComparison.Ordinal), "Remote read-only must be a remote client mode.");
        Require(app.Contains("RemoteReadOnlyClientPage", StringComparison.Ordinal), "Remote read-only page not rendered.");
        Require(File.Exists(Path.Combine(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx")), "Remote page must live under App.Web.");
        Require(!Directory.Exists(Path.Combine(root.FullName, "src", "Salryn.App.WinForms", "Features", "RemoteClient")), "WinForms RemoteClient feature path must not exist.");

        Require(localProgram.Contains("http://127.0.0.1:5299", StringComparison.Ordinal), "Api.Local must remain bound to localhost by default.");
        Require(!localProgram.Contains("0.0.0.0", StringComparison.Ordinal), "Api.Local must not bind to all interfaces.");
        Require(!localProgram.Contains("http://192.", StringComparison.OrdinalIgnoreCase), "Api.Local must not use a default LAN IP bind.");
        Require(localProgram.Contains("MapRemoteReadOnlyEndpoints", StringComparison.Ordinal), "Remote-readonly endpoints must be mapped.");

        RequireRemoteReadonlyEndpointShape(endpoints);
        RequireNoEndpointDisclosure(endpoints, "RemoteReadOnlyApiEndpoints.cs");
        RequireNoEndpointDisclosure(contracts, "RemoteReadOnlyApiContracts.cs");
        RequireNoEndpointDisclosure(service, "RemoteReadOnlyApiService.cs");
        Require(!endpoints.Contains("Salryn.App.WinForms", StringComparison.Ordinal), "Remote-readonly endpoints must not reference App.WinForms.");
        Require(!service.Contains("Salryn.App.WinForms", StringComparison.Ordinal), "Remote-readonly service must not reference App.WinForms.");
        RequireNoRemoteReadonlyServiceMutations(service);
        RequireNoMojibakeSeparators(page, "RemoteReadOnlyClientPage.tsx");
        RequireNoMojibakeSeparators(webApi, "remoteReadOnlyClient.ts");
        RequireNoMojibakeSeparators(hook, "useRemoteReadOnlyClient.ts");

        Require(service.Contains("LocalOnlyReadMode", StringComparison.Ordinal), "Service must expose LocalOnlyReadMode when live LAN plumbing is deferred.");
        Require(service.Contains("DisabledByDefault", StringComparison.Ordinal), "Bind policy must show disabled-by-default state.");
        Require(service.Contains("LocalhostOnly", StringComparison.Ordinal), "Bind policy must show localhost-only state.");
        Require(service.Contains("ExplicitHostBindRequired", StringComparison.Ordinal), "Bind policy must show explicit bind requirement.");
        Require(service.Contains("SpecificIpOnly", StringComparison.Ordinal), "Bind policy must show specific-IP-only state.");
        Require(service.Contains("NoDiscovery", StringComparison.Ordinal), "Bind policy must show no-discovery state.");
        Require(service.Contains("SearchProductsAsync", StringComparison.Ordinal), "Product browse must use read service path.");
        Require(service.Contains("GetAuditVisibility", StringComparison.Ordinal), "Audit visibility surface missing.");
        Require(contracts.Contains("RemoteReadOnlyAuditVisibilityResponse", StringComparison.Ordinal), "Audit visibility contract missing.");

        Require(page.Contains("Pairing / Session State", StringComparison.Ordinal), "Pairing/session state card missing.");
        Require(page.Contains("Bind Policy / Readiness", StringComparison.Ordinal), "Bind policy/readiness card missing.");
        Require(page.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Product browse read-only surface missing.");
        Require(page.Contains("Remote Operator Audit Visibility", StringComparison.Ordinal), "Audit visibility surface missing.");
        Require(page.Contains("Remote read-only client only", StringComparison.Ordinal) || webApi.Contains("Remote read-only client only", StringComparison.Ordinal), "Read-only warning missing.");
        Require(webApi.Contains("Clients must not open the SQLite database directly.", StringComparison.Ordinal), "SQLite warning missing.");
        Require(webApi.Contains("official journal", StringComparison.Ordinal), "Official journal disabled warning missing.");
        Require(webApi.Contains("official sequence", StringComparison.Ordinal), "Official sequence disabled warning missing.");
        Require(webApi.Contains("fetchRemoteReadOnlyStatus", StringComparison.Ordinal), "Status API bridge missing.");
        Require(webApi.Contains("fetchRemoteReadOnlyProducts", StringComparison.Ordinal), "Product API bridge missing.");
        Require(webApi.Contains("fetchRemoteReadOnlyAuditVisibility", StringComparison.Ordinal), "Audit visibility API bridge missing.");
        Require(hook.Contains("statusQuery", StringComparison.Ordinal), "Session/status hook missing.");
        Require(hook.Contains("bindPolicyQuery", StringComparison.Ordinal), "Bind policy hook missing.");
        Require(hook.Contains("auditQuery", StringComparison.Ordinal), "Audit visibility hook missing.");

        RequireNoForbiddenWebSource(webRoot);
        RequireNoRemoteReadonlyMutations(apiRoot);

        LanM5ControlledRemoteTransportGateChecks.Run();
        LanM6PairingActivationRemoteHandshakeChecks.Run();
        LanM7SafeRemoteReadOnlyClientChecks.Run();
        LanM8ReactRemoteReadOnlyShellRuntimeChecks.Run();
    }

    private static void RequireRemoteReadonlyEndpointShape(string endpoints)
    {
        string[] getRoutes =
        {
            "/status",
            "/capabilities",
            "/dashboard-summary",
            "/products",
            "/terminal-status",
            "/bind-policy",
            "/audit"
        };

        Require(endpoints.Contains("app.MapGroup(\"/api/remote-readonly\")", StringComparison.Ordinal), "Remote-readonly endpoint group missing.");

        foreach (string route in getRoutes)
        {
            Require(endpoints.Contains("group.MapGet(\"" + route + "\"", StringComparison.Ordinal), "Remote-readonly route must be GET-only: " + route);
        }

        Require(!endpoints.Contains("MapPost", StringComparison.Ordinal), "Remote-readonly endpoints must not map POST routes.");
        Require(!endpoints.Contains("MapPut", StringComparison.Ordinal), "Remote-readonly endpoints must not map PUT routes.");
        Require(!endpoints.Contains("MapPatch", StringComparison.Ordinal), "Remote-readonly endpoints must not map PATCH routes.");
        Require(!endpoints.Contains("MapDelete", StringComparison.Ordinal), "Remote-readonly endpoints must not map DELETE routes.");
        Require(endpoints.Contains("LocalApiPermissionGuards", StringComparison.Ordinal), "Remote-readonly endpoints must use the shared local API permission guard.");
        Require(CountOccurrences(endpoints, "LocalApiPermissionGuards.") >= getRoutes.Length, "Every remote-readonly route must be covered by the shared local API permission guard.");
        Require(endpoints.Contains("LocalApiPermissions.ProductsView", StringComparison.Ordinal), "Remote product reads must require Products.View.");
        Require(endpoints.Contains("LocalApiPermissions.StoreTerminalView", StringComparison.Ordinal), "Remote terminal status reads must require StoreTerminal.View.");
        Require(endpoints.Contains("LocalApiPermissions.AuditView", StringComparison.Ordinal), "Remote audit visibility reads must require Audit.View.");
        Require(CountOccurrences(endpoints, "Results.Ok") >= getRoutes.Length - 1, "Remote-readonly endpoints should only return read responses.");
    }

    private static void RequireNoEndpointDisclosure(string text, string label)
    {
        Require(!text.Contains("Microsoft.Data.Sqlite", StringComparison.Ordinal), label + " must not reference SQLite package.");
        Require(!text.Contains("SqliteConnection", StringComparison.Ordinal), label + " must not open SQLite.");
        Require(!text.Contains("DbPaths", StringComparison.Ordinal), label + " must not expose DB paths.");
        Require(!text.Contains("DatabasePath", StringComparison.Ordinal), label + " must not expose database path.");
        Require(!text.Contains("Data Source", StringComparison.OrdinalIgnoreCase), label + " must not expose connection strings.");
        Require(!text.Contains(".db", StringComparison.OrdinalIgnoreCase), label + " must not expose database files.");
        Require(!text.Contains("shared database", StringComparison.OrdinalIgnoreCase), label + " must not describe shared database use.");
        Require(!text.Contains("database file", StringComparison.OrdinalIgnoreCase), label + " must not expose database file language.");
    }

    private static void RequireNoRemoteReadonlyServiceMutations(string service)
    {
        string[] forbiddenServiceMarkers =
        {
            "OfficialDocument",
            "OfficialSequence",
            "AllocateSequence",
            "AppendJournal",
            "JournalAppend",
            "CompleteSaleAsync",
            "CheckoutApplication",
            "ProductWrite",
            "InventoryWrite",
            "StockAdjustment",
            "UserWrite",
            "RoleWrite",
            "BackupRecovery",
            "RestoreBackup",
            "CreateProduct",
            "UpdateProduct",
            "DeactivateProduct"
        };

        foreach (string marker in forbiddenServiceMarkers)
        {
            Require(!service.Contains(marker, StringComparison.Ordinal), "Remote-readonly API service must not reference mutation path: " + marker);
        }
    }

    private static void RequireNoMojibakeSeparators(string text, string label)
    {
        string mojibakeSeparator = "\u00C2" + "\u00B7";
        string middleDot = "\u00B7";
        Require(!text.Contains(mojibakeSeparator, StringComparison.Ordinal), label + " must not contain mojibake separator.");
        Require(!text.Contains(middleDot, StringComparison.Ordinal), label + " must not contain middle-dot separator.");
    }

    private static void RequireNoForbiddenWebSource(string webRoot)
    {
        foreach (string file in Directory.EnumerateFiles(webRoot, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains("node_modules", StringComparison.OrdinalIgnoreCase) || file.Contains("dist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(webRoot, file);
            Require(!text.Contains("Salryn.Host.Local", StringComparison.Ordinal), "App.Web must not reference Host.Local: " + relative);
            Require(!text.Contains("DbPaths", StringComparison.Ordinal), "App.Web must not reference DB paths: " + relative);
            Require(!text.Contains("SqliteConnection", StringComparison.Ordinal), "App.Web must not open SQLite: " + relative);
            Require(!text.Contains("Open SQLite path", StringComparison.OrdinalIgnoreCase), "App.Web must not expose SQLite path instructions: " + relative);
            Require(!text.Contains("Share database folder", StringComparison.OrdinalIgnoreCase), "App.Web must not expose database sharing instructions: " + relative);
            Require(!text.Contains("QR " + "pairing", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce QR pairing: " + relative);
            Require(!text.Contains("network " + "discovery", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce network discovery: " + relative);
            Require(!text.Contains("mobile" + "/cloud", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce mobile/cloud endpoint: " + relative);
        }
    }

    private static void RequireNoRemoteReadonlyMutations(string apiRoot)
    {
        foreach (string file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Require(!text.Contains("remote-readonly/check" + "out", StringComparison.OrdinalIgnoreCase), "Remote-readonly checkout endpoint must not exist.");
            Require(!text.Contains("remote-readonly/admin", StringComparison.OrdinalIgnoreCase), "Remote-readonly admin endpoint must not exist.");
            Require(!text.Contains("remote-readonly/inventory", StringComparison.OrdinalIgnoreCase), "Remote-readonly inventory endpoint must not exist.");
            Require(!text.Contains("remote-readonly/sales", StringComparison.OrdinalIgnoreCase), "Remote-readonly sales endpoint must not exist.");
            Require(!text.Contains("remote-readonly/backup", StringComparison.OrdinalIgnoreCase), "Remote-readonly backup endpoint must not exist.");
            Require(!text.Contains("remote-readonly/products/create", StringComparison.OrdinalIgnoreCase), "Remote product mutation endpoint must not exist.");
            Require(!text.Contains("remote-readonly/audit/delete", StringComparison.OrdinalIgnoreCase), "Remote audit mutation endpoint must not exist.");
        }
    }

    private static int CountOccurrences(string text, string marker)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    private static string Read(string root, params string[] parts)
    {
        string path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Require(File.Exists(path), "Expected file does not exist: " + path);
        return File.ReadAllText(path);
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

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

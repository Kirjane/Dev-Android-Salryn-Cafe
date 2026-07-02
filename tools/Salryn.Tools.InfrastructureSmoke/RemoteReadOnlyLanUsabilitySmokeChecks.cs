internal static class RemoteReadOnlyLanUsabilitySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== REMOTE READ-ONLY LAN USABILITY SMOKE CHECKS =====");

        string root = FindProjectRoot(projectRoot);
        string webRoot = Path.Combine(root, "src", "Salryn.App.Web");
        string apiRoot = Path.Combine(root, "src", "Salryn.Api.Local");

        string page = Read(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string webApi = Read(webRoot, "src", "api", "remoteReadOnlyClient.ts");
        string hook = Read(webRoot, "src", "hooks", "useRemoteReadOnlyClient.ts");
        string localProgram = Read(apiRoot, "Program.cs");
        string endpoints = Read(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string service = Read(apiRoot, "Services", "RemoteReadOnlyApiService.cs");
        string contracts = Read(apiRoot, "Contracts", "RemoteReadOnlyApiContracts.cs");

        Require(page.Contains("Pairing / Session State", StringComparison.Ordinal), "Pairing/session UX state missing.");
        Require(page.Contains("Bind Policy / Readiness", StringComparison.Ordinal), "Bind policy/readiness display missing.");
        Require(page.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Product browse surface missing.");
        Require(page.Contains("Remote Operator Audit Visibility", StringComparison.Ordinal), "Audit visibility surface missing.");
        Require(webApi.Contains("fetchRemoteReadOnlyStatus", StringComparison.Ordinal), "Remote status bridge missing.");
        Require(webApi.Contains("fetchRemoteReadOnlyProducts", StringComparison.Ordinal), "Remote product browse bridge missing.");
        Require(webApi.Contains("fetchRemoteReadOnlyAuditVisibility", StringComparison.Ordinal), "Remote audit visibility bridge missing.");
        Require(hook.Contains("statusQuery", StringComparison.Ordinal), "Status hook missing.");
        Require(hook.Contains("bindPolicyQuery", StringComparison.Ordinal), "Bind policy hook missing.");
        Require(hook.Contains("auditQuery", StringComparison.Ordinal), "Audit visibility hook missing.");
        Require(localProgram.Contains("UseUrls(\"http://127.0.0.1:5299\")", StringComparison.Ordinal), "Api.Local must remain localhost-only.");
        Require(localProgram.Contains("MapRemoteReadOnlyEndpoints", StringComparison.Ordinal), "Remote-readonly endpoint map missing.");
        Require(!localProgram.Contains("0.0.0.0", StringComparison.Ordinal), "All-interface bind must not exist.");
        RequireRemoteReadonlyEndpointShape(endpoints);
        RequireNoEndpointDisclosure(endpoints, "RemoteReadOnlyApiEndpoints.cs");
        RequireNoEndpointDisclosure(service, "RemoteReadOnlyApiService.cs");
        Require(!endpoints.Contains("Salryn.App.WinForms", StringComparison.Ordinal), "Remote-readonly endpoints must not reference App.WinForms.");
        Require(!service.Contains("Salryn.App.WinForms", StringComparison.Ordinal), "Remote-readonly service must not reference App.WinForms.");
        RequireNoRemoteReadonlyServiceMutations(service);
        RequireNoMojibakeSeparators(page, "RemoteReadOnlyClientPage.tsx");
        RequireNoMojibakeSeparators(webApi, "remoteReadOnlyClient.ts");
        RequireNoMojibakeSeparators(hook, "useRemoteReadOnlyClient.ts");
        Require(service.Contains("LocalOnlyReadMode", StringComparison.Ordinal), "LocalOnlyReadMode status missing.");
        Require(service.Contains("DisabledByDefault", StringComparison.Ordinal), "Disabled-by-default bind policy missing.");
        Require(service.Contains("LocalhostOnly", StringComparison.Ordinal), "Localhost-only bind policy missing.");
        Require(service.Contains("ExplicitHostBindRequired", StringComparison.Ordinal), "Explicit host bind policy missing.");
        Require(service.Contains("SpecificIpOnly", StringComparison.Ordinal), "Specific-IP bind policy missing.");
        Require(service.Contains("NoDiscovery", StringComparison.Ordinal), "No-discovery bind policy missing.");
        Require(contracts.Contains("RemoteReadOnlyAuditVisibilityResponse", StringComparison.Ordinal), "Audit visibility contract missing.");
        Require(!Directory.Exists(Path.Combine(root, "src", "Salryn.App.WinForms", "Features", "RemoteClient")), "WinForms RemoteClient feature path must not exist.");
        RemoteReadOnlyWebShellSmokeChecks.Run(root);

        Console.WriteLine("Remote read-only LAN usability smoke checks succeeded.");
        Console.WriteLine();
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
        string[] markers =
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

        foreach (string marker in markers)
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

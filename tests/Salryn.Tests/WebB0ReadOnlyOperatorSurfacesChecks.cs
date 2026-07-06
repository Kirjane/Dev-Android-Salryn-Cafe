using System.Text.RegularExpressions;

namespace Salryn.Tests;

public static class WebB0ReadOnlyOperatorSurfacesChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");
        string apiProgram = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Program.cs")
            + "\n"
            + ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string readOnlyEndpoints = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string compositionText = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Composition.Local", "SalrynLocalComposition.cs");
        string coreSmokeText = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        RequireContains(coreSmokeText, "WebB0ReadOnlyOperatorSurfacesChecks.Run();", "CoreSmoke must run WEB-B0 checks.");

        // API read endpoints exist and require session.
        string[] requiredGetEndpoints =
        {
            "/api/products",
            "/api/inventory/low-stock",
            "/api/inventory/movements",
            "/api/sales/history",
            "/api/users",
            "/api/audit/logs",
            "/api/audit/errors",
        };

        foreach (string endpoint in requiredGetEndpoints)
        {
            RequireContains(readOnlyEndpoints, endpoint, $"API must expose GET {endpoint}.");
        }

        RequireContains(readOnlyEndpoints, "LocalApiPermissionGuards.RequirePermission", "Read endpoints must require backend permission checks.");
        RequireContains(compositionText, "IUserReadRepository", "Composition must wire user read repository.");
        RequireContains(compositionText, "IAuditReadRepository", "Composition must wire audit read repository.");
        RequireContains(compositionText, "IErrorLogReadRepository", "Composition must wire error log read repository.");
        RequireContains(compositionText, "ICatalogReadRepository", "Composition must wire catalog read repository.");

        RequireNotContains(readOnlyEndpoints, "MapPost(\"/api/products", "Products must not expose POST.");
        RequireNotContains(readOnlyEndpoints, "MapPut(", "Read-only API must not expose PUT.");
        RequireNotContains(readOnlyEndpoints, "MapPatch(", "Read-only API must not expose PATCH.");
        RequireNotContains(readOnlyEndpoints, "MapDelete(", "Read-only API must not expose DELETE.");

        // React pages and GET clients.
        RequireFileExists(webSrc, "pages", "ProductsPage.tsx");
        RequireFileExists(webSrc, "pages", "InventoryPage.tsx");
        RequireFileExists(webSrc, "pages", "SalesHistoryPage.tsx");
        RequireFileExists(webSrc, "pages", "UsersPage.tsx");
        RequireFileExists(webSrc, "pages", "AuditPage.tsx");

        string productsApi = ReadRequiredFile(webSrc, "api", "products.ts");
        string inventoryApi = ReadRequiredFile(webSrc, "api", "inventory.ts");
        string salesApi = ReadRequiredFile(webSrc, "api", "sales.ts");
        string usersApi = ReadRequiredFile(webSrc, "api", "users.ts");
        string auditApi = ReadRequiredFile(webSrc, "api", "audit.ts");
        string appShell = ReadRequiredFile(webSrc, "components", "AppShell.tsx");
        string navigation = ReadRequiredFile(webSrc, "navigation.ts");

        RequireContains(productsApi, "/api/products", "Products client must call GET /api/products.");
        RequireContains(inventoryApi, "/api/inventory/low-stock", "Inventory client must call low-stock endpoint.");
        RequireContains(inventoryApi, "/api/inventory/movements", "Inventory client must call movements endpoint.");
        RequireContains(salesApi, "/api/sales/history", "Sales client must call history endpoint.");
        RequireContains(usersApi, "/api/users", "Users client must call GET /api/users.");
        RequireContains(auditApi, "/api/audit/logs", "Audit client must call audit logs endpoint.");
        RequireContains(auditApi, "/api/audit/errors", "Audit client must call error logs endpoint.");

        RequireContains(navigation, "Products", "Navigation must enable Products.");
        RequireContains(navigation, "Inventory", "Navigation must enable Inventory.");
        RequireContains(navigation, "Sales History", "Navigation must enable Sales History.");
        RequireContains(navigation, "Users", "Navigation must enable Users.");
        RequireContains(navigation, "Audit", "Navigation must enable Audit.");
        RequireContains(appShell, "onNavigate", "AppShell must support page navigation.");

        string usersPage = ReadRequiredFile(webSrc, "pages", "UsersPage.tsx");
        string readOnlyModuleSource = string.Join(
            "\n",
            productsApi,
            inventoryApi,
            salesApi,
            auditApi,
            ReadRequiredFile(webSrc, "pages", "ProductsPage.tsx"),
            ReadRequiredFile(webSrc, "pages", "InventoryPage.tsx"),
            ReadRequiredFile(webSrc, "pages", "SalesHistoryPage.tsx"),
            ReadRequiredFile(webSrc, "pages", "AuditPage.tsx"));
        string usersModuleSource = usersApi + "\n" + usersPage;

        RequireNotContains(readOnlyModuleSource, "method: \"POST\"", "Read-only module clients must not POST.");
        RequireNotContains(readOnlyModuleSource, "method: 'POST'", "Read-only module clients must not POST.");
        RequireNotContains(readOnlyModuleSource, "method: \"PUT\"", "Read-only module clients must not PUT.");
        RequireNotContains(readOnlyModuleSource, "method: \"PATCH\"", "Read-only module clients must not PATCH.");
        RequireNotContains(readOnlyModuleSource, "method: \"DELETE\"", "Read-only module clients must not DELETE.");
        RequireContains(usersApi, "method: \"POST\"", "Users client may POST only for USER-M1 host-local user creation.");
        RequireContains(usersApi, "/api/users", "Users create client must stay under /api/users.");
        RequireContains(usersPage, "Create user", "USER-M1 Users page must expose the host-local create form.");
        RequireNotContains(usersModuleSource, "method: \"PUT\"", "USER-M1 must not add broad users PUT.");
        RequireNotContains(usersModuleSource, "method: \"PATCH\"", "USER-M1 must not add broad users PATCH.");
        RequireNotContains(usersModuleSource, "method: \"DELETE\"", "USER-M1 must not add broad users DELETE.");

        string[] forbiddenMutations =
        {
            "Void Sale",
            "Complete Sale",
            "Adjust Stock",
            "Add Product",
            "Deactivate Product",
            "Reset Password",
            "checkout",
            "handoff",
            "NativeHandoff",
            "upload",
            "file-picker",
            "blob",
            "printer",
            "scanner",
            "photo",
        };

        foreach (string token in forbiddenMutations)
        {
            RequireNoForbiddenToken(readOnlyModuleSource, token, $"WEB-B0 must not add forbidden mutation: {token}");
        }

        // No forbidden module pages beyond read-only set.
        string[] forbiddenPagePrefixes =
        {
            "Checkout", "Backup", "Settings", "OfficialOutput", "Rmo",
        };
        string pagesDir = Path.Combine(webSrc, "pages");
        foreach (string pageFile in Directory.GetFiles(pagesDir, "*.tsx", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(pageFile);
            if (name.Equals("LoginPage.tsx", StringComparison.Ordinal) ||
                name.Equals("DashboardPage.tsx", StringComparison.Ordinal) ||
                name.Equals("ProductsPage.tsx", StringComparison.Ordinal) ||
                name.Equals("InventoryPage.tsx", StringComparison.Ordinal) ||
                name.Equals("SalesHistoryPage.tsx", StringComparison.Ordinal) ||
                name.Equals("UsersPage.tsx", StringComparison.Ordinal) ||
                name.Equals("AuditPage.tsx", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string prefix in forbiddenPagePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"WEB-B0 must not add forbidden page: {name}");
                }
            }
        }

        RequireNotContains(apiProgram, "handoff", "API must not reintroduce session handoff.");
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

    private static void RequireFileExists(string root, params string[] pathParts)
    {
        string path = root;
        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;
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

    private static void RequireNoForbiddenToken(string text, string token, string message)
    {
        string pattern = $@"\b{Regex.Escape(token)}\b";
        if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(message);
        }
    }
}

using System.Text.RegularExpressions;

namespace Salryn.Tests;

public static class SecA1LocalApiWebViewSecurityChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string coreSmokeText = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        RequireContains(coreSmokeText, "SecA1LocalApiWebViewSecurityChecks.Run();", "CoreSmoke must run SEC-A1 checks.");

        string apiProgram = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Program.cs")
            + "\n"
            + ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string readOnlyEndpoints = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string readOnlyContracts = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Contracts", "ReadOnlyContracts.cs");
        string authSessionContracts = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Contracts", "AuthSessionContracts.cs");
        string sessionService = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Auth", "LocalApiSessionService.cs");

        string webViewDir = Path.Combine(repoRoot.FullName, "src", "Salryn.Desktop.WebView");
        string devHostConfig = ReadRequiredFile(webViewDir, "DevHostConfig.cs");
        string localWebOriginPolicy = ReadRequiredFile(webViewDir, "LocalWebOriginPolicy.cs");
        string mainForm = ReadRequiredFile(webViewDir, "MainForm.cs");
        string webViewProgram = ReadRequiredFile(webViewDir, "Program.cs");
        string webViewSource = string.Join("\n", devHostConfig, localWebOriginPolicy, mainForm, webViewProgram);

        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");
        string sessionApi = ReadRequiredFile(webSrc, "api", "session.ts");
        string httpApi = ReadRequiredFile(webSrc, "api", "http.ts");
        string productsApi = ReadRequiredFile(webSrc, "api", "products.ts");
        string inventoryApi = ReadRequiredFile(webSrc, "api", "inventory.ts");
        string salesApi = ReadRequiredFile(webSrc, "api", "sales.ts");
        string usersApi = ReadRequiredFile(webSrc, "api", "users.ts");
        string auditApi = ReadRequiredFile(webSrc, "api", "audit.ts");
        string readOnlyModuleApi = string.Join("\n", productsApi, inventoryApi, salesApi, auditApi);

        string gitignore = ReadRequiredFile(repoRoot.FullName, ".gitignore");

        // API localhost/CORS posture.
        RequireContains(apiProgram, "127.0.0.1:5299", "API must bind to localhost only.");
        RequireContains(apiProgram, "WithOrigins(\"http://127.0.0.1:5173\", \"http://localhost:5173\")", "API CORS must stay restricted to local Vite origins.");
        RequireContains(apiProgram, "AllowCredentials", "API CORS must allow credentials for session cookies.");
        RequireNotContains(apiProgram, "AllowAnyOrigin", "API CORS must not use AllowAnyOrigin.");
        RequireNotContains(apiProgram, "ListenAnyIP", "API must not use ListenAnyIP.");
        RequireNotContains(apiProgram, "0.0.0.0", "API must not bind to 0.0.0.0.");

        // Dev DB path diagnostic stays console-only.
        RequireContains(apiProgram, "IsDevelopmentRequested", "Dev DB path diagnostic must remain dev-gated.");
        RequireNotContains(apiProgram, "MapGet(\"/api/database", "API must not expose database path through a public endpoint.");
        RequireNotContains(apiProgram, "Results.Ok(composition.DbPaths", "API must not return database paths in HTTP responses.");

        // Sensitive leakage guards.
        RequireNotContains(readOnlyContracts, "TechnicalDetail", "Error log DTOs must not expose technical detail.");
        RequireNotContains(authSessionContracts, "PasswordHash", "Session contracts must not expose password hashes.");
        RequireNotContains(sessionService, "PasswordHash", "Session service must not expose password hashes.");

        // WebView URL allowlist and navigation blocking.
        RequireContains(localWebOriginPolicy, "TryResolveConfiguredWebUrl", "WebView must validate SALRYN_WEB_URL before use.");
        RequireContains(localWebOriginPolicy, "http://127.0.0.1:5173", "WebView allowlist must include local Vite origin.");
        RequireContains(localWebOriginPolicy, "http://127.0.0.1:5299", "WebView allowlist must include local API static origin.");
        RequireContains(devHostConfig, "ResolveWebUrl(out string? configurationRejection)", "DevHostConfig must surface rejected SALRYN_WEB_URL.");
        RequireContains(mainForm, "NavigationStarting", "WebView must block non-local navigation.");
        RequireContains(mainForm, "NewWindowRequested", "WebView must block external new-window navigation.");
        RequireContains(webViewProgram, "configurationRejection", "WebView host must warn when SALRYN_WEB_URL is rejected.");

        RequireNotContains(webViewSource, "AllowAnyOrigin", "WebView host must not weaken CORS.");
        RequireNotContains(webViewSource, "0.0.0.0", "WebView host must not target 0.0.0.0.");

        foreach (string blockedScheme in new[] { "file://", "data:", "javascript:" })
        {
            RequireNotContains(webViewSource, blockedScheme, $"WebView host must not allow {blockedScheme} navigation.");
        }

        // Session client must send cookies.
        RequireContains(sessionApi, "credentials: \"include\"", "Web session client must send cookies.");

        // Read-only module clients remain GET-only. Users may POST only for USER-M1 host-local creation.
        RequireNotContains(readOnlyModuleApi, "method: \"POST\"", "Read-only module clients must not POST.");
        RequireNotContains(readOnlyModuleApi, "method: \"PUT\"", "Read-only module clients must not PUT.");
        RequireNotContains(readOnlyModuleApi, "method: \"PATCH\"", "Read-only module clients must not PATCH.");
        RequireNotContains(readOnlyModuleApi, "method: \"DELETE\"", "Read-only module clients must not DELETE.");
        RequireContains(usersApi, "method: \"POST\"", "Users client may POST only for USER-M1 host-local user creation.");
        RequireContains(usersApi, "/api/users", "USER-M1 user creation must stay under /api/users.");
        RequireNotContains(usersApi, "method: \"PUT\"", "USER-M1 must not add broad users PUT.");
        RequireNotContains(usersApi, "method: \"PATCH\"", "USER-M1 must not add broad users PATCH.");
        RequireNotContains(usersApi, "method: \"DELETE\"", "USER-M1 must not add broad users DELETE.");

        // No hardcoded diagnostic passwords in app source.
        foreach (string appRoot in new[]
        {
            Path.Combine(repoRoot.FullName, "src", "Salryn.Api.Local"),
            Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web"),
            Path.Combine(repoRoot.FullName, "src", "Salryn.Desktop.WebView"),
            Path.Combine(repoRoot.FullName, "src", "Salryn.Composition.Local"),
        })
        {
            string combined = ReadAllSource(appRoot, new[] { ".cs", ".ts", ".tsx" }, SearchOption.AllDirectories);
            RequireNoForbiddenToken(combined, "StrongPass123!", "App source must not hardcode diagnostic passwords.");
            RequireNoForbiddenToken(combined, "StrongAdmin123!", "App source must not hardcode seed passwords.");
        }

        // Local diagnostic log hygiene.
        RequireContains(gitignore, ".salryn-*.log", "Gitignore must ignore local Salryn diagnostic logs.");
        RequireContains(gitignore, ".salryn-*.log.err", "Gitignore must ignore local Salryn diagnostic log errors.");

        // Read-only API surface still guarded.
        RequireNotContains(readOnlyEndpoints, "MapPost(\"/api/products", "Products must not expose POST.");
        RequireNotContains(readOnlyEndpoints, "MapPut(", "Read-only API must not expose PUT.");
        RequireNotContains(readOnlyEndpoints, "MapPatch(", "Read-only API must not expose PATCH.");
        RequireNotContains(readOnlyEndpoints, "MapDelete(", "Read-only API must not expose DELETE.");
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

    private static string ReadAllSource(
        string root,
        string[] extensions,
        SearchOption searchOption)
    {
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        IEnumerable<string> files = Directory.EnumerateFiles(root, "*.*", searchOption)
            .Where(path => extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

        return string.Join("\n", files.Select(File.ReadAllText));
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
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

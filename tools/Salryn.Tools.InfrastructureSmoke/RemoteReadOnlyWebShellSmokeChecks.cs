internal static class RemoteReadOnlyWebShellSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== REMOTE READ-ONLY WEB SHELL SMOKE CHECKS =====");

        string root = FindProjectRoot(projectRoot);
        string webRoot = Path.Combine(root, "src", "Salryn.App.Web");
        string apiRoot = Path.Combine(root, "src", "Salryn.Api.Local");

        string packageJson = Read(webRoot, "package.json");
        string app = Read(webRoot, "src", "App.tsx");
        string nav = Read(webRoot, "src", "navigation.ts");
        string page = Read(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string source = Read(webRoot, "src", "api", "remoteReadOnlyClient.ts");
        string localApiProgram = Read(apiRoot, "Program.cs")
            + "\n"
            + Read(apiRoot, "LocalApiApplicationFactory.cs");

        Require(packageJson.Contains("react", StringComparison.OrdinalIgnoreCase), "React package marker missing.");
        Require(packageJson.Contains("vite", StringComparison.OrdinalIgnoreCase), "Vite package marker missing.");
        Require(nav.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Navigation entry missing.");
        Require(app.Contains("RemoteReadOnlyClientPage", StringComparison.Ordinal), "App page render missing.");
        Require(page.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Page header missing.");
        Require(page.Contains("Connection / Session Status", StringComparison.Ordinal) || page.Contains("Pairing / Session State", StringComparison.Ordinal), "Session status card missing.");
        Require(page.Contains("Product Lookup / Read-Only Area", StringComparison.Ordinal) || page.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Product read area missing.");
        Require(page.Contains("Dashboard / Status Area", StringComparison.Ordinal), "Dashboard read area missing.");
        Require(page.Contains("Terminal Status Area", StringComparison.Ordinal) || page.Contains("Terminal status", StringComparison.Ordinal), "Terminal status area missing.");
        Require(source.Contains("Remote read-only client only. Remote cashier/admin execution is not enabled.", StringComparison.Ordinal), "Read-only warning missing.");
        Require(source.Contains("Clients must not open the SQLite database directly.", StringComparison.Ordinal), "SQLite warning missing.");
        Require(source.Contains("official journal", StringComparison.Ordinal), "Official journal disabled text missing.");
        Require(source.Contains("official sequence", StringComparison.Ordinal), "Official sequence disabled text missing.");
        Require(source.Contains("Status read", StringComparison.Ordinal), "Status read capability missing.");
        Require(source.Contains("Product read", StringComparison.Ordinal), "Product read capability missing.");
        Require(source.Contains("Dashboard read", StringComparison.Ordinal), "Dashboard read capability missing.");
        Require(source.Contains("Terminal status read", StringComparison.Ordinal), "Terminal status read capability missing.");
        Require(!Directory.Exists(Path.Combine(root, "src", "Salryn.App.WinForms", "Features", "RemoteClient")), "WinForms RemoteClient UX path must not exist.");
        Require(localApiProgram.Contains("http://127.0.0.1:5299", StringComparison.Ordinal), "Api.Local must remain localhost-only.");
        Require(!localApiProgram.Contains("0.0.0.0", StringComparison.Ordinal), "Api.Local must not bind to all interfaces.");
        RequireNoForbiddenWebMarkers(webRoot);

        Console.WriteLine("Remote read-only web shell smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireNoForbiddenWebMarkers(string webRoot)
    {
        foreach (string file in Directory.EnumerateFiles(webRoot, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains("node_modules", StringComparison.OrdinalIgnoreCase) || file.Contains("dist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            Require(!text.Contains("Salryn.Host.Local", StringComparison.Ordinal), "App.Web must not reference Host.Local.");
            Require(!text.Contains("DbPaths", StringComparison.Ordinal), "App.Web must not reference database paths.");
            Require(!text.Contains("SqliteConnection", StringComparison.Ordinal), "App.Web must not open SQLite.");
            Require(!text.Contains("Open SQLite path", StringComparison.OrdinalIgnoreCase), "App.Web must not expose SQLite path instructions.");
            Require(!text.Contains("Share database folder", StringComparison.OrdinalIgnoreCase), "App.Web must not expose database sharing instructions.");
            Require(!text.Contains("QR " + "pairing", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce QR pairing.");
            Require(!text.Contains("network " + "discovery", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce network discovery.");
            Require(!text.Contains("mobile" + "/cloud", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce mobile/cloud endpoint.");
        }
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

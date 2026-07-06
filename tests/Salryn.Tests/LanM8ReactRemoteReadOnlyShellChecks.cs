namespace Salryn.Tests;

public static class LanM8ReactRemoteReadOnlyShellChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string packageJson = Read(root, "src", "Salryn.App.Web", "package.json");
        string app = Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        string nav = Read(root, "src", "Salryn.App.Web", "src", "navigation.ts");
        string landing = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemoteClientLandingPage.tsx");
        string page = Read(root, "src", "Salryn.App.Web", "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string api = Read(root, "src", "Salryn.App.Web", "src", "api", "remoteReadOnlyClient.ts");
        string hook = Read(root, "src", "Salryn.App.Web", "src", "hooks", "useRemoteReadOnlyClient.ts");
        string localApiProgram = Read(root, "src", "Salryn.Api.Local", "Program.cs")
            + "\n"
            + Read(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string m7Service = Read(root, "src", "Salryn.App.WinForms", "HostCommands", "RemoteReadOnlyClientService.cs");

        Require(packageJson.Contains("react", StringComparison.OrdinalIgnoreCase), "App.Web must keep React stack.");
        Require(packageJson.Contains("vite", StringComparison.OrdinalIgnoreCase), "App.Web must keep Vite stack.");
        Require(packageJson.Contains("tailwindcss", StringComparison.OrdinalIgnoreCase), "App.Web must keep Tailwind stack.");
        Require(packageJson.Contains("@tanstack/react-query", StringComparison.OrdinalIgnoreCase), "App.Web must keep TanStack Query stack.");
        Require(page.Contains("Remote Read-Only Client", StringComparison.Ordinal), "React page must label itself read-only.");
        Require(!nav.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Host navigation must not include the Remote Read-Only Client page.");
        Require(!nav.Contains("remote-readonly", StringComparison.Ordinal), "Host navigation must not route to remote-readonly.");
        Require(landing.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Remote client landing must include the read-only client option.");
        Require(app.Contains("type RemoteClientMode = \"landing\" | \"remote-pos\" | \"remote-readonly\"", StringComparison.Ordinal), "Remote read-only must be a remote client mode.");
        Require(app.Contains("RemoteReadOnlyClientPage", StringComparison.Ordinal), "App.tsx must keep the remote read-only page for RemoteClientApp.");
        Require(hook.Contains("useRemoteReadOnlyClient", StringComparison.Ordinal), "React hook must exist.");

        Require(api.Contains("Remote read-only client only. Remote cashier/admin execution is not enabled.", StringComparison.Ordinal), "Read-only warning must exist.");
        Require(api.Contains("Clients must not open the SQLite database directly.", StringComparison.Ordinal), "SQLite warning must exist.");
        Require(api.Contains("official journal", StringComparison.Ordinal), "Official journal disabled warning must exist.");
        Require(api.Contains("official sequence", StringComparison.Ordinal), "Official sequence disabled warning must exist.");
        Require(api.Contains("Status read", StringComparison.Ordinal), "Status read capability must exist.");
        Require(api.Contains("Product read", StringComparison.Ordinal), "Product read capability must exist.");
        Require(api.Contains("Dashboard read", StringComparison.Ordinal), "Dashboard read capability must exist.");
        Require(api.Contains("Terminal status read", StringComparison.Ordinal), "Terminal status read capability must exist.");
        Require(api.Contains("Check" + "out" + " disabled", StringComparison.Ordinal), "Checkout disabled text must exist.");
        Require(api.Contains("Sales" + " mutation disabled", StringComparison.Ordinal), "Sales disabled text must exist.");
        Require(api.Contains("Inventory" + " mutation disabled", StringComparison.Ordinal), "Inventory disabled text must exist.");
        Require(api.Contains("Admin" + " mutation disabled", StringComparison.Ordinal), "Admin disabled text must exist.");
        Require(api.Contains("Backup" + " mutation disabled", StringComparison.Ordinal), "Backup disabled text must exist.");

        Require(page.Contains("Product Lookup / Read-Only Area", StringComparison.Ordinal) || page.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Product lookup surface must exist.");
        Require(page.Contains("Dashboard / Status Area", StringComparison.Ordinal), "Dashboard/status surface must exist.");
        Require(page.Contains("Terminal Status Area", StringComparison.Ordinal), "Terminal status surface must exist.");
        Require(page.Contains("No catalog mutation controls are present", StringComparison.Ordinal), "Catalog mutation controls must remain absent.");
        Require(page.Contains("remoteReadOnlyWarnings", StringComparison.Ordinal), "Page must render read-only warnings.");
        Require(!Directory.Exists(Path.Combine(root.FullName, "src", "Salryn.App.WinForms", "Features", "RemoteClient")), "WinForms RemoteClient operator UX path must not exist.");
        Require(m7Service.Contains("IRemoteHostCommandTransport", StringComparison.Ordinal), "LAN-M7 service must keep Core transport abstraction.");
        Require(!m7Service.Contains("Salryn.Host.Local", StringComparison.Ordinal), "LAN-M7 service must not reference Host.Local.");
        Require(localApiProgram.Contains("http://127.0.0.1:5299", StringComparison.Ordinal), "Api.Local must remain localhost-only.");
        Require(!localApiProgram.Contains("0.0.0.0", StringComparison.Ordinal), "Api.Local must not bind to all interfaces.");

        RequireNoForbiddenWebMarkers(root);
        LanM5ControlledRemoteTransportGateChecks.Run();
        LanM6PairingActivationRemoteHandshakeChecks.Run();
        LanM7SafeRemoteReadOnlyClientChecks.Run();
    }

    private static void RequireNoForbiddenWebMarkers(DirectoryInfo root)
    {
        string appWebRoot = Path.Combine(root.FullName, "src", "Salryn.App.Web");
        foreach (string file in Directory.EnumerateFiles(appWebRoot, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains("node_modules", StringComparison.OrdinalIgnoreCase) || file.Contains("dist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(root.FullName, file);
            Require(!text.Contains("Salryn.Host.Local", StringComparison.Ordinal), "App.Web must not reference Host.Local: " + relative);
            Require(!text.Contains("DbPaths", StringComparison.Ordinal), "App.Web must not reference database paths: " + relative);
            Require(!text.Contains("SqliteConnection", StringComparison.Ordinal), "App.Web must not open SQLite: " + relative);
            Require(!text.Contains("Open SQLite path", StringComparison.OrdinalIgnoreCase), "App.Web must not instruct opening SQLite paths: " + relative);
            Require(!text.Contains("Share database folder", StringComparison.OrdinalIgnoreCase), "App.Web must not instruct sharing database folders: " + relative);
            Require(!text.Contains("QR " + "pairing", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce QR pairing: " + relative);
            Require(!text.Contains("network " + "discovery", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce network discovery: " + relative);
            Require(!text.Contains("mobile" + "/cloud", StringComparison.OrdinalIgnoreCase), "App.Web must not introduce mobile/cloud endpoint: " + relative);
        }
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = Path.Combine(new[] { root.FullName }.Concat(parts).ToArray());
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

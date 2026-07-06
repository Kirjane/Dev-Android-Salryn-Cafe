namespace Salryn.Tests;

public static class LanM8ReactRemoteReadOnlyShellRuntimeChecks
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string webRoot = Path.Combine(root.FullName, "src", "Salryn.App.Web");
        string apiRoot = Path.Combine(root.FullName, "src", "Salryn.Api.Local");

        string packageJson = File.ReadAllText(Path.Combine(webRoot, "package.json"));
        string app = File.ReadAllText(Path.Combine(webRoot, "src", "App.tsx"));
        string nav = File.ReadAllText(Path.Combine(webRoot, "src", "navigation.ts"));
        string landing = File.ReadAllText(Path.Combine(webRoot, "src", "pages", "RemoteClientLandingPage.tsx"));
        string page = File.ReadAllText(Path.Combine(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx"));
        string readOnlySource = File.ReadAllText(Path.Combine(webRoot, "src", "api", "remoteReadOnlyClient.ts"));
        string localApiProgram = File.ReadAllText(Path.Combine(apiRoot, "Program.cs"))
            + "\n"
            + File.ReadAllText(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string m7Service = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.WinForms", "HostCommands", "RemoteReadOnlyClientService.cs"));

        Require(packageJson.Contains("react", StringComparison.OrdinalIgnoreCase), "React stack missing.");
        Require(packageJson.Contains("vite", StringComparison.OrdinalIgnoreCase), "Vite stack missing.");
        Require(packageJson.Contains("@tanstack/react-query", StringComparison.OrdinalIgnoreCase), "TanStack Query stack missing.");
        Require(!nav.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Host navigation must not include Remote Read-Only Client.");
        Require(!nav.Contains("remote-readonly", StringComparison.Ordinal), "Host navigation must not route to remote-readonly.");
        Require(landing.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Remote client landing must include Remote Read-Only Client.");
        Require(app.Contains("type RemoteClientMode = \"landing\" | \"remote-pos\" | \"remote-readonly\"", StringComparison.Ordinal), "Remote read-only must be a remote client mode.");
        Require(app.Contains("RemoteReadOnlyClientPage", StringComparison.Ordinal), "App render entry missing.");
        Require(page.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Page title missing.");
        Require(page.Contains("Connection / Session Status", StringComparison.Ordinal) || page.Contains("Pairing / Session State", StringComparison.Ordinal), "Connection/session card missing.");
        Require(page.Contains("Terminal Identity", StringComparison.Ordinal), "Terminal identity card missing.");
        Require(page.Contains("Allowed Capabilities", StringComparison.Ordinal), "Capabilities card missing.");
        Require(page.Contains("Disabled Mutations", StringComparison.Ordinal), "Disabled mutation card missing.");
        Require(page.Contains("Product Lookup / Read-Only Area", StringComparison.Ordinal) || page.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Product read area missing.");
        Require(page.Contains("Dashboard / Status Area", StringComparison.Ordinal), "Dashboard/status area missing.");
        Require(page.Contains("Terminal Status Area", StringComparison.Ordinal) || page.Contains("Terminal status", StringComparison.Ordinal), "Terminal status area missing.");
        Require(readOnlySource.Contains("Remote read-only client only. Remote cashier/admin execution is not enabled.", StringComparison.Ordinal), "Read-only warning missing.");
        Require(readOnlySource.Contains("Clients must not open the SQLite database directly.", StringComparison.Ordinal), "SQLite warning missing.");
        Require(readOnlySource.Contains("Status read", StringComparison.Ordinal), "Status read missing.");
        Require(readOnlySource.Contains("Product read", StringComparison.Ordinal), "Product read missing.");
        Require(readOnlySource.Contains("Dashboard read", StringComparison.Ordinal), "Dashboard read missing.");
        Require(readOnlySource.Contains("Terminal status read", StringComparison.Ordinal), "Terminal status read missing.");
        Require(readOnlySource.Contains("checkout + \" disabled\"", StringComparison.Ordinal), "Checkout-disabled runtime text source missing.");
        Require(readOnlySource.Contains("sales + \" mutation disabled\"", StringComparison.Ordinal), "Sales-disabled runtime text source missing.");
        Require(readOnlySource.Contains("inventory + \" mutation disabled\"", StringComparison.Ordinal), "Inventory-disabled runtime text source missing.");
        Require(readOnlySource.Contains("admin + \" mutation disabled\"", StringComparison.Ordinal), "Admin-disabled runtime text source missing.");
        Require(readOnlySource.Contains("backup + \" mutation disabled\"", StringComparison.Ordinal), "Backup-disabled runtime text source missing.");

        Require(!Directory.Exists(Path.Combine(root.FullName, "src", "Salryn.App.WinForms", "Features", "RemoteClient")), "WinForms RemoteClient feature path must not exist.");
        Require(m7Service.Contains("IRemoteHostCommandTransport", StringComparison.Ordinal), "LAN-M7 transport abstraction missing.");
        Require(!m7Service.Contains("Salryn.Host.Local", StringComparison.Ordinal), "LAN-M7 service must not reference Host.Local.");
        Require(localApiProgram.Contains("http://127.0.0.1:5299", StringComparison.Ordinal), "Api.Local must stay localhost-only.");
        Require(!localApiProgram.Contains("0.0.0.0", StringComparison.Ordinal), "Api.Local must not bind to all interfaces.");
        RequireNoForbiddenWebMarkers(webRoot);
        LanM5ControlledRemoteTransportGateChecks.Run();
        LanM6PairingActivationRemoteHandshakeChecks.Run();
        LanM7SafeRemoteReadOnlyClientChecks.Run();
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

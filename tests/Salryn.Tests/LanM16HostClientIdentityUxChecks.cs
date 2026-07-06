namespace Salryn.Tests;

// LAN-M16 guard: the host / client identity UX in App.Web. It proves the host dashboard identifies
// itself as the POS host (DB local, second-computer-localhost is its own independent host, no sync),
// the remote POS client page carries the mandatory trust warnings and headers, and the remote POS
// client never calls host-local LAN control. Remote-client routing is checked separately from
// authenticated host navigation.
public static class LanM16HostClientIdentityUxChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string dashboard = ReadRequiredFile(webRoot, "pages", "DashboardPage.tsx");
        string remotePosPage = ReadRequiredFile(webRoot, "pages", "RemotePosClientPage.tsx");
        string remotePosClient = ReadRequiredFile(webRoot, "api", "remotePosClient.ts");
        string navigation = ReadRequiredFile(webRoot, "navigation.ts");
        string permissions = ReadRequiredFile(webRoot, "security", "permissions.ts");
        string app = ReadRequiredFile(webRoot, "App.tsx");
        string doc = ReadRequiredFile(projectRoot, "00-HANDOFF", "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertHostBanner(dashboard);
        AssertRemoteClientIdentity(remotePosPage);
        AssertRemoteClientWarningsAndHeaders(remotePosClient);
        AssertNoRemoteLanControl(remotePosClient, remotePosPage);
        AssertRemoteClientRouting(navigation, permissions, app);
        AssertWiring(doc, coreSmoke, infraSmoke);
    }

    private static void AssertHostBanner(string dashboard)
    {
        AssertContains(dashboard, "This computer is the POS host", "Dashboard must show the host identity banner.");
        AssertContains(dashboard, "independent host", "Dashboard must warn a second computer's localhost is its own host.");
        AssertContains(dashboard, "does not mean sync", "Dashboard must warn same-app-installed does not mean sync.");
    }

    private static void AssertRemoteClientIdentity(string remotePosPage)
    {
        AssertContains(remotePosPage, "Remote POS client", "Remote POS page must identify the remote client role.");
        AssertContains(remotePosPage, "This device is a Remote POS client", "Remote POS page must show the remote client warning banner.");
        AssertContains(remotePosPage, "Connect to the POS host over its explicit LAN address.", "Remote POS page must guide the operator to the host LAN address.");
        AssertContains(remotePosPage, "Pair this device first, then sign in as Cashier or Manager.", "Remote POS page must show the pairing-then-login flow.");
        AssertContains(remotePosPage, "Cashier / Manager login only", "Remote POS page must limit the login stage to Cashier/Manager.");
        AssertContains(remotePosPage, "Session expires", "Remote POS page must show session expiry.");
        AssertContains(remotePosPage, "Host LAN address", "Remote POS page must show the host LAN address.");
        AssertContains(remotePosPage, "Submit checkout", "Remote POS page must offer host-owned checkout.");
    }

    private static void AssertRemoteClientWarningsAndHeaders(string remotePosClient)
    {
        AssertContains(remotePosClient, "This device is a Remote POS client.", "Client warnings must identify the remote POS client role.");
        AssertContains(remotePosClient, "The host owns the database and sales.", "Client warnings must state the host owns data and sales.");
        AssertContains(remotePosClient, "Use the host LAN address shown on the host.", "Client warnings must direct the operator to the host LAN address.");
        AssertContains(remotePosClient, "Do not use localhost from another device.", "Client warnings must clarify localhost-on-another-computer semantics.");
        AssertContains(remotePosClient, "Ask the host operator for the join code.", "Client warnings must document the host-controlled join step.");
        AssertContains(remotePosClient, "Wait for the host to approve this device.", "Client warnings must document the host approval step.");
        AssertContains(remotePosClient, "After pairing, sign in as Cashier or Manager.", "Client warnings must document the login step.");
        AssertContains(remotePosClient, "Admin and Owner accounts are host-local only for this build.", "Client warnings must preserve the host-local Admin/Owner boundary.");
        AssertContains(remotePosClient, "X-Salryn-Terminal-Id", "Client must send the terminal identity header.");
        AssertContains(remotePosClient, "X-Salryn-Handshake-Session-Id", "Client must send the handshake proof header.");
        AssertContains(remotePosClient, "X-Salryn-Remote-Pos-Session", "Client must send the operator session header.");
    }

    private static void AssertNoRemoteLanControl(string remotePosClient, string remotePosPage)
    {
        AssertNotContains(remotePosClient, "/api/lan-control", "Remote POS client must never call /api/lan-control.");
        AssertNotContains(remotePosPage, "/api/lan-control", "Remote POS page must never call /api/lan-control.");
    }

    private static void AssertRemoteClientRouting(string navigation, string permissions, string app)
    {
        string consoleApp = ExtractBlock(app, "function ConsoleApp()", "export default function App()");
        string remoteClientApp = ExtractBlock(app, "function RemoteClientApp()", "function ConsoleApp()");

        AssertNotContains(navigation, "Remote POS Client", "Host navigation must not expose the Remote POS Client page.");
        AssertNotContains(navigation, "remote-pos", "Host navigation must not route to remote-pos.");
        AssertNotContains(permissions, "\"remote-pos\"", "Remote POS Client must not be a host AppPage.");
        AssertNotContains(consoleApp, "RemotePosClientPage", "ConsoleApp must not render Remote POS Client.");
        AssertContains(remoteClientApp, "RemotePosClientPage", "RemoteClientApp must render the Remote POS Client page.");
        AssertContains(app, "isRemoteClientBrowserLocation()", "Non-loopback browser routing must select the remote client app.");
    }

    private static void AssertWiring(string doc, string coreSmoke, string infraSmoke)
    {
        AssertContains(doc, "Host / Client Identity UX", "LAN-M16 doc must document the host/client identity UX.");
        AssertContains(coreSmoke, "LanM16HostClientIdentityUxChecks.Run();", "CoreSmoke must wire the LAN-M16 identity UX guard.");
        AssertContains(infraSmoke, "remote-pos-host-client-identity", "InfrastructureSmoke must wire the remote-pos-host-client-identity command.");
    }

    private static string ReadRequiredFile(params string[] pathParts)
    {
        string path = Path.Combine(pathParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Expected block start missing: " + startMarker);
        }

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Expected block end missing: " + endMarker);
        }

        return source[start..end];
    }
}

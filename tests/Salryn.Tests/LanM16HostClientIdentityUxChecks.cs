namespace Salryn.Tests;

// LAN-M16 guard: the host / client identity UX in App.Web. It proves the host dashboard identifies
// itself as the POS host (DB local, second-computer-localhost is its own independent host, no sync),
// the remote POS client page carries the mandatory trust warnings and headers, and the remote POS
// client never calls host-local LAN control. Registration wiring (nav/permissions/router) is checked.
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
        AssertRegistrationWiring(navigation, permissions, app);
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
        AssertContains(remotePosPage, "This is a remote POS client, not the POS host", "Remote POS page must identify the remote client role.");
        AssertContains(remotePosPage, "Remote client identity", "Remote POS page must show remote client identity.");
        AssertContains(remotePosPage, "Session expires", "Remote POS page must show session expiry.");
        AssertContains(remotePosPage, "Host LAN address", "Remote POS page must show the host LAN address.");
        AssertContains(remotePosPage, "Role capabilities", "Remote POS page must show the role capability matrix.");
        AssertContains(remotePosPage, "Submit sale to host", "Remote POS page must offer host-owned checkout.");
    }

    private static void AssertRemoteClientWarningsAndHeaders(string remotePosClient)
    {
        AssertContains(remotePosClient, "does not open the host database", "Client warnings must state it never opens the host DB.");
        AssertContains(remotePosClient, "does not synchronize", "Client warnings must state installs do not sync.");
        AssertContains(remotePosClient, "its own local host", "Client warnings must clarify localhost-on-another-computer semantics.");
        AssertContains(remotePosClient, "never performs discovery or network scanning", "Client must document no discovery/scan.");
        AssertContains(remotePosClient, "X-Salryn-Terminal-Id", "Client must send the terminal identity header.");
        AssertContains(remotePosClient, "X-Salryn-Handshake-Session-Id", "Client must send the handshake proof header.");
        AssertContains(remotePosClient, "X-Salryn-Remote-Pos-Session", "Client must send the operator session header.");
    }

    private static void AssertNoRemoteLanControl(string remotePosClient, string remotePosPage)
    {
        AssertNotContains(remotePosClient, "/api/lan-control", "Remote POS client must never call /api/lan-control.");
        AssertNotContains(remotePosPage, "/api/lan-control", "Remote POS page must never call /api/lan-control.");
    }

    private static void AssertRegistrationWiring(string navigation, string permissions, string app)
    {
        AssertContains(navigation, "remote-pos", "Navigation must register the remote POS client page.");
        AssertContains(permissions, "remote-pos", "Permissions must register the remote POS page.");
        AssertContains(app, "RemotePosClientPage", "App must route to the remote POS client page.");
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
}

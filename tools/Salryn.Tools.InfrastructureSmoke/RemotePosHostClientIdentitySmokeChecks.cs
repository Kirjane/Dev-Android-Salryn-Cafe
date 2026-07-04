// LAN-M16 remote-pos-host-client-identity smoke: verifies the App.Web surface makes host vs
// remote-client identity unambiguous, warns that a second computer's localhost is its own host
// (not sync), and never exposes host-local LAN control to the remote POS client. This is a
// source-level guard because the identity UX is static React content.
internal static class RemotePosHostClientIdentitySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16 REMOTE POS HOST/CLIENT IDENTITY SMOKE CHECKS =====");

        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string remotePosPage = ReadRequired(webRoot, "pages", "RemotePosClientPage.tsx");
        string dashboardPage = ReadRequired(webRoot, "pages", "DashboardPage.tsx");
        string remotePosClient = ReadRequired(webRoot, "api", "remotePosClient.ts");
        string navigation = ReadRequired(webRoot, "navigation.ts");
        string permissions = ReadRequired(webRoot, "security", "permissions.ts");
        string app = ReadRequired(webRoot, "App.tsx");

        // Host identity banner on the host dashboard.
        RequireContains(dashboardPage, "This computer is the POS host", "Dashboard must show the host identity banner.");
        RequireContains(dashboardPage, "independent host", "Dashboard must warn a second computer becomes its own host.");

        // Remote client identity + warnings.
        RequireContains(remotePosPage, "Remote POS client", "Remote POS page must identify the remote client role.");
        RequireContains(remotePosPage, "Remote client identity", "Remote POS page must show remote client identity.");
        RequireContains(remotePosPage, "Role capabilities", "Remote POS page must show role capabilities.");
        RequireContains(remotePosPage, "Submit sale to host", "Remote POS page must offer host-owned checkout.");

        RequireContains(remotePosClient, "does not open the host database", "Client warnings must state it never opens the host DB.");
        RequireContains(remotePosClient, "does not synchronize", "Client warnings must state installs do not sync.");
        RequireContains(remotePosClient, "its own local host", "Client warnings must clarify localhost-on-another-computer semantics.");
        RequireContains(remotePosClient, "X-Salryn-Remote-Pos-Session", "Client must send the operator session header.");
        RequireContains(remotePosClient, "X-Salryn-Terminal-Id", "Client must send terminal identity header.");
        RequireContains(remotePosClient, "X-Salryn-Handshake-Session-Id", "Client must send handshake proof header.");

        // The remote POS client must NOT call host-local LAN control.
        RequireNotContains(remotePosClient, "/api/lan-control", "Remote POS client must never call /api/lan-control.");
        RequireNotContains(remotePosPage, "/api/lan-control", "Remote POS page must never call /api/lan-control.");

        // Registration wiring.
        RequireContains(navigation, "remote-pos", "Navigation must register the remote POS client page.");
        RequireContains(permissions, "remote-pos", "Permissions must register the remote POS page.");
        RequireContains(app, "RemotePosClientPage", "App must route to the remote POS client page.");

        Console.WriteLine("LAN-M16 remote POS host/client identity smoke checks succeeded.");
        Console.WriteLine();
    }

    private static string ReadRequired(params string[] parts)
    {
        string path = Path.Combine(parts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required web source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

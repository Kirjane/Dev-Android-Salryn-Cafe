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
        RequireContains(remotePosPage, "This device is a Remote POS client", "Remote POS page must show the client warning banner.");
        RequireContains(remotePosPage, "Connect to the POS host over its explicit LAN address.", "Remote POS page must explain host connection topology.");
        RequireContains(remotePosPage, "Cashier / Manager login only", "Remote POS page must state the remote login role boundary.");
        RequireContains(remotePosPage, "Submit checkout", "Remote POS page must offer host-owned checkout.");

        RequireContains(remotePosClient, "This device is a Remote POS client.", "Client warnings must identify the device as a client.");
        RequireContains(remotePosClient, "The host owns the database and sales.", "Client warnings must state host-owned data authority.");
        RequireContains(remotePosClient, "Use the host LAN address shown on the host.", "Client warnings must explain host LAN address usage.");
        RequireContains(remotePosClient, "Do not use localhost from another device.", "Client warnings must clarify localhost-on-another-computer semantics.");
        RequireContains(remotePosClient, "Ask the host operator for the join code.", "Client warnings must mention the join code boundary.");
        RequireContains(remotePosClient, "Wait for the host to approve this device.", "Client warnings must state approval remains host-owned.");
        RequireContains(remotePosClient, "After pairing, sign in as Cashier or Manager.", "Client warnings must state the allowed remote login roles.");
        RequireContains(remotePosClient, "Admin and Owner accounts are host-local only for this build.", "Client warnings must state the host-local Admin/Owner boundary.");
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

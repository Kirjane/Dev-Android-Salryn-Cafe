internal static class LanM16RcBEntryBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16-RC-B ENTRY BOUNDARY SMOKE =====");

        string root = FindProjectRoot(projectRoot);
        string webRoot = Path.Combine(root, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(root, "src", "Salryn.Api.Local");

        string app = Read(webRoot, "App.tsx");
        string landing = Read(webRoot, "pages", "RemoteClientLandingPage.tsx");
        string login = Read(webRoot, "pages", "LoginPage.tsx");
        string hostPage = Read(webRoot, "pages", "HostLanControlPage.tsx");
        string readOnlyPage = Read(webRoot, "pages", "RemoteReadOnlyClientPage.tsx");
        string remotePosPage = Read(webRoot, "pages", "RemotePosClientPage.tsx");
        string remotePosHook = Read(webRoot, "hooks", "useRemotePosClient.ts");
        string localAccessGuard = Read(apiRoot, "Auth", "LocalApiLocalAccessGuard.cs");
        string permissionGuards = Read(apiRoot, "Auth", "LocalApiPermissionGuards.cs");
        string appFactory = Read(apiRoot, "LocalApiApplicationFactory.cs");
        string lanControlEndpoints = Read(apiRoot, "LocalLanControlEndpoints.cs");
        string capacityPolicy = Read(apiRoot, "RemotePos", "RemotePosCapacityPolicy.cs");
        string pairingGate = Read(apiRoot, "RemotePos", "RemotePosPairingGateService.cs");
        string sessionService = Read(apiRoot, "RemotePos", "RemotePosOperatorSessionService.cs");
        string lanControlService = Read(apiRoot, "Services", "LocalLanControlService.cs");

        Require(app.Contains("isRemoteClientBrowserLocation()", StringComparison.Ordinal), "App must classify LAN/non-loopback browser entry.");
        Require(app.Contains("return <RemoteClientApp />;", StringComparison.Ordinal), "LAN/non-loopback root must render remote-client entry first.");
        Require(app.Contains("return <ConsoleApp />;", StringComparison.Ordinal), "Loopback root must keep local console path.");
        Require(login.Contains("Sign in to Salryn Ledger Console", StringComparison.Ordinal), "Loopback login must remain available.");
        Require(!landing.Contains("Sign in to Salryn Ledger Console", StringComparison.Ordinal), "LAN landing must not expose local login first.");

        foreach (string required in new[]
        {
            "Connect to Salryn Host",
            "This is a remote client device",
            "The host owns the database, sales, inventory, and LAN approvals",
            "Do not use localhost from another device",
            "Use the host LAN address shown on the host PC",
            "Remote POS allows Cashier or Manager only after pairing",
            "Remote POS Client",
            "Remote Read-Only Client"
        })
        {
            Require(landing.Contains(required, StringComparison.Ordinal), "Missing landing copy: " + required);
        }

        Require(remotePosHook.Contains("startsFromRemoteEntry ? currentBrowserOrigin() : \"\"", StringComparison.Ordinal), "Remote POS LAN entry must use current host origin.");
        Require(remotePosPage.Contains("Cashier / Manager login only", StringComparison.Ordinal), "Remote POS must keep Cashier/Manager-only login copy.");
        Require(!remotePosPage.Contains("Admin credentials", StringComparison.OrdinalIgnoreCase), "Remote POS must not say admin credentials.");

        Require(hostPage.Contains("Host LAN Control", StringComparison.Ordinal), "Dedicated Host LAN Control page missing.");
        Require(hostPage.Contains("Host control is local-only", StringComparison.Ordinal), "Host LAN Control must show local-only safety.");
        Require(hostPage.Contains("Save/apply LAN bind", StringComparison.Ordinal), "Host LAN Control must apply explicit bind.");
        Require(hostPage.Contains("Rotate/show host join code", StringComparison.Ordinal), "Host LAN Control must rotate/show join code.");
        Require(hostPage.Contains("One-time pairing code", StringComparison.Ordinal), "Host LAN Control must show pairing code only after accept.");
        Require(hostPage.Contains("Max paired Remote POS terminals", StringComparison.Ordinal), "Host LAN Control must display paired terminal cap.");
        Require(hostPage.Contains("Max active Remote POS sessions", StringComparison.Ordinal), "Host LAN Control must display active session cap.");
        Require(!hostPage.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Host LAN Control must not show product browse.");
        Require(!hostPage.Contains("Preflight checklist", StringComparison.Ordinal), "Host LAN Control must not show giant read-only preflight.");
        Require(!readOnlyPage.Contains("Local LAN Control (Operator-Only)", StringComparison.Ordinal), "Remote Read-Only page must not expose host controls.");
        Require(!readOnlyPage.Contains("Save/apply LAN bind", StringComparison.Ordinal), "Remote Read-Only page must not expose LAN bind controls.");

        Require(localAccessGuard.Contains("LocalApi.LocalOperatorRequired", StringComparison.Ordinal), "Local API guard must reject non-local console access.");
        Require(localAccessGuard.Contains("RemoteReadOnlyBridgeHeaders.TerminalId", StringComparison.Ordinal), "Local API guard must reject bridge headers.");
        Require(permissionGuards.Contains("LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", StringComparison.Ordinal), "Host-local operator APIs must require loopback.");
        Require(appFactory.Contains("LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", StringComparison.Ordinal), "Login/session routes must require loopback.");
        Require(lanControlEndpoints.Contains("RequireLocalOperator", StringComparison.Ordinal), "/api/lan-control must remain local-only.");

        Require(capacityPolicy.Contains("DefaultMaxPairedTerminals = 5", StringComparison.Ordinal), "Default paired cap must be 5.");
        Require(capacityPolicy.Contains("DefaultMaxActiveSessions = 3", StringComparison.Ordinal), "Default active session cap must be 3.");
        Require(capacityPolicy.Contains("HardCap = 10", StringComparison.Ordinal), "Hard cap must be 10.");
        Require(pairingGate.Contains("PairedTerminalLimitReached", StringComparison.Ordinal), "Pairing gate must enforce paired terminal cap.");
        Require(sessionService.Contains("RemotePos.ActiveSessionLimitReached", StringComparison.Ordinal), "Session service must enforce active session cap.");
        Require(lanControlService.Contains("RemotePosLimits", StringComparison.Ordinal), "Host control status must expose capacity state.");

        Console.WriteLine("LAN-M16-RC-B entry boundary smoke passed.");
        Console.WriteLine();
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

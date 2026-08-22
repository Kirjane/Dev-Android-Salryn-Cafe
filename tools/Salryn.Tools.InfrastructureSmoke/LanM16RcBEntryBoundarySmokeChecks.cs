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
        string dashboardPage = Read(webRoot, "pages", "DashboardPage.tsx");
        string remotePosHook = Read(webRoot, "hooks", "useRemotePosClient.ts");
        string appShell = Read(webRoot, "components", "AppShell.tsx");
        string navigation = Read(webRoot, "navigation.ts");
        string permissions = Read(webRoot, "security", "permissions.ts");
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
        Require(app.Contains("type RemoteClientMode = \"landing\" | \"remote-pos\" | \"remote-readonly\"", StringComparison.Ordinal), "Remote client modes must stay separate from host pages.");
        Require(app.Contains("mode === \"remote-pos\" ? <RemotePosClientPage /> : <RemoteReadOnlyClientPage />", StringComparison.Ordinal), "RemoteClientApp must render remote POS and read-only pages.");
        string consoleApp = ExtractBlock(app, "function ConsoleApp()", "export default function App()");
        string remoteClientApp = ExtractBlock(app, "function RemoteClientApp()", "function ConsoleApp()");
        Require(!consoleApp.Contains("RemotePosClientPage", StringComparison.Ordinal), "ConsoleApp must not render Remote POS Client.");
        Require(!consoleApp.Contains("RemoteReadOnlyClientPage", StringComparison.Ordinal), "ConsoleApp must not render Remote Read-Only Client.");
        Require(!app.Contains("case \"remote-pos\"", StringComparison.Ordinal), "Host page switch must not contain Remote POS Client route.");
        Require(!app.Contains("case \"remote-readonly\"", StringComparison.Ordinal), "Host page switch must not contain Remote Read-Only Client route.");
        Require(remoteClientApp.Contains("RemotePosClientPage", StringComparison.Ordinal), "Remote POS Client must remain inside RemoteClientApp.");
        Require(remoteClientApp.Contains("RemoteReadOnlyClientPage", StringComparison.Ordinal), "Remote Read-Only Client must remain inside RemoteClientApp.");
        Require(app.Contains("useState<AppPage>(\"host-lan-control\")", StringComparison.Ordinal), "Host authenticated default must be Host LAN Control.");
        Require(!app.Contains("useState<AppPage>(\"remote-readonly\")", StringComparison.Ordinal), "Host authenticated default must not be Remote Read-Only Client.");
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
        Require(remotePosPage.Contains("Cashier / Manager login", StringComparison.Ordinal), "Remote POS must keep Cashier/Manager login copy.");
        Require(!remotePosPage.Contains("Admin credentials", StringComparison.OrdinalIgnoreCase), "Remote POS must not say admin credentials.");

        Require(navigation.Contains("{ id: \"host-lan-control\", label: \"Host LAN Control\", enabled: true }", StringComparison.Ordinal), "Host LAN Control must be an enabled sidebar item.");
        Require(!navigation.Contains("label: \"Host LAN Control\", enabled: false", StringComparison.Ordinal), "Host LAN Control must not be marked Later.");
        Require(!navigation.Contains("Remote POS Client", StringComparison.Ordinal), "Host navigation must not show Remote POS Client.");
        Require(!navigation.Contains("Remote Read-Only Client", StringComparison.Ordinal), "Host navigation must not show Remote Read-Only Client.");
        Require(!navigation.Contains("remote-pos", StringComparison.Ordinal), "Host navigation must not route to Remote POS Client.");
        Require(!navigation.Contains("remote-readonly", StringComparison.Ordinal), "Host navigation must not route to Remote Read-Only Client.");
        Require(permissions.Contains("\"host-lan-control\": [", StringComparison.Ordinal), "Host LAN Control must have a page permission rule.");
        Require(permissions.Contains("Permissions.StoreTerminalView", StringComparison.Ordinal), "Host LAN Control must allow terminal view permission.");
        Require(permissions.Contains("Permissions.StoreTerminalManage", StringComparison.Ordinal), "Host LAN Control must allow terminal manage permission.");
        Require(permissions.Contains("Permissions.StoreDeploymentManage", StringComparison.Ordinal), "Host LAN Control must allow deployment permission.");
        Require(permissions.Contains("const authenticatedOnlyPages = new Set<AppPage>([\"dashboard\"])", StringComparison.Ordinal), "Host LAN Control must not be authenticated-only.");
        Require(!permissions.Contains("\"remote-pos\"", StringComparison.Ordinal), "Remote POS Client must not be a host AppPage.");
        Require(!permissions.Contains("\"remote-readonly\"", StringComparison.Ordinal), "Remote Read-Only Client must not be a host AppPage.");
        Require(appShell.Contains("Device wants to join", StringComparison.Ordinal), "Host shell must show pending join notification.");
        Require(appShell.Contains("onNavigate(\"host-lan-control\")", StringComparison.Ordinal), "Pending join notification must open Host LAN Control.");
        Require(appShell.Contains("Permissions.StoreTerminalView", StringComparison.Ordinal), "Pending join polling must be permission-gated.");
        Require(appShell.Contains("Salryn host operator console", StringComparison.Ordinal), "Host shell footer must label host operator console.");
        Require(!appShell.Contains("Salryn read-only operator console", StringComparison.Ordinal), "Host shell must not keep stale read-only console label.");
        Require(dashboardPage.Contains("Open Host LAN Control", StringComparison.Ordinal), "Dashboard must link to Host LAN Control.");
        Require(hostPage.Contains("Host LAN Control", StringComparison.Ordinal), "Dedicated Host LAN Control page missing.");
        Require(hostPage.Contains("Host control is local-only", StringComparison.Ordinal), "Host LAN Control must show local-only safety.");
        Require(hostPage.Contains("LAN Connection", StringComparison.Ordinal), "Host LAN Control must lead with LAN Connection section.");
        Require(hostPage.Contains("Activate LAN connection", StringComparison.Ordinal), "Host LAN Control must expose activate LAN action.");
        Require(hostPage.Contains("Disable LAN connection", StringComparison.Ordinal), "Host LAN Control must expose disable LAN action.");
        Require(hostPage.Contains("Rotate/show host join code", StringComparison.Ordinal), "Host LAN Control must rotate/show join code.");
        Require(hostPage.Contains("Review the pending device request here", StringComparison.Ordinal), "Host LAN Control must put pending requests in the main approval flow.");
        Require(hostPage.Contains("Device wants to join", StringComparison.Ordinal), "Host LAN Control must show a pending request alert.");
        Require(hostPage.Contains("Accept", StringComparison.Ordinal), "Host LAN Control must include accept action.");
        Require(hostPage.Contains("Reject", StringComparison.Ordinal), "Host LAN Control must include reject action.");
        Require(hostPage.Contains("One-time pairing code", StringComparison.Ordinal), "Host LAN Control must show pairing code only after accept.");
        Require(hostPage.Contains("Give this code to the client device.", StringComparison.Ordinal), "Host LAN Control must explain one-time pairing code handoff.");
        Require(hostPage.Contains("Remaining capacity", StringComparison.Ordinal), "Host LAN Control must display terminal/session capacity.");
        Require(hostPage.Contains("Terminal connections", StringComparison.Ordinal), "Host LAN Control must group terminal connections.");
        Require(!hostPage.Contains("Product Browse / Read-Only Surface", StringComparison.Ordinal), "Host LAN Control must not show product browse.");
        Require(!hostPage.Contains("Preflight checklist", StringComparison.Ordinal), "Host LAN Control must not show giant read-only preflight.");
        Require(!readOnlyPage.Contains("Local LAN Control (Operator-Only)", StringComparison.Ordinal), "Remote Read-Only page must not expose host controls.");
        Require(!readOnlyPage.Contains("Activate LAN connection", StringComparison.Ordinal), "Remote Read-Only page must not expose LAN bind controls.");

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

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Require(start >= 0, "Expected block start missing: " + startMarker);

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Require(end >= 0, "Expected block end missing: " + endMarker);

        return source[start..end];
    }
}

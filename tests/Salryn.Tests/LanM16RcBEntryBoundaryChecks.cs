namespace Salryn.Tests;

public static class LanM16RcBEntryBoundaryChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string app = ReadRequiredFile(webRoot, "App.tsx");
        string landing = ReadRequiredFile(webRoot, "pages", "RemoteClientLandingPage.tsx");
        string login = ReadRequiredFile(webRoot, "pages", "LoginPage.tsx");
        string hostPage = ReadRequiredFile(webRoot, "pages", "HostLanControlPage.tsx");
        string readOnlyPage = ReadRequiredFile(webRoot, "pages", "RemoteReadOnlyClientPage.tsx");
        string remotePosPage = ReadRequiredFile(webRoot, "pages", "RemotePosClientPage.tsx");
        string dashboardPage = ReadRequiredFile(webRoot, "pages", "DashboardPage.tsx");
        string remotePosHook = ReadRequiredFile(webRoot, "hooks", "useRemotePosClient.ts");
        string appShell = ReadRequiredFile(webRoot, "components", "AppShell.tsx");
        string navigation = ReadRequiredFile(webRoot, "navigation.ts");
        string permissions = ReadRequiredFile(webRoot, "security", "permissions.ts");

        string appFactory = ReadRequiredFile(apiRoot, "LocalApiApplicationFactory.cs");
        string localAccessGuard = ReadRequiredFile(apiRoot, "Auth", "LocalApiLocalAccessGuard.cs");
        string permissionGuards = ReadRequiredFile(apiRoot, "Auth", "LocalApiPermissionGuards.cs");
        string lanControlAuthorization = ReadRequiredFile(apiRoot, "Auth", "LocalLanControlAuthorization.cs");
        string lanControlService = ReadRequiredFile(apiRoot, "Services", "LocalLanControlService.cs");
        string lanControlEndpoints = ReadRequiredFile(apiRoot, "LocalLanControlEndpoints.cs");
        string capacityPolicy = ReadRequiredFile(apiRoot, "RemotePos", "RemotePosCapacityPolicy.cs");
        string pairingGate = ReadRequiredFile(apiRoot, "RemotePos", "RemotePosPairingGateService.cs");
        string sessionService = ReadRequiredFile(apiRoot, "RemotePos", "RemotePosOperatorSessionService.cs");
        string apiEndpoints = ReadRequiredFile(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs");
        string contracts = ReadRequiredFile(apiRoot, "Contracts", "RemoteReadOnlyApiContracts.cs");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertRemoteEntry(app, landing, login, remotePosHook);
        AssertHostControlPage(app, appShell, hostPage, readOnlyPage, remotePosPage, dashboardPage, navigation, permissions);
        AssertBackendBoundary(appFactory, localAccessGuard, permissionGuards, lanControlAuthorization, lanControlEndpoints);
        AssertCapacity(capacityPolicy, pairingGate, sessionService, apiEndpoints, lanControlService, contracts, hostPage);
        AssertWiring(coreSmoke, infraSmoke);
    }

    private static void AssertRemoteEntry(
        string app,
        string landing,
        string login,
        string remotePosHook)
    {
        AssertContains(app, "isRemoteClientBrowserLocation()", "App must classify non-loopback browser locations.");
        AssertContains(app, "isRemoteClientBrowserLocation() ? <RemoteClientApp /> : <ConsoleApp />", "LAN/non-loopback root must render remote client entry before console auth while loopback keeps the local console path.");
        AssertContains(login, "Sign in to Salryn POS", "Local loopback login page must remain available.");

        // PRE-BETA-E: primary path is Salryn terminal shell; Remote Read-Only is support-only.
        foreach (string requiredCopy in new[]
        {
            "Open Salryn on this terminal",
            "Terminal Mode",
            "The Host owns the database, approvals, and admin tools",
            "Use the Host LAN address shown on the Host PC",
            "Pair this device, then sign in as Cashier or Manager",
            "Admin and Owner accounts remain host-local",
            "Open Salryn POS terminal",
            "Internal support surfaces",
            "Open Remote Read-Only (support only)"
        })
        {
            AssertContains(landing, requiredCopy, "Remote client landing copy missing: " + requiredCopy);
        }

        AssertNotContains(landing, "Sign in to Salryn POS", "LAN landing must not show the local POS login.");
        AssertContains(app, "type RemoteClientMode = \"activation\" | \"landing\" | \"remote-pos\" | \"remote-readonly\"", "Remote client modes must stay separate from host pages.");
        AssertContains(app, "function RemoteClientApp()", "Remote client app must own LAN client rendering.");
        AssertContains(app, "RemotePosClientPage onExitToLanding", "Remote client app must offer Remote POS terminal path.");
        AssertContains(app, "RemoteReadOnlyClientPage", "Remote client app must still offer support Remote Read-Only page.");
        AssertContains(remotePosHook, "startsFromRemoteEntry ? currentBrowserOrigin() : \"\"", "Remote POS must use current LAN host origin from remote entry.");
        AssertContains(remotePosHook, "useState(startsFromRemoteEntry)", "Remote POS must skip the extra host-address state when launched from LAN root.");
    }

    private static void AssertHostControlPage(
        string app,
        string appShell,
        string hostPage,
        string readOnlyPage,
        string remotePosPage,
        string dashboardPage,
        string navigation,
        string permissions)
    {
        // PRE-I LAN clarity: page is a human Host PC / terminal-device wizard; assert operator meaning.
        foreach (string required in new[]
        {
            "Terminal Management",
            "Host PC address",
            "Terminal link",
            "Terminal POS access",
            "Each terminal device needs its own Terminal license",
            "Capacity",
            "Connect terminals over LAN",
            "Start LAN access",
            "Stop LAN access",
            "Create Host join code",
            "Copy terminal link",
            "Pair a terminal device",
            "Review the terminal device request here",
            "Device wants to join",
            "Observed origin/IP",
            "Accept",
            "Reject",
            "One-time pairing code",
            "Enter this code on the terminal device.",
            "Terminal connections",
            "Safety availability",
            "Terminal help",
            "Advanced diagnostics"
        })
        {
            AssertContains(hostPage, required, "Host LAN Control page missing: " + required);
        }

        AssertNotContains(hostPage, "Product Browse / Read-Only Surface", "Host LAN Control page must not show product browse.");
        AssertNotContains(hostPage, "Preflight checklist", "Host LAN Control page must not show the giant read-only preflight dump.");
        AssertNotContains(readOnlyPage, "Local LAN Control (Operator-Only)", "Remote Read-Only Client page must not expose host controls.");
        AssertNotContains(readOnlyPage, "Save/apply LAN settings", "Remote Read-Only Client page must not expose LAN bind controls.");
        AssertNotContains(readOnlyPage, "Rotate/show host join code", "Remote Read-Only Client page must not expose join-code controls.");
        AssertContains(readOnlyPage, "Runtime Diagnostics", "Remote Read-Only Client page must keep safe diagnostics.");
        AssertContains(readOnlyPage, "<details>", "Remote Read-Only Client diagnostics/troubleshooting must be collapsed by default.");
        AssertContains(remotePosPage, "Cashier / Manager login", "Remote POS page must keep Cashier/Manager login copy.");
        AssertNotContains(remotePosPage, "Admin credentials", "Remote POS page must not say admin credentials.");
        string consoleApp = ExtractBlock(app, "function ConsoleApp()", "export default function App()");
        string remoteClientApp = ExtractBlock(app, "function RemoteClientApp()", "function ConsoleApp()");
        AssertNotContains(consoleApp, "RemotePosClientPage", "ConsoleApp must not render Remote POS Client.");
        AssertNotContains(consoleApp, "RemoteReadOnlyClientPage", "ConsoleApp must not render Remote Read-Only Client.");
        AssertNotContains(app, "case \"remote-pos\"", "Host page switch must not contain Remote POS Client route.");
        AssertNotContains(app, "case \"remote-readonly\"", "Host page switch must not contain Remote Read-Only Client route.");
        AssertContains(remoteClientApp, "RemotePosClientPage", "Remote POS Client must remain available inside RemoteClientApp.");
        AssertContains(remoteClientApp, "RemoteReadOnlyClientPage", "Remote Read-Only Client must remain available inside RemoteClientApp.");
        AssertContains(app, "useState<AppPage>(\"dashboard\")", "Authenticated host default page must remain dashboard, not Remote Read-Only Client.");
        AssertNotContains(app, "useState<AppPage>(\"remote-readonly\")", "Authenticated host default page must not be Remote Read-Only Client.");
        AssertNotContains(app, "useState<AppPage>(\"checkout\")", "Authenticated host default page must not be checkout.");
        AssertContains(appShell, "Terminal request pending", "Internal terminal mode must surface pending join notification.");
        AssertContains(appShell, "onNavigate(\"host-lan-control\")", "Host shell pending notification must open Host LAN Control.");
        AssertContains(appShell, "fetchRemotePosPairingHostStatus", "Host shell must poll pending Remote POS join requests.");
        AssertContains(appShell, "Permissions.StoreTerminalView", "Host shell pending join polling must be permission-gated.");
        AssertContains(appShell, "Salryn POS", "Host shell footer must use client-facing branding.");
        AssertNotContains(appShell, "Salryn read-only operator console", "Host shell must remove stale read-only operator label.");
        AssertNotContains(dashboardPage, "Open Host LAN Control", "Dashboard must not expose incomplete terminal controls.");
        AssertNotContains(dashboardPage, "canOpenHostLanControl", "Dashboard must not carry stale LAN-card props.");
        AssertContains(navigation, "{ id: \"host-lan-control\", label: \"LAN Terminals · Future Feature\", enabled: true, group: \"Admin\" }", "Normal Admin navigation must state the LAN future-feature boundary.");
        AssertContains(navigation, "VITE_SALRYN_INTERNAL_SURFACES", "Report Artifacts must retain its internal-mode boundary.");
        AssertNotContains(navigation, "Remote POS Client", "Host navigation must not show Remote POS Client.");
        AssertNotContains(navigation, "Remote Read-Only Client", "Host navigation must not show Remote Read-Only Client.");
        AssertNotContains(navigation, "remote-pos", "Host navigation must not route to Remote POS Client.");
        AssertNotContains(navigation, "remote-readonly", "Host navigation must not route to Remote Read-Only Client.");
        AssertContains(permissions, "\"host-lan-control\"", "Permission catalog must register Host LAN Control page.");
        AssertContains(permissions, "\"host-lan-control\": [", "Host LAN Control must use a permission rule, not authenticated-only access.");
        AssertContains(permissions, "Permissions.StoreTerminalView", "Host LAN Control view must allow terminal view permission.");
        AssertContains(permissions, "Permissions.StoreTerminalManage", "Host LAN Control view must allow terminal manage permission.");
        AssertContains(permissions, "Permissions.StoreDeploymentManage", "Host LAN Control view must allow deployment permission.");
        AssertContains(permissions, "const authenticatedOnlyPages = new Set<AppPage>([\"dashboard\"])", "Host LAN Control must not be authenticated-only.");
        AssertNotContains(permissions, "\"remote-pos\"", "Remote POS Client must not be a host AppPage.");
        AssertNotContains(permissions, "\"remote-readonly\"", "Remote Read-Only Client must not be a host AppPage.");
    }

    private static void AssertBackendBoundary(
        string appFactory,
        string localAccessGuard,
        string permissionGuards,
        string lanControlAuthorization,
        string lanControlEndpoints)
    {
        AssertContains(localAccessGuard, "IsLoopbackOperatorRequest", "Local access guard must classify loopback operator requests.");
        AssertContains(localAccessGuard, "RemoteReadOnlyBridgeHeaders.TerminalId", "Local access guard must reject bridge headers.");
        AssertContains(localAccessGuard, "LocalApi.LocalOperatorRequired", "Local access guard must return a safe rejection code.");
        AssertContains(appFactory, "MapPost(\"/api/auth/login\"", "Local auth login route must still exist.");
        AssertContains(appFactory, "MapGet(\"/api/session\"", "Local session route must still exist.");
        AssertContains(appFactory, "LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", "Login/session routes must require loopback.");
        AssertContains(permissionGuards, "LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", "Local operator APIs must require loopback.");
        AssertContains(lanControlAuthorization, "LocalLanControl.LocalOperatorRequired", "LAN control must remain local-only.");
        AssertContains(lanControlEndpoints, "MapGroup(\"/api/lan-control\")", "LAN control must remain under local-only route group.");
        AssertContains(lanControlEndpoints, "RequireLocalOperator", "LAN control endpoints must require local operators.");
    }

    private static void AssertCapacity(
        string capacityPolicy,
        string pairingGate,
        string sessionService,
        string apiEndpoints,
        string lanControlService,
        string contracts,
        string hostPage)
    {
        AssertContains(capacityPolicy, "DefaultMaxPairedTerminals = 5", "Default paired terminal cap must be 5.");
        AssertContains(capacityPolicy, "DefaultMaxActiveSessions = 3", "Default active session cap must be 3.");
        AssertContains(capacityPolicy, "HardCap = 10", "Remote POS hard cap must be 10.");
        AssertContains(pairingGate, "PairedTerminalLimitReached", "Pairing gate must reject terminal capacity overflow.");
        AssertContains(pairingGate, "CountReservedOrPairedTerminalsAsync", "Host approval must reserve terminal capacity.");
        AssertContains(pairingGate, "CountActivePairedTerminalsAsync", "Pair activation must re-check terminal capacity.");
        AssertContains(sessionService, "RemotePos.ActiveSessionLimitReached", "Remote session issuance must reject active-session overflow.");
        AssertContains(sessionService, "GetActiveSessionCount", "Session service must expose active session count.");
        AssertContains(apiEndpoints, "Status429TooManyRequests", "Active-session overflow should return a clear capacity status.");
        AssertContains(lanControlService, "RemotePosLimits", "Host control status must expose Remote POS limits.");
        AssertContains(contracts, "RemotePosCapacityStatusResponse", "Host control contract must carry limit state.");
        AssertContains(hostPage, "remainingPairingCapacity", "Host page must display paired terminal capacity.");
        AssertContains(hostPage, "remainingActiveSessionCapacity", "Host page must display active session capacity.");
    }

    private static void AssertWiring(string coreSmoke, string infraSmoke)
    {
        AssertContains(coreSmoke, "LanM16RcBEntryBoundaryChecks.Run();", "CoreSmoke must wire LAN-M16-RC-B guard.");
        AssertContains(infraSmoke, "lan-m16-rc-b-entry-boundary", "InfrastructureSmoke must wire LAN-M16-RC-B command.");
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

    private static string FindProjectRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln.");
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

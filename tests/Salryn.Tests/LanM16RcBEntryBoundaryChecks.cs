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
        string remotePosHook = ReadRequiredFile(webRoot, "hooks", "useRemotePosClient.ts");
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
        AssertHostControlPage(hostPage, readOnlyPage, remotePosPage, navigation, permissions);
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
        AssertContains(app, "return <RemoteClientApp />;", "LAN/non-loopback root must render remote client entry before console auth.");
        AssertContains(app, "return <ConsoleApp />;", "Loopback root must still render the local console path.");
        AssertContains(login, "Sign in to Salryn Ledger Console", "Local loopback login page must remain available.");

        foreach (string requiredCopy in new[]
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
            AssertContains(landing, requiredCopy, "Remote client landing copy missing: " + requiredCopy);
        }

        AssertNotContains(landing, "Sign in to Salryn Ledger Console", "LAN landing must not show the local Ledger Console login.");
        AssertContains(remotePosHook, "startsFromRemoteEntry ? currentBrowserOrigin() : \"\"", "Remote POS must use current LAN host origin from remote entry.");
        AssertContains(remotePosHook, "useState(startsFromRemoteEntry)", "Remote POS must skip the extra host-address state when launched from LAN root.");
    }

    private static void AssertHostControlPage(
        string hostPage,
        string readOnlyPage,
        string remotePosPage,
        string navigation,
        string permissions)
    {
        foreach (string required in new[]
        {
            "Host LAN Control",
            "LAN transport",
            "Host local URL",
            "Host LAN URL",
            "Remote POS runtime",
            "Remote Read-Only",
            "Host control is local-only",
            "Save/apply LAN bind",
            "Rotate/show host join code",
            "Copy client URL",
            "Client devices use the host LAN IP, not localhost.",
            "Pairing Control",
            "Observed origin/IP",
            "One-time pairing code",
            "Terminal Control",
            "Max paired Remote POS terminals",
            "Max active Remote POS sessions",
            "Current paired count",
            "Current active sessions",
            "Remaining capacity",
            "Advanced diagnostics"
        })
        {
            AssertContains(hostPage, required, "Host LAN Control page missing: " + required);
        }

        AssertNotContains(hostPage, "Product Browse / Read-Only Surface", "Host LAN Control page must not show product browse.");
        AssertNotContains(hostPage, "Preflight checklist", "Host LAN Control page must not show the giant read-only preflight dump.");
        AssertNotContains(readOnlyPage, "Local LAN Control (Operator-Only)", "Remote Read-Only Client page must not expose host controls.");
        AssertNotContains(readOnlyPage, "Save/apply LAN bind", "Remote Read-Only Client page must not expose LAN bind controls.");
        AssertNotContains(readOnlyPage, "Rotate/show host join code", "Remote Read-Only Client page must not expose join-code controls.");
        AssertContains(readOnlyPage, "Runtime Diagnostics", "Remote Read-Only Client page must keep safe diagnostics.");
        AssertContains(readOnlyPage, "<details>", "Remote Read-Only Client diagnostics/troubleshooting must be collapsed by default.");
        AssertContains(remotePosPage, "Cashier / Manager login only", "Remote POS page must keep Cashier/Manager login copy.");
        AssertNotContains(remotePosPage, "Admin credentials", "Remote POS page must not say admin credentials.");
        AssertContains(navigation, "Host LAN Control", "Navigation must expose dedicated Host LAN Control page to local operators.");
        AssertContains(permissions, "\"host-lan-control\"", "Permission catalog must register Host LAN Control page.");
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
}

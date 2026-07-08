namespace Salryn.Tests;

public static class V2LanClientPairingPersistenceChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string infraRoot = Path.Combine(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke");

        string storage = ReadRequiredFile(Path.Combine(webRoot, "utils", "lanClientStorage.ts"));
        string hook = ReadRequiredFile(Path.Combine(webRoot, "hooks", "useRemotePosClient.ts"));
        string page = ReadRequiredFile(Path.Combine(webRoot, "pages", "RemotePosClientPage.tsx"));
        string remotePosClient = ReadRequiredFile(Path.Combine(webRoot, "api", "remotePosClient.ts"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));
        string endpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs"));
        string contracts = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosPairingContracts.cs"));
        string trustService = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosTerminalTrustService.cs"));
        string handshakeService = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "RemoteClientHandshakeService.cs"));
        string sessionService = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosOperatorSessionService.cs"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string infraSmoke = ReadRequiredFile(Path.Combine(infraRoot, "Program.cs"));
        string flowResetChecks = ReadRequiredFile(Path.Combine(infraRoot, "FlowResetDevSeedChecks.cs"));
        string flowResetScript = ReadRequiredFile(Path.Combine(projectRoot, "tools", "dev", "Reset-SalrynDevDb.ps1"));

        AssertContains(storage, "salryn.lanClient.v1", "LAN client storage must use a versioned key.");
        AssertContains(storage, "version: 1", "LAN client storage record must be versioned.");
        AssertContains(storage, "localStorage", "LAN client pairing must use durable browser storage.");
        AssertContains(storage, "clearLanClientPairing", "LAN client storage must expose clear pairing.");
        AssertContains(storage, "hasPairedTerminalMaterial", "LAN client storage must gate paired terminal material.");
        AssertContains(storage, "lastKnownState", "LAN client storage must persist last known flow state.");
        AssertContains(storage, "S2_REQUEST_PENDING", "LAN client storage must track pending request state.");
        AssertContains(storage, "S3_HOST_APPROVED_NEEDS_ACTIVATION", "LAN client storage must track approved-awaiting-activation state.");
        AssertContains(storage, "S4_TERMINAL_TRUSTED", "LAN client storage must track trusted terminal state.");
        AssertContains(storage, "sessionStorage", "Operator session token must use session-scoped storage.");
        AssertContains(storage, "readRemotePosOperatorSessionToken", "LAN client storage must read operator session token.");
        AssertContains(storage, "writeRemotePosOperatorSessionToken", "LAN client storage must write operator session token.");
        AssertContains(storage, "clearRemotePosOperatorSessionToken", "LAN client storage must clear operator session token.");
        AssertNotContains(storage, "password", "Storage shape must not include operator password.");
        AssertPairingRecordExcludesSessionToken(storage);

        AssertContains(hook, "readLanClientPairing", "Remote POS hook must read stored pairing on load.");
        AssertContains(hook, "writeLanClientPairing", "Remote POS hook must persist pairing material.");
        AssertContains(hook, "clearLanClientPairing", "Remote POS hook must clear stored pairing.");
        AssertContains(hook, "fetchRemotePosTerminalTrust", "Remote POS hook must probe backend terminal trust on restore.");
        AssertContains(hook, "remotePosRefreshTerminalHandshake", "Remote POS hook must refresh expired trusted handshakes.");
        AssertContains(hook, "HandshakeExpired", "Remote POS hook must handle handshake-expired trust status.");
        AssertContains(hook, "flowState", "Remote POS hook must expose guided flow state.");
        AssertContains(hook, "RemotePosFlowState", "Remote POS hook must define explicit flow states.");
        AssertContains(hook, "S_RESTORING", "Remote POS hook must expose restoring state.");
        AssertContains(hook, "S_OFFLINE", "Remote POS hook must expose offline state.");
        AssertContains(hook, "S3_HOST_APPROVED_NEEDS_ACTIVATION", "Remote POS hook must separate approved from trusted state.");
        AssertContains(hook, "restoreAttemptedRef", "Remote POS hook must guard duplicate restore attempts.");
        AssertContains(hook, "pairingRestored", "Remote POS hook must expose pairing restored flag.");
        AssertContains(hook, "trustProbeResult", "Remote POS hook must expose trust probe result.");
        AssertContains(hook, "storageRestored", "Remote POS hook must expose storage restored flag.");
        AssertContains(hook, "clearPairing", "Remote POS hook must expose clear pairing action.");
        AssertContains(hook, "terminalIdSuffix", "Remote POS hook must expose terminal id suffix.");
        AssertContains(hook, "Pairing revoked or expired. Re-pair this device.", "Restore must clear revoked pairing and show re-pair message.");
        AssertContains(hook, "shouldProbeTerminalTrust", "Remote POS hook must gate terminal trust probe on stored terminal material.");
        AssertContains(hook, "resolveRestoreFlowState", "Remote POS hook must expose pure restore flow resolution.");
        AssertContains(hook, "S1_UNPAIRED", "Remote POS hook must route no-storage restore to unpaired state.");
        AssertContains(hook, "lastPairingNotice", "Remote POS hook must expose pairing notice for stale or revoked states.");
        AssertContains(hook, "hasStoredPairing", "Remote POS hook must expose hasStoredPairing diagnostic.");
        AssertContains(hook, "hasTerminalId", "Remote POS hook must expose hasTerminalId diagnostic.");
        AssertContains(hook, "startOver", "Remote POS hook must expose start-over action.");
        AssertContains(hook, "Pairing expired or reset. Request access again.", "Stale pairing after DB reset must show reset notice.");
        AssertContains(hook, "Pairing revoked by host. Request access again.", "Explicit revoked trust must show revoked notice.");
        AssertNotContains(hook, "void resolveTrustStatus(stored)", "Hook must not probe terminal trust when stored pairing is null.");
        AssertNotContains(hook, "localStorage.setItem", "Hook must route storage through lanClientStorage helper.");

        AssertContains(page, "Paired terminal restored", "Remote POS page must show restored pairing banner.");
        AssertContains(page, "Clear this device pairing", "Remote POS page must expose clear pairing action.");
        AssertContains(page, "Pairing revoked or expired", "Remote POS page must show revoked pairing message.");
        AssertContains(page, "terminalIdSuffix", "Remote POS page must show terminal id suffix.");
        AssertContains(page, "flowState", "Remote POS page must render guided flow state.");
        AssertContains(page, "S_RESTORING", "Remote POS page must show restoring state.");
        AssertContains(page, "S_OFFLINE", "Remote POS page must show offline state.");
        AssertContains(page, "S3_HOST_APPROVED_NEEDS_ACTIVATION", "Remote POS page must show approved-awaiting-activation state.");
        AssertContains(page, "trustProbeResult", "Remote POS page must show trust probe diagnostics.");
        AssertContains(page, "storageRestored", "Remote POS page must show storage restore diagnostics.");
        AssertContains(page, "hasStoredPairing", "Remote POS page must show hasStoredPairing diagnostic.");
        AssertContains(page, "hasTerminalId", "Remote POS page must show hasTerminalId diagnostic.");
        AssertContains(page, "Start over / request access again", "Remote POS page must expose start-over path.");
        AssertContains(page, "lastPairingNotice", "Remote POS page must render pairing notice on unpaired restore.");
        AssertNotContains(page, "\"/api/checkout\"", "Remote POS page must not call host /api/checkout.");

        AssertContains(remotePosClient, "/api/remote-pos/terminal/trust", "Remote POS client must call terminal trust probe.");
        AssertContains(remotePosClient, "/api/remote-pos/terminal/refresh-handshake", "Remote POS client must call handshake refresh endpoint.");
        AssertContains(remotePosClient, "HandshakeExpired", "Remote POS client must model handshake-expired trust status.");
        AssertContains(remotePosClient, "remotePosRefreshTerminalHandshake", "Remote POS client must expose handshake refresh helper.");

        AssertContains(endpoints, "/terminal/trust", "Remote POS API must expose terminal trust probe.");
        AssertContains(endpoints, "/terminal/refresh-handshake", "Remote POS API must expose trusted handshake refresh.");
        AssertContains(endpoints, "Results.Ok(response)", "Terminal trust probe must return 200 with rich status payload.");
        AssertContains(endpoints, "RefreshTrustedTerminalHandshakeAsync", "Handshake refresh endpoint must call trusted refresh service.");

        AssertContains(contracts, "RemotePosTerminalTrustResponse", "Remote POS contracts must include terminal trust response.");
        AssertContains(contracts, "RemotePosTerminalHandshakeRefreshResponse", "Remote POS contracts must include handshake refresh response.");
        AssertContains(contracts, "OperatorSessionValid", "Terminal trust response must expose operator session validity.");
        AssertContains(contracts, "RequiresPairingCode", "Terminal trust response must expose pairing-code requirement.");

        AssertContains(trustService, "HandshakeExpired", "Terminal trust service must classify expired handshakes without clearing registry trust.");
        AssertContains(trustService, "RemotePosTerminalTrustStatuses.Trusted", "Terminal trust service must resolve trusted active terminals.");

        AssertContains(handshakeService, "RefreshTrustedTerminalHandshakeAsync", "Handshake service must refresh trusted terminal sessions.");
        AssertContains(handshakeService, "TerminalRegistrationStatus.Active", "Handshake refresh must require active terminal registration.");

        AssertContains(sessionService, "RemotePos.HandshakeInvalid", "Backend must reject expired handshake on restore.");
        AssertContains(sessionService, "RemotePos.TerminalNotAccepted", "Backend must reject revoked/disabled terminal on restore.");

        AssertContains(app, "isRemoteClientBrowserLocation", "App must separate remote client entry from host console.");
        AssertRemoteClientDoesNotExposeHostCheckout(app);

        AssertNotContains(endpoints, "0.0.0.0", "Remote POS surface must not introduce wildcard bind.");
        AssertNotContains(endpoints, "/api/lan-control", "Remote POS surface must not expose /api/lan-control.");

        AssertContains(coreSmoke, "v2-lan-client-pairing", "CoreSmoke must expose v2-lan-client-pairing runner.");

        AssertContains(infraSmoke, "flow-reset-dev-seed", "InfrastructureSmoke must expose flow-reset-dev-seed command.");
        AssertContains(infraSmoke, "FlowResetDevSeedChecks.Run", "InfrastructureSmoke must route flow-reset-dev-seed to checks.");
        AssertContains(flowResetChecks, ".salryn-dev", "Flow reset must target development database root only.");
        AssertContains(flowResetChecks, "salryn-dev-", "Flow reset must archive existing dev database before reset.");
        AssertContains(flowResetChecks, "cashier@salryn.local", "Flow reset must seed known dev cashier credentials.");
        AssertContains(flowResetScript, "flow-reset-dev-seed", "Dev reset script must invoke flow-reset-dev-seed.");
    }

    private static void AssertPairingRecordExcludesSessionToken(string storageSource)
    {
        const string marker = "export type LanClientPairingRecord";
        int start = storageSource.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Could not locate LanClientPairingRecord type for session-token guard.");
        }

        int end = storageSource.IndexOf("};", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Could not isolate LanClientPairingRecord type for session-token guard.");
        }

        string recordType = storageSource.Substring(start, end - start);
        if (recordType.Contains("sessionToken", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LAN pairing record must not include operator session token.");
        }
    }

    private static void AssertRemoteClientDoesNotExposeHostCheckout(string appSource)
    {
        const string remoteMarker = "function RemoteClientApp()";
        const string consoleMarker = "function ConsoleApp()";

        int remoteStart = appSource.IndexOf(remoteMarker, StringComparison.Ordinal);
        int consoleStart = appSource.IndexOf(consoleMarker, StringComparison.Ordinal);

        if (remoteStart < 0 || consoleStart < 0 || consoleStart <= remoteStart)
        {
            throw new InvalidOperationException("Could not isolate RemoteClientApp for checkout boundary guard.");
        }

        string remoteSection = appSource.Substring(remoteStart, consoleStart - remoteStart);

        if (remoteSection.Contains("CheckoutPage", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Remote client shell must not expose host CheckoutPage.");
        }
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

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file not found: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}
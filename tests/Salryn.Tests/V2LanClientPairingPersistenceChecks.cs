namespace Salryn.Tests;

public static class V2LanClientPairingPersistenceChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string storage = ReadRequiredFile(Path.Combine(webRoot, "utils", "lanClientStorage.ts"));
        string hook = ReadRequiredFile(Path.Combine(webRoot, "hooks", "useRemotePosClient.ts"));
        string page = ReadRequiredFile(Path.Combine(webRoot, "pages", "RemotePosClientPage.tsx"));
        string remotePosClient = ReadRequiredFile(Path.Combine(webRoot, "api", "remotePosClient.ts"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));
        string endpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs"));
        string contracts = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosPairingContracts.cs"));
        string sessionService = ReadRequiredFile(Path.Combine(apiRoot, "RemotePos", "RemotePosOperatorSessionService.cs"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        AssertContains(storage, "salryn.lanClient.v1", "LAN client storage must use a versioned key.");
        AssertContains(storage, "version: 1", "LAN client storage record must be versioned.");
        AssertContains(storage, "localStorage", "LAN client pairing must use durable browser storage.");
        AssertContains(storage, "clearLanClientPairing", "LAN client storage must expose clear pairing.");
        AssertContains(storage, "hasPairedTerminalMaterial", "LAN client storage must gate paired terminal material.");

        AssertContains(hook, "readLanClientPairing", "Remote POS hook must read stored pairing on load.");
        AssertContains(hook, "writeLanClientPairing", "Remote POS hook must persist pairing material.");
        AssertContains(hook, "clearLanClientPairing", "Remote POS hook must clear stored pairing.");
        AssertContains(hook, "fetchRemotePosTerminalTrust", "Remote POS hook must probe backend terminal trust on restore.");
        AssertContains(hook, "restoreState", "Remote POS hook must expose restore state.");
        AssertContains(hook, "pairingRestored", "Remote POS hook must expose pairing restored flag.");
        AssertContains(hook, "clearPairing", "Remote POS hook must expose clear pairing action.");
        AssertContains(hook, "terminalIdSuffix", "Remote POS hook must expose terminal id suffix.");
        AssertContains(hook, "Pairing revoked or expired. Re-pair this device.", "Restore must clear revoked pairing and show re-pair message.");
        AssertNotContains(hook, "localStorage.setItem", "Hook must route storage through lanClientStorage helper.");
        AssertNotContains(storage, "password", "Storage shape must not include operator password.");
        AssertNotContains(storage, "sessionToken", "Storage shape must not include operator session token.");

        AssertContains(page, "Paired terminal restored", "Remote POS page must show restored pairing banner.");
        AssertContains(page, "Clear this device pairing", "Remote POS page must expose clear pairing action.");
        AssertContains(page, "Pairing revoked or expired", "Remote POS page must show revoked pairing message.");
        AssertContains(page, "terminalIdSuffix", "Remote POS page must show terminal id suffix.");
        AssertNotContains(page, "\"/api/checkout\"", "Remote POS page must not call host /api/checkout.");

        AssertContains(remotePosClient, "/api/remote-pos/terminal/trust", "Remote POS client must call terminal trust probe.");
        AssertContains(endpoints, "/terminal/trust", "Remote POS API must expose terminal trust probe.");
        AssertContains(contracts, "RemotePosTerminalTrustResponse", "Remote POS contracts must include terminal trust response.");

        AssertContains(sessionService, "RemotePos.HandshakeInvalid", "Backend must reject expired handshake on restore.");
        AssertContains(sessionService, "RemotePos.TerminalNotAccepted", "Backend must reject revoked/disabled terminal on restore.");

        AssertContains(app, "isRemoteClientBrowserLocation", "App must separate remote client entry from host console.");
        AssertRemoteClientDoesNotExposeHostCheckout(app);

        AssertNotContains(endpoints, "0.0.0.0", "Remote POS surface must not introduce wildcard bind.");
        AssertNotContains(endpoints, "/api/lan-control", "Remote POS surface must not expose /api/lan-control.");

        AssertContains(coreSmoke, "v2-lan-client-pairing", "CoreSmoke must expose v2-lan-client-pairing runner.");
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
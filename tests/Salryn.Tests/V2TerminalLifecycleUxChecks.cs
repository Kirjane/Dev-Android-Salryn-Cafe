namespace Salryn.Tests;

public static class V2TerminalLifecycleUxChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");

        string lifecycleUx = ReadRequiredFile(Path.Combine(webRoot, "utils", "terminalLifecycleUx.ts"));
        string hostPage = ReadRequiredFile(Path.Combine(webRoot, "pages", "HostLanControlPage.tsx"));
        string hook = ReadRequiredFile(Path.Combine(webRoot, "hooks", "useRemotePosClient.ts"));
        string storage = ReadRequiredFile(Path.Combine(webRoot, "utils", "lanClientStorage.ts"));
        string app = ReadRequiredFile(Path.Combine(webRoot, "App.tsx"));
        string remotePosEndpoints = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs"));
        string remotePosClient = ReadRequiredFile(Path.Combine(webRoot, "api", "remotePosClient.ts"));
        string registry = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalRegistryService.cs"));

        AssertContains(lifecycleUx, "canDisableTerminal", "Terminal lifecycle UX must gate disable by status.");
        AssertContains(lifecycleUx, "canRevokeTerminal", "Terminal lifecycle UX must gate revoke by status.");
        AssertContains(lifecycleUx, "isRevokedTerminalReadOnly", "Terminal lifecycle UX must treat revoked terminals as read-only.");
        AssertContains(lifecycleUx, "status === \"Active\"", "Disable must be limited to active terminals.");
        AssertContains(lifecycleUx, "Revoked", "Terminal lifecycle UX must model revoked status.");
        AssertContains(lifecycleUx, "must request access again", "Revoked explanation must tell operator client must request access again.");
        AssertContains(lifecycleUx, "Re-enable is not supported", "Disabled explanation must state re-enable is unsupported.");

        AssertContains(hostPage, "terminalLifecycleUx", "Host LAN Control must use terminal lifecycle helpers.");
        AssertContains(hostPage, "TerminalControlRow", "Host LAN Control must render status-aware terminal rows.");
        AssertContains(hostPage, "canDisableTerminal", "Host LAN Control must conditionally show disable.");
        AssertContains(hostPage, "canRevokeTerminal", "Host LAN Control must conditionally show revoke.");
        AssertContains(hostPage, "formatTerminalRoleLabel", "Host LAN Control must show clear terminal role labels.");
        AssertNotContains(hostPage, "onClick={() => disableTerminalMutation.mutate(terminal.terminalId)}", "Host LAN Control must not unconditionally show disable on every terminal row.");

        AssertContains(hook, "Previous pairing was revoked. Request access again.", "Remote POS client must show revoked-specific re-pair notice.");
        AssertContains(hook, "clearRemotePosOperatorSessionToken", "Revoked/stale restore must clear operator session token.");
        AssertContains(hook, "S1_UNPAIRED", "Revoked client must return to unpaired flow.");
        AssertContains(hook, "requestJoin", "Revoked client must keep request access available.");
        AssertNotContains(hook, "localStorage.setItem", "Hook must not store operator password or bypass storage helpers.");

        AssertContains(storage, "sessionStorage", "Operator session token must use sessionStorage.");
        AssertNotContains(storage, "password", "LAN client storage must not store operator password.");
        AssertPairingRecordExcludesSessionToken(storage);

        AssertRemoteClientDoesNotExposeHostCheckout(app);
        AssertNotContains(remotePosEndpoints, "0.0.0.0", "Remote POS surface must not introduce wildcard bind.");
        AssertNotContains(remotePosEndpoints, "/api/lan-control", "Remote POS surface must not expose /api/lan-control.");
        AssertNotContains(remotePosClient, "/api/lan-control", "Remote POS client must not call /api/lan-control.");

        AssertContains(registry, "RevokeTerminalAsync", "Terminal registry must support revoke.");
        AssertNotContains(registry, "EnableTerminalAsync", "Terminal registry must not fake re-enable from revoked.");
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
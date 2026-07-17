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
        string lanControlService = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.Api.Local", "Services", "LocalLanControlService.cs"));
        string lanEndpoints = ReadRequiredFile(Path.Combine(projectRoot, "src", "Salryn.Api.Local", "LocalLanControlEndpoints.cs"));
        string apiClient = ReadRequiredFile(Path.Combine(webRoot, "api", "remoteReadOnlyClient.ts"));

        AssertContains(lifecycleUx, "canDisableTerminal", "Terminal lifecycle UX must gate disable by status.");
        AssertContains(lifecycleUx, "canRevokeTerminal", "Terminal lifecycle UX must gate revoke/remove by status.");
        AssertContains(lifecycleUx, "canRemoveTerminal", "Terminal lifecycle UX must gate Remove terminal by Disabled status.");
        AssertContains(lifecycleUx, "canEnableDisabledTerminal", "Terminal lifecycle UX must gate Enable for Disabled only.");
        AssertContains(lifecycleUx, "isRevokedTerminalReadOnly", "Terminal lifecycle UX must treat revoked terminals as read-only.");
        AssertContains(lifecycleUx, "status === \"Active\"", "Disable must be limited to active terminals.");
        AssertContains(lifecycleUx, "status === \"Disabled\"", "Remove/Enable must key off Disabled status.");
        AssertContains(lifecycleUx, "Revoked", "Terminal lifecycle UX must model revoked status.");
        AssertContains(lifecycleUx, "must request access again", "Revoked explanation must tell operator client must request access again.");
        AssertContains(lifecycleUx, "canHideTerminalFromList", "Lifecycle UX must allow hide only for non-active rows.");
        AssertContains(lifecycleUx, "isHostLocalTerminalRole", "Lifecycle UX must classify host-local admin rows.");
        AssertContains(lifecycleUx, "terminalConnectionGroup", "Lifecycle UX must group terminal connections.");
        AssertNotContains(lifecycleUx, "Re-enable is not supported", "Lifecycle must not claim enable is unsupported.");

        AssertContains(hostPage, "terminalLifecycleUx", "Host LAN Control must use terminal lifecycle helpers.");
        AssertContains(hostPage, "TerminalControlRow", "Host LAN Control must render status-aware terminal rows.");
        AssertContains(hostPage, "canDisableTerminal", "Host LAN Control must conditionally show disable.");
        AssertContains(hostPage, "canRemoveTerminal", "Host LAN Control must conditionally show remove.");
        AssertContains(hostPage, "canEnableDisabledTerminal", "Host LAN Control must conditionally show enable.");
        AssertContains(hostPage, "formatTerminalRoleLabel", "Host LAN Control must show clear terminal role labels.");
        AssertContains(hostPage, "terminalGroupTitle", "Host LAN Control must render grouped terminal section titles.");
        AssertContains(lifecycleUx, "Active terminals", "Lifecycle UX must define active terminal group title.");
        AssertContains(lifecycleUx, "Blocked terminals", "Lifecycle UX must define blocked terminal group title.");
        AssertContains(lifecycleUx, "Retired / removed terminals", "Lifecycle UX must define retired terminal group title.");
        AssertContains(lifecycleUx, "Local host / admin clients", "Lifecycle UX must define host-local group title.");
        AssertContains(hostPage, "Hide from list", "Host LAN Control must allow hide for retired/blocked rows.");
        AssertContains(hostPage, "Clear operator sessions", "Host LAN Control must offer clear sessions when supported.");
        AssertContains(hostPage, "Disable terminal", "Active path must label Disable terminal.");
        AssertContains(hostPage, "Enable terminal", "Disabled path must label Enable terminal.");
        AssertContains(hostPage, "Remove terminal", "Disabled path must label Remove terminal.");
        AssertContains(hostPage, "pendingDisableTerminal", "Disable must use confirmation state.");
        AssertContains(hostPage, "pendingEnableTerminal", "Enable must use confirmation state.");
        AssertContains(hostPage, "pendingRemoveTerminal", "Remove must use confirmation state.");
        AssertContains(hostPage, "Disable terminal?", "Disable confirmation title required.");
        AssertContains(hostPage, "Remove terminal?", "Remove confirmation title required.");
        AssertContains(hostPage, "ConfirmDialog", "Lifecycle actions must use ConfirmDialog.");
        AssertContains(hostPage, "Host-local admin/console row is read-only", "Host-local admin row must be read-only.");
        AssertNotContains(hostPage, "onClick={() => disableTerminalMutation.mutate(terminal.terminalId)}", "Disable must not fire immediately from row button.");
        AssertNotContains(hostPage, "Restore (not supported)", "Must not show fake Restore not supported control.");
        AssertNotContains(hostPage, "Re-enable is not supported", "Must not claim re-enable unsupported.");
        // Primary visible label must not be bare "Revoke" button text.
        AssertNotContains(hostPage, ">Revoke<", "Primary visible label must not be Revoke.");
        AssertNotContains(hostPage, ">\n              Revoke\n            <", "Primary visible label must not be Revoke.");

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

        AssertContains(registry, "RevokeTerminalAsync", "Terminal registry must support revoke/remove.");
        AssertContains(registry, "EnableTerminalAsync", "Terminal registry must support enable for Disabled terminals.");
        AssertContains(registry, "CannotEnableRevoked", "Enable must refuse Revoked terminals.");
        AssertContains(registry, "EnableRequiresDisabled", "Enable must require Disabled status.");
        AssertContains(lanControlService, "EnableTerminalAsync", "LAN control service must expose enable.");
        AssertContains(lanEndpoints, "/terminals/{terminalId}/enable", "LAN control must expose enable endpoint.");
        AssertContains(apiClient, "/enable", "Web client must call enable endpoint.");
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

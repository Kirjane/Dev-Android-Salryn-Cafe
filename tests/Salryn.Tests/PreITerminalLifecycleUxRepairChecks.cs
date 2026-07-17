namespace Salryn.Tests;

/// <summary>
/// PRE-I: Terminal Management lifecycle UX — Disable / Enable / Remove with confirmation guards.
/// </summary>
public static class PreITerminalLifecycleUxRepairChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string hostPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
        string lifecycle = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "utils", "terminalLifecycleUx.ts"));
        string hook = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "hooks", "useLocalLanControl.ts"));
        string api = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "api", "remoteReadOnlyClient.ts"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalRegistryService.cs"));
        string lanService = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "LocalLanControlService.cs"));
        string endpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalLanControlEndpoints.cs"));
        string iface = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Services", "ITerminalRegistryService.cs"));

        // 1 Active: Disable terminal
        RequireContains(hostPage, "Disable terminal", "Active terminal row shows Disable terminal.");
        RequireContains(lifecycle, "status === \"Active\"", "Disable gated to Active.");
        RequireContains(lifecycle, "canDisableTerminal", "Disable gate helper.");

        // 2 Active does not show Remove (Remove only Disabled)
        RequireContains(lifecycle, "export function canRemoveTerminal", "Remove helper.");
        RequireContains(lifecycle, "return status === \"Disabled\" && isManagedRemoteLanTerminal(role);", "Remove terminal only when Disabled.");

        // 3 Disable confirmation
        RequireContains(hostPage, "pendingDisableTerminal", "Disable confirmation state.");
        RequireContains(hostPage, "Disable terminal?", "Disable confirmation title.");
        RequireContains(hostPage, "This blocks this terminal from using Remote POS", "Disable confirmation body.");
        RequireContains(hostPage, "Keep active", "Disable cancel label.");
        RequireNotContains(hostPage, "onClick={() => disableTerminalMutation.mutate(terminal.terminalId)}", "Disable does not mutate immediately from row.");

        // 4 Disabled: Enable terminal
        RequireContains(hostPage, "Enable terminal", "Disabled row shows Enable terminal.");
        RequireContains(hostPage, "pendingEnableTerminal", "Enable confirmation state.");
        RequireContains(lifecycle, "canEnableDisabledTerminal", "Enable gate helper.");
        RequireContains(api, "/enable", "Client calls enable endpoint.");
        RequireContains(hook, "enableDisabledTerminalMutation", "Hook exposes enable mutation.");
        RequireContains(endpoints, "/terminals/{terminalId}/enable", "API enable route.");
        RequireContains(lanService, "EnableTerminalAsync", "LAN service enable.");
        RequireContains(registry, "EnableTerminalAsync", "Registry enable.");
        RequireContains(iface, "EnableTerminalAsync", "Registry interface enable.");
        RequireContains(registry, "CannotEnableRevoked", "Enable refuses Revoked.");
        RequireContains(registry, "EnableRequiresDisabled", "Enable requires Disabled.");

        // 5 Disabled: Remove terminal
        RequireContains(hostPage, "Remove terminal", "Disabled row shows Remove terminal.");
        RequireContains(hostPage, "pendingRemoveTerminal", "Remove confirmation state.");
        RequireContains(hostPage, "Remove terminal?", "Remove confirmation title.");
        RequireContains(hostPage, "This permanently removes this terminal trust from the Host", "Remove confirmation body.");
        RequireContains(hostPage, "Keep disabled", "Remove cancel label.");
        RequireContains(hostPage, "revokeTerminalMutation.mutate", "Remove uses revoke semantics under the hood.");

        // 6 No fake restore
        RequireNotContains(hostPage, "Restore (not supported)", "No fake Restore not supported.");
        RequireNotContains(hostPage, "Re-enable is not supported", "No unsupported re-enable copy.");

        // 7 Primary label not Revoke
        RequireNotContains(hostPage, ">Revoke<", "No primary Revoke label.");
        RequireNotContains(hostPage, "\n              Revoke\n", "No Revoke button text.");

        // 8 Clear sessions secondary
        RequireContains(hostPage, "Clear operator sessions", "Clear sessions retained.");
        RequireContains(hostPage, "variant=\"ghost\"", "Secondary actions use ghost variant.");

        // 9 ConfirmDialog
        RequireContains(hostPage, "ConfirmDialog", "Uses ConfirmDialog component.");
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

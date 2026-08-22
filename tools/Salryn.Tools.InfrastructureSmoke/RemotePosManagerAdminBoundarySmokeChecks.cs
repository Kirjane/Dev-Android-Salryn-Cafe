using Salryn.Api.Local.RemotePos;
using Salryn.Core.Security;

// LAN-M16 remote-pos-manager-admin-boundary smoke: proves manager/admin remote power is explicit
// and bounded. Manager overrides are NotSupported (no safe remote domain path); admin is not an
// automatic host (host-local controls are never authorized over LAN); admin reads require the
// mapped permission. There is no accidental "admin can do everything" path.
internal static class RemotePosManagerAdminBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M16 REMOTE POS MANAGER/ADMIN BOUNDARY SMOKE CHECKS =====");

        string[] managerPermissions =
        {
            SalrynPermissions.SalesCreate,
            SalrynPermissions.ShiftOpenClose,
            SalrynPermissions.SalesVoid,
        };

        // Even a very broad "admin" permission set never unlocks host-local controls over LAN.
        string[] adminPermissions =
        {
            SalrynPermissions.SalesCreate,
            SalrynPermissions.ShiftOpenClose,
            SalrynPermissions.SalesVoid,
            SalrynPermissions.AuditView,
            SalrynPermissions.SettingsManage,
            SalrynPermissions.BackupManage,
            SalrynPermissions.UsersManage,
            SalrynPermissions.StoreTerminalManage,
        };

        // Manager override has no safe remote domain path -> NotSupported for manager AND admin.
        RequireDecision(RemotePosCommands.ManagerOverride, managerPermissions, RemotePosAuthorizationDecision.NotSupported);
        RequireDecision(RemotePosCommands.ManagerOverride, adminPermissions, RemotePosAuthorizationDecision.NotSupported);

        // Admin status read requires Audit.View specifically; SettingsManage alone does not grant it.
        RequireDecision(RemotePosCommands.AdminStatus, new[] { SalrynPermissions.SettingsManage }, RemotePosAuthorizationDecision.PermissionDenied);
        RequireDecision(RemotePosCommands.AdminStatus, adminPermissions, RemotePosAuthorizationDecision.Allowed);

        // Host-local control verbs are not part of the remote command surface at all -> UnknownCommand,
        // regardless of how privileged the operator's permission set is.
        RequireDecision("remote-pos.lan-control.enable", adminPermissions, RemotePosAuthorizationDecision.UnknownCommand);
        RequireDecision("remote-pos.terminal.revoke", adminPermissions, RemotePosAuthorizationDecision.UnknownCommand);
        RequireDecision("remote-pos.backup.run", adminPermissions, RemotePosAuthorizationDecision.UnknownCommand);
        RequireDecision("remote-pos.settings.update", adminPermissions, RemotePosAuthorizationDecision.UnknownCommand);
        RequireDecision("remote-pos.official.output", adminPermissions, RemotePosAuthorizationDecision.UnknownCommand);

        // The advertised host-local-only list documents exactly what stays on the host.
        IReadOnlyList<string> hostLocalOnly = RemotePosAuthorizationMatrix.HostLocalOnlyActions();
        RequireListContains(hostLocalOnly, "lan-control", "LAN control");
        RequireListContains(hostLocalOnly, "Terminal disable/revoke", "terminal trust management");
        RequireListContains(hostLocalOnly, "Backup", "backup/recovery");
        RequireListContains(hostLocalOnly, "Official output", "official output devices");
        RequireListContains(hostLocalOnly, "Role / permission editor", "role/permission editor");

        Console.WriteLine("LAN-M16 remote POS manager/admin boundary smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireDecision(string command, IReadOnlyCollection<string> permissions, RemotePosAuthorizationDecision expected)
    {
        RemotePosCommandAuthorization decision = RemotePosAuthorizationMatrix.Evaluate(command, permissions);
        RemotePosSmokeSupport.Require(
            decision.Decision == expected,
            $"Command '{command}' expected {expected} but got {decision.Decision} ({decision.Code}).");
    }

    private static void RequireListContains(IReadOnlyList<string> list, string fragment, string label)
    {
        RemotePosSmokeSupport.Require(
            list.Any(item => item.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            $"Host-local-only actions must document {label}.");
    }
}

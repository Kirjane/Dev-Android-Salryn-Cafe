using Salryn.Api.Local.RemotePos;
using Salryn.Core.Security;

// LAN-M16 remote-pos-command-authorization smoke: exercises the real role authorization matrix.
// Terminal + handshake + operator session are necessary but NOT sufficient; every command is
// additionally gated on the operator's permission snapshot. Unknown commands are rejected and
// unsafe manager/admin write actions are explicitly NotSupported rather than silently succeeding.
internal static class RemotePosCommandAuthorizationSmokeChecks
{
    private static readonly string[] CashierPermissions =
    {
        SalrynPermissions.SalesCreate,
        SalrynPermissions.ShiftOpenClose,
    };

    private static readonly string[] ManagerPermissions =
    {
        SalrynPermissions.SalesCreate,
        SalrynPermissions.ShiftOpenClose,
        SalrynPermissions.SalesVoid,
    };

    private static readonly string[] AdminPermissions =
    {
        SalrynPermissions.SalesCreate,
        SalrynPermissions.ShiftOpenClose,
        SalrynPermissions.AuditView,
        SalrynPermissions.SettingsManage,
    };

    private static readonly string[] NoPermissions = Array.Empty<string>();

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M16 REMOTE POS COMMAND AUTHORIZATION SMOKE CHECKS =====");

        // Reads only need an authenticated session.
        RequireDecision(RemotePosCommands.SessionRead, NoPermissions, RemotePosAuthorizationDecision.Allowed);
        RequireDecision(RemotePosCommands.Capabilities, NoPermissions, RemotePosAuthorizationDecision.Allowed);
        RequireDecision(RemotePosCommands.CartView, NoPermissions, RemotePosAuthorizationDecision.Allowed);
        RequireDecision(RemotePosCommands.ShiftRead, NoPermissions, RemotePosAuthorizationDecision.Allowed);
        RequireDecision(RemotePosCommands.CheckoutStatus, NoPermissions, RemotePosAuthorizationDecision.Allowed);

        // Cart/checkout writes require Sales.Create.
        RequireDecision(RemotePosCommands.CartStart, NoPermissions, RemotePosAuthorizationDecision.PermissionDenied);
        RequireDecision(RemotePosCommands.CartAddLine, CashierPermissions, RemotePosAuthorizationDecision.Allowed);
        RequireDecision(RemotePosCommands.CheckoutSubmit, NoPermissions, RemotePosAuthorizationDecision.PermissionDenied);
        RequireDecision(RemotePosCommands.CheckoutSubmit, CashierPermissions, RemotePosAuthorizationDecision.Allowed);

        // Shift writes require Shift.OpenClose.
        RequireDecision(RemotePosCommands.ShiftOpen, new[] { SalrynPermissions.SalesCreate }, RemotePosAuthorizationDecision.PermissionDenied);
        RequireDecision(RemotePosCommands.ShiftOpen, CashierPermissions, RemotePosAuthorizationDecision.Allowed);

        // Admin status read requires Audit.View; without it even an admin-ish set is denied.
        RequireDecision(RemotePosCommands.AdminStatus, CashierPermissions, RemotePosAuthorizationDecision.PermissionDenied);
        RequireDecision(RemotePosCommands.AdminStatus, AdminPermissions, RemotePosAuthorizationDecision.Allowed);

        // Manager override has no safe remote domain path: NotSupported for EVERY role (no accidental power).
        RequireDecision(RemotePosCommands.ManagerOverride, ManagerPermissions, RemotePosAuthorizationDecision.NotSupported);
        RequireDecision(RemotePosCommands.ManagerOverride, AdminPermissions, RemotePosAuthorizationDecision.NotSupported);

        // Unknown commands are rejected.
        RequireDecision("remote-pos.settings.change", AdminPermissions, RemotePosAuthorizationDecision.UnknownCommand);
        RequireDecision("remote-pos.lan-control.enable", AdminPermissions, RemotePosAuthorizationDecision.UnknownCommand);

        RequireCapabilities();
        RequireHostLocalOnly();

        Console.WriteLine("LAN-M16 remote POS command authorization smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireDecision(string command, IReadOnlyCollection<string> permissions, RemotePosAuthorizationDecision expected)
    {
        RemotePosCommandAuthorization decision = RemotePosAuthorizationMatrix.Evaluate(command, permissions);
        RemotePosSmokeSupport.Require(
            decision.Decision == expected,
            $"Command '{command}' expected {expected} but got {decision.Decision} ({decision.Code}).");
    }

    private static void RequireCapabilities()
    {
        IReadOnlyList<RemotePosCapabilityResponse> cashierCaps = RemotePosAuthorizationMatrix.BuildCapabilities(CashierPermissions);
        RemotePosSmokeSupport.Require(cashierCaps.Count > 0, "Capabilities must be advertised.");

        RemotePosCapabilityResponse checkout = cashierCaps.First(c => c.Command == RemotePosCommands.CheckoutSubmit);
        RemotePosSmokeSupport.Require(checkout.Allowed, "Cashier must be allowed to submit checkout.");

        RemotePosCapabilityResponse managerOverride = cashierCaps.First(c => c.Command == RemotePosCommands.ManagerOverride);
        RemotePosSmokeSupport.Require(!managerOverride.Allowed, "Manager override must never be advertised as allowed.");
        RemotePosSmokeSupport.Require(managerOverride.Decision == RemotePosAuthorizationDecision.NotSupported.ToString(),
            "Manager override capability decision must be NotSupported.");

        IReadOnlyList<RemotePosCapabilityResponse> noPermCaps = RemotePosAuthorizationMatrix.BuildCapabilities(NoPermissions);
        RemotePosCapabilityResponse deniedCheckout = noPermCaps.First(c => c.Command == RemotePosCommands.CheckoutSubmit);
        RemotePosSmokeSupport.Require(!deniedCheckout.Allowed, "No-permission operator must not be allowed to checkout.");
    }

    private static void RequireHostLocalOnly()
    {
        IReadOnlyList<string> hostLocalOnly = RemotePosAuthorizationMatrix.HostLocalOnlyActions();
        RemotePosSmokeSupport.Require(hostLocalOnly.Count > 0, "Host-local-only actions must be advertised.");
        RemotePosSmokeSupport.Require(
            hostLocalOnly.Any(a => a.Contains("lan-control", StringComparison.OrdinalIgnoreCase)),
            "Host-local-only actions must include LAN control.");
        RemotePosSmokeSupport.Require(
            hostLocalOnly.Any(a => a.Contains("Backup", StringComparison.OrdinalIgnoreCase)),
            "Host-local-only actions must include backup/recovery.");
    }
}

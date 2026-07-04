using Salryn.Core.Security;

namespace Salryn.Api.Local.RemotePos;

public enum RemotePosAuthorizationDecision
{
    Allowed = 1,
    PermissionDenied = 2,
    NotSupported = 3,
    UnknownCommand = 4,
}

public sealed record RemotePosCommandAuthorization(
    string Command,
    RemotePosCommandCategory Category,
    RemotePosAuthorizationDecision Decision,
    string Code,
    string Message);

/// <summary>
/// LAN-M16 role authorization matrix. Terminal identity + handshake proof + a valid host-issued
/// operator session are necessary but NOT sufficient: every remote POS command is additionally
/// gated on the operator's role/permission snapshot here. Unknown commands are rejected. Actions
/// with no safe host-side domain path (manager overrides, void/refund/discount) return an explicit
/// NotSupported code rather than silently succeeding. Host-local controls (LAN bind, terminal
/// trust management, settings, backup, official output devices) are never authorized over LAN.
/// </summary>
public static class RemotePosAuthorizationMatrix
{
    // Kirch: why - reads only require an authenticated operator session; writes require the mapped
    // domain permission. Manager/admin write actions have no safe remote domain path yet.
    public static RemotePosCommandCategory? TryResolveCategory(string command) => command switch
    {
        RemotePosCommands.SessionRead => RemotePosCommandCategory.SessionRead,
        RemotePosCommands.Logout => RemotePosCommandCategory.SessionRead,
        RemotePosCommands.Capabilities => RemotePosCommandCategory.CapabilitiesRead,
        RemotePosCommands.CartView => RemotePosCommandCategory.CartRead,
        RemotePosCommands.ShiftRead => RemotePosCommandCategory.ShiftRead,
        RemotePosCommands.CheckoutStatus => RemotePosCommandCategory.CheckoutStatusRead,
        RemotePosCommands.AdminStatus => RemotePosCommandCategory.AdminStatusRead,

        RemotePosCommands.CartStart => RemotePosCommandCategory.CartWrite,
        RemotePosCommands.CartAddLine => RemotePosCommandCategory.CartWrite,
        RemotePosCommands.CartUpdateLine => RemotePosCommandCategory.CartWrite,
        RemotePosCommands.CartRemoveLine => RemotePosCommandCategory.CartWrite,
        RemotePosCommands.CartClear => RemotePosCommandCategory.CartWrite,

        RemotePosCommands.ShiftOpen => RemotePosCommandCategory.ShiftWrite,
        RemotePosCommands.ShiftClose => RemotePosCommandCategory.ShiftWrite,

        RemotePosCommands.CheckoutSubmit => RemotePosCommandCategory.CheckoutWrite,

        RemotePosCommands.ManagerOverride => RemotePosCommandCategory.ManagerAction,
        _ => null,
    };

    public static RemotePosCommandAuthorization Evaluate(
        string command,
        IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        RemotePosCommandCategory? category = TryResolveCategory(command);

        if (category is null)
        {
            return new RemotePosCommandAuthorization(
                command,
                RemotePosCommandCategory.SessionRead,
                RemotePosAuthorizationDecision.UnknownCommand,
                "RemotePos.UnknownCommand",
                "Requested remote POS command is not recognized.");
        }

        return category.Value switch
        {
            // Any authenticated operator session may read its own session/capabilities/cart/shift/status.
            RemotePosCommandCategory.SessionRead
            or RemotePosCommandCategory.CapabilitiesRead
            or RemotePosCommandCategory.CartRead
            or RemotePosCommandCategory.ShiftRead
            or RemotePosCommandCategory.CheckoutStatusRead =>
                Allow(command, category.Value),

            RemotePosCommandCategory.AdminStatusRead =>
                RequirePermission(command, category.Value, permissions, SalrynPermissions.AuditView),

            RemotePosCommandCategory.CartWrite =>
                RequirePermission(command, category.Value, permissions, SalrynPermissions.SalesCreate),

            RemotePosCommandCategory.ShiftWrite =>
                RequirePermission(command, category.Value, permissions, SalrynPermissions.ShiftOpenClose),

            RemotePosCommandCategory.CheckoutWrite =>
                RequirePermission(command, category.Value, permissions, SalrynPermissions.SalesCreate),

            // Manager overrides (void/refund/discount) have no safe remote domain path in this build.
            RemotePosCommandCategory.ManagerAction =>
                NotSupported(
                    command,
                    category.Value,
                    "RemotePos.ManagerOverrideNotSupported",
                    "Remote manager overrides (void/refund/discount) are not supported over LAN in this build."),

            // Host-local admin actions are never authorized over LAN.
            RemotePosCommandCategory.AdminAction =>
                NotSupported(
                    command,
                    category.Value,
                    "RemotePos.AdminActionHostLocalOnly",
                    "This administrative action is host-local only and is never authorized over LAN."),

            _ => NotSupported(
                command,
                category.Value,
                "RemotePos.NotSupported",
                "Requested remote POS action is not supported."),
        };
    }

    public static IReadOnlyList<RemotePosCapabilityResponse> BuildCapabilities(
        IReadOnlyCollection<string> permissions)
    {
        string[] advertisedCommands =
        {
            RemotePosCommands.SessionRead,
            RemotePosCommands.Capabilities,
            RemotePosCommands.CartView,
            RemotePosCommands.CartStart,
            RemotePosCommands.CartAddLine,
            RemotePosCommands.CartUpdateLine,
            RemotePosCommands.CartRemoveLine,
            RemotePosCommands.CartClear,
            RemotePosCommands.ShiftRead,
            RemotePosCommands.ShiftOpen,
            RemotePosCommands.ShiftClose,
            RemotePosCommands.CheckoutSubmit,
            RemotePosCommands.CheckoutStatus,
            RemotePosCommands.ManagerOverride,
            RemotePosCommands.AdminStatus,
        };

        List<RemotePosCapabilityResponse> capabilities = new();

        foreach (string command in advertisedCommands)
        {
            RemotePosCommandAuthorization authorization = Evaluate(command, permissions);

            capabilities.Add(new RemotePosCapabilityResponse(
                command,
                authorization.Category.ToString(),
                authorization.Decision == RemotePosAuthorizationDecision.Allowed,
                authorization.Decision.ToString(),
                DescribeCommand(command)));
        }

        return capabilities;
    }

    public static IReadOnlyList<string> HostLocalOnlyActions() => new[]
    {
        "LAN bind enable/disable (/api/lan-control)",
        "Terminal disable/revoke (/api/lan-control)",
        "Remote POS runtime enable/disable (/api/lan-control)",
        "Database path / connection string visibility",
        "Backup / recovery",
        "Official output devices (printer/PDF/receipt hardware)",
        "Role / permission editor",
    };

    private static RemotePosCommandAuthorization Allow(string command, RemotePosCommandCategory category) =>
        new(command, category, RemotePosAuthorizationDecision.Allowed, "RemotePos.Allowed", "Allowed.");

    private static RemotePosCommandAuthorization RequirePermission(
        string command,
        RemotePosCommandCategory category,
        IReadOnlyCollection<string> permissions,
        string requiredPermission)
    {
        if (permissions.Contains(requiredPermission))
        {
            return Allow(command, category);
        }

        return new RemotePosCommandAuthorization(
            command,
            category,
            RemotePosAuthorizationDecision.PermissionDenied,
            "RemotePos.PermissionDenied",
            "Operator role does not have permission for this remote POS command.");
    }

    private static RemotePosCommandAuthorization NotSupported(
        string command,
        RemotePosCommandCategory category,
        string code,
        string message) =>
        new(command, category, RemotePosAuthorizationDecision.NotSupported, code, message);

    private static string DescribeCommand(string command) => command switch
    {
        RemotePosCommands.SessionRead => "Read the current remote operator session.",
        RemotePosCommands.Capabilities => "Read the role capability matrix.",
        RemotePosCommands.CartView => "View the current remote cart.",
        RemotePosCommands.CartStart => "Start a new remote cart.",
        RemotePosCommands.CartAddLine => "Add a product line to the remote cart.",
        RemotePosCommands.CartUpdateLine => "Update a product line quantity.",
        RemotePosCommands.CartRemoveLine => "Remove a product line from the cart.",
        RemotePosCommands.CartClear => "Clear the remote cart.",
        RemotePosCommands.ShiftRead => "Read the operator's open cashier shift.",
        RemotePosCommands.ShiftOpen => "Open a cashier shift on the host for this operator.",
        RemotePosCommands.ShiftClose => "Close the operator's open cashier shift.",
        RemotePosCommands.CheckoutSubmit => "Submit the cart as a host-owned sale (idempotent).",
        RemotePosCommands.CheckoutStatus => "Read the result of a submitted checkout by client request id.",
        RemotePosCommands.ManagerOverride => "Manager override (not supported over LAN in this build).",
        RemotePosCommands.AdminStatus => "Read administrative remote status (requires audit permission).",
        _ => command,
    };
}

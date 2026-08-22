using Salryn.Api.Local.RemotePos;
using Salryn.Core.Security;

namespace Salryn.Tests;

// LAN-M16 guard: the remote LAN role authorization matrix. This guard EXECUTES the pure matrix
// logic (no database) to prove that terminal + handshake + a valid session are necessary but not
// sufficient: every write is additionally gated on the mapped permission, unknown commands are
// rejected, and manager/admin write actions with no safe remote domain path return NotSupported.
public static class LanM16RemotePosCommandAuthorizationChecks
{
    // Cashier: can sell + run a shift, but has no audit/admin permission.
    private static readonly string[] CashierPermissions =
    {
        SalrynPermissions.SalesCreate,
        SalrynPermissions.ShiftOpenClose,
    };

    // Manager here holds Settings.Manage but NOT Audit.View, to prove admin-status needs Audit.View.
    private static readonly string[] ManagerPermissions =
    {
        SalrynPermissions.SalesCreate,
        SalrynPermissions.ShiftOpenClose,
        SalrynPermissions.SettingsManage,
    };

    // Admin: everything a remote admin might hold, including audit view.
    private static readonly string[] AdminPermissions =
    {
        SalrynPermissions.SalesCreate,
        SalrynPermissions.ShiftOpenClose,
        SalrynPermissions.SettingsManage,
        SalrynPermissions.AuditView,
    };

    private static readonly string[] NoPermissions = System.Array.Empty<string>();

    public static void Run()
    {
        AssertReadsAlwaysAllowed();
        AssertWritesRequirePermission();
        AssertAdminStatusRequiresAuditView();
        AssertManagerOverrideNotSupportedForEveryone();
        AssertUnknownCommandRejected();
        AssertCapabilitiesAndHostLocalOnly();
        AssertSourceAndWiring();
    }

    private static void AssertReadsAlwaysAllowed()
    {
        // Any authenticated operator session (even with zero domain permissions) may read.
        foreach (string command in new[]
        {
            RemotePosCommands.SessionRead,
            RemotePosCommands.Capabilities,
            RemotePosCommands.CartView,
            RemotePosCommands.ShiftRead,
            RemotePosCommands.CheckoutStatus,
        })
        {
            AssertDecision(command, NoPermissions, RemotePosAuthorizationDecision.Allowed,
                $"Read command {command} must be allowed for any authenticated session.");
        }
    }

    private static void AssertWritesRequirePermission()
    {
        // Cart writes need Sales.Create.
        foreach (string command in new[]
        {
            RemotePosCommands.CartStart,
            RemotePosCommands.CartAddLine,
            RemotePosCommands.CartUpdateLine,
            RemotePosCommands.CartRemoveLine,
            RemotePosCommands.CartClear,
        })
        {
            AssertDecision(command, CashierPermissions, RemotePosAuthorizationDecision.Allowed,
                $"Cart command {command} must be allowed with Sales.Create.");
            AssertDecision(command, NoPermissions, RemotePosAuthorizationDecision.PermissionDenied,
                $"Cart command {command} must be denied without Sales.Create (trust alone is insufficient).");
        }

        // Shift writes need Shift.OpenClose.
        foreach (string command in new[] { RemotePosCommands.ShiftOpen, RemotePosCommands.ShiftClose })
        {
            AssertDecision(command, CashierPermissions, RemotePosAuthorizationDecision.Allowed,
                $"Shift command {command} must be allowed with Shift.OpenClose.");
            AssertDecision(command, NoPermissions, RemotePosAuthorizationDecision.PermissionDenied,
                $"Shift command {command} must be denied without Shift.OpenClose.");
        }

        // Checkout submit needs Sales.Create.
        AssertDecision(RemotePosCommands.CheckoutSubmit, CashierPermissions, RemotePosAuthorizationDecision.Allowed,
            "Checkout submit must be allowed with Sales.Create.");
        AssertDecision(RemotePosCommands.CheckoutSubmit, NoPermissions, RemotePosAuthorizationDecision.PermissionDenied,
            "Checkout submit must be denied without Sales.Create.");
    }

    private static void AssertAdminStatusRequiresAuditView()
    {
        AssertDecision(RemotePosCommands.AdminStatus, AdminPermissions, RemotePosAuthorizationDecision.Allowed,
            "Admin status must be allowed with Audit.View.");
        // Manager has Settings.Manage but not Audit.View: admin status must still be denied.
        AssertDecision(RemotePosCommands.AdminStatus, ManagerPermissions, RemotePosAuthorizationDecision.PermissionDenied,
            "Admin status must require Audit.View, not Settings.Manage.");
        AssertDecision(RemotePosCommands.AdminStatus, NoPermissions, RemotePosAuthorizationDecision.PermissionDenied,
            "Admin status must be denied without Audit.View.");
    }

    private static void AssertManagerOverrideNotSupportedForEveryone()
    {
        // No "admin can do everything by accident": overrides are NotSupported even for admins.
        AssertDecision(RemotePosCommands.ManagerOverride, CashierPermissions, RemotePosAuthorizationDecision.NotSupported,
            "Manager override must be NotSupported for cashiers.");
        AssertDecision(RemotePosCommands.ManagerOverride, ManagerPermissions, RemotePosAuthorizationDecision.NotSupported,
            "Manager override must be NotSupported for managers.");
        AssertDecision(RemotePosCommands.ManagerOverride, AdminPermissions, RemotePosAuthorizationDecision.NotSupported,
            "Manager override must be NotSupported even for admins.");
    }

    private static void AssertUnknownCommandRejected()
    {
        AssertDecision("remote-pos.settings.write", AdminPermissions, RemotePosAuthorizationDecision.UnknownCommand,
            "Unknown/unsupported host-local command must be rejected as UnknownCommand.");
        AssertDecision("lan-control.enable", AdminPermissions, RemotePosAuthorizationDecision.UnknownCommand,
            "Host-local LAN control must never be recognized as a remote POS command.");
    }

    private static void AssertCapabilitiesAndHostLocalOnly()
    {
        IReadOnlyList<RemotePosCapabilityResponse> cashierCaps = RemotePosAuthorizationMatrix.BuildCapabilities(CashierPermissions);

        RemotePosCapabilityResponse? checkout = cashierCaps.FirstOrDefault(c => c.Command == RemotePosCommands.CheckoutSubmit);
        if (checkout is null || !checkout.Allowed)
        {
            throw new InvalidOperationException("Capabilities must advertise checkout submit as allowed for a cashier.");
        }

        RemotePosCapabilityResponse? adminStatus = cashierCaps.FirstOrDefault(c => c.Command == RemotePosCommands.AdminStatus);
        if (adminStatus is null || adminStatus.Allowed)
        {
            throw new InvalidOperationException("Capabilities must advertise admin status as NOT allowed for a cashier.");
        }

        RemotePosCapabilityResponse? managerOverride = cashierCaps.FirstOrDefault(c => c.Command == RemotePosCommands.ManagerOverride);
        if (managerOverride is null || managerOverride.Allowed)
        {
            throw new InvalidOperationException("Capabilities must advertise manager override as NOT allowed.");
        }

        IReadOnlyList<string> hostLocalOnly = RemotePosAuthorizationMatrix.HostLocalOnlyActions();
        if (!hostLocalOnly.Any(a => a.Contains("/api/lan-control", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Host-local-only actions must list /api/lan-control controls.");
        }

        if (!hostLocalOnly.Any(a => a.Contains("Backup", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Host-local-only actions must list backup/recovery.");
        }
    }

    private static void AssertSourceAndWiring()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string matrix = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosAuthorizationMatrix.cs");
        string doc = ReadRequiredFile(projectRoot, "00-HANDOFF", "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertContains(matrix, "AdminActionHostLocalOnly", "Matrix must define host-local-only admin action denial.");
        AssertContains(matrix, "ManagerOverrideNotSupported", "Matrix must define manager override NotSupported denial.");
        AssertContains(doc, "LAN Role Authorization Matrix", "LAN-M16 doc must document the role authorization matrix.");
        AssertContains(coreSmoke, "LanM16RemotePosCommandAuthorizationChecks.Run();", "CoreSmoke must wire the LAN-M16 authorization guard.");
        AssertContains(infraSmoke, "remote-pos-command-authorization", "InfrastructureSmoke must wire the remote-pos-command-authorization command.");
    }

    private static void AssertDecision(
        string command,
        IReadOnlyCollection<string> permissions,
        RemotePosAuthorizationDecision expected,
        string message)
    {
        RemotePosCommandAuthorization authorization = RemotePosAuthorizationMatrix.Evaluate(command, permissions);
        if (authorization.Decision != expected)
        {
            throw new InvalidOperationException($"{message} (got {authorization.Decision}, code {authorization.Code})");
        }
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

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

using Salryn.Core.Models;

namespace Salryn.App.WinForms.Features.Shell;

// Kirch file guide: current user/session snapshot used by Shell and Checkout.
// UI location: drives user line, role line, and permission-based module access.
// Frontend note: read this for display only; do not mutate permissions from forms.

public sealed class CurrentAppSession
{
    private readonly HashSet<string> _permissions;

    public CurrentAppSession(
        Guid userId,
        string username,
        string displayName,
        string roleName,
        UserStatus status,
        IReadOnlyList<string> permissions,
        DateTimeOffset startedAt)
        : this(
            userId,
            Guid.Empty,
            username,
            displayName,
            roleName,
            status,
            permissions,
            startedAt)
    {
    }

    public CurrentAppSession(
        Guid userId,
        Guid roleId,
        string username,
        string displayName,
        string roleName,
        UserStatus status,
        IReadOnlyList<string> permissions,
        DateTimeOffset startedAt)
    {
        UserId = userId;
        RoleId = roleId;
        Username = username;
        DisplayName = displayName;
        RoleName = roleName;
        Status = status;
        Permissions = permissions;
        StartedAt = startedAt;

        _permissions = new HashSet<string>(
            permissions,
            StringComparer.OrdinalIgnoreCase);
    }

    public Guid UserId { get; }

    public Guid RoleId { get; }

    public string Username { get; }

    public string DisplayName { get; }

    public string RoleName { get; }

    public UserStatus Status { get; }

    public IReadOnlyList<string> Permissions { get; }

    public DateTimeOffset StartedAt { get; }

    public bool IsActive => Status == UserStatus.Active;

    // Kirch: Checks whether the current app user can access a routed module or workflow.
    public bool Can(string permission)
    {
        return _permissions.Contains(permission);
    }
}

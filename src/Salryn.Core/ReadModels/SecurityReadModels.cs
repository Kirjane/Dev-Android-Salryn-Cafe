using Salryn.Core.Models;

namespace Salryn.Core.ReadModels;

public sealed record UserRoleReadModel(
    Guid RoleId,
    string RoleName,
    bool IsActive);

public sealed record UserPermissionReadModel(
    Guid UserId,
    Guid RoleId,
    string Username,
    string DisplayName,
    string RoleName,
    UserStatus Status,
    IReadOnlyList<string> Permissions)
{
    public UserPermissionReadModel(
        Guid userId,
        string username,
        string displayName,
        string roleName,
        UserStatus status,
        IReadOnlyList<string> permissions)
        : this(
            userId,
            Guid.Empty,
            username,
            displayName,
            roleName,
            status,
            permissions)
    {
    }
}
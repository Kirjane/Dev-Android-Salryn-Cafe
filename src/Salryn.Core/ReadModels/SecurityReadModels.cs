using Salryn.Core.Models;

namespace Salryn.Core.ReadModels;

public sealed record UserPermissionReadModel(
    Guid UserId,
    string Username,
    string DisplayName,
    string RoleName,
    UserStatus Status,
    IReadOnlyList<string> Permissions);

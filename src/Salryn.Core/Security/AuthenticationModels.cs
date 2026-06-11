using Salryn.Core.Models;

namespace Salryn.Core.Security;

public sealed record FirstRunAdminSetupRequest(
    string Username,
    string DisplayName,
    string Password,
    string ConfirmPassword);

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record AuthenticatedUserReadModel(
    Guid UserId,
    string Username,
    string DisplayName,
    string RoleName,
    UserStatus Status,
    IReadOnlyList<string> Permissions);

public sealed record LoginUserCredentialReadModel(
    Guid UserId,
    string Username,
    string DisplayName,
    string RoleName,
    UserStatus Status,
    string PasswordHash);

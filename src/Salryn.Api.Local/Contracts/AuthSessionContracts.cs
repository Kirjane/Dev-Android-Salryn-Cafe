namespace Salryn.Api.Local.Contracts;

public sealed record LoginApiRequest(
    string Username,
    string Password);

public sealed record SessionUserResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string RoleName,
    Guid RoleId,
    string Status,
    IReadOnlyList<string> Permissions);

public sealed record SessionResponse(
    bool IsAuthenticated,
    SessionUserResponse? User,
    DateTimeOffset? SessionStartedAt);

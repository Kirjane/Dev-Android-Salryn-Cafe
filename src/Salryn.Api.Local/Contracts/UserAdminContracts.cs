namespace Salryn.Api.Local.Contracts;

public sealed record CreateUserApiRequest(
    string Username,
    string DisplayName,
    string TemporaryPassword,
    string ConfirmTemporaryPassword,
    Guid RoleId,
    bool IsActive);

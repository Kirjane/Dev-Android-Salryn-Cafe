using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;

namespace Salryn.App.WinForms.Features.Users;

// Kirch file guide: presenter for Users Admin behavior.
// UI location: backs Shell > Users.
// Frontend note: forms call this boundary for validation and writes; do not move repository calls into controls.

public sealed class UsersAdminPresenter
{
    private readonly IUserReadRepository _userReads;
    private readonly IUserWriteRepository _userWrites;
    private readonly CurrentAppSession _session;
    private UsersAdminViewModel _state = UsersAdminViewModel.Empty();

    public UsersAdminPresenter(
        IUserReadRepository userReads,
        IUserWriteRepository userWrites,
        CurrentAppSession session)
    {
        _userReads = userReads ?? throw new ArgumentNullException(nameof(userReads));
        _userWrites = userWrites ?? throw new ArgumentNullException(nameof(userWrites));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public UsersAdminViewModel State => _state;

    public async Task<UsersAdminOperationResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<IReadOnlyList<UserReadModel>> usersResult =
            await _userReads.GetAllAsync(cancellationToken);

        if (usersResult.IsFailure)
        {
            return LoadFailure(usersResult.ErrorDetail.Code, usersResult.ErrorDetail.Message);
        }

        Result<IReadOnlyList<UserRoleReadModel>> rolesResult =
            await _userReads.GetRolesAsync(cancellationToken);

        if (rolesResult.IsFailure)
        {
            return LoadFailure(rolesResult.ErrorDetail.Code, rolesResult.ErrorDetail.Message);
        }

        UsersAdminRoleOptionViewModel[] roles = rolesResult.Value
            .Where(role => role.IsActive)
            .Select(role => new UsersAdminRoleOptionViewModel(role.RoleId, role.RoleName))
            .ToArray();

        UsersAdminRowViewModel[] users = usersResult.Value
            .Select(user => MapRow(user, roles))
            .ToArray();

        _state = new UsersAdminViewModel(
            Users: users,
            Roles: roles,
            StatusMessage: BuildStatusMessage(users.Length),
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);

        return UsersAdminOperationResult.Success(_state);
    }

    public async Task<UsersAdminOperationResult> CreateUserAsync(
        UsersAdminCreateInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateUserFields(
            input.Username,
            input.DisplayName,
            input.RoleId);

        if (validation.IsFailure)
        {
            return OperationFailure(validation.ErrorDetail.Code, validation.ErrorDetail.Message);
        }

        Result passwordValidation = ValidatePassword(input.Password, input.ConfirmPassword);

        if (passwordValidation.IsFailure)
        {
            return OperationFailure(passwordValidation.ErrorDetail.Code, passwordValidation.ErrorDetail.Message);
        }

        Result<string> hashResult = SalrynPasswordHasher.HashPassword(input.Password);

        if (hashResult.IsFailure)
        {
            return OperationFailure(hashResult.ErrorDetail.Code, hashResult.ErrorDetail.Message);
        }

        Result result = await _userWrites.CreateAsync(
            new UserAdminCreateRequest(
                input.Username.Trim(),
                input.DisplayName.Trim(),
                input.RoleId,
                hashResult.Value,
                _session.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            return OperationFailure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        return await LoadAsync(cancellationToken);
    }

    public async Task<UsersAdminOperationResult> UpdateUserAsync(
        UsersAdminUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input.UserId == Guid.Empty)
        {
            return OperationFailure("Users.InvalidUserId", "Select a user before saving changes.");
        }

        Result validation = ValidateUserFields(
            input.Username,
            input.DisplayName,
            input.RoleId);

        if (validation.IsFailure)
        {
            return OperationFailure(validation.ErrorDetail.Code, validation.ErrorDetail.Message);
        }

        Result result = await _userWrites.UpdateAsync(
            new UserAdminUpdateRequest(
                input.UserId,
                input.Username.Trim(),
                input.DisplayName.Trim(),
                input.RoleId,
                _session.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            return OperationFailure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        return await LoadAsync(cancellationToken);
    }

    public async Task<UsersAdminOperationResult> ResetPasswordAsync(
        UsersAdminPasswordResetInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input.UserId == Guid.Empty)
        {
            return OperationFailure("Users.InvalidUserId", "Select a user before resetting password.");
        }

        Result passwordValidation = ValidatePassword(input.Password, input.ConfirmPassword);

        if (passwordValidation.IsFailure)
        {
            return OperationFailure(passwordValidation.ErrorDetail.Code, passwordValidation.ErrorDetail.Message);
        }

        Result<string> hashResult = SalrynPasswordHasher.HashPassword(input.Password);

        if (hashResult.IsFailure)
        {
            return OperationFailure(hashResult.ErrorDetail.Code, hashResult.ErrorDetail.Message);
        }

        Result result = await _userWrites.ResetPasswordAsync(
            new UserAdminPasswordResetRequest(
                input.UserId,
                hashResult.Value,
                _session.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            return OperationFailure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        return await LoadAsync(cancellationToken);
    }

    public async Task<UsersAdminOperationResult> SetUserStatusAsync(
        Guid userId,
        UserStatus status,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (userId == Guid.Empty)
        {
            return OperationFailure("Users.InvalidUserId", "Select a user before changing status.");
        }

        if (userId == _session.UserId && status != UserStatus.Active)
        {
            return OperationFailure(
                "Users.SelfDeactivateBlocked",
                "The current user cannot deactivate their own account.");
        }

        if (status == UserStatus.Inactive && string.IsNullOrWhiteSpace(reason))
        {
            return OperationFailure(
                "Users.DeactivateReasonRequired",
                "A reason is required when deactivating a user.");
        }

        Result result = await _userWrites.SetStatusAsync(
            new UserAdminStatusChangeRequest(
                userId,
                status,
                reason,
                _session.UserId),
            cancellationToken);

        if (result.IsFailure)
        {
            return OperationFailure(result.ErrorDetail.Code, result.ErrorDetail.Message);
        }

        return await LoadAsync(cancellationToken);
    }

    private UsersAdminOperationResult LoadFailure(
        string errorCode,
        string errorMessage)
    {
        _state = new UsersAdminViewModel(
            Users: Array.Empty<UsersAdminRowViewModel>(),
            Roles: Array.Empty<UsersAdminRoleOptionViewModel>(),
            StatusMessage: "Users could not be loaded.",
            HasError: true,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage);

        return UsersAdminOperationResult.Failure(errorCode, errorMessage, _state);
    }

    private UsersAdminOperationResult OperationFailure(
        string errorCode,
        string errorMessage)
    {
        _state = _state with
        {
            HasError = true,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            StatusMessage = errorMessage
        };

        return UsersAdminOperationResult.Failure(errorCode, errorMessage, _state);
    }

    private static Result ValidateUserFields(
        string username,
        string displayName,
        Guid roleId)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Fail(Error.Validation("Users.UsernameRequired", "Username is required."));
        }

        if (username.Trim().Length < 3)
        {
            return Result.Fail(Error.Validation("Users.UsernameTooShort", "Username must be at least 3 characters."));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Fail(Error.Validation("Users.DisplayNameRequired", "Display name is required."));
        }

        if (roleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.RoleRequired", "Role is required."));
        }

        return Result.Success();
    }

    private static Result ValidatePassword(
        string password,
        string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return Result.Fail(Error.Validation("Users.PasswordRequired", "Password is required."));
        }

        if (password.Length < 8)
        {
            return Result.Fail(Error.Validation("Users.PasswordTooShort", "Password must be at least 8 characters."));
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation("Users.PasswordMismatch", "Password confirmation does not match."));
        }

        return Result.Success();
    }

    private static UsersAdminRowViewModel MapRow(
        UserReadModel user,
        IReadOnlyList<UsersAdminRoleOptionViewModel> roles)
    {
        UsersAdminRoleOptionViewModel? role = roles.FirstOrDefault(
            item => string.Equals(item.RoleName, user.RoleName, StringComparison.OrdinalIgnoreCase));

        return new UsersAdminRowViewModel(
            UserId: user.Id,
            Username: user.Username,
            DisplayName: user.DisplayName,
            RoleId: role?.RoleId ?? Guid.Empty,
            RoleName: user.RoleName,
            Status: user.Status,
            StatusText: user.Status.ToString(),
            CreatedAtText: user.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
    }

    private static string BuildStatusMessage(int userCount)
    {
        return userCount == 0
            ? "No users found."
            : $"Showing {userCount} user account(s).";
    }
}

public sealed record UsersAdminCreateInput(
    string Username,
    string DisplayName,
    Guid RoleId,
    string Password,
    string ConfirmPassword);

public sealed record UsersAdminUpdateInput(
    Guid UserId,
    string Username,
    string DisplayName,
    Guid RoleId);

public sealed record UsersAdminPasswordResetInput(
    Guid UserId,
    string Password,
    string ConfirmPassword);

public sealed record UsersAdminRoleOptionViewModel(
    Guid RoleId,
    string RoleName)
{
    public override string ToString()
    {
        return RoleName;
    }
}

public sealed record UsersAdminRowViewModel(
    Guid UserId,
    string Username,
    string DisplayName,
    Guid RoleId,
    string RoleName,
    UserStatus Status,
    string StatusText,
    string CreatedAtText);

public sealed record UsersAdminViewModel(
    IReadOnlyList<UsersAdminRowViewModel> Users,
    IReadOnlyList<UsersAdminRoleOptionViewModel> Roles,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static UsersAdminViewModel Empty()
    {
        return new UsersAdminViewModel(
            Array.Empty<UsersAdminRowViewModel>(),
            Array.Empty<UsersAdminRoleOptionViewModel>(),
            "Users are not loaded.",
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
    }
}

public sealed record UsersAdminOperationResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    UsersAdminViewModel State)
{
    public static UsersAdminOperationResult Success(UsersAdminViewModel state)
    {
        return new UsersAdminOperationResult(true, null, null, state);
    }

    public static UsersAdminOperationResult Failure(
        string errorCode,
        string errorMessage,
        UsersAdminViewModel state)
    {
        return new UsersAdminOperationResult(false, errorCode, errorMessage, state);
    }
}

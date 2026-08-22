using Salryn.Api.Local.Contracts;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local.Services;

public sealed class UserAdminApiService
{
    private static readonly string[] AllowedMvpRoles = { "Cashier", "Manager" };

    private readonly IUserReadRepository _userReads;
    private readonly IUserWriteRepository _userWrites;

    public UserAdminApiService(Salryn.Composition.Local.SalrynLocalComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        _userReads = composition.UserReadRepository;
        _userWrites = new SqliteRepositoryFactory(composition.ConnectionFactory)
            .CreateUserWriteRepository();
    }

    public async Task<Result<UserListItemResponse>> CreateUserAsync(
        CreateUserApiRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request, actorUserId);
        if (validation.IsFailure)
        {
            return Result<UserListItemResponse>.Fail(validation.ErrorDetail);
        }

        Result<IReadOnlyList<UserRoleReadModel>> rolesResult =
            await _userReads.GetRolesAsync(cancellationToken).ConfigureAwait(false);

        if (rolesResult.IsFailure)
        {
            return Result<UserListItemResponse>.Fail(rolesResult.ErrorDetail);
        }

        UserRoleReadModel? selectedRole = rolesResult.Value.FirstOrDefault(
            role => role.RoleId == request.RoleId && role.IsActive);

        if (selectedRole is null)
        {
            return Result<UserListItemResponse>.Fail(Error.Validation(
                "Users.RoleRequired",
                "Role is required."));
        }

        if (!AllowedMvpRoles.Contains(selectedRole.RoleName, StringComparer.OrdinalIgnoreCase))
        {
            return Result<UserListItemResponse>.Fail(Error.Forbidden(
                "Users.RoleNotAllowedForMvp",
                "This host user creation flow can create Cashier or Manager accounts only."));
        }

        Result<string> passwordHashResult = SalrynPasswordHasher.HashPassword(request.TemporaryPassword);
        if (passwordHashResult.IsFailure)
        {
            return Result<UserListItemResponse>.Fail(passwordHashResult.ErrorDetail);
        }

        Result createResult = await _userWrites.CreateAsync(
            new UserAdminCreateRequest(
                request.Username.Trim(),
                request.DisplayName.Trim(),
                selectedRole.RoleId,
                passwordHashResult.Value,
                actorUserId,
                request.IsActive ? UserStatus.Active : UserStatus.Inactive),
            cancellationToken).ConfigureAwait(false);

        if (createResult.IsFailure)
        {
            return Result<UserListItemResponse>.Fail(createResult.ErrorDetail);
        }

        Result<UserReadModel?> createdResult = await _userReads
            .FindByUsernameAsync(request.Username.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (createdResult.IsFailure)
        {
            return Result<UserListItemResponse>.Fail(createdResult.ErrorDetail);
        }

        if (createdResult.Value is null)
        {
            return Result<UserListItemResponse>.Fail(Error.Unexpected(
                "Users.CreatedUserReadbackFailed",
                "User was created but could not be read back."));
        }

        return Result<UserListItemResponse>.Success(ReadModelMapper.ToUserListItem(createdResult.Value));
    }

    private static Result ValidateRequest(CreateUserApiRequest request, Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Users.CurrentUserRequired",
                "A current admin user is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Fail(Error.Validation("Users.UsernameRequired", "Username is required."));
        }

        if (request.Username.Trim().Length < 3)
        {
            return Result.Fail(Error.Validation(
                "Users.UsernameTooShort",
                "Username must be at least 3 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result.Fail(Error.Validation("Users.DisplayNameRequired", "Display name is required."));
        }

        if (request.RoleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Users.RoleRequired", "Role is required."));
        }

        if (string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            return Result.Fail(Error.Validation("Users.PasswordRequired", "Password is required."));
        }

        if (request.TemporaryPassword.Length < 8)
        {
            return Result.Fail(Error.Validation(
                "Users.PasswordTooShort",
                "Password must be at least 8 characters."));
        }

        if (!string.Equals(request.TemporaryPassword, request.ConfirmTemporaryPassword, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "Users.PasswordMismatch",
                "Password confirmation does not match."));
        }

        return Result.Success();
    }
}

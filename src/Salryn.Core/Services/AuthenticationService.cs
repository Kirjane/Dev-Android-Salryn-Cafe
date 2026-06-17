using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;

namespace Salryn.Core.Services;

public interface IAuthenticationService
{
    Task<Result<bool>> HasAnyActiveUserAsync(
        CancellationToken cancellationToken = default);

    Task<Result<AuthenticatedUserReadModel>> CreateFirstAdminAsync(
        FirstRunAdminSetupRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AuthenticatedUserReadModel>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IAuthenticationRepository _authenticationRepository;
    private readonly IUserPermissionReadRepository _userPermissionReadRepository;

    public AuthenticationService(
        IAuthenticationRepository authenticationRepository,
        IUserPermissionReadRepository userPermissionReadRepository)
    {
        _authenticationRepository = authenticationRepository;
        _userPermissionReadRepository = userPermissionReadRepository;
    }

    public Task<Result<bool>> HasAnyActiveUserAsync(
        CancellationToken cancellationToken = default)
    {
        return _authenticationRepository.HasAnyActiveUserAsync(cancellationToken);
    }

    // Kirch: Validates first-run admin data, hashes the password, and creates the first Admin account.
    public async Task<Result<AuthenticatedUserReadModel>> CreateFirstAdminAsync(
        FirstRunAdminSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateFirstRunAdminRequest(request);

        if (validation.IsFailure)
        {
            return Result<AuthenticatedUserReadModel>.Fail(validation.ErrorDetail);
        }

        Result<bool> hasAnyActiveUserResult =
            await _authenticationRepository.HasAnyActiveUserAsync(cancellationToken);

        if (hasAnyActiveUserResult.IsFailure)
        {
            return Result<AuthenticatedUserReadModel>.Fail(hasAnyActiveUserResult.ErrorDetail);
        }

        if (hasAnyActiveUserResult.Value)
        {
            return Result<AuthenticatedUserReadModel>.Fail(Error.Conflict(
                "Auth.FirstAdminAlreadyExists",
                "First-run admin setup is no longer available because an active user already exists."));
        }

        Result<string> passwordHashResult = SalrynPasswordHasher.HashPassword(request.Password);

        if (passwordHashResult.IsFailure)
        {
            return Result<AuthenticatedUserReadModel>.Fail(passwordHashResult.ErrorDetail);
        }

        return await _authenticationRepository.CreateFirstAdminAsync(
            NormalizeFirstRunAdminRequest(request),
            passwordHashResult.Value,
            cancellationToken);
    }

    // Kirch: Verifies login credentials and returns the authenticated user with permissions.
    public async Task<Result<AuthenticatedUserReadModel>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateLoginRequest(request);

        if (validation.IsFailure)
        {
            return Result<AuthenticatedUserReadModel>.Fail(validation.ErrorDetail);
        }

        Result<LoginUserCredentialReadModel?> credentialResult =
            await _authenticationRepository.FindCredentialByUsernameAsync(
                request.Username.Trim(),
                cancellationToken);

        if (credentialResult.IsFailure)
        {
            return Result<AuthenticatedUserReadModel>.Fail(credentialResult.ErrorDetail);
        }

        LoginUserCredentialReadModel? credential = credentialResult.Value;

        if (credential is null)
        {
            return Result<AuthenticatedUserReadModel>.Fail(Error.Unauthorized(
                "Auth.InvalidCredentials",
                "Invalid username or password."));
        }

        if (credential.Status != UserStatus.Active)
        {
            return Result<AuthenticatedUserReadModel>.Fail(Error.Forbidden(
                "Auth.UserNotActive",
                "User account is not active."));
        }

        bool passwordValid = SalrynPasswordHasher.VerifyPassword(
            request.Password,
            credential.PasswordHash);

        if (!passwordValid)
        {
            return Result<AuthenticatedUserReadModel>.Fail(Error.Unauthorized(
                "Auth.InvalidCredentials",
                "Invalid username or password."));
        }

        Result<UserPermissionReadModel?> permissionResult =
            await _userPermissionReadRepository.FindByUserIdAsync(
                credential.UserId,
                cancellationToken);

        if (permissionResult.IsFailure)
        {
            return Result<AuthenticatedUserReadModel>.Fail(permissionResult.ErrorDetail);
        }

        UserPermissionReadModel? profile = permissionResult.Value;

        if (profile is null)
        {
            return Result<AuthenticatedUserReadModel>.Fail(Error.Unexpected(
                "Auth.PermissionProfileMissing",
                "User was authenticated but permission profile could not be loaded."));
        }

        AuthenticatedUserReadModel authenticated = new(
            profile.UserId,
            profile.Username,
            profile.DisplayName,
            profile.RoleName,
            profile.Status,
            profile.Permissions)
        {
            RoleId = profile.RoleId
        };

        return Result<AuthenticatedUserReadModel>.Success(authenticated);
    }

    private static Result ValidateFirstRunAdminRequest(
        FirstRunAdminSetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Fail(Error.Validation(
                "Auth.UsernameRequired",
                "Username is required."));
        }

        if (request.Username.Trim().Length < 3)
        {
            return Result.Fail(Error.Validation(
                "Auth.UsernameTooShort",
                "Username must be at least 3 characters."));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Result.Fail(Error.Validation(
                "Auth.DisplayNameRequired",
                "Display name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Fail(Error.Validation(
                "Auth.PasswordRequired",
                "Password is required."));
        }

        if (request.Password.Length < 8)
        {
            return Result.Fail(Error.Validation(
                "Auth.PasswordTooShort",
                "Password must be at least 8 characters."));
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Validation(
                "Auth.PasswordMismatch",
                "Password confirmation does not match."));
        }

        return Result.Success();
    }

    private static Result ValidateLoginRequest(
        LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Fail(Error.Validation(
                "Auth.UsernameRequired",
                "Username is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Fail(Error.Validation(
                "Auth.PasswordRequired",
                "Password is required."));
        }

        return Result.Success();
    }

    private static FirstRunAdminSetupRequest NormalizeFirstRunAdminRequest(
        FirstRunAdminSetupRequest request)
    {
        return new FirstRunAdminSetupRequest(
            request.Username.Trim(),
            request.DisplayName.Trim(),
            request.Password,
            request.ConfirmPassword);
    }
}

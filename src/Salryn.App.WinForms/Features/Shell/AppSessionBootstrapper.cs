using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.App.WinForms.Features.Shell;

// Kirch file guide: loads the first active user session for app startup.
// UI location: runs before Shell decides what modules the user can open.
// Frontend note: this is not a visual component.

public static class AppSessionBootstrapper
{
    // Kirch: Loads the first active user as a temporary app session until a real login screen is implemented.
    public static async Task<Result<CurrentAppSession?>> LoadFirstActiveUserAsync(
        IUserReadRepository userReadRepository,
        IUserPermissionReadRepository userPermissionReadRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userReadRepository);
        ArgumentNullException.ThrowIfNull(userPermissionReadRepository);

        Result<IReadOnlyList<UserReadModel>> usersResult =
            await userReadRepository.GetAllAsync(cancellationToken);

        if (usersResult.IsFailure)
        {
            return Result<CurrentAppSession?>.Fail(usersResult.ErrorDetail);
        }

        UserReadModel? activeUser = usersResult.Value
            .FirstOrDefault(user => user.Status == UserStatus.Active);

        if (activeUser is null)
        {
            return Result<CurrentAppSession?>.Success(null);
        }

        Result<UserPermissionReadModel?> permissionsResult =
            await userPermissionReadRepository.FindByUserIdAsync(
                activeUser.Id,
                cancellationToken);

        if (permissionsResult.IsFailure)
        {
            return Result<CurrentAppSession?>.Fail(permissionsResult.ErrorDetail);
        }

        UserPermissionReadModel? profile = permissionsResult.Value;

        if (profile is null)
        {
            return Result<CurrentAppSession?>.Fail(Error.Unexpected(
                "AppSession.UserProfileMissing",
                "Active user was found but permission profile could not be loaded."));
        }

        CurrentAppSession session = new(
            profile.UserId,
            profile.RoleId,
            profile.Username,
            profile.DisplayName,
            profile.RoleName,
            profile.Status,
            profile.Permissions,
            DateTimeOffset.UtcNow);

        return Result<CurrentAppSession?>.Success(session);
    }
}

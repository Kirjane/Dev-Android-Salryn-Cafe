using Salryn.Core.Services.Licensing;

namespace Salryn.Api.Local.Auth;

internal static class LocalLicenseInstallAuthorization
{
    public static IResult? Require(
        LicenseStatus status,
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        if (!LocalLicenseEndpoints.RequiresPrivilegedInstallAuthorization(status))
        {
            return null;
        }

        return LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            LocalApiPermissions.SettingsManage);
    }
}

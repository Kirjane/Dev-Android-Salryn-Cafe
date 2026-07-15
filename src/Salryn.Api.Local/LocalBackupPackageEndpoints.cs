using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

internal static class LocalBackupPackageEndpoints
{
    public static WebApplication MapLocalBackupPackageEndpoints(this WebApplication app, SalrynLocalComposition composition)
    {
        ControlledBackupPackageService service=new(composition.DbPaths);
        async Task<IResult?> GuardAsync(HttpContext c, LocalApiSessionService s, CancellationToken ct)
        {
            IResult? permission = LocalApiPermissionGuards.RequirePermission(c, s, LocalApiPermissions.BackupManage);
            if (permission is not null)
            {
                return permission;
            }

            return await ApiLicenseGuard.RequireLicensedFeatureAsync(LicenseFeatures.Backup, ct).ConfigureAwait(false);
        }

        app.MapPost("/api/backups", async (HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.Create()));
        app.MapGet("/api/backups", async (HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.List()));
        app.MapPost("/api/backups/{id}/verify", async (string id, HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.Verify(id)));
        app.MapPost("/api/backups/{id}/restore", async (string id, RestoreRequest r, HttpContext c, LocalApiSessionService s, CancellationToken ct) =>
            await GuardAsync(c, s, ct).ConfigureAwait(false) ?? Map(service.Restore(id, r.Confirmation)));
        return app;
    }
    private static IResult Map<T>(Salryn.Core.Common.Result<T> r)=>r.IsSuccess?Results.Ok(r.Value):Results.BadRequest(new{code=r.ErrorDetail.Code,error=r.ErrorDetail.Message});
    private sealed record RestoreRequest(string Confirmation);
}

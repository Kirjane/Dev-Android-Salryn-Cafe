using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

internal static class LocalBackupPackageEndpoints
{
    public static WebApplication MapLocalBackupPackageEndpoints(this WebApplication app, SalrynLocalComposition composition)
    {
        ControlledBackupPackageService service=new(composition.DbPaths);
        IResult? Guard(HttpContext c,LocalApiSessionService s)=>LocalApiPermissionGuards.RequirePermission(c,s,LocalApiPermissions.BackupManage);
        app.MapPost("/api/backups",(HttpContext c,LocalApiSessionService s)=>Guard(c,s)??Map(service.Create()));
        app.MapGet("/api/backups",(HttpContext c,LocalApiSessionService s)=>Guard(c,s)??Map(service.List()));
        app.MapPost("/api/backups/{id}/verify",(string id,HttpContext c,LocalApiSessionService s)=>Guard(c,s)??Map(service.Verify(id)));
        app.MapPost("/api/backups/{id}/restore",(string id,RestoreRequest r,HttpContext c,LocalApiSessionService s)=>Guard(c,s)??Map(service.Restore(id,r.Confirmation)));
        return app;
    }
    private static IResult Map<T>(Salryn.Core.Common.Result<T> r)=>r.IsSuccess?Results.Ok(r.Value):Results.BadRequest(new{code=r.ErrorDetail.Code,error=r.ErrorDetail.Message});
    private sealed record RestoreRequest(string Confirmation);
}

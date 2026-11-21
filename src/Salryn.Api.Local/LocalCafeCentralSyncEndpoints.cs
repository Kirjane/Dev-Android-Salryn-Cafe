using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Api.Local;

public static class LocalCafeCentralSyncEndpoints
{
    public static WebApplication MapLocalCafeCentralSyncEndpoints(this WebApplication app)
    {
        app.MapPost("/api/cafe/central/enroll",async (EnrollRequest request,HttpContext http,LocalApiSessionService sessions,
            SalrynLocalComposition composition,CafeCentralConnection central,ILocalLicenseEnforcementService licensing,CancellationToken ct)=>
        {
            if(await Guard(http,sessions,composition,licensing,"StoreTerminal.Manage",ct) is IResult denied)return denied;
            try {return Results.Ok(new{deviceId=await central.EnrollAsync(request.Configuration,request.JoinCode,request.DeviceName,ct),status="PendingAdministratorApproval"});}
            catch(Exception error) when(error is InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException or FormatException)
            {return Results.Json(new{code="CafeCentral.SetupRejected",error=error.Message},statusCode:409);}
        });
        app.MapPost("/api/cafe/central/synchronize",async (SynchronizeRequest request,HttpContext http,LocalApiSessionService sessions,
            SalrynLocalComposition composition,CafeCentralConnection central,ILocalLicenseEnforcementService licensing,CancellationToken ct)=>
        {
            if(await Guard(http,sessions,composition,licensing,"Sales.Create",ct) is IResult denied)return denied;
            var actor=sessions.GetRequiredSession(http.Request.Cookies[LocalApiSessionService.SessionCookieName])!;
            // Current local actor is authoritative; callers never supply a substituted username/user ID.
            try{return Results.Ok(await central.ConnectAsync(actor.UserId,actor.Username,request.Password,ct));}
            catch(Exception error) when(error is InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException or FormatException)
            {return Results.Json(new{code="CafeCentral.SyncUnconfirmed",error=error.Message},statusCode:409);}
        });
        return app;
    }
    private static async Task<IResult?> Guard(HttpContext http,LocalApiSessionService sessions,SalrynLocalComposition composition,
        ILocalLicenseEnforcementService licensing,string permission,CancellationToken ct)
    {
        if(LocalApiPermissionGuards.RequirePermission(http,sessions,permission) is IResult denied)return denied;
        var actor=sessions.GetRequiredSession(http.Request.Cookies[LocalApiSessionService.SessionCookieName])!;
        var current=await new SqliteUserPermissionReadRepository(composition.ConnectionFactory).FindByUserIdAsync(actor.UserId,ct);
        if(current.IsFailure||current.Value is null||current.Value.Status!=UserStatus.Active||current.Value.RoleId!=actor.RoleId||
            !new HashSet<string>(actor.Permissions,StringComparer.Ordinal).SetEquals(current.Value.Permissions))
        {sessions.Logout(actor.SessionId);return Results.Json(new{code="Auth.OperatorAuthorityChanged",error="Sign in again with current operator authority."},statusCode:403);}
        return await ApiLicenseGuard.RequireLicensedFeatureAsync(licensing,LicenseFeatures.Pos,ct);
    }
    public sealed record EnrollRequest(CafeCentralConfiguration Configuration,string JoinCode,string DeviceName);
    public sealed record SynchronizeRequest(string Password);
}

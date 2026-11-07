using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.Services.Licensing;

namespace Salryn.Api.Local;

internal static class LocalOwnerAnalyticsEndpoints
{
    public static WebApplication MapLocalOwnerAnalyticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/owner-analytics/overview", (DateOnly? from, DateOnly? to, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a) =>
            Run(c,s,a,from,to,false,(service,range)=>service.GetOverview(range)));
        app.MapGet("/api/owner-analytics/sales-trend", (DateOnly? from, DateOnly? to, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a) =>
            Run(c,s,a,from,to,false,(service,range)=>service.GetSalesTrend(range)));
        app.MapGet("/api/owner-analytics/payment-mix", (DateOnly? from, DateOnly? to, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a) =>
            Run(c,s,a,from,to,false,(service,range)=>service.GetPaymentMix(range)));
        app.MapGet("/api/owner-analytics/top-products", (DateOnly? from, DateOnly? to, int? limit, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a) =>
            Run(c,s,a,from,to,false,(service,range)=>service.GetTopProducts(range,limit??10)));
        app.MapGet("/api/owner-analytics/ar-aging", async (DateOnly? from, DateOnly? to, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a, CancellationToken ct) =>
        {
            if (await Guard(c,s,true,ct) is IResult denied) return denied;
            Result<OwnerAnalyticsRange> range=OwnerAnalyticsRange.Create(from,to);
            return range.IsFailure?ErrorResult(range.ErrorDetail):ToResult(a.GetArAging(range.Value));
        });
        app.MapGet("/api/owner-analytics/export/sales.csv", (DateOnly? from, DateOnly? to, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a) =>
            Csv(c,s,a,from,to,false,(service,range)=>service.ExportSalesCsv(range),"salryn-sales.csv"));
        app.MapGet("/api/owner-analytics/export/ar.csv", (DateOnly? from, DateOnly? to, HttpContext c, LocalApiSessionService s, OwnerAnalyticsApiService a) =>
            Csv(c,s,a,from,to,true,(service,range)=>service.ExportArCsv(range),"salryn-ar.csv"));
        return app;
    }

    private static async Task<IResult?> Guard(HttpContext context,LocalApiSessionService sessions,bool ar,CancellationToken ct)
    {
        if(LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(context) is IResult local)return local;
        if(LocalApiPermissionGuards.RequirePermission(context,sessions,LocalApiPermissions.ReportsView) is IResult reports)return reports;
        ILocalLicenseEnforcementService licensing=context.RequestServices.GetRequiredService<ILocalLicenseEnforcementService>();
        if(await ApiLicenseGuard.RequireLicensedFeatureAsync(licensing,LicenseFeatures.Reports,ct).ConfigureAwait(false) is IResult reportLicense)return reportLicense;
        if(!ar)return null;
        if(LocalApiPermissionGuards.RequirePermission(context,sessions,LocalApiPermissions.ReceivablesView) is IResult receivables)return receivables;
        return await ApiLicenseGuard.RequireLicensedFeatureAsync(licensing,LicenseFeatures.ReceivablesUtangLedger,ct).ConfigureAwait(false);
    }

    private static async Task<IResult> Run<T>(HttpContext c,LocalApiSessionService s,OwnerAnalyticsApiService a,DateOnly? from,DateOnly? to,bool ar,Func<OwnerAnalyticsApiService,OwnerAnalyticsRange,Result<T>> read)
    {
        if(await Guard(c,s,ar,c.RequestAborted) is IResult denied)return denied;
        Result<OwnerAnalyticsRange> range=OwnerAnalyticsRange.Create(from,to);
        return range.IsFailure?ErrorResult(range.ErrorDetail):ToResult(read(a,range.Value));
    }

    private static async Task<IResult> Csv(HttpContext c,LocalApiSessionService s,OwnerAnalyticsApiService a,DateOnly? from,DateOnly? to,bool ar,Func<OwnerAnalyticsApiService,OwnerAnalyticsRange,Result<byte[]>> read,string name)
    {
        if(await Guard(c,s,ar,c.RequestAborted) is IResult denied)return denied;
        Result<OwnerAnalyticsRange> range=OwnerAnalyticsRange.Create(from,to);
        if(range.IsFailure)return ErrorResult(range.ErrorDetail);
        Result<byte[]> result=read(a,range.Value);
        return result.IsSuccess?Results.File(result.Value,"text/csv; charset=utf-8",name):ErrorResult(result.ErrorDetail);
    }
    private static IResult ToResult<T>(Result<T> r)=>r.IsSuccess?Results.Ok(r.Value):ErrorResult(r.ErrorDetail);
    private static IResult ErrorResult(Error e)=>Results.Json(new{error=e.Message,code=e.Code},statusCode:e.Type==ErrorType.Validation?400:500);
}

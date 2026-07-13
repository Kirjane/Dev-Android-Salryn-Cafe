using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class LocalSaleVoidEndpoints
{
    public static WebApplication MapLocalSaleVoidEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sales/{saleId:guid}/void", async (
            Guid saleId,
            SaleVoidRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SaleVoidApiService service,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(httpContext, sessions, LocalApiPermissions.SalesVoid) is IResult denied)
            {
                return denied;
            }

            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            LocalApiSession? session = sessions.GetRequiredSession(sessionId);
            if (session is null)
            {
                return Results.Unauthorized();
            }

            Result<SaleVoidResponse> result = await service.VoidAsync(saleId, request, session, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return Results.Ok(result.Value);
            }

            int status = result.ErrorDetail.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message }, statusCode: status);
        });

        return app;
    }
}

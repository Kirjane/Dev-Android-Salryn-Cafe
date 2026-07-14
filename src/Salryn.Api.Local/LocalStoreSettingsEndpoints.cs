using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local;

internal static class LocalStoreSettingsEndpoints
{
    public static WebApplication MapLocalStoreSettingsEndpoints(this WebApplication app, SalrynLocalComposition composition)
    {
        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);
        IStoreSettingsReadRepository reader = repositories.CreateStoreSettingsReadRepository();
        IStoreSettingsWriteRepository writer = repositories.CreateStoreSettingsWriteRepository();

        app.MapGet("/api/settings/store", async (HttpContext context, LocalApiSessionService sessions, CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.SettingsManage) is IResult denied) return denied;
            var result = await reader.GetCurrentAsync(ct);
            if (result.IsFailure) return Results.BadRequest(new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message });
            var s = result.Value;
            return s is null ? Results.NotFound() : Results.Ok(new StoreSettingsResponse(s.Id.ToString(), s.StoreName, s.BusinessName, s.Address, s.ContactNumber, s.Tin, s.CurrencyLabel, s.ReceiptWidthMm, s.ReceiptHeader, s.ReceiptFooter, s.PrinterMode));
        });

        app.MapPut("/api/settings/store", async (HttpContext context, LocalApiSessionService sessions, StoreSettingsUpdateApiRequest request, CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.SettingsManage) is IResult denied) return denied;
            var current = await reader.GetCurrentAsync(ct);
            if (current.IsFailure || current.Value is null) return Results.NotFound();
            var s = current.Value;
            var result = await writer.UpdateAsync(new StoreSettingsUpdateRequest(s.Id, request.StoreName, request.BusinessName, request.Address, request.ContactNumber, request.Tin, request.CurrencyLabel, request.ReceiptWidthMm, s.TaxModeEnabled, s.ShiftModeEnabled, request.ReceiptHeader, request.ReceiptFooter, request.PrinterMode), ct);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message });
        });
        return app;
    }
}

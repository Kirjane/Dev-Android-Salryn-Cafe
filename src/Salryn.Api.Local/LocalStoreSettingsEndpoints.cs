using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
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
            if (LocalApiPermissionGuards.RequireAuthenticatedSession(context, sessions) is IResult denied) return denied;
            var result = await reader.GetCurrentAsync(ct);
            if (result.IsFailure) return Results.BadRequest(new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message });
            var s = result.Value;
            return s is null
                ? Results.NotFound()
                : Results.Ok(new StoreSettingsResponse(
                    s.Id.ToString(),
                    s.StoreName,
                    s.BusinessName,
                    s.Address,
                    s.ContactNumber,
                    s.Tin,
                    s.CurrencyLabel,
                    s.ReceiptWidthMm,
                    s.ReceiptHeader,
                    s.ReceiptFooter,
                    s.PrinterMode,
                    s.WindowsPrinterName,
                    s.HasLogo,
                    s.LogoContentType));
        });

        app.MapPut("/api/settings/store", async (HttpContext context, LocalApiSessionService sessions, StoreSettingsUpdateApiRequest request, CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.SettingsManage) is IResult denied) return denied;
            var current = await reader.GetCurrentAsync(ct);
            if (current.IsFailure || current.Value is null) return Results.NotFound();
            var s = current.Value;
            var result = await writer.UpdateAsync(new StoreSettingsUpdateRequest(s.Id, request.StoreName, request.BusinessName, request.Address, request.ContactNumber, request.Tin, request.CurrencyLabel, request.ReceiptWidthMm, s.TaxModeEnabled, s.ShiftModeEnabled, request.ReceiptHeader, request.ReceiptFooter, request.PrinterMode, request.WindowsPrinterName), ct);
            if (result.IsSuccess)
            {
                string? sessionId = context.Request.Cookies[LocalApiSessionService.SessionCookieName];
                Guid? userId = sessions.GetRequiredSession(sessionId)?.UserId;
                await OperatorAuditTrail.TryWriteAsync(
                        composition.ConnectionFactory,
                        userId,
                        OperatorAuditActions.StoreSettingsUpdated,
                        "StoreSettings",
                        s.Id.ToString("D"),
                        new
                        {
                            storeName = request.StoreName,
                            businessName = request.BusinessName,
                            currencyLabel = request.CurrencyLabel,
                            receiptWidthMm = request.ReceiptWidthMm,
                            printerMode = request.PrinterMode
                        },
                        oldValue: new
                        {
                            storeName = s.StoreName,
                            businessName = s.BusinessName,
                            currencyLabel = s.CurrencyLabel,
                            receiptWidthMm = s.ReceiptWidthMm,
                            printerMode = s.PrinterMode
                        },
                        reason: "Store settings updated.",
                        cancellationToken: ct)
                    .ConfigureAwait(false);
            }

            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { code = result.ErrorDetail.Code, error = result.ErrorDetail.Message });
        });
        return app;
    }
}

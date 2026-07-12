using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Services;

namespace Salryn.Api.Local;

internal static class LocalReceiptPrinterEndpoints
{
    public static WebApplication MapLocalReceiptPrinterEndpoints(this WebApplication app)
    {
        app.MapPost("/api/receipts/{saleId:guid}/print", async (
            Guid saleId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(
                    httpContext,
                    sessions,
                    new[] { LocalApiPermissions.SalesCreate, LocalApiPermissions.SalesViewHistory }) is IResult unauthorized)
            {
                return unauthorized;
            }

            PosReceiptPrintResult result = await hostOperator.ReceiptPrintService
                .PrintSaleReceiptAsync(saleId, cancellationToken)
                .ConfigureAwait(false);

            return ToReceiptPrintResult(result, "Receipt.PrintSale");
        });

        app.MapPost("/api/receipts/test-print", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            HostOperatorApiServices hostOperator,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(
                    httpContext,
                    sessions,
                    new[] { LocalApiPermissions.SalesCreate, LocalApiPermissions.SalesViewHistory }) is IResult unauthorized)
            {
                return unauthorized;
            }

            PosReceiptPrintResult result = await hostOperator.ReceiptPrintService
                .TestPrintAsync(cancellationToken)
                .ConfigureAwait(false);

            return ToReceiptPrintResult(result, "Receipt.TestPrint");
        });

        return app;
    }

    private static IResult ToReceiptPrintResult(PosReceiptPrintResult result, string codePrefix)
    {
        ReceiptPrintResponse response = new(
            result.Succeeded,
            result.Status,
            result.SaleId?.ToString(),
            result.ReceiptNumber,
            result.Source,
            result.AdapterName,
            result.Message,
            result.ErrorMessage,
            result.PrintedAtUtc?.ToString("O"));

        string code = result.Status switch
        {
            PosReceiptPrintStatuses.Succeeded => codePrefix + ".Succeeded",
            PosReceiptPrintStatuses.DisabledUnavailable => codePrefix + ".DisabledUnavailable",
            PosReceiptPrintStatuses.NotAttempted => codePrefix + ".NotAttempted",
            _ => codePrefix + ".Failed"
        };

        return Results.Ok(new ReceiptPrintEnvelope(code, result.Message, response));
    }
}

using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local invoice output preview/export. Does not allocate numbers, mutate sales, or enable production activation.
/// </summary>
public static class LocalOfficialInvoiceOutputEndpoints
{
    public static void MapLocalOfficialInvoiceOutputEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapPost("/api/official/invoice-output/generate", async (
            InvoiceOutputApiRequest body,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            if (ApiAuthExtensions.RequireAuthenticatedSession(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            IInvoiceOutputService service = new InvoiceOutputService(
                new SqliteOfficialInvoiceSnapshotReadService(connectionFactory));

            // Client cannot disable watermark or force production activation.
            InvoiceOutputMode mode = body.Mode == "ProductionCapable"
                ? InvoiceOutputMode.ProductionCapable
                : InvoiceOutputMode.InternalDemo;

            List<InvoiceOutputFormat> formats = new();
            if (body.IncludeHtml != false)
            {
                formats.Add(InvoiceOutputFormat.Html);
            }

            if (body.IncludeThermalText != false)
            {
                formats.Add(InvoiceOutputFormat.ThermalText);
            }

            if (formats.Count == 0)
            {
                formats.Add(InvoiceOutputFormat.Html);
            }

            Guid? saleId = null;
            if (!string.IsNullOrWhiteSpace(body.SaleId) && Guid.TryParse(body.SaleId, out Guid parsedSale))
            {
                saleId = parsedSale;
            }

            Result<InvoiceOutputPackage> result = await service.GenerateAsync(
                new InvoiceOutputRequest(
                    saleId,
                    body.OfficialDocumentNumber,
                    mode,
                    formats),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Results.Json(
                    new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                    statusCode: MapStatus(result.ErrorDetail.Type));
            }

            InvoiceOutputPackage package = result.Value;
            return Results.Json(new
            {
                packageId = package.Manifest.PackageId,
                saleId = package.Manifest.SaleId,
                journalEntryId = package.Manifest.JournalEntryId,
                officialDocumentNumber = package.Manifest.OfficialDocumentNumber,
                taxRegistrationMode = package.Manifest.TaxpayerMode,
                invoiceVariant = package.Manifest.InvoiceVariant,
                outputMode = package.Manifest.OutputMode.ToString(),
                internalWatermarkApplied = package.Manifest.InternalWatermarkApplied,
                productionActivationEnabled = package.Manifest.ProductionActivationEnabled,
                liveOfficialPostingAllowed = package.Manifest.LiveOfficialPostingAllowed,
                complianceClaimed = package.Manifest.ComplianceClaimed,
                canonicalContentSha256 = package.Manifest.CanonicalContentSha256,
                missingOptionalFields = package.Manifest.MissingOptionalFields,
                validationNotes = package.Manifest.ValidationNotes,
                noComplianceClaimNote = package.Manifest.NoComplianceClaimNote,
                documentTitle = package.Model.DocumentTitle,
                registrationPhrase = package.Model.RegistrationPhrase,
                nonVatDisclaimer = package.Model.NonVatDisclaimer,
                artifacts = package.Artifacts.Select(a => new
                {
                    format = a.Format.ToString(),
                    contentType = a.ContentType,
                    fileNameHint = a.FileNameHint,
                    contentSha256 = a.ContentSha256,
                    utf8Text = a.Utf8Text
                }).ToArray()
            });
        });
    }

    private static int MapStatus(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError
    };

    public sealed record InvoiceOutputApiRequest(
        string? SaleId,
        string? OfficialDocumentNumber,
        string? Mode,
        bool? IncludeHtml,
        bool? IncludeThermalText);
}

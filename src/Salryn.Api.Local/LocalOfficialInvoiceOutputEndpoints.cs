using System.Text;
using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local invoice output: generate JSON package + raw download/print-view bytes.
/// Server-owned InternalDemo while production activation is OFF. No number allocation.
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

            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    SalrynPermissions.OfficialOutputRun) is IResult unauthorized)
            {
                return unauthorized;
            }

            if (string.Equals(body.Mode, "ProductionCapable", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new
                    {
                        code = "InvoiceOutput.ClientModeNotAllowed",
                        message = "Output trust mode is server-owned. Client cannot request ProductionCapable."
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            Result<InvoiceOutputPackage> result = await GenerateInternalDemoAsync(
                connectionFactory,
                body.SaleId,
                body.OfficialDocumentNumber,
                includeHtml: body.IncludeHtml != false,
                includeThermal: body.IncludeThermalText != false,
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Results.Json(
                    new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                    statusCode: MapStatus(result.ErrorDetail.Type));
            }

            InvoiceOutputPackage package = result.Value;
            return Results.Json(ToJsonPackage(package));
        });

        // Raw HTML download (attachment) — server-owned InternalDemo.
        app.MapGet("/api/official/invoice-output/html", async (
            string? saleId,
            string? officialDocumentNumber,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            return await DeliverArtifactAsync(
                connectionFactory,
                httpContext,
                sessions,
                saleId,
                officialDocumentNumber,
                InvoiceOutputFormat.Html,
                inline: false,
                cancellationToken).ConfigureAwait(false);
        });

        // Print-view HTML (inline) for browser print workflow.
        app.MapGet("/api/official/invoice-output/print-view", async (
            string? saleId,
            string? officialDocumentNumber,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            return await DeliverArtifactAsync(
                connectionFactory,
                httpContext,
                sessions,
                saleId,
                officialDocumentNumber,
                InvoiceOutputFormat.Html,
                inline: true,
                cancellationToken).ConfigureAwait(false);
        });

        // Raw thermal text download.
        app.MapGet("/api/official/invoice-output/thermal", async (
            string? saleId,
            string? officialDocumentNumber,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            return await DeliverArtifactAsync(
                connectionFactory,
                httpContext,
                sessions,
                saleId,
                officialDocumentNumber,
                InvoiceOutputFormat.ThermalText,
                inline: false,
                cancellationToken).ConfigureAwait(false);
        });
    }

    private static async Task<IResult> DeliverArtifactAsync(
        SqliteConnectionFactory connectionFactory,
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string? saleId,
        string? officialDocumentNumber,
        InvoiceOutputFormat format,
        bool inline,
        CancellationToken cancellationToken)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        if (LocalApiPermissionGuards.RequirePermission(
                httpContext,
                sessions,
                SalrynPermissions.OfficialOutputRun) is IResult unauthorized)
        {
            return unauthorized;
        }

        Result<InvoiceOutputPackage> result = await GenerateInternalDemoAsync(
            connectionFactory,
            saleId,
            officialDocumentNumber,
            includeHtml: format == InvoiceOutputFormat.Html,
            includeThermal: format == InvoiceOutputFormat.ThermalText,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.Json(
                new { code = result.ErrorDetail.Code, message = result.ErrorDetail.Message },
                statusCode: MapStatus(result.ErrorDetail.Type));
        }

        InvoiceOutputArtifact? artifact = result.Value.Artifacts.FirstOrDefault(a => a.Format == format);
        if (artifact is null)
        {
            return Results.Json(
                new { code = "InvoiceOutput.ArtifactMissing", message = "Requested artifact was not generated." },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(artifact.Utf8Text);
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers["X-Salryn-Content-Sha256"] = artifact.ContentSha256;
        httpContext.Response.Headers["X-Salryn-Official-Document-Number"] = result.Value.Model.OfficialDocumentNumber;
        httpContext.Response.Headers["X-Salryn-Internal-Watermark"] =
            result.Value.Manifest.InternalWatermarkApplied ? "1" : "0";

        string fileName = SanitizeFileName(artifact.FileNameHint);
        if (inline)
        {
            httpContext.Response.Headers.ContentDisposition = "inline; filename=\"" + fileName + "\"";
            return Results.File(bytes, contentType: artifact.ContentType);
        }

        return Results.File(
            bytes,
            contentType: artifact.ContentType,
            fileDownloadName: fileName);
    }

    private static async Task<Result<InvoiceOutputPackage>> GenerateInternalDemoAsync(
        SqliteConnectionFactory connectionFactory,
        string? saleIdRaw,
        string? officialDocumentNumber,
        bool includeHtml,
        bool includeThermal,
        CancellationToken cancellationToken)
    {
        // Server-owned trust mode: always InternalDemo on public endpoints while activation is OFF.
        List<InvoiceOutputFormat> formats = new();
        if (includeHtml)
        {
            formats.Add(InvoiceOutputFormat.Html);
        }

        if (includeThermal)
        {
            formats.Add(InvoiceOutputFormat.ThermalText);
        }

        if (formats.Count == 0)
        {
            formats.Add(InvoiceOutputFormat.Html);
        }

        Guid? saleId = null;
        if (!string.IsNullOrWhiteSpace(saleIdRaw) && Guid.TryParse(saleIdRaw, out Guid parsedSale))
        {
            saleId = parsedSale;
        }

        IInvoiceOutputService service = new InvoiceOutputService(
            new SqliteOfficialInvoiceSnapshotReadService(connectionFactory));

        return await service.GenerateAsync(
            new InvoiceOutputRequest(
                saleId,
                officialDocumentNumber,
                InvoiceOutputMode.InternalDemo,
                formats),
            cancellationToken).ConfigureAwait(false);
    }

    private static object ToJsonPackage(InvoiceOutputPackage package) =>
        new
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
            download = new
            {
                html = "/api/official/invoice-output/html",
                thermal = "/api/official/invoice-output/thermal",
                printView = "/api/official/invoice-output/print-view"
            },
            artifacts = package.Artifacts.Select(a => new
            {
                format = a.Format.ToString(),
                contentType = a.ContentType,
                fileNameHint = a.FileNameHint,
                contentSha256 = a.ContentSha256,
                utf8Text = a.Utf8Text
            }).ToArray()
        };

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new();
        foreach (char c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "invoice" : result;
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

using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.Api.Local;

/// <summary>
/// Host-local internal dry-run package + report preview API (RMO-BIR-M8).
/// No sale/sequence/EJ live mutation. No legal output. No LAN/Remote POS.
/// </summary>
public static class LocalOfficialDryRunPackageEndpoints
{
    public static void MapLocalOfficialDryRunPackageEndpoints(
        this WebApplication app,
        SqliteConnectionFactory connectionFactory)
    {
        app.MapGet("/api/official/dry-run/packages", async (
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

            IHostOfficialDryRunPackageService service =
                new SqliteHostOfficialDryRunPackageService(connectionFactory);
            Result<IReadOnlyList<OfficialDryRunPackageSummaryContract>> result =
                await service.ListRecentAsync(20, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Fail(result.ErrorDetail);
            }

            return Results.Ok(new
            {
                dryRunOnly = true,
                nonOfficial = true,
                officialReceiptIssued = false,
                liveOfficialPostingAllowed = false,
                officialCheckoutAllocationEnabled = false,
                checkoutUsesAllocator = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                complianceClaimed = false,
                packages = result.Value.Select(p => new
                {
                    id = p.Id,
                    publicId = p.PublicId,
                    createdAtUtc = p.CreatedAtUtc,
                    sourceMode = p.SourceMode.ToString(),
                    status = p.Status.ToString(),
                    summary = p.Summary
                }).ToArray(),
                note = "Internal dry-run packages only. Not an official receipt or invoice."
            });
        });

        app.MapGet("/api/official/dry-run/packages/{id:guid}", async (
            Guid id,
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

            IHostOfficialDryRunPackageService service =
                new SqliteHostOfficialDryRunPackageService(connectionFactory);
            Result<OfficialDryRunPackageContract> result =
                await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            return ToPackageResult(result);
        });

        app.MapPost("/api/official/dry-run/packages", async (
            OfficialDryRunPackageCreateRequest? body,
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

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");

            IHostOfficialDryRunPackageService service =
                new SqliteHostOfficialDryRunPackageService(connectionFactory);
            Result<OfficialDryRunPackageContract> result = await service.CreateAsync(
                body ?? new OfficialDryRunPackageCreateRequest(null),
                userId,
                cancellationToken).ConfigureAwait(false);
            return ToPackageResult(result);
        });

        app.MapGet("/api/official/source-sale/{saleId:guid}/dry-run-readonly", async (
            Guid saleId,
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

            IHostOfficialSourceSaleReadOnlyAdapter adapter =
                new SqliteHostOfficialSourceSaleReadOnlyAdapter(connectionFactory);
            Result<OfficialSourceSaleReadOnlyResult> result = await adapter.ReadAsync(
                new OfficialSourceSaleReadOnlyRequest(saleId, "OFFICIAL_INVOICE"),
                cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Fail(result.ErrorDetail);
            }

            OfficialSourceSaleReadOnlyResult r = result.Value;
            return Results.Ok(new
            {
                contractVersion = r.ContractVersion,
                dryRunOnly = true,
                nonOfficial = true,
                sourceSaleReadOnly = true,
                liveOfficialPostingAllowed = false,
                officialCheckoutAllocationEnabled = false,
                checkoutUsesAllocator = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                officialReceiptIssued = false,
                complianceClaimed = false,
                snapshot = r.Snapshot is null
                    ? null
                    : new
                    {
                        saleId = r.Snapshot.SaleId,
                        operationalReceiptNo = r.Snapshot.OperationalReceiptNo,
                        saleDateUtc = r.Snapshot.SaleDateUtc,
                        cashierDisplayName = r.Snapshot.CashierDisplayName,
                        status = r.Snapshot.Status,
                        lineCount = r.Snapshot.Lines.Count,
                        taxSummary = r.Snapshot.TaxSummary
                    },
                warnings = r.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
                blockers = r.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
                summary = r.Summary,
                note = "Source-sale read-only only. Not an official receipt or invoice. No mutation."
            });
        });

        app.MapPost("/api/official/dry-run/packages/from-sale", async (
            FromSaleBody? body,
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

            if (body is null || body.SaleId == Guid.Empty)
            {
                return Results.Json(
                    new { error = "saleId is required.", code = "HostOfficialDryRunPackage.SaleIdRequired" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            string? userId = sessions.GetRequiredSession(
                httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName])?.UserId.ToString("D");

            IHostOfficialDryRunPackageService service =
                new SqliteHostOfficialDryRunPackageService(connectionFactory);
            Result<OfficialDryRunPackageContract> result = await service.CreateFromSaleAsync(
                body.SaleId,
                userId,
                cancellationToken).ConfigureAwait(false);
            return ToPackageResult(result);
        });

        app.MapGet("/api/official/internal-reports/{packageId:guid}", async (
            Guid packageId,
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

            IHostOfficialDryRunPackageService service =
                new SqliteHostOfficialDryRunPackageService(connectionFactory);
            Result<OfficialDryRunPackageContract> result =
                await service.GetByIdAsync(packageId, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Fail(result.ErrorDetail);
            }

            OfficialDryRunPackageContract p = result.Value;
            return Results.Ok(new
            {
                packageId = p.Id,
                publicId = p.PublicId,
                watermark = OfficialSafetyConstants.InternalWatermark,
                dryRunOnly = true,
                nonOfficial = true,
                officialReceiptIssued = false,
                liveOfficialPostingAllowed = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                complianceClaimed = false,
                reportPreview = p.ReportPreview is null
                    ? null
                    : new
                    {
                        watermark = p.ReportPreview.Watermark,
                        receiptPlainText = p.ReportPreview.Receipt.PlainTextPreview,
                        xReadingPlainText = p.ReportPreview.XReading.PlainTextPreview,
                        zReadingPlainText = p.ReportPreview.ZReading.PlainTextPreview,
                        journalPlainText = p.ReportPreview.JournalExport.PlainTextPreview,
                        voidRefundPlainText = p.ReportPreview.VoidRefund.PlainTextPreview,
                        salesSummaryPlainText = OfficialInternalSalesSummaryPreviewRenderer.RenderPlainText(p.Plan),
                        summary = p.ReportPreview.Summary
                    },
                note = "Internal reports only. Not an official receipt or invoice."
            });
        });

        app.MapPost("/api/official/internal-reports/preview", async (
            OfficialDryRunPostingRequest? body,
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

            OfficialDryRunPostingRequest request = body ?? new OfficialDryRunPostingRequest(
                DocumentKind: "OFFICIAL_INVOICE",
                OperationalPosNumberHint: null,
                TaxpayerNameHint: null,
                Lines: Array.Empty<OfficialDryRunLineItem>(),
                Totals: null);

            IHostOfficialDryRunPostingPlanner planner =
                new SqliteHostOfficialDryRunPostingPlanner(connectionFactory);
            Result<OfficialDryRunPostingPlan> planResult =
                await planner.BuildPlanAsync(request, cancellationToken).ConfigureAwait(false);
            if (planResult.IsFailure)
            {
                return Fail(planResult.ErrorDetail);
            }

            IHostOfficialReportPreviewService previewService = new HostOfficialReportPreviewService();
            Result<OfficialReportPreviewPackageContract> previewResult =
                await previewService.BuildPreviewAsync(planResult.Value, cancellationToken).ConfigureAwait(false);
            if (previewResult.IsFailure)
            {
                return Fail(previewResult.ErrorDetail);
            }

            OfficialReportPreviewPackageContract p = previewResult.Value;
            return Results.Ok(new
            {
                contractVersion = p.ContractVersion,
                watermark = OfficialSafetyConstants.InternalWatermark,
                dryRunOnly = true,
                nonOfficial = true,
                officialReceiptIssued = false,
                liveOfficialPostingAllowed = false,
                officialCheckoutAllocationEnabled = false,
                checkoutUsesAllocator = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                reportEngineSelected = false,
                complianceClaimed = false,
                salesSummaryPlainText = OfficialInternalSalesSummaryPreviewRenderer.RenderPlainText(planResult.Value),
                receipt = new
                {
                    documentKind = p.Receipt.DocumentKind,
                    previewOfficialNumber = p.Receipt.PreviewOfficialNumber,
                    plainTextPreview = p.Receipt.PlainTextPreview,
                    officialReceiptIssued = false
                },
                xReading = new { plainTextPreview = p.XReading.PlainTextPreview, resetsCounters = false, summary = p.XReading.Summary },
                zReading = new { plainTextPreview = p.ZReading.PlainTextPreview, resetsCounters = false, summary = p.ZReading.Summary },
                journalExport = new { plainTextPreview = p.JournalExport.PlainTextPreview, liveAppended = false, exportReady = false, summary = p.JournalExport.Summary },
                voidRefund = new { plainTextPreview = p.VoidRefund.PlainTextPreview, liveIntegrated = false, placeholderReasonCodes = p.VoidRefund.PlaceholderReasonCodes, summary = p.VoidRefund.Summary },
                summary = p.Summary,
                note = "Internal dry-run only. Not an official receipt or invoice. No official number is issued."
            });
        });

        app.MapPost("/api/official/dry-run/report-preview", async (
            OfficialDryRunPostingRequest? body,
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

            OfficialDryRunPostingRequest request = body ?? new OfficialDryRunPostingRequest(
                DocumentKind: "OFFICIAL_INVOICE",
                OperationalPosNumberHint: null,
                TaxpayerNameHint: null,
                Lines: Array.Empty<OfficialDryRunLineItem>(),
                Totals: null);

            IHostOfficialDryRunPostingPlanner planner =
                new SqliteHostOfficialDryRunPostingPlanner(connectionFactory);
            Result<OfficialDryRunPostingPlan> planResult =
                await planner.BuildPlanAsync(request, cancellationToken).ConfigureAwait(false);
            if (planResult.IsFailure)
            {
                return Fail(planResult.ErrorDetail);
            }

            IHostOfficialReportPreviewService previewService = new HostOfficialReportPreviewService();
            Result<OfficialReportPreviewPackageContract> previewResult =
                await previewService.BuildPreviewAsync(planResult.Value, cancellationToken).ConfigureAwait(false);
            if (previewResult.IsFailure)
            {
                return Fail(previewResult.ErrorDetail);
            }

            OfficialReportPreviewPackageContract p = previewResult.Value;
            return Results.Ok(new
            {
                contractVersion = p.ContractVersion,
                watermark = p.Watermark,
                dryRunOnly = true,
                nonOfficial = true,
                officialReceiptIssued = false,
                liveOfficialPostingAllowed = false,
                officialCheckoutAllocationEnabled = false,
                checkoutUsesAllocator = false,
                sequenceWillMutate = false,
                salesWillMutate = false,
                ejWillAppendLive = false,
                outputWillGenerate = false,
                reportEngineSelected = false,
                complianceClaimed = false,
                receipt = new
                {
                    documentKind = p.Receipt.DocumentKind,
                    previewOfficialNumber = p.Receipt.PreviewOfficialNumber,
                    plainTextPreview = p.Receipt.PlainTextPreview,
                    officialReceiptIssued = false
                },
                xReading = new { plainTextPreview = p.XReading.PlainTextPreview, resetsCounters = false, summary = p.XReading.Summary },
                zReading = new { plainTextPreview = p.ZReading.PlainTextPreview, resetsCounters = false, summary = p.ZReading.Summary },
                journalExport = new { plainTextPreview = p.JournalExport.PlainTextPreview, liveAppended = false, exportReady = false, summary = p.JournalExport.Summary },
                voidRefund = new { plainTextPreview = p.VoidRefund.PlainTextPreview, liveIntegrated = false, placeholderReasonCodes = p.VoidRefund.PlaceholderReasonCodes, summary = p.VoidRefund.Summary },
                summary = p.Summary,
                note = "Internal dry-run only. Not an official receipt or invoice. No official number is issued."
            });
        });
    }

    private static IResult ToPackageResult(Result<OfficialDryRunPackageContract> result)
    {
        if (result.IsFailure)
        {
            return Fail(result.ErrorDetail);
        }

        OfficialDryRunPackageContract p = result.Value;
        return Results.Ok(new
        {
            contractVersion = p.ContractVersion,
            id = p.Id,
            publicId = p.PublicId,
            createdAtUtc = p.CreatedAtUtc,
            sourceMode = p.SourceMode.ToString(),
            sourceSaleId = p.SourceSaleId,
            status = p.Status.ToString(),
            dryRunOnly = true,
            nonOfficial = true,
            sourceSaleReadOnly = p.SourceMode == OfficialDryRunPackageSourceMode.OperationalSaleReadOnly,
            officialReceiptIssued = false,
            liveOfficialPostingAllowed = false,
            officialCheckoutAllocationEnabled = false,
            checkoutUsesAllocator = false,
            sequenceWillMutate = false,
            salesWillMutate = false,
            ejWillAppendLive = false,
            outputWillGenerate = false,
            complianceClaimed = false,
            plan = new
            {
                status = p.Plan.Status.ToString(),
                saleSource = p.Plan.SaleSource.ToString(),
                documentKind = p.Plan.DocumentKind,
                numbering = p.Plan.Numbering,
                journal = p.Plan.Journal,
                voidRefund = p.Plan.VoidRefund,
                summary = p.Plan.Summary
            },
            reportPreview = p.ReportPreview is null
                ? null
                : new
                {
                    watermark = p.ReportPreview.Watermark,
                    receiptPlainText = p.ReportPreview.Receipt.PlainTextPreview,
                    xReadingPlainText = p.ReportPreview.XReading.PlainTextPreview,
                    zReadingPlainText = p.ReportPreview.ZReading.PlainTextPreview,
                    journalPlainText = p.ReportPreview.JournalExport.PlainTextPreview,
                    voidRefundPlainText = p.ReportPreview.VoidRefund.PlainTextPreview,
                    summary = p.ReportPreview.Summary
                },
            warnings = p.Warnings.Select(w => new { code = w.Code, message = w.Message }).ToArray(),
            blockers = p.Blockers.Select(b => new { code = b.Code, message = b.Message }).ToArray(),
            summary = p.Summary,
            note = "Internal dry-run only. Not an official receipt or invoice. Checkout is not connected."
        });
    }

    private static IResult Fail(Error error)
    {
        int status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Json(new { error = error.Message, code = error.Code }, statusCode: status);
    }

    private sealed record FromSaleBody(Guid SaleId);
}

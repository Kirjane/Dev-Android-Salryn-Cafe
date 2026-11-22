using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Api.Local;

internal static class LocalCafePrintEndpoints
{
    public static TBuilder MapLocalCafePrintEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/print/stations", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.PrintingView) is IResult denied)
            {
                return denied;
            }

            Result<LocalPrintAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafePrint.ListStations(
                        authority.Value.StoreId,
                        authority.Value.ActorUserId));
        });

        app.MapPut("/api/cafe/print/stations/{stationId:guid}", async (
            Guid stationId,
            PrinterStationRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.PrintingStationManage) is IResult denied)
            {
                return denied;
            }

            if (!Enum.TryParse(
                    request.StationKind,
                    ignoreCase: true,
                    out CafePrinterStationKind stationKind))
            {
                return Results.Json(
                    new
                    {
                        code = "CafePrint.StationKindInvalid",
                        error = "StationKind is invalid."
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            Result<LocalPrintAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            return ToResult(
                composition.CafePrint.PublishStation(
                    new CafePrinterStationMutation(
                        request.OperationId.GetValueOrDefault(),
                        stationId,
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        request.Name,
                        stationKind,
                        request.RouteKey,
                        request.IsActive,
                        request.ExpectedVersion,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow)));
        });

        app.MapPost("/api/cafe/print/jobs/receipt", async (
            ReceiptPrintJobRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (!TryCopyKind(
                    request.CopyKind,
                    out CafePrintCopyKind copyKind))
            {
                return CopyKindInvalid();
            }

            string permission =
                copyKind == CafePrintCopyKind.Original
                    ? LocalApiPermissions.PrintingPrint
                    : LocalApiPermissions.PrintingReprint;

            if (Require(
                    httpContext,
                    sessions,
                    permission) is IResult denied)
            {
                return denied;
            }

            Result<LocalPrintAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Guid operationId = request.OperationId.GetValueOrDefault();
            Result<CafePrintJobMutationResult> result =
                await composition.CafePrint.CreateReceiptJobAsync(
                    new CafeReceiptPrintJobMutation(
                        operationId,
                        ResolveJobId(request.JobId, operationId, "receipt"),
                        authority.Value.DeviceId,
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        request.StationId,
                        request.SaleId,
                        copyKind,
                        request.ParentJobId,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);

            return ToResult(result);
        });

        app.MapPost("/api/cafe/print/jobs/kitchen", async (
            KitchenPrintJobRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (!TryCopyKind(
                    request.CopyKind,
                    out CafePrintCopyKind copyKind))
            {
                return CopyKindInvalid();
            }

            string permission =
                copyKind == CafePrintCopyKind.Original
                    ? LocalApiPermissions.PrintingPrint
                    : LocalApiPermissions.PrintingReprint;

            if (Require(
                    httpContext,
                    sessions,
                    permission) is IResult denied)
            {
                return denied;
            }

            Result<LocalPrintAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Guid operationId = request.OperationId.GetValueOrDefault();
            return ToResult(
                composition.CafePrint.CreateKitchenTicketJob(
                    new CafeKitchenPrintJobMutation(
                        operationId,
                        ResolveJobId(request.JobId, operationId, "kitchen"),
                        authority.Value.DeviceId,
                        authority.Value.StoreId,
                        authority.Value.ActorUserId,
                        request.StationId,
                        request.OrderId,
                        request.OrderRevisionId,
                        copyKind,
                        request.ParentJobId,
                        request.OccurredAtUtc ?? DateTimeOffset.UtcNow)));
        });

        app.MapPost("/api/cafe/print/jobs/{jobId:guid}/attempt", async (
            Guid jobId,
            PrintAttemptRequest? request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            Result<LocalPrintAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
                return ToError(authority.ErrorDetail);

            Result<CafePrintJob?> jobResult =
                composition.CafePrint.FindJob(
                    jobId,
                    authority.Value.ActorUserId);

            if (jobResult.IsFailure)
                return ToError(jobResult.ErrorDetail);

            if (jobResult.Value is null)
            {
                return Results.Json(
                    new
                    {
                        code = "CafePrint.JobNotFound",
                        error = "Print job was not found."
                    },
                    statusCode: StatusCodes.Status404NotFound);
            }

            string permission =
                jobResult.Value.CopyKind == CafePrintCopyKind.Original
                    ? LocalApiPermissions.PrintingPrint
                    : LocalApiPermissions.PrintingReprint;

            if (Require(
                    httpContext,
                    sessions,
                    permission) is IResult denied)
            {
                return denied;
            }

            IReceiptPrinterAdapter adapter =
                CreateConfiguredReceiptPrinterAdapter(
                    composition);

            return ToResult(
                await composition.CafePrint.ExecuteAttemptAsync(
                    jobId,
                    authority.Value.DeviceId,
                    authority.Value.ActorUserId,
                    adapter,
                    cancellationToken).ConfigureAwait(false));
        });

        app.MapGet("/api/cafe/print/jobs/{jobId:guid}", async (
            Guid jobId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(
                    httpContext,
                    sessions,
                    LocalApiPermissions.PrintingView) is IResult denied)
            {
                return denied;
            }

            Result<LocalPrintAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(
                    composition.CafePrint.FindJob(
                        jobId,
                        authority.Value.ActorUserId));
        });

        return app;
    }

    private static IReceiptPrinterAdapter
        CreateConfiguredReceiptPrinterAdapter(
            SalrynLocalComposition composition)
    {
        SqliteRepositoryFactory repositories =
            new(composition.ConnectionFactory);

        Result<StoreSettingsReadModel?> settingsResult =
            repositories
                .CreateStoreSettingsReadRepository()
                .GetCurrentAsync()
                .GetAwaiter()
                .GetResult();

        if (settingsResult.IsFailure)
        {
            return new DisabledReceiptPrinterAdapter(
                "Printing is unavailable because store printer settings could not be read: " +
                settingsResult.ErrorDetail.Message,
                "ReceiptPrinterSettings.ReadFailed");
        }

        return ReceiptPrinterAdapterFactory.Create(
            settingsResult.Value);
    }

    private static async Task<Result<LocalPrintAuthority>>
        ResolveAuthorityAsync(
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken)
    {
        LocalApiSession? session =
            sessions.GetRequiredSession(
                httpContext.Request.Cookies[
                    LocalApiSessionService.SessionCookieName]);

        if (session is null)
        {
            return Result<LocalPrintAuthority>.Fail(
                Error.Unauthorized(
                    "Auth.AuthenticationRequired",
                    "Authenticated local operator session is required."));
        }

        Result<DeploymentIdentitySnapshot?> deployment =
            await composition.DeploymentIdentityRepository
                .GetCurrentAsync(cancellationToken)
                .ConfigureAwait(false);

        if (deployment.IsFailure)
        {
            return Result<LocalPrintAuthority>.Fail(
                deployment.ErrorDetail);
        }

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalPrintAuthority>.Fail(
                Error.Conflict(
                    "CafePrint.DeploymentIdentityMissing",
                    "Configured StoreId and local TerminalId are required."));
        }

        return Result<LocalPrintAuthority>.Success(
            new LocalPrintAuthority(
                deployment.Value.Store.StoreId,
                deployment.Value.Terminal.TerminalId,
                session.UserId));
    }

    private static bool TryCopyKind(
        string? value,
        out CafePrintCopyKind copyKind) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out copyKind) &&
        Enum.IsDefined(copyKind);

    private static IResult CopyKindInvalid() =>
        Results.Json(
            new
            {
                code = "CafePrint.CopyKindInvalid",
                error = "CopyKind must be Original, Reprint, or VoidedReprint."
            },
            statusCode: StatusCodes.Status400BadRequest);

    private static Guid ResolveJobId(Guid? value, Guid operationId, string kind)
    {
        if (value.GetValueOrDefault() != Guid.Empty)
        {
            return value!.Value;
        }

        if (operationId == Guid.Empty)
        {
            return Guid.NewGuid();
        }

        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"print-job|{kind}|{operationId:D}"));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static IResult? Require(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission) =>
        LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            permission);

    private static IResult ToResult<T>(
        Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation =>
                StatusCodes.Status400BadRequest,
            ErrorType.NotFound =>
                StatusCodes.Status404NotFound,
            ErrorType.Conflict =>
                StatusCodes.Status409Conflict,
            ErrorType.Unauthorized =>
                StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden =>
                StatusCodes.Status403Forbidden,
            _ =>
                StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new
            {
                code = error.Code,
                error = error.Message
            },
            statusCode: statusCode);
    }

    private sealed record LocalPrintAuthority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId);

    private sealed record PrinterStationRequest(
        Guid? OperationId,
        string Name,
        string StationKind,
        string? RouteKey,
        bool IsActive,
        long ExpectedVersion,
        DateTimeOffset? OccurredAtUtc);

    private sealed record ReceiptPrintJobRequest(
        Guid? OperationId,
        Guid? JobId,
        Guid StationId,
        Guid SaleId,
        string CopyKind,
        Guid? ParentJobId,
        DateTimeOffset? OccurredAtUtc);

    private sealed record KitchenPrintJobRequest(
        Guid? OperationId,
        Guid? JobId,
        Guid StationId,
        Guid OrderId,
        Guid OrderRevisionId,
        string CopyKind,
        Guid? ParentJobId,
        DateTimeOffset? OccurredAtUtc);

    private sealed record PrintAttemptRequest(Guid? OperationId);
}

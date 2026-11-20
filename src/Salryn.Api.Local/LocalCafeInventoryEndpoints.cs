using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Models;

namespace Salryn.Api.Local;

internal static class LocalCafeInventoryEndpoints
{
    public static TBuilder MapLocalCafeInventoryEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/inventory/low-stock", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.InventoryView) is IResult denied)
            {
                return denied;
            }

            return ToResult(composition.CafeInventory.GetLowStock());
        });

        app.MapGet("/api/cafe/suppliers", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.SuppliersView) is IResult denied)
            {
                return denied;
            }

            return ToResult(composition.SupplierDirectory.GetSuppliers());
        });

        app.MapPost("/api/cafe/suppliers", (
            SupplierMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.SuppliersManage) is IResult denied)
            {
                return denied;
            }

            return ToResult(composition.SupplierDirectory.SaveSupplier(request));
        });

        app.MapPost("/api/cafe/suppliers/publish", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.SuppliersManage) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(composition.SupplierDirectory.PublishCurrent(authority.Value.StoreId));
        });

        app.MapGet("/api/cafe/suppliers/publication/current", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.SuppliersView) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            return authority.IsFailure
                ? ToError(authority.ErrorDetail)
                : ToResult(composition.SupplierDirectory.GetCurrentPublication(authority.Value.StoreId));
        });

        app.MapPost("/api/cafe/purchases/receive", async (
            PurchaseReceiveRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.PurchasingReceive) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            PurchaseReceiptMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                request.PurchaseReceiptId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : request.PurchaseReceiptId!.Value,
                authority.Value.DeviceId,
                authority.Value.StoreId,
                request.SupplierId,
                authority.Value.ActorUserId,
                request.ReceivedAtUtc ?? DateTimeOffset.UtcNow,
                request.Lines.Select(line => new PurchaseReceiptLineMutation(
                    line.LineId,
                    line.StockTargetType,
                    line.StockTargetId,
                    line.QuantityReceivedX1000,
                    line.LineAcquisitionCostCents,
                    line.PurchasePackageLabel,
                    line.PurchasePackageQuantityX1000,
                    line.PurchasePackageContentX1000)).ToArray(),
                request.ExternalReference,
                request.Notes,
                request.CorrectionOfPurchaseReceiptId);

            return ToResult(composition.CafeInventory.CommitPurchase(mutation));
        });

        app.MapPost("/api/cafe/inventory/waste", async (
            WasteCommitRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.InventoryWaste) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            InventoryWasteMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                request.WasteEventId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : request.WasteEventId!.Value,
                authority.Value.DeviceId,
                authority.Value.StoreId,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                request.Reason,
                request.Lines.Select(line => new InventoryWasteLineMutation(
                    line.LineId,
                    line.StockTargetType,
                    line.StockTargetId,
                    line.QuantityWastedX1000)).ToArray(),
                request.Notes,
                request.CorrectionOfWasteEventId);

            return ToResult(composition.CafeInventory.CommitWaste(mutation));
        });

        app.MapPost("/api/cafe/inventory/adjust", async (
            AdjustmentCommitRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.InventoryAdjust) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            InventoryAdjustmentMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                request.AdjustmentEventId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : request.AdjustmentEventId!.Value,
                authority.Value.DeviceId,
                authority.Value.StoreId,
                request.StockTargetType,
                request.StockTargetId,
                request.QuantityDeltaX1000,
                request.Reason,
                authority.Value.ActorUserId,
                request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                request.ReferenceType,
                request.ReferenceId,
                request.CorrectionOfReferenceId);

            return ToResult(composition.CafeInventory.CommitAdjustment(mutation));
        });

        app.MapPost("/api/cafe/inventory/counts/draft", async (
            CountDraftRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.InventoryCount) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            InventoryCountStartMutation mutation = new(
                request.CountSessionId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : request.CountSessionId!.Value,
                authority.Value.StoreId,
                authority.Value.ActorUserId,
                request.StartedAtUtc ?? DateTimeOffset.UtcNow,
                request.Notes,
                request.Lines.Select(line => new InventoryCountLineMutation(
                    line.LineId,
                    line.StockTargetType,
                    line.StockTargetId,
                    line.ExpectedBeforeX1000,
                    line.CountedX1000)).ToArray());

            return ToResult(composition.CafeInventory.SaveCountDraft(mutation));
        });

        app.MapPost("/api/cafe/inventory/counts/{countSessionId:guid}/finalize", async (
            Guid countSessionId,
            CountFinalizeRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition,
            CancellationToken cancellationToken) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.InventoryCount) is IResult denied)
            {
                return denied;
            }

            Result<LocalInventoryAuthority> authority =
                await ResolveAuthorityAsync(
                    httpContext,
                    sessions,
                    composition,
                    cancellationToken).ConfigureAwait(false);

            if (authority.IsFailure)
            {
                return ToError(authority.ErrorDetail);
            }

            InventoryCountFinalizeMutation mutation = new(
                request.OperationId.GetValueOrDefault(),
                authority.Value.DeviceId,
                authority.Value.StoreId,
                countSessionId,
                authority.Value.ActorUserId,
                request.FinalizedAtUtc ?? DateTimeOffset.UtcNow);

            return ToResult(composition.CafeInventory.FinalizeCount(mutation));
        });

        app.MapGet("/api/cafe/inventory/counts/{countSessionId:guid}", (
            Guid countSessionId,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (Require(httpContext, sessions, LocalApiPermissions.InventoryView) is IResult denied)
            {
                return denied;
            }

            return ToResult(composition.CafeInventory.GetCount(countSessionId));
        });

        return app;
    }

    private static IResult? Require(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission) =>
        LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            permission);

    private static async Task<Result<LocalInventoryAuthority>> ResolveAuthorityAsync(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        SalrynLocalComposition composition,
        CancellationToken cancellationToken)
    {
        LocalApiSession? session = sessions.GetRequiredSession(
            httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName]);

        if (session is null)
        {
            return Result<LocalInventoryAuthority>.Fail(
                Error.Unauthorized(
                    "Auth.AuthenticationRequired",
                    "Authenticated local operator session is required."));
        }

        Result<DeploymentIdentitySnapshot?> deployment =
            await composition.DeploymentIdentityRepository.GetCurrentAsync(
                cancellationToken).ConfigureAwait(false);

        if (deployment.IsFailure)
        {
            return Result<LocalInventoryAuthority>.Fail(deployment.ErrorDetail);
        }

        if (deployment.Value is null ||
            deployment.Value.Store.StoreId == Guid.Empty ||
            deployment.Value.Terminal.TerminalId == Guid.Empty)
        {
            return Result<LocalInventoryAuthority>.Fail(
                Error.Conflict(
                    "CafeInventory.DeploymentIdentityMissing",
                    "Configured StoreId and local TerminalId are required."));
        }

        return Result<LocalInventoryAuthority>.Success(
            new LocalInventoryAuthority(
                deployment.Value.Store.StoreId,
                deployment.Value.Terminal.TerminalId,
                session.UserId));
    }

    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new { code = error.Code, error = error.Message },
            statusCode: statusCode);
    }

    private sealed record LocalInventoryAuthority(
        Guid StoreId,
        Guid DeviceId,
        Guid ActorUserId);

    private sealed record PurchaseReceiveRequest(
        Guid? OperationId,
        Guid? PurchaseReceiptId,
        Guid SupplierId,
        DateTimeOffset? ReceivedAtUtc,
        string? ExternalReference,
        string? Notes,
        Guid? CorrectionOfPurchaseReceiptId,
        IReadOnlyList<PurchaseReceiveLineRequest> Lines);

    private sealed record PurchaseReceiveLineRequest(
        Guid? LineId,
        string StockTargetType,
        Guid StockTargetId,
        long QuantityReceivedX1000,
        long LineAcquisitionCostCents,
        string? PurchasePackageLabel,
        long? PurchasePackageQuantityX1000,
        long? PurchasePackageContentX1000);

    private sealed record WasteCommitRequest(
        Guid? OperationId,
        Guid? WasteEventId,
        DateTimeOffset? OccurredAtUtc,
        string Reason,
        string? Notes,
        Guid? CorrectionOfWasteEventId,
        IReadOnlyList<WasteLineRequest> Lines);

    private sealed record WasteLineRequest(
        Guid? LineId,
        string StockTargetType,
        Guid StockTargetId,
        long QuantityWastedX1000);

    private sealed record AdjustmentCommitRequest(
        Guid? OperationId,
        Guid? AdjustmentEventId,
        string StockTargetType,
        Guid StockTargetId,
        long QuantityDeltaX1000,
        string Reason,
        DateTimeOffset? OccurredAtUtc,
        string? ReferenceType,
        Guid? ReferenceId,
        Guid? CorrectionOfReferenceId);

    private sealed record CountDraftRequest(
        Guid? CountSessionId,
        DateTimeOffset? StartedAtUtc,
        string? Notes,
        IReadOnlyList<CountLineRequest> Lines);

    private sealed record CountLineRequest(
        Guid? LineId,
        string StockTargetType,
        Guid StockTargetId,
        long ExpectedBeforeX1000,
        long CountedX1000);

    private sealed record CountFinalizeRequest(
        Guid? OperationId,
        DateTimeOffset? FinalizedAtUtc);
}

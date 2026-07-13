using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;

namespace Salryn.Api.Local;

internal static class ReadOnlyEndpointExtensions
{
    public static WebApplication MapReadOnlyOperatorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/products", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            string? search,
            Guid? categoryId,
            Guid? brandId,
            bool? isActive,
            int page = 1,
            int pageSize = 25,
            CancellationToken cancellationToken = default) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ProductsView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.PagedApiResponse<Contracts.ProductListItemResponse>> result =
                await reads.SearchProductsAsync(
                    search, categoryId, brandId, isActive, page, pageSize, cancellationToken)
                    .ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/products/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ProductsView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.ProductDetailResponse> result =
                await reads.GetProductByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ErrorDetail.Code.EndsWith("NotFound", StringComparison.Ordinal)
                    ? Results.NotFound(new { error = result.ErrorDetail.Message })
                    : ApiAuthExtensions.ToErrorResult(result);
            }

            return Results.Ok(result.Value);
        });

        app.MapGet("/api/products/catalog/categories", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ProductsView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<IReadOnlyList<Contracts.CatalogOptionResponse>> result =
                await reads.GetProductCategoriesAsync(cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/products/catalog/brands", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ProductsView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<IReadOnlyList<Contracts.CatalogOptionResponse>> result =
                await reads.GetProductBrandsAsync(cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/products/catalog/units", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ProductsView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<IReadOnlyList<Contracts.CatalogOptionResponse>> result =
                await reads.GetProductUnitsAsync(cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/inventory/low-stock", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.InventoryView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<IReadOnlyList<Contracts.LowStockProductResponse>> result =
                await reads.GetLowStockAsync(cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/inventory/movements", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            Guid? productId,
            string? movementType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int page = 1,
            int pageSize = 25,
            CancellationToken cancellationToken = default) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.InventoryView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.PagedApiResponse<Contracts.StockMovementResponse>> result =
                await reads.SearchStockMovementsAsync(
                    productId, movementType, from, to, page, pageSize, cancellationToken)
                    .ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/sales/history", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? status,
            string? receiptNo,
            int page = 1,
            int pageSize = 25,
            CancellationToken cancellationToken = default) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SalesViewHistory) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.PagedApiResponse<Contracts.SaleHistoryItemResponse>> result =
                await reads.SearchSalesAsync(from, to, status, receiptNo, page, pageSize, cancellationToken)
                    .ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/sales/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.SalesViewHistory) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.SaleDetailResponse> result =
                await reads.GetSaleByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ErrorDetail.Code.EndsWith("NotFound", StringComparison.Ordinal)
                    ? Results.NotFound(new { error = result.ErrorDetail.Message })
                    : ApiAuthExtensions.ToErrorResult(result);
            }

            return Results.Ok(result.Value);
        });

        app.MapGet("/api/users", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.UsersManage) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<IReadOnlyList<Contracts.UserListItemResponse>> result =
                await reads.GetUsersAsync(cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/users/roles", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.UsersManage) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<IReadOnlyList<Contracts.UserRoleResponse>> result =
                await reads.GetUserRolesAsync(cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/users/{id:guid}", async (
            Guid id,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.UsersManage) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.UserDetailResponse> result =
                await reads.GetUserByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ErrorDetail.Code.EndsWith("NotFound", StringComparison.Ordinal)
                    ? Results.NotFound(new { error = result.ErrorDetail.Message })
                    : ApiAuthExtensions.ToErrorResult(result);
            }

            return Results.Ok(result.Value);
        });

        app.MapGet("/api/audit/logs", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? search,
            int page = 1,
            int pageSize = 25,
            CancellationToken cancellationToken = default) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.AuditView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.PagedApiResponse<Contracts.AuditLogResponse>> result =
                await reads.SearchAuditLogsAsync(from, to, search, page, pageSize, cancellationToken)
                    .ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        app.MapGet("/api/audit/errors", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            OperatorReadService reads,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? search,
            int page = 1,
            int pageSize = 25,
            CancellationToken cancellationToken = default) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.AuditView) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<Contracts.PagedApiResponse<Contracts.ErrorLogResponse>> result =
                await reads.SearchErrorLogsAsync(from, to, search, page, pageSize, cancellationToken)
                    .ConfigureAwait(false);

            return result.IsSuccess ? Results.Ok(result.Value) : ApiAuthExtensions.ToErrorResult(result);
        });

        return app;
    }
}

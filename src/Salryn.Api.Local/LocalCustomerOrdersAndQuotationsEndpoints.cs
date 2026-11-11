using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Api.Local;

internal static class LocalCustomerOrdersAndQuotationsEndpoints
{
    public static IEndpointRouteBuilder MapLocalCustomerOrdersAndQuotationsEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Customer Quotations (Price Canvass) ---

        app.MapGet("/api/customer-quotations", async (
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.GetQuotationsAsync(ct).ConfigureAwait(false));
        });

        app.MapGet("/api/customer-quotations/{id:guid}", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.GetQuotationByIdAsync(id, ct).ConfigureAwait(false));
        });

        app.MapPost("/api/customer-quotations", async (
            CreateCustomerQuotationRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.CreateQuotationAsync(session.UserId, session.DisplayName ?? session.Username, request, ct).ConfigureAwait(false), StatusCodes.Status201Created);
        });

        app.MapPost("/api/customer-quotations/{id:guid}/status", async (
            Guid id,
            UpdateStatusRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.UpdateQuotationStatusAsync(id, request.Status, ct).ConfigureAwait(false));
        });

        // --- Customer Orders ---

        app.MapGet("/api/customer-orders", async (
            string? status,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.GetOrdersAsync(status, ct).ConfigureAwait(false));
        });

        app.MapGet("/api/customer-orders/{id:guid}", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.GetOrderByIdAsync(id, ct).ConfigureAwait(false));
        });

        app.MapPost("/api/customer-orders", async (
            CreateCustomerOrderRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.CreateOrderAsync(session.UserId, session.DisplayName ?? session.Username, request, ct).ConfigureAwait(false), StatusCodes.Status201Created);
        });

        app.MapPost("/api/customer-orders/{id:guid}/status", async (
            Guid id,
            UpdateStatusRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProductsView, LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.UpdateOrderStatusAsync(id, request.Status, ct).ConfigureAwait(false));
        });

        app.MapPost("/api/customer-orders/{id:guid}/fulfill", async (
            Guid id,
            FulfillOrderRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            ICustomerOrdersAndQuotesService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.SalesCreate }) is IResult denied)
                return denied;
            return ToResult(await service.FulfillOrderAsync(id, request.SaleId, ct).ConfigureAwait(false));
        });

        return app;
    }

    private static LocalApiSession RequiredSession(HttpContext context, LocalApiSessionService sessions) =>
        sessions.GetRequiredSession(context.Request.Cookies[LocalApiSessionService.SessionCookieName])!;

    private static IResult ToResult<T>(Result<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatusCode == StatusCodes.Status201Created
                ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                : Results.Ok(result.Value);
        }

        Error error = result.ErrorDetail;
        return error.Type switch
        {
            ErrorType.NotFound => Results.Json(new { error = error.Message, code = error.Code }, statusCode: StatusCodes.Status404NotFound),
            ErrorType.Conflict => Results.Json(new { error = error.Message, code = error.Code }, statusCode: StatusCodes.Status409Conflict),
            ErrorType.Forbidden => Results.Json(new { error = error.Message, code = error.Code }, statusCode: StatusCodes.Status403Forbidden),
            ErrorType.Validation => Results.Json(new { error = error.Message, code = error.Code }, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Json(new { error = error.Message, code = error.Code }, statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    public sealed record UpdateStatusRequest(string Status);
    public sealed record FulfillOrderRequest(Guid SaleId);
}

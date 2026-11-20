using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Api.Local;

internal static class LocalMenuCatalogEndpoints
{
    public static TBuilder MapLocalMenuCatalogEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/menu/catalog", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireView(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<MenuCatalogDraft> result = composition.MenuCatalog.GetDraft();
            return ToResult(result);
        });

        app.MapGet("/api/menu/publication/current", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireView(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            Result<MenuPublicationSnapshot?> result = composition.MenuCatalog.GetCurrentPublication();
            return ToResult(result);
        });

        app.MapPost("/api/menu/categories", (
            MenuCategoryMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SaveCategory(request));
        });

        app.MapPost("/api/menu/items", (
            MenuItemMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SaveItem(request));
        });

        app.MapPost("/api/menu/variants", (
            MenuVariantMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SaveVariant(request));
        });

        app.MapPost("/api/menu/modifier-groups", (
            MenuModifierGroupMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SaveModifierGroup(request));
        });

        app.MapPost("/api/menu/modifier-options", (
            MenuModifierOptionMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SaveModifierOption(request));
        });

        app.MapPut("/api/menu/items/{productId:guid}/modifier-groups", (
            Guid productId,
            string[] groupIds,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SetItemModifierGroups(
                new MenuModifierLinkMutation(productId.ToString("D"), groupIds)));
        });

        app.MapPut("/api/menu/variants/{variantId}/modifier-groups", (
            string variantId,
            string[] groupIds,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.SetVariantModifierGroups(
                new MenuModifierLinkMutation(variantId, groupIds)));
        });

        app.MapPost("/api/menu/publish", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireManage(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            return ToResult(composition.MenuCatalog.PublishCurrent());
        });

        return app;
    }

    private static IResult? RequireView(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        return LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            LocalApiPermissions.ProductsView);
    }

    private static IResult? RequireManage(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        return LocalApiPermissionGuards.RequirePermission(
            httpContext,
            sessions,
            LocalApiPermissions.ProductsManage);
    }

    private static IResult ToResult(Result result)
    {
        return result.IsSuccess
            ? Results.Ok(new { status = "ok" })
            : ToError(result.ErrorDetail);
    }

    private static IResult ToResult<T>(Result<T> result)
    {
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ToError(result.ErrorDetail);
    }

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
}

using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Api.Local;

internal static class LocalIngredientRecipeEndpoints
{
    public static TBuilder MapLocalIngredientRecipeEndpoints<TBuilder>(this TBuilder app) where TBuilder : IEndpointRouteBuilder
    {
        app.MapGet("/api/cafe/ingredients-recipes", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireView(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.GetDraft());
        });

        app.MapPost("/api/cafe/ingredients", (
            IngredientMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireIngredientManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.SaveIngredient(request));
        });

        app.MapPost("/api/cafe/recipes", (
            RecipeMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireRecipeManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.SaveRecipe(request));
        });

        app.MapPost("/api/cafe/recipes/lines", (
            RecipeLineMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireRecipeManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.SaveRecipeLine(request));
        });

        app.MapPut("/api/cafe/recipes/lines", (
            RecipeLineUpdateMutation request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireRecipeManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.UpdateRecipeLine(request));
        });

        app.MapPut("/api/cafe/recipes/items/{productId:guid}", (
            Guid productId,
            RecipeBindingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireRecipeManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.SetItemRecipe(productId, request.RecipeId));
        });

        app.MapPut("/api/cafe/recipes/variants/{variantId}", (
            string variantId,
            RecipeBindingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireRecipeManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.SetVariantRecipe(variantId, request.RecipeId));
        });

        app.MapPut("/api/cafe/recipes/modifier-options/{optionId}", (
            string optionId,
            RecipeBindingRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireRecipeManage(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.SetModifierOptionRecipe(optionId, request.RecipeId));
        });

        app.MapPost("/api/cafe/recipes/publish", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.RecipesPublish) is IResult denied)
            {
                return denied;
            }

            return ToResult(composition.IngredientRecipes.PublishCurrent());
        });

        app.MapGet("/api/cafe/recipes/publication/current", (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            SalrynLocalComposition composition) =>
        {
            if (RequireView(httpContext, sessions) is IResult denied) return denied;
            return ToResult(composition.IngredientRecipes.GetCurrentPublication());
        });

        return app;
    }

    private static IResult? RequireView(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        IResult? ingredients = LocalApiPermissionGuards.RequirePermission(
            httpContext, sessions, LocalApiPermissions.IngredientsView);
        if (ingredients is not null) return ingredients;

        return LocalApiPermissionGuards.RequirePermission(
            httpContext, sessions, LocalApiPermissions.RecipesView);
    }

    private static IResult? RequireIngredientManage(
        HttpContext httpContext,
        LocalApiSessionService sessions) =>
        LocalApiPermissionGuards.RequirePermission(
            httpContext, sessions, LocalApiPermissions.IngredientsManage);

    private static IResult? RequireRecipeManage(
        HttpContext httpContext,
        LocalApiSessionService sessions) =>
        LocalApiPermissionGuards.RequirePermission(
            httpContext, sessions, LocalApiPermissions.RecipesManage);

    private static IResult ToResult(Result result) =>
        result.IsSuccess
            ? Results.Ok(new { status = "ok" })
            : ToError(result.ErrorDetail);

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

    private sealed record RecipeBindingRequest(string? RecipeId);
}

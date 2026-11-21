using System.Net;
using System.Net.Http.Json;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Repositories.Menu;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Sync.Central.Server;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class CafePublicationHttpTests
{
    [Fact]
    public async Task ApprovedDevice_ReadsAndAppliesScopedPublications_ForeignStoreAndAnonymousDenied()
    {
        var source = C4ProofDatabase.Create(Path.Combine(Path.GetTempPath(), "cafe-publications-" + Guid.NewGuid().ToString("N")));
        var catalog = new SqliteMenuCatalogStore(source.Factory);
        Assert.True(catalog.SaveVariant(new(null, C4ProofIds.ProductId, "Standard", null, null, 500, 250, true, false, 0)).IsSuccess);
        var menu = catalog.PublishCurrent();
        var recipes = new SqliteIngredientRecipeStore(source.Factory).PublishCurrent();
        Assert.True(menu.IsSuccess, menu.IsFailure ? menu.ErrorDetail.Message : null);
        Assert.True(recipes.IsSuccess, recipes.IsFailure ? recipes.ErrorDetail.Message : null);
        using var host = new CentralSyncTestHost(source.Paths.DatabasePath, canonical: true, canonicalStore: C4ProofIds.StoreId);
        using var admin = host.CreateHttpsClient();
        string token = await host.LoginAsAdminAsync(admin);
        using var device = await host.EnrollApproveAndAuthenticateDeviceAsync(admin, token, "Cafe publication QA");
        foreach (string kind in new[] { "menu", "recipes" })
        {
            string path = $"/api/sync/v1/cafe/{kind}/publication?storeId={host.StoreId:D}";
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await device.Http.GetAsync($"/api/sync/v1/cafe/{kind}/publication?storeId={Guid.NewGuid():D}")).StatusCode);
        }
        var menuSnapshot = await device.Http.GetFromJsonAsync<MenuPublicationSnapshot>($"/api/sync/v1/cafe/menu/publication?storeId={host.StoreId:D}");
        var recipeSnapshot = await device.Http.GetFromJsonAsync<RecipeBookPublicationSnapshot>($"/api/sync/v1/cafe/recipes/publication?storeId={host.StoreId:D}");
        Assert.NotNull(menuSnapshot); Assert.NotNull(recipeSnapshot);
        Assert.Equal(host.StoreId, menuSnapshot.StoreId); Assert.Equal(host.StoreId, recipeSnapshot.StoreId);
        var target = C4ProofDatabase.CreateEmpty(Path.Combine(Path.GetTempPath(), "cafe-publication-target-" + Guid.NewGuid().ToString("N")));
        target.Execute("UPDATE deployment_identity SET store_id=$store;", ("$store", host.StoreId.ToString("D")));
        var appliedMenu = new SqliteMenuCatalogStore(target.Factory).ApplyPublication(menuSnapshot);
        Assert.True(appliedMenu.IsSuccess, appliedMenu.IsFailure ? appliedMenu.ErrorDetail.Message : null);
        var appliedRecipes = new SqliteIngredientRecipeStore(target.Factory).ApplyPublication(recipeSnapshot);
        Assert.True(appliedRecipes.IsSuccess, appliedRecipes.IsFailure ? appliedRecipes.ErrorDetail.Message : null);
        Assert.Equal(menuSnapshot.PublicationHash, new SqliteMenuCatalogStore(target.Factory).GetCurrentPublication().Value!.PublicationHash);
        Assert.Equal(recipeSnapshot.PublicationHash, new SqliteIngredientRecipeStore(target.Factory).GetCurrentPublication().Value!.PublicationHash);
    }
}

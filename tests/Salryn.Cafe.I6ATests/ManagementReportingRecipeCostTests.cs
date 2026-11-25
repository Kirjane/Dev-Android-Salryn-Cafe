using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Services;
using Xunit;

namespace Salryn.Cafe.I6ATests;

public sealed class ManagementReportingRecipeCostTests
{
    [Fact]
    public void GetReport_PopulatesRecipeCosts_ForPublishedStoreRevisionIdFormat()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-report-recipes-" + Guid.NewGuid().ToString("N"));
        var db = C4ProofDatabase.Create(root);

        try
        {
            var comp = SalrynLocalComposition.Create(db.Paths);

            db.Execute(
                "UPDATE ingredients SET name='Espresso Beans', stock_on_hand_x1000=0 WHERE id=$id;",
                ("$id", C4ProofIds.ItemIngredientId.ToString("D")));
            db.Execute(
                "UPDATE ingredients SET name='Oat Milk', stock_on_hand_x1000=0 WHERE id=$id;",
                ("$id", C4ProofIds.VariantIngredientId.ToString("D")));

            // Establish real weighted-average unit cost for Espresso Beans (10 pcs for 2500 cents = 250 cents/pc);
            // leave Oat Milk uncosted so Incomplete coverage is verified alongside Complete coverage.
            var supplier = comp.SupplierDirectory.SaveSupplier(new SupplierMutation(null, "Synthetic Roasters", null, null, true));
            Assert.True(supplier.IsSuccess, supplier.IsFailure ? supplier.ErrorDetail.Message : null);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var purchase = comp.CafeInventory.CommitPurchase(new PurchaseReceiptMutation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                C4ProofIds.DeviceId,
                C4ProofIds.StoreId,
                supplier.Value.Id,
                C4ProofIds.ActorUserId,
                now.AddMinutes(-10),
                [new PurchaseReceiptLineMutation(Guid.NewGuid(), CafeStockTargetTypes.Ingredient, C4ProofIds.ItemIngredientId, 10000, 2500)]));
            Assert.True(purchase.IsSuccess, purchase.IsFailure ? purchase.ErrorDetail.Message : null);

            var recComplete = comp.IngredientRecipes.SaveRecipe(new RecipeMutation(null, "Espresso Base", 1000, true));
            Assert.True(recComplete.IsSuccess, recComplete.IsFailure ? recComplete.ErrorDetail.Message : null);

            var recIncomplete = comp.IngredientRecipes.SaveRecipe(new RecipeMutation(null, "Oat Latte Special", 1000, true));
            Assert.True(recIncomplete.IsSuccess, recIncomplete.IsFailure ? recIncomplete.ErrorDetail.Message : null);

            Assert.True(comp.IngredientRecipes.SaveRecipeLine(new RecipeLineMutation(
                recComplete.Value.Id, C4ProofIds.ItemIngredientId, RecipeInputUnits.Piece, 1000, 0)).IsSuccess);
            Assert.True(comp.IngredientRecipes.SaveRecipeLine(new RecipeLineMutation(
                recIncomplete.Value.Id, C4ProofIds.ItemIngredientId, RecipeInputUnits.Piece, 1000, 0)).IsSuccess);
            Assert.True(comp.IngredientRecipes.SaveRecipeLine(new RecipeLineMutation(
                recIncomplete.Value.Id, C4ProofIds.VariantIngredientId, RecipeInputUnits.Piece, 1000, 1)).IsSuccess);

            var published = comp.IngredientRecipes.PublishCurrent();
            Assert.True(published.IsSuccess, published.IsFailure ? published.ErrorDetail.Message : null);
            Assert.Contains(":", published.Value.RevisionId);
            Assert.False(Guid.TryParse(published.Value.RevisionId, out _));

            var reporting = new SqliteCafeManagementReportingService(db.Factory);
            var reportRes = reporting.GetReport(
                new CafeManagementReportRequest(
                    C4ProofIds.StoreId,
                    C4ProofIds.ActorUserId,
                    now.AddDays(-1),
                    now.AddDays(1),
                    now));

            Assert.True(reportRes.IsSuccess, reportRes.IsFailure ? reportRes.ErrorDetail.Message : null);
            var recipeCosts = reportRes.Value.RecipeCosts;
            Assert.Equal(2, recipeCosts.Count);

            var completeRow = Assert.Single(recipeCosts, r => r.RecipeName == "Espresso Base");
            Assert.Equal(recComplete.Value.Id, completeRow.RecipeId);
            Assert.Equal(published.Value.RevisionId, completeRow.RecipeBookRevisionId);
            Assert.Equal("Complete", completeRow.CostCoverage);
            Assert.Equal(250, completeRow.KnownCostCents);
            Assert.Equal(1, completeRow.IngredientLineCount);
            Assert.Equal(0, completeRow.MissingCostLineCount);

            var incompleteRow = Assert.Single(recipeCosts, r => r.RecipeName == "Oat Latte Special");
            Assert.Equal(recIncomplete.Value.Id, incompleteRow.RecipeId);
            Assert.Equal(published.Value.RevisionId, incompleteRow.RecipeBookRevisionId);
            Assert.Equal("Incomplete", incompleteRow.CostCoverage);
            Assert.Equal(2, incompleteRow.IngredientLineCount);
            Assert.Equal(1, incompleteRow.MissingCostLineCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }
}

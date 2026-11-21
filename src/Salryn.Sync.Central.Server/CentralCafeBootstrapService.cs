using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

public sealed partial class CentralSyncService
{
    public ServiceOutcome<object> ReadCohostProbe(Guid key)
    {
        if(!_options.EnableCanonicalDomainProcessing || key==Guid.Empty)
            return ServiceOutcome<object>.Fail(403,"ERR_COHOST_UNAVAILABLE","Canonical cohost proof is unavailable.");
        using var c=_db.OpenConnection();using var q=c.CreateCommand();
        q.CommandText="SELECT value FROM app_metadata WHERE key=$key;";
        q.Parameters.AddWithValue("$key","salryn.cafe.cohost.probe."+key.ToString("D"));
        return q.ExecuteScalar() is string value && value.Length==64
            ? ServiceOutcome<object>.Ok(new{value})
            : ServiceOutcome<object>.Fail(409,"ERR_COHOST_FILE_MISMATCH","No matching local-file probe exists.");
    }

    public ServiceOutcome<CafeIngredientRecipeBootstrapV1> GetIngredientBootstrap(AuthenticatedDeviceContext device,
        Guid storeId, Guid centralInstanceId, long baselineServerSequence)
    {
        if (!_options.EnableCanonicalDomainProcessing)
            return ServiceOutcome<CafeIngredientRecipeBootstrapV1>.Fail(503,"ERR_BOOTSTRAP_DOMAIN_SNAPSHOT_UNAVAILABLE","Canonical ingredient snapshots are unavailable.");
        if (storeId != device.StoreId || device.StoreId != _options.CanonicalStoreId || device.TenantId != _options.CanonicalTenantId)
            return ServiceOutcome<CafeIngredientRecipeBootstrapV1>.Fail(403,"ERR_CANONICAL_AUTHORITY_SCOPE","Ingredient snapshot is outside approved device scope.");
        using var connection=_db.OpenConnection();
        // Serialize writers while stock, feed baseline and the current recipe publication are read.
        using var transaction=connection.BeginTransaction(deferred:false);
        var state=_db.ReadServerState(connection,transaction);
        if(state.CentralInstanceId != centralInstanceId || state.HeadSequence != baselineServerSequence)
            return ServiceOutcome<CafeIngredientRecipeBootstrapV1>.Fail(409,"ERR_CAFE_BOOTSTRAP_BASELINE_CHANGED",
                "Central authority advanced after base bootstrap; do not apply stock or advance client cursors.",state.ServerEpoch);
        List<CafeIngredientBootstrapIngredientV1> ingredients=[];
        using(var command=connection.CreateCommand())
        {
            command.Transaction=transaction;
            command.CommandText="SELECT id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,is_active,notes FROM ingredients ORDER BY id;";
            using var reader=command.ExecuteReader();
            while(reader.Read()) ingredients.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetString(2),
                reader.GetInt64(3),reader.GetInt64(4),reader.GetInt32(5)!=0,reader.IsDBNull(6)?null:reader.GetString(6)));
        }
        // The separate read connection sees the same committed publication because the write lock is held.
        var recipeResult=new SqliteIngredientRecipeStore(new SqliteConnectionFactory(_options.DatabasePath)).GetCurrentPublication();
        if(recipeResult.IsFailure || recipeResult.Value is null)
            return ServiceOutcome<CafeIngredientRecipeBootstrapV1>.Fail(503,"ERR_RECIPE_PUBLICATION_UNAVAILABLE","Publish Cafe recipes before bootstrap.");
        var book=recipeResult.Value;
        if(book.StoreId != storeId)
            return ServiceOutcome<CafeIngredientRecipeBootstrapV1>.Fail(403,"ERR_CANONICAL_AUTHORITY_SCOPE","Recipe publication belongs to another store.");
        var publication=new CafeRecipeBookPublicationV1(book.RevisionId,book.StoreId,book.RevisionNumber,book.ContentHash,
            book.PublicationHash,book.PublishedAtUtc,
            book.Ingredients.Select(x=>new CafeRecipeBookIngredientV1(x.IngredientId,x.Name,x.StockUnit,x.ReorderLevelX1000,x.DisplayOrder)).ToArray(),
            book.Recipes.Select(x=>new CafeRecipeBookRecipeV1(x.RecipeId,x.RecipeRevisionId,x.Name,x.YieldX1000,x.DisplayOrder)).ToArray(),
            book.Lines.Select(x=>new CafeRecipeBookLineV1(x.RecipeRevisionId,x.RecipeId,x.IngredientId,x.StockUnit,x.NormalizedQuantityX1000,x.DisplayOrder)).ToArray(),
            book.ItemBindings.Concat(book.VariantBindings).Concat(book.ModifierOptionBindings)
                .Select(x=>new CafeRecipeBookBindingV1(x.BindingType,x.BindingId,x.RecipeId,x.RecipeRevisionId)).ToArray());
        var snapshot=CafeIngredientRecipeBootstrapProtocolV1.Create(state.CentralInstanceId,storeId,baselineServerSequence,ingredients,publication);
        transaction.Commit();
        return ServiceOutcome<CafeIngredientRecipeBootstrapV1>.Ok(snapshot);
    }
}

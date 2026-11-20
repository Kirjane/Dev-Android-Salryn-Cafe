using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeIngredientBootstrapApplyResult(
    bool IsReplay,
    Guid SnapshotId,
    string SnapshotHash,
    long BaselineServerSequence);

public sealed class SqliteCafeIngredientBootstrapStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCafeIngredientBootstrapStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Result<CafeIngredientBootstrapApplyResult> Apply(
        CafeIngredientRecipeBootstrapV1 snapshot)
    {
        if (!CafeIngredientRecipeBootstrapProtocolV1.Validate(snapshot))
        {
            return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Conflict(
                "CafeBootstrap.SnapshotIntegrityMismatch",
                "Cafe ingredient bootstrap identity/hash does not match canonical content."));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid localStoreId = ReadStoreId(connection, transaction);
            if (localStoreId != snapshot.StoreId)
            {
                transaction.Rollback();
                return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Conflict(
                    "CafeBootstrap.StoreMismatch",
                    "Cafe bootstrap StoreId does not match local deployment authority."));
            }

            SyncRemoteState? f1eState =
                SqliteSyncPullStore.ReadRemoteState(
                    connection,
                    transaction,
                    snapshot.StoreId);
            SyncBootstrapEvidence? f1eEvidence =
                SqliteSyncBootstrapStore.ReadBootstrapEvidence(
                    connection,
                    transaction,
                    snapshot.StoreId);

            if (f1eState is null ||
                !f1eState.CentralInstanceId.HasValue ||
                f1eEvidence is null)
            {
                transaction.Rollback();
                return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Conflict(
                    "CafeBootstrap.F1EAuthorityMissing",
                    "Cafe ingredient bootstrap requires accepted F1E remote/bootstrap authority first."));
            }

            bool sameF1EAuthority =
                f1eState.CentralInstanceId.Value == snapshot.CentralInstanceId &&
                f1eEvidence.StoreId == snapshot.StoreId &&
                f1eEvidence.CentralInstanceId == snapshot.CentralInstanceId &&
                f1eEvidence.BaselineServerSequence == snapshot.BaselineServerSequence &&
                f1eState.LastReceivedServerSequence == snapshot.BaselineServerSequence &&
                f1eState.LastAppliedServerSequence == snapshot.BaselineServerSequence &&
                string.Equals(
                    f1eState.State,
                    AppliedBusinessChangeContract.Ready,
                    StringComparison.Ordinal);

            if (!sameF1EAuthority)
            {
                transaction.Rollback();
                return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Conflict(
                    "CafeBootstrap.F1EAuthorityConflict",
                    "Cafe ingredient bootstrap StoreId/epoch/baseline must exactly extend accepted F1E bootstrap authority."));
            }

            ExistingState? existing = ReadState(connection, transaction, snapshot.StoreId);
            if (existing is not null)
            {
                if (existing.SnapshotId == snapshot.SnapshotId &&
                    string.Equals(existing.SnapshotHash, snapshot.SnapshotHash, StringComparison.Ordinal) &&
                    existing.CentralInstanceId == snapshot.CentralInstanceId &&
                    existing.BaselineServerSequence == snapshot.BaselineServerSequence)
                {
                    transaction.Commit();
                    return Result<CafeIngredientBootstrapApplyResult>.Success(
                        new CafeIngredientBootstrapApplyResult(
                            true,snapshot.SnapshotId,snapshot.SnapshotHash,snapshot.BaselineServerSequence));
                }

                transaction.Rollback();
                return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Conflict(
                    "CafeBootstrap.EpochConflict",
                    "Different Cafe bootstrap evidence already exists; explicit rebootstrap is required."));
            }

            if (ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM ingredient_stock_movements;") != 0 ||
                ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM sale_ingredient_consumption_decisions;") != 0 ||
                ScalarLong(connection, transaction,
                    "SELECT COUNT(*) FROM recipe_book_revisions;") != 0)
            {
                transaction.Rollback();
                return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Conflict(
                    "CafeBootstrap.NonEmptyDomainConflict",
                    "Cafe ingredient bootstrap requires an empty C2 operational/config domain."));
            }

            string now = DateTimeOffset.UtcNow.ToString("O");
            foreach (CafeIngredientBootstrapIngredientV1 ingredient in snapshot.Ingredients)
            {
                Exec(connection, transaction, """
                    INSERT INTO ingredients(
                        id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,
                        is_active,notes,created_at_utc,updated_at_utc)
                    VALUES($id,$name,$unit,$stock,$reorder,$active,$notes,$now,NULL);
                    """,
                    ("$id",ingredient.Id.ToString("D")),("$name",ingredient.Name),
                    ("$unit",ingredient.StockUnit),("$stock",ingredient.StockOnHandX1000),
                    ("$reorder",ingredient.ReorderLevelX1000),("$active",ingredient.IsActive?1:0),
                    ("$notes",ingredient.Notes),("$now",now));
            }

            if (snapshot.RecipeBook is not null)
            {
                ApplyRecipeBook(connection, transaction, snapshot.RecipeBook);
            }

            Exec(connection, transaction, """
                INSERT INTO cafe_ingredient_bootstrap_state(
                    store_id,central_instance_id,baseline_server_sequence,
                    snapshot_id,snapshot_hash,applied_at_utc)
                VALUES($store,$central,$baseline,$snapshot,$hash,$at);
                """,
                ("$store",snapshot.StoreId.ToString("D")),
                ("$central",snapshot.CentralInstanceId.ToString("D")),
                ("$baseline",snapshot.BaselineServerSequence),
                ("$snapshot",snapshot.SnapshotId.ToString("D")),
                ("$hash",snapshot.SnapshotHash),
                ("$at",now));

            transaction.Commit();
            return Result<CafeIngredientBootstrapApplyResult>.Success(
                new CafeIngredientBootstrapApplyResult(
                    false,snapshot.SnapshotId,snapshot.SnapshotHash,snapshot.BaselineServerSequence));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<CafeIngredientBootstrapApplyResult>.Fail(Error.Unexpected(
                "CafeBootstrap.ApplyFailed", ex.Message));
        }
    }

    private static void ApplyRecipeBook(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRecipeBookPublicationV1 book)
    {
        Exec(connection, transaction, """
            INSERT INTO recipe_book_revisions(
                revision_id,store_id,revision_number,content_hash,publication_hash,status,
                created_at_utc,published_at_utc,superseded_at_utc)
            VALUES($r,$store,$number,$content,$publication,'Published',$at,$at,NULL);
            """,
            ("$r",book.RevisionId),("$store",book.StoreId.ToString("D")),
            ("$number",book.RevisionNumber),("$content",book.ContentHash),
            ("$publication",book.PublicationHash),("$at",book.PublishedAtUtc.ToUniversalTime().ToString("O")));

        foreach (CafeRecipeBookIngredientV1 x in book.Ingredients)
            Exec(connection, transaction, """
                INSERT INTO recipe_book_revision_ingredients(
                    revision_id,ingredient_id,name,stock_unit,reorder_level_x1000,display_order)
                VALUES($r,$id,$name,$unit,$reorder,$order);
                """,
                ("$r",book.RevisionId),("$id",x.IngredientId.ToString("D")),("$name",x.Name),
                ("$unit",x.StockUnit),("$reorder",x.ReorderLevelX1000),("$order",x.DisplayOrder));

        foreach (CafeRecipeBookRecipeV1 x in book.Recipes)
        {
            Exec(connection, transaction, """
                INSERT INTO recipe_book_revision_recipes(
                    revision_id,recipe_id,recipe_revision_id,name,yield_x1000,display_order)
                VALUES($r,$id,$rr,$name,$yield,$order);
                """,
                ("$r",book.RevisionId),("$id",x.RecipeId),("$rr",x.RecipeRevisionId),
                ("$name",x.Name),("$yield",x.YieldX1000),("$order",x.DisplayOrder));

            Exec(connection, transaction, """
                INSERT INTO recipes(id,name,yield_x1000,is_active,created_at_utc,updated_at_utc)
                VALUES($id,$name,$yield,1,$at,NULL);
                """,
                ("$id",x.RecipeId),("$name",x.Name),("$yield",x.YieldX1000),
                ("$at",book.PublishedAtUtc.ToUniversalTime().ToString("O")));
        }

        foreach (CafeRecipeBookLineV1 x in book.Lines)
        {
            Exec(connection, transaction, """
                INSERT INTO recipe_book_revision_lines(
                    revision_id,recipe_revision_id,recipe_id,ingredient_id,stock_unit,
                    normalized_quantity_x1000,display_order)
                VALUES($r,$rr,$recipe,$ingredient,$unit,$quantity,$order);
                """,
                ("$r",book.RevisionId),("$rr",x.RecipeRevisionId),("$recipe",x.RecipeId),
                ("$ingredient",x.IngredientId.ToString("D")),("$unit",x.StockUnit),
                ("$quantity",x.NormalizedQuantityX1000),("$order",x.DisplayOrder));

            Exec(connection, transaction, """
                INSERT INTO recipe_lines(
                    recipe_id,ingredient_id,input_unit,declared_quantity_x1000,
                    normalized_quantity_x1000,display_order)
                VALUES($recipe,$ingredient,$unit,$quantity,$quantity,$order);
                """,
                ("$recipe",x.RecipeId),("$ingredient",x.IngredientId.ToString("D")),
                ("$unit",x.StockUnit),("$quantity",x.NormalizedQuantityX1000),("$order",x.DisplayOrder));
        }

        foreach (CafeRecipeBookBindingV1 x in book.Bindings)
        {
            Exec(connection, transaction, """
                INSERT INTO recipe_book_revision_bindings(
                    revision_id,binding_type,binding_id,recipe_id,recipe_revision_id)
                VALUES($r,$type,$binding,$recipe,$rr);
                """,
                ("$r",book.RevisionId),("$type",x.BindingType),("$binding",x.BindingId),
                ("$recipe",x.RecipeId),("$rr",x.RecipeRevisionId));

            if (string.Equals(x.BindingType,"Item",StringComparison.Ordinal) &&
                Exists(connection,transaction,"products","id",x.BindingId))
            {
                Exec(connection,transaction,
                    "INSERT INTO recipe_item_bindings(product_id,recipe_id) VALUES($id,$recipe);",
                    ("$id",x.BindingId),("$recipe",x.RecipeId));
            }
            else if (string.Equals(x.BindingType,"Variant",StringComparison.Ordinal) &&
                Exists(connection,transaction,"menu_variants","id",x.BindingId))
            {
                Exec(connection,transaction,
                    "INSERT INTO recipe_variant_bindings(variant_id,recipe_id) VALUES($id,$recipe);",
                    ("$id",x.BindingId),("$recipe",x.RecipeId));
            }
            else if (string.Equals(x.BindingType,"ModifierOption",StringComparison.Ordinal) &&
                Exists(connection,transaction,"menu_modifier_options","id",x.BindingId))
            {
                Exec(connection,transaction,
                    "INSERT INTO recipe_modifier_option_bindings(option_id,recipe_id) VALUES($id,$recipe);",
                    ("$id",x.BindingId),("$recipe",x.RecipeId));
            }
        }

        Exec(connection,transaction, """
            INSERT INTO recipe_book_current_publication(
                store_id,revision_id,content_hash,publication_hash,applied_at_utc)
            VALUES($store,$revision,$content,$publication,$at);
            """,
            ("$store",book.StoreId.ToString("D")),("$revision",book.RevisionId),
            ("$content",book.ContentHash),("$publication",book.PublicationHash),
            ("$at",book.PublishedAtUtc.ToUniversalTime().ToString("O")));
    }

    private static Guid ReadStoreId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";
        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text,out Guid id)
            ? id
            : throw new InvalidOperationException("Local deployment StoreId missing.");
    }

    private static ExistingState? ReadState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT central_instance_id,baseline_server_sequence,snapshot_id,snapshot_hash
            FROM cafe_ingredient_bootstrap_state WHERE store_id=$store LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store",storeId.ToString("D"));
        using SqliteDataReader r=command.ExecuteReader();
        return r.Read()?new ExistingState(
            Guid.Parse(r.GetString(0)),r.GetInt64(1),Guid.Parse(r.GetString(2)),r.GetString(3)):null;
    }

    private static bool Exists(
        SqliteConnection connection,SqliteTransaction transaction,
        string table,string column,string id)
    {
        using SqliteCommand cmd=connection.CreateCommand();
        cmd.Transaction=transaction;
        cmd.CommandText="SELECT 1 FROM "+table+" WHERE "+column+"=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id",id);
        return cmd.ExecuteScalar() is not null;
    }

    private static long ScalarLong(
        SqliteConnection connection,SqliteTransaction transaction,string sql)
    {
        using SqliteCommand cmd=connection.CreateCommand();
        cmd.Transaction=transaction; cmd.CommandText=sql;
        return Convert.ToInt64(cmd.ExecuteScalar()??0L);
    }

    private static void Exec(
        SqliteConnection connection,SqliteTransaction transaction,string sql,
        params (string Name,object? Value)[] parameters)
    {
        using SqliteCommand cmd=connection.CreateCommand();
        cmd.Transaction=transaction; cmd.CommandText=sql;
        foreach((string name,object? value) in parameters)
            cmd.Parameters.AddWithValue(name,value??DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private sealed record ExistingState(
        Guid CentralInstanceId,long BaselineServerSequence,Guid SnapshotId,string SnapshotHash);
}

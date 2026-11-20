using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Repositories.Ingredients;

public sealed class SqliteIngredientRecipeStore : SqliteRepositoryBase
{
    public SqliteIngredientRecipeStore(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Result<IngredientRecipeDraft> GetDraft()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            return Result<IngredientRecipeDraft>.Success(ReadDraft(connection));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<IngredientRecipeDraft>.Fail(Error.Unexpected(
                "Ingredients.ReadDraftFailed", ex.Message));
        }
    }

    public Result<IngredientConfig> SaveIngredient(IngredientMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<IngredientConfig>.Fail(Error.Validation(
                "Ingredients.NameRequired", "Ingredient name is required."));
        }

        if (!IngredientStockUnits.IsCanonical(request.StockUnit))
        {
            return Result<IngredientConfig>.Fail(Error.Validation(
                "Ingredients.InvalidStockUnit", "Stock unit must be Gram, Milliliter, or Piece."));
        }

        if (request.ReorderLevelX1000 < 0)
        {
            return Result<IngredientConfig>.Fail(Error.Validation(
                "Ingredients.InvalidReorderLevel", "Ingredient reorder level cannot be negative."));
        }

        Guid id = request.Id is { } supplied && supplied != Guid.Empty
            ? supplied
            : Guid.CreateVersion7();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string now = DateTimeOffset.UtcNow.ToString("O");

            bool exists = Exists(connection, transaction, "ingredients", "id", id.ToString("D"));
            if (exists)
            {
                using SqliteCommand current = connection.CreateCommand();
                current.Transaction = transaction;
                current.CommandText = """
                    SELECT stock_unit,stock_on_hand_x1000
                    FROM ingredients WHERE id=$id;
                    """;
                current.Parameters.AddWithValue("$id", id.ToString("D"));
                using SqliteDataReader reader = current.ExecuteReader();
                if (!reader.Read())
                {
                    transaction.Rollback();
                    return Result<IngredientConfig>.Fail(Error.NotFound(
                        "Ingredients.NotFound", "Ingredient disappeared during update."));
                }

                string currentUnit = reader.GetString(0);
                long currentStock = reader.GetInt64(1);
                reader.Close();

                if (!string.Equals(currentUnit, request.StockUnit, StringComparison.Ordinal))
                {
                    bool hasDependentEvidence =
                        currentStock != 0 ||
                        ScalarLong(
                            connection,
                            transaction,
                            "SELECT COUNT(*) FROM recipe_lines WHERE ingredient_id=$id;",
                            ("$id", id.ToString("D"))) != 0 ||
                        ScalarLong(
                            connection,
                            transaction,
                            "SELECT COUNT(*) FROM ingredient_stock_movements WHERE ingredient_id=$id;",
                            ("$id", id.ToString("D"))) != 0;

                    if (hasDependentEvidence)
                    {
                        transaction.Rollback();
                        return Result<IngredientConfig>.Fail(Error.Conflict(
                            "Ingredients.StockUnitLocked",
                            "Ingredient stock unit cannot change after stock, recipe, or movement evidence exists."));
                    }
                }
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = exists
                ? """
                    UPDATE ingredients
                    SET name=$name,
                        stock_unit=$unit,
                        reorder_level_x1000=$reorder,
                        is_active=$active,
                        notes=$notes,
                        updated_at_utc=$now
                    WHERE id=$id;
                    """
                : """
                    INSERT INTO ingredients(
                        id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,
                        is_active,notes,created_at_utc,updated_at_utc)
                    VALUES($id,$name,$unit,0,$reorder,$active,$notes,$now,NULL);
                    """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$name", request.Name.Trim());
            command.Parameters.AddWithValue("$unit", request.StockUnit);
            command.Parameters.AddWithValue("$reorder", request.ReorderLevelX1000);
            command.Parameters.AddWithValue("$active", request.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$notes", DbText(request.Notes));
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();

            long stock = ScalarLong(connection, transaction,
                "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                ("$id", id.ToString("D")));

            transaction.Commit();
            return Result<IngredientConfig>.Success(new IngredientConfig(
                id,
                request.Name.Trim(),
                request.StockUnit,
                stock,
                request.ReorderLevelX1000,
                request.IsActive,
                NullIfBlank(request.Notes)));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<IngredientConfig>.Fail(Error.Conflict(
                "Ingredients.DuplicateName", "Ingredient name already exists."));
        }
        catch (SqliteException ex)
        {
            return Result<IngredientConfig>.Fail(Error.Unexpected(
                "Ingredients.SaveFailed", ex.Message));
        }
    }

    public Result<RecipeConfig> SaveRecipe(RecipeMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<RecipeConfig>.Fail(Error.Validation(
                "Recipes.NameRequired", "Recipe name is required."));
        }

        if (request.YieldX1000 <= 0)
        {
            return Result<RecipeConfig>.Fail(Error.Validation(
                "Recipes.YieldRequired", "Recipe yield must be greater than zero."));
        }

        string id = string.IsNullOrWhiteSpace(request.Id)
            ? Guid.CreateVersion7().ToString("D")
            : request.Id.Trim();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string now = DateTimeOffset.UtcNow.ToString("O");
            bool exists = Exists(connection, transaction, "recipes", "id", id);

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = exists
                ? """
                    UPDATE recipes
                    SET name=$name,yield_x1000=$yield,is_active=$active,updated_at_utc=$now
                    WHERE id=$id;
                    """
                : """
                    INSERT INTO recipes(id,name,yield_x1000,is_active,created_at_utc,updated_at_utc)
                    VALUES($id,$name,$yield,$active,$now,NULL);
                    """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", request.Name.Trim());
            command.Parameters.AddWithValue("$yield", request.YieldX1000);
            command.Parameters.AddWithValue("$active", request.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
            transaction.Commit();

            return Result<RecipeConfig>.Success(new RecipeConfig(
                id, request.Name.Trim(), request.YieldX1000, request.IsActive));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<RecipeConfig>.Fail(Error.Conflict(
                "Recipes.DuplicateName", "Recipe name already exists."));
        }
        catch (SqliteException ex)
        {
            return Result<RecipeConfig>.Fail(Error.Unexpected(
                "Recipes.SaveFailed", ex.Message));
        }
    }

    public Result<RecipeLineConfig> SaveRecipeLine(RecipeLineMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.RecipeId) || request.IngredientId == Guid.Empty)
        {
            return Result<RecipeLineConfig>.Fail(Error.Validation(
                "Recipes.LineIdentityRequired", "Recipe and ingredient identities are required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string recipeId = request.RecipeId.Trim();

            if (!Exists(connection, transaction, "recipes", "id", recipeId))
            {
                return RollbackFail<RecipeLineConfig>(
                    transaction, "Recipes.NotFound", "Recipe does not exist.");
            }

            IngredientConfig? ingredient = ReadIngredient(
                connection, transaction, request.IngredientId);
            if (ingredient is null)
            {
                return RollbackFail<RecipeLineConfig>(
                    transaction, "Ingredients.NotFound", "Ingredient does not exist.");
            }

            if (ScalarLong(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*) FROM recipe_lines
                    WHERE recipe_id=$recipe AND ingredient_id=$ingredient;
                    """,
                    ("$recipe", recipeId),
                    ("$ingredient", request.IngredientId.ToString("D"))) != 0)
            {
                transaction.Rollback();
                return Result<RecipeLineConfig>.Fail(Error.Conflict(
                    "Recipes.DuplicateIngredientLine",
                    "Recipe already contains this ingredient. Use the explicit recipe-line update contract."));
            }

            Result<long> normalized = IngredientQuantityMath.NormalizeToStockX1000(
                ingredient.StockUnit,
                request.InputUnit,
                request.DeclaredQuantityX1000);
            if (normalized.IsFailure)
            {
                transaction.Rollback();
                return Result<RecipeLineConfig>.Fail(normalized.ErrorDetail);
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO recipe_lines(
                    recipe_id,ingredient_id,input_unit,declared_quantity_x1000,
                    normalized_quantity_x1000,display_order)
                VALUES($recipe,$ingredient,$unit,$declared,$normalized,$order);
                """;
            command.Parameters.AddWithValue("$recipe", recipeId);
            command.Parameters.AddWithValue("$ingredient", request.IngredientId.ToString("D"));
            command.Parameters.AddWithValue("$unit", request.InputUnit);
            command.Parameters.AddWithValue("$declared", request.DeclaredQuantityX1000);
            command.Parameters.AddWithValue("$normalized", normalized.Value);
            command.Parameters.AddWithValue("$order", request.DisplayOrder);
            command.ExecuteNonQuery();
            transaction.Commit();

            return Result<RecipeLineConfig>.Success(new RecipeLineConfig(
                recipeId,
                request.IngredientId,
                request.InputUnit,
                request.DeclaredQuantityX1000,
                normalized.Value,
                request.DisplayOrder));
        }
        catch (SqliteException ex)
        {
            return Result<RecipeLineConfig>.Fail(Error.Unexpected(
                "Recipes.SaveLineFailed", ex.Message));
        }
    }

    public Result<RecipeLineConfig> UpdateRecipeLine(RecipeLineUpdateMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.RecipeId) || request.IngredientId == Guid.Empty)
        {
            return Result<RecipeLineConfig>.Fail(Error.Validation(
                "Recipes.LineIdentityRequired", "Recipe and ingredient identities are required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string recipeId = request.RecipeId.Trim();

            IngredientConfig? ingredient = ReadIngredient(
                connection, transaction, request.IngredientId);
            if (ingredient is null)
            {
                transaction.Rollback();
                return Result<RecipeLineConfig>.Fail(Error.NotFound(
                    "Ingredients.NotFound", "Ingredient does not exist."));
            }

            if (ScalarLong(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*) FROM recipe_lines
                    WHERE recipe_id=$recipe AND ingredient_id=$ingredient;
                    """,
                    ("$recipe", recipeId),
                    ("$ingredient", request.IngredientId.ToString("D"))) != 1)
            {
                transaction.Rollback();
                return Result<RecipeLineConfig>.Fail(Error.NotFound(
                    "Recipes.LineNotFound",
                    "Recipe ingredient line does not exist for explicit update."));
            }

            Result<long> normalized = IngredientQuantityMath.NormalizeToStockX1000(
                ingredient.StockUnit,
                request.InputUnit,
                request.DeclaredQuantityX1000);
            if (normalized.IsFailure)
            {
                transaction.Rollback();
                return Result<RecipeLineConfig>.Fail(normalized.ErrorDetail);
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE recipe_lines
                SET input_unit=$unit,
                    declared_quantity_x1000=$declared,
                    normalized_quantity_x1000=$normalized,
                    display_order=$order
                WHERE recipe_id=$recipe AND ingredient_id=$ingredient;
                """;
            command.Parameters.AddWithValue("$recipe", recipeId);
            command.Parameters.AddWithValue("$ingredient", request.IngredientId.ToString("D"));
            command.Parameters.AddWithValue("$unit", request.InputUnit);
            command.Parameters.AddWithValue("$declared", request.DeclaredQuantityX1000);
            command.Parameters.AddWithValue("$normalized", normalized.Value);
            command.Parameters.AddWithValue("$order", request.DisplayOrder);

            if (command.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return Result<RecipeLineConfig>.Fail(Error.Conflict(
                    "Recipes.LineUpdateConflict",
                    "Recipe ingredient line update lost its stable target."));
            }

            transaction.Commit();
            return Result<RecipeLineConfig>.Success(new RecipeLineConfig(
                recipeId,
                request.IngredientId,
                request.InputUnit,
                request.DeclaredQuantityX1000,
                normalized.Value,
                request.DisplayOrder));
        }
        catch (SqliteException ex)
        {
            return Result<RecipeLineConfig>.Fail(Error.Unexpected(
                "Recipes.UpdateLineFailed", ex.Message));
        }
    }

    public Result SetItemRecipe(Guid productId, string? recipeId) =>
        SetBinding("recipe_item_bindings", "product_id", productId.ToString("D"), recipeId,
            "products", "id", RecipeBindingTypes.Item);

    public Result SetVariantRecipe(string variantId, string? recipeId) =>
        SetBinding("recipe_variant_bindings", "variant_id", variantId, recipeId,
            "menu_variants", "id", RecipeBindingTypes.Variant);

    public Result SetModifierOptionRecipe(string optionId, string? recipeId) =>
        SetBinding("recipe_modifier_option_bindings", "option_id", optionId, recipeId,
            "menu_modifier_options", "id", RecipeBindingTypes.ModifierOption);

    public Result<RecipeBookPublicationSnapshot> PublishCurrent()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid storeId = ReadStoreId(connection, transaction);
            PublicationMaterial material = ReadPublicationMaterial(connection, transaction, revisionNumber: null);
            string contentHash = ComputeContentHash(storeId, material);

            RecipeBookPublicationSummary? current =
                ReadCurrentSummary(connection, transaction);

            if (current is not null &&
                string.Equals(current.ContentHash, contentHash, StringComparison.Ordinal))
            {
                RecipeBookPublicationSnapshot existing =
                    ReadPublication(connection, transaction, current.RevisionId);
                transaction.Commit();
                return Result<RecipeBookPublicationSnapshot>.Success(existing);
            }

            int revisionNumber = ReadNextRevisionNumber(connection, transaction, storeId);
            string revisionId = storeId.ToString("D") + ":" +
                revisionNumber.ToString(CultureInfo.InvariantCulture);
            PublicationMaterial revisionMaterial =
                MaterialWithRecipeRevisionIds(material, revisionNumber);
            contentHash = ComputeContentHash(storeId, revisionMaterial);
            string publicationHash = ComputeHash(
                storeId.ToString("D") + "|" + revisionId + "|" +
                revisionNumber.ToString(CultureInfo.InvariantCulture) + "|" + contentHash);
            DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

            if (current is not null)
            {
                using SqliteCommand supersede = connection.CreateCommand();
                supersede.Transaction = transaction;
                supersede.CommandText = """
                    UPDATE recipe_book_revisions
                    SET status='Superseded', superseded_at_utc=$at
                    WHERE revision_id=$revision;
                    """;
                supersede.Parameters.AddWithValue("$at", publishedAt.ToString("O"));
                supersede.Parameters.AddWithValue("$revision", current.RevisionId);
                supersede.ExecuteNonQuery();
            }

            InsertRevision(
                connection, transaction, revisionId, storeId, revisionNumber,
                contentHash, publicationHash, publishedAt);
            InsertSnapshotRows(connection, transaction, revisionId, revisionMaterial);
            UpsertCurrent(
                connection, transaction, storeId, revisionId,
                contentHash, publicationHash, publishedAt);

            transaction.Commit();
            return Result<RecipeBookPublicationSnapshot>.Success(
                new RecipeBookPublicationSnapshot(
                    revisionId, storeId, revisionNumber, contentHash, publicationHash,
                    publishedAt, revisionMaterial.Ingredients, revisionMaterial.Recipes,
                    revisionMaterial.Lines, revisionMaterial.ItemBindings,
                    revisionMaterial.VariantBindings, revisionMaterial.ModifierOptionBindings));
        }
        catch (InvalidOperationException ex)
        {
            return Result<RecipeBookPublicationSnapshot>.Fail(Error.Validation(
                "Recipes.PublishInvalidState", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or OverflowException or FormatException)
        {
            return Result<RecipeBookPublicationSnapshot>.Fail(Error.Unexpected(
                "Recipes.PublishFailed", ex.Message));
        }
    }

    public Result<RecipeBookPublicationSnapshot?> GetCurrentPublication()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            RecipeBookPublicationSummary? current = ReadCurrentSummary(connection, null);
            return Result<RecipeBookPublicationSnapshot?>.Success(
                current is null ? null : ReadPublication(connection, null, current.RevisionId));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<RecipeBookPublicationSnapshot?>.Fail(Error.Unexpected(
                "Recipes.ReadPublicationFailed", ex.Message));
        }
    }

    public Result<RecipeBookPublicationApplyResult> ApplyPublication(
        RecipeBookPublicationSnapshot snapshot)
    {
        if (snapshot.StoreId == Guid.Empty || snapshot.RevisionNumber <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.RevisionId))
        {
            return Result<RecipeBookPublicationApplyResult>.Fail(Error.Validation(
                "Recipes.InvalidPublication", "RecipeBook publication identity is invalid."));
        }

        string expectedRevisionId = snapshot.StoreId.ToString("D") + ":" +
            snapshot.RevisionNumber.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(expectedRevisionId, snapshot.RevisionId, StringComparison.Ordinal))
        {
            return Result<RecipeBookPublicationApplyResult>.Fail(Error.Conflict(
                "Recipes.PublicationIdentityConflict",
                "RecipeBook revision identity does not match store/revision authority."));
        }

        PublicationMaterial material = new(
            snapshot.Ingredients, snapshot.Recipes, snapshot.Lines,
            snapshot.ItemBindings, snapshot.VariantBindings, snapshot.ModifierOptionBindings);

        string contentHash = ComputeContentHash(snapshot.StoreId, material);
        string publicationHash = ComputeHash(
            snapshot.StoreId.ToString("D") + "|" + snapshot.RevisionId + "|" +
            snapshot.RevisionNumber.ToString(CultureInfo.InvariantCulture) + "|" + contentHash);

        if (!string.Equals(contentHash, snapshot.ContentHash, StringComparison.Ordinal) ||
            !string.Equals(publicationHash, snapshot.PublicationHash, StringComparison.Ordinal))
        {
            return Result<RecipeBookPublicationApplyResult>.Fail(Error.Conflict(
                "Recipes.PublicationHashConflict",
                "RecipeBook content/hash failed deterministic verification."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid localStoreId = ReadStoreId(connection, transaction);
            if (localStoreId != snapshot.StoreId)
            {
                transaction.Rollback();
                return Result<RecipeBookPublicationApplyResult>.Fail(Error.Conflict(
                    "Recipes.ForeignStorePublication",
                    "RecipeBook StoreId does not match local store authority."));
            }

            RecipeBookPublicationSummary? existing =
                ReadRevisionSummary(connection, transaction, snapshot.RevisionId);
            RecipeBookPublicationSummary? current =
                ReadCurrentSummary(connection, transaction);

            if (existing is not null &&
                (!string.Equals(existing.ContentHash, snapshot.ContentHash, StringComparison.Ordinal) ||
                 !string.Equals(existing.PublicationHash, snapshot.PublicationHash, StringComparison.Ordinal)))
            {
                transaction.Rollback();
                return Result<RecipeBookPublicationApplyResult>.Fail(Error.Conflict(
                    "Recipes.RevisionContentConflict",
                    "Same RecipeBook revision identity already exists with different content."));
            }

            if (current is not null && current.RevisionNumber > snapshot.RevisionNumber)
            {
                transaction.Rollback();
                return Result<RecipeBookPublicationApplyResult>.Fail(Error.Conflict(
                    "Recipes.StalePublication",
                    "Stale RecipeBook publication cannot replace newer authority."));
            }

            if (current is not null &&
                current.RevisionNumber == snapshot.RevisionNumber &&
                string.Equals(current.PublicationHash, snapshot.PublicationHash, StringComparison.Ordinal))
            {
                transaction.Commit();
                return Result<RecipeBookPublicationApplyResult>.Success(
                    new RecipeBookPublicationApplyResult(false, true, current));
            }

            // ApplyPublication is the shared runtime projection path. It installs
            // immutable published RecipeBook authority plus ingredient identity/config,
            // but does not synthesize mutable authoring recipes/lines/bindings.
            // Cafe bootstrap owns bootstrap-time authoring/config materialization.
            EnsurePublishedIngredientConfig(connection, transaction, snapshot.Ingredients);

            if (existing is null)
            {
                InsertRevision(
                    connection, transaction, snapshot.RevisionId, snapshot.StoreId,
                    snapshot.RevisionNumber, snapshot.ContentHash, snapshot.PublicationHash,
                    snapshot.PublishedAtUtc);
                InsertSnapshotRows(connection, transaction, snapshot.RevisionId, material);
            }

            if (current is not null)
            {
                using SqliteCommand supersede = connection.CreateCommand();
                supersede.Transaction = transaction;
                supersede.CommandText = """
                    UPDATE recipe_book_revisions
                    SET status='Superseded', superseded_at_utc=$at
                    WHERE revision_id=$revision;
                    """;
                supersede.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                supersede.Parameters.AddWithValue("$revision", current.RevisionId);
                supersede.ExecuteNonQuery();
            }

            UpsertCurrent(
                connection, transaction, snapshot.StoreId, snapshot.RevisionId,
                snapshot.ContentHash, snapshot.PublicationHash, DateTimeOffset.UtcNow);

            transaction.Commit();
            RecipeBookPublicationSummary applied = new(
                snapshot.RevisionId, snapshot.StoreId, snapshot.RevisionNumber,
                snapshot.ContentHash, snapshot.PublicationHash, "Published",
                snapshot.PublishedAtUtc);
            return Result<RecipeBookPublicationApplyResult>.Success(
                new RecipeBookPublicationApplyResult(true, false, applied));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<RecipeBookPublicationApplyResult>.Fail(Error.Unexpected(
                "Recipes.ApplyPublicationFailed", ex.Message));
        }
    }

    private Result SetBinding(
        string table,
        string ownerColumn,
        string ownerId,
        string? recipeId,
        string ownerTable,
        string ownerIdColumn,
        string bindingType)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Result.Fail(Error.Validation(
                "Recipes.BindingIdentityRequired", bindingType + " binding identity is required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            if (!Exists(connection, transaction, ownerTable, ownerIdColumn, ownerId))
            {
                transaction.Rollback();
                return Result.Fail(Error.Validation(
                    "Recipes.BindingTargetMissing", bindingType + " binding target does not exist."));
            }

            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM " + table + " WHERE " + ownerColumn + "=$owner;";
                delete.Parameters.AddWithValue("$owner", ownerId);
                delete.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(recipeId))
            {
                if (!Exists(connection, transaction, "recipes", "id", recipeId.Trim()))
                {
                    transaction.Rollback();
                    return Result.Fail(Error.Validation(
                        "Recipes.BindingRecipeMissing", "Binding recipe does not exist."));
                }

                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO " + table +
                    "(" + ownerColumn + ",recipe_id) VALUES($owner,$recipe);";
                insert.Parameters.AddWithValue("$owner", ownerId);
                insert.Parameters.AddWithValue("$recipe", recipeId.Trim());
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
            return Result.Success();
        }
        catch (SqliteException ex)
        {
            return Result.Fail(Error.Unexpected("Recipes.SaveBindingFailed", ex.Message));
        }
    }

    private static IngredientRecipeDraft ReadDraft(SqliteConnection connection)
    {
        List<IngredientConfig> ingredients = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,is_active,notes
                FROM ingredients ORDER BY name,id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ingredients.Add(new IngredientConfig(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetInt32(5) == 1,
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        List<RecipeConfig> recipes = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,yield_x1000,is_active FROM recipes ORDER BY name,id;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                recipes.Add(new RecipeConfig(
                    reader.GetString(0), reader.GetString(1), reader.GetInt64(2),
                    reader.GetInt32(3) == 1));
            }
        }

        List<RecipeLineConfig> lines = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT recipe_id,ingredient_id,input_unit,declared_quantity_x1000,
                       normalized_quantity_x1000,display_order
                FROM recipe_lines ORDER BY recipe_id,display_order,ingredient_id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                lines.Add(new RecipeLineConfig(
                    reader.GetString(0), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetInt32(5)));
            }
        }

        return new IngredientRecipeDraft(
            ingredients, recipes, lines,
            ReadDraftBindings(connection, "recipe_item_bindings", "product_id", RecipeBindingTypes.Item),
            ReadDraftBindings(connection, "recipe_variant_bindings", "variant_id", RecipeBindingTypes.Variant),
            ReadDraftBindings(connection, "recipe_modifier_option_bindings", "option_id", RecipeBindingTypes.ModifierOption),
            ReadCurrentSummary(connection, null));
    }

    private static List<RecipeBindingConfig> ReadDraftBindings(
        SqliteConnection connection,
        string table,
        string ownerColumn,
        string bindingType)
    {
        List<RecipeBindingConfig> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT " + ownerColumn + ",recipe_id FROM " + table +
            " ORDER BY " + ownerColumn + ";";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new RecipeBindingConfig(bindingType, reader.GetString(0), reader.GetString(1)));
        }
        return result;
    }

    private static PublicationMaterial ReadPublicationMaterial(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int? revisionNumber)
    {
        List<RecipeBookIngredientSnapshot> ingredients = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id,name,stock_unit,reorder_level_x1000
                FROM ingredients WHERE is_active=1 ORDER BY name,id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            int order = 0;
            while (reader.Read())
            {
                ingredients.Add(new RecipeBookIngredientSnapshot(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), order++));
            }
        }

        HashSet<Guid> ingredientIds = ingredients.Select(x => x.IngredientId).ToHashSet();

        List<RecipeBookRecipeSnapshot> recipes = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id,name,yield_x1000
                FROM recipes WHERE is_active=1 ORDER BY name,id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            int order = 0;
            while (reader.Read())
            {
                string recipeId = reader.GetString(0);
                recipes.Add(new RecipeBookRecipeSnapshot(
                    recipeId,
                    revisionNumber.HasValue
                        ? recipeId + ":" + revisionNumber.Value.ToString(CultureInfo.InvariantCulture)
                        : recipeId + ":draft",
                    reader.GetString(1), reader.GetInt64(2), order++));
            }
        }

        HashSet<string> recipeIds = recipes.Select(x => x.RecipeId).ToHashSet(StringComparer.Ordinal);
        Dictionary<string,string> recipeRevisionIds =
            recipes.ToDictionary(x => x.RecipeId, x => x.RecipeRevisionId, StringComparer.Ordinal);

        List<RecipeBookLineSnapshot> lines = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT l.recipe_id,l.ingredient_id,i.stock_unit,l.normalized_quantity_x1000,l.display_order
                FROM recipe_lines l
                INNER JOIN ingredients i ON i.id=l.ingredient_id
                WHERE l.recipe_id IN (SELECT id FROM recipes WHERE is_active=1)
                ORDER BY l.recipe_id,l.display_order,l.ingredient_id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string recipeId = reader.GetString(0);
                Guid ingredientId = Guid.Parse(reader.GetString(1));
                if (!ingredientIds.Contains(ingredientId))
                {
                    throw new InvalidOperationException(
                        "Active recipe references inactive ingredient: " + ingredientId.ToString("D"));
                }

                lines.Add(new RecipeBookLineSnapshot(
                    recipeRevisionIds[recipeId], recipeId, ingredientId,
                    reader.GetString(2), reader.GetInt64(3), reader.GetInt32(4)));
            }
        }

        foreach (RecipeBookRecipeSnapshot recipe in recipes)
        {
            if (!lines.Any(x => string.Equals(x.RecipeId, recipe.RecipeId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Active recipe must contain at least one active ingredient line: " + recipe.Name);
            }
        }

        List<RecipeBookBindingSnapshot> itemBindings = ReadPublicationBindings(
            connection, transaction, "recipe_item_bindings", "product_id",
            RecipeBindingTypes.Item, "products", "id", "is_active", recipeIds, recipeRevisionIds);
        List<RecipeBookBindingSnapshot> variantBindings = ReadPublicationBindings(
            connection, transaction, "recipe_variant_bindings", "variant_id",
            RecipeBindingTypes.Variant, "menu_variants", "id", "is_active", recipeIds, recipeRevisionIds);
        List<RecipeBookBindingSnapshot> modifierBindings = ReadPublicationBindings(
            connection, transaction, "recipe_modifier_option_bindings", "option_id",
            RecipeBindingTypes.ModifierOption, "menu_modifier_options", "id", "is_active", recipeIds, recipeRevisionIds);

        return new PublicationMaterial(
            ingredients, recipes, lines, itemBindings, variantBindings, modifierBindings);
    }

    private static List<RecipeBookBindingSnapshot> ReadPublicationBindings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string bindingTable,
        string ownerColumn,
        string bindingType,
        string ownerTable,
        string ownerIdColumn,
        string activeColumn,
        HashSet<string> recipeIds,
        IReadOnlyDictionary<string,string> recipeRevisionIds)
    {
        List<RecipeBookBindingSnapshot> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT b." + ownerColumn + ",b.recipe_id,t." + activeColumn +
            " FROM " + bindingTable + " b INNER JOIN " + ownerTable + " t ON t." +
            ownerIdColumn + "=b." + ownerColumn + " ORDER BY b." + ownerColumn + ";";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string owner = reader.GetString(0);
            string recipe = reader.GetString(1);
            if (reader.GetInt32(2) != 1)
            {
                throw new InvalidOperationException(
                    bindingType + " recipe binding references an inactive C1 target: " + owner);
            }
            if (!recipeIds.Contains(recipe))
            {
                throw new InvalidOperationException(
                    bindingType + " recipe binding references an inactive recipe: " + recipe);
            }
            result.Add(new RecipeBookBindingSnapshot(
                bindingType, owner, recipe, recipeRevisionIds[recipe]));
        }
        return result;
    }

    private static PublicationMaterial MaterialWithRecipeRevisionIds(
        PublicationMaterial material,
        int revisionNumber)
    {
        Dictionary<string,string> ids = material.Recipes.ToDictionary(
            x => x.RecipeId,
            x => x.RecipeId + ":" + revisionNumber.ToString(CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        return new PublicationMaterial(
            material.Ingredients,
            material.Recipes.Select(x => x with { RecipeRevisionId = ids[x.RecipeId] }).ToArray(),
            material.Lines.Select(x => x with { RecipeRevisionId = ids[x.RecipeId] }).ToArray(),
            material.ItemBindings.Select(x => x with { RecipeRevisionId = ids[x.RecipeId] }).ToArray(),
            material.VariantBindings.Select(x => x with { RecipeRevisionId = ids[x.RecipeId] }).ToArray(),
            material.ModifierOptionBindings.Select(x => x with { RecipeRevisionId = ids[x.RecipeId] }).ToArray());
    }

    private static string ComputeContentHash(Guid storeId, PublicationMaterial material)
    {
        StringBuilder builder = new();
        Append(builder, "store", storeId.ToString("D"));
        foreach (RecipeBookIngredientSnapshot x in material.Ingredients.OrderBy(x => x.DisplayOrder).ThenBy(x => x.IngredientId))
            Append(builder, "ingredient", x.IngredientId.ToString("D"), x.Name, x.StockUnit,
                x.ReorderLevelX1000.ToString(CultureInfo.InvariantCulture), x.DisplayOrder.ToString(CultureInfo.InvariantCulture));
        foreach (RecipeBookRecipeSnapshot x in material.Recipes.OrderBy(x => x.DisplayOrder).ThenBy(x => x.RecipeId, StringComparer.Ordinal))
            Append(builder, "recipe", x.RecipeId, x.Name,
                x.YieldX1000.ToString(CultureInfo.InvariantCulture), x.DisplayOrder.ToString(CultureInfo.InvariantCulture));
        foreach (RecipeBookLineSnapshot x in material.Lines.OrderBy(x => x.RecipeId, StringComparer.Ordinal).ThenBy(x => x.DisplayOrder).ThenBy(x => x.IngredientId))
            Append(builder, "line", x.RecipeId, x.IngredientId.ToString("D"), x.StockUnit,
                x.NormalizedQuantityX1000.ToString(CultureInfo.InvariantCulture), x.DisplayOrder.ToString(CultureInfo.InvariantCulture));
        foreach (RecipeBookBindingSnapshot x in material.ItemBindings.OrderBy(x => x.BindingId, StringComparer.Ordinal))
            Append(builder, "item", x.BindingId, x.RecipeId);
        foreach (RecipeBookBindingSnapshot x in material.VariantBindings.OrderBy(x => x.BindingId, StringComparer.Ordinal))
            Append(builder, "variant", x.BindingId, x.RecipeId);
        foreach (RecipeBookBindingSnapshot x in material.ModifierOptionBindings.OrderBy(x => x.BindingId, StringComparer.Ordinal))
            Append(builder, "modifier", x.BindingId, x.RecipeId);
        return ComputeHash(builder.ToString());
    }

    private static void Append(StringBuilder builder, params string?[] values)
    {
        foreach (string? value in values)
        {
            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty)));
            builder.Append('|');
        }
        builder.Append('\n');
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Guid ReadStoreId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;";
        object? value = command.ExecuteScalar();
        if (value is not string text || !Guid.TryParse(text, out Guid storeId) || storeId == Guid.Empty)
        {
            throw new InvalidOperationException("Deployment StoreId is required before publishing recipes.");
        }
        return storeId;
    }

    private static int ReadNextRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        return checked((int)ScalarLong(
            connection, transaction,
            "SELECT COALESCE(MAX(revision_number),0)+1 FROM recipe_book_revisions WHERE store_id=$store;",
            ("$store", storeId.ToString("D"))));
    }

    private static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId,
        int revisionNumber,
        string contentHash,
        string publicationHash,
        DateTimeOffset publishedAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO recipe_book_revisions(
                revision_id,store_id,revision_number,content_hash,publication_hash,
                status,created_at_utc,published_at_utc,superseded_at_utc)
            VALUES($revision,$store,$number,$content,$publication,'Published',$at,$at,NULL);
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$number", revisionNumber);
        command.Parameters.AddWithValue("$content", contentHash);
        command.Parameters.AddWithValue("$publication", publicationHash);
        command.Parameters.AddWithValue("$at", publishedAt.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSnapshotRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        PublicationMaterial material)
    {
        foreach (RecipeBookIngredientSnapshot x in material.Ingredients)
            Execute(connection, transaction, """
                INSERT INTO recipe_book_revision_ingredients(
                    revision_id,ingredient_id,name,stock_unit,reorder_level_x1000,display_order)
                VALUES($r,$id,$name,$unit,$reorder,$order);
                """,
                ("$r",revisionId),("$id",x.IngredientId.ToString("D")),("$name",x.Name),
                ("$unit",x.StockUnit),("$reorder",x.ReorderLevelX1000),("$order",x.DisplayOrder));

        foreach (RecipeBookRecipeSnapshot x in material.Recipes)
            Execute(connection, transaction, """
                INSERT INTO recipe_book_revision_recipes(
                    revision_id,recipe_id,recipe_revision_id,name,yield_x1000,display_order)
                VALUES($r,$id,$rr,$name,$yield,$order);
                """,
                ("$r",revisionId),("$id",x.RecipeId),("$rr",x.RecipeRevisionId),("$name",x.Name),
                ("$yield",x.YieldX1000),("$order",x.DisplayOrder));

        foreach (RecipeBookLineSnapshot x in material.Lines)
            Execute(connection, transaction, """
                INSERT INTO recipe_book_revision_lines(
                    revision_id,recipe_revision_id,recipe_id,ingredient_id,stock_unit,
                    normalized_quantity_x1000,display_order)
                VALUES($r,$rr,$recipe,$ingredient,$unit,$quantity,$order);
                """,
                ("$r",revisionId),("$rr",x.RecipeRevisionId),("$recipe",x.RecipeId),
                ("$ingredient",x.IngredientId.ToString("D")),("$unit",x.StockUnit),
                ("$quantity",x.NormalizedQuantityX1000),("$order",x.DisplayOrder));

        foreach (RecipeBookBindingSnapshot x in material.ItemBindings
            .Concat(material.VariantBindings).Concat(material.ModifierOptionBindings))
            Execute(connection, transaction, """
                INSERT INTO recipe_book_revision_bindings(
                    revision_id,binding_type,binding_id,recipe_id,recipe_revision_id)
                VALUES($r,$type,$binding,$recipe,$rr);
                """,
                ("$r",revisionId),("$type",x.BindingType),("$binding",x.BindingId),
                ("$recipe",x.RecipeId),("$rr",x.RecipeRevisionId));
    }

    private static void EnsurePublishedIngredientConfig(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<RecipeBookIngredientSnapshot> ingredients)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        foreach (RecipeBookIngredientSnapshot x in ingredients)
        {
            Execute(connection, transaction, """
                INSERT INTO ingredients(
                    id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,
                    is_active,notes,created_at_utc,updated_at_utc)
                VALUES($id,$name,$unit,0,$reorder,1,NULL,$now,NULL)
                ON CONFLICT(id) DO UPDATE SET
                    name=excluded.name,
                    stock_unit=excluded.stock_unit,
                    reorder_level_x1000=excluded.reorder_level_x1000,
                    is_active=1,
                    updated_at_utc=$now;
                """,
                ("$id",x.IngredientId.ToString("D")),("$name",x.Name),("$unit",x.StockUnit),
                ("$reorder",x.ReorderLevelX1000),("$now",now));
        }
    }

    private static void UpsertCurrent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        string revisionId,
        string contentHash,
        string publicationHash,
        DateTimeOffset appliedAt)
    {
        Execute(connection, transaction, """
            INSERT INTO recipe_book_current_publication(
                store_id,revision_id,content_hash,publication_hash,applied_at_utc)
            VALUES($store,$revision,$content,$publication,$at)
            ON CONFLICT(store_id) DO UPDATE SET
                revision_id=excluded.revision_id,
                content_hash=excluded.content_hash,
                publication_hash=excluded.publication_hash,
                applied_at_utc=excluded.applied_at_utc;
            """,
            ("$store",storeId.ToString("D")),("$revision",revisionId),("$content",contentHash),
            ("$publication",publicationHash),("$at",appliedAt.ToUniversalTime().ToString("O")));
    }

    private static RecipeBookPublicationSummary? ReadCurrentSummary(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.revision_id,r.store_id,r.revision_number,r.content_hash,
                   r.publication_hash,r.status,r.published_at_utc
            FROM recipe_book_current_publication c
            INNER JOIN recipe_book_revisions r ON r.revision_id=c.revision_id
            LIMIT 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapSummary(reader) : null;
    }

    private static RecipeBookPublicationSummary? ReadRevisionSummary(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_id,store_id,revision_number,content_hash,publication_hash,status,published_at_utc
            FROM recipe_book_revisions WHERE revision_id=$revision LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapSummary(reader) : null;
    }

    private static RecipeBookPublicationSummary MapSummary(SqliteDataReader reader) =>
        new(
            reader.GetString(0), Guid.Parse(reader.GetString(1)), reader.GetInt32(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)));

    private static RecipeBookPublicationSnapshot ReadPublication(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string revisionId)
    {
        RecipeBookPublicationSummary summary =
            ReadRevisionSummary(connection, transaction, revisionId)
            ?? throw new InvalidOperationException("RecipeBook revision not found.");

        List<RecipeBookIngredientSnapshot> ingredients = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT ingredient_id,name,stock_unit,reorder_level_x1000,display_order
                FROM recipe_book_revision_ingredients
                WHERE revision_id=$revision ORDER BY display_order,ingredient_id;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                ingredients.Add(new RecipeBookIngredientSnapshot(
                    Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetString(2),
                    reader.GetInt64(3),reader.GetInt32(4)));
        }

        List<RecipeBookRecipeSnapshot> recipes = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT recipe_id,recipe_revision_id,name,yield_x1000,display_order
                FROM recipe_book_revision_recipes
                WHERE revision_id=$revision ORDER BY display_order,recipe_id;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                recipes.Add(new RecipeBookRecipeSnapshot(
                    reader.GetString(0),reader.GetString(1),reader.GetString(2),
                    reader.GetInt64(3),reader.GetInt32(4)));
        }

        List<RecipeBookLineSnapshot> lines = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT recipe_revision_id,recipe_id,ingredient_id,stock_unit,
                       normalized_quantity_x1000,display_order
                FROM recipe_book_revision_lines
                WHERE revision_id=$revision ORDER BY recipe_id,display_order,ingredient_id;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                lines.Add(new RecipeBookLineSnapshot(
                    reader.GetString(0),reader.GetString(1),Guid.Parse(reader.GetString(2)),
                    reader.GetString(3),reader.GetInt64(4),reader.GetInt32(5)));
        }

        List<RecipeBookBindingSnapshot> bindings = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT binding_type,binding_id,recipe_id,recipe_revision_id
                FROM recipe_book_revision_bindings
                WHERE revision_id=$revision ORDER BY binding_type,binding_id;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                bindings.Add(new RecipeBookBindingSnapshot(
                    reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3)));
        }

        return new RecipeBookPublicationSnapshot(
            summary.RevisionId,summary.StoreId,summary.RevisionNumber,
            summary.ContentHash,summary.PublicationHash,summary.PublishedAtUtc,
            ingredients,recipes,lines,
            bindings.Where(x=>x.BindingType==RecipeBindingTypes.Item).ToArray(),
            bindings.Where(x=>x.BindingType==RecipeBindingTypes.Variant).ToArray(),
            bindings.Where(x=>x.BindingType==RecipeBindingTypes.ModifierOption).ToArray());
    }

    private static IngredientConfig? ReadIngredient(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,is_active,notes
            FROM ingredients WHERE id=$id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new IngredientConfig(
                Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetString(2),
                reader.GetInt64(3),reader.GetInt64(4),reader.GetInt32(5)==1,
                reader.IsDBNull(6)?null:reader.GetString(6))
            : null;
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string idColumn,
        string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM " + table + " WHERE " + idColumn + "=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is not null;
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name,object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name,object? value) in parameters)
            command.Parameters.AddWithValue(name,value??DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar()??0L);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name,object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name,object? value) in parameters)
            command.Parameters.AddWithValue(name,value??DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static Result<T> RollbackFail<T>(
        SqliteTransaction transaction,
        string code,
        string message)
    {
        transaction.Rollback();
        return Result<T>.Fail(Error.Validation(code,message));
    }

    private static object DbText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(SqliteException ex) =>
        ex.SqliteExtendedErrorCode is 1555 or 2067 ||
        ex.Message.Contains("UNIQUE constraint failed",StringComparison.OrdinalIgnoreCase);

    private sealed record PublicationMaterial(
        IReadOnlyList<RecipeBookIngredientSnapshot> Ingredients,
        IReadOnlyList<RecipeBookRecipeSnapshot> Recipes,
        IReadOnlyList<RecipeBookLineSnapshot> Lines,
        IReadOnlyList<RecipeBookBindingSnapshot> ItemBindings,
        IReadOnlyList<RecipeBookBindingSnapshot> VariantBindings,
        IReadOnlyList<RecipeBookBindingSnapshot> ModifierOptionBindings);
}

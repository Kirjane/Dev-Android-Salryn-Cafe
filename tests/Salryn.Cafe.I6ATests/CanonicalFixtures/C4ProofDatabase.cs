// Synthetic fixture recovered from Cafe Yza 4241ea1e; production uses shared canonical persistence.
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;


namespace Salryn.Cafe.I6ATests;

internal sealed class C4ProofDatabase
{
    private C4ProofDatabase(DbPaths paths, SqliteConnectionFactory factory)
    {
        Paths = paths;
        Factory = factory;
        Orders = new SqliteCafeOrderStore(factory);
        Completion = new SqliteCafeOrderCompletionCoordinator(factory);
    }

    public DbPaths Paths { get; }
    public SqliteConnectionFactory Factory { get; }
    public SqliteCafeOrderStore Orders { get; }
    public SqliteCafeOrderCompletionCoordinator Completion { get; }

    public static C4ProofDatabase Create(string root)
    {
        C4ProofDatabase db = CreateEmpty(root);
        db.SeedAuthority();
        return db;
    }

    public static C4ProofDatabase CreateEmpty(string root)
    {
        DbPaths paths = DbPaths.CreateAtRoot(root);
        Result<DatabaseInitializationSummary> initialized =
            DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
        if (initialized.IsFailure)
            throw new InvalidOperationException(
                "C4 fixture initialization failed: " +
                initialized.ErrorDetail.Code + " / " + initialized.ErrorDetail.Message);

        return new C4ProofDatabase(paths, new SqliteConnectionFactory(paths));
    }

    public static C4ProofDatabase OpenExisting(string root)
    {
        DbPaths paths = DbPaths.CreateAtRoot(root);
        if (!File.Exists(paths.DatabasePath))
            throw new InvalidOperationException("C4 existing fixture database is missing: " + paths.DatabasePath);
        return new C4ProofDatabase(paths, new SqliteConnectionFactory(paths));
    }

    public void SeedAuthority(bool includeRecipeAuthority = true)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        Execute(
            "UPDATE deployment_identity SET store_id=$store,updated_at=$at;",
            ("$store", C4ProofIds.StoreId.ToString("D")),
            ("$at", now));

        string adminRole =
            ScalarString("SELECT id FROM roles WHERE name='Admin' LIMIT 1;")
            ?? throw new InvalidOperationException("Admin role missing.");
        string unitId =
            ScalarString("SELECT id FROM units WHERE name='Piece' LIMIT 1;")
            ?? throw new InvalidOperationException("Piece unit missing.");

        if (ScalarLong("SELECT COUNT(*) FROM users WHERE id=$id;",
            ("$id", C4ProofIds.ActorUserId.ToString("D"))) == 0)
        {
            Execute("""
                INSERT INTO users(
                    id,username,display_name,password_hash,role_id,status,created_at,updated_at)
                VALUES($id,'c4-proof','C4 Proof Manager','proof-only',$role,1,$at,NULL);
                """,
                ("$id", C4ProofIds.ActorUserId.ToString("D")),
                ("$role", adminRole),
                ("$at", now));
        }

        if (ScalarLong("SELECT COUNT(*) FROM products WHERE id=$id;",
            ("$id", C4ProofIds.ProductId.ToString("D"))) == 0)
        {
            Execute("""
                INSERT INTO products(
                    id,sku,barcode,name,description,category_id,brand_id,unit_id,supplier_id,
                    cost_price_cents,selling_price_cents,wholesale_price_cents,
                    stock_on_hand_x1000,reorder_level_x1000,track_stock,allow_discount,
                    is_active,notes,created_at,updated_at)
                VALUES($id,'C4-PRODUCT','C4-PRODUCT','C4 Coffee',NULL,NULL,NULL,$unit,NULL,
                    250,500,NULL,10000,1000,1,1,1,NULL,$at,NULL);
                """,
                ("$id", C4ProofIds.ProductId.ToString("D")),
                ("$unit", unitId),
                ("$at", now));
        }

        Execute("""
            INSERT OR IGNORE INTO menu_revisions(
                revision_id,store_id,revision_number,content_hash,publication_hash,status,
                created_at_utc,published_at_utc,superseded_at_utc)
            VALUES($rev,$store,1,'c4-proof-content','c4-proof-publication','Published',$at,$at,NULL);
            """,
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$store", C4ProofIds.StoreId.ToString("D")),
            ("$at", now));

        Execute("""
            INSERT OR IGNORE INTO menu_revision_items(
                revision_id,product_id,category_id,name,description,unit_id,unit_name,
                sku,barcode,selling_price_cents,cost_price_cents,is_temporarily_unavailable,display_order)
            VALUES($rev,$product,NULL,'C4 Coffee',NULL,$unit,'Piece',
                'C4-PRODUCT','C4-PRODUCT',500,250,0,0);
            """,
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$product", C4ProofIds.ProductId.ToString("D")),
            ("$unit", unitId));

        Execute("""
            INSERT OR IGNORE INTO menu_revision_variants(
                revision_id,variant_id,product_id,name,sku,barcode,selling_price_cents,
                cost_price_cents,is_temporarily_unavailable,display_order)
            VALUES($rev,$variant,$product,'Large',NULL,NULL,650,300,0,0);
            """,
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$variant", C4ProofIds.VariantId),
            ("$product", C4ProofIds.ProductId.ToString("D")));

        Execute("""
            INSERT OR IGNORE INTO menu_revision_modifier_groups(
                revision_id,group_id,name,is_required,min_selections,max_selections,display_order)
            VALUES($rev,$group,'Extras',0,0,1,0);
            """,
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$group", C4ProofIds.ModifierGroupId));

        Execute("""
            INSERT OR IGNORE INTO menu_revision_modifier_options(
                revision_id,option_id,group_id,name,price_delta_cents,display_order)
            VALUES($rev,$option,$group,'Extra Shot',50,0);
            """,
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$option", C4ProofIds.ModifierOptionId),
            ("$group", C4ProofIds.ModifierGroupId));

        Execute("""
            INSERT OR IGNORE INTO menu_revision_variant_modifier_groups(
                revision_id,variant_id,group_id,display_order)
            VALUES($rev,$variant,$group,0);
            """,
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$variant", C4ProofIds.VariantId),
            ("$group", C4ProofIds.ModifierGroupId));

        Execute("""
            INSERT OR REPLACE INTO menu_current_publication(
                store_id,revision_id,content_hash,publication_hash,applied_at_utc)
            VALUES($store,$rev,'c4-proof-content','c4-proof-publication',$at);
            """,
            ("$store", C4ProofIds.StoreId.ToString("D")),
            ("$rev", C4ProofIds.MenuRevisionId),
            ("$at", now));

        if (includeRecipeAuthority)
            SeedRecipeAuthority(now);

        if (ScalarLong(
            "SELECT COUNT(*) FROM cashier_sessions WHERE id=$id;",
            ("$id", C4ProofIds.CashierSessionId.ToString("D"))) == 0)
        {
            Execute("""
                INSERT INTO cashier_sessions(
                    id,user_id,opened_at,closed_at,opening_cash_cents,expected_cash_cents,
                    counted_cash_cents,variance_cents,status,notes)
                VALUES($id,$user,$at,NULL,0,0,NULL,0,1,'C4 proof');
                """,
                ("$id", C4ProofIds.CashierSessionId.ToString("D")),
                ("$user", C4ProofIds.ActorUserId.ToString("D")),
                ("$at", now));
        }
    }

    private void SeedRecipeAuthority(string now)
    {
        foreach ((Guid id, string name) in new[]
        {
            (C4ProofIds.ItemIngredientId, "C4 Item Ingredient"),
            (C4ProofIds.VariantIngredientId, "C4 Variant Ingredient"),
            (C4ProofIds.ModifierIngredientId, "C4 Modifier Ingredient")
        })
        {
            if (ScalarLong("SELECT COUNT(*) FROM ingredients WHERE id=$id;", ("$id", id.ToString("D"))) == 0)
            {
                Execute("""
                    INSERT INTO ingredients(
                        id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,
                        is_active,notes,created_at_utc,updated_at_utc)
                    VALUES($id,$name,'Piece',100000,1000,1,'C4 proof',$at,NULL);
                    """,
                    ("$id", id.ToString("D")),
                    ("$name", name),
                    ("$at", now));
            }
        }

        Execute("""
            INSERT OR IGNORE INTO recipe_book_revisions(
                revision_id,store_id,revision_number,content_hash,publication_hash,status,
                created_at_utc,published_at_utc,superseded_at_utc)
            VALUES($rev,$store,1,'c4-recipe-content','c4-recipe-publication','Published',$at,$at,NULL);
            """,
            ("$rev", C4ProofIds.RecipeBookRevisionId),
            ("$store", C4ProofIds.StoreId.ToString("D")),
            ("$at", now));

        foreach ((Guid id, string name, int display) in new[]
        {
            (C4ProofIds.ItemIngredientId, "C4 Item Ingredient", 0),
            (C4ProofIds.VariantIngredientId, "C4 Variant Ingredient", 1),
            (C4ProofIds.ModifierIngredientId, "C4 Modifier Ingredient", 2)
        })
        {
            Execute("""
                INSERT OR IGNORE INTO recipe_book_revision_ingredients(
                    revision_id,ingredient_id,name,stock_unit,reorder_level_x1000,display_order)
                VALUES($rev,$id,$name,'Piece',1000,$display);
                """,
                ("$rev", C4ProofIds.RecipeBookRevisionId),
                ("$id", id.ToString("D")),
                ("$name", name),
                ("$display", display));
        }

        foreach ((string recipeId, string recipeRevisionId, string name, int display) in new[]
        {
            (C4ProofIds.ItemRecipeId, C4ProofIds.ItemRecipeRevisionId, "C4 Item Recipe", 0),
            (C4ProofIds.VariantRecipeId, C4ProofIds.VariantRecipeRevisionId, "C4 Variant Recipe", 1),
            (C4ProofIds.ModifierRecipeId, C4ProofIds.ModifierRecipeRevisionId, "C4 Modifier Recipe", 2)
        })
        {
            Execute("""
                INSERT OR IGNORE INTO recipe_book_revision_recipes(
                    revision_id,recipe_id,recipe_revision_id,name,yield_x1000,display_order)
                VALUES($rev,$recipe,$recipeRevision,$name,1000,$display);
                """,
                ("$rev", C4ProofIds.RecipeBookRevisionId),
                ("$recipe", recipeId),
                ("$recipeRevision", recipeRevisionId),
                ("$name", name),
                ("$display", display));
        }

        foreach ((string recipeId, string recipeRevisionId, Guid ingredientId, long quantity, int display) in new[]
        {
            (C4ProofIds.ItemRecipeId, C4ProofIds.ItemRecipeRevisionId, C4ProofIds.ItemIngredientId, 1000L, 0),
            (C4ProofIds.VariantRecipeId, C4ProofIds.VariantRecipeRevisionId, C4ProofIds.VariantIngredientId, 2000L, 0),
            (C4ProofIds.ModifierRecipeId, C4ProofIds.ModifierRecipeRevisionId, C4ProofIds.ModifierIngredientId, 500L, 0)
        })
        {
            Execute("""
                INSERT OR IGNORE INTO recipe_book_revision_lines(
                    revision_id,recipe_revision_id,recipe_id,ingredient_id,
                    stock_unit,normalized_quantity_x1000,display_order)
                VALUES($rev,$recipeRevision,$recipe,$ingredient,'Piece',$quantity,$display);
                """,
                ("$rev", C4ProofIds.RecipeBookRevisionId),
                ("$recipeRevision", recipeRevisionId),
                ("$recipe", recipeId),
                ("$ingredient", ingredientId.ToString("D")),
                ("$quantity", quantity),
                ("$display", display));
        }

        foreach ((string type, string id, string recipeId, string recipeRevisionId) in new[]
        {
            ("Item", C4ProofIds.ProductId.ToString("D"), C4ProofIds.ItemRecipeId, C4ProofIds.ItemRecipeRevisionId),
            ("Variant", C4ProofIds.VariantId, C4ProofIds.VariantRecipeId, C4ProofIds.VariantRecipeRevisionId),
            ("ModifierOption", C4ProofIds.ModifierOptionId, C4ProofIds.ModifierRecipeId, C4ProofIds.ModifierRecipeRevisionId)
        })
        {
            Execute("""
                INSERT OR IGNORE INTO recipe_book_revision_bindings(
                    revision_id,binding_type,binding_id,recipe_id,recipe_revision_id)
                VALUES($rev,$type,$id,$recipe,$recipeRevision);
                """,
                ("$rev", C4ProofIds.RecipeBookRevisionId),
                ("$type", type),
                ("$id", id),
                ("$recipe", recipeId),
                ("$recipeRevision", recipeRevisionId));
        }

        Execute("""
            INSERT OR REPLACE INTO recipe_book_current_publication(
                store_id,revision_id,content_hash,publication_hash,applied_at_utc)
            VALUES($store,$rev,'c4-recipe-content','c4-recipe-publication',$at);
            """,
            ("$store", C4ProofIds.StoreId.ToString("D")),
            ("$rev", C4ProofIds.RecipeBookRevisionId),
            ("$at", now));
    }

    public long ScalarLong(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    public string? ScalarString(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        object? scalarValue = command.ExecuteScalar();
        return scalarValue is null or DBNull ? null : Convert.ToString(scalarValue);
    }

    public void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection = Factory.CreateConnection();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}

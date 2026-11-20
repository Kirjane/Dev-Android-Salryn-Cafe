using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Repositories.Menu;

public sealed class SqliteMenuCatalogStore : SqliteRepositoryBase
{
    public SqliteMenuCatalogStore(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Result<MenuCatalogDraft> GetDraft()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            return Result<MenuCatalogDraft>.Success(ReadDraft(connection));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<MenuCatalogDraft>.Fail(Error.Unexpected(
                "MenuCatalog.ReadDraftFailed",
                ex.Message));
        }
    }

    public Result<MenuCategoryConfig> SaveCategory(MenuCategoryMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<MenuCategoryConfig>.Fail(Error.Validation(
                "Menu.CategoryNameRequired",
                "Menu category name is required."));
        }

        Guid id = request.Id is { } requested && requested != Guid.Empty
            ? requested
            : Guid.NewGuid();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO categories (id, name, is_active)
                VALUES ($id, $name, $isActive)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    is_active = excluded.is_active;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$name", request.Name.Trim());
            command.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
            command.ExecuteNonQuery();
            transaction.Commit();

            return Result<MenuCategoryConfig>.Success(new MenuCategoryConfig(
                id,
                request.Name.Trim(),
                request.IsActive));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<MenuCategoryConfig>.Fail(Error.Conflict(
                "Menu.DuplicateCategory",
                "Another menu category already uses that name."));
        }
        catch (SqliteException ex)
        {
            return Result<MenuCategoryConfig>.Fail(Error.Unexpected(
                "Menu.SaveCategoryFailed",
                ex.Message));
        }
    }

    public Result<MenuItemConfig> SaveItem(MenuItemMutation request)
    {
        Result validation = ValidateItem(request);
        if (validation.IsFailure)
        {
            return Result<MenuItemConfig>.Fail(validation.ErrorDetail);
        }

        Guid productId = request.ProductId is { } requested && requested != Guid.Empty
            ? requested
            : Guid.NewGuid();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            if (!Exists(connection, transaction, "units", request.UnitId.ToString("D")))
            {
                return FailAndRollback<MenuItemConfig>(
                    transaction,
                    "Menu.InvalidUnit",
                    "Menu item unit does not exist.");
            }

            if (request.CategoryId is { } categoryId &&
                !Exists(connection, transaction, "categories", categoryId.ToString("D")))
            {
                return FailAndRollback<MenuItemConfig>(
                    transaction,
                    "Menu.InvalidCategory",
                    "Menu item category does not exist.");
            }

            bool exists = Exists(connection, transaction, "products", productId.ToString("D"));
            string now = DateTimeOffset.UtcNow.ToString("O");

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = exists
                    ? """
                        UPDATE products
                        SET
                            sku = $sku,
                            barcode = $barcode,
                            name = $name,
                            description = $description,
                            category_id = $categoryId,
                            unit_id = $unitId,
                            cost_price_cents = $costPriceCents,
                            selling_price_cents = $sellingPriceCents,
                            is_active = $isActive,
                            updated_at = $now
                        WHERE id = $id;
                        """
                    : """
                        INSERT INTO products (
                            id,
                            sku,
                            barcode,
                            name,
                            description,
                            category_id,
                            brand_id,
                            unit_id,
                            supplier_id,
                            cost_price_cents,
                            selling_price_cents,
                            wholesale_price_cents,
                            stock_on_hand_x1000,
                            reorder_level_x1000,
                            track_stock,
                            allow_discount,
                            tax_classification,
                            is_active,
                            notes,
                            created_at,
                            updated_at)
                        VALUES (
                            $id,
                            $sku,
                            $barcode,
                            $name,
                            $description,
                            $categoryId,
                            NULL,
                            $unitId,
                            NULL,
                            $costPriceCents,
                            $sellingPriceCents,
                            NULL,
                            0,
                            0,
                            0,
                            1,
                            1,
                            $isActive,
                            'C1 menu item compatibility row. Ingredient stock authority begins in C2.',
                            $now,
                            NULL);
                        """;

                command.Parameters.AddWithValue("$id", productId.ToString("D"));
                command.Parameters.AddWithValue("$sku", DbText(request.Sku));
                command.Parameters.AddWithValue("$barcode", DbText(request.Barcode));
                command.Parameters.AddWithValue("$name", request.Name.Trim());
                command.Parameters.AddWithValue("$description", DbText(request.Description));
                command.Parameters.AddWithValue("$categoryId", request.CategoryId?.ToString("D") ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$unitId", request.UnitId.ToString("D"));
                command.Parameters.AddWithValue("$costPriceCents", request.CostPriceCents ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$sellingPriceCents", request.SellingPriceCents);
                command.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
                command.Parameters.AddWithValue("$now", now);
                command.ExecuteNonQuery();
            }

            using (SqliteCommand availability = connection.CreateCommand())
            {
                availability.Transaction = transaction;
                availability.CommandText = """
                    INSERT INTO menu_item_availability (
                        product_id,
                        is_temporarily_unavailable,
                        updated_at_utc)
                    VALUES ($productId, $unavailable, $now)
                    ON CONFLICT(product_id) DO UPDATE SET
                        is_temporarily_unavailable = excluded.is_temporarily_unavailable,
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                availability.Parameters.AddWithValue("$productId", productId.ToString("D"));
                availability.Parameters.AddWithValue("$unavailable", request.IsTemporarilyUnavailable ? 1 : 0);
                availability.Parameters.AddWithValue("$now", now);
                availability.ExecuteNonQuery();
            }

            if (!exists)
            {
                using SqliteCommand defaultVariant = connection.CreateCommand();
                defaultVariant.Transaction = transaction;
                defaultVariant.CommandText = """
                    INSERT INTO menu_variants (
                        id,
                        product_id,
                        name,
                        sku,
                        barcode,
                        selling_price_cents,
                        cost_price_cents,
                        is_active,
                        is_temporarily_unavailable,
                        display_order,
                        created_at_utc,
                        updated_at_utc)
                    VALUES (
                        $id,
                        $productId,
                        'Standard',
                        $sku,
                        $barcode,
                        $sellingPriceCents,
                        $costPriceCents,
                        $isActive,
                        $unavailable,
                        0,
                        $now,
                        NULL);
                    """;
                defaultVariant.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                defaultVariant.Parameters.AddWithValue("$productId", productId.ToString("D"));
                defaultVariant.Parameters.AddWithValue("$sku", DbText(request.Sku));
                defaultVariant.Parameters.AddWithValue("$barcode", DbText(request.Barcode));
                defaultVariant.Parameters.AddWithValue("$sellingPriceCents", request.SellingPriceCents);
                defaultVariant.Parameters.AddWithValue("$costPriceCents", request.CostPriceCents ?? (object)DBNull.Value);
                defaultVariant.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
                defaultVariant.Parameters.AddWithValue("$unavailable", request.IsTemporarilyUnavailable ? 1 : 0);
                defaultVariant.Parameters.AddWithValue("$now", now);
                defaultVariant.ExecuteNonQuery();
            }

            transaction.Commit();

            return Result<MenuItemConfig>.Success(new MenuItemConfig(
                productId,
                request.CategoryId,
                request.Name.Trim(),
                NullIfBlank(request.Description),
                request.UnitId,
                NullIfBlank(request.Sku),
                NullIfBlank(request.Barcode),
                request.SellingPriceCents,
                request.CostPriceCents,
                request.IsActive,
                request.IsTemporarilyUnavailable));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<MenuItemConfig>.Fail(Error.Conflict(
                "Menu.DuplicateIdentifier",
                "Menu item SKU, barcode, or name conflicts with existing active catalog authority."));
        }
        catch (SqliteException ex) when (IsForeignKeyViolation(ex))
        {
            return Result<MenuItemConfig>.Fail(Error.Validation(
                "Menu.InvalidReference",
                "Menu item contains an invalid category or unit reference."));
        }
        catch (SqliteException ex)
        {
            return Result<MenuItemConfig>.Fail(Error.Unexpected(
                "Menu.SaveItemFailed",
                ex.Message));
        }
    }

    public Result<MenuVariantConfig> SaveVariant(MenuVariantMutation request)
    {
        Result validation = ValidateVariant(request);
        if (validation.IsFailure)
        {
            return Result<MenuVariantConfig>.Fail(validation.ErrorDetail);
        }

        string id = string.IsNullOrWhiteSpace(request.Id)
            ? Guid.NewGuid().ToString("D")
            : request.Id.Trim();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            if (!Exists(connection, transaction, "products", request.ProductId.ToString("D")))
            {
                return FailAndRollback<MenuVariantConfig>(
                    transaction,
                    "Menu.ItemNotFound",
                    "Variant parent menu item does not exist.");
            }

            Result identifierCheck = EnsureVariantIdentifiersAvailable(
                connection,
                transaction,
                id,
                request.ProductId,
                request.Sku,
                request.Barcode,
                request.IsActive);
            if (identifierCheck.IsFailure)
            {
                transaction.Rollback();
                return Result<MenuVariantConfig>.Fail(identifierCheck.ErrorDetail);
            }

            string now = DateTimeOffset.UtcNow.ToString("O");
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO menu_variants (
                    id,
                    product_id,
                    name,
                    sku,
                    barcode,
                    selling_price_cents,
                    cost_price_cents,
                    is_active,
                    is_temporarily_unavailable,
                    display_order,
                    created_at_utc,
                    updated_at_utc)
                VALUES (
                    $id,
                    $productId,
                    $name,
                    $sku,
                    $barcode,
                    $sellingPriceCents,
                    $costPriceCents,
                    $isActive,
                    $unavailable,
                    $displayOrder,
                    $now,
                    NULL)
                ON CONFLICT(id) DO UPDATE SET
                    product_id = excluded.product_id,
                    name = excluded.name,
                    sku = excluded.sku,
                    barcode = excluded.barcode,
                    selling_price_cents = excluded.selling_price_cents,
                    cost_price_cents = excluded.cost_price_cents,
                    is_active = excluded.is_active,
                    is_temporarily_unavailable = excluded.is_temporarily_unavailable,
                    display_order = excluded.display_order,
                    updated_at_utc = $now;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$productId", request.ProductId.ToString("D"));
            command.Parameters.AddWithValue("$name", request.Name.Trim());
            command.Parameters.AddWithValue("$sku", DbText(request.Sku));
            command.Parameters.AddWithValue("$barcode", DbText(request.Barcode));
            command.Parameters.AddWithValue("$sellingPriceCents", request.SellingPriceCents);
            command.Parameters.AddWithValue("$costPriceCents", request.CostPriceCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$unavailable", request.IsTemporarilyUnavailable ? 1 : 0);
            command.Parameters.AddWithValue("$displayOrder", Math.Max(0, request.DisplayOrder));
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
            transaction.Commit();

            return Result<MenuVariantConfig>.Success(new MenuVariantConfig(
                id,
                request.ProductId,
                request.Name.Trim(),
                NullIfBlank(request.Sku),
                NullIfBlank(request.Barcode),
                request.SellingPriceCents,
                request.CostPriceCents,
                request.IsActive,
                request.IsTemporarilyUnavailable,
                Math.Max(0, request.DisplayOrder)));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<MenuVariantConfig>.Fail(Error.Conflict(
                "Menu.DuplicateVariant",
                "Variant name or barcode conflicts with existing menu authority."));
        }
        catch (SqliteException ex)
        {
            return Result<MenuVariantConfig>.Fail(Error.Unexpected(
                "Menu.SaveVariantFailed",
                ex.Message));
        }
    }

    public Result<MenuModifierGroupConfig> SaveModifierGroup(MenuModifierGroupMutation request)
    {
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Result<MenuModifierGroupConfig>.Fail(Error.Validation(
                "Menu.ModifierGroupNameRequired",
                "Modifier group name is required."));
        }

        if (request.MinSelections < 0 || request.MaxSelections < request.MinSelections)
        {
            return Result<MenuModifierGroupConfig>.Fail(Error.Validation(
                "Menu.InvalidModifierSelectionConstraint",
                "Modifier selection limits are invalid."));
        }

        if (request.IsRequired && request.MinSelections < 1)
        {
            return Result<MenuModifierGroupConfig>.Fail(Error.Validation(
                "Menu.RequiredModifierNeedsMinimum",
                "A required modifier group must require at least one selection."));
        }

        string id = string.IsNullOrWhiteSpace(request.Id)
            ? Guid.NewGuid().ToString("D")
            : request.Id.Trim();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            string now = DateTimeOffset.UtcNow.ToString("O");
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO menu_modifier_groups (
                    id,
                    name,
                    is_required,
                    min_selections,
                    max_selections,
                    is_active,
                    display_order,
                    created_at_utc,
                    updated_at_utc)
                VALUES (
                    $id,
                    $name,
                    $isRequired,
                    $minSelections,
                    $maxSelections,
                    $isActive,
                    $displayOrder,
                    $now,
                    NULL)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    is_required = excluded.is_required,
                    min_selections = excluded.min_selections,
                    max_selections = excluded.max_selections,
                    is_active = excluded.is_active,
                    display_order = excluded.display_order,
                    updated_at_utc = $now;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$isRequired", request.IsRequired ? 1 : 0);
            command.Parameters.AddWithValue("$minSelections", request.MinSelections);
            command.Parameters.AddWithValue("$maxSelections", request.MaxSelections);
            command.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$displayOrder", Math.Max(0, request.DisplayOrder));
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
            transaction.Commit();

            return Result<MenuModifierGroupConfig>.Success(new MenuModifierGroupConfig(
                id,
                name,
                request.IsRequired,
                request.MinSelections,
                request.MaxSelections,
                request.IsActive,
                Math.Max(0, request.DisplayOrder)));
        }
        catch (SqliteException ex)
        {
            return Result<MenuModifierGroupConfig>.Fail(Error.Unexpected(
                "Menu.SaveModifierGroupFailed",
                ex.Message));
        }
    }

    public Result<MenuModifierOptionConfig> SaveModifierOption(MenuModifierOptionMutation request)
    {
        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || string.IsNullOrWhiteSpace(request.GroupId))
        {
            return Result<MenuModifierOptionConfig>.Fail(Error.Validation(
                "Menu.InvalidModifierOption",
                "Modifier option name and group are required."));
        }

        string id = string.IsNullOrWhiteSpace(request.Id)
            ? Guid.NewGuid().ToString("D")
            : request.Id.Trim();

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            if (!Exists(connection, transaction, "menu_modifier_groups", request.GroupId.Trim()))
            {
                return FailAndRollback<MenuModifierOptionConfig>(
                    transaction,
                    "Menu.ModifierGroupNotFound",
                    "Modifier group does not exist.");
            }

            string now = DateTimeOffset.UtcNow.ToString("O");
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO menu_modifier_options (
                    id,
                    group_id,
                    name,
                    price_delta_cents,
                    is_active,
                    display_order,
                    created_at_utc,
                    updated_at_utc)
                VALUES (
                    $id,
                    $groupId,
                    $name,
                    $priceDeltaCents,
                    $isActive,
                    $displayOrder,
                    $now,
                    NULL)
                ON CONFLICT(id) DO UPDATE SET
                    group_id = excluded.group_id,
                    name = excluded.name,
                    price_delta_cents = excluded.price_delta_cents,
                    is_active = excluded.is_active,
                    display_order = excluded.display_order,
                    updated_at_utc = $now;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$groupId", request.GroupId.Trim());
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$priceDeltaCents", request.PriceDeltaCents);
            command.Parameters.AddWithValue("$isActive", request.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$displayOrder", Math.Max(0, request.DisplayOrder));
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
            transaction.Commit();

            return Result<MenuModifierOptionConfig>.Success(new MenuModifierOptionConfig(
                id,
                request.GroupId.Trim(),
                name,
                request.PriceDeltaCents,
                request.IsActive,
                Math.Max(0, request.DisplayOrder)));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<MenuModifierOptionConfig>.Fail(Error.Conflict(
                "Menu.DuplicateModifierOption",
                "Modifier option name already exists in this group."));
        }
        catch (SqliteException ex)
        {
            return Result<MenuModifierOptionConfig>.Fail(Error.Unexpected(
                "Menu.SaveModifierOptionFailed",
                ex.Message));
        }
    }

    public Result SetItemModifierGroups(MenuModifierLinkMutation request)
    {
        if (!Guid.TryParse(request.OwnerId, out Guid productId) || productId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Menu.InvalidItemId",
                "Menu item ID is invalid."));
        }

        return ReplaceLinks(
            "menu_item_modifier_groups",
            "product_id",
            productId.ToString("D"),
            request.GroupIds,
            "Menu.SetItemModifierGroupsFailed");
    }

    public Result SetVariantModifierGroups(MenuModifierLinkMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerId))
        {
            return Result.Fail(Error.Validation(
                "Menu.InvalidVariantId",
                "Variant ID is required."));
        }

        return ReplaceLinks(
            "menu_variant_modifier_groups",
            "variant_id",
            request.OwnerId.Trim(),
            request.GroupIds,
            "Menu.SetVariantModifierGroupsFailed");
    }

    public Result<MenuPublicationSnapshot> PublishCurrent()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid storeId = ReadStoreId(connection, transaction);
            PublicationMaterial material = ReadPublicationMaterial(connection, transaction);
            string contentHash = ComputeContentHash(storeId, material);

            MenuPublicationSummary? current = ReadCurrentSummary(connection, transaction);
            if (current is not null &&
                string.Equals(current.ContentHash, contentHash, StringComparison.Ordinal))
            {
                MenuPublicationSnapshot existing = ReadPublication(connection, transaction, current.RevisionId);
                transaction.Commit();
                return Result<MenuPublicationSnapshot>.Success(existing);
            }

            int revisionNumber = ReadNextRevisionNumber(connection, transaction, storeId);
            string revisionId = storeId.ToString("D") + ":" +
                revisionNumber.ToString(CultureInfo.InvariantCulture);
            string publicationHash = ComputeHash(
                storeId.ToString("D") + "|" +
                revisionId + "|" +
                revisionNumber.ToString(CultureInfo.InvariantCulture) + "|" +
                contentHash);
            DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

            if (current is not null)
            {
                using SqliteCommand supersede = connection.CreateCommand();
                supersede.Transaction = transaction;
                supersede.CommandText = """
                    UPDATE menu_revisions
                    SET status = 'Superseded',
                        superseded_at_utc = $now
                    WHERE revision_id = $revisionId;
                    """;
                supersede.Parameters.AddWithValue("$now", publishedAt.ToString("O"));
                supersede.Parameters.AddWithValue("$revisionId", current.RevisionId);
                supersede.ExecuteNonQuery();
            }

            InsertRevision(
                connection,
                transaction,
                revisionId,
                storeId,
                revisionNumber,
                contentHash,
                publicationHash,
                publishedAt);

            InsertSnapshotRows(connection, transaction, revisionId, material);

            UpsertCurrentPublication(
                connection,
                transaction,
                storeId,
                revisionId,
                contentHash,
                publicationHash,
                publishedAt);

            transaction.Commit();

            return Result<MenuPublicationSnapshot>.Success(new MenuPublicationSnapshot(
                revisionId,
                storeId,
                revisionNumber,
                contentHash,
                publicationHash,
                publishedAt,
                material.Categories,
                material.Items,
                material.Variants,
                material.ModifierGroups,
                material.ModifierOptions,
                material.ItemModifierGroups,
                material.VariantModifierGroups));
        }
        catch (InvalidOperationException ex)
        {
            return Result<MenuPublicationSnapshot>.Fail(Error.Validation(
                "Menu.PublishInvalidState",
                ex.Message));
        }
        catch (SqliteException ex)
        {
            return Result<MenuPublicationSnapshot>.Fail(Error.Unexpected(
                "Menu.PublishFailed",
                ex.Message));
        }
    }

    public Result<MenuPublicationSnapshot?> GetCurrentPublication()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            MenuPublicationSummary? summary = ReadCurrentSummary(connection, null);
            if (summary is null)
            {
                return Result<MenuPublicationSnapshot?>.Success(null);
            }

            return Result<MenuPublicationSnapshot?>.Success(
                ReadPublication(connection, null, summary.RevisionId));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<MenuPublicationSnapshot?>.Fail(Error.Unexpected(
                "Menu.ReadCurrentPublicationFailed",
                ex.Message));
        }
    }

    public Result<MenuPublicationApplyResult> ApplyPublication(MenuPublicationSnapshot snapshot)
    {
        if (snapshot.StoreId == Guid.Empty ||
            snapshot.RevisionNumber <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.RevisionId))
        {
            return Result<MenuPublicationApplyResult>.Fail(Error.Validation(
                "Menu.InvalidPublication",
                "Publication identity is invalid."));
        }

        string expectedRevisionId = snapshot.StoreId.ToString("D") + ":" +
            snapshot.RevisionNumber.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(expectedRevisionId, snapshot.RevisionId, StringComparison.Ordinal))
        {
            return Result<MenuPublicationApplyResult>.Fail(Error.Conflict(
                "Menu.PublicationIdentityConflict",
                "Publication revision identity does not match store/revision authority."));
        }

        PublicationMaterial material = new(
            snapshot.Categories,
            snapshot.Items,
            snapshot.Variants,
            snapshot.ModifierGroups,
            snapshot.ModifierOptions,
            snapshot.ItemModifierGroups,
            snapshot.VariantModifierGroups);

        string computedContentHash = ComputeContentHash(snapshot.StoreId, material);
        string computedPublicationHash = ComputeHash(
            snapshot.StoreId.ToString("D") + "|" +
            snapshot.RevisionId + "|" +
            snapshot.RevisionNumber.ToString(CultureInfo.InvariantCulture) + "|" +
            computedContentHash);

        if (!string.Equals(computedContentHash, snapshot.ContentHash, StringComparison.Ordinal) ||
            !string.Equals(computedPublicationHash, snapshot.PublicationHash, StringComparison.Ordinal))
        {
            return Result<MenuPublicationApplyResult>.Fail(Error.Conflict(
                "Menu.PublicationHashConflict",
                "Publication content/hash failed deterministic verification."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Guid localStoreId = ReadStoreId(connection, transaction);
            if (localStoreId != snapshot.StoreId)
            {
                transaction.Rollback();
                return Result<MenuPublicationApplyResult>.Fail(Error.Conflict(
                    "Menu.ForeignStorePublication",
                    "Menu publication StoreId does not match this client's bound store authority."));
            }

            MenuPublicationSummary? existing = ReadRevisionSummary(
                connection,
                transaction,
                snapshot.RevisionId);

            if (existing is not null)
            {
                if (!string.Equals(existing.ContentHash, snapshot.ContentHash, StringComparison.Ordinal) ||
                    !string.Equals(existing.PublicationHash, snapshot.PublicationHash, StringComparison.Ordinal))
                {
                    transaction.Rollback();
                    return Result<MenuPublicationApplyResult>.Fail(Error.Conflict(
                        "Menu.RevisionContentConflict",
                        "The same revision identity already exists with different content."));
                }
            }

            MenuPublicationSummary? current = ReadCurrentSummary(connection, transaction);
            if (current is not null)
            {
                if (current.RevisionNumber > snapshot.RevisionNumber)
                {
                    transaction.Rollback();
                    return Result<MenuPublicationApplyResult>.Fail(Error.Conflict(
                        "Menu.StalePublication",
                        "A stale menu publication cannot replace newer authority."));
                }

                if (current.RevisionNumber == snapshot.RevisionNumber)
                {
                    if (!string.Equals(current.RevisionId, snapshot.RevisionId, StringComparison.Ordinal) ||
                        !string.Equals(current.PublicationHash, snapshot.PublicationHash, StringComparison.Ordinal))
                    {
                        transaction.Rollback();
                        return Result<MenuPublicationApplyResult>.Fail(Error.Conflict(
                            "Menu.PublicationRevisionConflict",
                            "Current publication has the same revision number with different authority."));
                    }

                    transaction.Commit();
                    return Result<MenuPublicationApplyResult>.Success(
                        new MenuPublicationApplyResult(
                            Applied: false,
                            Idempotent: true,
                            current));
                }
            }

            if (existing is null)
            {
                InsertRevision(
                    connection,
                    transaction,
                    snapshot.RevisionId,
                    snapshot.StoreId,
                    snapshot.RevisionNumber,
                    snapshot.ContentHash,
                    snapshot.PublicationHash,
                    snapshot.PublishedAtUtc);
                InsertSnapshotRows(connection, transaction, snapshot.RevisionId, material);
            }

            if (current is not null)
            {
                using SqliteCommand supersede = connection.CreateCommand();
                supersede.Transaction = transaction;
                supersede.CommandText = """
                    UPDATE menu_revisions
                    SET status = 'Superseded',
                        superseded_at_utc = $now
                    WHERE revision_id = $revisionId;
                    """;
                supersede.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                supersede.Parameters.AddWithValue("$revisionId", current.RevisionId);
                supersede.ExecuteNonQuery();
            }

            UpsertCurrentPublication(
                connection,
                transaction,
                snapshot.StoreId,
                snapshot.RevisionId,
                snapshot.ContentHash,
                snapshot.PublicationHash,
                DateTimeOffset.UtcNow);

            transaction.Commit();

            MenuPublicationSummary applied = new(
                snapshot.RevisionId,
                snapshot.StoreId,
                snapshot.RevisionNumber,
                snapshot.ContentHash,
                snapshot.PublicationHash,
                "Published",
                snapshot.PublishedAtUtc);

            return Result<MenuPublicationApplyResult>.Success(new MenuPublicationApplyResult(
                Applied: true,
                Idempotent: false,
                applied));
        }
        catch (SqliteException ex)
        {
            return Result<MenuPublicationApplyResult>.Fail(Error.Unexpected(
                "Menu.ApplyPublicationFailed",
                ex.Message));
        }
    }

    private Result ReplaceLinks(
        string table,
        string ownerColumn,
        string ownerId,
        IReadOnlyList<string> groupIds,
        string errorCode)
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM " + table + " WHERE " + ownerColumn + " = $ownerId;";
                delete.Parameters.AddWithValue("$ownerId", ownerId);
                delete.ExecuteNonQuery();
            }

            int order = 0;
            foreach (string groupId in groupIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal))
            {
                if (!Exists(connection, transaction, "menu_modifier_groups", groupId))
                {
                    transaction.Rollback();
                    return Result.Fail(Error.Validation(
                        "Menu.ModifierGroupNotFound",
                        "Modifier group does not exist: " + groupId));
                }

                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO " + table +
                    " (" + ownerColumn + ", group_id, display_order) " +
                    "VALUES ($ownerId, $groupId, $displayOrder);";
                insert.Parameters.AddWithValue("$ownerId", ownerId);
                insert.Parameters.AddWithValue("$groupId", groupId);
                insert.Parameters.AddWithValue("$displayOrder", order++);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
            return Result.Success();
        }
        catch (SqliteException ex)
        {
            return Result.Fail(Error.Unexpected(errorCode, ex.Message));
        }
    }

    private static Result ValidateItem(MenuItemMutation request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Fail(Error.Validation("Menu.ItemNameRequired", "Menu item name is required."));
        }

        if (request.UnitId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("Menu.UnitRequired", "Menu item unit is required."));
        }

        if (request.SellingPriceCents < 0 ||
            (request.CostPriceCents.HasValue && request.CostPriceCents.Value < 0))
        {
            return Result.Fail(Error.Validation(
                "Menu.InvalidPrice",
                "Menu item prices cannot be negative."));
        }

        return Result.Success();
    }

    private static Result ValidateVariant(MenuVariantMutation request)
    {
        if (request.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Fail(Error.Validation(
                "Menu.InvalidVariant",
                "Variant parent item and name are required."));
        }

        if (request.SellingPriceCents < 0 ||
            (request.CostPriceCents.HasValue && request.CostPriceCents.Value < 0))
        {
            return Result.Fail(Error.Validation(
                "Menu.InvalidVariantPrice",
                "Variant prices cannot be negative."));
        }

        return Result.Success();
    }

    private static Result EnsureVariantIdentifiersAvailable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        Guid productId,
        string? sku,
        string? barcode,
        bool isActive)
    {
        if (!isActive)
        {
            return Result.Success();
        }

        Result skuResult = EnsureVariantIdentifierAvailable(
            connection,
            transaction,
            id,
            productId,
            "sku",
            NullIfBlank(sku),
            "Menu.DuplicateVariantSku",
            "SKU is already assigned to another active variant.");
        if (skuResult.IsFailure)
        {
            return skuResult;
        }

        return EnsureVariantIdentifierAvailable(
            connection,
            transaction,
            id,
            productId,
            "barcode",
            NullIfBlank(barcode),
            "Menu.DuplicateVariantBarcode",
            "Barcode is already assigned to another active variant.");
    }

    private static Result EnsureVariantIdentifierAvailable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        Guid productId,
        string column,
        string? value,
        string code,
        string message)
    {
        if (value is null)
        {
            return Result.Success();
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM menu_variants " +
            "WHERE " + column + " = $value AND is_active = 1 " +
            "AND id <> $id LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$id", id);

        return command.ExecuteScalar() is null
            ? Result.Success()
            : Result.Fail(Error.Conflict(code, message));
    }

    private static MenuCatalogDraft ReadDraft(SqliteConnection connection)
    {
        List<MenuCategoryConfig> categories = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name, is_active FROM categories ORDER BY name;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new MenuCategoryConfig(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetInt32(2) == 1));
            }
        }

        List<MenuItemConfig> items = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    p.id,
                    p.category_id,
                    p.name,
                    p.description,
                    p.unit_id,
                    p.sku,
                    p.barcode,
                    p.selling_price_cents,
                    p.cost_price_cents,
                    p.is_active,
                    COALESCE(a.is_temporarily_unavailable, 0)
                FROM products p
                LEFT JOIN menu_item_availability a ON a.product_id = p.id
                ORDER BY p.name, p.id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new MenuItemConfig(
                    Guid.Parse(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Guid.Parse(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.GetInt32(9) == 1,
                    reader.GetInt32(10) == 1));
            }
        }

        List<MenuVariantConfig> variants = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    id,
                    product_id,
                    name,
                    sku,
                    barcode,
                    selling_price_cents,
                    cost_price_cents,
                    is_active,
                    is_temporarily_unavailable,
                    display_order
                FROM menu_variants
                ORDER BY product_id, display_order, name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                variants.Add(new MenuVariantConfig(
                    reader.GetString(0),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.GetInt32(7) == 1,
                    reader.GetInt32(8) == 1,
                    reader.GetInt32(9)));
            }
        }

        List<MenuModifierGroupConfig> groups = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, name, is_required, min_selections, max_selections, is_active, display_order
                FROM menu_modifier_groups
                ORDER BY display_order, name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                groups.Add(new MenuModifierGroupConfig(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) == 1,
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5) == 1,
                    reader.GetInt32(6)));
            }
        }

        List<MenuModifierOptionConfig> options = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, group_id, name, price_delta_cents, is_active, display_order
                FROM menu_modifier_options
                ORDER BY group_id, display_order, name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                options.Add(new MenuModifierOptionConfig(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4) == 1,
                    reader.GetInt32(5)));
            }
        }

        List<MenuModifierLinkConfig> itemLinks = ReadLinks(
            connection,
            "menu_item_modifier_groups",
            "product_id");
        List<MenuModifierLinkConfig> variantLinks = ReadLinks(
            connection,
            "menu_variant_modifier_groups",
            "variant_id");

        return new MenuCatalogDraft(
            categories,
            items,
            variants,
            groups,
            options,
            itemLinks,
            variantLinks,
            ReadCurrentSummary(connection, null));
    }

    private static List<MenuModifierLinkConfig> ReadLinks(
        SqliteConnection connection,
        string table,
        string ownerColumn)
    {
        List<MenuModifierLinkConfig> links = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT " + ownerColumn + ", group_id, display_order FROM " + table +
            " ORDER BY " + ownerColumn + ", display_order, group_id;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            links.Add(new MenuModifierLinkConfig(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2)));
        }

        return links;
    }

    private static PublicationMaterial ReadPublicationMaterial(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        List<MenuPublicationCategorySnapshot> categories = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, name
                FROM categories
                WHERE is_active = 1
                ORDER BY name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            int order = 0;
            while (reader.Read())
            {
                categories.Add(new MenuPublicationCategorySnapshot(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    order++));
            }
        }

        List<MenuPublicationItemSnapshot> items = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    p.id,
                    p.category_id,
                    p.name,
                    p.description,
                    p.unit_id,
                    u.name,
                    p.sku,
                    p.barcode,
                    p.selling_price_cents,
                    p.cost_price_cents,
                    COALESCE(a.is_temporarily_unavailable, 0)
                FROM products p
                INNER JOIN units u ON u.id = p.unit_id
                LEFT JOIN menu_item_availability a ON a.product_id = p.id
                WHERE p.is_active = 1
                ORDER BY p.name, p.id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            int order = 0;
            while (reader.Read())
            {
                items.Add(new MenuPublicationItemSnapshot(
                    Guid.Parse(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Guid.Parse(reader.GetString(4)),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    reader.GetInt32(10) == 1,
                    order++));
            }
        }

        HashSet<Guid> activeItemIds = items.Select(x => x.ProductId).ToHashSet();
        HashSet<Guid> activeCategoryIds = categories.Select(x => x.CategoryId).ToHashSet();

        MenuPublicationItemSnapshot? itemWithInactiveCategory = items.FirstOrDefault(
            item => item.CategoryId.HasValue && !activeCategoryIds.Contains(item.CategoryId.Value));
        if (itemWithInactiveCategory is not null)
        {
            throw new InvalidOperationException(
                "Active menu item references an inactive/unpublished category: " +
                itemWithInactiveCategory.Name);
        }

        List<MenuPublicationVariantSnapshot> variants = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    id,
                    product_id,
                    name,
                    sku,
                    barcode,
                    selling_price_cents,
                    cost_price_cents,
                    is_temporarily_unavailable,
                    display_order
                FROM menu_variants
                WHERE is_active = 1
                ORDER BY product_id, display_order, name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Guid productId = Guid.Parse(reader.GetString(1));
                if (!activeItemIds.Contains(productId))
                {
                    continue;
                }

                variants.Add(new MenuPublicationVariantSnapshot(
                    reader.GetString(0),
                    productId,
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.GetInt32(7) == 1,
                    reader.GetInt32(8)));
            }
        }

        if (items.Count > 0)
        {
            HashSet<Guid> itemIdsWithVariant = variants.Select(x => x.ProductId).ToHashSet();
            Guid? missing = activeItemIds.FirstOrDefault(id => !itemIdsWithVariant.Contains(id));
            if (missing.HasValue && missing.Value != Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Every active menu item must have at least one active first-class variant before publication.");
            }
        }

        List<MenuPublicationModifierGroupSnapshot> groups = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, name, is_required, min_selections, max_selections, display_order
                FROM menu_modifier_groups
                WHERE is_active = 1
                ORDER BY display_order, name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                groups.Add(new MenuPublicationModifierGroupSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) == 1,
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
            }
        }

        HashSet<string> activeGroupIds = groups.Select(x => x.GroupId).ToHashSet(StringComparer.Ordinal);

        List<MenuPublicationModifierOptionSnapshot> options = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, group_id, name, price_delta_cents, display_order
                FROM menu_modifier_options
                WHERE is_active = 1
                ORDER BY group_id, display_order, name, id;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string groupId = reader.GetString(1);
                if (!activeGroupIds.Contains(groupId))
                {
                    continue;
                }

                options.Add(new MenuPublicationModifierOptionSnapshot(
                    reader.GetString(0),
                    groupId,
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4)));
            }
        }

        foreach (MenuPublicationModifierGroupSnapshot group in groups)
        {
            int optionCount = options.Count(x => string.Equals(x.GroupId, group.GroupId, StringComparison.Ordinal));
            if (group.MinSelections > optionCount || group.MaxSelections > optionCount)
            {
                throw new InvalidOperationException(
                    "Modifier group selection limits exceed the number of active options: " + group.Name);
            }
        }

        HashSet<string> activeVariantIds = variants.Select(x => x.VariantId).ToHashSet(StringComparer.Ordinal);

        List<MenuPublicationLinkSnapshot> itemLinks = ReadPublicationLinks(
            connection,
            transaction,
            "menu_item_modifier_groups",
            "product_id",
            owner => Guid.TryParse(owner, out Guid id) && activeItemIds.Contains(id),
            activeGroupIds);

        List<MenuPublicationLinkSnapshot> variantLinks = ReadPublicationLinks(
            connection,
            transaction,
            "menu_variant_modifier_groups",
            "variant_id",
            owner => activeVariantIds.Contains(owner),
            activeGroupIds);

        return new PublicationMaterial(
            categories,
            items,
            variants,
            groups,
            options,
            itemLinks,
            variantLinks);
    }

    private static List<MenuPublicationLinkSnapshot> ReadPublicationLinks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        Func<string, bool> includeOwner,
        HashSet<string> activeGroupIds)
    {
        List<MenuPublicationLinkSnapshot> links = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT " + ownerColumn + ", group_id, display_order FROM " + table +
            " ORDER BY " + ownerColumn + ", display_order, group_id;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string ownerId = reader.GetString(0);
            string groupId = reader.GetString(1);
            if (includeOwner(ownerId) && activeGroupIds.Contains(groupId))
            {
                links.Add(new MenuPublicationLinkSnapshot(
                    ownerId,
                    groupId,
                    reader.GetInt32(2)));
            }
        }

        return links;
    }

    private static string ComputeContentHash(Guid storeId, PublicationMaterial material)
    {
        StringBuilder builder = new();
        Append(builder, "store", storeId.ToString("D"));

        foreach (MenuPublicationCategorySnapshot x in material.Categories.OrderBy(x => x.DisplayOrder).ThenBy(x => x.CategoryId))
            Append(builder, "category", x.CategoryId.ToString("D"), x.Name, x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        foreach (MenuPublicationItemSnapshot x in material.Items.OrderBy(x => x.DisplayOrder).ThenBy(x => x.ProductId))
            Append(builder, "item", x.ProductId.ToString("D"), x.CategoryId?.ToString("D"), x.Name, x.Description, x.UnitId.ToString("D"), x.UnitName, x.Sku, x.Barcode, x.SellingPriceCents.ToString(CultureInfo.InvariantCulture), x.CostPriceCents?.ToString(CultureInfo.InvariantCulture), x.IsTemporarilyUnavailable ? "1" : "0", x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        foreach (MenuPublicationVariantSnapshot x in material.Variants.OrderBy(x => x.ProductId).ThenBy(x => x.DisplayOrder).ThenBy(x => x.VariantId, StringComparer.Ordinal))
            Append(builder, "variant", x.VariantId, x.ProductId.ToString("D"), x.Name, x.Sku, x.Barcode, x.SellingPriceCents.ToString(CultureInfo.InvariantCulture), x.CostPriceCents?.ToString(CultureInfo.InvariantCulture), x.IsTemporarilyUnavailable ? "1" : "0", x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        foreach (MenuPublicationModifierGroupSnapshot x in material.ModifierGroups.OrderBy(x => x.DisplayOrder).ThenBy(x => x.GroupId, StringComparer.Ordinal))
            Append(builder, "group", x.GroupId, x.Name, x.IsRequired ? "1" : "0", x.MinSelections.ToString(CultureInfo.InvariantCulture), x.MaxSelections.ToString(CultureInfo.InvariantCulture), x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        foreach (MenuPublicationModifierOptionSnapshot x in material.ModifierOptions.OrderBy(x => x.GroupId, StringComparer.Ordinal).ThenBy(x => x.DisplayOrder).ThenBy(x => x.OptionId, StringComparer.Ordinal))
            Append(builder, "option", x.OptionId, x.GroupId, x.Name, x.PriceDeltaCents.ToString(CultureInfo.InvariantCulture), x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        foreach (MenuPublicationLinkSnapshot x in material.ItemModifierGroups.OrderBy(x => x.OwnerId, StringComparer.Ordinal).ThenBy(x => x.DisplayOrder).ThenBy(x => x.GroupId, StringComparer.Ordinal))
            Append(builder, "item-group", x.OwnerId, x.GroupId, x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        foreach (MenuPublicationLinkSnapshot x in material.VariantModifierGroups.OrderBy(x => x.OwnerId, StringComparer.Ordinal).ThenBy(x => x.DisplayOrder).ThenBy(x => x.GroupId, StringComparer.Ordinal))
            Append(builder, "variant-group", x.OwnerId, x.GroupId, x.DisplayOrder.ToString(CultureInfo.InvariantCulture));

        return ComputeHash(builder.ToString());
    }

    private static void Append(StringBuilder builder, params string?[] values)
    {
        foreach (string? value in values)
        {
            string normalized = value ?? string.Empty;
            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized)));
            builder.Append('|');
        }

        builder.Append('\n');
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static Guid ReadStoreId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id
            FROM deployment_identity
            ORDER BY created_at
            LIMIT 1;
            """;
        object? value = command.ExecuteScalar();
        if (value is not string text || !Guid.TryParse(text, out Guid storeId) || storeId == Guid.Empty)
        {
            throw new InvalidOperationException("Deployment StoreId is required before publishing a menu.");
        }

        return storeId;
    }

    private static int ReadNextRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(revision_number), 0) + 1
            FROM menu_revisions
            WHERE store_id = $storeId;
            """;
        command.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar() ?? 1);
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
            INSERT INTO menu_revisions (
                revision_id,
                store_id,
                revision_number,
                content_hash,
                publication_hash,
                status,
                created_at_utc,
                published_at_utc,
                superseded_at_utc)
            VALUES (
                $revisionId,
                $storeId,
                $revisionNumber,
                $contentHash,
                $publicationHash,
                'Published',
                $publishedAt,
                $publishedAt,
                NULL);
            """;
        command.Parameters.AddWithValue("$revisionId", revisionId);
        command.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
        command.Parameters.AddWithValue("$revisionNumber", revisionNumber);
        command.Parameters.AddWithValue("$contentHash", contentHash);
        command.Parameters.AddWithValue("$publicationHash", publicationHash);
        command.Parameters.AddWithValue("$publishedAt", publishedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void UpsertCurrentPublication(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        string revisionId,
        string contentHash,
        string publicationHash,
        DateTimeOffset appliedAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO menu_current_publication (
                store_id,
                revision_id,
                content_hash,
                publication_hash,
                applied_at_utc)
            VALUES (
                $storeId,
                $revisionId,
                $contentHash,
                $publicationHash,
                $appliedAt)
            ON CONFLICT(store_id) DO UPDATE SET
                revision_id = excluded.revision_id,
                content_hash = excluded.content_hash,
                publication_hash = excluded.publication_hash,
                applied_at_utc = excluded.applied_at_utc;
            """;
        command.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
        command.Parameters.AddWithValue("$revisionId", revisionId);
        command.Parameters.AddWithValue("$contentHash", contentHash);
        command.Parameters.AddWithValue("$publicationHash", publicationHash);
        command.Parameters.AddWithValue("$appliedAt", appliedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSnapshotRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        PublicationMaterial material)
    {
        foreach (MenuPublicationCategorySnapshot x in material.Categories)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_categories (
                    revision_id, category_id, name, display_order)
                VALUES ($revisionId, $id, $name, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$id", x.CategoryId.ToString("D")),
                ("$name", x.Name),
                ("$displayOrder", x.DisplayOrder));
        }

        foreach (MenuPublicationItemSnapshot x in material.Items)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_items (
                    revision_id, product_id, category_id, name, description, unit_id, unit_name,
                    sku, barcode, selling_price_cents, cost_price_cents,
                    is_temporarily_unavailable, display_order)
                VALUES (
                    $revisionId, $productId, $categoryId, $name, $description, $unitId, $unitName,
                    $sku, $barcode, $sellingPriceCents, $costPriceCents,
                    $unavailable, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$productId", x.ProductId.ToString("D")),
                ("$categoryId", x.CategoryId?.ToString("D")),
                ("$name", x.Name),
                ("$description", x.Description),
                ("$unitId", x.UnitId.ToString("D")),
                ("$unitName", x.UnitName),
                ("$sku", x.Sku),
                ("$barcode", x.Barcode),
                ("$sellingPriceCents", x.SellingPriceCents),
                ("$costPriceCents", x.CostPriceCents),
                ("$unavailable", x.IsTemporarilyUnavailable ? 1 : 0),
                ("$displayOrder", x.DisplayOrder));
        }

        foreach (MenuPublicationVariantSnapshot x in material.Variants)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_variants (
                    revision_id, variant_id, product_id, name, sku, barcode,
                    selling_price_cents, cost_price_cents, is_temporarily_unavailable, display_order)
                VALUES (
                    $revisionId, $variantId, $productId, $name, $sku, $barcode,
                    $sellingPriceCents, $costPriceCents, $unavailable, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$variantId", x.VariantId),
                ("$productId", x.ProductId.ToString("D")),
                ("$name", x.Name),
                ("$sku", x.Sku),
                ("$barcode", x.Barcode),
                ("$sellingPriceCents", x.SellingPriceCents),
                ("$costPriceCents", x.CostPriceCents),
                ("$unavailable", x.IsTemporarilyUnavailable ? 1 : 0),
                ("$displayOrder", x.DisplayOrder));
        }

        foreach (MenuPublicationModifierGroupSnapshot x in material.ModifierGroups)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_modifier_groups (
                    revision_id, group_id, name, is_required, min_selections, max_selections, display_order)
                VALUES (
                    $revisionId, $groupId, $name, $isRequired, $minSelections, $maxSelections, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$groupId", x.GroupId),
                ("$name", x.Name),
                ("$isRequired", x.IsRequired ? 1 : 0),
                ("$minSelections", x.MinSelections),
                ("$maxSelections", x.MaxSelections),
                ("$displayOrder", x.DisplayOrder));
        }

        foreach (MenuPublicationModifierOptionSnapshot x in material.ModifierOptions)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_modifier_options (
                    revision_id, option_id, group_id, name, price_delta_cents, display_order)
                VALUES (
                    $revisionId, $optionId, $groupId, $name, $priceDeltaCents, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$optionId", x.OptionId),
                ("$groupId", x.GroupId),
                ("$name", x.Name),
                ("$priceDeltaCents", x.PriceDeltaCents),
                ("$displayOrder", x.DisplayOrder));
        }

        foreach (MenuPublicationLinkSnapshot x in material.ItemModifierGroups)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_item_modifier_groups (
                    revision_id, product_id, group_id, display_order)
                VALUES ($revisionId, $ownerId, $groupId, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$ownerId", x.OwnerId),
                ("$groupId", x.GroupId),
                ("$displayOrder", x.DisplayOrder));
        }

        foreach (MenuPublicationLinkSnapshot x in material.VariantModifierGroups)
        {
            ExecuteSnapshotInsert(connection, transaction, """
                INSERT INTO menu_revision_variant_modifier_groups (
                    revision_id, variant_id, group_id, display_order)
                VALUES ($revisionId, $ownerId, $groupId, $displayOrder);
                """,
                ("$revisionId", revisionId),
                ("$ownerId", x.OwnerId),
                ("$groupId", x.GroupId),
                ("$displayOrder", x.DisplayOrder));
        }
    }

    private static void ExecuteSnapshotInsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static MenuPublicationSummary? ReadCurrentSummary(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                r.revision_id,
                r.store_id,
                r.revision_number,
                r.content_hash,
                r.publication_hash,
                r.status,
                r.published_at_utc
            FROM menu_current_publication c
            INNER JOIN menu_revisions r ON r.revision_id = c.revision_id
            LIMIT 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapRevisionSummary(reader) : null;
    }

    private static MenuPublicationSummary? ReadRevisionSummary(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                revision_id,
                store_id,
                revision_number,
                content_hash,
                publication_hash,
                status,
                published_at_utc
            FROM menu_revisions
            WHERE revision_id = $revisionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revisionId", revisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapRevisionSummary(reader) : null;
    }

    private static MenuPublicationSummary MapRevisionSummary(SqliteDataReader reader)
    {
        return new MenuPublicationSummary(
            reader.GetString(0),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    private static MenuPublicationSnapshot ReadPublication(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string revisionId)
    {
        MenuPublicationSummary summary = ReadRevisionSummaryRequired(
            connection,
            transaction,
            revisionId);

        List<MenuPublicationCategorySnapshot> categories = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT category_id, name, display_order
                FROM menu_revision_categories
                WHERE revision_id = $revisionId
                ORDER BY display_order, category_id;
                """;
            command.Parameters.AddWithValue("$revisionId", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                categories.Add(new MenuPublicationCategorySnapshot(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2)));
        }

        List<MenuPublicationItemSnapshot> items = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT product_id, category_id, name, description, unit_id, unit_name, sku, barcode,
                       selling_price_cents, cost_price_cents, is_temporarily_unavailable, display_order
                FROM menu_revision_items
                WHERE revision_id = $revisionId
                ORDER BY display_order, product_id;
                """;
            command.Parameters.AddWithValue("$revisionId", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(new MenuPublicationItemSnapshot(
                    Guid.Parse(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Guid.Parse(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    reader.GetInt32(10) == 1,
                    reader.GetInt32(11)));
        }

        List<MenuPublicationVariantSnapshot> variants = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT variant_id, product_id, name, sku, barcode, selling_price_cents,
                       cost_price_cents, is_temporarily_unavailable, display_order
                FROM menu_revision_variants
                WHERE revision_id = $revisionId
                ORDER BY product_id, display_order, variant_id;
                """;
            command.Parameters.AddWithValue("$revisionId", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                variants.Add(new MenuPublicationVariantSnapshot(
                    reader.GetString(0),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.GetInt32(7) == 1,
                    reader.GetInt32(8)));
        }

        List<MenuPublicationModifierGroupSnapshot> groups = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT group_id, name, is_required, min_selections, max_selections, display_order
                FROM menu_revision_modifier_groups
                WHERE revision_id = $revisionId
                ORDER BY display_order, group_id;
                """;
            command.Parameters.AddWithValue("$revisionId", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                groups.Add(new MenuPublicationModifierGroupSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) == 1,
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
        }

        List<MenuPublicationModifierOptionSnapshot> options = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT option_id, group_id, name, price_delta_cents, display_order
                FROM menu_revision_modifier_options
                WHERE revision_id = $revisionId
                ORDER BY group_id, display_order, option_id;
                """;
            command.Parameters.AddWithValue("$revisionId", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                options.Add(new MenuPublicationModifierOptionSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4)));
        }

        List<MenuPublicationLinkSnapshot> itemLinks = ReadSnapshotLinks(
            connection, transaction, "menu_revision_item_modifier_groups", "product_id", revisionId);
        List<MenuPublicationLinkSnapshot> variantLinks = ReadSnapshotLinks(
            connection, transaction, "menu_revision_variant_modifier_groups", "variant_id", revisionId);

        return new MenuPublicationSnapshot(
            summary.RevisionId,
            summary.StoreId,
            summary.RevisionNumber,
            summary.ContentHash,
            summary.PublicationHash,
            summary.PublishedAtUtc,
            categories,
            items,
            variants,
            groups,
            options,
            itemLinks,
            variantLinks);
    }

    private static MenuPublicationSummary ReadRevisionSummaryRequired(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_id, store_id, revision_number, content_hash, publication_hash, status, published_at_utc
            FROM menu_revisions
            WHERE revision_id = $revisionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revisionId", revisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Published menu revision was not found.");
        }

        return MapRevisionSummary(reader);
    }

    private static List<MenuPublicationLinkSnapshot> ReadSnapshotLinks(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        string ownerColumn,
        string revisionId)
    {
        List<MenuPublicationLinkSnapshot> links = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT " + ownerColumn + ", group_id, display_order FROM " + table +
            " WHERE revision_id = $revisionId ORDER BY " + ownerColumn + ", display_order, group_id;";
        command.Parameters.AddWithValue("$revisionId", revisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            links.Add(new MenuPublicationLinkSnapshot(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return links;
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM " + table + " WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is not null;
    }

    private static Result<T> FailAndRollback<T>(
        SqliteTransaction transaction,
        string code,
        string message)
    {
        transaction.Rollback();
        return Result<T>.Fail(Error.Validation(code, message));
    }

    private static object DbText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(SqliteException ex) =>
        ex.SqliteExtendedErrorCode is 1555 or 2067 ||
        ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsForeignKeyViolation(SqliteException ex) =>
        ex.SqliteExtendedErrorCode == 787 ||
        ex.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);

    private sealed record PublicationMaterial(
        IReadOnlyList<MenuPublicationCategorySnapshot> Categories,
        IReadOnlyList<MenuPublicationItemSnapshot> Items,
        IReadOnlyList<MenuPublicationVariantSnapshot> Variants,
        IReadOnlyList<MenuPublicationModifierGroupSnapshot> ModifierGroups,
        IReadOnlyList<MenuPublicationModifierOptionSnapshot> ModifierOptions,
        IReadOnlyList<MenuPublicationLinkSnapshot> ItemModifierGroups,
        IReadOnlyList<MenuPublicationLinkSnapshot> VariantModifierGroups);
}

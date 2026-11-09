using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories.Products;

public sealed class SqliteProductWriteRepository :
    SqliteRepositoryBase,
    IProductWriteRepository
{
    public SqliteProductWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> AddAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateProduct(product);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = """
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
                    updated_at
                )
                VALUES (
                    $id,
                    $sku,
                    $barcode,
                    $name,
                    $description,
                    $categoryId,
                    $brandId,
                    $unitId,
                    $supplierId,
                    $costPriceCents,
                    $sellingPriceCents,
                    $wholesalePriceCents,
                    $stockOnHandX1000,
                    $reorderLevelX1000,
                    $trackStock,
                    $allowDiscount,
                    $taxClassification,
                    $isActive,
                    $notes,
                    $createdAt,
                    $updatedAt
                );
                """;

            AddProductParameters(command, product, product.CreatedAt, product.UpdatedAt);

            command.ExecuteNonQuery();

            // P1-R3-01: Initialize location bucket atomically inside the same transaction
            // so new products immediately have an initialized Available bucket in MAIN-STORE.
            using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'product_stock_buckets' LIMIT 1;";
            if (checkCmd.ExecuteScalar() is not null and not DBNull && product.TrackStock)
            {
                using var locCmd = connection.CreateCommand();
                locCmd.Transaction = transaction;
                locCmd.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
                object? locIdObj = locCmd.ExecuteScalar();
                if (locIdObj is not null and not DBNull)
                {
                    using var bucketCmd = connection.CreateCommand();
                    bucketCmd.Transaction = transaction;
                    bucketCmd.CommandText = """
                        INSERT OR IGNORE INTO product_stock_buckets (
                            id, product_id, location_id, bucket_status, quantity_x1000, updated_at
                        ) VALUES (
                            $id, $productId, $locationId, 'Available', $quantityX1000, $updatedAt
                        );
                        """;
                    bucketCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                    bucketCmd.Parameters.AddWithValue("$productId", product.Id.ToString("D"));
                    bucketCmd.Parameters.AddWithValue("$locationId", (string)locIdObj);
                    bucketCmd.Parameters.AddWithValue("$quantityX1000", product.StockOnHand.ValueX1000);
                    bucketCmd.Parameters.AddWithValue("$updatedAt", (product.UpdatedAt ?? product.CreatedAt).ToString("O"));
                    bucketCmd.ExecuteNonQuery();
                }
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "Barcode is already assigned to another active product.")));
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Products.InvalidReference",
                "Product contains an invalid category, brand, unit, or supplier reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Products.AddFailed", ex));
        }
    }

    public Task<Result> UpdateAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateProduct(product);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = """
                UPDATE products
                SET
                    sku = $sku,
                    barcode = $barcode,
                    name = $name,
                    description = $description,
                    category_id = $categoryId,
                    brand_id = $brandId,
                    unit_id = $unitId,
                    supplier_id = $supplierId,
                    cost_price_cents = $costPriceCents,
                    selling_price_cents = $sellingPriceCents,
                    wholesale_price_cents = $wholesalePriceCents,
                    stock_on_hand_x1000 = $stockOnHandX1000,
                    reorder_level_x1000 = $reorderLevelX1000,
                    track_stock = $trackStock,
                    allow_discount = $allowDiscount,
                    tax_classification = $taxClassification,
                    is_active = $isActive,
                    notes = $notes,
                    updated_at = $updatedAt
                WHERE id = $id
                  AND is_active = 1;
                """;

            AddProductParameters(
                command,
                product,
                product.CreatedAt,
                DateTimeOffset.UtcNow);

            int affectedRows = command.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.NotFound(
                    "Products.NotFoundOrInactive",
                    "Product was not found or is inactive.")));
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "Barcode is already assigned to another active product.")));
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Products.InvalidReference",
                "Product contains an invalid category, brand, unit, or supplier reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Products.UpdateFailed", ex));
        }
    }

    public Task<Result> DeactivateAsync(
        Guid productId,
        string reason,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (productId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Products.InvalidProductId",
                "Product ID is required.")));
        }

        if (userId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Products.InvalidUserId",
                "User ID is required when deactivating a product.")));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Products.DeactivateReasonRequired",
                "A reason is required when deactivating a product.")));
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = """
                UPDATE products
                SET
                    is_active = 0,
                    updated_at = $updatedAt
                WHERE id = $id
                  AND is_active = 1;
                """;

            updateCommand.Parameters.AddWithValue("$id", productId.ToString("D"));
            updateCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

            int affectedRows = updateCommand.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.NotFound(
                    "Products.NotFoundOrInactive",
                    "Product was not found or is already inactive.")));
            }

            using var auditCommand = connection.CreateCommand();
            auditCommand.Transaction = transaction;
            auditCommand.CommandText = """
                INSERT INTO audit_logs (
                    id,
                    user_id,
                    action,
                    entity_type,
                    entity_id,
                    reason,
                    created_at
                )
                VALUES (
                    $id,
                    $userId,
                    $action,
                    $entityType,
                    $entityId,
                    $reason,
                    $createdAt
                );
                """;

            auditCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            auditCommand.Parameters.AddWithValue("$userId", userId.ToString("D"));
            auditCommand.Parameters.AddWithValue("$action", "PRODUCT_DEACTIVATED");
            auditCommand.Parameters.AddWithValue("$entityType", "PRODUCT");
            auditCommand.Parameters.AddWithValue("$entityId", productId.ToString("D"));
            auditCommand.Parameters.AddWithValue("$reason", reason.Trim());
            auditCommand.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

            auditCommand.ExecuteNonQuery();

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Products.InvalidUserReference",
                "Product deactivate contains an invalid user reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("Products.DeactivateFailed", ex));
        }
    }

    private static Result ValidateProduct(Product product)
    {
        if (product.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Products.InvalidProductId",
                "Product ID is required."));
        }

        if (string.IsNullOrWhiteSpace(product.Name))
        {
            return Result.Fail(Error.Validation(
                "Products.NameRequired",
                "Product name is required."));
        }

        if (product.UnitId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Products.UnitRequired",
                "Product unit is required."));
        }

        if (!product.IsActive)
        {
            return Result.Fail(Error.Validation(
                "Products.InactiveWriteNotAllowed",
                "Product write operations require an active product. Use DeactivateAsync to deactivate products."));
        }

        if (!Enum.IsDefined(typeof(ProductTaxClassification), product.TaxClassification))
        {
            return Result.Fail(Error.Validation(
                "Products.InvalidTaxClassification",
                "Product tax classification is invalid."));
        }

        return Result.Success();
    }

    private static void AddProductParameters(
        SqliteCommand command,
        Product product,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        command.Parameters.AddWithValue("$id", product.Id.ToString("D"));
        command.Parameters.AddWithValue("$sku", DbNullableText(product.Sku));
        command.Parameters.AddWithValue("$barcode", DbNullableBarcode(product.Barcode));
        command.Parameters.AddWithValue("$name", product.Name.Trim());
        command.Parameters.AddWithValue("$description", DbNullableText(product.Description));
        command.Parameters.AddWithValue("$categoryId", DbNullableGuid(product.CategoryId));
        command.Parameters.AddWithValue("$brandId", DbNullableGuid(product.BrandId));
        command.Parameters.AddWithValue("$unitId", product.UnitId.ToString("D"));
        command.Parameters.AddWithValue("$supplierId", DbNullableGuid(product.SupplierId));
        command.Parameters.AddWithValue("$costPriceCents", DbNullableMoney(product.CostPrice));
        command.Parameters.AddWithValue("$sellingPriceCents", product.SellingPrice.Cents);
        command.Parameters.AddWithValue("$wholesalePriceCents", DbNullableMoney(product.WholesalePrice));
        command.Parameters.AddWithValue("$stockOnHandX1000", product.StockOnHand.ValueX1000);
        command.Parameters.AddWithValue("$reorderLevelX1000", product.ReorderLevel.ValueX1000);
        command.Parameters.AddWithValue("$trackStock", product.TrackStock ? 1 : 0);
        command.Parameters.AddWithValue("$allowDiscount", product.AllowDiscount ? 1 : 0);
        command.Parameters.AddWithValue("$taxClassification", (int)product.TaxClassification);
        command.Parameters.AddWithValue("$isActive", product.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$notes", DbNullableText(product.Notes));
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", updatedAt?.ToString("O") ?? (object)DBNull.Value);
    }

    private static object DbNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static object DbNullableGuid(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty
            ? value.Value.ToString("D")
            : DBNull.Value;
    }

    private static object DbNullableBarcode(Barcode? value)
    {
        return value is Barcode barcode
            ? DbNullableText(barcode.Value)
            : DBNull.Value;
    }

    private static object DbNullableMoney(Money? value)
    {
        return value is Money money
            ? money.Cents
            : DBNull.Value;
    }

    private static bool IsUniqueConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode is 1555 or 2067 ||
            exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForeignKeyConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode == 787 ||
            exception.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}

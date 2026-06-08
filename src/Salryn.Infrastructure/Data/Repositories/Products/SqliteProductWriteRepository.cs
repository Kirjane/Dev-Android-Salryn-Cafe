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
                    is_active,
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
                    $isActive,
                    $createdAt,
                    $updatedAt
                );
                """;

            AddProductParameters(command, product, product.CreatedAt, product.UpdatedAt);

            command.ExecuteNonQuery();
            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "An active product with the same barcode already exists.")));
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
                    is_active = $isActive,
                    updated_at = $updatedAt
                WHERE id = $id;
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
                    "Products.NotFound",
                    "Product was not found.")));
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "An active product with the same barcode already exists.")));
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
        command.Parameters.AddWithValue("$isActive", product.IsActive ? 1 : 0);
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
        return exception.SqliteErrorCode == 19;
    }
}



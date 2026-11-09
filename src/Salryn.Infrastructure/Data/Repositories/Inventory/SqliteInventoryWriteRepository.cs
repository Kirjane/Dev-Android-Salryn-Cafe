using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories.Inventory;

public sealed class SqliteInventoryWriteRepository :
    SqliteRepositoryBase,
    IInventoryWriteRepository
{
    public SqliteInventoryWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> AddStockMovementAsync(
        StockMovement movement,
        CancellationToken cancellationToken = default)
    {
        return AddStockMovementsAsync(
            new[] { movement },
            cancellationToken);
    }

    public Task<Result> AddStockMovementsAsync(
        IReadOnlyList<StockMovement> movements,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateMovements(movements);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            foreach (StockMovement movement in movements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Result applyResult = ApplyMovement(
                    connection,
                    transaction,
                    movement);

                if (applyResult.IsFailure)
                {
                    transaction.Rollback();
                    return Task.FromResult(applyResult);
                }
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Inventory.DuplicateMovementId",
                "Stock movement ID already exists.")));
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Inventory.InvalidReference",
                "Stock movement contains an invalid product or user reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Task.FromResult(Unexpected("Inventory.AddStockMovementFailed", ex));
        }
    }

    private static Result ApplyMovement(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StockMovement movement)
    {
        ProductStockState? productState = GetProductStockState(
            connection,
            transaction,
            movement.ProductId);

        if (productState is null || !productState.IsActive)
        {
            return Result.Fail(Error.NotFound(
                "Inventory.ProductNotFoundOrInactive",
                "Product was not found or is inactive."));
        }

        if (!productState.TrackStock)
        {
            return Result.Fail(Error.Validation(
                "Inventory.ProductNotStockTracked",
                "Product is not configured for stock tracking."));
        }

        long signedQuantityChange = GetSignedQuantityChangeX1000(movement);
        long newStockOnHand = checked(productState.StockOnHandX1000 + signedQuantityChange);

        if (newStockOnHand < 0)
        {
            return Result.Fail(Error.Conflict(
                "Inventory.NegativeStockNotAllowed",
                "Stock movement would make product stock negative."));
        }

        UpdateProductStock(
            connection,
            transaction,
            movement.ProductId,
            newStockOnHand);

        InsertStockMovement(
            connection,
            transaction,
            movement,
            signedQuantityChange);

        Result syncResult = StockBucketSyncHelper.SyncAvailableStockBucket(
            connection,
            transaction,
            movement.ProductId,
            signedQuantityChange,
            DateTimeOffset.UtcNow);

        if (syncResult.IsFailure)
        {
            return syncResult;
        }

        return Result.Success();
    }

    private static ProductStockState? GetProductStockState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                stock_on_hand_x1000,
                track_stock,
                is_active
            FROM products
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", productId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new ProductStockState(
            reader.GetInt64(0),
            reader.GetInt32(1) != 0,
            reader.GetInt32(2) != 0);
    }

    private static void UpdateProductStock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId,
        long newStockOnHandX1000)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE products
            SET
                stock_on_hand_x1000 = $stockOnHandX1000,
                updated_at = $updatedAt
            WHERE id = $id
              AND is_active = 1
              AND track_stock = 1;
            """;

        command.Parameters.AddWithValue("$id", productId.ToString("D"));
        command.Parameters.AddWithValue("$stockOnHandX1000", newStockOnHandX1000);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        int affectedRows = command.ExecuteNonQuery();

        if (affectedRows == 0)
        {
            throw new InvalidOperationException("Product stock update failed.");
        }
    }

    private static void InsertStockMovement(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StockMovement movement,
        long signedQuantityChangeX1000)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO stock_movements (
                id,
                product_id,
                movement_type,
                qty_change_x1000,
                cost_price_cents,
                reference_type,
                reference_id,
                reason,
                user_id,
                created_at
            )
            VALUES (
                $id,
                $productId,
                $movementType,
                $quantityChangeX1000,
                $costPriceCents,
                $referenceType,
                $referenceId,
                $reason,
                $userId,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", movement.Id.ToString("D"));
        command.Parameters.AddWithValue("$productId", movement.ProductId.ToString("D"));
        command.Parameters.AddWithValue("$movementType", Convert.ToInt32(movement.MovementType));
        command.Parameters.AddWithValue("$quantityChangeX1000", signedQuantityChangeX1000);
        command.Parameters.AddWithValue("$costPriceCents", DbNullableMoney(movement.CostPrice));
        command.Parameters.AddWithValue("$referenceType", DbNullableText(movement.ReferenceType));
        command.Parameters.AddWithValue("$referenceId", DbNullableGuid(movement.ReferenceId));
        command.Parameters.AddWithValue("$reason", DbNullableText(movement.Reason));
        command.Parameters.AddWithValue("$userId", movement.UserId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", movement.CreatedAt.ToString("O"));

        command.ExecuteNonQuery();
    }

    private static Result ValidateMovements(IReadOnlyList<StockMovement> movements)
    {
        if (movements.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "Inventory.EmptyMovementBatch",
                "At least one stock movement is required."));
        }

        HashSet<Guid> movementIds = new();

        foreach (StockMovement movement in movements)
        {
            Result validation = ValidateMovement(movement);

            if (validation.IsFailure)
            {
                return validation;
            }

            if (!movementIds.Add(movement.Id))
            {
                return Result.Fail(Error.Validation(
                    "Inventory.DuplicateMovementId",
                    "Stock movement IDs must be unique within a batch."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateMovement(StockMovement movement)
    {
        if (movement.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Inventory.InvalidMovementId",
                "Stock movement ID is required."));
        }

        if (movement.ProductId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Inventory.InvalidProductId",
                "Product ID is required."));
        }

        if (movement.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Inventory.InvalidUserId",
                "User ID is required."));
        }

        if (!Enum.IsDefined(typeof(StockMovementType), movement.MovementType))
        {
            return Result.Fail(Error.Validation(
                "Inventory.InvalidMovementType",
                "Stock movement type is invalid."));
        }

        if (movement.QuantityChange.ValueX1000 <= 0)
        {
            return Result.Fail(Error.Validation(
                "Inventory.QuantityRequired",
                "Stock movement quantity must be greater than zero."));
        }

        return Result.Success();
    }

    private static long GetSignedQuantityChangeX1000(StockMovement movement)
    {
        long quantity = movement.QuantityChange.ValueX1000;

        return movement.MovementType switch
        {
            StockMovementType.Sale => checked(-quantity),
            StockMovementType.VoidSale => quantity,
            StockMovementType.StockIn => quantity,
            StockMovementType.AdjustmentAdd => quantity,
            StockMovementType.AdjustmentRemove => checked(-quantity),
            StockMovementType.Damage => checked(-quantity),
            StockMovementType.Expired => checked(-quantity),
            StockMovementType.CountCorrection => quantity,
            _ => throw new InvalidOperationException("Unsupported stock movement type.")
        };
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

    private sealed record ProductStockState(
        long StockOnHandX1000,
        bool TrackStock,
        bool IsActive);
}

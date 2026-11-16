using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories.Products;

/// <summary>
/// Bounded unknown-barcode checkout product creation. This is intentionally separate from
/// general product management and owns all of its product/stock/audit writes in one transaction.
/// </summary>
public sealed class SqliteFastAddProductWriteRepository : SqliteRepositoryBase
{
    private const string ReferenceType = "POS_M4_FAST_ADD";

    public SqliteFastAddProductWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> CreateAsync(
        Product product,
        Quantity initialStockQuantity,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        cancellationToken.ThrowIfCancellationRequested();

        if (product.Id == Guid.Empty || product.UnitId == Guid.Empty ||
            product.Barcode is null || string.IsNullOrWhiteSpace(product.Name) ||
            product.SellingPrice.Cents <= 0 || !product.TrackStock || !product.IsActive ||
            product.StockOnHand != Quantity.Zero || initialStockQuantity.ValueX1000 < 0 ||
            operatorId == Guid.Empty)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "FastAdd.InvalidAtomicWrite",
                "Fast Add requires a new active tracked product, valid operator and zero starting balance.")));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            cancellationToken.ThrowIfCancellationRequested();

            string? locationId;
            using (SqliteCommand location = connection.CreateCommand())
            {
                location.Transaction = transaction;
                location.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
                locationId = location.ExecuteScalar() as string;
            }

            if (string.IsNullOrWhiteSpace(locationId))
            {
                return Task.FromResult(Result.Fail(Error.Failure(
                    "FastAdd.MainStoreMissing",
                    "MAIN-STORE inventory location is missing; no product was created.")));
            }

            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO products (
                        id, barcode, name, description, unit_id, selling_price_cents,
                        stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount,
                        tax_classification, is_active, notes, created_at
                    )
                    VALUES (
                        $id, $barcode, $name, $description, $unit, $price,
                        0, 0, 1, $allowDiscount, $tax, 1, $notes, $createdAt
                    );
                    """;
                insert.Parameters.AddWithValue("$id", product.Id.ToString("D"));
                insert.Parameters.AddWithValue("$barcode", product.Barcode.Value.Value);
                insert.Parameters.AddWithValue("$name", product.Name.Trim());
                insert.Parameters.AddWithValue("$description", (object?)product.Description ?? DBNull.Value);
                insert.Parameters.AddWithValue("$unit", product.UnitId.ToString("D"));
                insert.Parameters.AddWithValue("$price", product.SellingPrice.Cents);
                insert.Parameters.AddWithValue("$allowDiscount", product.AllowDiscount ? 1 : 0);
                insert.Parameters.AddWithValue("$tax", (int)product.TaxClassification);
                insert.Parameters.AddWithValue("$notes", (object?)product.Notes ?? DBNull.Value);
                insert.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
                insert.ExecuteNonQuery(); // unique active-barcode index is final concurrency arbiter
            }

            using (SqliteCommand bucket = connection.CreateCommand())
            {
                bucket.Transaction = transaction;
                bucket.CommandText = """
                    INSERT INTO product_stock_buckets (
                        id, product_id, location_id, bucket_status, quantity_x1000, updated_at
                    ) VALUES ($id, $product, $location, 'Available', 0, $at);
                    """;
                bucket.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                bucket.Parameters.AddWithValue("$product", product.Id.ToString("D"));
                bucket.Parameters.AddWithValue("$location", locationId);
                bucket.Parameters.AddWithValue("$at", createdAt.ToString("O"));
                bucket.ExecuteNonQuery();
            }

            if (initialStockQuantity.ValueX1000 > 0)
            {
                using (SqliteCommand stock = connection.CreateCommand())
                {
                    stock.Transaction = transaction;
                    stock.CommandText = """
                        UPDATE products SET stock_on_hand_x1000 = $qty, updated_at = $at
                        WHERE id = $id AND is_active = 1 AND track_stock = 1;
                        """;
                    stock.Parameters.AddWithValue("$qty", initialStockQuantity.ValueX1000);
                    stock.Parameters.AddWithValue("$at", createdAt.ToString("O"));
                    stock.Parameters.AddWithValue("$id", product.Id.ToString("D"));
                    if (stock.ExecuteNonQuery() != 1)
                    {
                        return Task.FromResult(Result.Fail(Error.Unexpected(
                            "FastAdd.ProductStockUpdateFailed",
                            "Product stock update failed; no Fast Add data was committed.")));
                    }
                }

                using (SqliteCommand movement = connection.CreateCommand())
                {
                    movement.Transaction = transaction;
                    movement.CommandText = """
                        INSERT INTO stock_movements (
                            id, product_id, movement_type, qty_change_x1000, cost_price_cents,
                            reference_type, reference_id, reason, user_id, created_at
                        )
                        VALUES ($id, $product, $type, $qty, NULL, $reference, $product,
                                $reason, $operator, $at);
                        """;
                    movement.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                    movement.Parameters.AddWithValue("$product", product.Id.ToString("D"));
                    movement.Parameters.AddWithValue("$type", (int)StockMovementType.StockIn);
                    movement.Parameters.AddWithValue("$qty", initialStockQuantity.ValueX1000);
                    movement.Parameters.AddWithValue("$reference", ReferenceType);
                    movement.Parameters.AddWithValue("$reason", "POS-M4 Fast Add initial inventory quantity.");
                    movement.Parameters.AddWithValue("$operator", operatorId.ToString("D"));
                    movement.Parameters.AddWithValue("$at", createdAt.ToString("O"));
                    movement.ExecuteNonQuery();
                }

                Result bucketSync = StockBucketSyncHelper.SyncAvailableStockBucket(
                    connection, transaction, product.Id, initialStockQuantity.ValueX1000, createdAt);
                if (bucketSync.IsFailure)
                {
                    return Task.FromResult(bucketSync);
                }
            }

            // Unlike best-effort post-commit auditing, creation evidence is required for commit.
            using (SqliteCommand audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText = """
                    INSERT INTO audit_logs (
                        id, user_id, action, entity_type, entity_id, old_value_json,
                        new_value_json, reason, created_at
                    ) VALUES ($id, $operator, 'PRODUCT_CREATED', 'Product', $product,
                              NULL, $newValue, $reason, $at);
                    """;
                audit.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                audit.Parameters.AddWithValue("$operator", operatorId.ToString("D"));
                audit.Parameters.AddWithValue("$product", product.Id.ToString("D"));
                audit.Parameters.AddWithValue("$newValue", JsonSerializer.Serialize(new
                {
                    productId = product.Id.ToString("D"),
                    barcode = product.Barcode.Value.Value,
                    name = product.Name,
                    sellingPriceCents = product.SellingPrice.Cents,
                    initialStockQuantityX1000 = initialStockQuantity.ValueX1000,
                    referenceType = ReferenceType
                }));
                audit.Parameters.AddWithValue("$reason", "Unknown-barcode checkout Fast Add product creation.");
                audit.Parameters.AddWithValue("$at", createdAt.ToString("O"));
                audit.ExecuteNonQuery();
            }

            using (SqliteCommand invariant = connection.CreateCommand())
            {
                invariant.Transaction = transaction;
                invariant.CommandText = """
                    SELECT p.stock_on_hand_x1000, b.quantity_x1000
                    FROM products p
                    INNER JOIN product_stock_buckets b ON b.product_id = p.id
                    WHERE p.id = $id AND b.location_id = $location AND b.bucket_status = 'Available';
                    """;
                invariant.Parameters.AddWithValue("$id", product.Id.ToString("D"));
                invariant.Parameters.AddWithValue("$location", locationId);
                using SqliteDataReader reader = invariant.ExecuteReader();
                if (!reader.Read() ||
                    reader.GetInt64(0) != initialStockQuantity.ValueX1000 ||
                    reader.GetInt64(1) != initialStockQuantity.ValueX1000 ||
                    reader.Read())
                {
                    return Task.FromResult(Result.Fail(Error.Unexpected(
                        "FastAdd.StockInvariantFailed",
                        "Fast Add stock and MAIN-STORE bucket did not reconcile; transaction rolled back.")));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 &&
            (ex.Message.Contains("products.barcode", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("idx_products_barcode_active", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Products.DuplicateBarcode",
                "Barcode is already assigned to another active product.")));
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 787)
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "FastAdd.InvalidReference",
                "Fast Add contains an invalid unit, product or operator reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Task.FromResult(Unexpected("FastAdd.AtomicWriteFailed", ex));
        }
    }
}

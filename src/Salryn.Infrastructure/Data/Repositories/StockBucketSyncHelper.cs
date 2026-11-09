using Microsoft.Data.Sqlite;
using Salryn.Core.Common;

namespace Salryn.Infrastructure.Data.Repositories;

internal static class StockBucketSyncHelper
{
    public static Result SyncAvailableStockBucket(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId,
        long quantityDeltaX1000,
        DateTimeOffset updatedAt)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.Transaction = transaction;
        checkCmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'product_stock_buckets' LIMIT 1;";
        object? tableExists = checkCmd.ExecuteScalar();
        if (tableExists is null or DBNull)
        {
            return Result.Success();
        }

        using var locCmd = connection.CreateCommand();
        locCmd.Transaction = transaction;
        locCmd.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
        object? locIdObj = locCmd.ExecuteScalar();
        if (locIdObj is null or DBNull)
        {
            return Result.Success();
        }

        string locId = (string)locIdObj;

        // Read current available stock in MAIN-STORE
        long currentAvailableX1000 = 0;
        bool exists = false;
        using var readCmd = connection.CreateCommand();
        readCmd.Transaction = transaction;
        readCmd.CommandText = """
            SELECT quantity_x1000 FROM product_stock_buckets
            WHERE product_id = $prodId AND location_id = $locId AND bucket_status = 'Available'
            LIMIT 1;
            """;
        readCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
        readCmd.Parameters.AddWithValue("$locId", locId);
        using (var reader = readCmd.ExecuteReader())
        {
            if (reader.Read())
            {
                exists = true;
                currentAvailableX1000 = reader.GetInt64(0);
            }
        }

        if (!exists)
        {
            // If this product has no bucket in MAIN-STORE, check if it has buckets in ANY other location.
            // If it has NO location buckets anywhere (e.g. pre-existing legacy product seeded without buckets),
            // its available stock on hand resides in MAIN-STORE.
            using var anyBucketCmd = connection.CreateCommand();
            anyBucketCmd.Transaction = transaction;
            anyBucketCmd.CommandText = "SELECT COUNT(*) FROM product_stock_buckets WHERE product_id = $prodId;";
            anyBucketCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
            long anyBucketCount = Convert.ToInt64(anyBucketCmd.ExecuteScalar());
            if (anyBucketCount == 0)
            {
                using var prodStockCmd = connection.CreateCommand();
                prodStockCmd.Transaction = transaction;
                prodStockCmd.CommandText = "SELECT COALESCE(stock_on_hand_x1000, 0) FROM products WHERE id = $prodId;";
                prodStockCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
                object? pStock = prodStockCmd.ExecuteScalar();
                if (pStock is not null and not DBNull)
                {
                    currentAvailableX1000 = Convert.ToInt64(pStock);
                }
            }
        }

        long newQuantityX1000 = checked(currentAvailableX1000 + quantityDeltaX1000);

        // Fail-closed invariant: Reject unavailable location quantities instead of clamping to zero
        if (newQuantityX1000 < 0)
        {
            return Result.Fail(Error.Conflict(
                "Sales.InsufficientLocationStock",
                $"Insufficient sellable stock in MAIN-STORE for product '{productId}'. Available in MAIN-STORE: {currentAvailableX1000 / 1000.0:F3}, requested: {(-quantityDeltaX1000) / 1000.0:F3}. Units stored in other locations must be transferred to MAIN-STORE before counter checkout."));
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        if (exists)
        {
            syncCmd.CommandText = """
                UPDATE product_stock_buckets
                SET quantity_x1000 = $newQty,
                    updated_at = $now
                WHERE product_id = $prodId AND location_id = $locId AND bucket_status = 'Available';
                """;
        }
        else
        {
            syncCmd.CommandText = """
                INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
                VALUES ($id, $prodId, $locId, 'Available', $newQty, $now);
                """;
            syncCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        }

        syncCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
        syncCmd.Parameters.AddWithValue("$locId", locId);
        syncCmd.Parameters.AddWithValue("$newQty", newQuantityX1000);
        syncCmd.Parameters.AddWithValue("$now", updatedAt.ToString("O"));
        syncCmd.ExecuteNonQuery();

        return Result.Success();
    }
}

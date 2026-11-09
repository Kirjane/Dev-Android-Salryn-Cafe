using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Repositories;

internal static class StockBucketSyncHelper
{
    public static void SyncAvailableStockBucket(
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
        if (tableExists is null || tableExists is DBNull)
        {
            return;
        }

        using var locCmd = connection.CreateCommand();
        locCmd.Transaction = transaction;
        locCmd.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
        object? locIdObj = locCmd.ExecuteScalar();
        if (locIdObj is null || locIdObj is DBNull)
        {
            return;
        }

        string locId = (string)locIdObj;

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = """
            INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
            VALUES ($id, $prodId, $locId, 'Available', MAX(0, $delta), $now)
            ON CONFLICT (product_id, location_id, bucket_status) DO UPDATE SET
                quantity_x1000 = MAX(0, product_stock_buckets.quantity_x1000 + $delta),
                updated_at = excluded.updated_at;
            """;
        syncCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        syncCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
        syncCmd.Parameters.AddWithValue("$locId", locId);
        syncCmd.Parameters.AddWithValue("$delta", quantityDeltaX1000);
        syncCmd.Parameters.AddWithValue("$now", updatedAt.ToString("O"));
        syncCmd.ExecuteNonQuery();
    }
}

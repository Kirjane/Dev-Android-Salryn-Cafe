using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.Inventory;

public sealed class SqliteInventoryReadRepository :
    SqliteRepositoryBase,
    IInventoryReadRepository
{
    public SqliteInventoryReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<PagedResult<StockMovementReadModel>>> SearchStockMovementsAsync(
        StockMovementFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountStockMovements(connection, filter);
            IReadOnlyList<StockMovementReadModel> items = SearchStockMovements(
                connection,
                filter,
                cancellationToken);

            PagedResult<StockMovementReadModel> page = new(
                items,
                totalCount,
                filter.Page.PageNumber,
                filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<StockMovementReadModel>>.Success(page));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<PagedResult<StockMovementReadModel>>(
                "Inventory.SearchStockMovementsFailed",
                ex));
        }
    }

    public Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockProductsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    p.id,
                    p.barcode,
                    p.name,
                    u.name AS unit_name,
                    p.stock_on_hand_x1000,
                    p.reorder_level_x1000
                FROM products p
                INNER JOIN units u ON u.id = p.unit_id
                WHERE p.is_active = 1
                  AND p.track_stock = 1
                  AND p.reorder_level_x1000 > 0
                  AND p.stock_on_hand_x1000 <= p.reorder_level_x1000
                ORDER BY p.name;
                """;

            using var reader = command.ExecuteReader();

            List<LowStockProductReadModel> items = new();

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                items.Add(new LowStockProductReadModel(
                    reader.GetGuidValue("id"),
                    reader.GetNullableString("barcode"),
                    reader.GetStringValue("name"),
                    reader.GetStringValue("unit_name"),
                    reader.GetQuantityValue("stock_on_hand_x1000"),
                    reader.GetQuantityValue("reorder_level_x1000")));
            }

            IReadOnlyList<LowStockProductReadModel> result = items;

            return Task.FromResult(Result<IReadOnlyList<LowStockProductReadModel>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<IReadOnlyList<LowStockProductReadModel>>(
                "Inventory.GetLowStockProductsFailed",
                ex));
        }
    }

    private static int CountStockMovements(
        SqliteConnection connection,
        StockMovementFilter filter)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM stock_movements sm
            WHERE ($productId IS NULL OR sm.product_id = $productId)
              AND ($movementType IS NULL OR sm.movement_type = $movementType)
              AND ($from IS NULL OR sm.created_at >= $from)
              AND ($to IS NULL OR sm.created_at <= $to);
            """;

        AddStockMovementFilterParameters(command, filter);

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }

    private static IReadOnlyList<StockMovementReadModel> SearchStockMovements(
        SqliteConnection connection,
        StockMovementFilter filter,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                sm.id,
                sm.product_id,
                p.name AS product_name,
                sm.movement_type,
                sm.qty_change_x1000,
                sm.cost_price_cents,
                sm.reference_type,
                sm.reference_id,
                sm.reason,
                u.display_name AS user_display_name,
                sm.created_at
            FROM stock_movements sm
            INNER JOIN products p ON p.id = sm.product_id
            INNER JOIN users u ON u.id = sm.user_id
            WHERE ($productId IS NULL OR sm.product_id = $productId)
              AND ($movementType IS NULL OR sm.movement_type = $movementType)
              AND ($from IS NULL OR sm.created_at >= $from)
              AND ($to IS NULL OR sm.created_at <= $to)
            ORDER BY sm.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;

        AddStockMovementFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", Math.Max(1, filter.Page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Page.Offset));

        using var reader = command.ExecuteReader();

        List<StockMovementReadModel> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            long quantityChangeX1000 = reader.GetInt64Value("qty_change_x1000");

            items.Add(new StockMovementReadModel(
                reader.GetGuidValue("id"),
                reader.GetGuidValue("product_id"),
                reader.GetStringValue("product_name"),
                reader.GetEnumValue<StockMovementType>("movement_type"),
                quantityChangeX1000,
                quantityChangeX1000 / 1000m,
                reader.GetNullableMoneyValue("cost_price_cents"),
                reader.GetNullableString("reference_type"),
                reader.GetNullableString("reference_id"),
                reader.GetNullableString("reason"),
                reader.GetStringValue("user_display_name"),
                reader.GetDateTimeOffsetValue("created_at")));
        }

        return items;
    }

    private static void AddStockMovementFilterParameters(
        SqliteCommand command,
        StockMovementFilter filter)
    {
        command.Parameters.AddWithValue("$productId", filter.ProductId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$movementType", filter.MovementType.HasValue ? Convert.ToInt32(filter.MovementType.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$from", filter.From?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$to", filter.To?.ToString("O") ?? (object)DBNull.Value);
    }
}

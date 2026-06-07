using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories.Products;

public sealed class SqliteProductReadRepository :
    SqliteRepositoryBase,
    IProductReadRepository
{
    public SqliteProductReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<ProductLookupReadModel?>> FindByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = ProductLookupSql + Environment.NewLine + """
                WHERE p.id = $id
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$id", productId.ToString("D"));

            using var reader = command.ExecuteReader();

            ProductLookupReadModel? product = reader.Read()
                ? MapProductLookup(reader)
                : null;

            return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<ProductLookupReadModel?>(
                "Products.FindByIdFailed",
                ex));
        }
    }

    public Task<Result<ProductLookupReadModel?>> FindByBarcodeAsync(
        Barcode barcode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = ProductLookupSql + Environment.NewLine + """
                WHERE p.barcode = $barcode AND p.is_active = 1
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$barcode", barcode.Value);

            using var reader = command.ExecuteReader();

            ProductLookupReadModel? product = reader.Read()
                ? MapProductLookup(reader)
                : null;

            return Task.FromResult(Result<ProductLookupReadModel?>.Success(product));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<ProductLookupReadModel?>(
                "Products.FindByBarcodeFailed",
                ex));
        }
    }

    public Task<Result<PagedResult<ProductListItemReadModel>>> SearchAsync(
        ProductSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountProducts(connection, filter);
            IReadOnlyList<ProductListItemReadModel> items = SearchProducts(
                connection,
                filter,
                cancellationToken);

            PagedResult<ProductListItemReadModel> page = new(
                items,
                totalCount,
                filter.Page.PageNumber,
                filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<ProductListItemReadModel>>.Success(page));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<PagedResult<ProductListItemReadModel>>(
                "Products.SearchFailed",
                ex));
        }
    }

    public Task<Result<IReadOnlyList<LowStockProductReadModel>>> GetLowStockAsync(
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
                "Products.GetLowStockFailed",
                ex));
        }
    }

    private static int CountProducts(
        SqliteConnection connection,
        ProductSearchFilter filter)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM products p
            WHERE ($includeInactive = 1 OR p.is_active = 1)
              AND ($isActive IS NULL OR p.is_active = $isActive)
              AND ($categoryId IS NULL OR p.category_id = $categoryId)
              AND ($brandId IS NULL OR p.brand_id = $brandId)
              AND (
                    $search IS NULL
                    OR p.name LIKE $searchLike
                    OR p.barcode LIKE $searchLike
                    OR p.sku LIKE $searchLike
                  );
            """;

        AddFilterParameters(command, filter);

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }

    private static IReadOnlyList<ProductListItemReadModel> SearchProducts(
        SqliteConnection connection,
        ProductSearchFilter filter,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                p.id,
                p.sku,
                p.barcode,
                p.name,
                u.name AS unit_name,
                c.name AS category_name,
                b.name AS brand_name,
                p.selling_price_cents,
                p.cost_price_cents,
                p.stock_on_hand_x1000,
                p.reorder_level_x1000,
                p.track_stock,
                p.is_active
            FROM products p
            INNER JOIN units u ON u.id = p.unit_id
            LEFT JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands b ON b.id = p.brand_id
            WHERE ($includeInactive = 1 OR p.is_active = 1)
              AND ($isActive IS NULL OR p.is_active = $isActive)
              AND ($categoryId IS NULL OR p.category_id = $categoryId)
              AND ($brandId IS NULL OR p.brand_id = $brandId)
              AND (
                    $search IS NULL
                    OR p.name LIKE $searchLike
                    OR p.barcode LIKE $searchLike
                    OR p.sku LIKE $searchLike
                  )
            ORDER BY p.name
            LIMIT $limit OFFSET $offset;
            """;

        AddFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", Math.Max(1, filter.Page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Page.Offset));

        using var reader = command.ExecuteReader();

        List<ProductListItemReadModel> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(MapProductListItem(reader));
        }

        return items;
    }

    private static void AddFilterParameters(
        SqliteCommand command,
        ProductSearchFilter filter)
    {
        string? search = string.IsNullOrWhiteSpace(filter.SearchText)
            ? null
            : filter.SearchText.Trim();

        command.Parameters.AddWithValue("$includeInactive", filter.IncludeInactive ? 1 : 0);
        command.Parameters.AddWithValue("$isActive", filter.IsActive.HasValue ? (filter.IsActive.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$categoryId", filter.CategoryId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$brandId", filter.BrandId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$search", search ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$searchLike", search is null ? DBNull.Value : $"%{search}%");
    }

    private static ProductLookupReadModel MapProductLookup(SqliteDataReader reader)
    {
        return new ProductLookupReadModel(
            reader.GetGuidValue("id"),
            reader.GetNullableString("sku"),
            reader.GetNullableString("barcode"),
            reader.GetStringValue("name"),
            reader.GetStringValue("unit_name"),
            reader.GetMoneyValue("selling_price_cents"),
            reader.GetNullableMoneyValue("cost_price_cents"),
            reader.GetQuantityValue("stock_on_hand_x1000"),
            reader.GetBoolValue("track_stock"),
            reader.GetBoolValue("allow_discount"),
            reader.GetBoolValue("is_active"));
    }

    private static ProductListItemReadModel MapProductListItem(SqliteDataReader reader)
    {
        return new ProductListItemReadModel(
            reader.GetGuidValue("id"),
            reader.GetNullableString("sku"),
            reader.GetNullableString("barcode"),
            reader.GetStringValue("name"),
            reader.GetStringValue("unit_name"),
            reader.GetNullableString("category_name"),
            reader.GetNullableString("brand_name"),
            reader.GetMoneyValue("selling_price_cents"),
            reader.GetNullableMoneyValue("cost_price_cents"),
            reader.GetQuantityValue("stock_on_hand_x1000"),
            reader.GetQuantityValue("reorder_level_x1000"),
            reader.GetBoolValue("track_stock"),
            reader.GetBoolValue("is_active"));
    }

    private const string ProductLookupSql = """
        SELECT
            p.id,
            p.sku,
            p.barcode,
            p.name,
            u.name AS unit_name,
            p.selling_price_cents,
            p.cost_price_cents,
            p.stock_on_hand_x1000,
            p.track_stock,
            p.allow_discount,
            p.is_active
        FROM products p
        INNER JOIN units u ON u.id = p.unit_id
        """;
}


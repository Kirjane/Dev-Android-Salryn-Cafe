using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
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

            // Kirch: Lookup feeds checkout selection, so inactive products stay unavailable by ID and barcode.
            command.CommandText = ProductLookupSql + Environment.NewLine + """
                WHERE p.id = $id AND p.is_active = 1
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

    // Kirch: Reads a complete product row with catalog names and IDs for safe edit screens.
    public Task<Result<ProductDetailReadModel?>> FindDetailByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = ProductDetailSql + Environment.NewLine + """
                WHERE p.id = $id
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$id", productId.ToString("D"));

            using var reader = command.ExecuteReader();

            ProductDetailReadModel? product = reader.Read()
                ? MapProductDetail(reader)
                : null;

            return Task.FromResult(Result<ProductDetailReadModel?>.Success(product));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<ProductDetailReadModel?>(
                "Products.FindDetailByIdFailed",
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
                p.tax_classification,
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

    // Kirch: Maps the complete SQL detail row into an edit-safe product detail read model.
    private static ProductDetailReadModel MapProductDetail(SqliteDataReader reader)
    {
        return new ProductDetailReadModel(
            reader.GetGuidValue("id"),
            reader.GetNullableString("sku"),
            reader.GetNullableString("barcode"),
            reader.GetStringValue("name"),
            reader.GetNullableString("description"),
            GetNullableGuid(reader, "category_id"),
            reader.GetNullableString("category_name"),
            GetNullableGuid(reader, "brand_id"),
            reader.GetNullableString("brand_name"),
            reader.GetGuidValue("unit_id"),
            reader.GetStringValue("unit_name"),
            GetNullableGuid(reader, "supplier_id"),
            reader.GetNullableString("supplier_name"),
            reader.GetNullableMoneyValue("cost_price_cents"),
            reader.GetMoneyValue("selling_price_cents"),
            reader.GetNullableMoneyValue("wholesale_price_cents"),
            reader.GetQuantityValue("stock_on_hand_x1000"),
            reader.GetQuantityValue("reorder_level_x1000"),
            reader.GetBoolValue("track_stock"),
            reader.GetBoolValue("allow_discount"),
            ReadTaxClassification(reader, "tax_classification"),
            reader.GetBoolValue("is_active"),
            reader.GetNullableString("notes"),
            reader.GetDateTimeOffsetValue("created_at"),
            reader.GetNullableDateTimeOffsetValue("updated_at"));
    }

    // Kirch: SQLite stores nullable foreign keys as text, so this keeps nullable GUID parsing in one place.
    private static Guid? GetNullableGuid(SqliteDataReader reader, string columnName)
    {
        string? value = reader.GetNullableString(columnName);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : Guid.Parse(value);
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
            ReadTaxClassification(reader, "tax_classification"),
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
            ReadTaxClassification(reader, "tax_classification"),
            reader.GetBoolValue("is_active"));
    }

    private static ProductTaxClassification ReadTaxClassification(SqliteDataReader reader, string columnName)
    {
        int storedValue = reader.GetInt32(reader.GetOrdinal(columnName));
        Result<ProductTaxClassification> parsed = ProductTaxClassificationMapper.Parse(storedValue);

        if (parsed.IsFailure)
        {
            throw new InvalidOperationException(parsed.Error);
        }

        return parsed.Value;
    }

    private const string ProductDetailSql = """
        SELECT
            p.id,
            p.sku,
            p.barcode,
            p.name,
            p.description,
            p.category_id,
            c.name AS category_name,
            p.brand_id,
            b.name AS brand_name,
            p.unit_id,
            u.name AS unit_name,
            p.supplier_id,
            s.name AS supplier_name,
            p.cost_price_cents,
            p.selling_price_cents,
            p.wholesale_price_cents,
            p.stock_on_hand_x1000,
            p.reorder_level_x1000,
            p.track_stock,
            p.allow_discount,
            p.tax_classification,
            p.is_active,
            p.notes,
            p.created_at,
            p.updated_at
        FROM products p
        INNER JOIN units u ON u.id = p.unit_id
        LEFT JOIN categories c ON c.id = p.category_id
        LEFT JOIN brands b ON b.id = p.brand_id
        LEFT JOIN suppliers s ON s.id = p.supplier_id
        """;
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
            p.tax_classification,
            p.is_active
        FROM products p
        INNER JOIN units u ON u.id = p.unit_id
        """;
}




using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.Catalog;

public sealed class SqliteCatalogReadRepository :
    SqliteRepositoryBase,
    ICatalogReadRepository
{
    public SqliteCatalogReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<IReadOnlyList<CategoryReadModel>>> GetCategoriesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return ReadListAsync(
            "Catalog.GetCategoriesFailed",
            """
            SELECT id, name, is_active
            FROM categories
            WHERE ($includeInactive = 1 OR is_active = 1)
            ORDER BY name;
            """,
            includeInactive,
            reader => new CategoryReadModel(
                reader.GetGuidValue("id"),
                reader.GetStringValue("name"),
                reader.GetBoolValue("is_active")),
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<BrandReadModel>>> GetBrandsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return ReadListAsync(
            "Catalog.GetBrandsFailed",
            """
            SELECT id, name, is_active
            FROM brands
            WHERE ($includeInactive = 1 OR is_active = 1)
            ORDER BY name;
            """,
            includeInactive,
            reader => new BrandReadModel(
                reader.GetGuidValue("id"),
                reader.GetStringValue("name"),
                reader.GetBoolValue("is_active")),
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<UnitReadModel>>> GetUnitsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return ReadListAsync(
            "Catalog.GetUnitsFailed",
            """
            SELECT id, name, abbreviation, allows_decimal_quantity, is_active
            FROM units
            WHERE ($includeInactive = 1 OR is_active = 1)
            ORDER BY name;
            """,
            includeInactive,
            reader => new UnitReadModel(
                reader.GetGuidValue("id"),
                reader.GetStringValue("name"),
                reader.GetNullableString("abbreviation"),
                reader.GetBoolValue("allows_decimal_quantity"),
                reader.GetBoolValue("is_active")),
            cancellationToken);
    }

    public Task<Result<IReadOnlyList<SupplierReadModel>>> GetSuppliersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        return ReadListAsync(
            "Catalog.GetSuppliersFailed",
            """
            SELECT id, name, contact_number, notes, is_active
            FROM suppliers
            WHERE ($includeInactive = 1 OR is_active = 1)
            ORDER BY name;
            """,
            includeInactive,
            reader => new SupplierReadModel(
                reader.GetGuidValue("id"),
                reader.GetStringValue("name"),
                reader.GetNullableString("contact_number"),
                reader.GetNullableString("notes"),
                reader.GetBoolValue("is_active")),
            cancellationToken);
    }

    private Task<Result<IReadOnlyList<T>>> ReadListAsync<T>(
        string errorCode,
        string sql,
        bool includeInactive,
        Func<SqliteDataReader, T> map,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = sql;
            command.Parameters.AddWithValue("$includeInactive", includeInactive ? 1 : 0);

            using var reader = command.ExecuteReader();

            List<T> items = new();

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(map(reader));
            }

            IReadOnlyList<T> result = items;

            return Task.FromResult(Result<IReadOnlyList<T>>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<IReadOnlyList<T>>(errorCode, ex));
        }
    }
}

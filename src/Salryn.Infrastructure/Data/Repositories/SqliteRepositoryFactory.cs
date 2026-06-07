using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Repositories.Catalog;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Repositories.Products;
using Salryn.Infrastructure.Data.Repositories.Sales;

namespace Salryn.Infrastructure.Data.Repositories;

public sealed class SqliteRepositoryFactory
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteRepositoryFactory(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ICatalogReadRepository CreateCatalogReadRepository()
    {
        return new SqliteCatalogReadRepository(_connectionFactory);
    }

    public IProductReadRepository CreateProductReadRepository()
    {
        return new SqliteProductReadRepository(_connectionFactory);
    }

    public ISaleReadRepository CreateSaleReadRepository()
    {
        return new SqliteSaleReadRepository(_connectionFactory);
    }

    public IInventoryReadRepository CreateInventoryReadRepository()
    {
        return new SqliteInventoryReadRepository(_connectionFactory);
    }
}

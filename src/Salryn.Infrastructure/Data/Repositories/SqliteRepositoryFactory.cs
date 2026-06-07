using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Repositories.Catalog;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Repositories.Products;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Repositories.System;

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

    public IUserReadRepository CreateUserReadRepository()
    {
        return new SqliteUserReadRepository(_connectionFactory);
    }

    public IStoreSettingsReadRepository CreateStoreSettingsReadRepository()
    {
        return new SqliteStoreSettingsReadRepository(_connectionFactory);
    }

    public ICashierSessionReadRepository CreateCashierSessionReadRepository()
    {
        return new SqliteCashierSessionReadRepository(_connectionFactory);
    }

    public IAuditReadRepository CreateAuditReadRepository()
    {
        return new SqliteAuditReadRepository(_connectionFactory);
    }

    public IErrorLogReadRepository CreateErrorLogReadRepository()
    {
        return new SqliteErrorLogReadRepository(_connectionFactory);
    }
}

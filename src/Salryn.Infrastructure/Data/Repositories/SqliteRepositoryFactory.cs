using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data.Repositories.Catalog;
using Salryn.Infrastructure.Data.Repositories.Products;

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
}

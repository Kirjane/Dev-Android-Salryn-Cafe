using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Catalog;
using Salryn.Infrastructure.Data.Repositories.Products;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase12SqliteRepositoryFoundationCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory connectionFactory = new(paths);

        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        ICatalogReadRepository catalogRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IProductReadRepository productRepository =
            repositoryFactory.CreateProductReadRepository();

        SqliteCatalogReadRepository concreteCatalogRepository =
            new(connectionFactory);

        SqliteProductReadRepository concreteProductRepository =
            new(connectionFactory);

        if (catalogRepository is null)
        {
            throw new InvalidOperationException("Catalog repository factory check failed.");
        }

        if (productRepository is null)
        {
            throw new InvalidOperationException("Product repository factory check failed.");
        }

        _ = concreteCatalogRepository;
        _ = concreteProductRepository;
    }
}

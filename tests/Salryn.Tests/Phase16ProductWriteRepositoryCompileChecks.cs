using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Products;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase16ProductWriteRepositoryCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory connectionFactory = new(paths);

        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IProductWriteRepository productWriteRepository =
            repositoryFactory.CreateProductWriteRepository();

        SqliteProductWriteRepository concreteProductWriteRepository =
            new(connectionFactory);

        if (productWriteRepository is null)
        {
            throw new InvalidOperationException("Product write repository factory check failed.");
        }

        _ = concreteProductWriteRepository;
    }
}

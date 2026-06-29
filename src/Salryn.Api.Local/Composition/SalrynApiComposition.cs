using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Api.Local.Composition;

public sealed class SalrynApiComposition
{
    private SalrynApiComposition(
        DbPaths dbPaths,
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializationOrchestrator databaseInitialization,
        ISaleReadRepository saleReadRepository,
        IProductReadRepository productReadRepository,
        IInventoryReadRepository inventoryReadRepository)
    {
        DbPaths = dbPaths;
        ConnectionFactory = connectionFactory;
        DatabaseInitialization = databaseInitialization;
        SaleReadRepository = saleReadRepository;
        ProductReadRepository = productReadRepository;
        InventoryReadRepository = inventoryReadRepository;
    }

    public DbPaths DbPaths { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public DatabaseInitializationOrchestrator DatabaseInitialization { get; }

    public ISaleReadRepository SaleReadRepository { get; }

    public IProductReadRepository ProductReadRepository { get; }

    public IInventoryReadRepository InventoryReadRepository { get; }

    public static SalrynApiComposition CreateProduction()
    {
        DbPaths productionPaths = DbPaths.CreateProduction();
        Result writabilityResult = productionPaths.ValidateProductionWritability();

        if (writabilityResult.IsFailure)
        {
            throw new InvalidOperationException(
                writabilityResult.ErrorDetail.Code + ": " + writabilityResult.ErrorDetail.Message);
        }

        return Create(productionPaths);
    }

    public static SalrynApiComposition CreateDevelopment(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        return Create(DbPaths.CreateDevelopment(projectRoot));
    }

    public static SalrynApiComposition Create(DbPaths dbPaths)
    {
        ArgumentNullException.ThrowIfNull(dbPaths);

        SqliteConnectionFactory connectionFactory = new(dbPaths);

        DatabaseInitializationOrchestrator databaseInitialization =
            DefaultDatabaseInitializationFactory.Create(dbPaths);

        SqliteRepositoryFactory repositories = new(connectionFactory);

        return new SalrynApiComposition(
            dbPaths,
            connectionFactory,
            databaseInitialization,
            repositories.CreateSaleReadRepository(),
            repositories.CreateProductReadRepository(),
            repositories.CreateInventoryReadRepository());
    }

    public Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<DatabaseInitializationSummary> initResult = DatabaseInitialization.InitializeDatabase();

        if (initResult.IsFailure)
        {
            throw new InvalidOperationException(
                initResult.ErrorDetail.Code + ": " + initResult.ErrorDetail.Message);
        }

        return Task.CompletedTask;
    }
}

using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Composition.Local;

public sealed class SalrynLocalComposition
{
    private SalrynLocalComposition(
        DbPaths dbPaths,
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializationOrchestrator databaseInitialization,
        IAuthenticationService authentication,
        ISaleReadRepository saleReadRepository,
        IProductReadRepository productReadRepository,
        IProductWriteRepository productWriteRepository,
        IInventoryReadRepository inventoryReadRepository,
        IInventoryWriteRepository inventoryWriteRepository,
        ICatalogReadRepository catalogReadRepository,
        IUserReadRepository userReadRepository,
        IUserPermissionReadRepository userPermissionReadRepository,
        IAuditReadRepository auditReadRepository,
        IErrorLogReadRepository errorLogReadRepository)
    {
        DbPaths = dbPaths;
        ConnectionFactory = connectionFactory;
        DatabaseInitialization = databaseInitialization;
        Authentication = authentication;
        SaleReadRepository = saleReadRepository;
        ProductReadRepository = productReadRepository;
        ProductWriteRepository = productWriteRepository;
        InventoryReadRepository = inventoryReadRepository;
        InventoryWriteRepository = inventoryWriteRepository;
        CatalogReadRepository = catalogReadRepository;
        UserReadRepository = userReadRepository;
        UserPermissionReadRepository = userPermissionReadRepository;
        AuditReadRepository = auditReadRepository;
        ErrorLogReadRepository = errorLogReadRepository;
    }

    public DbPaths DbPaths { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public DatabaseInitializationOrchestrator DatabaseInitialization { get; }

    public IAuthenticationService Authentication { get; }

    public ISaleReadRepository SaleReadRepository { get; }

    public IProductReadRepository ProductReadRepository { get; }

    public IProductWriteRepository ProductWriteRepository { get; }

    public IInventoryReadRepository InventoryReadRepository { get; }

    public IInventoryWriteRepository InventoryWriteRepository { get; }

    public ICatalogReadRepository CatalogReadRepository { get; }

    public IUserReadRepository UserReadRepository { get; }

    public IUserPermissionReadRepository UserPermissionReadRepository { get; }

    public IAuditReadRepository AuditReadRepository { get; }

    public IErrorLogReadRepository ErrorLogReadRepository { get; }

    public static SalrynLocalComposition CreateProduction()
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

    public static SalrynLocalComposition CreateDevelopment(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        return Create(DbPaths.CreateDevelopment(projectRoot));
    }

    public static SalrynLocalComposition Create(DbPaths dbPaths)
    {
        ArgumentNullException.ThrowIfNull(dbPaths);

        SqliteConnectionFactory connectionFactory = new(dbPaths);

        DatabaseInitializationOrchestrator databaseInitialization =
            DefaultDatabaseInitializationFactory.Create(dbPaths);

        SqliteRepositoryFactory repositories = new(connectionFactory);

        IUserPermissionReadRepository userPermissionReadRepository =
            repositories.CreateUserPermissionReadRepository();

        IAuthenticationService authentication = new AuthenticationService(
            repositories.CreateAuthenticationRepository(),
            userPermissionReadRepository);

        return new SalrynLocalComposition(
            dbPaths,
            connectionFactory,
            databaseInitialization,
            authentication,
            repositories.CreateSaleReadRepository(),
            repositories.CreateProductReadRepository(),
            repositories.CreateProductWriteRepository(),
            repositories.CreateInventoryReadRepository(),
            repositories.CreateInventoryWriteRepository(),
            repositories.CreateCatalogReadRepository(),
            repositories.CreateUserReadRepository(),
            userPermissionReadRepository,
            repositories.CreateAuditReadRepository(),
            repositories.CreateErrorLogReadRepository());
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

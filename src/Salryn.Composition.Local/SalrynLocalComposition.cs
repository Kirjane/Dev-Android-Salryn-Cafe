using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Repositories.Menu;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Paths;

namespace Salryn.Composition.Local;

public sealed class SalrynLocalComposition
{
    private const string CoreSmokeDevelopmentRootEnvironmentVariable = "SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT";

    private SalrynLocalComposition(
        DbPaths dbPaths,
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializationOrchestrator databaseInitialization,
        IAuthenticationService authentication,
        SqliteMenuCatalogStore menuCatalog,
        SqliteIngredientRecipeStore ingredientRecipes,
        SqliteSupplierDirectoryStore supplierDirectory,
        SqliteCafeInventoryStore cafeInventory,
        SqliteCafeOrderStore cafeOrders,
        SqliteCafeOrderCompletionCoordinator cafeOrderCompletion,
        ICafeCashService cafeCash,
        ICafeRefundService cafeRefunds,
        ICafePrintService cafePrint,
        ICafeDeliveryService cafeDelivery,
        ICafeBookingService cafeBooking,
        ICafeAttendanceIncidentService cafeAttendanceIncident,
        ICafeManagementReportingService cafeManagementReports,
        SqliteCafeCashBootstrapStore cafeCashBootstrap,
        SqliteCafeOrderBootstrapStore cafeOrderBootstrap,
        SqliteDeploymentIdentityRepository deploymentIdentityRepository,
        ISaleReadRepository saleReadRepository,
        ISaleWriteRepository saleWriteRepository,
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
        MenuCatalog = menuCatalog;
        IngredientRecipes = ingredientRecipes;
        SupplierDirectory = supplierDirectory;
        CafeInventory = cafeInventory;
        CafeOrders = cafeOrders;
        CafeOrderCompletion = cafeOrderCompletion;
        CafeCash = cafeCash;
        CafeRefunds = cafeRefunds;
        CafePrint = cafePrint;
        CafeDelivery = cafeDelivery;
        CafeBooking = cafeBooking;
        CafeAttendanceIncident = cafeAttendanceIncident;
        CafeManagementReports = cafeManagementReports;
        CafeCashBootstrap = cafeCashBootstrap;
        CafeOrderBootstrap = cafeOrderBootstrap;
        DeploymentIdentityRepository = deploymentIdentityRepository;
        SaleReadRepository = saleReadRepository;
        SaleWriteRepository = saleWriteRepository;
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

    public SqliteMenuCatalogStore MenuCatalog { get; }

    public SqliteIngredientRecipeStore IngredientRecipes { get; }

    public SqliteSupplierDirectoryStore SupplierDirectory { get; }

    public SqliteCafeInventoryStore CafeInventory { get; }

    public SqliteCafeOrderStore CafeOrders { get; }

    public SqliteCafeOrderCompletionCoordinator CafeOrderCompletion { get; }

    public ICafeCashService CafeCash { get; }

    public ICafeRefundService CafeRefunds { get; }

    public ICafePrintService CafePrint { get; }

    public ICafeDeliveryService CafeDelivery { get; }

    public ICafeBookingService CafeBooking { get; }

    public ICafeAttendanceIncidentService CafeAttendanceIncident { get; }

    public ICafeManagementReportingService CafeManagementReports { get; }

    public SqliteCafeCashBootstrapStore CafeCashBootstrap { get; }

    public SqliteCafeOrderBootstrapStore CafeOrderBootstrap { get; }

    public SqliteDeploymentIdentityRepository DeploymentIdentityRepository { get; }

    public ISaleReadRepository SaleReadRepository { get; }

    public ISaleWriteRepository SaleWriteRepository { get; }

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

        string resolvedRoot = ResolveDevelopmentRoot(projectRoot);
        return Create(DbPaths.CreateDevelopment(resolvedRoot));
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
            repositories.CreateMenuCatalogStore(),
            repositories.CreateIngredientRecipeStore(),
            new SqliteSupplierDirectoryStore(connectionFactory),
            new SqliteCafeInventoryStore(connectionFactory),
            new SqliteCafeOrderStore(connectionFactory),
            new SqliteCafeOrderCompletionCoordinator(connectionFactory),
            new SqliteCafeCashService(connectionFactory),
            new SqliteCafeRefundService(connectionFactory),
            new SqliteCafePrintService(connectionFactory),
            new SqliteCafeDeliveryService(connectionFactory),
            new SqliteCafeBookingService(connectionFactory),
            new SqliteCafeAttendanceIncidentService(connectionFactory),
            new SqliteCafeManagementReportingService(connectionFactory),
            new SqliteCafeCashBootstrapStore(connectionFactory),
            new SqliteCafeOrderBootstrapStore(connectionFactory),
            repositories.CreateDeploymentIdentityRepository(),
            repositories.CreateSaleReadRepository(),
            repositories.CreateSaleWriteRepository(),
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

    private static string ResolveDevelopmentRoot(string projectRoot)
    {
        string? isolatedRoot = Environment.GetEnvironmentVariable(CoreSmokeDevelopmentRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(isolatedRoot))
        {
            return projectRoot;
        }

        string fullProjectRoot = Path.GetFullPath(projectRoot);
        if (!File.Exists(Path.Combine(fullProjectRoot, "Salryn.sln")))
        {
            return projectRoot;
        }

        string fullIsolatedRoot = Path.GetFullPath(isolatedRoot);

        if (string.Equals(fullProjectRoot, fullIsolatedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CoreSmoke development database isolation root must not equal the active project root.");
        }

        string activeDevelopmentDatabase = Path.GetFullPath(DbPaths.CreateDevelopment(fullProjectRoot).DatabasePath);
        string isolatedDevelopmentDatabase = Path.GetFullPath(DbPaths.CreateDevelopment(fullIsolatedRoot).DatabasePath);

        if (string.Equals(activeDevelopmentDatabase, isolatedDevelopmentDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CoreSmoke development database isolation must not resolve to the active development database.");
        }

        Directory.CreateDirectory(fullIsolatedRoot);
        return fullIsolatedRoot;
    }
}

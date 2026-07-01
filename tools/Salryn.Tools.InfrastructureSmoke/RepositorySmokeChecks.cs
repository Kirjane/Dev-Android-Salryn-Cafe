using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Security;

internal static class RepositorySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== REPOSITORY SMOKE CHECKS =====");

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);

        EnsureDatabaseInitialized(projectRoot);

        Guid adminUserId = EnsureBootstrapAdmin(connectionFactory);

        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        CheckCatalogRepositories(repositoryFactory);
        CheckProductRepositories(repositoryFactory);
        CheckSalesRepositories(repositoryFactory);
        CheckInventoryRepositories(repositoryFactory);
        CheckSystemRepositories(repositoryFactory, adminUserId);

        Console.WriteLine("Repository smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void EnsureDatabaseInitialized(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        if (!File.Exists(orchestrator.DatabasePath))
        {
            DatabaseInitializationSummary summary = RequireSuccess(
                orchestrator.InitializeDatabase(),
                "RepositorySmoke.InitializeDatabase");

            Console.WriteLine($"Initialized smoke database: {summary.DatabasePath}");
            return;
        }

        DatabaseInitializationStatus status = RequireSuccess(
            orchestrator.CheckStatus(),
            "RepositorySmoke.CheckStatus");

        if (!status.FirstRunStatus.IsDatabaseInitialized || status.RequiresDatabaseUpgrade)
        {
            DatabaseInitializationSummary summary = RequireSuccess(
                orchestrator.InitializeDatabase(),
                "RepositorySmoke.InitializeExistingDatabase");

            Console.WriteLine($"Initialized existing smoke database: {summary.DatabasePath}");
        }
    }

    private static Guid EnsureBootstrapAdmin(SqliteConnectionFactory connectionFactory)
    {
        FirstRunBootstrapper bootstrapper = new(
            connectionFactory,
            new Pbkdf2PasswordHasher());

        FirstRunStatus status = RequireSuccess(
            bootstrapper.CheckStatus(),
            "RepositorySmoke.BootstrapStatus");

        if (status.IsFirstRunRequired)
        {
            AdminBootstrapRequest request = new(
                StoreName: "Smoke Test Store",
                BusinessName: "Smoke Test Business",
                Address: "Temporary smoke test address",
                ContactNumber: "00000000000",
                Tin: null,
                ReceiptWidthMm: 80,
                AdminUsername: "admin-smoke",
                AdminDisplayName: "Smoke Test Admin",
                AdminPassword: "StrongSmoke123!");

            AdminBootstrapSummary summary = RequireSuccess(
                bootstrapper.CreateFirstAdmin(request),
                "RepositorySmoke.CreateFirstAdmin");

            Console.WriteLine($"Bootstrap admin created: {summary.AdminUserId}");
            return summary.AdminUserId;
        }

        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        UserReadModel? admin = RequireSuccess(
            userRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "RepositorySmoke.FindAdminSmoke");

        if (admin is not null)
        {
            Console.WriteLine($"Bootstrap admin already exists: {admin.Id}");
            return admin.Id;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Repository smoke expected at least one user after first-run completion.");
        }

        Console.WriteLine($"Using existing user for repository checks: {users[0].Id}");
        return users[0].Id;
    }

    private static void CheckCatalogRepositories(SqliteRepositoryFactory repositoryFactory)
    {
        ICatalogReadRepository catalogRepository =
            repositoryFactory.CreateCatalogReadRepository();

        IReadOnlyList<CategoryReadModel> categories = RequireSuccess(
            catalogRepository.GetCategoriesAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.GetCategories");

        IReadOnlyList<BrandReadModel> brands = RequireSuccess(
            catalogRepository.GetBrandsAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.GetBrands");

        IReadOnlyList<UnitReadModel> units = RequireSuccess(
            catalogRepository.GetUnitsAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.GetUnits");

        IReadOnlyList<SupplierReadModel> suppliers = RequireSuccess(
            catalogRepository.GetSuppliersAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.GetSuppliers");

        if (categories.Count < 1)
        {
            throw new InvalidOperationException("Catalog repository category seed check failed.");
        }

        if (brands.Count < 1)
        {
            throw new InvalidOperationException("Catalog repository brand seed check failed.");
        }

        if (units.Count < 1)
        {
            throw new InvalidOperationException("Catalog repository unit seed check failed.");
        }

        Console.WriteLine($"Catalog repositories: OK ({categories.Count} categories, {brands.Count} brands, {units.Count} units, {suppliers.Count} suppliers)");
    }

    private static void CheckProductRepositories(SqliteRepositoryFactory repositoryFactory)
    {
        IProductReadRepository productRepository =
            repositoryFactory.CreateProductReadRepository();

        ProductSearchFilter filter = new(
            SearchText: null,
            CategoryId: null,
            BrandId: null,
            IsActive: true,
            IncludeInactive: false,
            Page: PageRequest.FirstPage(20));

        PagedResult<ProductListItemReadModel> productPage = RequireSuccess(
            productRepository.SearchAsync(filter).GetAwaiter().GetResult(),
            "RepositorySmoke.ProductSearch");

        ProductLookupReadModel? missingProduct = RequireSuccess(
            productRepository.FindByIdAsync(Guid.NewGuid()).GetAwaiter().GetResult(),
            "RepositorySmoke.ProductFindMissingId");

        IReadOnlyList<LowStockProductReadModel> lowStock = RequireSuccess(
            productRepository.GetLowStockAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.ProductLowStock");

        if (missingProduct is not null)
        {
            throw new InvalidOperationException("Product repository random missing ID check unexpectedly returned a product.");
        }

        Console.WriteLine($"Product repositories: OK ({productPage.TotalCount} products, {lowStock.Count} low-stock)");
    }

    private static void CheckSalesRepositories(SqliteRepositoryFactory repositoryFactory)
    {
        ISaleReadRepository saleRepository =
            repositoryFactory.CreateSaleReadRepository();

        SaleHistoryFilter filter = new(
            From: null,
            To: null,
            CashierUserId: null,
            Status: null,
            ReceiptNo: null,
            Page: PageRequest.FirstPage(20));

        PagedResult<SaleHistoryItemReadModel> sales = RequireSuccess(
            saleRepository.SearchSalesAsync(filter).GetAwaiter().GetResult(),
            "RepositorySmoke.SaleSearch");

        SaleDetailReadModel? missingSale = RequireSuccess(
            saleRepository.FindSaleByReceiptNoAsync("SLRN-2099-NOPE").GetAwaiter().GetResult(),
            "RepositorySmoke.SaleFindMissingReceipt");

        if (missingSale is not null)
        {
            throw new InvalidOperationException("Sale repository missing receipt check unexpectedly returned a sale.");
        }

        Console.WriteLine($"Sales repositories: OK ({sales.TotalCount} sales)");
    }

    private static void CheckInventoryRepositories(SqliteRepositoryFactory repositoryFactory)
    {
        IInventoryReadRepository inventoryRepository =
            repositoryFactory.CreateInventoryReadRepository();

        StockMovementFilter filter = new(
            ProductId: null,
            MovementType: null,
            From: null,
            To: null,
            Page: PageRequest.FirstPage(20));

        PagedResult<StockMovementReadModel> movements = RequireSuccess(
            inventoryRepository.SearchStockMovementsAsync(filter).GetAwaiter().GetResult(),
            "RepositorySmoke.StockMovementSearch");

        IReadOnlyList<LowStockProductReadModel> lowStock = RequireSuccess(
            inventoryRepository.GetLowStockProductsAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.InventoryLowStock");

        Console.WriteLine($"Inventory repositories: OK ({movements.TotalCount} movements, {lowStock.Count} low-stock)");
    }

    private static void CheckSystemRepositories(
        SqliteRepositoryFactory repositoryFactory,
        Guid adminUserId)
    {
        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        IStoreSettingsReadRepository storeSettingsRepository =
            repositoryFactory.CreateStoreSettingsReadRepository();

        ICashierSessionReadRepository sessionRepository =
            repositoryFactory.CreateCashierSessionReadRepository();

        IAuditReadRepository auditRepository =
            repositoryFactory.CreateAuditReadRepository();

        IErrorLogReadRepository errorLogRepository =
            repositoryFactory.CreateErrorLogReadRepository();

        UserReadModel? adminById = RequireSuccess(
            userRepository.FindByIdAsync(adminUserId).GetAwaiter().GetResult(),
            "RepositorySmoke.UserFindById");

        UserReadModel? adminByUsername = RequireSuccess(
            userRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "RepositorySmoke.UserFindByUsername");

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.UserGetAll");

        StoreSettingsReadModel? storeSettings = RequireSuccess(
            storeSettingsRepository.GetCurrentAsync().GetAwaiter().GetResult(),
            "RepositorySmoke.StoreSettingsGetCurrent");

        CashierSessionReadModel? openSession = RequireSuccess(
            sessionRepository.FindOpenSessionByUserIdAsync(adminUserId).GetAwaiter().GetResult(),
            "RepositorySmoke.CashierSessionFindOpen");

        PagedResult<AuditLogReadModel> auditLogs = RequireSuccess(
            auditRepository.SearchAuditLogsAsync(
                new AuditLogSearchFilter(
                    PageRequest.FirstPage(20),
                    From: null,
                    To: null,
                    Search: new SearchText(null))).GetAwaiter().GetResult(),
            "RepositorySmoke.AuditSearch");

        PagedResult<ErrorLogReadModel> errorLogs = RequireSuccess(
            errorLogRepository.SearchErrorLogsAsync(
                new ErrorLogSearchFilter(
                    PageRequest.FirstPage(20),
                    From: null,
                    To: null,
                    Search: new SearchText(null))).GetAwaiter().GetResult(),
            "RepositorySmoke.ErrorSearch");

        if (adminById is null)
        {
            throw new InvalidOperationException("User repository find by ID check failed.");
        }

        if (adminByUsername is null)
        {
            throw new InvalidOperationException("User repository find by username check failed.");
        }

        if (users.Count < 1)
        {
            throw new InvalidOperationException("User repository get all check failed.");
        }

        if (storeSettings is null)
        {
            throw new InvalidOperationException("Store settings repository check failed.");
        }

        if (openSession is not null)
        {
            throw new InvalidOperationException("Cashier session repository should not find an open session in smoke bootstrap.");
        }

        if (auditLogs.TotalCount < 1)
        {
            throw new InvalidOperationException("Audit repository expected at least one bootstrap audit log.");
        }

        Console.WriteLine($"System repositories: OK ({users.Count} users, store '{storeSettings.StoreName}', {auditLogs.TotalCount} audit logs, {errorLogs.TotalCount} error logs)");
    }

    private static T RequireSuccess<T>(
        Result<T> result,
        string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }
}

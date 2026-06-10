using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.App.WinForms.Composition;

public sealed class SalrynAppServices
{
    private SalrynAppServices(
        DbPaths dbPaths,
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializationOrchestrator databaseInitialization,
        SqliteRepositoryFactory repositories,
        IProductReadRepository productReadRepository,
        ICheckoutProductSearchBoundary checkoutProductSearch,
        ICashierSessionService cashierSessions,
        IPosCheckoutService posCheckout,
        IPosCheckoutApplicationService posCheckoutApplication)
    {
        DbPaths = dbPaths;
        ConnectionFactory = connectionFactory;
        DatabaseInitialization = databaseInitialization;
        Repositories = repositories;
        ProductReadRepository = productReadRepository;
        CheckoutProductSearch = checkoutProductSearch;
        CashierSessions = cashierSessions;
        PosCheckout = posCheckout;
        PosCheckoutApplication = posCheckoutApplication;
    }

    public DbPaths DbPaths { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public DatabaseInitializationOrchestrator DatabaseInitialization { get; }

    public SqliteRepositoryFactory Repositories { get; }

    public IProductReadRepository ProductReadRepository { get; }

    public ICheckoutProductSearchBoundary CheckoutProductSearch { get; }

    public ICashierSessionService CashierSessions { get; }

    public IPosCheckoutService PosCheckout { get; }

    public IPosCheckoutApplicationService PosCheckoutApplication { get; }

    public static SalrynAppServices CreateProduction()
    {
        return Create(DbPaths.CreateProduction());
    }

    public static SalrynAppServices CreateDevelopment(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        return Create(DbPaths.CreateDevelopment(projectRoot));
    }

    public static SalrynAppServices Create(DbPaths dbPaths)
    {
        ArgumentNullException.ThrowIfNull(dbPaths);

        SqliteConnectionFactory connectionFactory = new(dbPaths);

        DatabaseInitializationOrchestrator databaseInitialization =
            DefaultDatabaseInitializationFactory.Create(dbPaths);

        SqliteRepositoryFactory repositories = new(connectionFactory);

        IProductReadRepository productReadRepository =
            repositories.CreateProductReadRepository();

        ICheckoutProductSearchBoundary checkoutProductSearch =
            new CheckoutProductRepositorySearchBoundary(productReadRepository);

        ICashierSessionService cashierSessions = new CashierSessionService(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        IPosCheckoutService posCheckout = new PosCheckoutService(
            repositories.CreateSaleWriteRepository(),
            cashierSessions);

        IPosCheckoutApplicationService posCheckoutApplication =
            new PosCheckoutApplicationService(posCheckout);

        return new SalrynAppServices(
            dbPaths,
            connectionFactory,
            databaseInitialization,
            repositories,
            productReadRepository,
            checkoutProductSearch,
            cashierSessions,
            posCheckout,
            posCheckoutApplication);
    }
}

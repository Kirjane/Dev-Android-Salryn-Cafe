using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Deployment;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;
using Salryn.Host.Local.Composition;

namespace Salryn.App.WinForms.Composition;

// Kirch: This composition root exposes app-level services so WinForms screens do not create infrastructure directly.
public sealed class SalrynAppServices
{
    // Kirch: The private constructor forces the app to build services through the controlled factory methods below.
    private SalrynAppServices(
        DbPaths dbPaths,
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializationOrchestrator databaseInitialization,
        SqliteRepositoryFactory repositories,
        IUserReadRepository userReadRepository,
        IUserPermissionReadRepository userPermissionReadRepository,
        IAuthenticationService authentication,
        IProductReadRepository productReadRepository,
        IProductWriteRepository productWriteRepository,
        ICatalogReadRepository catalogReadRepository,
        ICheckoutProductSearchBoundary checkoutProductSearch,
        ICashierSessionService cashierSessions,
        IPosCheckoutService posCheckout,
        IPosCheckoutApplicationService posCheckoutApplication,
        // Kirch: Passive ProductLookup gateway boundary; kept separate from holder-aware wiring.
        IAppLocalHostProductLookupGateway localHostProductLookupGateway,
        StoreDeploymentProfile localHostProfile)
    {
        DbPaths = dbPaths;
        ConnectionFactory = connectionFactory;
        DatabaseInitialization = databaseInitialization;
        Repositories = repositories;
        UserReadRepository = userReadRepository;
        UserPermissionReadRepository = userPermissionReadRepository;
        Authentication = authentication;
        ProductReadRepository = productReadRepository;
        ProductWriteRepository = productWriteRepository;
        CatalogReadRepository = catalogReadRepository;
        CheckoutProductSearch = checkoutProductSearch;
        CashierSessions = cashierSessions;
        PosCheckout = posCheckout;
        PosCheckoutApplication = posCheckoutApplication;
        // Kirch: Stores the inert gateway only; this does not consume the holder, dispatch, start runtime, or add transport.
        LocalHostProductLookupGateway = localHostProductLookupGateway;
        LocalHostProductLookupCompositionHolder =
            CreateLocalHostProductLookupCompositionHolder(localHostProfile);
    }

    public DbPaths DbPaths { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public DatabaseInitializationOrchestrator DatabaseInitialization { get; }

    public SqliteRepositoryFactory Repositories { get; }

    public IUserReadRepository UserReadRepository { get; }

    public IUserPermissionReadRepository UserPermissionReadRepository { get; }

    public IAuthenticationService Authentication { get; }

    public IProductReadRepository ProductReadRepository { get; }

    public IProductWriteRepository ProductWriteRepository { get; }

    public ICatalogReadRepository CatalogReadRepository { get; }

    public ICheckoutProductSearchBoundary CheckoutProductSearch { get; }

    public ICashierSessionService CashierSessions { get; }

    public IPosCheckoutService PosCheckout { get; }

    public IPosCheckoutApplicationService PosCheckoutApplication { get; }

    public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }

    // Kirch: Read-only passive gateway exposure for future ProductLookup wiring; not connected to UI behavior yet.
    public IAppLocalHostProductLookupGateway LocalHostProductLookupGateway { get; }

    // Kirch: Production mode uses the real local app database paths.
    public static SalrynAppServices CreateProduction()
    {
        return Create(DbPaths.CreateProduction());
    }

    // Kirch: Development mode uses the project-root database paths for safer local testing.
    public static SalrynAppServices CreateDevelopment(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        return Create(DbPaths.CreateDevelopment(projectRoot));
    }

    // Kirch: Creates the local standalone Host profile used only for stopped startup composition wiring.
    private static StoreDeploymentProfile CreateStandaloneLocalHostProfile()
    {
        return new StoreDeploymentProfile(
            new StoreIdentity(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new StoreHostIdentity(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Environment.MachineName,
                SalrynMachineRole.StandaloneHost),
            SalrynDeploymentMode.Standalone,
            Endpoint: null,
            Terminal: null);
    }

    // Kirch: Builds a stopped Local Host ProductLookup composition through the app composition root only.
    public LocalHostBootstrapCompositionResult CreateLocalHostProductLookupComposition(StoreDeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        LocalHostBootstrapCompositionResult result =
            CreateUncheckedLocalHostProductLookupComposition(profile);

        if (!LocalHostBootstrapCompositionRules.IsCompositionValid(result))
        {
            throw new InvalidOperationException("Invalid app Local Host ProductLookup composition.");
        }

        return result;
    }

    // Kirch: Creates a holder for guarded Local Host ProductLookup composition without starting runtime behavior.
    public SalrynLocalHostCompositionHolder CreateLocalHostProductLookupCompositionHolder(StoreDeploymentProfile profile)
    {
        LocalHostBootstrapCompositionResult composition =
            CreateLocalHostProductLookupComposition(profile);

        return new SalrynLocalHostCompositionHolder(composition);
    }
    // Kirch: Keeps raw Host.Local composition isolated so the public app method can guard the result.
    private LocalHostBootstrapCompositionResult CreateUncheckedLocalHostProductLookupComposition(StoreDeploymentProfile profile)
    {
        LocalHostBootstrapComposer composer = new();

        return composer.ComposeWithProductLookup(
            profile,
            ProductReadRepository);
    }
    // Kirch: This method wires repositories and services once, then passes them into the app shell.
    public static SalrynAppServices Create(DbPaths dbPaths)
    {
        ArgumentNullException.ThrowIfNull(dbPaths);

        SqliteConnectionFactory connectionFactory = new(dbPaths);

        DatabaseInitializationOrchestrator databaseInitialization =
            DefaultDatabaseInitializationFactory.Create(dbPaths);

        SqliteRepositoryFactory repositories = new(connectionFactory);

        IUserReadRepository userReadRepository =
            repositories.CreateUserReadRepository();

        IUserPermissionReadRepository userPermissionReadRepository =
            repositories.CreateUserPermissionReadRepository();

        IAuthenticationService authentication = new AuthenticationService(
            repositories.CreateAuthenticationRepository(),
            userPermissionReadRepository);

        IProductReadRepository productReadRepository =
            repositories.CreateProductReadRepository();

        IProductWriteRepository productWriteRepository =
            repositories.CreateProductWriteRepository();

        ICatalogReadRepository catalogReadRepository =
            repositories.CreateCatalogReadRepository();

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
            userReadRepository,
            userPermissionReadRepository,
            authentication,
            productReadRepository,
            productWriteRepository,
            catalogReadRepository,
            checkoutProductSearch,
            cashierSessions,
            posCheckout,
            posCheckoutApplication,
            // Kirch: Creates only the inert adapter shell; holder-aware lookup remains a later approved phase.
            new CompositionLocalHostProductLookupGatewayAdapter(),
            CreateStandaloneLocalHostProfile());
    }
}

using Salryn.App.WinForms.Features.Checkout;
using Salryn.Core.Deployment;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Host.Local.Composition;

namespace Salryn.App.WinForms.Composition;

// Kirch file guide: WinForms composition root.
// UI location: every real screen receives services from here instead of constructing repositories.
// Frontend note: safe UI work should consume AppServices through presenters/forms, not bypass it.

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
        ISaleReadRepository saleReadRepository,
        IInventoryReadRepository inventoryReadRepository,
        IInventoryWriteRepository inventoryWriteRepository,
        IStoreSettingsReadRepository storeSettingsReadRepository,
        ICashierSessionService cashierSessions,
        IPosCheckoutService posCheckout,
        IPosCheckoutApplicationService posCheckoutApplication,
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
        // Kirch: June 20 MVP screens use these read/write services through AppServices, not direct repository construction.
        SaleReadRepository = saleReadRepository;
        InventoryReadRepository = inventoryReadRepository;
        InventoryWriteRepository = inventoryWriteRepository;
        StoreSettingsReadRepository = storeSettingsReadRepository;
        CashierSessions = cashierSessions;
        PosCheckout = posCheckout;
        PosCheckoutApplication = posCheckoutApplication;
        // Kirch: Creates the passive holder before gateway wiring; this still does not start runtime, dispatch, or add transport.
        LocalHostProductLookupCompositionHolder =
            CreateLocalHostProductLookupCompositionHolder(localHostProfile);

        // Kirch: Wires passive Local Host ownership shells only; activation and request consumption remain blocked.
        LocalHostRuntimeLifecycleHolder =
            new SalrynLocalHostRuntimeLifecycleHolder(LocalHostProductLookupCompositionHolder);

        LocalHostRuntimeStartupResult =
            LocalHostRuntimeLifecycleHolder.TryStart(DateTimeOffset.UtcNow);

        if (!LocalHostRuntimeStartupResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Local Host runtime failed to start during app service composition: {LocalHostRuntimeStartupResult.Message}");
        }
        LocalHostRequestContextProvider =
            new SalrynLocalHostRequestContextProvider(localHostProfile);
        LocalHostSessionIdentityProvider =
            new SalrynLocalHostSessionIdentityProvider();
        // Kirch: Wires the holder-aware adapter through AppServices only; runtime start, Program, Shell, UI, and transport remain blocked.
        LocalHostProductLookupGateway =
            new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);

        // Kirch: 66G6 routes checkout product search through the Local Host-backed boundary while keeping Shell checkout routing stable.
        CheckoutProductSearch =
            new CheckoutLocalHostProductSearchBoundary(LocalHostProductLookupGateway);

        // Kirch: why - official mode UI must read the same backend readiness gate that protects official persistence.
        SqliteOfficialCheckoutReadinessService officialReadinessService =
            new(connectionFactory);

        CheckoutOfficialModeReadiness =
            new CheckoutOfficialModeReadinessBoundary(officialReadinessService);

        // Kirch: why - official completion uses trusted sale building, readiness, sequence, sale, and journal persistence as one app-owned boundary.
        OfficialCheckoutCompletion =
            new OfficialCheckoutCompletionBoundary(
                new TrustedPosCheckoutSaleBuilder(productReadRepository),
                cashierSessions,
                officialReadinessService,
                new SqliteOfficialCheckoutPersistenceBoundary(
                    new SqliteOfficialCheckoutPersistenceService(connectionFactory)));

        // Kirch: why - official evidence reads must come from journal snapshot evidence, not checkout UI receipt text.
        OfficialEvidenceRead =
            new SqliteOfficialEvidenceReadService(connectionFactory);

        // Kirch: why - X-reading stays read-only by consuming official evidence export instead of writing reports or counters.
        OfficialXReading =
            new OfficialXReadingService(OfficialEvidenceRead);

        // Kirch: why - Z-reading finalizes X-reading totals without touching original sales, sequence, stock, or journal evidence.
        OfficialZReading =
            new OfficialZReadingService(
                OfficialXReading,
                new SqliteOfficialZReadingService(connectionFactory));

        // Kirch: why - official void/cancel/correction remains a backend-only append event boundary, not a source-row rewrite.
        OfficialDocumentAuditEvents =
            new OfficialDocumentAuditEventService(
                new SqliteOfficialDocumentAuditEventService(connectionFactory));

        // Kirch: why - lifecycle state must be read from official journal evidence and events before UI or reports consume it.
        OfficialDocumentLifecycleRead =
            new SqliteOfficialDocumentLifecycleReadService(connectionFactory);
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

    // Kirch: Sales History and receipt preview MVP screens read completed sales through this repository.
    public ISaleReadRepository SaleReadRepository { get; }

    // Kirch: Inventory and dashboard MVP screens read low-stock data and stock movement history through this repository.
    public IInventoryReadRepository InventoryReadRepository { get; }

    // Kirch: Minimal stock-in/adjustment MVP flows write inventory movements through this repository.
    public IInventoryWriteRepository InventoryWriteRepository { get; }

    // Kirch: Store Settings and receipt header MVP screens read store profile fields through this repository.
    public IStoreSettingsReadRepository StoreSettingsReadRepository { get; }

    public ICheckoutProductSearchBoundary CheckoutProductSearch { get; }

    public ICheckoutOfficialModeReadinessBoundary CheckoutOfficialModeReadiness { get; }

    public IOfficialCheckoutCompletionBoundary OfficialCheckoutCompletion { get; }

    public IOfficialEvidenceReadService OfficialEvidenceRead { get; }

    public IOfficialXReadingService OfficialXReading { get; }

    public IOfficialZReadingService OfficialZReading { get; }

    public IOfficialDocumentAuditEventService OfficialDocumentAuditEvents { get; }

    public IOfficialDocumentLifecycleReadService OfficialDocumentLifecycleRead { get; }

    public ICashierSessionService CashierSessions { get; }

    public IPosCheckoutService PosCheckout { get; }

    public IPosCheckoutApplicationService PosCheckoutApplication { get; }

    public SalrynLocalHostCompositionHolder LocalHostProductLookupCompositionHolder { get; }

    // Kirch: Passive Local Host ownership shells exposed for future controlled activation/context phases.
    public SalrynLocalHostRuntimeLifecycleHolder LocalHostRuntimeLifecycleHolder { get; }

    public SalrynLocalHostRuntimeLifecycleResult LocalHostRuntimeStartupResult { get; }

    public SalrynLocalHostRequestContextProvider LocalHostRequestContextProvider { get; }

    public SalrynLocalHostSessionIdentityProvider LocalHostSessionIdentityProvider { get; }

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
            Terminal: new TerminalIdentity(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "standalone-local-terminal",
                Environment.MachineName,
                SalrynMachineRole.StandaloneHost));
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

        // Kirch: June 20 MVP backend exposure for Sales History, Inventory, Dashboard, and Store Settings screens.
        ISaleReadRepository saleReadRepository =
            repositories.CreateSaleReadRepository();

        IInventoryReadRepository inventoryReadRepository =
            repositories.CreateInventoryReadRepository();

        IInventoryWriteRepository inventoryWriteRepository =
            repositories.CreateInventoryWriteRepository();

        IStoreSettingsReadRepository storeSettingsReadRepository =
            repositories.CreateStoreSettingsReadRepository();

        ICashierSessionService cashierSessions = new CashierSessionService(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        IPosCheckoutService posCheckout = new PosCheckoutService(
            repositories.CreateSaleWriteRepository(),
            cashierSessions);

        // Kirch: Checkout completion must re-read product facts before committing a sale.
        IPosCheckoutApplicationService posCheckoutApplication =
            new PosCheckoutApplicationService(posCheckout, productReadRepository);

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
            saleReadRepository,
            inventoryReadRepository,
            inventoryWriteRepository,
            storeSettingsReadRepository,
            cashierSessions,
            posCheckout,
            posCheckoutApplication,
            CreateStandaloneLocalHostProfile());
    }
}

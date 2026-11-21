using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Repositories.Menu;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Paths;

namespace Salryn.Mobile.Runtime;

/// <summary>
/// Mobile-safe in-process production composition. This is the Android/local
/// operational authority and deliberately has no dependency on Salryn.Api.Local,
/// ASP.NET/Kestrel, desktop UI, printing, or PDF/reporting assemblies.
/// </summary>
public sealed class MobileProductionComposition
{
    private static readonly string[] RequiredTables =
    [
        "sales",
        "payments",
        "stock_movements",
        "cashier_sessions",
        "customers",
        "accounts_receivable_entries",
        "accounts_receivable_collections",
        "cashier_session_reports",
        "cash_movements",
        "sync_device_state",
        "sync_outbox",
        "sync_remote_state",
        "sync_change_inbox",
        "menu_item_availability",
        "menu_variants",
        "menu_modifier_groups",
        "menu_modifier_options",
        "menu_item_modifier_groups",
        "menu_variant_modifier_groups",
        "menu_revisions",
        "menu_revision_categories",
        "menu_revision_items",
        "menu_revision_variants",
        "menu_revision_modifier_groups",
        "menu_revision_modifier_options",
        "menu_revision_item_modifier_groups",
        "menu_revision_variant_modifier_groups",
        "menu_current_publication",
        "ingredients",
        "recipes",
        "recipe_lines",
        "recipe_item_bindings",
        "recipe_variant_bindings",
        "recipe_modifier_option_bindings",
        "recipe_book_revisions",
        "recipe_book_revision_ingredients",
        "recipe_book_revision_recipes",
        "recipe_book_revision_lines",
        "recipe_book_revision_bindings",
        "recipe_book_current_publication",
        "sale_ingredient_consumption_decisions",
        "sale_ingredient_consumption_lines",
        "ingredient_stock_movements",
        "cafe_ingredient_bootstrap_state",
        "supplier_directory_revisions",
        "supplier_directory_revision_items",
        "supplier_directory_current_publication",
        "purchase_receipts",
        "purchase_receipt_lines",
        "inventory_waste_events",
        "inventory_waste_lines",
        "inventory_adjustment_events",
        "inventory_count_sessions",
        "inventory_count_lines",
        "cafe_inventory_bootstrap_state",
        "cafe_orders",
        "cafe_order_revisions",
        "cafe_order_revision_lines",
        "cafe_order_revision_modifiers",
        "cafe_order_operations",
        "cafe_order_lifecycle_events",
        "cafe_order_sale_links",
        "sale_item_order_links",
        "cafe_order_bootstrap_state",
        "cafe_sale_financial_decisions",
        "cafe_sale_financial_line_discounts",
        "cafe_sale_fees",
        "cafe_sale_refunds",
        "cafe_sale_refund_lines",
        "cafe_sale_refund_operations",
        "cafe_sale_refund_events",
        "cafe_sale_refund_settlements",
        "cafe_financial_bootstrap_state",
        "cafe_cash_operations",
        "cafe_cash_bootstrap_state",
        "cafe_printer_stations",
        "cafe_print_jobs",
        "cafe_print_attempts",
        "cafe_delivery_fulfillments",
        "cafe_delivery_revisions",
        "cafe_delivery_operations",
        "cafe_delivery_events",
        "cafe_delivery_bootstrap_state",
        "cafe_bookings",
        "cafe_booking_revisions",
        "cafe_booking_revision_lines",
        "cafe_booking_revision_modifier_selections",
        "cafe_booking_operations",
        "cafe_booking_events",
        "cafe_booking_deposit_facts",
        "cafe_booking_deposit_allocations",
        "cafe_booking_order_links",
        "cafe_booking_sale_links",
        "cafe_booking_bootstrap_state",
        "cafe_attendance_events",
        "cafe_attendance_corrections",
        "cafe_incidents",
        "cafe_incident_revisions",
        "cafe_incident_operations",
        "cafe_incident_resolutions",
        "cafe_attendance_incident_bootstrap_state"
    ];

    private static readonly string[] RequiredTriggers =
    [
        "trg_cafe_mutation_idempotency_no_update",
        "trg_cafe_mutation_idempotency_no_delete",
        "trg_cashier_session_reports_no_update",
        "trg_cashier_session_reports_no_delete",
        "trg_cash_movements_no_update",
        "trg_cash_movements_no_delete",
        "trg_sales_session_must_be_open_on_insert",
        "trg_receivable_cash_collection_requires_open_session",
        "trg_payments_complete_sale_posts_cash_movement",
        "trg_sync_outbox_immutable_envelope",
        "trg_sync_outbox_no_delete",
        "trg_sync_change_inbox_immutable_evidence",
        "trg_sync_change_inbox_no_delete",
        "trg_menu_revisions_immutable_identity",
        "trg_menu_revisions_no_delete",
        "trg_menu_revision_categories_no_update",
        "trg_menu_revision_categories_no_delete",
        "trg_menu_revision_items_no_update",
        "trg_menu_revision_items_no_delete",
        "trg_menu_revision_variants_no_update",
        "trg_menu_revision_variants_no_delete",
        "trg_menu_revision_modifier_groups_no_update",
        "trg_menu_revision_modifier_groups_no_delete",
        "trg_menu_revision_modifier_options_no_update",
        "trg_menu_revision_modifier_options_no_delete",
        "trg_menu_revision_item_modifier_groups_no_update",
        "trg_menu_revision_item_modifier_groups_no_delete",
        "trg_menu_revision_variant_modifier_groups_no_update",
        "trg_menu_revision_variant_modifier_groups_no_delete",
        "trg_recipe_book_revisions_immutable_identity",
        "trg_recipe_book_revisions_no_delete",
        "trg_recipe_book_revision_ingredients_no_update",
        "trg_recipe_book_revision_ingredients_no_delete",
        "trg_recipe_book_revision_recipes_no_update",
        "trg_recipe_book_revision_recipes_no_delete",
        "trg_recipe_book_revision_lines_no_update",
        "trg_recipe_book_revision_lines_no_delete",
        "trg_recipe_book_revision_bindings_no_update",
        "trg_recipe_book_revision_bindings_no_delete",
        "trg_sale_ingredient_consumption_decisions_no_update",
        "trg_sale_ingredient_consumption_decisions_no_delete",
        "trg_sale_ingredient_consumption_lines_no_update",
        "trg_sale_ingredient_consumption_lines_no_delete",
        "trg_ingredient_stock_movements_no_update",
        "trg_ingredient_stock_movements_no_delete",
        "trg_supplier_directory_revisions_no_update",
        "trg_supplier_directory_revisions_no_delete",
        "trg_supplier_directory_revision_items_no_update",
        "trg_supplier_directory_revision_items_no_delete",
        "trg_purchase_receipts_no_update",
        "trg_purchase_receipts_no_delete",
        "trg_purchase_receipt_lines_no_update",
        "trg_purchase_receipt_lines_no_delete",
        "trg_inventory_waste_events_no_update",
        "trg_inventory_waste_events_no_delete",
        "trg_inventory_adjustment_events_no_update",
        "trg_inventory_adjustment_events_no_delete",
        "trg_inventory_count_sessions_finalized_no_update",
        "trg_inventory_count_lines_finalized_no_update",
        "trg_cafe_order_revisions_no_update",
        "trg_cafe_order_revisions_no_delete",
        "trg_cafe_order_revision_lines_no_update",
        "trg_cafe_order_revision_lines_no_delete",
        "trg_cafe_order_revision_modifiers_no_update",
        "trg_cafe_order_revision_modifiers_no_delete",
        "trg_cafe_order_operations_no_update",
        "trg_cafe_order_operations_no_delete",
        "trg_cafe_order_lifecycle_events_no_update",
        "trg_cafe_order_lifecycle_events_no_delete",
        "trg_cafe_order_sale_links_no_update",
        "trg_cafe_order_sale_links_no_delete",
        "trg_sale_item_order_links_no_update",
        "trg_sale_item_order_links_no_delete",
        "trg_cafe_orders_terminal_guard",
        "trg_cafe_orders_no_delete",
        "trg_cafe_sale_financial_decisions_no_update",
        "trg_cafe_sale_financial_decisions_no_delete",
        "trg_cafe_sale_financial_line_discounts_no_update",
        "trg_cafe_sale_financial_line_discounts_no_delete",
        "trg_cafe_sale_fees_no_update",
        "trg_cafe_sale_fees_no_delete",
        "trg_cafe_sale_refund_lines_no_update",
        "trg_cafe_sale_refund_lines_no_delete",
        "trg_cafe_sale_refund_operations_no_update",
        "trg_cafe_sale_refund_operations_no_delete",
        "trg_cafe_sale_refund_events_no_update",
        "trg_cafe_sale_refund_events_no_delete",
        "trg_cafe_sale_refund_settlements_no_update",
        "trg_cafe_sale_refund_settlements_no_delete",
        "trg_cafe_sale_refunds_terminal_guard",
        "trg_cafe_sale_refunds_no_delete",
        "trg_c5_ar_refund_credit_shape",
        "trg_cafe_cash_operations_no_update",
        "trg_cafe_cash_operations_no_delete",
        "trg_c6_cash_operation_movement_match",
        "trg_c6_cash_operation_source_owned_guard",
        "trg_cafe_printer_stations_no_delete",
        "trg_cafe_printer_stations_versioned_update",
        "trg_cafe_print_jobs_no_update",
        "trg_cafe_print_jobs_no_delete",
        "trg_cafe_print_attempts_no_update",
        "trg_cafe_print_attempts_no_delete",
        "trg_cafe_delivery_insert_order_guard",
        "trg_cafe_delivery_terminal_guard",
        "trg_cafe_delivery_version_guard",
        "trg_cafe_delivery_no_delete",
        "trg_cafe_delivery_revisions_no_update",
        "trg_cafe_delivery_revisions_no_delete",
        "trg_cafe_delivery_operations_no_update",
        "trg_cafe_delivery_operations_no_delete",
        "trg_cafe_delivery_events_no_update",
        "trg_cafe_delivery_events_no_delete",
        "trg_cafe_bookings_terminal_guard",
        "trg_cafe_bookings_version_guard",
        "trg_cafe_bookings_no_delete",
        "trg_cafe_booking_revisions_no_update",
        "trg_cafe_booking_revisions_no_delete",
        "trg_cafe_booking_revision_lines_no_update",
        "trg_cafe_booking_revision_lines_no_delete",
        "trg_cafe_booking_revision_modifier_selections_no_update",
        "trg_cafe_booking_revision_modifier_selections_no_delete",
        "trg_cafe_booking_operations_no_update",
        "trg_cafe_booking_operations_no_delete",
        "trg_cafe_booking_events_no_update",
        "trg_cafe_booking_events_no_delete",
        "trg_cafe_booking_deposit_facts_no_update",
        "trg_cafe_booking_deposit_facts_no_delete",
        "trg_cafe_booking_deposit_allocations_no_update",
        "trg_cafe_booking_deposit_allocations_no_delete",
        "trg_cafe_booking_order_links_no_update",
        "trg_cafe_booking_order_links_no_delete",
        "trg_cafe_booking_sale_links_no_update",
        "trg_cafe_booking_sale_links_no_delete",
        "trg_cash_movements_booking_deposit_shape",
        "trg_cafe_attendance_events_no_update",
        "trg_cafe_attendance_events_no_delete",
        "trg_cafe_attendance_corrections_no_update",
        "trg_cafe_attendance_corrections_no_delete",
        "trg_cafe_incidents_terminal_guard",
        "trg_cafe_incidents_version_guard",
        "trg_cafe_incidents_no_delete",
        "trg_cafe_incident_revisions_no_update",
        "trg_cafe_incident_revisions_no_delete",
        "trg_cafe_incident_operations_no_update",
        "trg_cafe_incident_operations_no_delete",
        "trg_cafe_incident_resolutions_no_update",
        "trg_cafe_incident_resolutions_no_delete"
    ];

    private static readonly string[] RequiredIndexes =
    [
        "idx_cashier_sessions_one_open_per_user",
        "idx_sync_outbox_pending",
        "idx_sync_change_inbox_store_status_sequence",
        "idx_cash_movements_refund_cash",
        "idx_ar_entries_refund_credit",
        "idx_cash_movements_booking_deposit_source",
        "idx_cafe_bookings_store_status",
        "idx_cafe_booking_revisions_booking",
        "idx_cafe_booking_operations_booking",
        "idx_cafe_booking_events_booking",
        "idx_cafe_booking_deposit_facts_booking",
        "idx_cafe_attendance_store_subject_time",
        "idx_cafe_attendance_correction_event",
        "idx_cafe_incidents_store_status_time",
        "idx_cafe_incident_revisions_incident",
        "idx_cafe_incident_operations_incident"
    ];

    private static readonly string[] RequiredReportV2Columns =
    [
        "reconciliation_basis",
        "ledger_net_delta_cents",
        "sale_cash_receipt_cents",
        "receivable_cash_collection_cents",
        "cash_in_cents",
        "inventory_purchase_cents",
        "operational_expense_cents",
        "cash_refund_cents",
        "cash_out_cents",
        "reversal_cents"
    ];

    private MobileProductionComposition(
        DbPaths dbPaths,
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializationOrchestrator databaseInitialization,
        IAuthenticationService authentication,
        ICashierSessionService cashierSessions,
        IAccountsReceivableService accountsReceivable,
        ICafeRefundService cafeRefunds,
        ICafeCashService cafeCash,
        ICafePrintService cafePrint,
        ICafeDeliveryService cafeDelivery,
        ICafeBookingService cafeBooking,
        ICafeAttendanceIncidentService cafeAttendanceIncident,
        ICafeManagementReportingService cafeManagementReports,
        SqliteMenuCatalogStore menuCatalog,
        SqliteIngredientRecipeStore ingredientRecipes,
        SqliteSupplierDirectoryStore supplierDirectory,
        SqliteCafeInventoryStore cafeInventory,
        SqliteCafeOrderStore cafeOrders,
        SqliteCafeOrderCompletionCoordinator cafeOrderCompletion,
        SqliteDeploymentIdentityRepository deploymentIdentityRepository,
        SqliteSyncOutboxStore syncOutbox,
        SqliteSyncPullStore syncPull,
        SqliteSyncBootstrapStore syncBootstrap,
        SqliteCafeIngredientBootstrapStore cafeIngredientBootstrap,
        SqliteCafeInventoryBootstrapStore cafeInventoryBootstrap,
        SqliteCafeOrderBootstrapStore cafeOrderBootstrap,
        SqliteCafeFinancialBootstrapStore cafeFinancialBootstrap,
        SqliteCafeCashBootstrapStore cafeCashBootstrap,
        SqliteCafeDeliveryBootstrapStore cafeDeliveryBootstrap,
        SqliteCafeBookingBootstrapStore cafeBookingBootstrap,
        SqliteCafeAttendanceIncidentBootstrapStore cafeAttendanceIncidentBootstrap,
        SqliteSyncProjectionService syncProjection,
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
        CashierSessions = cashierSessions;
        AccountsReceivable = accountsReceivable;
        CafeRefunds = cafeRefunds;
        CafeCash = cafeCash;
        CafePrint = cafePrint;
        CafeDelivery = cafeDelivery;
        CafeBooking = cafeBooking;
        CafeAttendanceIncident = cafeAttendanceIncident;
        CafeManagementReports = cafeManagementReports;
        MenuCatalog = menuCatalog;
        IngredientRecipes = ingredientRecipes;
        SupplierDirectory = supplierDirectory;
        CafeInventory = cafeInventory;
        CafeOrders = cafeOrders;
        CafeOrderCompletion = cafeOrderCompletion;
        DeploymentIdentityRepository = deploymentIdentityRepository;
        SyncOutbox = syncOutbox;
        SyncPull = syncPull;
        SyncBootstrap = syncBootstrap;
        CafeIngredientBootstrap = cafeIngredientBootstrap;
        CafeInventoryBootstrap = cafeInventoryBootstrap;
        CafeOrderBootstrap = cafeOrderBootstrap;
        CafeFinancialBootstrap = cafeFinancialBootstrap;
        CafeCashBootstrap = cafeCashBootstrap;
        CafeDeliveryBootstrap = cafeDeliveryBootstrap;
        CafeBookingBootstrap = cafeBookingBootstrap;
        CafeAttendanceIncidentBootstrap = cafeAttendanceIncidentBootstrap;
        SyncProjection = syncProjection;
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

    public MobileOperatorCredentialEnrollment OperatorCredentials => new(ConnectionFactory);

    public SqliteCafeShiftStore CafeShifts => new(ConnectionFactory);

    public DatabaseInitializationOrchestrator DatabaseInitialization { get; }

    public IAuthenticationService Authentication { get; }

    public ICashierSessionService CashierSessions { get; }

    public IAccountsReceivableService AccountsReceivable { get; }

    public ICafeRefundService CafeRefunds { get; }

    public ICafeCashService CafeCash { get; }

    public ICafePrintService CafePrint { get; }

    public ICafeDeliveryService CafeDelivery { get; }

    public ICafeBookingService CafeBooking { get; }

    public ICafeAttendanceIncidentService CafeAttendanceIncident { get; }

    public ICafeManagementReportingService CafeManagementReports { get; }

    public SqliteMenuCatalogStore MenuCatalog { get; }

    public SqliteIngredientRecipeStore IngredientRecipes { get; }

    public SqliteSupplierDirectoryStore SupplierDirectory { get; }

    public SqliteCafeInventoryStore CafeInventory { get; }

    public SqliteCafeOrderStore CafeOrders { get; }

    public SqliteCafeOrderCompletionCoordinator CafeOrderCompletion { get; }

    public SqliteDeploymentIdentityRepository DeploymentIdentityRepository { get; }

    public SqliteSyncOutboxStore SyncOutbox { get; }

    public SqliteSyncPullStore SyncPull { get; }

    public SqliteSyncBootstrapStore SyncBootstrap { get; }

    public SqliteCafeIngredientBootstrapStore CafeIngredientBootstrap { get; }

    public SqliteCafeInventoryBootstrapStore CafeInventoryBootstrap { get; }

    public SqliteCafeOrderBootstrapStore CafeOrderBootstrap { get; }

    public SqliteCafeFinancialBootstrapStore CafeFinancialBootstrap { get; }

    public SqliteCafeCashBootstrapStore CafeCashBootstrap { get; }

    public SqliteCafeDeliveryBootstrapStore CafeDeliveryBootstrap { get; }

    public SqliteCafeBookingBootstrapStore CafeBookingBootstrap { get; }

    public SqliteCafeAttendanceIncidentBootstrapStore CafeAttendanceIncidentBootstrap { get; }

    public SqliteSyncProjectionService SyncProjection { get; }

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

    public static MobileProductionComposition Create(string appPrivateRoot)
    {
        if (string.IsNullOrWhiteSpace(appPrivateRoot))
        {
            throw new ArgumentException("An app-private Salryn root is required.", nameof(appPrivateRoot));
        }

        return Create(DbPaths.CreateAtRoot(appPrivateRoot));
    }

    public static MobileProductionComposition Create(DbPaths dbPaths)
    {
        ArgumentNullException.ThrowIfNull(dbPaths);

        SqliteConnectionFactory connectionFactory = new(dbPaths);
        DatabaseInitializationOrchestrator initialization =
            DefaultDatabaseInitializationFactory.Create(dbPaths);
        SqliteRepositoryFactory repositories = new(connectionFactory);

        IUserPermissionReadRepository permissions =
            repositories.CreateUserPermissionReadRepository();

        IAuthenticationService authentication = new AuthenticationService(
            repositories.CreateAuthenticationRepository(),
            permissions);

        ICashierSessionService cashierSessions = new CashierSessionService(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        IAccountsReceivableService accountsReceivable =
            new SqliteAccountsReceivableService(connectionFactory);

        return new MobileProductionComposition(
            dbPaths,
            connectionFactory,
            initialization,
            authentication,
            cashierSessions,
            accountsReceivable,
            new SqliteCafeRefundService(connectionFactory),
            new SqliteCafeCashService(connectionFactory),
            new SqliteCafePrintService(connectionFactory),
            new SqliteCafeDeliveryService(connectionFactory),
            new SqliteCafeBookingService(connectionFactory),
            new SqliteCafeAttendanceIncidentService(connectionFactory),
            new SqliteCafeManagementReportingService(connectionFactory),
            repositories.CreateMenuCatalogStore(),
            repositories.CreateIngredientRecipeStore(),
            new SqliteSupplierDirectoryStore(connectionFactory),
            new SqliteCafeInventoryStore(connectionFactory),
            new SqliteCafeOrderStore(connectionFactory),
            new SqliteCafeOrderCompletionCoordinator(connectionFactory),
            repositories.CreateDeploymentIdentityRepository(),
            new SqliteSyncOutboxStore(connectionFactory),
            new SqliteSyncPullStore(connectionFactory),
            new SqliteSyncBootstrapStore(connectionFactory),
            new SqliteCafeIngredientBootstrapStore(connectionFactory),
            new SqliteCafeInventoryBootstrapStore(connectionFactory),
            new SqliteCafeOrderBootstrapStore(connectionFactory),
            new SqliteCafeFinancialBootstrapStore(connectionFactory),
            new SqliteCafeCashBootstrapStore(connectionFactory),
            new SqliteCafeDeliveryBootstrapStore(connectionFactory),
            new SqliteCafeBookingBootstrapStore(connectionFactory),
            new SqliteCafeAttendanceIncidentBootstrapStore(connectionFactory),
            new SqliteSyncProjectionService(connectionFactory),
            repositories.CreateSaleReadRepository(),
            repositories.CreateSaleWriteRepository(),
            repositories.CreateProductReadRepository(),
            repositories.CreateProductWriteRepository(),
            repositories.CreateInventoryReadRepository(),
            repositories.CreateInventoryWriteRepository(),
            repositories.CreateCatalogReadRepository(),
            repositories.CreateUserReadRepository(),
            permissions,
            repositories.CreateAuditReadRepository(),
            repositories.CreateErrorLogReadRepository());
    }

    public Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<DatabaseInitializationSummary> result =
            DatabaseInitialization.InitializeDatabase();

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                result.ErrorDetail.Code + ": " + result.ErrorDetail.Message);
        }

        return Task.CompletedTask;
    }

    public MobileProductionReadiness InspectReadiness()
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = ConnectionFactory.DatabasePath, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        var lineage = SchemaLineageClassifier.EnsureAllowedForCafeMigration(connection);

        HashSet<string> tables = ReadNames(connection, "table");
        HashSet<string> triggers = ReadNames(connection, "trigger");
        HashSet<string> indexes = ReadNames(connection, "index");
        HashSet<string> reportColumns = ReadTableColumns(connection, "cashier_session_reports");

        int maxMigrationVersion = checked((int)ScalarLong(
            connection,
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;"));

        bool migration23Present = ScalarLong(
            connection,
            "SELECT COUNT(*) FROM schema_migrations WHERE version = 23;") != 0;

        bool hasP0ProofTables =
            tables.Contains("p0_business") ||
            tables.Contains("p0_evidence") ||
            tables.Contains("p0_outbox") || tables.Any(t => t.StartsWith("mobile_", StringComparison.OrdinalIgnoreCase));

        bool requiredSchemaPresent =
            RequiredTables.All(tables.Contains) &&
            SalrynDatabaseMigrationCatalog.GetCriticalTablesForMigrationVersion(CafeMigrationCatalog.CurrentCafeSchemaVersion).All(tables.Contains) &&
            RequiredTriggers.All(triggers.Contains) &&
            RequiredIndexes.All(indexes.Contains) &&
            RequiredReportV2Columns.All(reportColumns.Contains);

        bool ready =
            maxMigrationVersion == SalrynDatabaseMigrationCatalog.SupportedMigrationVersion &&
            migration23Present && lineage.Kind == SchemaLineageKind.SalrynCafe &&
            !hasP0ProofTables &&
            requiredSchemaPresent;

        return new MobileProductionReadiness(
            ConnectionFactory.DatabasePath,
            maxMigrationVersion,
            migration23Present,
            hasP0ProofTables,
            requiredSchemaPresent,
            ready,
            tables.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            triggers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            indexes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            reportColumns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static HashSet<string> ReadNames(SqliteConnection connection, string type)
    {
        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = $type;
            """;
        command.Parameters.AddWithValue("$type", type);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static HashSet<string> ReadTableColumns(
        SqliteConnection connection,
        string table)
    {
        HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(" + table + ");";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }
}

public sealed record MobileProductionReadiness(
    string DatabasePath,
    int MaxMigrationVersion,
    bool Migration23Present,
    bool HasP0ProofTables,
    bool RequiredSchemaPresent,
    bool IsReady,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> ReportV2Columns);

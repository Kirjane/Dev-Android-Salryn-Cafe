using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Paths;

string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
string command = args.Length == 0 ? "status" : args[0].Trim().ToLowerInvariant();

Console.WriteLine("===== SALRYN INFRASTRUCTURE SMOKE TOOL =====");
Console.WriteLine($"Project root: {projectRoot}");
Console.WriteLine($"Command: {command}");
Console.WriteLine();

try
{
    switch (command)
    {
        case "status":
            PrintStatus(projectRoot);
            break;

        case "init":
            Initialize(projectRoot);
            Inspect(projectRoot);
            break;

        case "inspect":
            Inspect(projectRoot);
            break;

        case "repositories":
            RepositorySmokeChecks.Run(projectRoot);
            break;

        case "auth-first-run":
            AuthenticationFirstRunStoreSettingsSmokeChecks.Run(projectRoot);
            break;

        case "mvp-business-flow":
            MvpBusinessFlowDatabaseSmokeChecks.Run(projectRoot);
            break;

        case "dev-prelaunch-seed":
            DevelopmentPreLaunchSeedChecks.Run(projectRoot);
            break;

        case "qa-products-seed":
            QaProductsSeedChecks.Run(projectRoot);
            break;

        case "product-write":
            ProductWriteSmokeChecks.Run(projectRoot);
            break;

        case "product-write-edge":
            ProductWriteEdgeSmokeChecks.Run(projectRoot);
            break;

        case "inventory-write":
            InventoryWriteSmokeChecks.Run(projectRoot);
            break;

        case "inventory-write-edge":
            InventoryWriteEdgeSmokeChecks.Run(projectRoot);
            break;

        case "sales-write":
            SalesWriteSmokeChecks.Run(projectRoot);
            break;

        case "sales-write-edge":
            SalesWriteEdgeSmokeChecks.Run(projectRoot);
            break;

        case "sales-read-write":
            SalesReadWriteIntegrationSmokeChecks.Run(projectRoot);
            break;

        case "cashier-session-write":
            CashierSessionWriteSmokeChecks.Run(projectRoot);
            break;

        case "cashier-session-write-edge":
            CashierSessionWriteEdgeSmokeChecks.Run(projectRoot);
            break;

        case "cashier-session-read-write":
            CashierSessionReadWriteIntegrationSmokeChecks.Run(projectRoot);
            break;

        case "cashier-session-service":
            CashierSessionServiceSmokeChecks.Run(projectRoot);
            break;

        case "cashier-session-service-edge":
            CashierSessionServiceEdgeSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-session-enforcement":
            PosCheckoutSessionEnforcementSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-repository-integration":
            PosCheckoutRepositoryIntegrationSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-application-boundary":
            PosCheckoutApplicationBoundarySmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-application-repository":
            PosCheckoutApplicationRepositorySmokeChecks.Run(projectRoot);
            OfficialCheckoutCompletionTrustSmokeChecks.Run(projectRoot);
            OfficialXReadingSmokeChecks.Run(projectRoot);
            OfficialZReadingSmokeChecks.Run(projectRoot);
            OfficialDocumentAuditEventSmokeChecks.Run(projectRoot);
            OfficialDocumentLifecycleSmokeChecks.Run(projectRoot);
            OfficialOutputSourceSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-draft-transaction-builder":
            PosCheckoutDraftTransactionBuilderSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-product-picker-boundary":
            PosCheckoutProductPickerBoundarySmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-product-picker-repository-integration":
            PosCheckoutProductPickerRepositoryIntegrationSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-ui-presenter-viewmodel":
            PosCheckoutUiPresenterViewModelSmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-ui-product-search-presenter-boundary":
            PosCheckoutUiProductSearchPresenterBoundarySmokeChecks.Run(projectRoot);
            break;

        case "pos-checkout-ui-product-repository-search-boundary":
            PosCheckoutUiProductRepositorySearchBoundarySmokeChecks.Run(projectRoot);
            break;

        case "reset":
            Reset(projectRoot);
            break;

        case "full":
            Reset(projectRoot);
            PrintStatus(projectRoot);
            Initialize(projectRoot);
            Inspect(projectRoot);
            AuthenticationFirstRunStoreSettingsSmokeChecks.Run(projectRoot);
            MvpBusinessFlowDatabaseSmokeChecks.Run(projectRoot);
            RepositorySmokeChecks.Run(projectRoot);
            ProductWriteSmokeChecks.Run(projectRoot);
            ProductWriteEdgeSmokeChecks.Run(projectRoot);
            InventoryWriteSmokeChecks.Run(projectRoot);
            InventoryWriteEdgeSmokeChecks.Run(projectRoot);
            SalesWriteSmokeChecks.Run(projectRoot);
            SalesWriteEdgeSmokeChecks.Run(projectRoot);
            SalesReadWriteIntegrationSmokeChecks.Run(projectRoot);
            CashierSessionWriteSmokeChecks.Run(projectRoot);
            CashierSessionWriteEdgeSmokeChecks.Run(projectRoot);
            CashierSessionReadWriteIntegrationSmokeChecks.Run(projectRoot);
            CashierSessionServiceSmokeChecks.Run(projectRoot);
            CashierSessionServiceEdgeSmokeChecks.Run(projectRoot);
            PosCheckoutSessionEnforcementSmokeChecks.Run(projectRoot);
            PosCheckoutRepositoryIntegrationSmokeChecks.Run(projectRoot);
            PosCheckoutApplicationBoundarySmokeChecks.Run(projectRoot);
            PosCheckoutApplicationRepositorySmokeChecks.Run(projectRoot);
            OfficialCheckoutCompletionTrustSmokeChecks.Run(projectRoot);
            OfficialXReadingSmokeChecks.Run(projectRoot);
            OfficialZReadingSmokeChecks.Run(projectRoot);
            OfficialDocumentAuditEventSmokeChecks.Run(projectRoot);
            OfficialDocumentLifecycleSmokeChecks.Run(projectRoot);
            OfficialOutputSourceSmokeChecks.Run(projectRoot);
            PosCheckoutDraftTransactionBuilderSmokeChecks.Run(projectRoot);
            PosCheckoutProductPickerBoundarySmokeChecks.Run(projectRoot);
            PosCheckoutProductPickerRepositoryIntegrationSmokeChecks.Run(projectRoot);
            PosCheckoutUiPresenterViewModelSmokeChecks.Run(projectRoot);
            PosCheckoutUiProductSearchPresenterBoundarySmokeChecks.Run(projectRoot);
            PosCheckoutUiProductRepositorySearchBoundarySmokeChecks.Run(projectRoot);
            Reset(projectRoot);
            Console.WriteLine("FULL SMOKE TEST PASSED AND CLEANED UP.");
            break;

        default:
            Console.WriteLine("Unknown command.");
            Console.WriteLine("Use: status | init | inspect | reset | repositories | auth-first-run | mvp-business-flow | dev-prelaunch-seed | qa-products-seed | product-write | product-write-edge | inventory-write | inventory-write-edge | sales-write | sales-write-edge | sales-read-write | cashier-session-write | cashier-session-write-edge | cashier-session-read-write | cashier-session-service | cashier-session-service-edge | pos-checkout-session-enforcement | pos-checkout-repository-integration | pos-checkout-application-boundary | pos-checkout-application-repository | pos-checkout-draft-transaction-builder | pos-checkout-product-picker-boundary | pos-checkout-product-picker-repository-integration | pos-checkout-ui-presenter-viewmodel | pos-checkout-ui-product-search-presenter-boundary | pos-checkout-ui-product-repository-search-boundary | full");
            Environment.ExitCode = 1;
            break;
    }
}
catch (Exception ex)
{
    Console.WriteLine("SMOKE TOOL FAILED.");
    Console.WriteLine(ex);
    Environment.ExitCode = 1;
}

static void PrintStatus(string projectRoot)
{
    DatabaseInitializationOrchestrator orchestrator =
        DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

    Result<DatabaseInitializationStatus> statusResult = orchestrator.CheckStatus();

    if (statusResult.IsFailure)
    {
        Fail(statusResult.Error);
        return;
    }

    DatabaseInitializationStatus status = statusResult.Value;

    Console.WriteLine("===== STATUS =====");
    Console.WriteLine($"Database path: {status.DatabasePath}");
    Console.WriteLine($"Database file exists: {status.DatabaseFileExists}");
    Console.WriteLine($"Configured migrations: {status.ConfiguredMigrationCount}");
    Console.WriteLine($"Has seed foundation: {status.HasSeedDataFoundation}");
    Console.WriteLine($"DB initialized: {status.FirstRunStatus.IsDatabaseInitialized}");
    Console.WriteLine($"First-run required: {status.FirstRunStatus.IsFirstRunRequired}");
    Console.WriteLine($"Has users: {status.FirstRunStatus.HasUsers}");
    Console.WriteLine($"Has store settings: {status.FirstRunStatus.HasStoreSettings}");
    Console.WriteLine();
}

static void Initialize(string projectRoot)
{
    DatabaseInitializationOrchestrator orchestrator =
        DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

    Console.WriteLine("===== INITIALIZE DATABASE =====");

    Result<DatabaseInitializationSummary> result = orchestrator.InitializeDatabase();

    if (result.IsFailure)
    {
        Fail(result.Error);
        return;
    }

    Console.WriteLine($"Database path: {result.Value.DatabasePath}");
    Console.WriteLine($"Configured migrations: {result.Value.ConfiguredMigrationCount}");
    Console.WriteLine($"Initialized at: {result.Value.InitializedAt:O}");
    Console.WriteLine($"First-run required: {result.Value.FirstRunStatus.IsFirstRunRequired}");
    Console.WriteLine("Initialization succeeded.");
    Console.WriteLine();
}

static void Inspect(string projectRoot)
{
    DbPaths paths = DbPaths.CreateDevelopment(projectRoot);

    Console.WriteLine("===== INSPECT DATABASE =====");
    Console.WriteLine($"Database path: {paths.DatabasePath}");

    if (!File.Exists(paths.DatabasePath))
    {
        Fail("Database file does not exist. Run init or full first.");
        return;
    }

    SqliteConnectionFactory factory = new(paths);

    using var connection = factory.CreateConnection();
    connection.Open();

    SqlitePragma.Apply(connection);

    string[] requiredTables =
    {
        "schema_migrations",
        "app_metadata",
        "roles",
        "users",
        "role_permissions",
        "store_settings",
        "categories",
        "brands",
        "units",
        "suppliers",
        "products",
        "receipt_sequences",
        "cashier_sessions",
        "sales",
        "sale_items",
        "payments",
        "stock_movements",
        "print_jobs",
        "audit_logs",
        "error_logs",
        "import_export_jobs",
        "backup_logs"
    };

    HashSet<string> tables = GetTableNames(connection);

    foreach (string table in requiredTables)
    {
        if (!tables.Contains(table))
        {
            Fail($"Missing required table: {table}");
            return;
        }
    }

    Console.WriteLine("Required tables: OK");

    int migrationCount = CountRows(connection, "schema_migrations");
    int roleCount = CountRows(connection, "roles");
    int permissionCount = CountRows(connection, "role_permissions");
    int unitCount = CountRows(connection, "units");
    int categoryCount = CountRows(connection, "categories");
    int brandCount = CountRows(connection, "brands");
    int receiptSequenceCount = CountRows(connection, "receipt_sequences");
    int userCount = CountRows(connection, "users");

    Console.WriteLine($"schema_migrations: {migrationCount}");
    Console.WriteLine($"roles: {roleCount}");
    Console.WriteLine($"role_permissions: {permissionCount}");
    Console.WriteLine($"units: {unitCount}");
    Console.WriteLine($"categories: {categoryCount}");
    Console.WriteLine($"brands: {brandCount}");
    Console.WriteLine($"receipt_sequences: {receiptSequenceCount}");
    Console.WriteLine($"users: {userCount}");

    if (migrationCount < 1)
    {
        Fail("Migration row check failed.");
        return;
    }

    if (roleCount < 3 || permissionCount < 1 || unitCount < 1 || categoryCount < 1 || brandCount < 1)
    {
        Fail("Seed data count check failed.");
        return;
    }

    if (receiptSequenceCount < 1)
    {
        Fail("Receipt sequence seed check failed.");
        return;
    }

    if (userCount != 0)
    {
        Fail("Smoke test should not create an admin user.");
        return;
    }

    string firstRunRequired = ScalarString(
        connection,
        "SELECT value FROM app_metadata WHERE key = 'first_run_required' LIMIT 1;") ?? "";

    if (!string.Equals(firstRunRequired, "true", StringComparison.OrdinalIgnoreCase))
    {
        Fail("first_run_required metadata should remain true before admin bootstrap.");
        return;
    }

    Console.WriteLine("Seed verification: OK");
    Console.WriteLine("Admin user not created: OK");
    Console.WriteLine("Inspection succeeded.");
    Console.WriteLine();
}

static void Reset(string projectRoot)
{
    DbPaths paths = DbPaths.CreateDevelopment(projectRoot);

    Console.WriteLine("===== RESET DEV DATABASE =====");
    Console.WriteLine($"Reset path: {paths.RootPath}");

    SqliteConnection.ClearAllPools();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    if (Directory.Exists(paths.RootPath))
    {
        DeleteDirectoryWithRetry(paths.RootPath);
        Console.WriteLine("Deleted dev database folder.");
    }
    else
    {
        Console.WriteLine("Nothing to reset.");
    }

    Console.WriteLine();
}

static void DeleteDirectoryWithRetry(string path)
{
    const int maxAttempts = 10;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(path, recursive: true);
            return;
        }
        catch (IOException) when (attempt < maxAttempts)
        {
            Thread.Sleep(250);
        }
        catch (UnauthorizedAccessException) when (attempt < maxAttempts)
        {
            Thread.Sleep(250);
        }
    }

    SqliteConnection.ClearAllPools();
    Directory.Delete(path, recursive: true);
}

static HashSet<string> GetTableNames(SqliteConnection connection)
{
    HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);

    using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT name
        FROM sqlite_master
        WHERE type = 'table';
        """;

    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        tables.Add(reader.GetString(0));
    }

    return tables;
}

static int CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

    object? value = command.ExecuteScalar();

    return Convert.ToInt32(value);
}

static string? ScalarString(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;

    object? value = command.ExecuteScalar();

    return value?.ToString();
}

static void Fail(string message)
{
    Console.WriteLine($"FAILED: {message}");
    Environment.ExitCode = 1;
}

static string FindProjectRoot(string startPath)
{
    DirectoryInfo? directory = new(startPath);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
}





























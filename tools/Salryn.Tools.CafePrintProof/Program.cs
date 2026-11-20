using Salryn.Tools.CafePrintProof;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Paths;

string root = Path.Combine(
    Path.GetTempPath(),
    "salryn-c7-cafe-print-proof-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(root);

try
{
    RunMigrationPermissionFoundation(Path.Combine(root, "migration"));
    C7PrintBehaviorProof.Run(Path.Combine(root, "behavior"));
    C7BackupMobileProof.Run(Path.Combine(root, "readiness"));
    Console.WriteLine("PASS C7 Receipt / Kitchen Print Domain proof");
}
finally
{
    SqliteConnectionFactory.ClearAllPools();
    TryDeleteDirectory(root);
}

static void RunMigrationPermissionFoundation(string root)
{
    IReadOnlyList<IDatabaseMigration> plan =
        CafeMigrationCatalog.CreateCanonicalCafeMigrations();

    Require(plan.Any(x => x.Version == 36), "C7 migration 36 must exist.");
    Require(
        plan.Max(x => x.Version) ==
            SalrynDatabaseMigrationCatalog.SupportedMigrationVersion &&
        SalrynDatabaseMigrationCatalog.SupportedMigrationVersion == 41,
        "C7 regression must accept current C10 migration-39 production ceiling.");
    Require(plan.Any(x => x.Version == 23), "Cafe migration23 must be present.");
    Require(plan.Any(x => x.Version == 37), "C8 migration 37 must coexist with accepted C7 migration 36.");
    Require(plan.Any(x => x.Version == 38), "C9 migration 38 must coexist with accepted C7 migration 36.");
    Require(plan.Any(x => x.Version == 39), "C10 migration 39 must coexist with accepted C7 migration 36.");
    Require(plan.Any(x => x.Version == 40) && plan.Any(x => x.Version == 41), "Cafe lineage40 and idempotency41 must be present.");

    DbPaths freshPaths = DbPaths.CreateAtRoot(root + "-fresh");
    _ = RequireValue(
        DefaultDatabaseInitializationFactory.Create(freshPaths)
            .InitializeDatabase(),
        "fresh migration 36 initialization");

    SqliteConnectionFactory fresh = new(freshPaths);

    Require(
        ScalarLong(fresh, "SELECT COALESCE(MAX(version),0) FROM schema_migrations;") ==
            SalrynDatabaseMigrationCatalog.SupportedMigrationVersion,
        "Fresh database must reach the current supported migration while retaining C7.");
    Require(
        ScalarLong(fresh, "SELECT COUNT(*) FROM schema_migrations WHERE version=23;") == 1,
        "Fresh Cafe database must record reconciled migration23.");
    Require(
        ScalarLong(fresh, "SELECT COUNT(*) FROM schema_migrations WHERE version=36;") == 1,
        "Fresh database must apply migration 36 once.");

    Require(TableExists(fresh, "print_jobs"), "Legacy migration-1 print_jobs table must remain present.");
    Require(TableExists(fresh, "cafe_printer_stations"), "Migration 36 must create cafe_printer_stations.");
    Require(TableExists(fresh, "cafe_print_jobs"), "Migration 36 must create cafe_print_jobs.");
    Require(TableExists(fresh, "cafe_print_attempts"), "Migration 36 must create cafe_print_attempts.");

    Require(RoleHasPermission(fresh, "Admin", "Printing.View"), "Admin Printing.View missing.");
    Require(RoleHasPermission(fresh, "Admin", "Printing.Print"), "Admin Printing.Print missing.");
    Require(RoleHasPermission(fresh, "Admin", "Printing.Reprint"), "Admin Printing.Reprint missing.");
    Require(RoleHasPermission(fresh, "Admin", "Printing.StationManage"), "Admin Printing.StationManage missing.");

    Require(RoleHasPermission(fresh, "Manager", "Printing.View"), "Manager Printing.View missing.");
    Require(RoleHasPermission(fresh, "Manager", "Printing.Print"), "Manager Printing.Print missing.");
    Require(RoleHasPermission(fresh, "Manager", "Printing.Reprint"), "Manager Printing.Reprint missing.");
    Require(RoleHasPermission(fresh, "Manager", "Printing.StationManage"), "Manager Printing.StationManage missing.");

    Require(RoleHasPermission(fresh, "Cashier", "Printing.View"), "Cashier Printing.View missing.");
    Require(RoleHasPermission(fresh, "Cashier", "Printing.Print"), "Cashier Printing.Print missing.");
    Require(!RoleHasPermission(fresh, "Cashier", "Printing.Reprint"), "Cashier must not receive Printing.Reprint by default.");
    Require(!RoleHasPermission(fresh, "Cashier", "Printing.StationManage"), "Cashier must not receive Printing.StationManage by default.");

    DbPaths upgradePaths = DbPaths.CreateAtRoot(root + "-upgrade");
    SqliteConnectionFactory upgrade = new(upgradePaths);

    new MigrationRunner(
        upgrade,
        plan.Where(x => x.Version <= 35))
        .RunPendingMigrations();
    new DatabaseSeeder(upgrade).SeedDefaultData();

    Require(
        ScalarLong(upgrade, "SELECT COALESCE(MAX(version),0) FROM schema_migrations;") == 35,
        "Upgrade fixture must begin at migration 35.");

    Guid legacyJobId = Guid.Parse("77000000-0000-0000-0000-000000000001");
    Execute(
        upgrade,
        """
        INSERT INTO print_jobs(
            id,job_type,reference_type,reference_id,
            printer_name,payload_text,status,error_message,
            created_at,printed_at)
        VALUES(
            $id,'LEGACY_RECEIPT','SALE','legacy-sale',
            'Legacy Printer','legacy payload',1,NULL,
            '2026-09-22T00:00:00.0000000+00:00',NULL);
        """,
        ("$id", legacyJobId.ToString("D")));

    long c6CashTablesBefore =
        ScalarLong(
            upgrade,
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table'
              AND name IN ('cafe_cash_operations','cafe_cash_bootstrap_state');
            """);

    new MigrationRunner(
        upgrade,
        new IDatabaseMigration[] { new CafePrintDomainSchemaMigration() })
        .RunPendingMigrations();
    new DatabaseSeeder(upgrade).SeedDefaultData();

    Require(
        ScalarLong(upgrade, "SELECT COALESCE(MAX(version),0) FROM schema_migrations;") == 36,
        "35 -> 36 upgrade must reach migration 36.");
    Require(
        ScalarLong(upgrade, "SELECT COUNT(*) FROM schema_migrations WHERE version=23;") == 1,
        "35 -> 36 upgrade must preserve migration 23 absence.");
    Require(
        ScalarLong(
            upgrade,
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table'
              AND name IN ('cafe_cash_operations','cafe_cash_bootstrap_state');
            """) == c6CashTablesBefore,
        "Migration 36 must preserve accepted C6 cash schema.");

    Require(
        ScalarLong(
            upgrade,
            """
            SELECT COUNT(*)
            FROM print_jobs
            WHERE id=$id
              AND job_type='LEGACY_RECEIPT'
              AND payload_text='legacy payload';
            """,
            ("$id", legacyJobId.ToString("D"))) == 1,
        "Migration 36 must preserve legacy migration-1 print_jobs evidence untouched.");

    Require(
        ScalarLong(upgrade, "SELECT COUNT(*) FROM cafe_print_jobs;") == 0 &&
        ScalarLong(upgrade, "SELECT COUNT(*) FROM cafe_print_attempts;") == 0,
        "Migration 36 must not fabricate C7 print facts during upgrade.");

    Console.WriteLine(
        "PASS C7 migration 35 -> 36, legacy print_jobs preservation, migration gaps, C6 preservation, permissions");
}

static bool TableExists(SqliteConnectionFactory factory, string table) =>
    ScalarLong(
        factory,
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;",
        ("$name", table)) == 1;

static bool RoleHasPermission(
    SqliteConnectionFactory factory,
    string roleName,
    string permission) =>
    ScalarLong(
        factory,
        """
        SELECT COUNT(*)
        FROM role_permissions rp
        INNER JOIN roles r ON r.id=rp.role_id
        WHERE r.name=$role
          AND rp.permission=$permission;
        """,
        ("$role", roleName),
        ("$permission", permission)) == 1;

static long ScalarLong(
    SqliteConnectionFactory factory,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    using SqliteConnection connection = factory.CreateConnection();
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    foreach ((string name, object? value) in parameters)
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
}

static void Execute(
    SqliteConnectionFactory factory,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    using SqliteConnection connection = factory.CreateConnection();
    connection.Open();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    foreach ((string name, object? value) in parameters)
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    command.ExecuteNonQuery();
}

static T RequireValue<T>(Result<T> result, string operation)
{
    if (result.IsFailure)
    {
        throw new InvalidOperationException(
            operation + " failed: " +
            result.ErrorDetail.Code + " / " +
            result.ErrorDetail.Message);
    }

    return result.Value;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
        // Proof cleanup is best effort.
    }
}

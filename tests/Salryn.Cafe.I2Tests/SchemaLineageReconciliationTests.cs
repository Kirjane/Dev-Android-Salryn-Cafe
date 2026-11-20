using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.I2Tests;

public sealed class SchemaLineageReconciliationTests : IDisposable
{
    private readonly string _tempDir;

    public SchemaLineageReconciliationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"salryn-cafe-i2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup on Windows file locks.
        }
    }

    private (DbPaths Paths, SqliteConnectionFactory Factory) CreateTempDb(string name)
    {
        string sub = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(sub);
        DbPaths paths = DbPaths.CreateDevelopment(sub);
        paths.EnsureDirectories();
        return (paths, new SqliteConnectionFactory(paths));
    }

    [Fact]
    public void FreshDatabase_MigratesV1ToV41_WithZeroPendingAndFullParity()
    {
        var (_, factory) = CreateTempDb("fresh-v1-v41");
        MigrationRunner runner = new(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());

        Assert.Equal(SchemaLineageKind.Fresh, runner.ClassifyDatabase().Kind);

        runner.RunPendingMigrations();

        Assert.Equal(0, runner.GetPendingMigrationCount());
        SchemaLineageClassification after = runner.ClassifyDatabase();
        Assert.Equal(SchemaLineageKind.SalrynCafe, after.Kind);
        Assert.Equal("Salryn.Cafe", after.ProductId);
        Assert.Equal(41, after.MaxAppliedVersion);

        using SqliteConnection conn = factory.CreateConnection();
        conn.Open();
        SqlitePragma.Apply(conn);

        // Verify lineage marker
        Assert.Equal("SALRYN_CAFE_FRESH_V40", ScalarString(conn, "SELECT lineage_code FROM salryn_schema_lineage WHERE id = 1;"));
        Assert.Equal("Salryn.Cafe", ScalarString(conn, "SELECT product_id FROM salryn_schema_lineage WHERE id = 1;"));

        // Verify Kernel v23/v25/v26 parity tables & columns
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "host_checkout_requests"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cash_drawer_events"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cash_drawer_reconciliations"));
        Assert.True(SchemaLineageClassifier.ColumnExists(conn, null, "cash_drawer_reconciliations", "close_note"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cash_drawer_reviews"));
        Assert.True(SchemaLineageClassifier.ColumnExists(conn, null, "cashier_sessions", "cash_coverage_started_at"));
        Assert.True(SchemaLineageClassifier.ColumnExists(conn, null, "accounts_receivable_collections", "cashier_session_id"));

        // Verify Yza Cafe v25-v39 tables
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cash_movements"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "sync_outbox"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "menu_variants"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "ingredients"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cafe_orders"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cafe_attendance_events"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cafe_bookings"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cafe_printer_stations"));

        // Verify v41 unified idempotency & device trust tables/columns
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "cafe_mutation_idempotency"));
        Assert.True(SchemaLineageClassifier.TableExists(conn, null, "sync_device_trust_state"));
        Assert.True(SchemaLineageClassifier.ColumnExists(conn, null, "salryn_terminal_registrations", "product_id"));
        Assert.True(SchemaLineageClassifier.ColumnExists(conn, null, "salryn_terminal_registrations", "public_key_thumbprint"));
        Assert.True(SchemaLineageClassifier.ColumnExists(conn, null, "salryn_terminal_registrations", "offline_lease_expires_at_utc"));

        Assert.Equal("ok", ScalarString(conn, "PRAGMA integrity_check;"));
        Assert.Equal(0L, CountRows(conn, "PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void V41_CafeMutationIdempotency_TriggersEnforceImmutability()
    {
        var (_, factory) = CreateTempDb("v41-immutability");
        MigrationRunner runner = new(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        runner.RunPendingMigrations();

        using SqliteConnection conn = factory.CreateConnection();
        conn.Open();

        string opId = Guid.NewGuid().ToString("D");
        Execute(conn, """
            INSERT INTO cafe_mutation_idempotency (
                operation_id, endpoint_key, actor_user_id, request_hash, status_code, response_json, created_at_utc)
            VALUES ($opId, 'POST /api/cafe/orders', 'user-1', 'sha256:abc', 201, '{"ok":true}', '2026-11-20T00:00:00Z');
            """, ("$opId", opId));

        SqliteException updateEx = Assert.Throws<SqliteException>(() =>
            Execute(conn, "UPDATE cafe_mutation_idempotency SET status_code = 500 WHERE operation_id = $opId;", ("$opId", opId)));
        Assert.Contains("immutable", updateEx.Message, StringComparison.OrdinalIgnoreCase);

        SqliteException deleteEx = Assert.Throws<SqliteException>(() =>
            Execute(conn, "DELETE FROM cafe_mutation_idempotency WHERE operation_id = $opId;", ("$opId", opId)));
        Assert.Contains("immutable", deleteEx.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(201L, ScalarLong(conn, "SELECT status_code FROM cafe_mutation_idempotency WHERE operation_id = $opId;", ("$opId", opId)));
    }

    [Fact]
    public void LegacyYzaV39_UpgradesToV41_ZeroDataLoss_RetiresSkippedV23_AndBackfillsVoidAudit()
    {
        var (_, factory) = CreateTempDb("yza-v39-upgrade");

        // 1. Build authentic synthetic Yza v39 database (skipping v23)
        MigrationRunner legacyRunner = new(factory, CafeMigrationCatalog.CreateLegacyYzaV39Migrations());
        legacyRunner.RunPendingMigrations();

        string roleId = Guid.NewGuid().ToString("D");
        string userId = Guid.NewGuid().ToString("D");
        string saleId = Guid.NewGuid().ToString("D");
        string catId = Guid.NewGuid().ToString("D");
        string now = "2026-11-06T10:00:00.0000000+08:00";

        using (SqliteConnection seedConn = factory.CreateConnection())
        {
            seedConn.Open();
            SqlitePragma.Apply(seedConn);

            Assert.Equal(0L, ScalarLong(seedConn, "SELECT COUNT(*) FROM schema_migrations WHERE version = 23;"));
            Assert.False(SchemaLineageClassifier.TableExists(seedConn, null, "host_checkout_requests"));
            Assert.False(SchemaLineageClassifier.TableExists(seedConn, null, "cash_drawer_reconciliations"));

            Execute(seedConn, "INSERT INTO roles (id, name, created_at) VALUES ($id, 'Cashier', $now);", ("$id", roleId), ("$now", now));
            Execute(seedConn, """
                INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
                VALUES ($id, 'yza_cashier', 'Yza Cashier', 'hash', $roleId, 1, $now, $now);
                """, ("$id", userId), ("$roleId", roleId), ("$now", now));
            Execute(seedConn, """
                INSERT INTO sales (
                    id, receipt_no, sale_date, cashier_user_id, subtotal_cents, discount_cents, tax_cents, total_cents,
                    amount_paid_cents, change_cents, status, voided_by_user_id, void_reason, voided_at, created_at)
                VALUES (
                    $id, 'YZA-0001', '2026-11-06', $userId, 12000, 0, 0, 12000,
                    12000, 0, 3, $userId, 'Customer changed mind', $now, $now);
                """, ("$id", saleId), ("$userId", userId), ("$now", now));
            Execute(seedConn, """
                INSERT INTO categories (id, name)
                VALUES ($id, 'Iced Coffee');
                """, ("$id", catId));
        }

        // 2. Classify & upgrade using canonical Salryn Cafe runner
        MigrationRunner cafeRunner = new(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        SchemaLineageClassification preUpgrade = cafeRunner.ClassifyDatabase();
        Assert.Equal(SchemaLineageKind.YzaV39Legacy, preUpgrade.Kind);
        Assert.Equal(39, preUpgrade.MaxAppliedVersion);

        cafeRunner.RunPendingMigrations();

        // 3. Verify zero pending migrations, lineage marker, preserved rows, Kernel parity & v27 void audit backfill
        Assert.Equal(0, cafeRunner.GetPendingMigrationCount());
        SchemaLineageClassification postUpgrade = cafeRunner.ClassifyDatabase();
        Assert.Equal(SchemaLineageKind.SalrynCafe, postUpgrade.Kind);
        Assert.Equal(41, postUpgrade.MaxAppliedVersion);

        using SqliteConnection verifyConn = factory.CreateConnection();
        verifyConn.Open();
        SqlitePragma.Apply(verifyConn);

        Assert.Equal("YZA_V39_LEGACY_ADOPTED_V40", ScalarString(verifyConn, "SELECT lineage_code FROM salryn_schema_lineage WHERE id = 1;"));
        Assert.Equal(39L, ScalarLong(verifyConn, "SELECT adopted_from_version FROM salryn_schema_lineage WHERE id = 1;"));
        Assert.Equal(1L, ScalarLong(verifyConn, "SELECT COUNT(*) FROM schema_migrations WHERE version = 23;"));

        // Synthetic Yza data preserved 100%
        Assert.Equal("Iced Coffee", ScalarString(verifyConn, "SELECT name FROM categories WHERE id = $id;", ("$id", catId)));
        Assert.Equal("YZA-0001", ScalarString(verifyConn, "SELECT receipt_no FROM sales WHERE id = $id;", ("$id", saleId)));

        // Kernel parity tables & v27 void audit backfill created
        Assert.True(SchemaLineageClassifier.TableExists(verifyConn, null, "host_checkout_requests"));
        Assert.True(SchemaLineageClassifier.TableExists(verifyConn, null, "cash_drawer_reconciliations"));
        Assert.True(SchemaLineageClassifier.ColumnExists(verifyConn, null, "cash_drawer_reconciliations", "close_note"));
        Assert.Equal(1L, ScalarLong(verifyConn, "SELECT COUNT(*) FROM audit_logs WHERE action = 'SALE_VOIDED' AND entity_id = $saleId;", ("$saleId", saleId)));

        Assert.Equal("ok", ScalarString(verifyConn, "PRAGMA integrity_check;"));
        Assert.Equal(0L, CountRows(verifyConn, "PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void RetailKernelDatabase_FailsClosed_WithoutMutatingData()
    {
        var (_, factory) = CreateTempDb("retail-kernel-v32");

        MigrationRunner retailRunner = new(factory, CafeMigrationCatalog.CreateLegacyRetailKernelV32Migrations());
        retailRunner.RunPendingMigrations();

        long migrationsBefore;
        using (SqliteConnection beforeConn = factory.CreateConnection())
        {
            beforeConn.Open();
            migrationsBefore = ScalarLong(beforeConn, "SELECT COUNT(*) FROM schema_migrations;");
        }

        MigrationRunner cafeRunner = new(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        Assert.Equal(SchemaLineageKind.RetailKernel, cafeRunner.ClassifyDatabase().Kind);

        WrongProductDatabaseException ex = Assert.Throws<WrongProductDatabaseException>(() => cafeRunner.RunPendingMigrations());
        Assert.Contains(WrongProductDatabaseException.ErrorCode, ex.Message);

        // Verify zero mutation / zero data reset
        using SqliteConnection afterConn = factory.CreateConnection();
        afterConn.Open();
        Assert.False(SchemaLineageClassifier.TableExists(afterConn, null, "salryn_schema_lineage"));
        Assert.False(SchemaLineageClassifier.TableExists(afterConn, null, "menu_variants"));
        Assert.Equal(migrationsBefore, ScalarLong(afterConn, "SELECT COUNT(*) FROM schema_migrations;"));
    }

    [Fact]
    public void UnknownOrCorruptDatabase_FailsClosed_WithoutMutatingData()
    {
        var (_, factory) = CreateTempDb("unknown-corrupt");
        using (SqliteConnection seedConn = factory.CreateConnection())
        {
            seedConn.Open();
            Execute(seedConn, "CREATE TABLE mystery_table (id INTEGER PRIMARY KEY, label TEXT NOT NULL);");
            Execute(seedConn, "INSERT INTO mystery_table (id, label) VALUES (1, 'preserve-me');");
        }

        MigrationRunner cafeRunner = new(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        Assert.Equal(SchemaLineageKind.UnknownOrCorrupt, cafeRunner.ClassifyDatabase().Kind);

        UnknownSchemaLineageException ex = Assert.Throws<UnknownSchemaLineageException>(() => cafeRunner.RunPendingMigrations());
        Assert.Contains(UnknownSchemaLineageException.ErrorCode, ex.Message);

        using SqliteConnection verifyConn = factory.CreateConnection();
        verifyConn.Open();
        Assert.False(SchemaLineageClassifier.TableExists(verifyConn, null, "schema_migrations"));
        Assert.Equal("preserve-me", ScalarString(verifyConn, "SELECT label FROM mystery_table WHERE id = 1;"));
    }

    [Fact]
    public void CrashRetryIdempotency_ReExecutingV40AndV41IsSafeNoOp()
    {
        var (_, factory) = CreateTempDb("crash-retry-idempotency");
        MigrationRunner runner = new(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        runner.RunPendingMigrations();

        // Repeated full runner invocations must be clean no-ops
        runner.RunPendingMigrations();
        Assert.Equal(0, runner.GetPendingMigrationCount());

        // Direct re-execution of v40 and v41 DDL inside a transaction must be idempotent
        using SqliteConnection conn = factory.CreateConnection();
        conn.Open();
        SqlitePragma.Apply(conn);
        using SqliteTransaction tx = conn.BeginTransaction();
        new CafeLineageAdoptionAndKernelParityMigration().Up(conn, tx);
        new CafeMutationIdempotencyAndDeviceTrustMigration().Up(conn, tx);
        tx.Commit();

        Assert.Equal(1L, ScalarLong(conn, "SELECT COUNT(*) FROM salryn_schema_lineage;"));
        Assert.Equal("ok", ScalarString(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void BackupAndRestore_PreservesUpgradedV41SchemaAndDataWithZeroPending()
    {
        var (_, sourceFactory) = CreateTempDb("backup-source");
        var (restorePaths, restoreFactory) = CreateTempDb("backup-restore");

        MigrationRunner sourceRunner = new(sourceFactory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        sourceRunner.RunPendingMigrations();

        string opId = Guid.NewGuid().ToString("D");
        using (SqliteConnection srcConn = sourceFactory.CreateConnection())
        {
            srcConn.Open();
            Execute(srcConn, """
                INSERT INTO cafe_mutation_idempotency (
                    operation_id, endpoint_key, actor_user_id, request_hash, status_code, response_json, created_at_utc)
                VALUES ($opId, 'POST /api/cafe/orders', 'cashier-1', 'sha256:xyz', 200, '{"receipt":"R-1"}', '2026-11-20T01:00:00Z');
                """, ("$opId", opId));

            using SqliteConnection dstConn = new($"Data Source={restorePaths.DatabasePath};Pooling=False");
            dstConn.Open();
            srcConn.BackupDatabase(dstConn);
        }

        MigrationRunner restoreRunner = new(restoreFactory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        Assert.Equal(SchemaLineageKind.SalrynCafe, restoreRunner.ClassifyDatabase().Kind);
        Assert.Equal(0, restoreRunner.GetPendingMigrationCount());
        restoreRunner.RunPendingMigrations();

        using SqliteConnection checkConn = restoreFactory.CreateConnection();
        checkConn.Open();
        Assert.Equal("{\"receipt\":\"R-1\"}", ScalarString(checkConn, "SELECT response_json FROM cafe_mutation_idempotency WHERE operation_id = $opId;", ("$opId", opId)));
        Assert.Equal("ok", ScalarString(checkConn, "PRAGMA integrity_check;"));
        Assert.Equal(0L, CountRows(checkConn, "PRAGMA foreign_key_check;"));
    }

    private static void Execute(SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        cmd.ExecuteNonQuery();
    }

    private static string ScalarString(SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private static long ScalarLong(SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long CountRows(SqliteConnection conn, string sql)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using SqliteDataReader reader = cmd.ExecuteReader();
        long count = 0;
        while (reader.Read())
        {
            count++;
        }
        return count;
    }
}

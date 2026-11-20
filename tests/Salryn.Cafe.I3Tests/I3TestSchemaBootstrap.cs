using Microsoft.Data.Sqlite;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Paths;

namespace Salryn.Cafe.I3Tests;

/// <summary>
/// Test-only SQLite bootstrapper for I3 tests so I3 never modifies <c>Migrations/**</c> (owned by I2).
/// Initializes the Kernel baseline DB and provisions the locked v41 <c>cafe_mutation_idempotency</c> table.
/// </summary>
internal sealed class I3TestDatabaseFixture : IDisposable
{
    public string TempRoot { get; }
    public DbPaths DbPaths { get; }
    public SalrynLocalComposition Composition { get; }
    public SqliteConnectionFactory ConnectionFactory => Composition.ConnectionFactory;

    public Guid StoreId { get; } = Guid.NewGuid();
    public Guid AdminUserId { get; private set; }
    public Guid ProductId { get; } = Guid.NewGuid();
    public Guid UnitId { get; private set; }
    public Guid CustomerId { get; } = Guid.NewGuid();
    public Guid CashierSessionId { get; private set; }

    public I3TestDatabaseFixture()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "salryn-i3-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
        DbPaths = DbPaths.CreateDevelopment(TempRoot);
        Composition = SalrynLocalComposition.Create(DbPaths);
        Composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

        EnsureV41IdempotencyTable();
        SeedSyntheticData();
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = ConnectionFactory.CreateConnection();
        connection.Open();
        return connection;
    }

    private void EnsureV41IdempotencyTable()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS cafe_mutation_idempotency (
                operation_id TEXT PRIMARY KEY,
                endpoint_key TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                request_hash TEXT NOT NULL,
                status_code INTEGER NOT NULL,
                response_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void SeedSyntheticData()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction tx = connection.BeginTransaction();

        string now = DateTimeOffset.UtcNow.ToString("O");

        // Pick or seed synthetic admin user
        using (SqliteCommand userCmd = connection.CreateCommand())
        {
            userCmd.Transaction = tx;
            userCmd.CommandText = "SELECT id FROM users LIMIT 1;";
            object? existingUser = userCmd.ExecuteScalar();
            if (existingUser is string uid && !string.IsNullOrWhiteSpace(uid))
            {
                AdminUserId = Guid.Parse(uid);
            }
            else
            {
                string roleId;
                using (SqliteCommand roleCmd = connection.CreateCommand())
                {
                    roleCmd.Transaction = tx;
                    roleCmd.CommandText = "SELECT id FROM roles LIMIT 1;";
                    roleId = (string)roleCmd.ExecuteScalar()!;
                }

                AdminUserId = Guid.NewGuid();
                using SqliteCommand insertUser = connection.CreateCommand();
                insertUser.Transaction = tx;
                insertUser.CommandText = """
                    INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
                    VALUES ($id, 'i3_cashier', 'Synthetic I3 Cashier', 'PBKDF2$SYNTHETIC', $roleId, 1, $now);
                    """;
                insertUser.Parameters.AddWithValue("$id", AdminUserId.ToString("D"));
                insertUser.Parameters.AddWithValue("$roleId", roleId);
                insertUser.Parameters.AddWithValue("$now", now);
                insertUser.ExecuteNonQuery();
            }
        }

        // Pick seeded unit
        using (SqliteCommand unitCmd = connection.CreateCommand())
        {
            unitCmd.Transaction = tx;
            unitCmd.CommandText = "SELECT id FROM units LIMIT 1;";
            UnitId = Guid.Parse((string)unitCmd.ExecuteScalar()!);
        }

        // Seed synthetic coffee product with 100 units of stock at PHP 150.00 (15000 cents)
        using (SqliteCommand prodCmd = connection.CreateCommand())
        {
            prodCmd.Transaction = tx;
            prodCmd.CommandText = """
                INSERT INTO products (
                    id, sku, barcode, name, category_id, unit_id,
                    selling_price_cents, cost_price_cents, stock_on_hand_x1000,
                    reorder_level_x1000, is_active, created_at, updated_at
                )
                VALUES (
                    $id, 'CAFE-LATTE-01', NULL, 'Iced Spanish Latte', NULL, $unitId,
                    15000, 6000, 100000, 5000, 1, $now, $now
                );
                """;
            prodCmd.Parameters.AddWithValue("$id", ProductId.ToString("D"));
            prodCmd.Parameters.AddWithValue("$unitId", UnitId.ToString("D"));
            prodCmd.Parameters.AddWithValue("$now", now);
            prodCmd.ExecuteNonQuery();
        }

        // Seed active synthetic AR customer
        using (SqliteCommand custCmd = connection.CreateCommand())
        {
            custCmd.Transaction = tx;
            custCmd.CommandText = """
                INSERT INTO customers (id, full_name, phone, is_active, created_by_user_id, created_at, updated_at)
                VALUES ($id, 'Synthetic Cafe Regular', '09170000001', 1, $userId, $now, $now);
                """;
            custCmd.Parameters.AddWithValue("$id", CustomerId.ToString("D"));
            custCmd.Parameters.AddWithValue("$userId", AdminUserId.ToString("D"));
            custCmd.Parameters.AddWithValue("$now", now);
            custCmd.ExecuteNonQuery();
        }

        // Open cashier session for AdminUserId
        CashierSessionId = Guid.NewGuid();
        using (SqliteCommand shiftCmd = connection.CreateCommand())
        {
            shiftCmd.Transaction = tx;
            shiftCmd.CommandText = """
                INSERT INTO cashier_sessions (
                    id, user_id, opened_at, opening_cash_cents, expected_cash_cents,
                    status, cash_coverage_started_at
                )
                VALUES ($id, $userId, $now, 100000, 100000, 1, $now);
                """;
            shiftCmd.Parameters.AddWithValue("$id", CashierSessionId.ToString("D"));
            shiftCmd.Parameters.AddWithValue("$userId", AdminUserId.ToString("D"));
            shiftCmd.Parameters.AddWithValue("$now", now);
            shiftCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public int CountSales()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup on Windows file locks
        }
    }
}

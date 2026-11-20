using Microsoft.Data.Sqlite;

namespace Salryn.Mobile.Runtime;

/// <summary>
/// Offline-first local SQLite WAL authority for the Salryn Cafe native Android cashier.
/// Completely independent of ASP.NET/Kestrel, desktop WinForms/WebView2, and PDF libraries.
/// </summary>
public sealed class CafeMobileDatabase
{
    public const string CanonicalProductId = "Salryn.Cafe";
    private readonly string _connectionString;

    public CafeMobileDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("DatabasePath is required.", nameof(databasePath));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        string? dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection OpenConnection()
    {
        SqliteConnection conn = new(_connectionString);
        conn.Open();
        using SqliteCommand pragma = conn.CreateCommand();
        pragma.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void Initialize()
    {
        using SqliteConnection conn = OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS mobile_device_state (
                    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                    product_id TEXT NOT NULL,
                    device_id TEXT NOT NULL,
                    tenant_id TEXT NOT NULL,
                    store_id TEXT NOT NULL,
                    server_epoch INTEGER NOT NULL DEFAULT 1,
                    pull_cursor INTEGER NOT NULL DEFAULT 0,
                    status TEXT NOT NULL DEFAULT 'Unpaired',
                    offline_lease_json TEXT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_operators (
                    operator_id TEXT PRIMARY KEY,
                    username TEXT NOT NULL UNIQUE,
                    display_name TEXT NOT NULL,
                    role TEXT NOT NULL,
                    pin_hash TEXT NOT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS mobile_menu_items (
                    product_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    category TEXT NOT NULL,
                    price_cents INTEGER NOT NULL,
                    modifiers_json TEXT NOT NULL DEFAULT '[]',
                    is_available INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS mobile_shifts (
                    shift_id TEXT PRIMARY KEY,
                    operator_id TEXT NOT NULL,
                    opened_at_utc TEXT NOT NULL,
                    opening_cash_cents INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    counted_cash_cents INTEGER NULL,
                    expected_cash_cents INTEGER NULL,
                    variance_cents INTEGER NULL,
                    close_note TEXT NULL,
                    closed_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_orders (
                    order_id TEXT PRIMARY KEY,
                    order_no TEXT NOT NULL,
                    order_type TEXT NOT NULL,
                    status TEXT NOT NULL,
                    lines_json TEXT NOT NULL,
                    discount_cents INTEGER NOT NULL DEFAULT 0,
                    total_cents INTEGER NOT NULL,
                    notes TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_sales (
                    sale_id TEXT PRIMARY KEY,
                    operation_id TEXT NOT NULL UNIQUE,
                    shift_id TEXT NOT NULL,
                    order_id TEXT NOT NULL,
                    receipt_no TEXT NOT NULL UNIQUE,
                    tender_mode TEXT NOT NULL,
                    cash_cents INTEGER NOT NULL,
                    ar_cents INTEGER NOT NULL,
                    customer_id TEXT NULL,
                    discount_cents INTEGER NOT NULL,
                    total_cents INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_refunds (
                    refund_id TEXT PRIMARY KEY,
                    operation_id TEXT NOT NULL UNIQUE,
                    sale_id TEXT NOT NULL,
                    actor_user_id TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    amount_cents INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_sync_outbox (
                    local_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                    operation_id TEXT NOT NULL UNIQUE,
                    operation_type TEXT NOT NULL,
                    envelope_json TEXT NOT NULL,
                    status TEXT NOT NULL,
                    last_error TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_print_stations (
                    station_key TEXT PRIMARY KEY,
                    printer_target_id TEXT NOT NULL,
                    printer_name TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS mobile_print_jobs (
                    job_id TEXT PRIMARY KEY,
                    station_key TEXT NOT NULL,
                    document_type TEXT NOT NULL,
                    reference_id TEXT NOT NULL,
                    reprint_count INTEGER NOT NULL DEFAULT 0,
                    reprint_reason TEXT NULL,
                    payload_base64 TEXT NOT NULL,
                    status TEXT NOT NULL,
                    last_error TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    printed_at_utc TEXT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}

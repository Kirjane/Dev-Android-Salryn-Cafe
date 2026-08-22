using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// RMO-BIR-M8: internal dry-run package storage only.
/// No sales/checkout/sequence/EJ live mutation. Not official issuance.
/// </summary>
public sealed class OfficialDryRunPackageSchemaMigration : IDatabaseMigration
{
    public int Version => 14;

    public string Name => "Official internal dry-run package schema";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            CREATE TABLE official_dry_run_packages (
                id TEXT PRIMARY KEY,
                public_id TEXT NOT NULL UNIQUE,
                created_at_utc TEXT NOT NULL,
                created_by_user_id TEXT NULL,
                source_mode TEXT NOT NULL DEFAULT 'Synthetic',
                source_sale_id TEXT NULL,
                contract_version INTEGER NOT NULL DEFAULT 1,
                dry_run_only INTEGER NOT NULL DEFAULT 1,
                non_official INTEGER NOT NULL DEFAULT 1,
                live_official_posting_allowed INTEGER NOT NULL DEFAULT 0,
                official_checkout_allocation_enabled INTEGER NOT NULL DEFAULT 0,
                checkout_uses_allocator INTEGER NOT NULL DEFAULT 0,
                sequence_will_mutate INTEGER NOT NULL DEFAULT 0,
                sales_will_mutate INTEGER NOT NULL DEFAULT 0,
                ej_will_append_live INTEGER NOT NULL DEFAULT 0,
                output_will_generate INTEGER NOT NULL DEFAULT 0,
                official_receipt_issued INTEGER NOT NULL DEFAULT 0,
                compliance_claimed INTEGER NOT NULL DEFAULT 0,
                request_json TEXT NOT NULL,
                plan_json TEXT NOT NULL,
                report_package_json TEXT NULL,
                status TEXT NOT NULL DEFAULT 'Created',
                warnings_json TEXT NULL,
                blockers_json TEXT NULL,
                summary TEXT NULL,

                CHECK (length(trim(public_id)) > 0),
                CHECK (source_mode IN ('Synthetic', 'OperationalSaleReadOnly', 'Unknown')),
                CHECK (dry_run_only = 1),
                CHECK (non_official = 1),
                CHECK (live_official_posting_allowed = 0),
                CHECK (official_checkout_allocation_enabled = 0),
                CHECK (checkout_uses_allocator = 0),
                CHECK (sequence_will_mutate = 0),
                CHECK (sales_will_mutate = 0),
                CHECK (ej_will_append_live = 0),
                CHECK (output_will_generate = 0),
                CHECK (official_receipt_issued = 0),
                CHECK (compliance_claimed = 0)
            );
            """);

        Execute(connection, transaction, """
            CREATE INDEX idx_official_dry_run_packages_created
            ON official_dry_run_packages(created_at_utc DESC);
            """);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

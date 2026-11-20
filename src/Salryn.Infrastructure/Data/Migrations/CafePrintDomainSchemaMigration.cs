using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Migrations;

public sealed class CafePrintDomainSchemaMigration : IDatabaseMigration
{
    public int Version => 36;

    public string Name => "Cafe receipt and kitchen print domain";

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, $"""
            CREATE TABLE cafe_printer_stations (
                station_id TEXT PRIMARY KEY,
                store_id TEXT NOT NULL,
                name TEXT NOT NULL,
                station_kind INTEGER NOT NULL,
                route_key TEXT NULL,
                is_active INTEGER NOT NULL,
                version INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                created_by_user_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_by_user_id TEXT NULL,
                updated_at_utc TEXT NULL,
                FOREIGN KEY(created_by_user_id) REFERENCES users(id),
                FOREIGN KEY(updated_by_user_id) REFERENCES users(id),
                CHECK(LENGTH(TRIM(name)) > 0),
                CHECK(station_kind IN (
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrinterStationKind.CustomerReceipt)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrinterStationKind.Kitchen)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrinterStationKind.BarDrinks)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrinterStationKind.OtherPreparation)}
                )),
                CHECK(is_active IN (0,1)),
                CHECK(version >= 1),
                CHECK(LENGTH(content_hash) = 64)
            );

            CREATE UNIQUE INDEX uq_cafe_printer_stations_store_name
                ON cafe_printer_stations(store_id, name);

            CREATE INDEX idx_cafe_printer_stations_store_kind
                ON cafe_printer_stations(store_id, station_kind, is_active);

            CREATE TABLE cafe_print_jobs (
                job_id TEXT PRIMARY KEY,
                operation_id TEXT NOT NULL UNIQUE,
                store_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                actor_user_id TEXT NOT NULL,
                station_id TEXT NOT NULL,
                document_kind INTEGER NOT NULL,
                source_type TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_version TEXT NULL,
                snapshot_text TEXT NOT NULL,
                snapshot_hash TEXT NOT NULL,
                copy_kind INTEGER NOT NULL,
                parent_job_id TEXT NULL,
                payload_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY(actor_user_id) REFERENCES users(id),
                FOREIGN KEY(station_id) REFERENCES cafe_printer_stations(station_id),
                FOREIGN KEY(parent_job_id) REFERENCES cafe_print_jobs(job_id),
                CHECK(document_kind IN (
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintDocumentKind.CustomerReceipt)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintDocumentKind.KitchenTicket)}
                )),
                CHECK(copy_kind IN (
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintCopyKind.Original)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintCopyKind.Reprint)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintCopyKind.VoidedReprint)}
                )),
                CHECK(LENGTH(TRIM(source_type)) > 0),
                CHECK(LENGTH(snapshot_text) > 0),
                CHECK(LENGTH(snapshot_hash) = 64),
                CHECK(LENGTH(payload_hash) = 64),
                CHECK(
                    copy_kind <> {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintCopyKind.Original)}
                    OR parent_job_id IS NULL
                )
            );

            CREATE UNIQUE INDEX uq_cafe_print_jobs_initial_source
                ON cafe_print_jobs(
                    station_id,
                    document_kind,
                    source_type,
                    source_id
                )
                WHERE copy_kind={Convert.ToInt32(CafeMigrationSqlCodes.CafePrintCopyKind.Original)};

            CREATE INDEX idx_cafe_print_jobs_source
                ON cafe_print_jobs(source_type, source_id, document_kind, created_at_utc);

            CREATE INDEX idx_cafe_print_jobs_station
                ON cafe_print_jobs(station_id, created_at_utc);

            CREATE TABLE cafe_print_attempts (
                attempt_id TEXT PRIMARY KEY,
                job_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                attempt_number INTEGER NOT NULL,
                adapter_name TEXT NOT NULL,
                adapter_mode TEXT NOT NULL,
                status INTEGER NOT NULL,
                result_code TEXT NOT NULL,
                result_message TEXT NOT NULL,
                error_message TEXT NULL,
                requested_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NOT NULL,
                FOREIGN KEY(job_id) REFERENCES cafe_print_jobs(job_id),
                CHECK(attempt_number >= 1),
                CHECK(LENGTH(TRIM(adapter_name)) > 0),
                CHECK(LENGTH(TRIM(adapter_mode)) > 0),
                CHECK(status IN (
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintAttemptStatus.Succeeded)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintAttemptStatus.Failed)},
                    {Convert.ToInt32(CafeMigrationSqlCodes.CafePrintAttemptStatus.DisabledUnavailable)}
                )),
                CHECK(LENGTH(TRIM(result_code)) > 0),
                CHECK(LENGTH(TRIM(result_message)) > 0)
            );

            CREATE UNIQUE INDEX uq_cafe_print_attempts_job_number
                ON cafe_print_attempts(job_id, attempt_number);

            CREATE INDEX idx_cafe_print_attempts_job_time
                ON cafe_print_attempts(job_id, requested_at_utc);

            CREATE TRIGGER trg_cafe_printer_stations_no_delete
            BEFORE DELETE ON cafe_printer_stations
            BEGIN
                SELECT RAISE(ABORT, 'cafe printer stations are retained for print history');
            END;

            CREATE TRIGGER trg_cafe_printer_stations_versioned_update
            BEFORE UPDATE ON cafe_printer_stations
            WHEN NEW.version <> OLD.version + 1
              OR NEW.station_id <> OLD.station_id
              OR NEW.store_id <> OLD.store_id
              OR NEW.created_by_user_id <> OLD.created_by_user_id
              OR NEW.created_at_utc <> OLD.created_at_utc
            BEGIN
                SELECT RAISE(ABORT, 'cafe printer station updates must advance exactly one version without rewriting identity');
            END;

            CREATE TRIGGER trg_cafe_print_jobs_no_update
            BEFORE UPDATE ON cafe_print_jobs
            BEGIN
                SELECT RAISE(ABORT, 'cafe print jobs are immutable');
            END;

            CREATE TRIGGER trg_cafe_print_jobs_no_delete
            BEFORE DELETE ON cafe_print_jobs
            BEGIN
                SELECT RAISE(ABORT, 'cafe print jobs are immutable');
            END;

            CREATE TRIGGER trg_cafe_print_attempts_no_update
            BEFORE UPDATE ON cafe_print_attempts
            BEGIN
                SELECT RAISE(ABORT, 'cafe print attempts are immutable');
            END;

            CREATE TRIGGER trg_cafe_print_attempts_no_delete
            BEFORE DELETE ON cafe_print_attempts
            BEGIN
                SELECT RAISE(ABORT, 'cafe print attempts are immutable');
            END;
            """);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

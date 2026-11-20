using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Mobile.Runtime;

namespace Salryn.Tools.CafePrintProof;

internal static class C7BackupMobileProof
{
    internal static void Run(string root)
    {
        ProveBackupRestore(Path.Combine(root, "backup"));
        ProveMobileReadiness(Path.Combine(root, "mobile"));

        Console.WriteLine(
            "PASS C7 migration-36 backup/restore and MobileProductionComposition readiness");
    }

    private static void ProveBackupRestore(string root)
    {
        DbPaths paths = DbPaths.CreateAtRoot(root);
        _ = RequireValue(
            DefaultDatabaseInitializationFactory.Create(paths)
                .InitializeDatabase(),
            "C7 backup initialization");

        SqliteConnectionFactory factory = new(paths);
        Guid storeId = Guid.Parse(
            ScalarString(
                factory,
                "SELECT store_id FROM deployment_identity LIMIT 1;"));

        Guid adminId =
            Guid.Parse("77000000-0000-0000-0000-000000000201");
        Guid stationId =
            Guid.Parse("77000000-0000-0000-0000-000000000202");
        Guid jobId =
            Guid.Parse("77000000-0000-0000-0000-000000000203");
        Guid operationId =
            Guid.Parse("77000000-0000-0000-0000-000000000204");
        Guid deviceId =
            Guid.Parse("77000000-0000-0000-0000-000000000205");
        Guid saleId =
            Guid.Parse("77000000-0000-0000-0000-000000000206");
        DateTimeOffset at =
            DateTimeOffset.Parse("2026-09-22T11:30:00Z");

        InsertUser(factory, adminId, at);
        InsertSale(factory, saleId, adminId, at);

        SqliteCafePrintService print = new(factory);

        _ = RequireValue(
            print.PublishStation(
                new CafePrinterStationMutation(
                    Guid.NewGuid(),
                    stationId,
                    storeId,
                    adminId,
                    "Backup Receipt",
                    CafePrinterStationKind.CustomerReceipt,
                    "front",
                    true,
                    0,
                    at)),
            "C7 backup station");

        _ = RequireValue(
            print.CreateReceiptJobAsync(
                    new CafeReceiptPrintJobMutation(
                        operationId,
                        jobId,
                        deviceId,
                        storeId,
                        adminId,
                        stationId,
                        saleId,
                        CafePrintCopyKind.Original,
                        null,
                        at.AddMinutes(1)))
                .GetAwaiter()
                .GetResult(),
            "C7 backup receipt job");

        _ = RequireValue(
            print.ExecuteAttemptAsync(
                    jobId,
                    deviceId,
                    adminId,
                    new NoOpReceiptPrinterAdapter("C7.Backup"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "C7 backup print attempt");

        SqliteBackupRecoveryService backup = new(paths);
        Result<string> created = backup.CreateBackup();

        Require(
            created.IsSuccess,
            "C7 backup creation failed.");

        Result<BackupVerificationSummary> verified =
            backup.VerifyBackup(created.Value);

        Require(
            verified.IsSuccess &&
            verified.Value.IsRestorable &&
            verified.Value.BackupMigrationVersion ==
                SalrynDatabaseMigrationCatalog.SupportedMigrationVersion &&
            verified.Value.SupportedMigrationVersion ==
                SalrynDatabaseMigrationCatalog.SupportedMigrationVersion &&
            verified.Value.MissingCriticalTables is { Count: 0 },
            "C7 backup verification did not require all current critical tables while preserving C7 print authority.");

        Guid postBackupStationId =
            Guid.Parse("77000000-0000-0000-0000-000000000207");

        _ = RequireValue(
            print.PublishStation(
                new CafePrinterStationMutation(
                    Guid.NewGuid(),
                    postBackupStationId,
                    storeId,
                    adminId,
                    "Post Backup Station",
                    CafePrinterStationKind.Kitchen,
                    "post-backup",
                    true,
                    0,
                    at.AddMinutes(2))),
            "C7 post-backup station mutation");

        Require(
            ScalarLong(
                factory,
                "SELECT COUNT(*) FROM cafe_printer_stations WHERE station_id=$station;",
                ("$station", postBackupStationId.ToString("D"))) == 1,
            "C7 post-backup mutation was not persisted before restore.");

        SqliteConnectionFactory.ClearAllPools();

        Result<BackupRestoreResult> restored =
            backup.RestoreVerifiedBackup(
                verified.Value,
                SqliteBackupRecoveryService.RestoreConfirmationText);

        Require(
            restored.IsSuccess,
            "C7 verified backup restore failed.");

        SqliteConnectionFactory restoredFactory = new(paths);

        Require(
            ScalarLong(
                restoredFactory,
                """
                SELECT COUNT(*)
                FROM cafe_print_jobs
                WHERE job_id=$job
                  AND operation_id=$operation
                  AND source_id=$sale;
                """,
                ("$job", jobId.ToString("D")),
                ("$operation", operationId.ToString("D")),
                ("$sale", saleId.ToString("D"))) == 1 &&
            ScalarLong(
                restoredFactory,
                """
                SELECT COUNT(*)
                FROM cafe_print_attempts
                WHERE job_id=$job
                  AND status=$status;
                """,
                ("$job", jobId.ToString("D")),
                ("$status", Convert.ToInt32(CafePrintAttemptStatus.Succeeded))) == 1 &&
            ScalarLong(
                restoredFactory,
                "SELECT COUNT(*) FROM cafe_printer_stations WHERE station_id=$station;",
                ("$station", stationId.ToString("D"))) == 1 &&
            ScalarLong(
                restoredFactory,
                "SELECT COUNT(*) FROM cafe_printer_stations WHERE station_id=$station;",
                ("$station", postBackupStationId.ToString("D"))) == 0 &&
            ScalarLong(
                restoredFactory,
                "SELECT COALESCE(MAX(version),0) FROM schema_migrations;") ==
                    SalrynDatabaseMigrationCatalog.SupportedMigrationVersion,
            "C7 restore did not recover print authority under the current migration ceiling or remove post-backup state.");

        Console.WriteLine(
            "PASS C7 backup verification and restore preserve migration-36 print authority");
    }

    private static void ProveMobileReadiness(string root)
    {
        MobileProductionComposition mobile =
            MobileProductionComposition.Create(root);

        mobile.InitializeDatabaseAsync().GetAwaiter().GetResult();

        MobileProductionReadiness readiness =
            mobile.InspectReadiness();

        Require(
            readiness.IsReady &&
            readiness.MaxMigrationVersion ==
                SalrynDatabaseMigrationCatalog.SupportedMigrationVersion &&
            readiness.Migration23Present &&
            readiness.RequiredSchemaPresent &&
            readiness.Tables.Contains(
                "cafe_printer_stations",
                StringComparer.OrdinalIgnoreCase) &&
            readiness.Tables.Contains(
                "cafe_print_jobs",
                StringComparer.OrdinalIgnoreCase) &&
            readiness.Tables.Contains(
                "cafe_print_attempts",
                StringComparer.OrdinalIgnoreCase) &&
            readiness.Triggers.Contains(
                "trg_cafe_print_jobs_no_update",
                StringComparer.OrdinalIgnoreCase) &&
            readiness.Triggers.Contains(
                "trg_cafe_print_attempts_no_delete",
                StringComparer.OrdinalIgnoreCase) &&
            mobile.CafePrint is not null,
            "C7 MobileProductionComposition is not ready for print authority under the current migration ceiling.");

        Console.WriteLine(
            "PASS C7 MobileProductionComposition migration-36 print readiness");
    }

    private static void InsertUser(
        SqliteConnectionFactory factory,
        Guid id,
        DateTimeOffset at)
    {
        Execute(
            factory,
            """
            INSERT INTO users(
                id,username,display_name,password_hash,
                role_id,status,created_at,updated_at)
            VALUES(
                $id,$username,'C7 Backup Admin','proof',
                $role,1,$at,$at);
            """,
            ("$id", id.ToString("D")),
            ("$username", "c7-backup-" + id.ToString("N")[..8]),
            ("$role", DefaultSeedData.AdminRoleId),
            ("$at", at.ToString("O")));
    }

    private static void InsertSale(
        SqliteConnectionFactory factory,
        Guid saleId,
        Guid actor,
        DateTimeOffset at)
    {
        Execute(
            factory,
            """
            INSERT INTO sales(
                id,receipt_no,sale_date,cashier_user_id,cashier_session_id,
                subtotal_cents,discount_cents,tax_cents,total_cents,
                amount_paid_cents,change_cents,status,
                voided_by_user_id,void_reason,voided_at,
                receipt_snapshot,created_at)
            VALUES(
                $id,'C7-BACKUP-001',$at,$actor,NULL,
                5000,0,0,5000,
                5000,0,$status,
                NULL,NULL,NULL,
                'C7 BACKUP RECEIPT SNAPSHOT',$at);
            """,
            ("$id", saleId.ToString("D")),
            ("$at", at.ToString("O")),
            ("$actor", actor.ToString("D")),
            ("$status", Convert.ToInt32(SaleStatus.Completed)));
    }

    private static long ScalarLong(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();
        using SqliteCommand command =
            connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L);
    }

    private static string ScalarString(
        SqliteConnectionFactory factory,
        string sql)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();
        using SqliteCommand command =
            connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())
            ?? throw new InvalidOperationException(
                "Expected scalar string.");
    }

    private static void Execute(
        SqliteConnectionFactory factory,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteConnection connection =
            factory.CreateConnection();
        connection.Open();
        using SqliteCommand command =
            connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static T RequireValue<T>(
        Result<T> result,
        string operation)
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

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

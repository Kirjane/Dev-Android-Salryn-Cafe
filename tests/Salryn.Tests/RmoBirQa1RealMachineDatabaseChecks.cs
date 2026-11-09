using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-QA1: real on-disk SQLite upgrade, backup/restore, future-version rejection, non-mutation.
/// Isolated under TEMP only. Never touches ProgramData operator databases.
/// </summary>
public static class RmoBirQa1RealMachineDatabaseChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        AssertSourceDocsExist(projectRoot);

        string uniqueId = Guid.NewGuid().ToString("N");
        string qaRoot = Path.Combine(Path.GetTempPath(), "Salryn-RMO-BIR-QA1-" + uniqueId);
        Console.WriteLine("[QA1] Isolated QA root: " + qaRoot);
        RequireTrue(
            qaRoot.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            "QA1 root must be under TEMP.");
        RequireFalse(
            qaRoot.Contains("ProgramData", StringComparison.OrdinalIgnoreCase),
            "QA1 must never use ProgramData.");

        Directory.CreateDirectory(qaRoot);

        try
        {
            RunCleanCurrent(qaRoot);
            RunV12ToCurrent(qaRoot);
            RunV14ToCurrent(qaRoot);
            RunFutureVersionRejection(qaRoot);
            RunBackupRestoreAndNonMutation(qaRoot);
            Console.WriteLine("[QA1] All automated real-machine scenarios passed.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try
            {
                if (Directory.Exists(qaRoot))
                {
                    Directory.Delete(qaRoot, recursive: true);
                    Console.WriteLine("[QA1] Cleaned QA root.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine("[QA1] WARNING: QA root cleanup incomplete: " + ex.Message);
            }
        }
    }

    private static void AssertSourceDocsExist(string projectRoot)
    {
        string study = Path.Combine(projectRoot, "00-HANDOFF", "RMO_BIR_QA1_REAL_MACHINE_STUDY.md");
        RequireTrue(File.Exists(study), "QA1 study document must exist.");
    }

    private const int CurrentSupportedMigrationVersion = 26;
    private const int FutureProbeMigrationVersion = 25;

    private static void RunCleanCurrent(string qaRoot)
    {
        Console.WriteLine("[QA1] A. Clean → v" + CurrentSupportedMigrationVersion);
        DbPaths paths = CreateQaDbPaths(qaRoot, "clean-v" + CurrentSupportedMigrationVersion);
        AssertInsideQaRoot(qaRoot, paths.DatabasePath);

        Result<DatabaseInitializationSummary> init =
            DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
        RequireTrue(init.IsSuccess, "Clean init must succeed: " + (init.IsFailure ? init.ErrorDetail.Message : ""));

        int maxVersion = ReadMaxMigrationVersion(paths.DatabasePath);
        RequireTrue(
            maxVersion == CurrentSupportedMigrationVersion,
            "Clean DB max migration must be " + CurrentSupportedMigrationVersion + ", was " + maxVersion);
        RequireTrue(TableExists(paths.DatabasePath, "official_receipt_profiles"), "clean must have official_receipt_profiles.");
        RequireTrue(TableExists(paths.DatabasePath, "official_dry_run_packages"), "clean must have official_dry_run_packages.");
        RequireTrue(ColumnExists(paths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
            "v15 clean must have vat_registration_mode column.");
        RequireTrue(
            CountMigrationRows(paths.DatabasePath) == CurrentSupportedMigrationVersion,
            "Exactly " + CurrentSupportedMigrationVersion + " migration rows required.");
        for (int v = 1; v <= CurrentSupportedMigrationVersion; v++)
        {
            RequireTrue(MigrationVersionPresent(paths.DatabasePath, v), "Migration " + v + " must be present.");
        }

        string[] critical = { "sales", "bir_machine_profiles", "bir_official_document_sequences",
            "bir_electronic_journal_entries", "official_receipt_profiles", "official_dry_run_packages", "backup_logs" };
        HashSet<string> tables = ReadTableNames(paths.DatabasePath);
        foreach (string table in critical)
        {
            RequireTrue(tables.Contains(table), "Critical table missing for current schema: " + table);
        }

        RequireIntegrityOk(paths.DatabasePath);
        RequireForeignKeysOk(paths.DatabasePath);
        Console.WriteLine("[QA1] A evidence: maxVersion=" + CurrentSupportedMigrationVersion + " tables ok integrity=ok");
    }

    private static void RunV12ToCurrent(string qaRoot)
    {
        Console.WriteLine("[QA1] B. Genuine v12 → v" + CurrentSupportedMigrationVersion);
        DbPaths paths = CreateQaDbPaths(qaRoot, "v12-upgrade");
        AssertInsideQaRoot(qaRoot, paths.DatabasePath);

        RunMigrationsThrough(paths, maxVersion: 12);
        new DatabaseSeeder(new SqliteConnectionFactory(paths)).SeedDefaultData();
        SentinelState before = InsertSentinelState(paths, includeReceiptProfile: false, label: "v12");

        RequireTrue(ReadMaxMigrationVersion(paths.DatabasePath) == 12, "Fixture must be genuine v12.");
        RequireFalse(TableExists(paths.DatabasePath, "official_receipt_profiles"), "v12 must not have official_receipt_profiles.");
        RequireFalse(TableExists(paths.DatabasePath, "official_dry_run_packages"), "v12 must not have official_dry_run_packages.");

        Result<DatabaseInitializationSummary> upgrade =
            DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
        RequireTrue(upgrade.IsSuccess, "v12→current upgrade must succeed: " + (upgrade.IsFailure ? upgrade.ErrorDetail.Message : ""));

        RequireTrue(
            ReadMaxMigrationVersion(paths.DatabasePath) == CurrentSupportedMigrationVersion,
            "After upgrade max must be " + CurrentSupportedMigrationVersion + ".");
        RequireTrue(MigrationVersionPresent(paths.DatabasePath, 13), "v13 must apply.");
        RequireTrue(MigrationVersionPresent(paths.DatabasePath, 14), "v14 must apply.");
        RequireTrue(MigrationVersionPresent(paths.DatabasePath, 15), "v15 must apply.");
        RequireTrue(TableExists(paths.DatabasePath, "official_receipt_profiles"), "v13 table required.");
        RequireTrue(TableExists(paths.DatabasePath, "official_dry_run_packages"), "v14 table required.");
        RequireTrue(ColumnExists(paths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
            "v15 column required.");

        SentinelState after = ReadSentinelState(paths.DatabasePath, before);
        AssertSentinelUnchanged(before, after);
        RequireIntegrityOk(paths.DatabasePath);
        RequireForeignKeysOk(paths.DatabasePath);
        Console.WriteLine(
            "[QA1] B evidence: seq " + before.SequenceCurrentNumber + "→" + after.SequenceCurrentNumber +
            " sale=" + after.SaleId + " ej=" + after.EjCount);
    }

    private static void RunV14ToCurrent(string qaRoot)
    {
        Console.WriteLine("[QA1] C. Genuine v14 → v" + CurrentSupportedMigrationVersion);
        DbPaths paths = CreateQaDbPaths(qaRoot, "v14-upgrade");
        AssertInsideQaRoot(qaRoot, paths.DatabasePath);

        RunMigrationsThrough(paths, maxVersion: 14);
        new DatabaseSeeder(new SqliteConnectionFactory(paths)).SeedDefaultData();
        SentinelState before = InsertSentinelState(paths, includeReceiptProfile: true, label: "v14");

        RequireTrue(ReadMaxMigrationVersion(paths.DatabasePath) == 14, "Fixture must be genuine v14.");
        RequireTrue(TableExists(paths.DatabasePath, "official_receipt_profiles"), "v14 must have receipt profiles.");
        RequireTrue(TableExists(paths.DatabasePath, "official_dry_run_packages"), "v14 must have dry-run packages.");
        RequireFalse(ColumnExists(paths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
            "v14 must not yet have vat_registration_mode.");

        Result<DatabaseInitializationSummary> upgrade =
            DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
        RequireTrue(upgrade.IsSuccess, "v14→current upgrade must succeed.");

        RequireTrue(
            ReadMaxMigrationVersion(paths.DatabasePath) == CurrentSupportedMigrationVersion,
            "After upgrade max must be " + CurrentSupportedMigrationVersion + ".");
        RequireTrue(ColumnExists(paths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
            "v15 column required.");
        RequireTrue(ReadReceiptProfileTitle(paths.DatabasePath, before.ReceiptProfileId!) == before.ReceiptTitle,
            "Receipt profile title must survive upgrade.");

        SentinelState after = ReadSentinelState(paths.DatabasePath, before);
        AssertSentinelUnchanged(before, after);
        RequireIntegrityOk(paths.DatabasePath);
        RequireForeignKeysOk(paths.DatabasePath);
        Console.WriteLine("[QA1] C evidence: receipt title preserved; seq unchanged " + after.SequenceCurrentNumber);
    }

    private static void RunFutureVersionRejection(string qaRoot)
    {
        Console.WriteLine("[QA1] D. Future v" + FutureProbeMigrationVersion + " rejection");
        DbPaths paths = CreateQaDbPaths(qaRoot, "future-v" + FutureProbeMigrationVersion);
        AssertInsideQaRoot(qaRoot, paths.DatabasePath);

        Result<DatabaseInitializationSummary> init =
            DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
        RequireTrue(init.IsSuccess, "Current live QA DB init must succeed.");
        InsertSentinelState(paths, includeReceiptProfile: true, label: "futurelive");
        long salesBefore = CountRows(paths.DatabasePath, "sales");
        int seqBefore = ReadSequenceCurrent(paths.DatabasePath);

        SqliteBackupRecoveryService recovery = new(paths);
        Result<string> backupResult = recovery.CreateBackup();
        RequireTrue(backupResult.IsSuccess, "Backup for future probe must succeed.");
        string goodBackup = backupResult.Value;
        AssertInsideQaRoot(qaRoot, goodBackup);

        string futureBackup = Path.Combine(paths.BackupPath, "future-v" + FutureProbeMigrationVersion + "-probe.db");
        File.Copy(goodBackup, futureBackup, overwrite: true);
        using (SqliteConnection connection = OpenRw(futureBackup))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO schema_migrations (version, name, applied_at)
                VALUES ($version, 'qa1-future-probe', $appliedAt);
                """;
            command.Parameters.AddWithValue("$version", FutureProbeMigrationVersion);
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        Result<BackupVerificationSummary> futureVerify = recovery.VerifyBackup(futureBackup);
        RequireTrue(futureVerify.IsSuccess, "Future backup must verify without throw.");
        RequireTrue(futureVerify.Value.HasFutureMigrationVersion, "Must detect future migration.");
        RequireFalse(futureVerify.Value.MigrationVersionCompatible, "Future must be incompatible.");
        RequireFalse(futureVerify.Value.IsRestorable, "Future must not be restorable.");

        Result<BackupRestoreResult> restore = recovery.RestoreVerifiedBackup(
            futureVerify.Value,
            SqliteBackupRecoveryService.RestoreConfirmationText);
        RequireTrue(restore.IsFailure, "Future restore must be rejected.");

        RequireTrue(
            ReadMaxMigrationVersion(paths.DatabasePath) == CurrentSupportedMigrationVersion,
            "Live QA DB must remain at current supported version.");
        RequireTrue(CountRows(paths.DatabasePath, "sales") == salesBefore, "Live sales count must be unchanged after rejected restore.");
        RequireTrue(ReadSequenceCurrent(paths.DatabasePath) == seqBefore, "Live sequence must be unchanged after rejected restore.");
        Console.WriteLine("[QA1] D evidence: HasFuture=true IsRestorable=false live unchanged");
    }

    private static void RunBackupRestoreAndNonMutation(string qaRoot)
    {
        Console.WriteLine("[QA1] E/F. Backup/restore + source-sale/candidate non-mutation");
        DbPaths paths = CreateQaDbPaths(qaRoot, "backup-restore");
        AssertInsideQaRoot(qaRoot, paths.DatabasePath);

        Result<DatabaseInitializationSummary> init =
            DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
        RequireTrue(init.IsSuccess, "Backup scenario init must succeed.");

        SentinelState before = InsertSentinelState(paths, includeReceiptProfile: true, label: "br");
        SqliteConnectionFactory factory = new(paths);

        // Synthetic dry-run package
        SqliteHostOfficialDryRunPackageService packages = new(factory);
        Result<OfficialDryRunPackageContract> synth = packages.CreateAsync(
            new OfficialDryRunPackageCreateRequest(null),
            createdByUserId: before.UserId).GetAwaiter().GetResult();
        RequireTrue(synth.IsSuccess, "Synthetic dry-run package must create: " + (synth.IsFailure ? synth.ErrorDetail.Message : ""));

        // OperationalSaleReadOnly package from sentinel sale
        Result<OfficialDryRunPackageContract> fromSale = packages.CreateFromSaleAsync(
            Guid.Parse(before.SaleId),
            createdByUserId: before.UserId).GetAwaiter().GetResult();
        RequireTrue(fromSale.IsSuccess, "From-sale dry-run package must create: " + (fromSale.IsFailure ? fromSale.ErrorDetail.Message : ""));
        RequireTrue(
            fromSale.Value.SourceMode == OfficialDryRunPackageSourceMode.OperationalSaleReadOnly,
            "Source mode must be OperationalSaleReadOnly.");
        RequireTrue(fromSale.Value.Flags.LiveOfficialPostingAllowed == false, "liveOfficialPostingAllowed must be false.");
        RequireTrue(fromSale.Value.Flags.SequenceWillMutate == false, "sequenceWillMutate must be false.");
        RequireTrue(fromSale.Value.Flags.SalesWillMutate == false, "salesWillMutate must be false.");
        RequireTrue(fromSale.Value.Flags.EjWillAppendLive == false, "ejWillAppendLive must be false.");
        RequireTrue(fromSale.Value.Flags.OutputWillGenerate == false, "outputWillGenerate must be false.");
        RequireTrue(fromSale.Value.Flags.ComplianceClaimed == false, "complianceClaimed must be false.");

        SentinelState afterPackages = ReadSentinelState(paths.DatabasePath, before);
        AssertSentinelUnchanged(before, afterPackages);
        long dryRunCount = CountRows(paths.DatabasePath, "official_dry_run_packages");
        RequireTrue(dryRunCount >= 2, "At least two dry-run packages expected.");

        // Candidate tracks — always blocked
        SqliteHostOfficialLivePostingCandidateService candidate = new(factory);
        Result<OfficialLivePostingCandidateResult> traditional = candidate.EvaluateAsync(
            new OfficialLivePostingCandidateRequest(
                Guid.Parse(before.SaleId),
                "OFFICIAL_INVOICE",
                ExplicitInternalActivationRequested: false,
                PreferredTrack: OfficialLivePostingCandidateTrack.TraditionalCrmPos)).GetAwaiter().GetResult();
        RequireTrue(traditional.IsSuccess, "Traditional candidate must evaluate.");
        RequireTrue(traditional.Value.Blocked, "Traditional candidate must be blocked.");
        RequireTrue(
            traditional.Value.TerminalStage == OfficialLivePostingCandidateStage.BlockBeforeMutation,
            "Traditional terminal stage must be BlockBeforeMutation.");
        RequireTrue(traditional.Value.SequenceWillMutate == false, "Candidate sequenceWillMutate false.");
        RequireTrue(traditional.Value.EisWillTransmit == false, "Candidate eisWillTransmit false.");

        Result<OfficialLivePostingCandidateResult> eis = candidate.EvaluateAsync(
            new OfficialLivePostingCandidateRequest(
                Guid.Parse(before.SaleId),
                "OFFICIAL_INVOICE",
                ExplicitInternalActivationRequested: false,
                PreferredTrack: OfficialLivePostingCandidateTrack.EisInvoiceReceipt)).GetAwaiter().GetResult();
        RequireTrue(eis.IsSuccess, "EIS candidate must evaluate.");
        RequireTrue(eis.Value.Blocked, "EIS candidate must be blocked.");
        RequireTrue(
            eis.Value.TerminalStage == OfficialLivePostingCandidateStage.BlockBeforeMutation,
            "EIS terminal stage must be BlockBeforeMutation.");
        RequireTrue(eis.Value.EisWillTransmit == false, "EIS transmit must remain false.");
        RequireTrue(
            eis.Value.Blockers.Any(b => b.Code.Contains("EisJsonSchema", StringComparison.OrdinalIgnoreCase)),
            "EIS JSON schema unresolved blocker required.");

        SentinelState afterCandidate = ReadSentinelState(paths.DatabasePath, before);
        AssertSentinelUnchanged(before, afterCandidate);

        // Backup + deliberate change + restore
        SqliteBackupRecoveryService recovery = new(paths);
        Result<string> backupResult = recovery.CreateBackup();
        RequireTrue(backupResult.IsSuccess, "CreateBackup must succeed.");
        string backupPath = backupResult.Value;
        AssertInsideQaRoot(qaRoot, backupPath);
        RequireTrue(File.Exists(backupPath), "Backup file must exist.");
        RequireFalse(File.Exists(backupPath + "-wal"), "Backup WAL sidecar must not remain.");
        RequireFalse(File.Exists(backupPath + "-shm"), "Backup SHM sidecar must not remain.");

        Result<BackupVerificationSummary> verify = recovery.VerifyBackup(backupPath);
        RequireTrue(verify.IsSuccess, "VerifyBackup must succeed.");
        RequireTrue(
            verify.Value.BackupMigrationVersion == CurrentSupportedMigrationVersion,
            "Backup migration version must be " + CurrentSupportedMigrationVersion + ".");
        RequireTrue(verify.Value.CriticalTablesExist, "Critical tables must exist.");
        RequireTrue(verify.Value.MigrationVersionCompatible, "Must be migration-compatible.");
        RequireTrue(verify.Value.IsRestorable, "Must be restorable.");
        RequireFalse(verify.Value.HasFutureMigrationVersion, "Must not be future.");

        // Deliberate live mutation after backup
        using (SqliteConnection live = OpenRw(paths.DatabasePath))
        {
            using SqliteCommand cmd = live.CreateCommand();
            cmd.CommandText = "UPDATE sales SET total_cents = total_cents + 1 WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", before.SaleId);
            cmd.ExecuteNonQuery();
        }

        RequireTrue(ReadSaleTotal(paths.DatabasePath, before.SaleId) == before.SaleTotalCents + 1, "Live DB must show deliberate change.");

        SqliteConnectionFactory.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        SqliteConnectionFactory.ClearAllPools();

        Result<BackupRestoreResult> restore = recovery.RestoreVerifiedBackup(
            verify.Value,
            SqliteBackupRecoveryService.RestoreConfirmationText);
        RequireTrue(restore.IsSuccess, "Restore must succeed: " + (restore.IsFailure ? restore.ErrorDetail.Message : ""));
        RequireTrue(File.Exists(restore.Value.SafetyBackupPath), "Safety backup must exist.");
        AssertInsideQaRoot(qaRoot, restore.Value.SafetyBackupPath);

        SqliteConnectionFactory.ClearAllPools();
        SentinelState restored = ReadSentinelState(paths.DatabasePath, before);
        AssertSentinelUnchanged(before, restored);
        AssertOfficialSetupSurvivedRestore(paths.DatabasePath, before);
        RequireTrue(ReadSaleTotal(paths.DatabasePath, before.SaleId) == before.SaleTotalCents, "Restored sale total must match pre-backup.");
        RequireTrue(CountRows(paths.DatabasePath, "official_dry_run_packages") >= 2, "Dry-run packages must survive restore.");
        RequireIntegrityOk(paths.DatabasePath);
        RequireForeignKeysOk(paths.DatabasePath);

        Console.WriteLine(
            "[QA1] E/F evidence: backup ok restore ok safety=" + Path.GetFileName(restore.Value.SafetyBackupPath) +
            " dryRunPackages=" + CountRows(paths.DatabasePath, "official_dry_run_packages") +
            " seq=" + restored.SequenceCurrentNumber +
            " receiptTitle=" + restored.ReceiptTitle +
            " receiptKind=" + restored.ReceiptDocumentKind);
    }

    // --- fixture helpers ---

    private static DbPaths CreateQaDbPaths(string qaRoot, string leaf)
    {
        string root = Path.Combine(qaRoot, leaf);
        Directory.CreateDirectory(root);
        DbPaths paths = DbPaths.CreateDevelopment(root);
        paths.EnsureDirectories();
        return paths;
    }

    private static void AssertInsideQaRoot(string qaRoot, string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(qaRoot);
        RequireTrue(
            full.StartsWith(root, StringComparison.OrdinalIgnoreCase),
            "Path escaped QA root: " + full);
    }

    private static IDatabaseMigration[] AllMigrationsOrdered() =>
        new IDatabaseMigration[]
        {
            new InitialSchemaMigration(),
            new BirMachineProfileSchemaMigration(),
            new BirOfficialDocumentSequenceSchemaMigration(),
            new BirElectronicJournalSchemaMigration(),
            new BirElectronicJournalSchemaHardeningMigration(),
            new BirZReadingSchemaMigration(),
            new BirElectronicJournalCorrectionEventSchemaMigration(),
            new ProductTaxClassificationSchemaMigration(),
            new DeploymentIdentitySchemaMigration(),
            new TerminalRegistrySchemaMigration(),
            new LanRemoteTransportSchemaMigration(),
            new TerminalCredentialHandshakeSchemaMigration(),
            new OfficialReceiptProfileSchemaMigration(),
            new OfficialDryRunPackageSchemaMigration(),
            new VatRegistrationModeSchemaMigration(),
            new OfficialDocumentLifecycleCompletionSchemaMigration(),
            new OfficialDocumentLifecycleAccountingSchemaMigration()
        };

    private static void RunMigrationsThrough(DbPaths paths, int maxVersion)
    {
        paths.EnsureDirectories();
        if (File.Exists(paths.DatabasePath))
        {
            File.Delete(paths.DatabasePath);
        }

        SqliteConnectionFactory factory = new(paths);
        IDatabaseMigration[] subset = AllMigrationsOrdered()
            .Where(m => m.Version <= maxVersion)
            .OrderBy(m => m.Version)
            .ToArray();
        new MigrationRunner(factory, subset).RunPendingMigrations();
    }

    private sealed record SentinelState(
        string UserId,
        string ProductId,
        string SaleId,
        long SaleTotalCents,
        int SaleStatus,
        string SequenceId,
        int SequenceCurrentNumber,
        long EjCount,
        long StockMovementCount,
        string? SessionId,
        int? SessionStatus,
        string? ReceiptProfileId,
        string? ReceiptTitle,
        string? ReceiptDocumentKind,
        string MachineProfileId);

    private static SentinelState InsertSentinelState(DbPaths paths, bool includeReceiptProfile, string label)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        string userId = Guid.NewGuid().ToString("D");
        string productId = Guid.NewGuid().ToString("D");
        string saleId = Guid.NewGuid().ToString("D");
        string sessionId = Guid.NewGuid().ToString("D");
        string machineId = Guid.NewGuid().ToString("D");
        string sequenceId = Guid.NewGuid().ToString("D");
        string unitId;
        string roleId;
        const int sequenceCurrent = 42;
        const long saleTotal = 15000;

        using SqliteConnection connection = OpenRw(paths.DatabasePath);
        roleId = ScalarString(connection, "SELECT id FROM roles ORDER BY name LIMIT 1;")
                 ?? throw new InvalidOperationException("Seed roles required.");
        unitId = ScalarString(connection, "SELECT id FROM units ORDER BY name LIMIT 1;")
                 ?? throw new InvalidOperationException("Seed units required.");

        Exec(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ($id, $username, $display, 'qa1-hash', $roleId, 1, $now);
            """,
            ("$id", userId),
            ("$username", "qa1_" + label + "_" + userId[..8]),
            ("$display", "QA1 User " + label),
            ("$roleId", roleId),
            ("$now", now));

        Exec(connection, """
            INSERT INTO products (
                id, sku, barcode, name, description, category_id, brand_id, unit_id, supplier_id,
                cost_price_cents, selling_price_cents, wholesale_price_cents,
                stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount, is_active, created_at)
            VALUES (
                $id, NULL, $barcode, $name, NULL, NULL, NULL, $unitId, NULL,
                1000, 1500, NULL,
                10000, 0, 1, 1, 1, $now);
            """,
            ("$id", productId),
            ("$name", "QA1 Product " + label),
            ("$barcode", "QA1" + label.ToUpperInvariant() + productId[..6]),
            ("$unitId", unitId),
            ("$now", now));

        // stock_on_hand may not exist as column - use stock_movements only
        Exec(connection, """
            INSERT INTO stock_movements (
                id, product_id, movement_type, qty_change_x1000, cost_price_cents,
                reference_type, reference_id, reason, user_id, created_at)
            VALUES ($id, $productId, 1, 5000, 1000, 'QA1', $ref, 'qa1 seed', $userId, $now);
            """,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$productId", productId),
            ("$ref", saleId),
            ("$userId", userId),
            ("$now", now));

        Exec(connection, """
            INSERT INTO cashier_sessions (
                id, user_id, opened_at, closed_at, opening_cash_cents, expected_cash_cents,
                counted_cash_cents, variance_cents, status, notes)
            VALUES ($id, $userId, $now, NULL, 0, 0, NULL, 0, 1, 'qa1 open');
            """,
            ("$id", sessionId),
            ("$userId", userId),
            ("$now", now));

        Exec(connection, """
            INSERT INTO sales (
                id, receipt_no, sale_date, cashier_user_id, cashier_session_id,
                subtotal_cents, discount_cents, tax_cents, total_cents,
                amount_paid_cents, change_cents, status, created_at)
            VALUES (
                $id, $receipt, $now, $userId, $sessionId,
                $total, 0, 0, $total,
                $total, 0, 2, $now);
            """,
            ("$id", saleId),
            ("$receipt", "QA1-" + label.ToUpperInvariant() + "-" + saleId[..8]),
            ("$now", now),
            ("$userId", userId),
            ("$sessionId", sessionId),
            ("$total", saleTotal));

        Exec(connection, """
            INSERT INTO sale_items (
                id, sale_id, product_id, product_name_snapshot, barcode_snapshot,
                unit_name_snapshot, qty_x1000, unit_price_cents, cost_price_cents,
                discount_cents, line_total_cents)
            VALUES (
                $id, $saleId, $productId, 'QA1 Product', NULL,
                'pc', 1000, 1500, 1000, 0, 1500);
            """,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$saleId", saleId),
            ("$productId", productId));

        Exec(connection, """
            INSERT INTO payments (id, sale_id, method, amount_cents, paid_at)
            VALUES ($id, $saleId, 1, $total, $now);
            """,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$saleId", saleId),
            ("$total", saleTotal),
            ("$now", now));

        Exec(connection, """
            INSERT INTO bir_machine_profiles (
                id, registration_scope_code, store_settings_id,
                registered_taxpayer_name, business_name, registered_address, branch_address,
                taxpayer_identification_number, branch_code,
                machine_identification_number, machine_serial_number, pos_terminal_number,
                accreditation_number, permit_to_use_number,
                software_name, software_version, software_build,
                compliance_mode, official_output_enabled, is_active, created_at)
            VALUES (
                $id, 'LOCAL_SINGLE_STORE', NULL,
                'QA1 Taxpayer', 'QA1 Biz', 'QA1 Address', NULL,
                '123456789', '00000',
                'MIN-QA1', $serial, 'T1',
                NULL, NULL,
                'Salryn', 'QA1', 'qa1',
                'NOT_ACCREDITED', 0, 1, $now);
            """,
            ("$id", machineId),
            ("$serial", "SN-QA1-" + label),
            ("$now", now));

        Exec(connection, """
            INSERT INTO bir_official_document_sequences (
                id, bir_machine_profile_id, document_kind, sequence_scope_code,
                prefix, year, start_number, current_number, end_number, padding_digits,
                is_active, is_exhausted, created_at)
            VALUES (
                $id, $machineId, 'OFFICIAL_INVOICE', 'LOCAL_SINGLE_STORE',
                'INV', $year, 1, $current, NULL, 6,
                1, 0, $now);
            """,
            ("$id", sequenceId),
            ("$machineId", machineId),
            ("$year", DateTime.UtcNow.Year),
            ("$current", sequenceCurrent),
            ("$now", now));

        // EJ requires sale + sequence + machine + user — insert one entry for count invariant
        string ejId = Guid.NewGuid().ToString("D");
        Exec(connection, """
            INSERT INTO bir_electronic_journal_entries (
                id, bir_machine_profile_id, sale_id, official_document_sequence_id,
                official_document_number, document_kind, transaction_timestamp,
                cashier_user_id, terminal_id, created_at)
            VALUES (
                $id, $machineId, $saleId, $sequenceId,
                'PREVIEW-000042', 'OFFICIAL_INVOICE', $now,
                $userId, 'QA1', $now);
            """,
            ("$id", ejId),
            ("$machineId", machineId),
            ("$saleId", saleId),
            ("$sequenceId", sequenceId),
            ("$userId", userId),
            ("$now", now));

        string? receiptId = null;
        string? receiptTitle = null;
        string? receiptKind = null;
        if (includeReceiptProfile && TableExists(paths.DatabasePath, "official_receipt_profiles"))
        {
            receiptId = Guid.NewGuid().ToString("D");
            receiptTitle = "QA1 Invoice " + label;
            receiptKind = "OFFICIAL_INVOICE";
            Exec(connection, """
                INSERT INTO official_receipt_profiles (
                    id, bir_machine_profile_id, document_title, document_kind, footer_legal_text,
                    supplementary_disclaimer_text, reprint_marker_text, training_marker_text,
                    void_refund_marker_text, invoice_or_transition_mode, sc_pwd_placeholder_enabled,
                    dry_run_only, created_at, updated_at, updated_by_user_id)
                VALUES (
                    $id, $machineId, $title, $kind, 'QA1 footer',
                    NULL, 'REPRINT', 'TRAINING - NOT OFFICIAL',
                    NULL, NULL, 0,
                    1, $now, NULL, NULL);
                """,
                ("$id", receiptId),
                ("$machineId", machineId),
                ("$title", receiptTitle),
                ("$kind", receiptKind),
                ("$now", now));
        }

        long ejCount = CountRows(paths.DatabasePath, "bir_electronic_journal_entries");
        long stockCount = CountRows(paths.DatabasePath, "stock_movements");

        return new SentinelState(
            userId, productId, saleId, saleTotal, 2,
            sequenceId, sequenceCurrent, ejCount, stockCount,
            sessionId, 1, receiptId, receiptTitle, receiptKind, machineId);
    }

    private static SentinelState ReadSentinelState(string databasePath, SentinelState template)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        long saleTotal = Convert.ToInt64(Scalar(connection, "SELECT total_cents FROM sales WHERE id = $id;", ("$id", template.SaleId)) ?? 0L);
        int saleStatus = Convert.ToInt32(Scalar(connection, "SELECT status FROM sales WHERE id = $id;", ("$id", template.SaleId)) ?? 0);
        int seq = Convert.ToInt32(Scalar(connection, "SELECT current_number FROM bir_official_document_sequences WHERE id = $id;", ("$id", template.SequenceId)) ?? 0);
        long ej = CountRows(databasePath, "bir_electronic_journal_entries");
        long stock = CountRows(databasePath, "stock_movements");
        int? sessionStatus = null;
        if (template.SessionId is not null)
        {
            object? s = Scalar(connection, "SELECT status FROM cashier_sessions WHERE id = $id;", ("$id", template.SessionId));
            if (s is not null)
            {
                sessionStatus = Convert.ToInt32(s);
            }
        }

        // Reread official setup rows from disk (do not carry template receipt/machine fields blindly).
        string? receiptId = null;
        string? receiptTitle = null;
        string? receiptKind = null;
        string? receiptMachineId = null;
        if (template.ReceiptProfileId is not null && TableExists(databasePath, "official_receipt_profiles"))
        {
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, document_title, document_kind, bir_machine_profile_id
                FROM official_receipt_profiles
                WHERE id = $id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", template.ReceiptProfileId);
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                receiptId = reader.GetString(0);
                receiptTitle = reader.GetString(1);
                receiptKind = reader.GetString(2);
                receiptMachineId = reader.GetString(3);
            }
        }

        string? machineId = null;
        if (TableExists(databasePath, "bir_machine_profiles"))
        {
            object? m = Scalar(connection, "SELECT id FROM bir_machine_profiles WHERE id = $id;", ("$id", template.MachineProfileId));
            machineId = m?.ToString();
        }

        string? sequenceMachineId = Scalar(
            connection,
            "SELECT bir_machine_profile_id FROM bir_official_document_sequences WHERE id = $id;",
            ("$id", template.SequenceId))?.ToString();

        // Stash linkage checks on unused template fields via require after return in caller;
        // store machine id only when present.
        RequireTrue(machineId is not null, "Machine profile must be reread from database.");
        if (template.ReceiptProfileId is not null)
        {
            RequireTrue(receiptId is not null, "Receipt profile must be reread from database.");
            RequireTrue(receiptMachineId == template.MachineProfileId,
                "Receipt profile must still reference expected machine profile.");
            RequireTrue(sequenceMachineId == template.MachineProfileId,
                "Sequence must still reference expected machine profile.");
        }

        return template with
        {
            SaleTotalCents = saleTotal,
            SaleStatus = saleStatus,
            SequenceCurrentNumber = seq,
            EjCount = ej,
            StockMovementCount = stock,
            SessionStatus = sessionStatus,
            ReceiptProfileId = receiptId,
            ReceiptTitle = receiptTitle,
            ReceiptDocumentKind = receiptKind,
            MachineProfileId = machineId ?? template.MachineProfileId
        };
    }

    private static void AssertSentinelUnchanged(SentinelState before, SentinelState after)
    {
        RequireTrue(before.SaleId == after.SaleId, "Sale id must match.");
        RequireTrue(before.SaleTotalCents == after.SaleTotalCents, "Sale total must not change. before=" + before.SaleTotalCents + " after=" + after.SaleTotalCents);
        RequireTrue(before.SaleStatus == after.SaleStatus, "Sale status must not change.");
        RequireTrue(before.SequenceCurrentNumber == after.SequenceCurrentNumber,
            "Sequence current_number must not change. before=" + before.SequenceCurrentNumber + " after=" + after.SequenceCurrentNumber);
        RequireTrue(before.EjCount == after.EjCount, "EJ count must not change. before=" + before.EjCount + " after=" + after.EjCount);
        RequireTrue(before.StockMovementCount == after.StockMovementCount, "Stock movement count must not change.");
        RequireTrue(before.SessionStatus == after.SessionStatus, "Session status must not change.");
        RequireTrue(before.MachineProfileId == after.MachineProfileId, "Machine profile id must survive.");
        if (before.ReceiptProfileId is not null)
        {
            RequireTrue(before.ReceiptProfileId == after.ReceiptProfileId, "Receipt profile id must survive.");
            RequireTrue(before.ReceiptTitle == after.ReceiptTitle, "Receipt document title must survive.");
            RequireTrue(before.ReceiptDocumentKind == after.ReceiptDocumentKind, "Receipt document kind must survive.");
        }
    }

    private static void AssertOfficialSetupSurvivedRestore(string databasePath, SentinelState before)
    {
        RequireTrue(before.ReceiptProfileId is not null, "Backup/restore fixture must include receipt profile.");
        using SqliteConnection connection = OpenRo(databasePath);

        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, document_title, document_kind, bir_machine_profile_id
                FROM official_receipt_profiles
                WHERE id = $id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", before.ReceiptProfileId!);
            using SqliteDataReader reader = cmd.ExecuteReader();
            RequireTrue(reader.Read(), "Post-restore: receipt profile row must exist.");
            string id = reader.GetString(0);
            string title = reader.GetString(1);
            string kind = reader.GetString(2);
            string machineFromReceipt = reader.GetString(3);
            RequireTrue(id == before.ReceiptProfileId, "Post-restore receipt profile id must match.");
            RequireTrue(title == before.ReceiptTitle, "Post-restore receipt title must match. got=" + title);
            RequireTrue(kind == before.ReceiptDocumentKind, "Post-restore document kind must match. got=" + kind);
            RequireTrue(machineFromReceipt == before.MachineProfileId, "Post-restore receipt machine FK must match.");
            Console.WriteLine("[QA1] Post-restore receipt: id=" + id + " title=" + title + " kind=" + kind);
        }

        object? machine = Scalar(connection, "SELECT id FROM bir_machine_profiles WHERE id = $id;", ("$id", before.MachineProfileId));
        RequireTrue(machine is not null, "Post-restore: machine profile must exist.");

        object? seqMachine = Scalar(
            connection,
            "SELECT bir_machine_profile_id FROM bir_official_document_sequences WHERE id = $id;",
            ("$id", before.SequenceId));
        RequireTrue(seqMachine?.ToString() == before.MachineProfileId, "Post-restore: sequence must reference machine profile.");

        int seq = Convert.ToInt32(Scalar(
            connection,
            "SELECT current_number FROM bir_official_document_sequences WHERE id = $id;",
            ("$id", before.SequenceId)) ?? -1);
        RequireTrue(seq == before.SequenceCurrentNumber, "Post-restore sequence current_number must match.");
        Console.WriteLine("[QA1] Post-restore machine=" + before.MachineProfileId + " seq=" + seq);
    }

    // --- SQL helpers ---

    private static SqliteConnection OpenRw(string databasePath)
    {
        SqliteConnection connection = new("Data Source=" + databasePath);
        connection.Open();
        SqlitePragma.Apply(connection);
        return connection;
    }

    private static SqliteConnection OpenRo(string databasePath)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command.ExecuteScalar();
    }

    private static string? ScalarString(SqliteConnection connection, string sql) =>
        Scalar(connection, sql)?.ToString();

    private static int ReadMaxMigrationVersion(string databasePath)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        object? value = Scalar(connection, "SELECT MAX(version) FROM schema_migrations;");
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static int CountMigrationRows(string databasePath)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        return Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM schema_migrations;") ?? 0);
    }

    private static bool MigrationVersionPresent(string databasePath, int version)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        object? value = Scalar(connection, "SELECT COUNT(*) FROM schema_migrations WHERE version = $v;", ("$v", version));
        return Convert.ToInt32(value ?? 0) == 1;
    }

    private static bool TableExists(string databasePath, string table)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        object? value = Scalar(connection, """
            SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n LIMIT 1;
            """, ("$n", table));
        return value is not null;
    }

    private static bool ColumnExists(string databasePath, string table, string column)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(" + table + ");";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> ReadTableNames(string databasePath)
    {
        HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
        using SqliteConnection connection = OpenRo(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static long CountRows(string databasePath, string table)
    {
        if (!TableExists(databasePath, table))
        {
            return 0;
        }

        using SqliteConnection connection = OpenRo(databasePath);
        // table names are internal constants only
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static int ReadSequenceCurrent(string databasePath)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        object? value = Scalar(connection, "SELECT current_number FROM bir_official_document_sequences ORDER BY created_at LIMIT 1;");
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static long ReadSaleTotal(string databasePath, string saleId)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        return Convert.ToInt64(Scalar(connection, "SELECT total_cents FROM sales WHERE id = $id;", ("$id", saleId)) ?? 0L);
    }

    private static string? ReadReceiptProfileTitle(string databasePath, string id)
    {
        if (!TableExists(databasePath, "official_receipt_profiles"))
        {
            return null;
        }

        using SqliteConnection connection = OpenRo(databasePath);
        return Scalar(connection, "SELECT document_title FROM official_receipt_profiles WHERE id = $id;", ("$id", id))?.ToString();
    }

    private static void RequireIntegrityOk(string databasePath)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        string result = Scalar(connection, "PRAGMA integrity_check;")?.ToString() ?? "";
        RequireTrue(string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase), "integrity_check failed: " + result);
    }

    private static void RequireForeignKeysOk(string databasePath)
    {
        using SqliteConnection connection = OpenRo(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader reader = command.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidOperationException("foreign_key_check returned rows.");
        }
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool condition, string message) => RequireTrue(!condition, message);
}

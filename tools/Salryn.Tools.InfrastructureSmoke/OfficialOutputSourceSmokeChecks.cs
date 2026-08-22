using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;

internal static class OfficialOutputSourceSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("26000000-0000-0000-0000-000000000001");
    private static readonly Guid MachineProfileId = Guid.Parse("26000000-0000-0000-0000-000000000002");
    private static readonly Guid SequenceId = Guid.Parse("26000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("26000000-0000-0000-0000-000000000004");

    private const long InitialStockX1000 = 12000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL OUTPUT SOURCE SMOKE CHECKS =====");

        RunActiveOutputSourcePath();
        RunFailurePaths();
        RunLifecycleAndZCoveragePath();

        Console.WriteLine("Official output source smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunActiveOutputSourcePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-output-source-active");
        SeedBaseDependencies(database.Factory);

        Guid journalEntryId = SeedDocument(
            database.Factory,
            "OR-2026-009001",
            new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
            snapshotHashOverride: null,
            "ORIGINAL");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        OfficialOutputSourceResult output = ReadOutput(database.Services, journalEntryId);

        RequireEqual("OR-2026-009001", output.Header.OfficialDocumentNumber, "Output source must expose official document number.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, output.Header.DocumentKind, "Output source must expose document kind.");
        RequireEqual("Official Output Cashier", output.Header.CashierIdentifier, "Output source must expose snapshot cashier identity.");
        RequireEqual("POS-OUTPUT", output.Header.TerminalId, "Output source must expose journal terminal identity.");
        RequireEqual("Output Source Taxpayer", output.MachineProfile.RegisteredTaxpayerName, "Output source must expose persisted machine profile facts.");
        RequireEqual("Trusted Output Item", output.LineItems[0].Description, "Output source must parse trusted line item evidence.");
        RequireEqual(1200L, output.Totals.TotalCents, "Output source must parse trusted totals.");
        RequireEqual(PaymentMethod.Cash, output.Payments[0].Method, "Output source must parse trusted payment evidence.");
        RequireTrue(output.Evidence.SnapshotHashMatches, "Output source must verify snapshot hash.");
        RequireEqual(OfficialDocumentLifecycleState.Active, output.Lifecycle.State, "Output source must include lifecycle state.");
        RequireTrue(!output.Evidence.SnapshotPayloadJson.Contains("legacy sale snapshot sentinel", StringComparison.Ordinal), "Output source must not use legacy sale snapshot text.");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialOutputSource.ActiveRead");
    }

    private static void RunFailurePaths()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-output-source-failures");
        SeedBaseDependencies(database.Factory);

        Guid missingOriginalEntryId = SeedDocument(
            database.Factory,
            "OR-2026-009101",
            new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero),
            snapshotHashOverride: null);

        Guid mismatchEntryId = SeedDocument(
            database.Factory,
            "OR-2026-009102",
            new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            snapshotHashOverride: "bad-hash",
            "ORIGINAL");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        ExpectOutputFailure(
            database.Services.OfficialOutputSourceRead.ReadAsync(new OfficialOutputSourceReadRequest(
                JournalEntryId: Guid.Parse("26000000-0000-0000-0000-000000009999"),
                OfficialDocumentNumber: null,
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult(),
            "OfficialOutputSource.JournalEntryNotFound");

        ExpectOutputFailure(
            database.Services.OfficialOutputSourceRead.ReadAsync(new OfficialOutputSourceReadRequest(
                JournalEntryId: missingOriginalEntryId,
                OfficialDocumentNumber: null,
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult(),
            "OfficialOutputSource.OriginalEventMissing");

        ExpectOutputFailure(
            database.Services.OfficialOutputSourceRead.ReadAsync(new OfficialOutputSourceReadRequest(
                JournalEntryId: mismatchEntryId,
                OfficialDocumentNumber: null,
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult(),
            "OfficialOutputSource.SnapshotHashMismatch");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialOutputSource.FailureReads");
    }

    private static void RunLifecycleAndZCoveragePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-output-source-lifecycle");
        SeedBaseDependencies(database.Factory);

        Guid voidedEntryId = SeedDocument(
            database.Factory,
            "OR-2026-009201",
            new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            snapshotHashOverride: null,
            "ORIGINAL",
            "CORRECTION:Corrected tender note.",
            "VOID");

        Guid cancelledEntryId = SeedDocument(
            database.Factory,
            "OR-2026-009202",
            new DateTimeOffset(2026, 6, 25, 13, 0, 0, TimeSpan.Zero),
            snapshotHashOverride: null,
            "ORIGINAL",
            "CANCEL");

        InsertZReading(
            database.Factory,
            Guid.Parse("26000000-0000-0000-0000-000000008001"),
            new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 5, 0, TimeSpan.Zero));

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        OfficialOutputSourceResult voided = ReadOutput(database.Services, voidedEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Voided, voided.Lifecycle.State, "Output source must include voided lifecycle state.");
        RequireEqual(1, voided.Lifecycle.CorrectionCount, "Output source must include correction count.");
        RequireEqual("Corrected tender note.", voided.Lifecycle.CorrectionNotes[0], "Output source must expose correction notes.");
        RequireTrue(voided.Lifecycle.IsCoveredByFinalizedZReading, "Output source must include post-Z coverage from lifecycle read.");

        OfficialOutputSourceResult cancelled = ReadOutput(database.Services, cancelledEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Cancelled, cancelled.Lifecycle.State, "Output source must include cancelled lifecycle state.");
        RequireTrue(cancelled.Lifecycle.IsCoveredByFinalizedZReading, "Cancelled output source must include post-Z coverage.");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialOutputSource.LifecycleReads");
    }

    private static OfficialOutputSourceResult ReadOutput(
        SalrynAppServices services,
        Guid journalEntryId)
    {
        Result<OfficialOutputSourceResult> result =
            services.OfficialOutputSourceRead.ReadAsync(new OfficialOutputSourceReadRequest(
                journalEntryId,
                OfficialDocumentNumber: null,
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialOutputSource.Read");

        return result.Value;
    }

    private static Guid SeedDocument(
        SqliteConnectionFactory factory,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        string? snapshotHashOverride,
        params string[] eventSpecs)
    {
        Guid saleId = Guid.NewGuid();
        Guid journalEntryId = Guid.NewGuid();

        InsertSale(factory, saleId, documentNumber, transactionTimestamp);
        InsertJournalEntry(factory, saleId, journalEntryId, documentNumber, transactionTimestamp, snapshotHashOverride);

        for (int index = 0; index < eventSpecs.Length; index++)
        {
            InsertEvent(
                factory,
                journalEntryId,
                eventSpecs[index],
                transactionTimestamp.AddMinutes(index + 1));
        }

        return journalEntryId;
    }

    private static void SeedBaseDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Execute(connection, """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('26000000-0000-0000-0000-000000000901', 'Official Output Cashier Role', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('26000000-0000-0000-0000-000000000001', 'official-output-cashier', 'Official Output Cashier', 'HASH', '26000000-0000-0000-0000-000000000901', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('26000000-0000-0000-0000-000000000902', 'piece', 'pc', 0, 1);
            """);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO products (
                    id,
                    sku,
                    barcode,
                    name,
                    unit_id,
                    cost_price_cents,
                    selling_price_cents,
                    stock_on_hand_x1000,
                    reorder_level_x1000,
                    track_stock,
                    allow_discount,
                    is_active,
                    created_at
                )
                VALUES (
                    $id,
                    'OUTPUT-STOCK',
                    '8660002223334',
                    'Output Stock Sentinel',
                    '26000000-0000-0000-0000-000000000902',
                    100,
                    200,
                    $stock,
                    1000,
                    1,
                    1,
                    1,
                    $now
                );
                """;
            command.Parameters.AddWithValue("$id", ProductId.ToString("D"));
            command.Parameters.AddWithValue("$stock", InitialStockX1000);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        InsertMachineProfile(connection);
        InsertSequence(connection);
    }

    private static void InsertMachineProfile(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_machine_profiles (
                id,
                registration_scope_code,
                registered_taxpayer_name,
                business_name,
                registered_address,
                branch_address,
                taxpayer_identification_number,
                branch_code,
                machine_identification_number,
                machine_serial_number,
                pos_terminal_number,
                accreditation_number,
                permit_to_use_number,
                software_name,
                software_version,
                software_build,
                compliance_mode,
                official_output_enabled,
                is_active,
                created_at
            )
            VALUES (
                $id,
                'LOCAL_SINGLE_STORE',
                'Output Source Taxpayer',
                'Output Source Store',
                'Output Source Registered Address',
                'Output Source Branch Address',
                '000-111-222-333',
                '00001',
                'MIN-OUTPUT',
                'SERIAL-OUTPUT',
                'POS-001',
                'ACC-OUTPUT',
                'PTU-OUTPUT',
                'Salryn',
                'TEST',
                'SMOKE',
                'ACCREDITED',
                1,
                1,
                $now
            );
            """;
        command.Parameters.AddWithValue("$id", MachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_official_document_sequences (
                id,
                bir_machine_profile_id,
                document_kind,
                sequence_scope_code,
                prefix,
                year,
                start_number,
                current_number,
                end_number,
                padding_digits,
                is_active,
                is_exhausted,
                created_at
            )
            VALUES (
                $id,
                $profileId,
                'OFFICIAL_RECEIPT',
                'LOCAL_SINGLE_STORE',
                'OR',
                2026,
                1,
                9201,
                999999,
                6,
                1,
                0,
                $now
            );
            """;
        command.Parameters.AddWithValue("$id", SequenceId.ToString("D"));
        command.Parameters.AddWithValue("$profileId", MachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertSale(
        SqliteConnectionFactory factory,
        Guid saleId,
        string receiptNumber,
        DateTimeOffset saleDate)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sales (
                id,
                receipt_no,
                sale_date,
                cashier_user_id,
                cashier_session_id,
                subtotal_cents,
                discount_cents,
                tax_cents,
                total_cents,
                amount_paid_cents,
                change_cents,
                status,
                receipt_snapshot,
                created_at
            )
            VALUES (
                $id,
                $receiptNo,
                $saleDate,
                $cashierUserId,
                NULL,
                1200,
                0,
                0,
                1200,
                1200,
                0,
                2,
                'legacy sale snapshot sentinel must not become official output source',
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        command.Parameters.AddWithValue("$receiptNo", receiptNumber);
        command.Parameters.AddWithValue("$saleDate", saleDate.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", saleDate.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertJournalEntry(
        SqliteConnectionFactory factory,
        Guid saleId,
        Guid journalEntryId,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        string? snapshotHashOverride)
    {
        ElectronicJournalSnapshotEvidence evidence = CreateSnapshotEvidence(documentNumber, transactionTimestamp);

        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entries (
                id,
                bir_machine_profile_id,
                sale_id,
                official_document_sequence_id,
                official_document_number,
                document_kind,
                transaction_timestamp,
                cashier_user_id,
                terminal_id,
                snapshot_payload_json,
                snapshot_payload_hash,
                snapshot_hash_algorithm,
                created_at
            )
            VALUES (
                $id,
                $machineProfileId,
                $saleId,
                $sequenceId,
                $documentNumber,
                'OFFICIAL_RECEIPT',
                $transactionTimestamp,
                $cashierUserId,
                'POS-OUTPUT',
                $snapshotPayloadJson,
                $snapshotPayloadHash,
                $snapshotHashAlgorithm,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", journalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$machineProfileId", MachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
        command.Parameters.AddWithValue("$sequenceId", SequenceId.ToString("D"));
        command.Parameters.AddWithValue("$documentNumber", documentNumber);
        command.Parameters.AddWithValue("$transactionTimestamp", transactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$snapshotPayloadJson", evidence.PayloadJson);
        command.Parameters.AddWithValue("$snapshotPayloadHash", snapshotHashOverride ?? evidence.PayloadHash);
        command.Parameters.AddWithValue("$snapshotHashAlgorithm", evidence.HashAlgorithm);
        command.Parameters.AddWithValue("$createdAt", transactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static ElectronicJournalSnapshotEvidence CreateSnapshotEvidence(
        string documentNumber,
        DateTimeOffset transactionTimestamp)
    {
        Result<ElectronicJournalSnapshotEvidence> result =
            new OfficialDocumentSnapshotEvidenceBuilder().Build(new OfficialDocumentSnapshotDraft(
                OfficialDocumentSnapshotDraftKind.OfficialReceipt,
                new OfficialDocumentSnapshotDraftHeader(
                    RegisteredTaxpayerName: "Output Source Taxpayer",
                    BusinessName: "Output Source Store",
                    TaxpayerIdentificationNumber: "000-111-222-333",
                    BranchCode: "00001",
                    RegisteredBusinessAddress: "Output Source Registered Address",
                    TransactionDateTime: transactionTimestamp,
                    DocumentNumber: documentNumber,
                    MachineSerialNumber: "SERIAL-OUTPUT",
                    MachineIdentificationNumber: "MIN-OUTPUT",
                    AccreditationNumber: "ACC-OUTPUT",
                    PermitToUseNumber: "PTU-OUTPUT",
                    CashierIdentifier: "Official Output Cashier",
                    TerminalIdentifier: "POS-OUTPUT"),
                new[]
                {
                    new OfficialDocumentSnapshotDraftLine(
                        "Trusted Output Item",
                        "4800000000011",
                        "pc",
                        1000,
                        1200,
                        0,
                        1200)
                },
                new OfficialDocumentSnapshotDraftAmounts(
                    SubtotalCents: 1200,
                    DiscountCents: 0,
                    TaxCents: 0,
                    TotalCents: 1200,
                    AmountPaidCents: 1200,
                    ChangeCents: 0),
                new[]
                {
                    new OfficialDocumentSnapshotDraftPayment(
                        PaymentMethod.Cash,
                        1200,
                        transactionTimestamp)
                },
                new OfficialDocumentSnapshotDraftTaxMode(
                    TaxModeCode: "VAT-DEFERRED",
                    VatBucketEngineDeferred: true,
                    Notes: "Detailed VAT bucket classification is deferred."),
                new[]
                {
                    "Output source smoke reads journal snapshot evidence only."
                }));

        RequireSuccess(result, "Snapshot evidence setup should succeed.");

        return result.Value;
    }

    private static void InsertEvent(
        SqliteConnectionFactory factory,
        Guid journalEntryId,
        string eventSpec,
        DateTimeOffset createdAt)
    {
        string eventType = eventSpec;
        string? reason = null;

        int reasonSeparator = eventSpec.IndexOf(':', StringComparison.Ordinal);

        if (reasonSeparator >= 0)
        {
            eventType = eventSpec[..reasonSeparator];
            reason = eventSpec[(reasonSeparator + 1)..];
        }

        if (reason is null &&
            eventType is "VOID" or "CANCEL" or "CORRECTION")
        {
            reason = "Output source smoke audit reason.";
        }

        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entry_events (
                id,
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                reason,
                created_at,
                metadata_json
            )
            VALUES (
                $id,
                $journalEntryId,
                $eventType,
                $originalMarker,
                0,
                $voidMarker,
                0,
                $cancelMarker,
                $cashierUserId,
                $reason,
                $createdAt,
                $metadataJson
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$originalMarker", eventType == "ORIGINAL" ? 1 : 0);
        command.Parameters.AddWithValue("$voidMarker", eventType == "VOID" ? 1 : 0);
        command.Parameters.AddWithValue("$cancelMarker", eventType == "CANCEL" ? 1 : 0);
        command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$reason", reason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$metadataJson", (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertZReading(
        SqliteConnectionFactory factory,
        Guid zReadingId,
        DateTimeOffset rangeStartedAt,
        DateTimeOffset rangeEndedAt,
        DateTimeOffset finalizedAt)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_z_readings (
                id,
                bir_machine_profile_id,
                registration_scope_code,
                document_kind,
                range_started_at,
                range_ended_at,
                finalized_at,
                entry_count,
                first_official_document_number,
                last_official_document_number,
                gross_sales_cents,
                subtotal_cents,
                discount_total_cents,
                tax_total_cents,
                net_sales_cents,
                total_sales_cents,
                payment_summary_json,
                tax_mode_summary_json,
                evidence_manifest_hash,
                all_snapshot_hashes_match,
                created_by_user_id,
                terminal_id,
                status,
                created_at
            )
            VALUES (
                $id,
                $machineProfileId,
                'LOCAL_SINGLE_STORE',
                'OFFICIAL_RECEIPT',
                $rangeStartedAt,
                $rangeEndedAt,
                $finalizedAt,
                2,
                'OR-2026-009201',
                'OR-2026-009202',
                2400,
                2400,
                0,
                0,
                2400,
                2400,
                '[]',
                '[]',
                'output-source-manifest-hash',
                1,
                $cashierUserId,
                'POS-OUTPUT',
                'FINALIZED',
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", zReadingId.ToString("D"));
        command.Parameters.AddWithValue("$machineProfileId", MachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$rangeStartedAt", rangeStartedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rangeEndedAt", rangeEndedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$finalizedAt", finalizedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", finalizedAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static DatabaseMutationSnapshot CaptureMutationSnapshot(SqliteConnectionFactory factory)
    {
        return new DatabaseMutationSnapshot(
            Sales: CountRows(factory, "sales"),
            SaleItems: CountRows(factory, "sale_items"),
            Payments: CountRows(factory, "payments"),
            StockMovements: CountRows(factory, "stock_movements"),
            JournalEntries: CountRows(factory, "bir_electronic_journal_entries"),
            JournalEvents: CountRows(factory, "bir_electronic_journal_entry_events"),
            ZReadings: CountRows(factory, "bir_z_readings"),
            CurrentSequenceNumber: ReadCurrentSequenceNumber(factory),
            ProductStockX1000: ReadProductStock(factory));
    }

    private static long CountRows(SqliteConnectionFactory factory, string tableName)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long ReadCurrentSequenceNumber(SqliteConnectionFactory factory)
    {
        return ReadLong(
            factory,
            "SELECT current_number FROM bir_official_document_sequences WHERE id = $id;",
            command => command.Parameters.AddWithValue("$id", SequenceId.ToString("D")));
    }

    private static long ReadProductStock(SqliteConnectionFactory factory)
    {
        return ReadLong(
            factory,
            "SELECT stock_on_hand_x1000 FROM products WHERE id = $id;",
            command => command.Parameters.AddWithValue("$id", ProductId.ToString("D")));
    }

    private static long ReadLong(
        SqliteConnectionFactory factory,
        string sql,
        Action<SqliteCommand>? configure)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AssertMutationSnapshot(
        DatabaseMutationSnapshot expected,
        DatabaseMutationSnapshot actual,
        string area)
    {
        RequireEqual(expected, actual, area + " must not mutate persisted state.");
    }

    private static void ExpectOutputFailure(
        Result<OfficialOutputSourceResult> result,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected output source failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected output source failure code.");
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Code} {result.Error}");
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Code} {result.Error}");
        }
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    private sealed record DatabaseMutationSnapshot(
        long Sales,
        long SaleItems,
        long Payments,
        long StockMovements,
        long JournalEntries,
        long JournalEvents,
        long ZReadings,
        long CurrentSequenceNumber,
        long ProductStockX1000);

    private sealed class SmokeDatabase : IDisposable
    {
        private SmokeDatabase(
            string tempRoot,
            SalrynAppServices services)
        {
            TempRoot = tempRoot;
            Services = services;
            Factory = services.ConnectionFactory;
        }

        public string TempRoot { get; }

        public SalrynAppServices Services { get; }

        public SqliteConnectionFactory Factory { get; }

        public static SmokeDatabase Create(string prefix)
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                prefix + "-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(tempRoot);

            SalrynAppServices services = SalrynAppServices.CreateDevelopment(tempRoot);

            Result<Salryn.Infrastructure.Data.Initialization.DatabaseInitializationSummary> initResult =
                services.DatabaseInitialization.InitializeDatabase();

            if (initResult.IsFailure)
            {
                throw new InvalidOperationException("Official output source smoke database initialization failed: " + initResult.Error);
            }

            return new SmokeDatabase(tempRoot, services);
        }

        public void Dispose()
        {
            try
            {
                _ = Services.LocalHostRuntimeLifecycleHolder.TryStop();
            }
            catch (InvalidOperationException)
            {
            }

            DeleteTempRoot();
        }

        private void DeleteTempRoot()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    SqliteConnectionFactory.ClearAllPools();

                    if (Directory.Exists(TempRoot))
                    {
                        Directory.Delete(TempRoot, recursive: true);
                    }

                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}

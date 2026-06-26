using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;

internal static class OfficialDocumentLifecycleSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("25000000-0000-0000-0000-000000000001");
    private static readonly Guid MachineProfileId = Guid.Parse("25000000-0000-0000-0000-000000000002");
    private static readonly Guid SequenceId = Guid.Parse("25000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("25000000-0000-0000-0000-000000000004");

    private const long InitialStockX1000 = 9000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL DOCUMENT LIFECYCLE SMOKE CHECKS =====");

        RunLifecycleStatePath();
        RunInvalidStatePath();
        RunZReadingCoveragePath();
        RunLookupAndMetadataPath();

        Console.WriteLine("Official document lifecycle smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunLifecycleStatePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-lifecycle-state");
        SeedBaseDependencies(database.Factory);

        Guid activeEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000101",
            new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
            "ORIGINAL");

        Guid voidedEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000102",
            new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "VOID");

        Guid cancelledEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000103",
            new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "CANCEL");

        Guid correctedEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000104",
            new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "CORRECTION:First correction note.",
            "CORRECTION:Second correction note.");

        Guid correctedVoidedEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000105",
            new DateTimeOffset(2026, 6, 25, 13, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "CORRECTION:Correct note before void.",
            "VOID");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        OfficialDocumentLifecycleResult active = ReadLifecycle(database.Services, activeEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Active, active.Summary.State, "ORIGINAL-only document must be active.");
        RequireEqual(0, active.Summary.CorrectionCount, "Active document must have zero corrections.");

        OfficialDocumentLifecycleResult voided = ReadLifecycle(database.Services, voidedEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Voided, voided.Summary.State, "VOID document must be voided.");
        RequireTrue(voided.Events.Any(eventRow => eventRow.EventType == "VOID"), "Void event must appear in event trail.");

        OfficialDocumentLifecycleResult cancelled = ReadLifecycle(database.Services, cancelledEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Cancelled, cancelled.Summary.State, "CANCEL document must be cancelled.");
        RequireTrue(cancelled.Events.Any(eventRow => eventRow.EventType == "CANCEL"), "Cancel event must appear in event trail.");

        OfficialDocumentLifecycleResult corrected = ReadLifecycle(database.Services, correctedEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Active, corrected.Summary.State, "Correction-only document must remain active.");
        RequireEqual(2, corrected.Summary.CorrectionCount, "Correction count must include all correction events.");
        RequireEqual("First correction note.", corrected.Corrections[0].Reason, "Correction notes must be deterministic.");
        RequireEqual("Second correction note.", corrected.Corrections[1].Reason, "Correction notes must keep event order.");

        OfficialDocumentLifecycleResult correctedVoided = ReadLifecycle(database.Services, correctedVoidedEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Voided, correctedVoided.Summary.State, "Corrected then voided document must be voided.");
        RequireEqual(1, correctedVoided.Summary.CorrectionCount, "Corrected voided document must preserve correction count.");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialDocumentLifecycle.StateReads");
    }

    private static void RunInvalidStatePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-lifecycle-invalid");
        SeedBaseDependencies(database.Factory);

        Guid missingOriginalEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000201",
            new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero));

        Guid contradictionEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000202",
            new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "VOID",
            "CANCEL");

        Guid duplicateVoidEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000203",
            new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "VOID",
            "VOID");

        Guid duplicateCancelEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000204",
            new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "CANCEL",
            "CANCEL");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        OfficialDocumentLifecycleResult missingOriginal = ReadLifecycle(database.Services, missingOriginalEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Invalid, missingOriginal.Summary.State, "Missing ORIGINAL must be invalid.");
        RequireIssue(missingOriginal, "OfficialDocumentLifecycle.OriginalEventMissing");

        OfficialDocumentLifecycleResult contradiction = ReadLifecycle(database.Services, contradictionEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Invalid, contradiction.Summary.State, "VOID+CANCEL must be invalid.");
        RequireIssue(contradiction, "OfficialDocumentLifecycle.ContradictoryTerminalEvents");

        OfficialDocumentLifecycleResult duplicateVoid = ReadLifecycle(database.Services, duplicateVoidEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Invalid, duplicateVoid.Summary.State, "Duplicate VOID must be invalid.");
        RequireIssue(duplicateVoid, "OfficialDocumentLifecycle.DuplicateVoidEvents");

        OfficialDocumentLifecycleResult duplicateCancel = ReadLifecycle(database.Services, duplicateCancelEntryId);
        RequireEqual(OfficialDocumentLifecycleState.Invalid, duplicateCancel.Summary.State, "Duplicate CANCEL must be invalid.");
        RequireIssue(duplicateCancel, "OfficialDocumentLifecycle.DuplicateCancelEvents");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialDocumentLifecycle.InvalidReads");
    }

    private static void RunZReadingCoveragePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-lifecycle-z-coverage");
        SeedBaseDependencies(database.Factory);

        Guid inRangeEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000301",
            new DateTimeOffset(2026, 6, 25, 14, 0, 0, TimeSpan.Zero),
            "ORIGINAL");

        Guid boundaryEntryId = SeedDocument(
            database.Factory,
            "OR-2026-000302",
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            "ORIGINAL");

        Guid firstZReadingId = Guid.Parse("25000000-0000-0000-0000-000000008001");
        Guid secondZReadingId = Guid.Parse("25000000-0000-0000-0000-000000008002");

        InsertZReading(
            database.Factory,
            firstZReadingId,
            new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 5, 0, TimeSpan.Zero));

        DatabaseMutationSnapshot beforeFirstRead = CaptureMutationSnapshot(database.Factory);

        OfficialDocumentLifecycleResult inRange = ReadLifecycle(database.Services, inRangeEntryId);
        RequireTrue(inRange.Summary.IsCoveredByFinalizedZReading, "In-range document must report finalized Z-reading coverage.");
        RequireEqual(firstZReadingId, inRange.Summary.CoveringZReadingId, "Lifecycle coverage must expose covering Z-reading id.");

        OfficialDocumentLifecycleResult boundaryBeforeSecondZ = ReadLifecycle(database.Services, boundaryEntryId);
        RequireTrue(!boundaryBeforeSecondZ.Summary.IsCoveredByFinalizedZReading, "Boundary document must not be covered by previous half-open range.");

        AssertMutationSnapshot(beforeFirstRead, CaptureMutationSnapshot(database.Factory), "OfficialDocumentLifecycle.ZCoverageFirstRead");

        InsertZReading(
            database.Factory,
            secondZReadingId,
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 27, 0, 5, 0, TimeSpan.Zero));

        DatabaseMutationSnapshot beforeSecondRead = CaptureMutationSnapshot(database.Factory);

        OfficialDocumentLifecycleResult boundaryAfterSecondZ = ReadLifecycle(database.Services, boundaryEntryId);
        RequireTrue(boundaryAfterSecondZ.Summary.IsCoveredByFinalizedZReading, "Boundary document must be covered by next half-open range.");
        RequireEqual(secondZReadingId, boundaryAfterSecondZ.Summary.CoveringZReadingId, "Boundary coverage must use next Z-reading id.");

        AssertMutationSnapshot(beforeSecondRead, CaptureMutationSnapshot(database.Factory), "OfficialDocumentLifecycle.ZCoverageSecondRead");
    }

    private static void RunLookupAndMetadataPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-lifecycle-lookup");
        SeedBaseDependencies(database.Factory);

        Guid entryId = SeedDocument(
            database.Factory,
            "OR-2026-000401",
            new DateTimeOffset(2026, 6, 25, 15, 0, 0, TimeSpan.Zero),
            "ORIGINAL",
            "CORRECTION:Metadata terminal note.");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        Result<OfficialDocumentLifecycleResult> byNumber =
            database.Services.OfficialDocumentLifecycleRead.ReadAsync(new OfficialDocumentLifecycleReadRequest(
                JournalEntryId: null,
                OfficialDocumentNumber: "  OR-2026-000401  ",
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult();

        RequireSuccess(byNumber, "OfficialDocumentLifecycle.LookupByNumber");
        RequireEqual(entryId, byNumber.Value.Summary.JournalEntryId, "Document-number lookup must return matching journal entry.");
        RequireEqual("POS-LIFECYCLE-META", byNumber.Value.Corrections[0].TerminalId, "Lifecycle read must parse terminal id from event metadata.");

        ExpectLifecycleFailure(
            database.Services.OfficialDocumentLifecycleRead.ReadAsync(new OfficialDocumentLifecycleReadRequest(
                JournalEntryId: Guid.Parse("25000000-0000-0000-0000-000000009999"),
                OfficialDocumentNumber: null,
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult(),
            "OfficialDocumentLifecycle.JournalEntryNotFound");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialDocumentLifecycle.LookupAndMetadata");
    }

    private static OfficialDocumentLifecycleResult ReadLifecycle(
        SalrynAppServices services,
        Guid journalEntryId)
    {
        Result<OfficialDocumentLifecycleResult> result =
            services.OfficialDocumentLifecycleRead.ReadAsync(new OfficialDocumentLifecycleReadRequest(
                journalEntryId,
                OfficialDocumentNumber: null,
                DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
                BirMachineProfileId: MachineProfileId,
                RegistrationScopeCode: null)).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialDocumentLifecycle.Read");

        return result.Value;
    }

    private static Guid SeedDocument(
        SqliteConnectionFactory factory,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        params string[] eventSpecs)
    {
        Guid saleId = Guid.NewGuid();
        Guid journalEntryId = Guid.NewGuid();

        InsertSale(factory, saleId, documentNumber, transactionTimestamp);
        InsertJournalEntry(factory, saleId, journalEntryId, documentNumber, transactionTimestamp);

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
            VALUES ('25000000-0000-0000-0000-000000000901', 'Official Lifecycle Cashier', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('25000000-0000-0000-0000-000000000001', 'official-lifecycle-cashier', 'Official Lifecycle Cashier', 'HASH', '25000000-0000-0000-0000-000000000901', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('25000000-0000-0000-0000-000000000902', 'piece', 'pc', 0, 1);
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
                    'LIFECYCLE-STOCK',
                    '8660001112223',
                    'Lifecycle Stock Sentinel',
                    '25000000-0000-0000-0000-000000000902',
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
                taxpayer_identification_number,
                branch_code,
                machine_identification_number,
                machine_serial_number,
                accreditation_number,
                permit_to_use_number,
                software_name,
                software_version,
                compliance_mode,
                official_output_enabled,
                is_active,
                created_at
            )
            VALUES (
                $id,
                'LOCAL_SINGLE_STORE',
                'Lifecycle Taxpayer',
                'Lifecycle Store',
                'Lifecycle Registered Address',
                '000-555-666-777',
                '00001',
                'MIN-LIFECYCLE',
                'SERIAL-LIFECYCLE',
                'ACC-LIFECYCLE',
                'PTU-LIFECYCLE',
                'Salryn',
                'TEST',
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
                401,
                999,
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
                1000,
                0,
                0,
                1000,
                1000,
                0,
                2,
                'Lifecycle smoke receipt_snapshot must not be lifecycle source.',
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
        DateTimeOffset transactionTimestamp)
    {
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
                'POS-LIFECYCLE',
                NULL,
                NULL,
                NULL,
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
        command.Parameters.AddWithValue("$createdAt", transactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
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
            reason = "Lifecycle smoke audit reason.";
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
        command.Parameters.AddWithValue("$metadataJson", eventType == "CORRECTION"
            ? """{"Schema":"SALRYN_OFFICIAL_DOCUMENT_AUDIT_EVENT_METADATA_V1","TerminalId":"POS-LIFECYCLE-META"}"""
            : (object)DBNull.Value);
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
                1,
                'OR-2026-Z-FIRST',
                'OR-2026-Z-LAST',
                1000,
                1000,
                0,
                0,
                1000,
                1000,
                '[]',
                '[]',
                'lifecycle-manifest-hash',
                1,
                $cashierUserId,
                'POS-LIFECYCLE',
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

    private static void RequireIssue(
        OfficialDocumentLifecycleResult result,
        string expectedCode)
    {
        if (!result.Issues.Any(issue => string.Equals(issue.Code, expectedCode, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Expected lifecycle issue {expectedCode}.");
        }
    }

    private static void ExpectLifecycleFailure(
        Result<OfficialDocumentLifecycleResult> result,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected lifecycle failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected lifecycle failure code.");
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
                throw new InvalidOperationException("Official lifecycle smoke database initialization failed: " + initResult.Error);
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

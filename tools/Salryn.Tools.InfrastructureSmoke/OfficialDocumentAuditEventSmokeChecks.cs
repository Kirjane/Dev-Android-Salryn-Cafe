using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;

internal static class OfficialDocumentAuditEventSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("24000000-0000-0000-0000-000000000001");
    private static readonly Guid MachineProfileId = Guid.Parse("24000000-0000-0000-0000-000000000002");
    private static readonly Guid SequenceId = Guid.Parse("24000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("24000000-0000-0000-0000-000000000004");

    private const long InitialStockX1000 = 9000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL DOCUMENT AUDIT EVENT SMOKE CHECKS =====");

        RunVoidPathAndReadingGuard();
        RunCancelPath();
        RunCorrectionPath();
        RunRefundPath();
        RunMissingSourcePath();
        RunPostZReadingBlockedPath();

        Console.WriteLine("Official document audit event smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunVoidPathAndReadingGuard()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-audit-void");
        SeedBaseDependencies(database.Factory);

        Guid journalEntryId = Guid.Parse("24000000-0000-0000-0000-000000000101");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000201"),
            journalEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000301"),
            documentNumber: "OR-2026-000001",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
            totalCents: 1000,
            insertOriginalEvent: true,
            includeSnapshotHash: true);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        Result<OfficialDocumentAuditEventResult> result =
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Void,
                "  Customer requested void before Z-reading.  ")).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialDocumentAuditEvent.Void");
        RequireEqual(OfficialDocumentAuditEventKind.Void, result.Value.EventKind, "Void result must carry event kind.");
        RequireEqual("Customer requested void before Z-reading.", result.Value.Reason, "Void reason must be trimmed.");
        RequireEqual(1L, CountRowsWhere(database.Factory, "bir_electronic_journal_entry_events", "event_type = 'VOID' AND void_marker = 1"), "Void event must be appended.");
        AssertOnlyAuditEventsAdded(before, CaptureMutationSnapshot(database.Factory), addedEvents: 1, "OfficialDocumentAuditEvent.Void");

        DatabaseMutationSnapshot afterVoid = CaptureMutationSnapshot(database.Factory);

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Void,
                "Duplicate void should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.AlreadyVoided");

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Cancel,
                "Cancel after void should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.TerminalEventAlreadyExists");

        AssertMutationSnapshot(afterVoid, CaptureMutationSnapshot(database.Factory), "OfficialDocumentAuditEvent.VoidDuplicates");

        Result<OfficialXReadingResult> xReading =
            database.Services.OfficialXReading.ReadAsync(CreateXReadingRequest()).GetAwaiter().GetResult();

        RequireSuccess(xReading, "OfficialDocumentAuditEvent.Void.XReadingGuard");
        RequireEqual(1000L, xReading.Value.Summary.TotalSalesCents, "Void audit event must not silently subtract X-reading totals in this phase.");

        Result<OfficialZReadingResult> zReading =
            database.Services.OfficialZReading.FinalizeAsync(CreateZReadingRequest()).GetAwaiter().GetResult();

        RequireSuccess(zReading, "OfficialDocumentAuditEvent.Void.ZReadingGuard");
        RequireEqual(1000L, zReading.Value.Summary.TotalSalesCents, "Void audit event must not silently subtract Z-reading totals in this phase.");

        DatabaseMutationSnapshot afterZ = CaptureMutationSnapshot(database.Factory);
        AssertSourceUnchanged(afterVoid, afterZ, "OfficialDocumentAuditEvent.Void.ZReadingGuard");
        RequireEqual(afterVoid.JournalEvents, afterZ.JournalEvents, "Z-reading finalization must not add audit events.");
        RequireEqual(afterVoid.ZReadings + 1, afterZ.ZReadings, "Z-reading guard should add one Z-reading record.");
    }

    private static void RunCancelPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-audit-cancel");
        SeedBaseDependencies(database.Factory);

        Guid journalEntryId = Guid.Parse("24000000-0000-0000-0000-000000000102");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000202"),
            journalEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000302"),
            documentNumber: "OR-2026-000002",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero),
            totalCents: 1200,
            insertOriginalEvent: true,
            includeSnapshotHash: true);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        Result<OfficialDocumentAuditEventResult> result =
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Cancel,
                "Customer cancelled before completion handoff.")).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialDocumentAuditEvent.Cancel");
        RequireEqual(1L, CountRowsWhere(database.Factory, "bir_electronic_journal_entry_events", "event_type = 'CANCEL' AND cancel_marker = 1"), "Cancel event must be appended.");
        AssertOnlyAuditEventsAdded(before, CaptureMutationSnapshot(database.Factory), addedEvents: 1, "OfficialDocumentAuditEvent.Cancel");

        DatabaseMutationSnapshot afterCancel = CaptureMutationSnapshot(database.Factory);

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Cancel,
                "Duplicate cancel should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.AlreadyCancelled");

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Void,
                "Void after cancel should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.TerminalEventAlreadyExists");

        AssertMutationSnapshot(afterCancel, CaptureMutationSnapshot(database.Factory), "OfficialDocumentAuditEvent.CancelDuplicates");
    }

    private static void RunCorrectionPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-audit-correction");
        SeedBaseDependencies(database.Factory);

        Guid journalEntryId = Guid.Parse("24000000-0000-0000-0000-000000000103");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000203"),
            journalEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000303"),
            documentNumber: "OR-2026-000003",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            totalCents: 1500,
            insertOriginalEvent: true,
            includeSnapshotHash: true);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        Result<OfficialDocumentAuditEventResult> first =
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Correction,
                "Correct cashier note before Z-reading.")).GetAwaiter().GetResult();

        RequireSuccess(first, "OfficialDocumentAuditEvent.Correction.First");

        Result<OfficialDocumentAuditEventResult> second =
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Correction,
                "Second append-only correction note.")).GetAwaiter().GetResult();

        RequireSuccess(second, "OfficialDocumentAuditEvent.Correction.Second");
        RequireEqual(2L, CountRowsWhere(database.Factory, "bir_electronic_journal_entry_events", "event_type = 'CORRECTION'"), "Multiple correction events are allowed when each is append-only and reasoned.");
        AssertOnlyAuditEventsAdded(before, CaptureMutationSnapshot(database.Factory), addedEvents: 2, "OfficialDocumentAuditEvent.Correction");
    }

    private static void RunRefundPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-audit-refund");
        SeedBaseDependencies(database.Factory);

        Guid journalEntryId = Guid.Parse("24000000-0000-0000-0000-000000000104");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000204"),
            journalEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000304"),
            documentNumber: "OR-2026-000004",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            totalCents: 1800,
            insertOriginalEvent: true,
            includeSnapshotHash: true);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        Result<OfficialDocumentAuditEventResult> result =
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Refund,
                "Customer refund before Z-reading.")).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialDocumentAuditEvent.Refund");
        RequireEqual(1L, CountRowsWhere(database.Factory, "bir_electronic_journal_entry_events", "event_type = 'REFUND' AND refund_marker = 1"), "Refund event must be appended.");
        AssertOnlyAuditEventsAdded(before, CaptureMutationSnapshot(database.Factory), addedEvents: 1, "OfficialDocumentAuditEvent.Refund");

        Result<OfficialDocumentLifecycleResult> lifecycle =
            database.Services.OfficialDocumentLifecycleRead.ReadAsync(
                new OfficialDocumentLifecycleReadRequest(journalEntryId, null, null, null, null)).GetAwaiter().GetResult();

        RequireSuccess(lifecycle, "OfficialDocumentLifecycle.Refund");
        RequireEqual(OfficialDocumentLifecycleState.Refunded, lifecycle.Value.Summary.State, "Lifecycle must report refunded state.");

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Void,
                "Void after refund should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.TerminalEventAlreadyExists");
    }

    private static void RunMissingSourcePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-audit-missing-source");
        SeedBaseDependencies(database.Factory);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                Guid.Parse("24000000-0000-0000-0000-000000009999"),
                OfficialDocumentAuditEventKind.Void,
                "Missing journal entry should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.JournalEntryNotFound");

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                Guid.Parse("24000000-0000-0000-0000-000000000104"),
                OfficialDocumentAuditEventKind.Void,
                " ")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.ReasonRequired");

        AssertMutationSnapshot(before, CaptureMutationSnapshot(database.Factory), "OfficialDocumentAuditEvent.MissingSource");

        Guid missingOriginalEntryId = Guid.Parse("24000000-0000-0000-0000-000000000104");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000204"),
            missingOriginalEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000304"),
            documentNumber: "OR-2026-000004",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            totalCents: 1600,
            insertOriginalEvent: false,
            includeSnapshotHash: true);

        DatabaseMutationSnapshot afterMissingOriginalSeed = CaptureMutationSnapshot(database.Factory);

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                missingOriginalEntryId,
                OfficialDocumentAuditEventKind.Void,
                "Missing original event should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.OriginalEventMissing");

        AssertMutationSnapshot(afterMissingOriginalSeed, CaptureMutationSnapshot(database.Factory), "OfficialDocumentAuditEvent.MissingOriginal");

        Guid missingHashEntryId = Guid.Parse("24000000-0000-0000-0000-000000000105");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000205"),
            missingHashEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000305"),
            documentNumber: "OR-2026-000005",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 13, 0, 0, TimeSpan.Zero),
            totalCents: 1700,
            insertOriginalEvent: true,
            includeSnapshotHash: false);

        DatabaseMutationSnapshot afterMissingHashSeed = CaptureMutationSnapshot(database.Factory);

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                missingHashEntryId,
                OfficialDocumentAuditEventKind.Void,
                "Missing hash should fail.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.OfficialEvidenceMissing");

        AssertMutationSnapshot(afterMissingHashSeed, CaptureMutationSnapshot(database.Factory), "OfficialDocumentAuditEvent.MissingHash");
    }

    private static void RunPostZReadingBlockedPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-audit-post-z");
        SeedBaseDependencies(database.Factory);

        Guid journalEntryId = Guid.Parse("24000000-0000-0000-0000-000000000106");
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("24000000-0000-0000-0000-000000000206"),
            journalEntryId,
            eventId: Guid.Parse("24000000-0000-0000-0000-000000000306"),
            documentNumber: "OR-2026-000006",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 14, 0, 0, TimeSpan.Zero),
            totalCents: 1800,
            insertOriginalEvent: true,
            includeSnapshotHash: true);

        Result<OfficialZReadingResult> zReading =
            database.Services.OfficialZReading.FinalizeAsync(CreateZReadingRequest()).GetAwaiter().GetResult();

        RequireSuccess(zReading, "OfficialDocumentAuditEvent.PostZ.ZReading");

        DatabaseMutationSnapshot afterZ = CaptureMutationSnapshot(database.Factory);

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Void,
                "Post Z void should be deferred.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.PostZReadingChangeDeferred");

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Cancel,
                "Post Z cancel should be deferred.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.PostZReadingChangeDeferred");

        ExpectAuditFailure(
            database.Services.OfficialDocumentAuditEvents.AppendAsync(CreateAuditRequest(
                journalEntryId,
                OfficialDocumentAuditEventKind.Correction,
                "Post Z correction should be deferred.")).GetAwaiter().GetResult(),
            "OfficialDocumentAuditEvent.PostZReadingChangeDeferred");

        AssertMutationSnapshot(afterZ, CaptureMutationSnapshot(database.Factory), "OfficialDocumentAuditEvent.PostZ");
    }

    private static OfficialDocumentAuditEventRequest CreateAuditRequest(
        Guid journalEntryId,
        OfficialDocumentAuditEventKind kind,
        string reason)
    {
        return new OfficialDocumentAuditEventRequest(
            journalEntryId,
            OfficialDocumentNumber: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            kind,
            reason,
            CashierUserId,
            "POS-AUDIT");
    }

    private static OfficialXReadingRequest CreateXReadingRequest()
    {
        return new OfficialXReadingRequest(
            RangeStartedAt: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            RangeEndedAt: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt);
    }

    private static OfficialZReadingRequest CreateZReadingRequest()
    {
        return new OfficialZReadingRequest(
            RangeStartedAt: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            RangeEndedAt: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            CreatedByUserId: CashierUserId,
            TerminalId: "POS-AUDIT");
    }

    private static void SeedBaseDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Execute(connection, """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('24000000-0000-0000-0000-000000000901', 'Official Audit Cashier', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('24000000-0000-0000-0000-000000000001', 'official-audit-cashier', 'Official Audit Cashier', 'HASH', '24000000-0000-0000-0000-000000000901', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('24000000-0000-0000-0000-000000000902', 'piece', 'pc', 0, 1);
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
                    'AUDIT-STOCK',
                    '8770001112223',
                    'Audit Stock Sentinel',
                    '24000000-0000-0000-0000-000000000902',
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
                'Audit Taxpayer',
                'Audit Store',
                'Audit Registered Address',
                '000-444-555-666',
                '00001',
                'MIN-AUDIT',
                'SERIAL-AUDIT',
                'ACC-AUDIT',
                'PTU-AUDIT',
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
                6,
                99,
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

    private static void InsertOfficialSaleAndEvidence(
        SqliteConnectionFactory factory,
        Guid saleId,
        Guid journalEntryId,
        Guid eventId,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        long totalCents,
        bool insertOriginalEvent,
        bool includeSnapshotHash)
    {
        InsertSale(factory, saleId, documentNumber, transactionTimestamp, totalCents);

        ElectronicJournalSnapshotEvidence evidence = BuildSnapshotEvidence(
            documentNumber,
            transactionTimestamp,
            totalCents);

        InsertJournalEntry(
            factory,
            journalEntryId,
            saleId,
            eventId,
            documentNumber,
            transactionTimestamp,
            evidence,
            insertOriginalEvent,
            includeSnapshotHash);
    }

    private static ElectronicJournalSnapshotEvidence BuildSnapshotEvidence(
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        long totalCents)
    {
        Result<ElectronicJournalSnapshotEvidence> result =
            new OfficialDocumentSnapshotEvidenceBuilder().Build(new OfficialDocumentSnapshotDraft(
                DocumentKind: OfficialDocumentSnapshotDraftKind.OfficialReceipt,
                Header: new OfficialDocumentSnapshotDraftHeader(
                    RegisteredTaxpayerName: "Audit Taxpayer",
                    BusinessName: "Audit Store",
                    TaxpayerIdentificationNumber: "000-444-555-666",
                    BranchCode: "00001",
                    RegisteredBusinessAddress: "Audit Registered Address",
                    TransactionDateTime: transactionTimestamp,
                    DocumentNumber: documentNumber,
                    MachineSerialNumber: "SERIAL-AUDIT",
                    MachineIdentificationNumber: "MIN-AUDIT",
                    AccreditationNumber: "ACC-AUDIT",
                    PermitToUseNumber: "PTU-AUDIT",
                    CashierIdentifier: "official-audit-cashier",
                    TerminalIdentifier: "POS-AUDIT"),
                Lines: new[]
                {
                    new OfficialDocumentSnapshotDraftLine(
                        Description: "Audit evidence item",
                        Barcode: "8770001112223",
                        UnitName: "pc",
                        QuantityX1000: 1000,
                        UnitPriceCents: totalCents,
                        DiscountCents: 0,
                        LineTotalCents: totalCents)
                },
                Amounts: new OfficialDocumentSnapshotDraftAmounts(
                    SubtotalCents: totalCents,
                    DiscountCents: 0,
                    TaxCents: 0,
                    TotalCents: totalCents,
                    AmountPaidCents: totalCents,
                    ChangeCents: 0),
                Payments: new[]
                {
                    new OfficialDocumentSnapshotDraftPayment(
                        PaymentMethod.Cash,
                        totalCents,
                        transactionTimestamp.AddMinutes(1))
                },
                TaxMode: new OfficialDocumentSnapshotDraftTaxMode(
                    TaxModeCode: "VAT_DEFERRED",
                    VatBucketEngineDeferred: true,
                    Notes: "Detailed VAT bucket classification deferred."),
                DeferredComplianceNotes: new[]
                {
                    "No VAT bucket engine."
                }));

        RequireSuccess(result, "OfficialDocumentAuditEvent.SnapshotEvidence");

        return result.Value;
    }

    private static void InsertSale(
        SqliteConnectionFactory factory,
        Guid saleId,
        string receiptNumber,
        DateTimeOffset saleDate,
        long totalCents)
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
                $total,
                0,
                0,
                $total,
                $total,
                0,
                2,
                'Audit event smoke receipt_snapshot must not be source evidence.',
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        command.Parameters.AddWithValue("$receiptNo", receiptNumber);
        command.Parameters.AddWithValue("$saleDate", saleDate.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$total", totalCents);
        command.Parameters.AddWithValue("$createdAt", saleDate.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertJournalEntry(
        SqliteConnectionFactory factory,
        Guid journalEntryId,
        Guid saleId,
        Guid eventId,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        ElectronicJournalSnapshotEvidence evidence,
        bool insertOriginalEvent,
        bool includeSnapshotHash)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
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
                    'POS-AUDIT',
                    $payload,
                    $payloadHash,
                    $hashAlgorithm,
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
            command.Parameters.AddWithValue("$payload", evidence.PayloadJson);
            command.Parameters.AddWithValue("$payloadHash", includeSnapshotHash ? evidence.PayloadHash : (object)DBNull.Value);
            command.Parameters.AddWithValue("$hashAlgorithm", includeSnapshotHash ? evidence.HashAlgorithm : (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", transactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        if (insertOriginalEvent)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
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
                    created_at
                )
                VALUES (
                    $id,
                    $journalEntryId,
                    'ORIGINAL',
                    1,
                    0,
                    0,
                    0,
                    0,
                    $cashierUserId,
                    $createdAt
                );
                """;
            command.Parameters.AddWithValue("$id", eventId.ToString("D"));
            command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D"));
            command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
            command.Parameters.AddWithValue("$createdAt", transactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
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
            OriginalEvents: CountRowsWhere(factory, "bir_electronic_journal_entry_events", "event_type = 'ORIGINAL' AND original_marker = 1"),
            JournalEvents: CountRows(factory, "bir_electronic_journal_entry_events"),
            ZReadings: CountRows(factory, "bir_z_readings"),
            CurrentSequenceNumber: ReadCurrentSequenceNumber(factory),
            ProductStockX1000: ReadProductStock(factory));
    }

    private static void AssertOnlyAuditEventsAdded(
        DatabaseMutationSnapshot before,
        DatabaseMutationSnapshot after,
        long addedEvents,
        string area)
    {
        AssertSourceUnchanged(before, after, area);
        RequireEqual(before.OriginalEvents, after.OriginalEvents, area + " must not add another ORIGINAL event.");
        RequireEqual(before.JournalEvents + addedEvents, after.JournalEvents, area + " must append only the requested audit event rows.");
        RequireEqual(before.ZReadings, after.ZReadings, area + " must not create Z-reading records.");
    }

    private static void AssertSourceUnchanged(
        DatabaseMutationSnapshot before,
        DatabaseMutationSnapshot after,
        string area)
    {
        RequireEqual(before.Sales, after.Sales, area + " must not mutate sales.");
        RequireEqual(before.SaleItems, after.SaleItems, area + " must not mutate sale items.");
        RequireEqual(before.Payments, after.Payments, area + " must not mutate payments.");
        RequireEqual(before.StockMovements, after.StockMovements, area + " must not mutate stock movements.");
        RequireEqual(before.JournalEntries, after.JournalEntries, area + " must not mutate journal entries.");
        RequireEqual(before.CurrentSequenceNumber, after.CurrentSequenceNumber, area + " must not advance official sequences.");
        RequireEqual(before.ProductStockX1000, after.ProductStockX1000, area + " must not mutate product stock.");
    }

    private static void AssertMutationSnapshot(
        DatabaseMutationSnapshot expected,
        DatabaseMutationSnapshot actual,
        string area)
    {
        RequireEqual(expected, actual, area + " must not mutate persisted state.");
    }

    private static long CountRows(SqliteConnectionFactory factory, string tableName)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long CountRowsWhere(
        SqliteConnectionFactory factory,
        string tableName,
        string whereClause)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {whereClause};";

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

    private static void ExpectAuditFailure(
        Result<OfficialDocumentAuditEventResult> result,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected audit event failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected audit event failure code.");
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
        long OriginalEvents,
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
                throw new InvalidOperationException("Official audit event smoke database initialization failed: " + initResult.Error);
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

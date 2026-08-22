using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;

internal static class OfficialDocumentReprintSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("27000000-0000-0000-0000-000000000001");
    private static readonly Guid MachineProfileId = Guid.Parse("27000000-0000-0000-0000-000000000002");
    private static readonly Guid SequenceId = Guid.Parse("27000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("27000000-0000-0000-0000-000000000004");

    private const long InitialStockX1000 = 15000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL DOCUMENT REPRINT SMOKE CHECKS =====");

        RunAllowedLifecyclePath();
        RunFailurePath();
        RunPostZAllowedPath();

        Console.WriteLine("Official document reprint smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunAllowedLifecyclePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-reprint-allowed");
        SeedBaseDependencies(database.Factory);

        Guid activeEntryId = SeedDocument(
            database.Factory,
            "OR-2026-010001",
            new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
            snapshotHash: "hash-active",
            "ORIGINAL");

        Guid voidedEntryId = SeedDocument(
            database.Factory,
            "OR-2026-010002",
            new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero),
            snapshotHash: "hash-voided",
            "ORIGINAL",
            "VOID");

        Guid cancelledEntryId = SeedDocument(
            database.Factory,
            "OR-2026-010003",
            new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            snapshotHash: "hash-cancelled",
            "ORIGINAL",
            "CANCEL");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        AppendReprint(database.Services, activeEntryId, "First active copy.");
        AppendReprint(database.Services, activeEntryId, "Second active copy.");
        AppendReprint(database.Services, voidedEntryId, "Voided document copy.");
        AppendReprint(database.Services, cancelledEntryId, "Cancelled document copy.");

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            expectedJournalEventIncrease: 4,
            "OfficialDocumentReprint.AllowedLifecycle");

        RequireEqual(2L, CountReprintEvents(database.Factory, activeEntryId), "Multiple active document reprints must be allowed.");
        RequireEqual("First active copy.|Second active copy.", ReadReprintReasons(database.Factory, activeEntryId), "Multiple reprints must remain ordered and reasoned.");
        RequireEqual(1L, CountReprintEvents(database.Factory, voidedEntryId), "Voided document reprint must be allowed.");
        RequireEqual(1L, CountReprintEvents(database.Factory, cancelledEntryId), "Cancelled document reprint must be allowed.");
    }

    private static void RunFailurePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-reprint-failures");
        SeedBaseDependencies(database.Factory);

        Guid missingOriginalEntryId = SeedDocument(
            database.Factory,
            "OR-2026-010101",
            new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            snapshotHash: "hash-missing-original");

        Guid invalidLifecycleEntryId = SeedDocument(
            database.Factory,
            "OR-2026-010102",
            new DateTimeOffset(2026, 6, 25, 13, 0, 0, TimeSpan.Zero),
            snapshotHash: "hash-invalid",
            "ORIGINAL",
            "VOID",
            "CANCEL");

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        ExpectReprintFailure(
            database.Services.OfficialDocumentReprints.AppendAsync(CreateRequest(missingOriginalEntryId, "Missing original copy.")).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.OriginalEventMissing");

        ExpectReprintFailure(
            database.Services.OfficialDocumentReprints.AppendAsync(CreateRequest(invalidLifecycleEntryId, "Invalid lifecycle copy.")).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.InvalidLifecycleState");

        ExpectReprintFailure(
            database.Services.OfficialDocumentReprints.AppendAsync(CreateRequest(invalidLifecycleEntryId, " ")).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.ReasonRequired");

        ExpectReprintFailure(
            database.Services.OfficialDocumentReprints.AppendAsync(CreateRequest(invalidLifecycleEntryId, "Missing actor copy.") with
            {
                ActorUserId = Guid.Empty
            }).GetAwaiter().GetResult(),
            "OfficialDocumentReprint.ActorUserRequired");

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            expectedJournalEventIncrease: 0,
            "OfficialDocumentReprint.Failures");
    }

    private static void RunPostZAllowedPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-reprint-post-z");
        SeedBaseDependencies(database.Factory);

        Guid postZEntryId = SeedDocument(
            database.Factory,
            "OR-2026-010201",
            new DateTimeOffset(2026, 6, 25, 14, 0, 0, TimeSpan.Zero),
            snapshotHash: "hash-post-z",
            "ORIGINAL");

        InsertZReading(
            database.Factory,
            Guid.Parse("27000000-0000-0000-0000-000000008001"),
            new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 5, 0, TimeSpan.Zero));

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        Result<OfficialDocumentReprintResult> result =
            database.Services.OfficialDocumentReprints.AppendAsync(CreateRequest(postZEntryId, "Post-Z reprint copy.")).GetAwaiter().GetResult();

        RequireSuccess(result, "Post-Z reprint should be allowed because it appends copy audit only.");
        RequireEqual("OR-2026-010201", result.Value.OfficialDocumentNumber, "Post-Z reprint must preserve original document number.");

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            expectedJournalEventIncrease: 1,
            "OfficialDocumentReprint.PostZAllowed");
    }

    private static Result<OfficialDocumentReprintResult> AppendReprint(
        SalrynAppServices services,
        Guid journalEntryId,
        string reason)
    {
        Result<OfficialDocumentReprintResult> result =
            services.OfficialDocumentReprints.AppendAsync(CreateRequest(journalEntryId, reason)).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialDocumentReprint.Append");

        return result;
    }

    private static OfficialDocumentReprintRequest CreateRequest(
        Guid journalEntryId,
        string reason)
    {
        return new OfficialDocumentReprintRequest(
            journalEntryId,
            OfficialDocumentNumber: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            reason,
            CashierUserId,
            "POS-REPRINT");
    }

    private static Guid SeedDocument(
        SqliteConnectionFactory factory,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        string snapshotHash,
        params string[] eventSpecs)
    {
        Guid saleId = Guid.NewGuid();
        Guid journalEntryId = Guid.NewGuid();

        InsertSale(factory, saleId, documentNumber, transactionTimestamp);
        InsertJournalEntry(factory, saleId, journalEntryId, documentNumber, transactionTimestamp, snapshotHash);

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
            VALUES ('27000000-0000-0000-0000-000000000901', 'Official Reprint Cashier Role', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('27000000-0000-0000-0000-000000000001', 'official-reprint-cashier', 'Official Reprint Cashier', 'HASH', '27000000-0000-0000-0000-000000000901', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('27000000-0000-0000-0000-000000000902', 'piece', 'pc', 0, 1);
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
                    'REPRINT-STOCK',
                    '8660003334445',
                    'Reprint Stock Sentinel',
                    '27000000-0000-0000-0000-000000000902',
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
                'Reprint Taxpayer',
                'Reprint Store',
                'Reprint Registered Address',
                '000-222-333-444',
                '00001',
                'MIN-REPRINT',
                'SERIAL-REPRINT',
                'ACC-REPRINT',
                'PTU-REPRINT',
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
                10201,
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
                1000,
                0,
                0,
                1000,
                1000,
                0,
                2,
                'Reprint smoke legacy sale snapshot must not be source.',
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
        string snapshotHash)
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
                'POS-REPRINT',
                '{}',
                $snapshotPayloadHash,
                'SHA-256',
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
        command.Parameters.AddWithValue("$snapshotPayloadHash", snapshotHash);
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
            reason = "Reprint smoke audit reason.";
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
                NULL
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
                'OR-2026-010201',
                'OR-2026-010201',
                1000,
                1000,
                0,
                0,
                1000,
                1000,
                '[]',
                '[]',
                'reprint-manifest-hash',
                1,
                $cashierUserId,
                'POS-REPRINT',
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

    private static long CountReprintEvents(
        SqliteConnectionFactory factory,
        Guid journalEntryId)
    {
        return ReadLong(
            factory,
            """
            SELECT COUNT(*)
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId
              AND event_type = 'REPRINT'
              AND reprint_marker = 1;
            """,
            command => command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D")));
    }

    private static string ReadReprintReasons(
        SqliteConnectionFactory factory,
        Guid journalEntryId)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reason
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId
              AND event_type = 'REPRINT'
            ORDER BY created_at ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> reasons = new();

        while (reader.Read())
        {
            reasons.Add(reader.GetString(0));
        }

        return string.Join("|", reasons);
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
        DatabaseMutationSnapshot before,
        DatabaseMutationSnapshot after,
        long expectedJournalEventIncrease,
        string area)
    {
        RequireEqual(before.Sales, after.Sales, area + " must not mutate sales.");
        RequireEqual(before.SaleItems, after.SaleItems, area + " must not mutate sale items.");
        RequireEqual(before.Payments, after.Payments, area + " must not mutate payments.");
        RequireEqual(before.StockMovements, after.StockMovements, area + " must not mutate stock movements.");
        RequireEqual(before.JournalEntries, after.JournalEntries, area + " must not mutate journal entries.");
        RequireEqual(before.ZReadings, after.ZReadings, area + " must not mutate Z-readings.");
        RequireEqual(before.CurrentSequenceNumber, after.CurrentSequenceNumber, area + " must not advance official sequence.");
        RequireEqual(before.ProductStockX1000, after.ProductStockX1000, area + " must not mutate product stock.");
        RequireEqual(before.JournalEvents + expectedJournalEventIncrease, after.JournalEvents, area + " must only append expected reprint events.");
    }

    private static void ExpectReprintFailure(
        Result<OfficialDocumentReprintResult> result,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected reprint failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected reprint failure code.");
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
                throw new InvalidOperationException("Official reprint smoke database initialization failed: " + initResult.Error);
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

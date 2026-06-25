using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;

internal static class OfficialXReadingSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("22000000-0000-0000-0000-000000000001");
    private static readonly Guid MachineProfileId = Guid.Parse("22000000-0000-0000-0000-000000000002");
    private static readonly Guid SequenceId = Guid.Parse("22000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("22000000-0000-0000-0000-000000000004");

    private const long InitialStockX1000 = 9000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL X-READING SMOKE CHECKS =====");

        RunReadOnlyAggregationPath();
        RunHashMismatchBlockedPath();

        Console.WriteLine("Official X-reading smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunReadOnlyAggregationPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-x-reading");
        SeedBaseDependencies(database.Factory);

        InsertStandardSale(database.Factory);
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("22000000-0000-0000-0000-000000000101"),
            journalEntryId: Guid.Parse("22000000-0000-0000-0000-000000000201"),
            eventId: Guid.Parse("22000000-0000-0000-0000-000000000301"),
            documentNumber: "OR-2026-000001",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
            subtotalCents: 1000,
            lineDiscountCents: 100,
            saleDiscountCents: 50,
            taxCents: 120,
            totalCents: 1070,
            corruptHash: false);
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("22000000-0000-0000-0000-000000000102"),
            journalEntryId: Guid.Parse("22000000-0000-0000-0000-000000000202"),
            eventId: Guid.Parse("22000000-0000-0000-0000-000000000302"),
            documentNumber: "OR-2026-000002",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero),
            subtotalCents: 2000,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            totalCents: 2000,
            corruptHash: false);
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("22000000-0000-0000-0000-000000000103"),
            journalEntryId: Guid.Parse("22000000-0000-0000-0000-000000000203"),
            eventId: Guid.Parse("22000000-0000-0000-0000-000000000303"),
            documentNumber: "OR-2026-000003",
            transactionTimestamp: new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero),
            subtotalCents: 999999,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            totalCents: 999999,
            corruptHash: false);
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("22000000-0000-0000-0000-000000000104"),
            journalEntryId: Guid.Parse("22000000-0000-0000-0000-000000000204"),
            eventId: Guid.Parse("22000000-0000-0000-0000-000000000304"),
            documentNumber: "OR-2026-000004",
            transactionTimestamp: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            subtotalCents: 500,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            totalCents: 500,
            corruptHash: false);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);
        OfficialXReadingRequest request = CreateRequest(
            new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero));

        Result<OfficialXReadingResult> first =
            database.Services.OfficialXReading.ReadAsync(request).GetAwaiter().GetResult();

        RequireSuccess(first, "OfficialXReading.Aggregation.First");
        VerifyAggregateXReading(first.Value);

        Result<OfficialXReadingResult> second =
            database.Services.OfficialXReading.ReadAsync(request).GetAwaiter().GetResult();

        RequireSuccess(second, "OfficialXReading.Aggregation.Second");
        VerifyDeterministicXReading(first.Value, second.Value);

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            "OfficialXReading.Aggregation");

        Result<OfficialXReadingResult> firstSaleOnly =
            database.Services.OfficialXReading.ReadAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 25, 8, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 25, 9, 30, 0, TimeSpan.Zero))).GetAwaiter().GetResult();

        RequireSuccess(firstSaleOnly, "OfficialXReading.Range.FirstSaleOnly");
        RequireEqual(1, firstSaleOnly.Value.Summary.EntryCount, "Date range must include only matching official journal evidence.");
        RequireEqual(1070L, firstSaleOnly.Value.Summary.TotalSalesCents, "Date range must exclude out-of-window official sales.");
        RequireEqual("OR-2026-000001", firstSaleOnly.Value.DocumentRange?.FirstOfficialDocumentNumber, "Single-entry range must keep the correct document.");

        Result<OfficialXReadingResult> empty =
            database.Services.OfficialXReading.ReadAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 28, 0, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult();

        RequireSuccess(empty, "OfficialXReading.Range.Empty");
        RequireEqual(0, empty.Value.Summary.EntryCount, "Empty date range must return a successful empty X-reading.");
        RequireEqual(null, empty.Value.DocumentRange, "Empty X-reading must not invent document range.");
        RequireEqual(0L, empty.Value.Summary.TotalSalesCents, "Empty X-reading must not invent totals.");

        Result<OfficialXReadingResult> boundarySecondDay =
            database.Services.OfficialXReading.ReadAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult();

        RequireSuccess(boundarySecondDay, "OfficialXReading.Range.BoundarySecondDay");
        RequireEqual(1, boundarySecondDay.Value.Summary.EntryCount, "Boundary timestamp evidence must belong to the second half-open range.");
        RequireEqual(500L, boundarySecondDay.Value.Summary.TotalSalesCents, "Second-day boundary range must include the midnight evidence.");
        RequireEqual("OR-2026-000004", boundarySecondDay.Value.DocumentRange?.FirstOfficialDocumentNumber, "Boundary evidence document must be the second-day first document.");
        RequireEqual("OR-2026-000004", boundarySecondDay.Value.DocumentRange?.LastOfficialDocumentNumber, "Boundary evidence document must be the second-day last document.");

        ExpectXReadingFailure(
            database.Services.OfficialXReading.ReadAsync(request with
            {
                RangeStartedAt = request.RangeEndedAt,
                RangeEndedAt = request.RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialXReading.InvalidDateRange");

        ExpectXReadingFailure(
            database.Services.OfficialXReading.ReadAsync(request with
            {
                RangeEndedAt = request.RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialXReading.InvalidDateRange");

        ExpectXReadingFailure(
            database.Services.OfficialXReading.ReadAsync(request with
            {
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialXReading.MachineProfileOrScopeRequired");

        ExpectXReadingFailure(
            database.Services.OfficialXReading.ReadAsync(request with
            {
                DocumentKind = (ElectronicJournalDocumentKind)999
            }).GetAwaiter().GetResult(),
            "OfficialXReading.UnsupportedDocumentKind");

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            "OfficialXReading.ValidationAndRangeReads");
    }

    private static void RunHashMismatchBlockedPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-x-reading-hash-block");
        SeedBaseDependencies(database.Factory);

        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("22000000-0000-0000-0000-000000000401"),
            journalEntryId: Guid.Parse("22000000-0000-0000-0000-000000000402"),
            eventId: Guid.Parse("22000000-0000-0000-0000-000000000403"),
            documentNumber: "OR-2026-000004",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            subtotalCents: 1000,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            totalCents: 1000,
            corruptHash: true);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        ExpectXReadingFailure(
            database.Services.OfficialXReading.ReadAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult(),
            "OfficialXReading.EvidenceHashMismatch");

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            "OfficialXReading.HashMismatch");
    }

    private static OfficialXReadingRequest CreateRequest(
        DateTimeOffset rangeStartedAt,
        DateTimeOffset rangeEndedAt)
    {
        return new OfficialXReadingRequest(
            RangeStartedAt: rangeStartedAt,
            RangeEndedAt: rangeEndedAt,
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt);
    }

    private static void VerifyAggregateXReading(OfficialXReadingResult reading)
    {
        RequireEqual(2, reading.Summary.EntryCount, "X-reading must include only in-range official journal evidence.");
        RequireEqual(MachineProfileId, reading.Summary.BirMachineProfileId, "X-reading summary must carry machine profile id.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, reading.Summary.DocumentKind, "X-reading summary must carry document kind.");
        RequireEqual(3150L, reading.Summary.GrossSalesCents, "X-reading gross must be computed from official snapshot evidence.");
        RequireEqual(3000L, reading.Summary.SubtotalCents, "X-reading subtotal must be computed from official snapshot evidence.");
        RequireEqual(150L, reading.Summary.DiscountTotalCents, "X-reading discount must include line and sale discounts.");
        RequireEqual(120L, reading.Summary.TaxTotalCents, "X-reading tax total must be computed from official snapshot evidence.");
        RequireEqual(2950L, reading.Summary.NetSalesCents, "X-reading net sales must be computed from official snapshot evidence.");
        RequireEqual(3070L, reading.Summary.TotalSalesCents, "X-reading total must exclude standard and out-of-range sales.");
        RequireTrue(!string.IsNullOrWhiteSpace(reading.Summary.EvidenceManifestHash), "X-reading must expose evidence manifest hash.");
        RequireTrue(reading.Summary.AllSnapshotHashesMatch, "X-reading must report verified evidence hashes.");
        RequireEqual("OR-2026-000001", reading.DocumentRange?.FirstOfficialDocumentNumber, "X-reading first document number must be deterministic.");
        RequireEqual("OR-2026-000002", reading.DocumentRange?.LastOfficialDocumentNumber, "X-reading last document number must be deterministic.");
        RequireEqual(1, reading.Payments.Count, "Current payment enum should aggregate all cash evidence rows.");
        RequireEqual("Cash", reading.Payments[0].Method, "X-reading payment summary must use snapshot payment method.");
        RequireEqual(3070L, reading.Payments[0].AmountCents, "X-reading payment total must be computed from snapshot payments.");
        RequireEqual(2, reading.Payments[0].PaymentCount, "X-reading payment count must be computed from snapshot payments.");
        RequireEqual(1, reading.TaxModes.Count, "X-reading must summarize tax mode evidence.");
        RequireEqual("VAT_DEFERRED", reading.TaxModes[0].TaxModeCode, "X-reading must surface snapshot tax mode code.");
        RequireTrue(reading.TaxModes[0].VatBucketEngineDeferred, "X-reading must preserve deferred VAT-bucket state.");
    }

    private static void VerifyDeterministicXReading(
        OfficialXReadingResult first,
        OfficialXReadingResult second)
    {
        RequireEqual(first.Summary, second.Summary, "X-reading summary must be deterministic.");
        RequireEqual(first.DocumentRange, second.DocumentRange, "X-reading document range must be deterministic.");
        RequireEqual(first.Payments.Count, second.Payments.Count, "X-reading payments must be deterministic.");
        RequireEqual(first.TaxModes.Count, second.TaxModes.Count, "X-reading tax modes must be deterministic.");

        for (int index = 0; index < first.Payments.Count; index++)
        {
            RequireEqual(first.Payments[index], second.Payments[index], "X-reading payment rows must be deterministic.");
        }

        for (int index = 0; index < first.TaxModes.Count; index++)
        {
            RequireEqual(first.TaxModes[index], second.TaxModes[index], "X-reading tax mode rows must be deterministic.");
        }
    }

    private static void SeedBaseDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Execute(connection, """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('22000000-0000-0000-0000-000000000901', 'Official X Reading Cashier', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('22000000-0000-0000-0000-000000000001', 'official-x-reading-cashier', 'Official X Reading Cashier', 'HASH', '22000000-0000-0000-0000-000000000901', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('22000000-0000-0000-0000-000000000902', 'piece', 'pc', 0, 1);
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
                    'X-READING-STOCK',
                    '8880001112223',
                    'X Reading Stock Sentinel',
                    '22000000-0000-0000-0000-000000000902',
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
                'X Reading Taxpayer',
                'X Reading Store',
                'X Reading Registered Address',
                '000-222-333-444',
                '00001',
                'MIN-X-READING',
                'SERIAL-X-READING',
                'ACC-X-READING',
                'PTU-X-READING',
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
                3,
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

    private static void InsertStandardSale(SqliteConnectionFactory factory)
    {
        InsertSale(
            factory,
            saleId: Guid.Parse("22000000-0000-0000-0000-000000000099"),
            receiptNumber: "STD-2026-999999",
            saleDate: new DateTimeOffset(2026, 6, 25, 9, 15, 0, TimeSpan.Zero),
            subtotalCents: 999999,
            discountCents: 0,
            taxCents: 0,
            totalCents: 999999,
            receiptSnapshot: "Standard sale receipt snapshot must not be X-reading evidence.");
    }

    private static void InsertOfficialSaleAndEvidence(
        SqliteConnectionFactory factory,
        Guid saleId,
        Guid journalEntryId,
        Guid eventId,
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        long subtotalCents,
        long lineDiscountCents,
        long saleDiscountCents,
        long taxCents,
        long totalCents,
        bool corruptHash)
    {
        InsertSale(
            factory,
            saleId,
            receiptNumber: documentNumber,
            saleDate: transactionTimestamp,
            subtotalCents,
            discountCents: saleDiscountCents,
            taxCents,
            totalCents,
            receiptSnapshot: "Sales receipt_snapshot is not official X-reading evidence.");

        ElectronicJournalSnapshotEvidence evidence = BuildSnapshotEvidence(
            documentNumber,
            transactionTimestamp,
            subtotalCents,
            lineDiscountCents,
            saleDiscountCents,
            taxCents,
            totalCents);

        InsertJournalEntry(
            factory,
            journalEntryId,
            saleId,
            eventId,
            documentNumber,
            transactionTimestamp,
            evidence,
            corruptHash);
    }

    private static ElectronicJournalSnapshotEvidence BuildSnapshotEvidence(
        string documentNumber,
        DateTimeOffset transactionTimestamp,
        long subtotalCents,
        long lineDiscountCents,
        long saleDiscountCents,
        long taxCents,
        long totalCents)
    {
        Result<ElectronicJournalSnapshotEvidence> result =
            new OfficialDocumentSnapshotEvidenceBuilder().Build(new OfficialDocumentSnapshotDraft(
                DocumentKind: OfficialDocumentSnapshotDraftKind.OfficialReceipt,
                Header: new OfficialDocumentSnapshotDraftHeader(
                    RegisteredTaxpayerName: "X Reading Taxpayer",
                    BusinessName: "X Reading Store",
                    TaxpayerIdentificationNumber: "000-222-333-444",
                    BranchCode: "00001",
                    RegisteredBusinessAddress: "X Reading Registered Address",
                    TransactionDateTime: transactionTimestamp,
                    DocumentNumber: documentNumber,
                    MachineSerialNumber: "SERIAL-X-READING",
                    MachineIdentificationNumber: "MIN-X-READING",
                    AccreditationNumber: "ACC-X-READING",
                    PermitToUseNumber: "PTU-X-READING",
                    CashierIdentifier: "official-x-reading-cashier",
                    TerminalIdentifier: "POS-X-READING"),
                Lines: new[]
                {
                    new OfficialDocumentSnapshotDraftLine(
                        Description: "X-reading evidence item",
                        Barcode: "8880001112223",
                        UnitName: "pc",
                        QuantityX1000: 1000,
                        UnitPriceCents: checked(subtotalCents + lineDiscountCents),
                        DiscountCents: lineDiscountCents,
                        LineTotalCents: subtotalCents)
                },
                Amounts: new OfficialDocumentSnapshotDraftAmounts(
                    SubtotalCents: subtotalCents,
                    DiscountCents: saleDiscountCents,
                    TaxCents: taxCents,
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

        RequireSuccess(result, "OfficialXReading.SnapshotEvidence");

        return result.Value;
    }

    private static void InsertSale(
        SqliteConnectionFactory factory,
        Guid saleId,
        string receiptNumber,
        DateTimeOffset saleDate,
        long subtotalCents,
        long discountCents,
        long taxCents,
        long totalCents,
        string receiptSnapshot)
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
                $subtotal,
                $discount,
                $tax,
                $total,
                $total,
                0,
                2,
                $receiptSnapshot,
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        command.Parameters.AddWithValue("$receiptNo", receiptNumber);
        command.Parameters.AddWithValue("$saleDate", saleDate.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cashierUserId", CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$subtotal", subtotalCents);
        command.Parameters.AddWithValue("$discount", discountCents);
        command.Parameters.AddWithValue("$tax", taxCents);
        command.Parameters.AddWithValue("$total", totalCents);
        command.Parameters.AddWithValue("$receiptSnapshot", receiptSnapshot);
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
        bool corruptHash)
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
                    'POS-X-READING',
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
            command.Parameters.AddWithValue("$payloadHash", corruptHash ? "corrupted-hash" : evidence.PayloadHash);
            command.Parameters.AddWithValue("$hashAlgorithm", evidence.HashAlgorithm);
            command.Parameters.AddWithValue("$createdAt", transactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
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
            JournalEvents: CountRows(factory, "bir_electronic_journal_entry_events"),
            PreservationBatches: CountRows(factory, "bir_electronic_journal_preservation_batches"),
            CurrentSequenceNumber: ReadCurrentSequenceNumber(factory),
            ProductStockX1000: ReadProductStock(factory));
    }

    private static void AssertMutationSnapshot(
        DatabaseMutationSnapshot expected,
        DatabaseMutationSnapshot actual,
        string area)
    {
        RequireEqual(expected, actual, area + " must not mutate persisted business or journal state.");
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

    private static void ExpectXReadingFailure(
        Result<OfficialXReadingResult> result,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected X-reading failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected X-reading failure code.");
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
        long PreservationBatches,
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
                throw new InvalidOperationException("Official X-reading smoke database initialization failed: " + initResult.Error);
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

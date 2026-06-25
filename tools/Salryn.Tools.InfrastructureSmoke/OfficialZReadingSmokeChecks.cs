using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;

internal static class OfficialZReadingSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("23000000-0000-0000-0000-000000000001");
    private static readonly Guid MachineProfileId = Guid.Parse("23000000-0000-0000-0000-000000000002");
    private static readonly Guid SequenceId = Guid.Parse("23000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("23000000-0000-0000-0000-000000000004");

    private const long InitialStockX1000 = 9000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL Z-READING SMOKE CHECKS =====");

        RunFinalizeAndGuardPath();
        RunEmptyRangePath();
        RunHashMismatchBlockedPath();

        Console.WriteLine("Official Z-reading smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunFinalizeAndGuardPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-z-reading");
        SeedBaseDependencies(database.Factory);

        InsertStandardSale(database.Factory);
        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("23000000-0000-0000-0000-000000000101"),
            journalEntryId: Guid.Parse("23000000-0000-0000-0000-000000000201"),
            eventId: Guid.Parse("23000000-0000-0000-0000-000000000301"),
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
            saleId: Guid.Parse("23000000-0000-0000-0000-000000000102"),
            journalEntryId: Guid.Parse("23000000-0000-0000-0000-000000000202"),
            eventId: Guid.Parse("23000000-0000-0000-0000-000000000302"),
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
            saleId: Guid.Parse("23000000-0000-0000-0000-000000000103"),
            journalEntryId: Guid.Parse("23000000-0000-0000-0000-000000000203"),
            eventId: Guid.Parse("23000000-0000-0000-0000-000000000303"),
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
            saleId: Guid.Parse("23000000-0000-0000-0000-000000000104"),
            journalEntryId: Guid.Parse("23000000-0000-0000-0000-000000000204"),
            eventId: Guid.Parse("23000000-0000-0000-0000-000000000304"),
            documentNumber: "OR-2026-000004",
            transactionTimestamp: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            subtotalCents: 500,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            totalCents: 500,
            corruptHash: false);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);
        OfficialZReadingRequest request = CreateRequest(
            new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero));

        Result<OfficialZReadingResult> result =
            database.Services.OfficialZReading.FinalizeAsync(request).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialZReading.Finalize");
        VerifyAggregateZReading(result.Value);
        VerifyPersistedZReading(database.Factory, result.Value);

        DatabaseMutationSnapshot afterSuccess = CaptureMutationSnapshot(database.Factory);
        AssertSourceMutationSnapshot(before, afterSuccess, "OfficialZReading.Finalize");
        RequireEqual(before.ZReadings + 1, afterSuccess.ZReadings, "Z-reading finalization must insert one Z-reading record.");

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(request).GetAwaiter().GetResult(),
            "OfficialZReading.DuplicateRange");
        AssertMutationSnapshot(
            afterSuccess,
            CaptureMutationSnapshot(database.Factory),
            "OfficialZReading.DuplicateRange");

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult(),
            "OfficialZReading.OverlappingRange");
        AssertMutationSnapshot(
            afterSuccess,
            CaptureMutationSnapshot(database.Factory),
            "OfficialZReading.OverlappingRange");

        Result<OfficialZReadingResult> adjacentSecondDay =
            database.Services.OfficialZReading.FinalizeAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult();

        RequireSuccess(adjacentSecondDay, "OfficialZReading.AdjacentSecondDay");
        VerifyBoundarySecondDayZReading(adjacentSecondDay.Value);
        VerifyPersistedZReading(database.Factory, adjacentSecondDay.Value);

        DatabaseMutationSnapshot afterAdjacent = CaptureMutationSnapshot(database.Factory);
        AssertSourceMutationSnapshot(afterSuccess, afterAdjacent, "OfficialZReading.AdjacentSecondDay");
        RequireEqual(afterSuccess.ZReadings + 1, afterAdjacent.ZReadings, "Adjacent daily Z-reading must insert one additional Z-reading record.");

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(request with
            {
                RangeStartedAt = request.RangeEndedAt,
                RangeEndedAt = request.RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialZReading.InvalidDateRange");

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(request with
            {
                RangeEndedAt = request.RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialZReading.InvalidDateRange");

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(request with
            {
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialZReading.MachineProfileOrScopeRequired");

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(request with
            {
                DocumentKind = (ElectronicJournalDocumentKind)999
            }).GetAwaiter().GetResult(),
            "OfficialZReading.UnsupportedDocumentKind");

        AssertMutationSnapshot(
            afterAdjacent,
            CaptureMutationSnapshot(database.Factory),
            "OfficialZReading.ValidationFailures");
    }

    private static void RunEmptyRangePath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-z-reading-empty");
        SeedBaseDependencies(database.Factory);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);
        Result<OfficialZReadingResult> result =
            database.Services.OfficialZReading.FinalizeAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialZReading.Empty Z-reading");
        RequireEqual(0, result.Value.Summary.EntryCount, "Empty Z-reading must persist zero entries.");
        RequireEqual(null, result.Value.Summary.FirstOfficialDocumentNumber, "Empty Z-reading must not invent first document number.");
        RequireEqual(null, result.Value.Summary.LastOfficialDocumentNumber, "Empty Z-reading must not invent last document number.");
        RequireEqual(0L, result.Value.Summary.TotalSalesCents, "Empty Z-reading must not invent sales totals.");
        VerifyPersistedZReading(database.Factory, result.Value);

        DatabaseMutationSnapshot after = CaptureMutationSnapshot(database.Factory);
        AssertSourceMutationSnapshot(before, after, "OfficialZReading.Empty Z-reading");
        RequireEqual(before.ZReadings + 1, after.ZReadings, "Empty Z-reading must insert one zero-total finalization record.");
    }

    private static void RunHashMismatchBlockedPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-z-reading-hash-block");
        SeedBaseDependencies(database.Factory);

        InsertOfficialSaleAndEvidence(
            database.Factory,
            saleId: Guid.Parse("23000000-0000-0000-0000-000000000401"),
            journalEntryId: Guid.Parse("23000000-0000-0000-0000-000000000402"),
            eventId: Guid.Parse("23000000-0000-0000-0000-000000000403"),
            documentNumber: "OR-2026-000004",
            transactionTimestamp: new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero),
            subtotalCents: 1000,
            lineDiscountCents: 0,
            saleDiscountCents: 0,
            taxCents: 0,
            totalCents: 1000,
            corruptHash: true);

        DatabaseMutationSnapshot before = CaptureMutationSnapshot(database.Factory);

        ExpectZReadingFailure(
            database.Services.OfficialZReading.FinalizeAsync(CreateRequest(
                new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero))).GetAwaiter().GetResult(),
            "OfficialZReading.EvidenceHashMismatch");

        AssertMutationSnapshot(
            before,
            CaptureMutationSnapshot(database.Factory),
            "OfficialZReading.EvidenceHashMismatch");
    }

    private static OfficialZReadingRequest CreateRequest(
        DateTimeOffset rangeStartedAt,
        DateTimeOffset rangeEndedAt)
    {
        return new OfficialZReadingRequest(
            rangeStartedAt,
            rangeEndedAt,
            MachineProfileId,
            RegistrationScopeCode: null,
            ElectronicJournalDocumentKind.OfficialReceipt,
            CashierUserId,
            "POS-Z-READING");
    }

    private static void VerifyAggregateZReading(OfficialZReadingResult reading)
    {
        RequireEqual(2, reading.Summary.EntryCount, "Z-reading must finalize only in-range official journal evidence.");
        RequireEqual(MachineProfileId, reading.Summary.BirMachineProfileId, "Z-reading summary must carry machine profile id.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, reading.Summary.DocumentKind, "Z-reading summary must carry document kind.");
        RequireEqual(3150L, reading.Summary.GrossSalesCents, "Z-reading gross must be computed through X-reading evidence.");
        RequireEqual(3000L, reading.Summary.SubtotalCents, "Z-reading subtotal must be computed through X-reading evidence.");
        RequireEqual(150L, reading.Summary.DiscountTotalCents, "Z-reading discount must include line and sale discounts.");
        RequireEqual(120L, reading.Summary.TaxTotalCents, "Z-reading tax total must be computed through X-reading evidence.");
        RequireEqual(2950L, reading.Summary.NetSalesCents, "Z-reading net sales must be computed through X-reading evidence.");
        RequireEqual(3070L, reading.Summary.TotalSalesCents, "Z-reading total must exclude standard and out-of-range sales.");
        RequireTrue(!string.IsNullOrWhiteSpace(reading.Summary.EvidenceManifestHash), "Z-reading must preserve evidence manifest hash.");
        RequireTrue(reading.Summary.AllSnapshotHashesMatch, "Z-reading must finalize only verified evidence hashes.");
        RequireEqual("OR-2026-000001", reading.Summary.FirstOfficialDocumentNumber, "Z-reading first document number must be deterministic.");
        RequireEqual("OR-2026-000002", reading.Summary.LastOfficialDocumentNumber, "Z-reading last document number must be deterministic.");
        RequireEqual(1, reading.Payments.Count, "Z-reading must persist payment summaries.");
        RequireEqual("Cash", reading.Payments[0].Method, "Z-reading payment summary must use snapshot payment method.");
        RequireEqual(3070L, reading.Payments[0].AmountCents, "Z-reading payment total must be computed from snapshot payments.");
        RequireEqual(2, reading.Payments[0].PaymentCount, "Z-reading payment count must be computed from snapshot payments.");
        RequireEqual(1, reading.TaxModes.Count, "Z-reading must persist tax mode summaries.");
        RequireEqual("VAT_DEFERRED", reading.TaxModes[0].TaxModeCode, "Z-reading must surface snapshot tax mode code.");
        RequireTrue(reading.TaxModes[0].VatBucketEngineDeferred, "Z-reading must preserve deferred VAT-bucket state.");
    }

    private static void VerifyBoundarySecondDayZReading(OfficialZReadingResult reading)
    {
        RequireEqual(1, reading.Summary.EntryCount, "Adjacent daily Z-reading must include only the boundary evidence.");
        RequireEqual(500L, reading.Summary.TotalSalesCents, "Boundary timestamp evidence must belong to the second half-open range.");
        RequireEqual("OR-2026-000004", reading.Summary.FirstOfficialDocumentNumber, "Second-day Z-reading first document must be the midnight evidence.");
        RequireEqual("OR-2026-000004", reading.Summary.LastOfficialDocumentNumber, "Second-day Z-reading last document must be the midnight evidence.");
        RequireEqual(1, reading.Payments.Count, "Second-day Z-reading must preserve boundary payment summary.");
        RequireEqual(500L, reading.Payments[0].AmountCents, "Second-day Z-reading payment total must match boundary evidence.");
    }

    private static void VerifyPersistedZReading(
        SqliteConnectionFactory factory,
        OfficialZReadingResult reading)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
                terminal_id
            FROM bir_z_readings
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", reading.Summary.ZReadingId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new InvalidOperationException("Persisted Z-reading row was not found.");
        }

        RequireEqual(reading.Summary.EntryCount, reader.GetInt32(0), "Persisted Z-reading entry count must match result.");
        RequireEqual(reading.Summary.FirstOfficialDocumentNumber, ReadNullableString(reader, 1), "Persisted Z-reading first document must match result.");
        RequireEqual(reading.Summary.LastOfficialDocumentNumber, ReadNullableString(reader, 2), "Persisted Z-reading last document must match result.");
        RequireEqual(reading.Summary.GrossSalesCents, reader.GetInt64(3), "Persisted Z-reading gross must match result.");
        RequireEqual(reading.Summary.SubtotalCents, reader.GetInt64(4), "Persisted Z-reading subtotal must match result.");
        RequireEqual(reading.Summary.DiscountTotalCents, reader.GetInt64(5), "Persisted Z-reading discount must match result.");
        RequireEqual(reading.Summary.TaxTotalCents, reader.GetInt64(6), "Persisted Z-reading tax must match result.");
        RequireEqual(reading.Summary.NetSalesCents, reader.GetInt64(7), "Persisted Z-reading net sales must match result.");
        RequireEqual(reading.Summary.TotalSalesCents, reader.GetInt64(8), "Persisted Z-reading total must match result.");
        RequireTrue(reader.GetString(9).StartsWith("[", StringComparison.Ordinal), "Persisted payment summary must be JSON.");
        RequireTrue(reader.GetString(10).StartsWith("[", StringComparison.Ordinal), "Persisted tax-mode summary must be JSON.");
        RequireEqual(reading.Summary.EvidenceManifestHash, reader.GetString(11), "Persisted Z-reading evidence hash must match result.");
        RequireEqual(reading.Summary.AllSnapshotHashesMatch ? 1 : 0, reader.GetInt32(12), "Persisted Z-reading hash flag must match result.");
        RequireEqual(CashierUserId.ToString("D"), ReadNullableString(reader, 13), "Persisted Z-reading user id must match request.");
        RequireEqual("POS-Z-READING", ReadNullableString(reader, 14), "Persisted Z-reading terminal id must match request.");
    }

    private static void SeedBaseDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Execute(connection, """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('23000000-0000-0000-0000-000000000901', 'Official Z Reading Cashier', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('23000000-0000-0000-0000-000000000001', 'official-z-reading-cashier', 'Official Z Reading Cashier', 'HASH', '23000000-0000-0000-0000-000000000901', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('23000000-0000-0000-0000-000000000902', 'piece', 'pc', 0, 1);
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
                    'Z-READING-STOCK',
                    '8990001112223',
                    'Z Reading Stock Sentinel',
                    '23000000-0000-0000-0000-000000000902',
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
                'Z Reading Taxpayer',
                'Z Reading Store',
                'Z Reading Registered Address',
                '000-333-444-555',
                '00001',
                'MIN-Z-READING',
                'SERIAL-Z-READING',
                'ACC-Z-READING',
                'PTU-Z-READING',
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
            saleId: Guid.Parse("23000000-0000-0000-0000-000000000099"),
            receiptNumber: "STD-2026-999999",
            saleDate: new DateTimeOffset(2026, 6, 25, 9, 15, 0, TimeSpan.Zero),
            subtotalCents: 999999,
            discountCents: 0,
            taxCents: 0,
            totalCents: 999999,
            receiptSnapshot: "Standard sale receipt snapshot must not be Z-reading evidence.");
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
            receiptSnapshot: "Sales receipt_snapshot is not official Z-reading evidence.");

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
                    RegisteredTaxpayerName: "Z Reading Taxpayer",
                    BusinessName: "Z Reading Store",
                    TaxpayerIdentificationNumber: "000-333-444-555",
                    BranchCode: "00001",
                    RegisteredBusinessAddress: "Z Reading Registered Address",
                    TransactionDateTime: transactionTimestamp,
                    DocumentNumber: documentNumber,
                    MachineSerialNumber: "SERIAL-Z-READING",
                    MachineIdentificationNumber: "MIN-Z-READING",
                    AccreditationNumber: "ACC-Z-READING",
                    PermitToUseNumber: "PTU-Z-READING",
                    CashierIdentifier: "official-z-reading-cashier",
                    TerminalIdentifier: "POS-Z-READING"),
                Lines: new[]
                {
                    new OfficialDocumentSnapshotDraftLine(
                        Description: "Z-reading evidence item",
                        Barcode: "8990001112223",
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

        RequireSuccess(result, "OfficialZReading.SnapshotEvidence");

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
                    'POS-Z-READING',
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
            ZReadings: CountRows(factory, "bir_z_readings"),
            CurrentSequenceNumber: ReadCurrentSequenceNumber(factory),
            ProductStockX1000: ReadProductStock(factory));
    }

    private static void AssertMutationSnapshot(
        DatabaseMutationSnapshot expected,
        DatabaseMutationSnapshot actual,
        string area)
    {
        RequireEqual(expected, actual, area + " must not mutate persisted state.");
    }

    private static void AssertSourceMutationSnapshot(
        DatabaseMutationSnapshot expected,
        DatabaseMutationSnapshot actual,
        string area)
    {
        RequireEqual(expected.Sales, actual.Sales, area + " must not mutate sales.");
        RequireEqual(expected.SaleItems, actual.SaleItems, area + " must not mutate sale items.");
        RequireEqual(expected.Payments, actual.Payments, area + " must not mutate payments.");
        RequireEqual(expected.StockMovements, actual.StockMovements, area + " must not mutate stock movements.");
        RequireEqual(expected.JournalEntries, actual.JournalEntries, area + " must not mutate journal entries.");
        RequireEqual(expected.JournalEvents, actual.JournalEvents, area + " must not mutate journal events.");
        RequireEqual(expected.CurrentSequenceNumber, actual.CurrentSequenceNumber, area + " must not advance official sequences.");
        RequireEqual(expected.ProductStockX1000, actual.ProductStockX1000, area + " must not mutate stock.");
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

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static void ExpectZReadingFailure(
        Result<OfficialZReadingResult> result,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected Z-reading failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected Z-reading failure code.");
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
                throw new InvalidOperationException("Official Z-reading smoke database initialization failed: " + initResult.Error);
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

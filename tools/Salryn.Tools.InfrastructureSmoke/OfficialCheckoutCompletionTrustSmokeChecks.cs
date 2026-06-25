using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Paths;

internal static class OfficialCheckoutCompletionTrustSmokeChecks
{
    private static readonly Guid CashierUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CashierSessionId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ReadyMachineProfileId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid SequenceId = Guid.Parse("10000000-0000-0000-0000-000000000005");

    private const string TrustedProductName = "Trusted Official Boundary Product";
    private const string TrustedBarcode = "7799001122334";
    private const string TrustedUnitName = "trusted-piece";
    private const long TrustedUnitPriceCents = 12345;
    private const long TrustedCostPriceCents = 6789;
    private const long InitialStockX1000 = 5000;

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== OFFICIAL CHECKOUT COMPLETION TRUST SMOKE CHECKS =====");

        RunSuccessTrustPath();
        RunReadinessBlockedNoSideEffectsPath();
        RunTrustedBuilderBlockedNoSideEffectsPath();

        Console.WriteLine("Official checkout completion trust smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RunSuccessTrustPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-completion-trust-success");
        SeedOfficialCompletionDependencies(
            database.Factory,
            officialReady: true,
            allowDiscount: true);

        Result result =
            database.Services.OfficialCheckoutCompletion.CompleteSaleAsync(CreateSpoofedRequest()).GetAwaiter().GetResult();

        RequireSuccess(result, "OfficialCompletionTrust.Success");

        string saleId = RequireString(
            database.Factory,
            "SELECT id FROM sales LIMIT 1;",
            "OfficialCompletionTrust.Success.SaleId");

        RequireEqual("OR-2026-000001", ReadSaleReceiptNo(database.Factory, saleId), "Sale receipt number must be the allocated official number.");
        RequireEqual(1L, CountRows(database.Factory, "sales"), "Official completion must insert one sale.");
        RequireEqual(1L, CountRows(database.Factory, "sale_items"), "Official completion must insert one sale item.");
        RequireEqual(1L, CountRows(database.Factory, "payments"), "Official completion must insert one payment.");
        RequireEqual(1L, CountRows(database.Factory, "stock_movements"), "Trusted TrackStock=true must create one stock movement.");
        RequireEqual(1L, CountRows(database.Factory, "bir_electronic_journal_entries"), "Official completion must insert one journal entry.");
        RequireEqual(1L, CountRows(database.Factory, "bir_electronic_journal_entry_events"), "Official completion must insert one journal ORIGINAL event.");
        RequireEqual(1L, CountRowsWhere(database.Factory, "bir_electronic_journal_entry_events", "event_type = 'ORIGINAL' AND original_marker = 1"), "Journal event must be ORIGINAL.");
        RequireEqual(1L, ReadCurrentSequenceNumber(database.Factory), "Official sequence must advance once.");
        RequireEqual(4000L, ReadProductStock(database.Factory), "Trusted TrackStock=true must reduce persisted stock.");

        RequireEqual(TrustedProductName, ReadSaleItemText(database.Factory, "product_name_snapshot"), "Sale item name must come from persisted product facts.");
        RequireEqual(TrustedBarcode, ReadSaleItemText(database.Factory, "barcode_snapshot"), "Sale item barcode must come from persisted product facts.");
        RequireEqual(TrustedUnitName, ReadSaleItemText(database.Factory, "unit_name_snapshot"), "Sale item unit must come from persisted product facts.");
        RequireEqual(TrustedUnitPriceCents, ReadSaleItemLong(database.Factory, "unit_price_cents"), "Sale item unit price must come from persisted product facts.");
        RequireEqual(TrustedCostPriceCents, ReadSaleItemLong(database.Factory, "cost_price_cents"), "Sale item cost must come from persisted product facts.");
        RequireEqual(TrustedUnitPriceCents, ReadSaleItemLong(database.Factory, "line_total_cents"), "Line total must follow trusted persisted unit price.");

        RequireEqual("OR-2026-000001", ReadJournalDocumentNumber(database.Factory), "Journal document number must match allocated official number.");
        RequireEqual("OR-2026-000001", ReadSnapshotDocumentNumber(database.Factory), "Snapshot document number must match allocated official number.");
        RequireEqual(TrustedProductName, ReadSnapshotFirstLineDescription(database.Factory), "Snapshot line must use trusted persisted sale item facts.");

        DatabaseMutationSnapshot beforeExport = CaptureMutationSnapshot(database.Factory);
        OfficialEvidenceReadRequest exportRequest = CreateEvidenceReadRequest();

        Result<OfficialEvidenceExportResult> firstExport =
            database.Services.OfficialEvidenceRead.ExportAsync(exportRequest).GetAwaiter().GetResult();

        RequireSuccess(firstExport, "OfficialCompletionTrust.EvidenceExport.First");
        VerifyEvidenceExport(firstExport.Value, saleId, "OR-2026-000001");

        Result<OfficialEvidenceExportResult> secondExport =
            database.Services.OfficialEvidenceRead.ExportAsync(exportRequest).GetAwaiter().GetResult();

        RequireSuccess(secondExport, "OfficialCompletionTrust.EvidenceExport.Second");
        VerifyDeterministicEvidenceExport(firstExport.Value, secondExport.Value);

        AssertMutationSnapshot(
            beforeExport,
            CaptureMutationSnapshot(database.Factory),
            "OfficialCompletionTrust.EvidenceExport");

        ExpectEvidenceExportFailure(
            database.Services.OfficialEvidenceRead.ExportAsync(exportRequest with
            {
                RangeStartedAt = exportRequest.RangeEndedAt,
                RangeEndedAt = exportRequest.RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialCompletionTrust.EvidenceExport.InvalidRange",
            "OfficialEvidence.InvalidDateRange");

        ExpectEvidenceExportFailure(
            database.Services.OfficialEvidenceRead.ExportAsync(exportRequest with
            {
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialCompletionTrust.EvidenceExport.MissingProfileOrScope",
            "OfficialEvidence.MachineProfileOrScopeRequired");

        ExpectEvidenceExportFailure(
            database.Services.OfficialEvidenceRead.ExportAsync(exportRequest with
            {
                DocumentKind = (ElectronicJournalDocumentKind)999
            }).GetAwaiter().GetResult(),
            "OfficialCompletionTrust.EvidenceExport.UnsupportedDocumentKind",
            "OfficialEvidence.UnsupportedDocumentKind");

        AssertMutationSnapshot(
            beforeExport,
            CaptureMutationSnapshot(database.Factory),
            "OfficialCompletionTrust.EvidenceExport.InvalidRequests");
    }

    private static void RunReadinessBlockedNoSideEffectsPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-completion-trust-readiness-blocked");
        SeedOfficialCompletionDependencies(
            database.Factory,
            officialReady: false,
            allowDiscount: true);

        Result result =
            database.Services.OfficialCheckoutCompletion.CompleteSaleAsync(CreateSpoofedRequest()).GetAwaiter().GetResult();

        ExpectFailure(
            result,
            "OfficialCompletionTrust.ReadinessBlocked",
            "OfficialCheckout.ReadinessBlocked");

        AssertNoOfficialSideEffects(database.Factory, "OfficialCompletionTrust.ReadinessBlocked");
    }

    private static void RunTrustedBuilderBlockedNoSideEffectsPath()
    {
        using SmokeDatabase database = SmokeDatabase.Create("salryn-official-completion-trust-builder-blocked");
        SeedOfficialCompletionDependencies(
            database.Factory,
            officialReady: true,
            allowDiscount: false);

        Result result =
            database.Services.OfficialCheckoutCompletion.CompleteSaleAsync(
                CreateSpoofedRequest(lineDiscountCents: 100)).GetAwaiter().GetResult();

        ExpectFailure(
            result,
            "OfficialCompletionTrust.DiscountNotAllowed",
            "CheckoutBoundary.ProductDiscountNotAllowed");

        AssertNoOfficialSideEffects(database.Factory, "OfficialCompletionTrust.DiscountNotAllowed");
    }

    private static PosCheckoutApplicationRequest CreateSpoofedRequest(long lineDiscountCents = 0)
    {
        return new PosCheckoutApplicationRequest(
            CashierUserId,
            CashierSessionId,
            "UI-SPOOFED-RECEIPT",
            new[]
            {
                new PosCheckoutLineInput(
                    ProductId,
                    "Spoofed UI Product Name",
                    "SPOOFED-BARCODE",
                    "spoofed-unit",
                    QuantityX1000: 1000,
                    UnitPriceCents: 1,
                    CostPriceCents: 2,
                    DiscountCents: lineDiscountCents,
                    TrackStock: false)
            },
            new[]
            {
                new PosCheckoutPaymentInput(PaymentMethod.Cash, TrustedUnitPriceCents)
            },
            SaleDiscountCents: 0,
            TaxCents: 0,
            ReceiptSnapshot: "Spoofed UI receipt snapshot.",
            SaleDate: new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero));
    }

    private static OfficialEvidenceReadRequest CreateEvidenceReadRequest()
    {
        return new OfficialEvidenceReadRequest(
            RangeStartedAt: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            RangeEndedAt: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            BirMachineProfileId: ReadyMachineProfileId,
            RegistrationScopeCode: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt);
    }

    private static void VerifyEvidenceExport(
        OfficialEvidenceExportResult export,
        string saleId,
        string expectedDocumentNumber)
    {
        RequireEqual(1, export.Manifest.EntryCount, "Evidence export manifest must report one entry.");
        RequireEqual(ReadyMachineProfileId, export.Manifest.BirMachineProfileId, "Evidence export manifest must identify machine profile.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, export.Manifest.DocumentKind, "Evidence export manifest must identify document kind.");
        RequireEqual("SHA-256", export.Manifest.HashAlgorithm, "Evidence export manifest must identify hash algorithm.");
        RequireTrue(export.Manifest.AllSnapshotHashesMatch, "Evidence export manifest must report matching snapshot hashes.");
        RequireTrue(!string.IsNullOrWhiteSpace(export.Manifest.ManifestHash), "Evidence export manifest hash is required.");
        RequireEqual(1, export.Entries.Count, "Evidence export must return one journal entry.");

        OfficialEvidenceEntryReadModel entry = export.Entries[0];

        RequireEqual(Guid.Parse(saleId), entry.SaleId, "Evidence entry sale id must match the official sale.");
        RequireEqual(ReadyMachineProfileId, entry.BirMachineProfileId, "Evidence entry must carry machine profile id.");
        RequireEqual(SequenceId, entry.OfficialDocumentSequenceId, "Evidence entry must carry sequence id.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, entry.DocumentKind, "Evidence entry must carry official receipt kind.");
        RequireEqual(expectedDocumentNumber, entry.OfficialDocumentNumber, "Evidence entry document number must match official number.");
        RequireEqual(expectedDocumentNumber, ReadSnapshotDocumentNumber(entry.SnapshotPayloadJson), "Evidence entry snapshot document number must match official number.");
        RequireEqual(TrustedProductName, ReadSnapshotFirstLineDescription(entry.SnapshotPayloadJson), "Evidence entry must read official snapshot payload, not sales receipt preview text.");
        RequireTrue(!entry.SnapshotPayloadJson.Contains("Spoofed UI receipt snapshot.", StringComparison.Ordinal), "Evidence entry must not use sales.receipt_snapshot or UI preview text.");
        RequireEqual("SHA-256", entry.SnapshotHashAlgorithm, "Evidence entry must carry snapshot hash algorithm.");
        RequireTrue(entry.SnapshotHashMatches, "Evidence entry snapshot hash must verify.");
        RequireEqual(entry.SnapshotPayloadHash, entry.RecomputedSnapshotPayloadHash, "Recomputed snapshot hash must match stored hash.");
        RequireTrue(entry.OriginalEventId != Guid.Empty, "Evidence entry must include ORIGINAL event id.");
        RequireEqual(CashierUserId, entry.CashierUserId, "Evidence entry must include cashier id.");
        RequireTrue(!string.IsNullOrWhiteSpace(entry.TerminalId), "Evidence entry must include terminal id when persisted.");
    }

    private static void VerifyDeterministicEvidenceExport(
        OfficialEvidenceExportResult first,
        OfficialEvidenceExportResult second)
    {
        RequireEqual(first.Manifest.ManifestHash, second.Manifest.ManifestHash, "Evidence export manifest hash must be deterministic.");
        RequireEqual(first.Manifest.EntryCount, second.Manifest.EntryCount, "Evidence export entry count must be deterministic.");
        RequireEqual(first.Entries.Count, second.Entries.Count, "Evidence export entries must be deterministic.");

        for (int index = 0; index < first.Entries.Count; index++)
        {
            RequireEqual(first.Entries[index].JournalEntryId, second.Entries[index].JournalEntryId, "Evidence export order must be deterministic.");
            RequireEqual(first.Entries[index].OfficialDocumentNumber, second.Entries[index].OfficialDocumentNumber, "Evidence export document order must be deterministic.");
            RequireEqual(first.Entries[index].SnapshotPayloadHash, second.Entries[index].SnapshotPayloadHash, "Evidence export hashes must be deterministic.");
        }
    }

    private static void SeedOfficialCompletionDependencies(
        SqliteConnectionFactory factory,
        bool officialReady,
        bool allowDiscount)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Execute(connection, """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('10000000-0000-0000-0000-000000000101', 'Official Trust Cashier', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('10000000-0000-0000-0000-000000000001', 'official-trust-cashier', 'Official Trust Cashier', 'HASH', '10000000-0000-0000-0000-000000000101', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO cashier_sessions (id, user_id, opened_at, opening_cash_cents, expected_cash_cents, variance_cents, status)
            VALUES ('10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', $now, 100000, 100000, 0, 1);
            """);

        Execute(connection, """
            INSERT INTO categories (id, name, is_active)
            VALUES ('10000000-0000-0000-0000-000000000201', 'Official Trust Category', 1);
            """);

        Execute(connection, """
            INSERT INTO brands (id, name, is_active)
            VALUES ('10000000-0000-0000-0000-000000000202', 'Official Trust Brand', 1);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('10000000-0000-0000-0000-000000000203', 'trusted-piece', 'tpc', 0, 1);
            """);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO products (
                    id,
                    sku,
                    barcode,
                    name,
                    category_id,
                    brand_id,
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
                    'OFFICIAL-TRUST',
                    $barcode,
                    $name,
                    '10000000-0000-0000-0000-000000000201',
                    '10000000-0000-0000-0000-000000000202',
                    '10000000-0000-0000-0000-000000000203',
                    $cost,
                    $price,
                    $stock,
                    1000,
                    1,
                    $allowDiscount,
                    1,
                    $now
                );
                """;
            command.Parameters.AddWithValue("$id", ProductId.ToString("D"));
            command.Parameters.AddWithValue("$barcode", TrustedBarcode);
            command.Parameters.AddWithValue("$name", TrustedProductName);
            command.Parameters.AddWithValue("$cost", TrustedCostPriceCents);
            command.Parameters.AddWithValue("$price", TrustedUnitPriceCents);
            command.Parameters.AddWithValue("$stock", InitialStockX1000);
            command.Parameters.AddWithValue("$allowDiscount", allowDiscount ? 1 : 0);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        InsertMachineProfile(connection, officialReady);
        InsertSequence(connection, officialReady);
    }

    private static void InsertMachineProfile(SqliteConnection connection, bool officialReady)
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
                'Trusted Official Taxpayer',
                'Trusted Official Store',
                'Trusted Official Registered Address',
                '000-111-222-333',
                '00001',
                $min,
                'SERIAL-OFFICIAL-TRUST',
                $accreditation,
                $ptu,
                'Salryn',
                'TEST',
                $complianceMode,
                $officialOutputEnabled,
                $isActive,
                $now
            );
            """;
        command.Parameters.AddWithValue("$id", ReadyMachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$min", officialReady ? "MIN-OFFICIAL-TRUST" : DBNull.Value);
        command.Parameters.AddWithValue("$accreditation", officialReady ? "ACC-OFFICIAL-TRUST" : DBNull.Value);
        command.Parameters.AddWithValue("$ptu", officialReady ? "PTU-OFFICIAL-TRUST" : DBNull.Value);
        command.Parameters.AddWithValue("$complianceMode", officialReady ? "ACCREDITED" : "NOT_ACCREDITED");
        command.Parameters.AddWithValue("$officialOutputEnabled", officialReady ? 1 : 0);
        command.Parameters.AddWithValue("$isActive", officialReady ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(SqliteConnection connection, bool isActive)
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
                0,
                99,
                6,
                $isActive,
                0,
                $now
            );
            """;
        command.Parameters.AddWithValue("$id", SequenceId.ToString("D"));
        command.Parameters.AddWithValue("$profileId", ReadyMachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void AssertNoOfficialSideEffects(
        SqliteConnectionFactory factory,
        string area)
    {
        RequireEqual(0L, CountRows(factory, "sales"), area + " must not insert a sale.");
        RequireEqual(0L, CountRows(factory, "sale_items"), area + " must not insert a sale item.");
        RequireEqual(0L, CountRows(factory, "payments"), area + " must not insert a payment.");
        RequireEqual(0L, CountRows(factory, "stock_movements"), area + " must not insert a stock movement.");
        RequireEqual(0L, CountRows(factory, "bir_electronic_journal_entries"), area + " must not insert a journal entry.");
        RequireEqual(0L, CountRows(factory, "bir_electronic_journal_entry_events"), area + " must not insert a journal event.");
        RequireEqual(0L, ReadCurrentSequenceNumber(factory), area + " must not advance official sequence.");
        RequireEqual(InitialStockX1000, ReadProductStock(factory), area + " must not mutate product stock.");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
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

    private static string ReadSaleReceiptNo(SqliteConnectionFactory factory, string saleId)
    {
        return ReadString(
            factory,
            "SELECT receipt_no FROM sales WHERE id = $id;",
            command => command.Parameters.AddWithValue("$id", saleId));
    }

    private static string ReadJournalDocumentNumber(SqliteConnectionFactory factory)
    {
        return RequireString(
            factory,
            "SELECT official_document_number FROM bir_electronic_journal_entries LIMIT 1;",
            "OfficialCompletionTrust.JournalDocumentNumber");
    }

    private static string ReadSnapshotDocumentNumber(SqliteConnectionFactory factory)
    {
        return ReadSnapshotDocumentNumber(ReadSnapshotPayloadJson(factory));
    }

    private static string ReadSnapshotDocumentNumber(string snapshotPayloadJson)
    {
        using JsonDocument document = JsonDocument.Parse(snapshotPayloadJson);

        return document.RootElement
            .GetProperty("header")
            .GetProperty("documentNumber")
            .GetString() ?? string.Empty;
    }

    private static string ReadSnapshotFirstLineDescription(SqliteConnectionFactory factory)
    {
        return ReadSnapshotFirstLineDescription(ReadSnapshotPayloadJson(factory));
    }

    private static string ReadSnapshotFirstLineDescription(string snapshotPayloadJson)
    {
        using JsonDocument document = JsonDocument.Parse(snapshotPayloadJson);

        return document.RootElement
            .GetProperty("lines")[0]
            .GetProperty("description")
            .GetString() ?? string.Empty;
    }

    private static string ReadSnapshotPayloadJson(SqliteConnectionFactory factory)
    {
        return RequireString(
            factory,
            "SELECT snapshot_payload_json FROM bir_electronic_journal_entries LIMIT 1;",
            "OfficialCompletionTrust.SnapshotPayload");
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

    private static string ReadSaleItemText(SqliteConnectionFactory factory, string columnName)
    {
        return RequireString(
            factory,
            $"SELECT {columnName} FROM sale_items LIMIT 1;",
            "OfficialCompletionTrust.SaleItem." + columnName);
    }

    private static long ReadSaleItemLong(SqliteConnectionFactory factory, string columnName)
    {
        return ReadLong(
            factory,
            $"SELECT {columnName} FROM sale_items LIMIT 1;",
            configure: null);
    }

    private static string RequireString(
        SqliteConnectionFactory factory,
        string sql,
        string area)
    {
        string value = ReadString(factory, sql, configure: null);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(area + " returned no value.");
        }

        return value;
    }

    private static string ReadString(
        SqliteConnectionFactory factory,
        string sql,
        Action<SqliteCommand>? configure)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);

        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
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

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.ErrorDetail.Code} {result.Error}");
        }
    }

    private static void ExpectFailure(
        Result result,
        string area,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(area + " was expected to fail.");
        }

        if (!string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{area} expected {expectedCode}, found {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void ExpectEvidenceExportFailure(
        Result<OfficialEvidenceExportResult> result,
        string area,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(area + " was expected to fail.");
        }

        if (!string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{area} expected {expectedCode}, found {result.ErrorDetail.Code}: {result.Error}");
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
                throw new InvalidOperationException("Official completion trust smoke database initialization failed: " + initResult.Error);
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
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}

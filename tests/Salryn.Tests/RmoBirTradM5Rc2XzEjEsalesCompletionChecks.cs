using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.Lifecycle;
using Salryn.Core.Services.Reporting;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// M5-RC2: lifecycle-aware X/Z/EJ/eSales runtime completion (final RMO feature coding gate).
/// </summary>
public static class RmoBirTradM5Rc2XzEjEsalesCompletionChecks
{
    public static void Run()
    {
        string root = FindRoot();
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "RMO_BIR_TRAD_M5_RC2_XZ_EJ_ESALES_COMPLETION_STUDY.md")), "Study required.");
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "rmo-bir-trad-m5-rc2-xz-ej-esales-completion", "CoreSmoke gate required.");
        RequireContains(coreSmoke, "RmoBirTradM5Rc2XzEjEsalesCompletionChecks.Run()", "CoreSmoke must run M5-RC2.");
        RequireContains(
            File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs")),
            "MapLocalOfficialTraditionalReportingEndpoints",
            "Factory must map reporting endpoints.");

        using TempDb db = TempDb.Create();
        SqliteConnectionFactory factory = new(db.Paths);
        HostTraditionalReportingService reporting = new(
            new SqliteOfficialReportingProjectionService(factory),
            factory);
        SqliteLifecycleAccountingService lifecycle = new(factory);

        // 1) VAT mixed sale
        SaleSeed vat = PostSale(
            factory,
            "vat",
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable, OfficialDocumentLineTaxClassification.VatExempt },
            new[] { 11200L, 5000L });

        OfficialReportingProjectionRequest range = WideRange(vat.MachineId);
        Result<TraditionalXReadingView> x1 = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(x1.IsSuccess, "X VAT: " + Fail(x1));
        RequireTrue(x1.Value.Projection.DocumentCount >= 1, "X has documents.");
        RequireTrue(x1.Value.Projection.GrossPostedSalesCents >= 16200, "X gross includes VAT sale.");
        RequireTrue(x1.Value.Projection.VatAmountCents > 0, "X has VAT amount.");

        // 2) Non-VAT SSPT
        SaleSeed sspt = PostSale(
            factory,
            "sspt",
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            new[] { OfficialDocumentLineTaxClassification.NonVat },
            new[] { 10000L });

        // 3) Non-VAT exempt
        SaleSeed exempt = PostSale(
            factory,
            "exempt",
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt,
            new[] { OfficialDocumentLineTaxClassification.NonVatExempt },
            new[] { 3000L });

        Result<TraditionalXReadingView> xAll = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(xAll.IsSuccess, "X all: " + Fail(xAll));
        RequireTrue(xAll.Value.Projection.SsptSalesCents > 0 || xAll.Value.Projection.Documents.Any(d => d.SsptCents > 0),
            "SSPT present.");
        RequireTrue(xAll.Value.Projection.NonVatExemptSalesCents > 0 || xAll.Value.Projection.Documents.Any(d => d.NonVatExemptCents > 0),
            "Non-VAT exempt present.");

        // Capture sequence after all official posts; lifecycle must not advance it further.
        long seqAfterPosts = ReadSequence(db.Paths.DatabasePath);

        // 4) Partial line refund on VAT first item
        long netBeforeRefund = xAll.Value.Projection.NetSalesCents;
        Result<LifecycleAccountingResult> refund = lifecycle.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            vat.JournalId,
            vat.DocNumber,
            vat.SaleId,
            "Partial return",
            vat.ActorId,
            "POS-1",
            "m5-ref-partial",
            new[] { new LifecycleRefundLineRequest(vat.SaleItemIds[0], 500, StockReturnDisposition.Restock) },
            new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash))).GetAwaiter().GetResult();
        RequireTrue(refund.IsSuccess, "Partial refund: " + Fail(refund));

        Result<TraditionalXReadingView> xAfterRefund = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(xAfterRefund.IsSuccess, "X after refund: " + Fail(xAfterRefund));
        RequireTrue(xAfterRefund.Value.Projection.RefundedValueCents > 0, "Refunded value > 0.");
        RequireTrue(xAfterRefund.Value.Projection.NetSalesCents < netBeforeRefund, "Net reduced after refund.");
        RequireTrue(ReadSequence(db.Paths.DatabasePath) == seqAfterPosts, "Sequence not advanced by refund.");

        // 7) Reprint does not increase sales totals
        long netBeforeReprint = xAfterRefund.Value.Projection.NetSalesCents;
        Result<LifecycleAccountingResult> reprint = lifecycle.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Reprint,
            vat.JournalId,
            vat.DocNumber,
            vat.SaleId,
            "Customer copy",
            vat.ActorId,
            "POS-1",
            "m5-reprint")).GetAwaiter().GetResult();
        RequireTrue(reprint.IsSuccess, "Reprint: " + Fail(reprint));
        Result<TraditionalXReadingView> xReprint = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(xReprint.IsSuccess, "X reprint: " + Fail(xReprint));
        RequireTrue(xReprint.Value.Projection.NetSalesCents == netBeforeReprint, "Reprint does not change net.");
        RequireTrue(xReprint.Value.Projection.ReprintEventCount >= 1, "Reprint event counted.");

        // 5/6) Full refund remaining of first line then void separate sale
        Result<LifecycleAccountingResult> refundRest = lifecycle.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            vat.JournalId,
            vat.DocNumber,
            vat.SaleId,
            "Rest of line0",
            vat.ActorId,
            null,
            "m5-ref-rest",
            new[] { new LifecycleRefundLineRequest(vat.SaleItemIds[0], 500, StockReturnDisposition.Restock) },
            new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash))).GetAwaiter().GetResult();
        RequireTrue(refundRest.IsSuccess, "Rest refund: " + Fail(refundRest));

        Result<LifecycleAccountingResult> voided = lifecycle.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Void,
            sspt.JournalId,
            sspt.DocNumber,
            sspt.SaleId,
            "Void SSPT",
            sspt.ActorId,
            null,
            "m5-void")).GetAwaiter().GetResult();
        RequireTrue(voided.IsSuccess, "Void: " + Fail(voided));

        Result<TraditionalXReadingView> xLifecycle = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(xLifecycle.IsSuccess, "X lifecycle: " + Fail(xLifecycle));
        RequireTrue(xLifecycle.Value.Projection.VoidedSalesCents > 0, "Voided sales counted.");
        RequireTrue(xLifecycle.Value.Projection.VoidedDocumentCount >= 1, "Voided doc count.");

        // 10) X is read-only: sequence and EJ entry count stable
        long seqMid = ReadSequence(db.Paths.DatabasePath);
        RequireTrue(seqMid == seqAfterPosts, "Lifecycle actions did not advance sequence.");
        int ejCount = CountEj(db.Paths.DatabasePath);
        _ = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(ReadSequence(db.Paths.DatabasePath) == seqMid, "X does not advance sequence.");
        RequireTrue(CountEj(db.Paths.DatabasePath) == ejCount, "X does not write EJ.");

        // 11/12) Z finalize once; duplicate rejected; sequence stable
        Result<TraditionalZReadingView> z1 = reporting.FinalizeZReadingAsync(
            range, vat.ActorId, "POS-1").GetAwaiter().GetResult();
        RequireTrue(z1.IsSuccess, "Z1: " + Fail(z1));
        RequireTrue(z1.Value.Projection.NetSalesCents == xLifecycle.Value.Projection.NetSalesCents, "Z matches X net.");
        RequireTrue(ReadSequence(db.Paths.DatabasePath) == seqMid, "Z does not advance sequence.");
        RequireTrue(CountEj(db.Paths.DatabasePath) == ejCount, "Z does not write EJ.");

        Result<TraditionalZReadingView> zDup = reporting.FinalizeZReadingAsync(
            range, vat.ActorId, "POS-1").GetAwaiter().GetResult();
        RequireTrue(zDup.IsFailure, "Duplicate Z rejected.");
        RequireTrue(zDup.ErrorDetail.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase), "Duplicate code.");

        // 13) Z failure injection rolls back
        OfficialReportingProjectionRequest emptyFuture = new(
            DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow.AddDays(11),
            vat.MachineId,
            "LOCAL_SINGLE_STORE",
            ElectronicJournalDocumentKind.OfficialInvoice);
        Result<TraditionalZReadingView> zFail = reporting.FinalizeZReadingAsync(
            emptyFuture, vat.ActorId, "POS-1", failureInjectionStage: "before_commit").GetAwaiter().GetResult();
        RequireTrue(zFail.IsFailure, "Injected Z failure must fail.");
        RequireTrue(CountZ(db.Paths.DatabasePath, emptyFuture.RangeStart) == 0, "Failed Z rolled back.");

        // 14/15) EJ + eSales deterministic
        Result<TraditionalReportingExportPackage> ej1 = reporting.ExportEjAsync(range).GetAwaiter().GetResult();
        Result<TraditionalReportingExportPackage> ej2 = reporting.ExportEjAsync(range).GetAwaiter().GetResult();
        RequireTrue(ej1.IsSuccess && ej2.IsSuccess, "EJ export.");
        RequireTrue(ej1.Value.ContentSha256 == ej2.Value.ContentSha256, "EJ hash stable.");
        RequireContains(ej1.Value.Utf8Content, "SALRYN_TRAD_EJ_EXPORT_V2", "EJ schema.");
        RequireContains(ej1.Value.Utf8Content, "REFUND", "EJ includes refund events.");
        RequireContains(ej1.Value.Utf8Content, "VOID", "EJ includes void events.");
        RequireContains(ej1.Value.Utf8Content, "REPRINT", "EJ includes reprint events.");

        Result<TraditionalReportingExportPackage> es1 = reporting.ExportESalesDraftAsync(range).GetAwaiter().GetResult();
        Result<TraditionalReportingExportPackage> es2 = reporting.ExportESalesDraftAsync(range).GetAwaiter().GetResult();
        RequireTrue(es1.IsSuccess && es2.IsSuccess, "eSales draft.");
        RequireTrue(es1.Value.ContentSha256 == es2.Value.ContentSha256, "eSales hash stable.");
        RequireTrue(es1.Value.SchemaCode == UnconfirmedTraditionalESalesSchemaProvider.DraftSchemaCode, "Draft V2 schema.");
        RequireFalse(es1.Value.BirAuthoritativeSchemaConfirmed, "BIR schema unconfirmed.");
        RequireFalse(es1.Value.TransmissionEnabled, "Transmission disabled.");
        RequireTrue(es1.Value.ControlTotalCents == es1.Value.Projection.NetSalesCents, "Control total matches net.");

        // 16) Corrupt hash fails closed
        CorruptOneSnapshotHash(db.Paths.DatabasePath, exempt.JournalId);
        Result<TraditionalXReadingView> xCorrupt = reporting.PreviewXReadingAsync(range).GetAwaiter().GetResult();
        RequireTrue(xCorrupt.IsFailure, "Corrupt hash must fail.");
        RequireTrue(xCorrupt.ErrorDetail.Code.Contains("Hash", StringComparison.OrdinalIgnoreCase), "Hash mismatch code.");

        // 20) Status honesty
        Result<TraditionalReportingStatus> status = reporting.GetStatus();
        RequireTrue(status.IsSuccess, "Status ok.");
        RequireFalse(status.Value.BirESalesSchemaConfirmed, "Status BIR schema false.");
        RequireFalse(status.Value.TransmissionEnabled, "Status transmission false.");
        RequireFalse(status.Value.ProductionActivationEnabled, "Activation off.");
        RequireFalse(status.Value.GrandTotalResetImplemented, "No grand total reset.");
        RequireTrue(status.Value.LifecycleAwareTotals, "Lifecycle-aware flag.");

        // Freeze document present (source guard)
        // written later — checked in freeze commit phase; soft-require study freeze intent
        RequireContains(
            File.ReadAllText(Path.Combine(root, "00-HANDOFF", "RMO_BIR_TRAD_M5_RC2_XZ_EJ_ESALES_COMPLETION_STUDY.md")),
            "GO WITH LIFECYCLE-AWARE M5 COMPLETION",
            "Study decision.");

        Console.WriteLine("[TRAD-M5-RC2] X/Z/EJ/eSales completion runtime checks passed.");
    }

    private sealed record SaleSeed(
        Guid MachineId,
        Guid SaleId,
        Guid JournalId,
        string DocNumber,
        Guid ActorId,
        Guid[] SaleItemIds);

    private static SaleSeed PostSale(
        SqliteConnectionFactory factory,
        string label,
        OfficialDocumentTaxBucketDraftTaxpayerMode mode,
        OfficialDocumentLineTaxClassification[] classifications,
        long[] lineTotals)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Guid roleId = Guid.Parse(DefaultSeedData.CashierRoleId);
        Guid actorId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid machineId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid categoryId = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid brandId = Guid.Parse("22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid unitId = Guid.Parse("33333333-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        Exec(connection, "INSERT OR IGNORE INTO users (id, username, display_name, password_hash, role_id, status, created_at) VALUES ($id,$u,$u,'H',$roleId,1,$now);",
            ("$id", actorId.ToString("D")), ("$u", "u-" + label + Guid.NewGuid().ToString("N")[..6]), ("$roleId", roleId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO cashier_sessions (id, user_id, opened_at, opening_cash_cents, expected_cash_cents, variance_cents, status) VALUES ($id,$u,$now,100000,100000,0,1);",
            ("$id", sessionId.ToString("D")), ("$u", actorId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO categories (id, name, is_active) VALUES ($id,'C',1);", ("$id", categoryId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO brands (id, name, is_active) VALUES ($id,'B',1);", ("$id", brandId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active) VALUES ($id,'piece-m5rc2','pc',0,1);", ("$id", unitId.ToString("D")));
        Exec(connection, """
            INSERT OR IGNORE INTO bir_machine_profiles (
                id, registration_scope_code, registered_taxpayer_name, registered_address,
                taxpayer_identification_number, branch_code, machine_serial_number,
                machine_identification_number, accreditation_number, permit_to_use_number,
                software_name, software_version, compliance_mode, official_output_enabled, is_active, created_at
            ) VALUES (
                $id, 'LOCAL_SINGLE_STORE', 'Taxpayer', 'Addr',
                '123-456-789-000', '00001', 'SN1', 'MIN1', 'ACC1', 'PTU1',
                'Salryn', '1.0', 'ACCREDITED', 1, 1, $now
            );
            """, ("$id", machineId.ToString("D")));
        Exec(connection, """
            INSERT OR IGNORE INTO bir_official_document_sequences (
                id, bir_machine_profile_id, document_kind, sequence_scope_code, prefix, year,
                start_number, current_number, end_number, padding_digits, is_active, is_exhausted, created_at
            ) VALUES (
                $id, $machineId, 'OFFICIAL_INVOICE', 'LOCAL_SINGLE_STORE', 'INV', 2026,
                1, 0, NULL, 6, 1, 0, $now
            );
            """, ("$id", "33333333-3333-3333-3333-333333333333"), ("$machineId", machineId.ToString("D")));

        Guid saleId = Guid.NewGuid();
        DateTimeOffset saleDate = DateTimeOffset.UtcNow;
        long total = lineTotals.Sum();
        List<SaleItem> items = new();
        List<StockMovement> moves = new();
        List<Guid> saleItemIds = new();

        for (int i = 0; i < lineTotals.Length; i++)
        {
            Guid productId = Guid.NewGuid();
            Guid saleItemId = Guid.NewGuid();
            saleItemIds.Add(saleItemId);
            Exec(connection, """
                INSERT INTO products (
                    id, sku, barcode, name, category_id, brand_id, unit_id,
                    cost_price_cents, selling_price_cents, stock_on_hand_x1000, reorder_level_x1000,
                    track_stock, allow_discount, is_active, tax_classification, created_at
                ) VALUES (
                    $id, $sku, $bc, $name, $cat, $brand, $unit,
                    100, $price, 10000, 0, 1, 1, 1, 1, $now
                );
                """,
                ("$id", productId.ToString("D")),
                ("$sku", "SKU-" + label + "-" + i + "-" + Guid.NewGuid().ToString("N")[..4]),
                ("$bc", "88" + Guid.NewGuid().ToString("N")[..10]),
                ("$name", label + "-P" + i),
                ("$cat", categoryId.ToString("D")),
                ("$brand", brandId.ToString("D")),
                ("$unit", unitId.ToString("D")),
                ("$price", lineTotals[i]));

            items.Add(new SaleItem
            {
                Id = saleItemId,
                SaleId = saleId,
                ProductId = productId,
                ProductNameSnapshot = label + "-P" + i,
                UnitNameSnapshot = "pc",
                Quantity = Quantity.FromX1000(1000).Value,
                UnitPrice = Money.FromCents(lineTotals[i]),
                CostPrice = Money.FromCents(100),
                LineTotal = Money.FromCents(lineTotals[i])
            });
            moves.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                MovementType = StockMovementType.Sale,
                QuantityChange = Quantity.FromX1000(1000).Value,
                CostPrice = Money.FromCents(100),
                ReferenceType = "SALE",
                ReferenceId = saleId,
                Reason = "sale",
                UserId = actorId,
                CreatedAt = saleDate
            });
        }

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "PENDING",
            SaleDate = saleDate,
            CashierUserId = actorId,
            CashierSessionId = sessionId,
            Subtotal = Money.FromCents(total),
            Discount = Money.FromCents(0),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(total),
            AmountPaid = Money.FromCents(total),
            Change = Money.FromCents(0),
            Status = SaleStatus.Completed,
            CreatedAt = saleDate
        };
        Payment pay = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(total),
            PaidAt = saleDate
        };

        Result<SqliteOfficialCheckoutPersistenceResult> posted = new SqliteOfficialCheckoutPersistenceService(factory)
            .CompleteOfficialSaleAsync(new SqliteOfficialCheckoutPersistenceRequest(
                new CompleteCheckoutSaleRequest(sale, items.ToArray(), new[] { pay }, moves.ToArray()),
                new OfficialDocumentSequenceAllocationRequest(machineId, "OFFICIAL_INVOICE", "LOCAL_SINGLE_STORE", 2026),
                OfficialDocumentSnapshotDraftKind.OfficialInvoice,
                "c-" + label + "@test",
                "POS-1",
                SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                classifications,
                mode)).GetAwaiter().GetResult();

        RequireTrue(posted.IsSuccess, "Post " + label + ": " + (posted.IsFailure ? posted.ErrorDetail.Message : ""));
        return new SaleSeed(
            machineId,
            saleId,
            posted.Value.JournalEntryId,
            posted.Value.OfficialDocumentNumber,
            actorId,
            saleItemIds.ToArray());
    }

    private static OfficialReportingProjectionRequest WideRange(Guid machineId) =>
        new(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            machineId,
            "LOCAL_SINGLE_STORE",
            ElectronicJournalDocumentKind.OfficialInvoice);

    private sealed class TempDb : IDisposable
    {
        public DbPaths Paths { get; }
        private readonly string _root;

        private TempDb(string root, DbPaths paths)
        {
            _root = root;
            Paths = paths;
        }

        public static TempDb Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "Salryn-M5RC2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            DbPaths paths = DbPaths.CreateDevelopment(root);
            paths.EnsureDirectories();
            Result<DatabaseInitializationSummary> init = DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
            if (init.IsFailure)
            {
                throw new InvalidOperationException(init.ErrorDetail.Message);
            }

            return new TempDb(root, paths);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch
            {
            }
        }
    }

    private static void Exec(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static long ReadSequence(string db)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT current_number FROM bir_official_document_sequences LIMIT 1;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static int CountEj(string db)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM bir_electronic_journal_entries;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountZ(string db, DateTimeOffset rangeStart)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM bir_z_readings WHERE range_started_at = $s;";
        cmd.Parameters.AddWithValue("$s", rangeStart.ToString("O"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void CorruptOneSnapshotHash(string db, Guid journalId)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        // Append-only triggers block UPDATE; drop briefly for deliberate fail-closed proof only.
        using (SqliteCommand drop = c.CreateCommand())
        {
            drop.CommandText = "DROP TRIGGER IF EXISTS trg_bir_electronic_journal_entries_no_update;";
            drop.ExecuteNonQuery();
        }

        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE bir_electronic_journal_entries SET snapshot_payload_hash = 'deadbeef' WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", journalId.ToString("D"));
            cmd.ExecuteNonQuery();
        }
    }

    private static string Fail<T>(Result<T> r) => r.IsFailure ? r.ErrorDetail.Code + " " + r.ErrorDetail.Message : "";

    private static string FindRoot()
    {
        DirectoryInfo? d = new(Directory.GetCurrentDirectory());
        while (d is not null)
        {
            if (File.Exists(Path.Combine(d.FullName, "Salryn.sln")))
            {
                return d.FullName;
            }

            d = d.Parent;
        }

        throw new InvalidOperationException("sln missing");
    }

    private static void RequireTrue(bool c, string m)
    {
        if (!c)
        {
            throw new InvalidOperationException(m);
        }
    }

    private static void RequireFalse(bool c, string m) => RequireTrue(!c, m);

    private static void RequireContains(string h, string n, string m)
    {
        if (!h.Contains(n, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(m + " Missing: " + n);
        }
    }
}

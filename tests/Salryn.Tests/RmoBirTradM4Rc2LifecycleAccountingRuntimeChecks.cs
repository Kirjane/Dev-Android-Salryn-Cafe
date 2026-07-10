using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Core.Services.Lifecycle;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// M4-RC2: tax-aware lifecycle accounting runtime matrix (VAT/non-VAT, lines, failure injection).
/// </summary>
public static class RmoBirTradM4Rc2LifecycleAccountingRuntimeChecks
{
    public static void Run()
    {
        string root = FindRoot();
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "RMO_BIR_TRAD_M4_RC2_GO_STOP_DECISION.md")), "GO decision required.");
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "RMO_BIR_TRAD_M4_RC2_LIFECYCLE_ACCOUNTING_STUDY.md")), "Study required.");
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "rmo-bir-trad-document-lifecycle-accounting-runtime", "RC2 runtime gate required.");
        RequireContains(coreSmoke, "RmoBirTradM4Rc2LifecycleAccountingRuntimeChecks.Run()", "RC2 checks wired.");

        string service = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteLifecycleAccountingService.cs"));
        RequireFalse(service.Contains("refundCents / originalTotalCents", StringComparison.Ordinal), "Ratio stock return must be gone.");
        RequireContains(service, "state_version = $expected", "Optimistic concurrency required.");

        RunVatMultiLineMatrix();
        RunNonVatSsptMatrix();
        RunFailureInjectionRollback();
        RunDraftCancelBoundary();
        RunAmountOnlyRefundRejected();
        RunSnapshotV3Posted();

        Console.WriteLine("[TRAD-M4-RC2] Lifecycle accounting runtime checks passed.");
    }

    private static void RunVatMultiLineMatrix()
    {
        using TempDb db = TempDb.Create();
        var (svc, items) = PostTwoLineSale(
            db,
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable, OfficialDocumentLineTaxClassification.VatExempt },
            new[] { 11200L, 5000L });

        // Refund only line 0 qty half — must not touch product of line 1.
        long stock0Before = ReadStock(db.Paths.DatabasePath, items[0].ProductId);
        long stock1Before = ReadStock(db.Paths.DatabasePath, items[1].ProductId);

        Result<LifecycleAccountingResult> refund = svc.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            items[0].JournalId,
            items[0].DocNumber,
            items[0].SaleId,
            "Return VATable half",
            items[0].ActorId,
            "POS-1",
            "rc2-vat-partial",
            new[]
            {
                new LifecycleRefundLineRequest(items[0].SaleItemId, 500, StockReturnDisposition.Restock)
            },
            new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash))).GetAwaiter().GetResult();

        RequireTrue(refund.IsSuccess, "VAT partial: " + (refund.IsFailure ? refund.ErrorDetail.Message : ""));
        RequireTrue(refund.Value.TaxReversal is not null, "Tax reversal required.");
        RequireTrue(refund.Value.TaxReversal!.VatAmountCents > 0, "VAT amount reversed.");
        RequireTrue(refund.Value.AdjustmentDocumentNumber is not null, "Internal adjustment number.");
        RequireTrue(refund.Value.AdjustmentSnapshotHash is not null, "Adjustment hash.");
        RequireTrue(ReadStock(db.Paths.DatabasePath, items[0].ProductId) > stock0Before, "Line0 restocked.");
        RequireTrue(ReadStock(db.Paths.DatabasePath, items[1].ProductId) == stock1Before, "Line1 stock unchanged.");

        // Damaged disposition on exempt line — no sellable stock increase.
        long stock1Mid = ReadStock(db.Paths.DatabasePath, items[1].ProductId);
        Result<LifecycleAccountingResult> damaged = svc.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            items[0].JournalId,
            items[0].DocNumber,
            items[0].SaleId,
            "Damaged exempt return",
            items[0].ActorId,
            null,
            "rc2-vat-damaged",
            new[]
            {
                new LifecycleRefundLineRequest(items[1].SaleItemId, 1000, StockReturnDisposition.DamagedNonRestockable)
            },
            new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash))).GetAwaiter().GetResult();
        RequireTrue(damaged.IsSuccess, "Damaged: " + (damaged.IsFailure ? damaged.ErrorDetail.Message : ""));
        RequireTrue(ReadStock(db.Paths.DatabasePath, items[1].ProductId) == stock1Mid, "Damaged does not restock.");
    }

    private static void RunNonVatSsptMatrix()
    {
        using TempDb db = TempDb.Create();
        var (svc, items) = PostTwoLineSale(
            db,
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            new[] { OfficialDocumentLineTaxClassification.NonVat, OfficialDocumentLineTaxClassification.NonVatExempt },
            new[] { 10000L, 3000L });

        Result<LifecycleAccountingResult> refund = svc.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            items[0].JournalId,
            items[0].DocNumber,
            items[0].SaleId,
            "SSPT return",
            items[0].ActorId,
            null,
            "rc2-sspt",
            new[]
            {
                new LifecycleRefundLineRequest(items[0].SaleItemId, 1000, StockReturnDisposition.Restock)
            },
            new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash))).GetAwaiter().GetResult();
        RequireTrue(refund.IsSuccess, "SSPT refund: " + (refund.IsFailure ? refund.ErrorDetail.Message : ""));
        RequireTrue(refund.Value.TaxReversal!.SsptCents > 0, "SSPT reversal.");
        RequireTrue(refund.Value.TaxReversal.VatAmountCents == 0, "No VAT on non-VAT mode.");
    }

    private static void RunFailureInjectionRollback()
    {
        using TempDb db = TempDb.Create();
        var (svc, items) = PostTwoLineSale(
            db,
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            new[] { 11200L });

        long stockBefore = ReadStock(db.Paths.DatabasePath, items[0].ProductId);
        int eventsBefore = CountEvents(db.Paths.DatabasePath, items[0].JournalId, "REFUND");

        Result<LifecycleAccountingResult> fail = svc.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            items[0].JournalId,
            items[0].DocNumber,
            items[0].SaleId,
            "Inject fail",
            items[0].ActorId,
            null,
            "rc2-fail",
            new[] { new LifecycleRefundLineRequest(items[0].SaleItemId, 500, StockReturnDisposition.Restock) },
            new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash),
            FailureInjectionStage: "after_stock")).GetAwaiter().GetResult();

        RequireTrue(fail.IsFailure, "Injected failure must fail.");
        RequireTrue(ReadStock(db.Paths.DatabasePath, items[0].ProductId) == stockBefore, "Stock rolled back.");
        RequireTrue(CountEvents(db.Paths.DatabasePath, items[0].JournalId, "REFUND") == eventsBefore, "EJ rolled back.");
        RequireTrue(CountSettlements(db.Paths.DatabasePath) == 0, "Settlement rolled back.");
    }

    private static void RunDraftCancelBoundary()
    {
        using TempDb db = TempDb.Create();
        SqliteConnectionFactory factory = new(db.Paths);
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        Guid roleId = Guid.Parse(DefaultSeedData.CashierRoleId);
        Guid actorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid saleId = Guid.NewGuid();
        string now = DateTimeOffset.UtcNow.ToString("O");
        Exec(connection, "INSERT OR IGNORE INTO users (id, username, display_name, password_hash, role_id, status, created_at) VALUES ($id,'d','d','H',$roleId,1,$now);",
            ("$id", actorId.ToString("D")), ("$roleId", roleId.ToString("D")));
        Exec(connection, """
            INSERT INTO sales (id, receipt_no, sale_date, cashier_user_id, subtotal_cents, discount_cents, tax_cents, total_cents, amount_paid_cents, change_cents, status, created_at)
            VALUES ($id, 'DRAFT-1', $now, $actor, 0, 0, 0, 0, 0, 0, $draft, $now);
            """,
            ("$id", saleId.ToString("D")),
            ("$actor", actorId.ToString("D")),
            ("$draft", (int)SaleStatus.Draft));

        SqliteLifecycleAccountingService svc = new(factory);
        Result<LifecycleAccountingResult> cancel = svc.CancelDraftSaleAsync(saleId, actorId, "abandon", "draft-1")
            .GetAwaiter().GetResult();
        RequireTrue(cancel.IsSuccess, "Draft cancel: " + (cancel.IsFailure ? cancel.ErrorDetail.Message : ""));
        RequireTrue(cancel.Value.LifecycleState == "CancelledBeforeCommit", "CancelledBeforeCommit.");

        // Post-commit cancel rejected via action.
        var (svc2, items) = PostTwoLineSale(
            db,
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            new[] { 11200L },
            label: "pc");
        Result<LifecycleAccountingResult> postCancel = svc2.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Cancel,
            items[0].JournalId,
            items[0].DocNumber,
            items[0].SaleId,
            "nope",
            items[0].ActorId,
            null,
            "post-cancel")).GetAwaiter().GetResult();
        RequireTrue(postCancel.IsFailure, "Post-commit cancel must fail.");
        RequireTrue(postCancel.ErrorDetail.Code == "DocumentLifecycle.PostCommitCancelForbidden", "Post-commit cancel code.");
    }

    private static void RunAmountOnlyRefundRejected()
    {
        using TempDb db = TempDb.Create();
        var (svc, items) = PostTwoLineSale(
            db,
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            new[] { 11200L },
            label: "amt");

        Result<LifecycleAccountingResult> amountOnly = svc.ExecuteAsync(new LifecycleAccountingCommand(
            OfficialDocumentLifecycleAction.Refund,
            items[0].JournalId,
            items[0].DocNumber,
            items[0].SaleId,
            "amount only",
            items[0].ActorId,
            null,
            "amt-only",
            RefundLines: null,
            ExpectedTotalRefundCents: 100)).GetAwaiter().GetResult();
        RequireTrue(amountOnly.IsFailure, "Amount-only refund rejected.");
        RequireTrue(amountOnly.ErrorDetail.Code == "DocumentLifecycle.RefundLinesRequired", "RefundLinesRequired.");
    }

    private static void RunSnapshotV3Posted()
    {
        using TempDb db = TempDb.Create();
        var (_, items) = PostTwoLineSale(
            db,
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[] { OfficialDocumentLineTaxClassification.Vatable },
            new[] { 11200L },
            label: "v3");
        string payload = ReadSnapshot(db.Paths.DatabasePath, items[0].JournalId);
        RequireContains(payload, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V3", "V3 schema required for tax-engine posts.");
        RequireContains(payload, "taxClassification", "Line tax classification frozen.");
        RequireContains(payload, "saleItemId", "Sale item id frozen.");
    }

    private sealed record LineSeed(Guid SaleId, Guid SaleItemId, Guid ProductId, Guid JournalId, string DocNumber, Guid ActorId);

    private static (SqliteLifecycleAccountingService Svc, LineSeed[] Items) PostTwoLineSale(
        TempDb db,
        OfficialDocumentTaxBucketDraftTaxpayerMode mode,
        OfficialDocumentLineTaxClassification[] classifications,
        long[] lineTotals,
        string label = "main")
    {
        SqliteConnectionFactory factory = new(db.Paths);
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Guid roleId = Guid.Parse(DefaultSeedData.CashierRoleId);
        Guid actorId = Guid.Parse(label == "main" ? "44444444-4444-4444-4444-444444444444" : Guid.NewGuid().ToString("D"));
        Guid sessionId = Guid.NewGuid();
        Guid machineId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid categoryId = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid brandId = Guid.Parse("22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid unitId = Guid.Parse("33333333-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        Exec(connection, "INSERT OR IGNORE INTO users (id, username, display_name, password_hash, role_id, status, created_at) VALUES ($id,$u,$u,'H',$roleId,1,$now);",
            ("$id", actorId.ToString("D")), ("$u", "c-" + label), ("$roleId", roleId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO cashier_sessions (id, user_id, opened_at, opening_cash_cents, expected_cash_cents, variance_cents, status) VALUES ($id,$u,$now,100000,100000,0,1);",
            ("$id", sessionId.ToString("D")), ("$u", actorId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO categories (id, name, is_active) VALUES ($id,'C',1);", ("$id", categoryId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO brands (id, name, is_active) VALUES ($id,'B',1);", ("$id", brandId.ToString("D")));
        Exec(connection, "INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active) VALUES ($id,'piece-rc2','pc',0,1);", ("$id", unitId.ToString("D")));
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

        List<SaleItem> saleItems = new();
        List<StockMovement> moves = new();
        List<LineSeed> seeds = new();
        Guid saleId = Guid.NewGuid();
        long total = lineTotals.Sum();
        DateTimeOffset saleDate = DateTimeOffset.UtcNow;

        for (int i = 0; i < lineTotals.Length; i++)
        {
            Guid productId = Guid.NewGuid();
            Guid saleItemId = Guid.NewGuid();
            string sku = "SKU-" + label + "-" + i;
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
                ("$sku", sku),
                ("$bc", "99" + label.GetHashCode().ToString("X") + i),
                ("$name", "P" + i),
                ("$cat", categoryId.ToString("D")),
                ("$brand", brandId.ToString("D")),
                ("$unit", unitId.ToString("D")),
                ("$price", lineTotals[i]));

            saleItems.Add(new SaleItem
            {
                Id = saleItemId,
                SaleId = saleId,
                ProductId = productId,
                ProductNameSnapshot = "P" + i,
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
            seeds.Add(new LineSeed(saleId, saleItemId, productId, Guid.Empty, "", actorId));
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

        SqliteOfficialCheckoutPersistenceService post = new(factory);
        Result<SqliteOfficialCheckoutPersistenceResult> posted = post.CompleteOfficialSaleAsync(
            new SqliteOfficialCheckoutPersistenceRequest(
                new CompleteCheckoutSaleRequest(sale, saleItems.ToArray(), new[] { pay }, moves.ToArray()),
                new OfficialDocumentSequenceAllocationRequest(machineId, "OFFICIAL_INVOICE", "LOCAL_SINGLE_STORE", 2026),
                OfficialDocumentSnapshotDraftKind.OfficialInvoice,
                "c-" + label + "@test",
                "POS-1",
                SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                classifications,
                mode)).GetAwaiter().GetResult();
        RequireTrue(posted.IsSuccess, "Post: " + (posted.IsFailure ? posted.ErrorDetail.Message : ""));

        LineSeed[] withJournal = seeds.Select(s => s with
        {
            JournalId = posted.Value.JournalEntryId,
            DocNumber = posted.Value.OfficialDocumentNumber
        }).ToArray();

        return (new SqliteLifecycleAccountingService(factory), withJournal);
    }

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
            string root = Path.Combine(Path.GetTempPath(), "Salryn-M4RC2-" + Guid.NewGuid().ToString("N"));
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
                // best effort
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

    private static long ReadStock(string db, Guid productId)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId.ToString("D"));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static int CountEvents(string db, Guid journalId, string type)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM bir_electronic_journal_entry_events WHERE journal_entry_id=$j AND event_type=$t;";
        cmd.Parameters.AddWithValue("$j", journalId.ToString("D"));
        cmd.Parameters.AddWithValue("$t", type);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountSettlements(string db)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM official_document_lifecycle_settlements;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string ReadSnapshot(string db, Guid journalId)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT snapshot_payload_json FROM bir_electronic_journal_entries WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", journalId.ToString("D"));
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

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

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
/// M4-RC1: genuine runtime lifecycle proofs on isolated SQLite.
/// </summary>
public static class RmoBirTradM4Rc1LifecycleRuntimeChecks
{
    public static void Run()
    {
        string root = FindRoot();
        RequireTrue(File.Exists(Path.Combine(root, "00-HANDOFF", "RMO_BIR_TRAD_M4_RC1_LIFECYCLE_COMPLETION_STUDY.md")), "Study required.");
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        RequireContains(coreSmoke, "rmo-bir-trad-document-lifecycle-runtime", "Runtime gate required.");
        RequireContains(coreSmoke, "RmoBirTradM4Rc1LifecycleRuntimeChecks.Run()", "Runtime checks wired.");

        string temp = Path.Combine(Path.GetTempPath(), "Salryn-M4RC1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(temp);
            paths.EnsureDirectories();
            Result<DatabaseInitializationSummary> init =
                DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
            RequireTrue(init.IsSuccess, "Init must succeed: " + (init.IsFailure ? init.ErrorDetail.Message : ""));
RequireTrue(ReadMaxVersion(paths.DatabasePath) == 29, "Migration max must be 29.");

            SeedAndPostOfficialSale(paths, out Guid saleId, out string docNumber, out Guid journalId, out Guid actorId, out long stockBefore, out Guid saleItemId);

            SqliteLifecycleAccountingService lifecycle = new(new SqliteConnectionFactory(paths));

            // Missing identity fails
            Result<LifecycleAccountingResult> missing = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Void, null, null, null, "r", actorId, null, "key-miss")).GetAwaiter().GetResult();
            RequireTrue(missing.IsFailure, "Missing identity must fail.");
            RequireTrue(missing.ErrorDetail.Code == "DocumentLifecycle.IdentityRequired", "Identity required code.");

            // Reprint with REPRINT marker in HTML
            Result<LifecycleAccountingResult> reprint = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Reprint,
                    journalId,
                    docNumber,
                    saleId,
                    "Customer copy",
                    actorId,
                    "POS-1",
                    "idem-reprint-1")).GetAwaiter().GetResult();
            RequireTrue(reprint.IsSuccess, "Reprint must succeed: " + (reprint.IsFailure ? reprint.ErrorDetail.Message : ""));
            RequireFalse(reprint.Value.SequenceAdvanced, "Reprint must not advance sequence.");
            RequireTrue(reprint.Value.Output is not null, "Reprint output required.");
            string html = reprint.Value.Output!.Artifacts.First(a => a.Format == InvoiceOutputFormat.Html).Utf8Text;
            RequireContains(html, "REPRINT", "HTML must contain REPRINT.");
            RequireTrue(CountEvents(paths.DatabasePath, journalId, "REPRINT") == 1, "One REPRINT event.");

            // Idempotent reprint
            Result<LifecycleAccountingResult> reprint2 = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Reprint, journalId, docNumber, saleId, "Customer copy", actorId, "POS-1", "idem-reprint-1"))
                .GetAwaiter().GetResult();
            RequireTrue(reprint2.IsSuccess, "Idempotent reprint must succeed.");
            RequireTrue(reprint2.Value.EventId == reprint.Value.EventId, "Same event id.");
            RequireTrue(CountEvents(paths.DatabasePath, journalId, "REPRINT") == 1, "Still one REPRINT event.");
            RequireTrue(reprint2.Value.Output is not null, "Idempotent reprint must rebuild output.");

            // Partial line refund (half qty)
            Result<LifecycleAccountingResult> refund1 = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Refund, journalId, docNumber, saleId, "Partial return", actorId, "POS-1", "idem-ref-1",
                    RefundLines: new[]
                    {
                        new LifecycleRefundLineRequest(saleItemId, 500, StockReturnDisposition.Restock)
                    },
                    Settlement: new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash)))
                .GetAwaiter().GetResult();
            RequireTrue(refund1.IsSuccess, "Partial refund: " + (refund1.IsFailure ? refund1.ErrorDetail.Message : ""));
            RequireTrue(CountEvents(paths.DatabasePath, journalId, "REFUND") == 1, "One REFUND event.");
            long stockMid = ReadStock(paths.DatabasePath, Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
            RequireTrue(stockMid > stockBefore, "Stock partially restored after partial refund.");

            // Over-quantity rejected
            Result<LifecycleAccountingResult> over = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Refund, journalId, docNumber, saleId, "Too much", actorId, null, "idem-over",
                    RefundLines: new[]
                    {
                        new LifecycleRefundLineRequest(saleItemId, 999999, StockReturnDisposition.Restock)
                    }))
                .GetAwaiter().GetResult();
            RequireTrue(over.IsFailure, "Over-quantity must fail.");

            // Complete refund of remaining qty
            Result<LifecycleAccountingResult> refund2 = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Refund, journalId, docNumber, saleId, "Rest", actorId, null, "idem-ref-2",
                    RefundLines: new[]
                    {
                        new LifecycleRefundLineRequest(saleItemId, 500, StockReturnDisposition.Restock)
                    },
                    Settlement: new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash)))
                .GetAwaiter().GetResult();
            RequireTrue(refund2.IsSuccess, "Second refund: " + (refund2.IsFailure ? refund2.ErrorDetail.Message : ""));
            RequireTrue(CountEvents(paths.DatabasePath, journalId, "REFUND") == 2, "Two REFUND events.");

            // Further refund rejected
            Result<LifecycleAccountingResult> again = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Refund, journalId, docNumber, saleId, "x", actorId, null, "idem-ref-3",
                    RefundLines: new[]
                    {
                        new LifecycleRefundLineRequest(saleItemId, 100, StockReturnDisposition.Restock)
                    }))
                .GetAwaiter().GetResult();
            RequireTrue(again.IsFailure, "Post-full refund must fail.");

            // Sequence never advanced by lifecycle actions (still at post-time allocation).
            RequireTrue(ReadSequence(paths.DatabasePath) == 1, "Sequence remains 1 after lifecycle.");

            // Separate void sale path
            SeedAndPostOfficialSale(paths, out Guid sale2, out string doc2, out Guid journal2, out Guid actor2, out long stock2Before, out _, label: "void");
            long sequenceAfterSecondPost = ReadSequence(paths.DatabasePath);
            Result<LifecycleAccountingResult> voided = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Void, journal2, doc2, sale2, "Void mistake", actor2, null, "idem-void-1"))
                .GetAwaiter().GetResult();
            RequireTrue(voided.IsSuccess, "Void must succeed: " + (voided.IsFailure ? voided.ErrorDetail.Message : ""));
            RequireTrue(ReadSaleStatus(paths.DatabasePath, sale2) == (int)SaleStatus.Voided, "Sale voided.");
            RequireTrue(CountEvents(paths.DatabasePath, journal2, "VOID") == 1, "VOID event.");
            RequireTrue(
                ReadStock(paths.DatabasePath, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")) >= stock2Before,
                "Stock restored on void.");
            RequireTrue(ReadSequence(paths.DatabasePath) == sequenceAfterSecondPost, "Void must not advance sequence.");

            // Double void rejected
            Result<LifecycleAccountingResult> void2 = lifecycle.ExecuteAsync(
                new LifecycleAccountingCommand(
                    OfficialDocumentLifecycleAction.Void, journal2, doc2, sale2, "again", actor2, null, "idem-void-2"))
                .GetAwaiter().GetResult();
            RequireTrue(void2.IsFailure, "Double void must fail.");

            Console.WriteLine("[TRAD-M4-RC1] Lifecycle runtime checks passed.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }

    private static void SeedAndPostOfficialSale(
        DbPaths paths,
        out Guid saleId,
        out string docNumber,
        out Guid journalId,
        out Guid actorId,
        out long stockBefore,
        out Guid saleItemId,
        string label = "main")
    {
        SqliteConnectionFactory factory = new(paths);
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        // Use seeded Cashier role from DefaultSeedData (name is unique; custom role ids would FK-fail).
        Guid roleId = Guid.Parse(DefaultSeedData.CashierRoleId);
        actorId = Guid.Parse(label == "main"
            ? "44444444-4444-4444-4444-444444444444"
            : "55555555-5555-5555-5555-555555555555");
        Guid sessionId = Guid.Parse(label == "main"
            ? "cccccccc-cccc-cccc-cccc-cccccccccccc"
            : "dddddddd-dddd-dddd-dddd-dddddddddddd");
        Guid productId = Guid.Parse(label == "main"
            ? "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
            : "ffffffff-ffff-ffff-ffff-ffffffffffff");
        Guid machineId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid categoryId = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid brandId = Guid.Parse("22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid unitId = Guid.Parse("33333333-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        string username = label == "main" ? "cashier-m4rc1" : "cashier-m4rc1-void";
        string sku = label == "main" ? "SKU-M4RC1" : "SKU-M4RC1-VOID";
        string barcode = label == "main" ? "9900000000001" : "9900000000002";
        string seqId = "33333333-3333-3333-3333-333333333333";

        Exec(connection, """
            INSERT OR IGNORE INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ($id, $username, $display, 'HASH', $roleId, 1, $now);
            """,
            ("$id", actorId.ToString("D")),
            ("$username", username),
            ("$display", username),
            ("$roleId", roleId.ToString("D")));

        Exec(connection, """
            INSERT OR IGNORE INTO cashier_sessions (
                id, user_id, opened_at, opening_cash_cents, expected_cash_cents, variance_cents, status)
            VALUES ($id, $userId, $now, 0, 0, 0, 1);
            """,
            ("$id", sessionId.ToString("D")),
            ("$userId", actorId.ToString("D")));

        Exec(connection, """
            INSERT OR IGNORE INTO categories (id, name, is_active)
            VALUES ($id, 'M4RC1 Category', 1);
            """,
            ("$id", categoryId.ToString("D")));

        Exec(connection, """
            INSERT OR IGNORE INTO brands (id, name, is_active)
            VALUES ($id, 'M4RC1 Brand', 1);
            """,
            ("$id", brandId.ToString("D")));

        Exec(connection, """
            INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ($id, 'piece-m4rc1', 'pc', 0, 1);
            """,
            ("$id", unitId.ToString("D")));

        // Never INSERT OR REPLACE parent BIR rows after EJ/sales exist — that is DELETE+INSERT and breaks FKs.
        Exec(connection, """
            INSERT OR IGNORE INTO products (
                id, sku, barcode, name, category_id, brand_id, unit_id,
                cost_price_cents, selling_price_cents, stock_on_hand_x1000, reorder_level_x1000,
                track_stock, allow_discount, is_active, tax_classification, created_at
            ) VALUES (
                $id, $sku, $barcode, 'M4RC1 Product', $categoryId, $brandId, $unitId,
                100, 11200, 10000, 0,
                1, 1, 1, 1, $now
            );
            """,
            ("$id", productId.ToString("D")),
            ("$sku", sku),
            ("$barcode", barcode),
            ("$categoryId", categoryId.ToString("D")),
            ("$brandId", brandId.ToString("D")),
            ("$unitId", unitId.ToString("D")));

        Exec(connection, """
            UPDATE products
            SET stock_on_hand_x1000 = 10000,
                is_active = 1,
                tax_classification = 1
            WHERE id = $id;
            """,
            ("$id", productId.ToString("D")));

        if (label == "main")
        {
            Exec(connection, """
                INSERT OR IGNORE INTO bir_machine_profiles (
                    id, registration_scope_code, registered_taxpayer_name, registered_address,
                    taxpayer_identification_number, branch_code, machine_serial_number,
                    machine_identification_number, accreditation_number, permit_to_use_number,
                    software_name, software_version, compliance_mode, official_output_enabled, is_active,
                    created_at
                ) VALUES (
                    $id, 'LOCAL_SINGLE_STORE', 'Taxpayer', 'Addr',
                    '123-456-789-000', '00001', 'SN1',
                    'MIN1', 'ACC1', 'PTU1',
                    'Salryn', '1.0', 'ACCREDITED', 1, 1,
                    $now
                );
                """,
                ("$id", machineId.ToString("D")));

            Exec(connection, """
                INSERT OR IGNORE INTO bir_official_document_sequences (
                    id, bir_machine_profile_id, document_kind, sequence_scope_code, prefix, year,
                    start_number, current_number, end_number, padding_digits, is_active, is_exhausted, created_at
                ) VALUES (
                    $id, $machineId, 'OFFICIAL_INVOICE', 'LOCAL_SINGLE_STORE', 'INV', 2026,
                    1, 0, NULL, 6, 1, 0, $now
                );
                """,
                ("$id", seqId),
                ("$machineId", machineId.ToString("D")));
        }

        saleId = Guid.NewGuid();
        saleItemId = Guid.NewGuid();
        DateTimeOffset saleDate = new(2026, 7, 10, 15, 0, 0, TimeSpan.Zero);
        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "PENDING",
            SaleDate = saleDate,
            CashierUserId = actorId,
            CashierSessionId = sessionId,
            Subtotal = Money.FromCents(11200),
            Discount = Money.FromCents(0),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(11200),
            AmountPaid = Money.FromCents(11200),
            Change = Money.FromCents(0),
            Status = SaleStatus.Completed,
            CreatedAt = saleDate
        };
        SaleItem item = new()
        {
            Id = saleItemId,
            SaleId = saleId,
            ProductId = productId,
            ProductNameSnapshot = "Item",
            UnitNameSnapshot = "pc",
            Quantity = Quantity.FromX1000(1000).Value,
            UnitPrice = Money.FromCents(11200),
            CostPrice = Money.FromCents(100),
            LineTotal = Money.FromCents(11200)
        };
        Payment pay = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(11200),
            PaidAt = saleDate
        };
        StockMovement move = new()
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
        };

        SqliteOfficialCheckoutPersistenceService post = new(factory);
        Result<SqliteOfficialCheckoutPersistenceResult> posted = post.CompleteOfficialSaleAsync(
            new SqliteOfficialCheckoutPersistenceRequest(
                new CompleteCheckoutSaleRequest(sale, new[] { item }, new[] { pay }, new[] { move }),
                new OfficialDocumentSequenceAllocationRequest(machineId, "OFFICIAL_INVOICE", "LOCAL_SINGLE_STORE", 2026),
                OfficialDocumentSnapshotDraftKind.OfficialInvoice,
                username + "@test",
                "POS-1",
                SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                new[] { OfficialDocumentLineTaxClassification.Vatable },
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered)).GetAwaiter().GetResult();

        RequireTrue(posted.IsSuccess, "Official post must succeed: " + (posted.IsFailure ? posted.ErrorDetail.Message : ""));
        docNumber = posted.Value.OfficialDocumentNumber;
        journalId = posted.Value.JournalEntryId;
        stockBefore = ReadStock(paths.DatabasePath, productId);
    }

    private static void Exec(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
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

    private static int ReadMaxVersion(string db)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        return Convert.ToInt32(cmd.ExecuteScalar());
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

    private static int ReadSaleStatus(string db, Guid saleId)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT status FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static long ReadSequence(string db)
    {
        using SqliteConnection c = new("Data Source=" + db);
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "SELECT current_number FROM bir_official_document_sequences LIMIT 1;";
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

using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks
{
    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string saleRepositoryText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "Sales", "SqliteSaleWriteRepository.cs");
        string saleParticipantText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SharedSqliteCompletedSaleWriteParticipant.cs");
        string serviceText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialCheckoutPersistenceService.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(saleRepositoryText, "internal static Result WriteCompletedSale", "Sale repository must expose a shared transaction write body.");
        RequireContains(saleRepositoryText, "shared official checkout uses this same write path", "Shared sale write path must explain the RMO reason.");
        RequireContains(saleParticipantText, "ISqliteSharedTransactionParticipant<CompleteCheckoutSaleRequest, SharedSqliteCompletedSaleWriteResult>", "Completed sale participant must use shared transaction contract.");
        RequireContains(serviceText, "SqliteOfficialCheckoutPersistenceService", "Official checkout persistence service must exist.");
        RequireContains(serviceText, "SharedSqliteOfficialDocumentSequenceAllocationParticipant", "Official checkout must allocate sequence inside shared transaction.");
        RequireContains(serviceText, "SharedSqliteCompletedSaleWriteParticipant", "Official checkout must persist sale inside shared transaction.");
        RequireContains(serviceText, "SqliteElectronicJournalAppendParticipant", "Official checkout must append journal inside shared transaction.");
        RequireContains(serviceText, "OfficialDocumentJournalAppendContractBuilder", "Official checkout must build snapshot-backed journal contract.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run official checkout shared transaction smoke.");

        await RunRuntimeSmoke();
    }

    private static async Task RunRuntimeSmoke()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-OfficialCheckout-" + Guid.NewGuid().ToString("N"));

        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SqliteConnectionFactory factory = new(paths);

            MigrationRunner runner = new(
                factory,
                new IDatabaseMigration[]
                {
                    new InitialSchemaMigration(),
                    new BirMachineProfileSchemaMigration(),
                    new BirOfficialDocumentSequenceSchemaMigration(),
                    new BirElectronicJournalSchemaMigration(),
                    new BirElectronicJournalSchemaHardeningMigration(),
                    new AccountsReceivableSchemaMigration(),
                    new CashDrawerReconciliationSchemaMigration()
                });

            runner.RunPendingMigrations();
            SeedDependencies(factory);

            SqliteOfficialCheckoutPersistenceService service = new(factory);

            Result<SqliteOfficialCheckoutPersistenceResult> committed =
                await service.CompleteOfficialSaleAsync(CreateRequest(
                    saleId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    quantityX1000: 1000,
                    totalCents: 10000));

            RequireSuccess(committed, "Official checkout shared transaction should commit.");
            RequireEqual("OR-2026-000005", committed.Value.OfficialDocumentNumber, "Official checkout must use allocated document number.");
            RequireEqual(5L, ReadCurrentSequenceNumber(factory), "Commit must advance sequence once.");
            RequireEqual(1L, CountRows(factory, "sales"), "Commit must insert sale.");
            RequireEqual(1L, CountRows(factory, "sale_items"), "Commit must insert sale item.");
            RequireEqual(1L, CountRows(factory, "payments"), "Commit must insert payment.");
            RequireEqual(1L, CountRows(factory, "stock_movements"), "Commit must insert stock movement.");
            RequireEqual(1L, CountRows(factory, "bir_electronic_journal_entries"), "Commit must insert journal entry.");
            RequireEqual(1L, CountRows(factory, "bir_electronic_journal_entry_events"), "Commit must insert journal event.");
            RequireEqual(9000L, ReadProductStock(factory), "Commit must reduce tracked stock.");
            RequireEqual("OR-2026-000005", ReadSaleReceiptNo(factory, committed.Value.SaleId), "Sale receipt number must match official document number.");
            RequireEqual("OR-2026-000005", ReadJournalDocumentNumber(factory), "Journal document number must match official sequence.");

            Result<SqliteOfficialCheckoutPersistenceResult> rolledBack =
                await service.CompleteOfficialSaleAsync(CreateRequest(
                    saleId: Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
                    quantityX1000: 20000,
                    totalCents: 200000));

            RequireFailureCode(
                rolledBack,
                "Sales.NegativeStockNotAllowed",
                "Rollback request must fail during stock movement.");

            RequireEqual(5L, ReadCurrentSequenceNumber(factory), "Rollback must not advance sequence.");
            RequireEqual(1L, CountRows(factory, "sales"), "Rollback must not leave sale row.");
            RequireEqual(1L, CountRows(factory, "sale_items"), "Rollback must not leave sale item row.");
            RequireEqual(1L, CountRows(factory, "payments"), "Rollback must not leave payment row.");
            RequireEqual(1L, CountRows(factory, "stock_movements"), "Rollback must not leave stock movement row.");
            RequireEqual(1L, CountRows(factory, "bir_electronic_journal_entries"), "Rollback must not leave journal entry.");
            RequireEqual(1L, CountRows(factory, "bir_electronic_journal_entry_events"), "Rollback must not leave journal event.");
            RequireEqual(9000L, ReadProductStock(factory), "Rollback must not change stock.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static SqliteOfficialCheckoutPersistenceRequest CreateRequest(
        Guid saleId,
        long quantityX1000,
        long totalCents)
    {
        DateTimeOffset saleDate = new(2026, 6, 25, 8, 0, 0, TimeSpan.Zero);
        Guid cashierUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid cashierSessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        Guid productId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "PENDING-OFFICIAL",
            SaleDate = saleDate,
            CashierUserId = cashierUserId,
            CashierSessionId = cashierSessionId,
            Subtotal = Money.FromCents(totalCents),
            Discount = Money.FromCents(0),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(totalCents),
            AmountPaid = Money.FromCents(totalCents),
            Change = Money.FromCents(0),
            Status = SaleStatus.Completed,
            CreatedAt = saleDate
        };

        SaleItem item = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = productId,
            ProductNameSnapshot = "Official Smoke Product",
            BarcodeSnapshot = "9912345678901",
            UnitNameSnapshot = "pc",
            Quantity = Quantity.FromX1000(quantityX1000).Value,
            UnitPrice = Money.FromCents(10000),
            CostPrice = Money.FromCents(7000),
            Discount = Money.FromCents(0),
            LineTotal = Money.FromCents(totalCents)
        };

        Payment payment = new()
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(totalCents),
            PaidAt = saleDate.AddMinutes(1)
        };

        StockMovement movement = new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            MovementType = StockMovementType.Sale,
            QuantityChange = Quantity.FromX1000(quantityX1000).Value,
            CostPrice = Money.FromCents(7000),
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "Official checkout shared transaction smoke stock-out",
            UserId = cashierUserId,
            CreatedAt = saleDate.AddMinutes(1)
        };

        return new SqliteOfficialCheckoutPersistenceRequest(
            new CompleteCheckoutSaleRequest(
                sale,
                new[] { item },
                new[] { payment },
                new[] { movement }),
            new OfficialDocumentSequenceAllocationRequest(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "OFFICIAL_RECEIPT",
                "LOCAL_SINGLE_STORE",
                2026),
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            CashierIdentifier: "cashier@test",
            TerminalIdentifier: "POS-1",
            TaxModeCode: SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
            TaxModeNotes: SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
            LineTaxClassifications: new[] { OfficialDocumentLineTaxClassification.Vatable },
            TaxpayerMode: OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered);
    }

    private static void SeedDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        Execute(connection, """
            INSERT INTO roles (id, name, is_system_role, created_at)
            VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Cashier', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ('44444444-4444-4444-4444-444444444444', 'cashier', 'cashier', 'HASH', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 1, $now);
            """);

        Execute(connection, """
            INSERT INTO cashier_sessions (id, user_id, opened_at, opening_cash_cents, expected_cash_cents, variance_cents, status)
            VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc', '44444444-4444-4444-4444-444444444444', $now, 10000, 10000, 0, 1);
            """);

        Execute(connection, """
            INSERT INTO categories (id, name, is_active)
            VALUES ('11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Smoke Category', 1);
            """);

        Execute(connection, """
            INSERT INTO brands (id, name, is_active)
            VALUES ('22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Smoke Brand', 1);
            """);

        Execute(connection, """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ('33333333-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'piece', 'pc', 0, 1);
            """);

        Execute(connection, """
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
                'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
                'OFFICIAL-SMOKE',
                '9912345678901',
                'Official Smoke Product',
                '11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                '22222222-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                '33333333-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                7000,
                10000,
                10000,
                1000,
                1,
                1,
                1,
                $now
            );
            """);

        Execute(connection, """
            INSERT INTO bir_machine_profiles (
                id,
                registered_taxpayer_name,
                registered_address,
                taxpayer_identification_number,
                branch_code,
                machine_identification_number,
                machine_serial_number,
                accreditation_number,
                permit_to_use_number,
                software_version,
                compliance_mode,
                official_output_enabled,
                is_active,
                created_at
            )
            VALUES (
                '11111111-1111-1111-1111-111111111111',
                'Kirch Test Taxpayer',
                'Test Registered Address',
                '000-000-000-000',
                '00000',
                'MIN-OFFICIAL-CHECKOUT',
                'TEST-SERIAL-OFFICIAL-CHECKOUT',
                'ACC-TEST',
                'PTU-TEST',
                'TEST',
                'ACCREDITED',
                1,
                1,
                $now
            );
            """);

        Execute(connection, """
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
                '33333333-3333-3333-3333-333333333333',
                '11111111-1111-1111-1111-111111111111',
                'OFFICIAL_RECEIPT',
                'LOCAL_SINGLE_STORE',
                'OR',
                2026,
                1,
                4,
                99,
                6,
                1,
                0,
                $now
            );
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static long CountRows(SqliteConnectionFactory factory, string tableName)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long ReadCurrentSequenceNumber(SqliteConnectionFactory factory)
    {
        return ReadLong(factory, "SELECT current_number FROM bir_official_document_sequences WHERE id = '33333333-3333-3333-3333-333333333333';");
    }

    private static long ReadProductStock(SqliteConnectionFactory factory)
    {
        return ReadLong(factory, "SELECT stock_on_hand_x1000 FROM products WHERE id = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';");
    }

    private static string ReadSaleReceiptNo(SqliteConnectionFactory factory, Guid saleId)
    {
        return ReadString(factory, $"SELECT receipt_no FROM sales WHERE id = '{saleId:D}';");
    }

    private static string ReadJournalDocumentNumber(SqliteConnectionFactory factory)
    {
        return ReadString(factory, "SELECT official_document_number FROM bir_electronic_journal_entries LIMIT 1;");
    }

    private static long ReadLong(SqliteConnectionFactory factory, string sql)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReadString(SqliteConnectionFactory factory, string sql)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.ErrorDetail.Code} {result.Error}");
        }
    }

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(message);
        }

        if (!string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected error code {expectedCode}, found {result.ErrorDetail.Code}.");
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected {expected}, found {actual}.");
        }
    }
}

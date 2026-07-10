using System.Text.Json;
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

public static class J21Rmo242023OfficialCheckoutReadinessGateChecks
{
    private const string ReadyProfileId = "51515151-5151-5151-5151-515151515151";
    private const string NotReadyProfileId = "52525252-5252-5252-5252-525252525252";
    private const string ReadySequenceId = "61616161-6161-6161-6161-616161616161";
    private const string NotReadySequenceId = "62626262-6262-6262-6262-626262626262";

    public static async Task RunAsync()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialCheckoutReadinessService.cs");
        string readinessText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialCheckoutReadinessService.cs");
        string persistenceText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteOfficialCheckoutPersistenceService.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "IOfficialCheckoutReadinessService", "Official checkout readiness must expose a core service contract.");
        RequireContains(readinessText, "OfficialCheckoutReadiness.MachineProfileNotAccredited", "Readiness must report machine accreditation blockers.");
        RequireContains(readinessText, "OfficialCheckoutReadiness.MultipleActiveSequences", "Readiness must block ambiguous active sequence ranges.");
        RequireContains(readinessText, "MachineIdentificationMissing", "Readiness must require persisted machine identity before official output.");
        RequireContains(persistenceText, "OfficialCheckout.ReadinessBlocked", "Official persistence must fail closed when readiness blocks official output.");
        RequireContains(persistenceText, "trustedMachineProfile", "Official persistence must use trusted persisted machine profile evidence.");
        RequireContains(persistenceText, "not UI/client input", "Official persistence must explain the caller-input spoofing boundary.");
        RequireNotContains(persistenceText, "OfficialDocumentSnapshotDraftMachineProfileInput MachineProfile", "Official persistence request must not accept caller-supplied machine profile evidence.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialCheckoutReadinessGateChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run the official checkout readiness gate smoke.");

        await RunRuntimeSmoke();
    }

    private static async Task RunRuntimeSmoke()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Salryn-J21-OfficialReadiness-" + Guid.NewGuid().ToString("N"));

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
                    new BirElectronicJournalSchemaHardeningMigration()
                });

            runner.RunPendingMigrations();
            SeedCheckoutDependencies(factory);
            SeedOfficialReadinessDependencies(factory);

            SqliteOfficialCheckoutReadinessService readinessService = new(factory);

            Result<OfficialCheckoutReadinessResult> ready =
                await readinessService.CheckAsync(new OfficialCheckoutReadinessRequest(
                    Guid.Parse(ReadyProfileId),
                    "OFFICIAL_RECEIPT",
                    "LOCAL_SINGLE_STORE",
                    2026));

            RequireSuccess(ready, "Ready profile readiness check should complete.");
            RequireTrue(ready.Value.IsReady, "Ready profile should be eligible for official checkout.");
            RequireEqual(Guid.Parse(ReadySequenceId), ready.Value.ActiveSequenceId, "Ready result must point at the active official sequence.");
            RequireEqual("Persisted Kirch Taxpayer", ready.Value.MachineProfile?.RegisteredTaxpayerName, "Readiness must return persisted machine profile evidence.");

            Result<OfficialCheckoutReadinessResult> notReady =
                await readinessService.CheckAsync(new OfficialCheckoutReadinessRequest(
                    Guid.Parse(NotReadyProfileId),
                    "OFFICIAL_RECEIPT",
                    "LOCAL_SINGLE_STORE",
                    2026));

            RequireSuccess(notReady, "Not-ready profile readiness check should still return a diagnostic result.");
            RequireFalse(notReady.Value.IsReady, "Not-ready profile must not be eligible for official checkout.");
            RequireBlock(notReady.Value, "OfficialCheckoutReadiness.MachineProfileNotAccredited");
            RequireBlock(notReady.Value, "OfficialCheckoutReadiness.OfficialOutputDisabled");
            RequireBlock(notReady.Value, "OfficialCheckoutReadiness.MachineIdentificationMissing");
            RequireBlock(notReady.Value, "OfficialCheckoutReadiness.AccreditationNumberMissing");
            RequireBlock(notReady.Value, "OfficialCheckoutReadiness.PermitToUseNumberMissing");

            SqliteOfficialCheckoutPersistenceService officialCheckout = new(factory);

            Result<SqliteOfficialCheckoutPersistenceResult> blocked =
                await officialCheckout.CompleteOfficialSaleAsync(CreateRequest(
                    Guid.Parse(NotReadyProfileId),
                    Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    totalCents: 10000));

            RequireFailureCode(
                blocked,
                "OfficialCheckout.ReadinessBlocked",
                "Official checkout must fail closed when readiness blocks official output.");
            RequireEqual(0L, ReadCurrentSequenceNumber(factory, NotReadySequenceId), "Blocked official checkout must not advance the unsafe sequence.");
            RequireEqual(0L, CountRows(factory, "sales"), "Blocked official checkout must not insert a sale.");
            RequireEqual(0L, CountRows(factory, "bir_electronic_journal_entries"), "Blocked official checkout must not insert a journal entry.");

            Guid saleId = Guid.Parse("88888888-8888-8888-8888-888888888888");
            Result<SqliteOfficialCheckoutPersistenceResult> committed =
                await officialCheckout.CompleteOfficialSaleAsync(CreateRequest(
                    Guid.Parse(ReadyProfileId),
                    saleId,
                    totalCents: 10000));

            RequireSuccess(committed, "Ready official checkout should commit.");
            RequireEqual("OR-2026-000001", committed.Value.OfficialDocumentNumber, "Ready official checkout must allocate the expected official number.");
            RequireEqual(1L, ReadCurrentSequenceNumber(factory, ReadySequenceId), "Ready official checkout must advance the trusted sequence once.");

            string snapshotJson = ReadSnapshotJson(factory, saleId);
            RequireSnapshotHeader(snapshotJson, "registeredTaxpayerName", "Persisted Kirch Taxpayer");
            RequireSnapshotHeader(snapshotJson, "machineIdentificationNumber", "MIN-PERSISTED-001");
            RequireNotContains(snapshotJson, "Spoofed Caller Taxpayer", "Snapshot evidence must not use caller-supplied machine profile text.");
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
        Guid profileId,
        Guid saleId,
        long totalCents)
    {
        DateTimeOffset saleDate = new(2026, 6, 25, 9, 0, 0, TimeSpan.Zero);
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
            ProductNameSnapshot = "Official Readiness Product",
            BarcodeSnapshot = "9912345678999",
            UnitNameSnapshot = "pc",
            Quantity = Quantity.FromX1000(1000).Value,
            UnitPrice = Money.FromCents(totalCents),
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
            QuantityChange = Quantity.FromX1000(1000).Value,
            CostPrice = Money.FromCents(7000),
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "Official checkout readiness gate smoke stock-out",
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
                profileId,
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

    private static void SeedCheckoutDependencies(SqliteConnectionFactory factory)
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
                'OFFICIAL-READINESS',
                '9912345678999',
                'Official Readiness Product',
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
    }

    private static void SeedOfficialReadinessDependencies(SqliteConnectionFactory factory)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        InsertMachineProfile(
            connection,
            ReadyProfileId,
            taxpayerName: "Persisted Kirch Taxpayer",
            businessName: "Persisted Kirch Store",
            machineIdentificationNumber: "MIN-PERSISTED-001",
            accreditationNumber: "ACC-PERSISTED-001",
            permitToUseNumber: "PTU-PERSISTED-001",
            complianceMode: "ACCREDITED",
            officialOutputEnabled: 1,
            isActive: 1);

        InsertMachineProfile(
            connection,
            NotReadyProfileId,
            taxpayerName: "Not Ready Kirch Taxpayer",
            businessName: "Not Ready Kirch Store",
            machineIdentificationNumber: null,
            accreditationNumber: null,
            permitToUseNumber: null,
            complianceMode: "NOT_ACCREDITED",
            officialOutputEnabled: 0,
            isActive: 0);

        InsertSequence(
            connection,
            ReadySequenceId,
            ReadyProfileId,
            currentNumber: 0);

        InsertSequence(
            connection,
            NotReadySequenceId,
            NotReadyProfileId,
            currentNumber: 0);
    }

    private static void InsertMachineProfile(
        SqliteConnection connection,
        string profileId,
        string taxpayerName,
        string businessName,
        string? machineIdentificationNumber,
        string? accreditationNumber,
        string? permitToUseNumber,
        string complianceMode,
        int officialOutputEnabled,
        int isActive)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bir_machine_profiles (
                id,
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
                $taxpayerName,
                $businessName,
                'Persisted Registered Address',
                '000-000-000-000',
                '00000',
                $machineIdentificationNumber,
                'SERIAL-' || $id,
                $accreditationNumber,
                $permitToUseNumber,
                'Salryn',
                'TEST',
                $complianceMode,
                $officialOutputEnabled,
                $isActive,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$taxpayerName", taxpayerName);
        command.Parameters.AddWithValue("$businessName", businessName);
        command.Parameters.AddWithValue("$machineIdentificationNumber", machineIdentificationNumber is null ? DBNull.Value : machineIdentificationNumber);
        command.Parameters.AddWithValue("$accreditationNumber", accreditationNumber is null ? DBNull.Value : accreditationNumber);
        command.Parameters.AddWithValue("$permitToUseNumber", permitToUseNumber is null ? DBNull.Value : permitToUseNumber);
        command.Parameters.AddWithValue("$complianceMode", complianceMode);
        command.Parameters.AddWithValue("$officialOutputEnabled", officialOutputEnabled);
        command.Parameters.AddWithValue("$isActive", isActive);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSequence(
        SqliteConnection connection,
        string sequenceId,
        string profileId,
        long currentNumber)
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
                $currentNumber,
                99,
                6,
                1,
                0,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", sequenceId);
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$currentNumber", currentNumber);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
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

    private static long ReadCurrentSequenceNumber(SqliteConnectionFactory factory, string sequenceId)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_number
            FROM bir_official_document_sequences
            WHERE id = $sequenceId;
            """;
        command.Parameters.AddWithValue("$sequenceId", sequenceId);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReadSnapshotJson(SqliteConnectionFactory factory, Guid saleId)
    {
        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT snapshot_payload_json
            FROM bir_electronic_journal_entries
            WHERE sale_id = $saleId;
            """;
        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void RequireSnapshotHeader(string snapshotJson, string propertyName, string expected)
    {
        using JsonDocument document = JsonDocument.Parse(snapshotJson);
        string actual = document.RootElement
            .GetProperty("header")
            .GetProperty(propertyName)
            .GetString() ?? string.Empty;

        RequireEqual(expected, actual, $"Snapshot header {propertyName} must come from persisted machine profile evidence.");
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

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
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

    private static void RequireBlock(OfficialCheckoutReadinessResult result, string expectedCode)
    {
        if (!result.Blocks.Any(block => string.Equals(block.Code, expectedCode, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Expected readiness block {expectedCode}.");
        }
    }

    private static void RequireTrue(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool value, string message)
    {
        if (value)
        {
            throw new InvalidOperationException(message);
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

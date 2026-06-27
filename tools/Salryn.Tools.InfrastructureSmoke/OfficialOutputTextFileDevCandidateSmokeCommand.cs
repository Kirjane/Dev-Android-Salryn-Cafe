using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

internal static class OfficialOutputTextFileDevCandidateSmokeCommand
{
    private static readonly Guid MachineProfileId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly Guid SequenceId = Guid.Parse("90000000-0000-0000-0000-000000000002");
    private static readonly Guid UnitId = Guid.Parse("90000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("90000000-0000-0000-0000-000000000004");

    private const string RegistrationScopeCode = "DEV-SMOKE";
    private const string DocumentKind = "OFFICIAL_RECEIPT";
    private const string SequencePrefix = "ORDEV";
    private const string ProductBarcode = "949000000101";
    private const string ProductName = "Dev Smoke Official Output Item";
    private const string TerminalId = "DEV-SMOKE-POS";
    private const long UnitPriceCents = 1500;

    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== OFFICIAL OUTPUT TEXT-FILE DEV CANDIDATE SMOKE =====");

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        paths.EnsureDirectories();

        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.Create(paths);

        RequireSuccess(orchestrator.InitializeDatabase(), "DevCandidate.InitializeDatabase");

        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositories = new(connectionFactory);

        OfficialOutputTextFileDevCandidate? existing = FindExistingCandidate(connectionFactory);

        if (existing is not null)
        {
            Console.WriteLine("Existing official evidence-backed candidate found.");
            PrintCandidate(paths, existing);
            return;
        }

        IUserReadRepository userReadRepository = repositories.CreateUserReadRepository();
        ICashierSessionService cashierSessions = new CashierSessionService(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        UserReadModel cashier = ResolveCashier(userReadRepository);
        Guid sessionId = EnsureOpenCashierSession(cashierSessions, cashier.Id);
        Guid profileId = EnsureMachineProfile(connectionFactory);
        Guid unitId = EnsureUnit(connectionFactory);
        Guid productId = EnsureProduct(repositories, connectionFactory, unitId);

        DateTimeOffset saleDate = DateTimeOffset.UtcNow;
        _ = EnsureSequence(connectionFactory, profileId, saleDate.Year);
        CompleteCheckoutSaleRequest checkoutSale = BuildTrustedCheckoutSale(
            repositories,
            cashier.Id,
            sessionId,
            productId,
            saleDate);

        SqliteOfficialCheckoutPersistenceService officialPersistence = new(connectionFactory);

        Result<SqliteOfficialCheckoutPersistenceResult> persistenceResult =
            officialPersistence.CompleteOfficialSaleAsync(
                new SqliteOfficialCheckoutPersistenceRequest(
                    checkoutSale,
                    new OfficialDocumentSequenceAllocationRequest(
                        profileId,
                        DocumentKind,
                        RegistrationScopeCode,
                        saleDate.Year),
                    OfficialDocumentSnapshotDraftKind.OfficialReceipt,
                    CashierIdentifier: cashier.Username,
                    TerminalIdentifier: TerminalId,
                    TaxModeCode: "VAT_BUCKET_ENGINE_DEFERRED",
                    TaxModeNotes: "Dev smoke candidate keeps VAT bucket engine deferred."),
                CancellationToken.None).GetAwaiter().GetResult();

        RequireSuccess(persistenceResult, "DevCandidate.CompleteOfficialSale");

        OfficialOutputTextFileDevCandidate? created = FindExistingCandidate(connectionFactory);

        if (created is null)
        {
            throw new InvalidOperationException("DevCandidate official persistence succeeded but no official evidence-backed candidate could be read.");
        }

        Console.WriteLine("Created official evidence-backed candidate.");
        PrintCandidate(paths, created);
    }

    private static CompleteCheckoutSaleRequest BuildTrustedCheckoutSale(
        SqliteRepositoryFactory repositories,
        Guid cashierUserId,
        Guid cashierSessionId,
        Guid productId,
        DateTimeOffset saleDate)
    {
        TrustedPosCheckoutSaleBuilder builder = new(repositories.CreateProductReadRepository());
        PosCheckoutApplicationRequest request = new(
            cashierUserId,
            cashierSessionId,
            ReceiptNumber: "DEV-SMOKE-PENDING",
            Lines: new[]
            {
                new PosCheckoutLineInput(
                    productId,
                    ProductNameSnapshot: "Untrusted dev smoke input name",
                    BarcodeSnapshot: "UNTRUSTED",
                    UnitNameSnapshot: "untrusted-unit",
                    QuantityX1000: 1000,
                    UnitPriceCents: 1,
                    CostPriceCents: 1,
                    DiscountCents: 0,
                    TrackStock: false)
            },
            Payments: new[]
            {
                new PosCheckoutPaymentInput(PaymentMethod.Cash, UnitPriceCents)
            },
            SaleDiscountCents: 0,
            TaxCents: 0,
            ReceiptSnapshot: "Dev smoke placeholder; official evidence must come from the official snapshot builder.",
            SaleDate: saleDate);

        return RequireSuccess(
            builder.BuildAsync(request).GetAwaiter().GetResult(),
            "DevCandidate.BuildTrustedCheckoutSale");
    }

    private static UserReadModel ResolveCashier(IUserReadRepository userReadRepository)
    {
        UserReadModel? admin = RequireSuccess(
            userReadRepository.FindByUsernameAsync("admin").GetAwaiter().GetResult(),
            "DevCandidate.FindAdmin");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userReadRepository.GetAllAsync().GetAwaiter().GetResult(),
            "DevCandidate.GetUsers");

        return users.Count == 0
            ? throw new InvalidOperationException("DevCandidate requires an existing development user. Run the existing dev seed/login setup first.")
            : users[0];
    }

    private static Guid EnsureOpenCashierSession(
        ICashierSessionService cashierSessions,
        Guid cashierUserId)
    {
        CashierSessionReadModel? existing = RequireSuccess(
            cashierSessions.FindOpenSessionByUserIdAsync(cashierUserId).GetAwaiter().GetResult(),
            "DevCandidate.FindOpenCashierSession");

        if (existing is not null)
        {
            return existing.Id;
        }

        CashierSessionReadModel opened = RequireSuccess(
            cashierSessions.OpenAsync(new OpenCashierSessionRequest(
                cashierUserId,
                Money.FromCents(10_000),
                "Official output text-file dev candidate smoke session.")).GetAwaiter().GetResult(),
            "DevCandidate.OpenCashierSession");

        return opened.Id;
    }

    private static Guid EnsureMachineProfile(SqliteConnectionFactory connectionFactory)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();

        Guid? existing = ReadGuid(
            connection,
            """
            SELECT id
            FROM bir_machine_profiles
            WHERE registration_scope_code = $scope
              AND is_active = 1
            LIMIT 1;
            """,
            command => command.Parameters.AddWithValue("$scope", RegistrationScopeCode));

        if (existing.HasValue)
        {
            return existing.Value;
        }

        Execute(
            connection,
            """
            INSERT INTO bir_machine_profiles (
                id,
                registration_scope_code,
                registered_taxpayer_name,
                business_name,
                registered_address,
                branch_address,
                taxpayer_identification_number,
                branch_code,
                machine_identification_number,
                machine_serial_number,
                pos_terminal_number,
                accreditation_number,
                permit_to_use_number,
                software_name,
                software_version,
                software_build,
                compliance_mode,
                official_output_enabled,
                is_active,
                created_at
            )
            VALUES (
                $id,
                $scope,
                'Dev Smoke Official Taxpayer',
                'Dev Smoke Official Store',
                'Dev Smoke Registered Address',
                'Dev Smoke Branch Address',
                '000-DEV-SMOKE',
                '00001',
                'MIN-DEV-SMOKE',
                'SERIAL-DEV-SMOKE',
                $terminal,
                'ACC-DEV-SMOKE',
                'PTU-DEV-SMOKE',
                'Salryn',
                'DEV',
                'DEV-SMOKE',
                'ACCREDITED',
                1,
                1,
                $now
            );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", MachineProfileId.ToString("D"));
                command.Parameters.AddWithValue("$scope", RegistrationScopeCode);
                command.Parameters.AddWithValue("$terminal", TerminalId);
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            });

        return MachineProfileId;
    }

    private static Guid EnsureUnit(SqliteConnectionFactory connectionFactory)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();

        Guid? existing = ReadGuid(
            connection,
            "SELECT id FROM units WHERE name = $name LIMIT 1;",
            command => command.Parameters.AddWithValue("$name", "dev-smoke-piece"));

        if (existing.HasValue)
        {
            return existing.Value;
        }

        Execute(
            connection,
            """
            INSERT INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
            VALUES ($id, 'dev-smoke-piece', 'dsp', 0, 1);
            """,
            command => command.Parameters.AddWithValue("$id", UnitId.ToString("D")));

        return UnitId;
    }

    private static Guid EnsureProduct(
        SqliteRepositoryFactory repositories,
        SqliteConnectionFactory connectionFactory,
        Guid unitId)
    {
        using (SqliteConnection connection = connectionFactory.CreateConnection())
        {
            connection.Open();

            Guid? existing = ReadGuid(
                connection,
                "SELECT id FROM products WHERE barcode = $barcode AND is_active = 1 LIMIT 1;",
                command => command.Parameters.AddWithValue("$barcode", ProductBarcode));

            if (existing.HasValue)
            {
                return existing.Value;
            }
        }

        IProductWriteRepository productWrite = repositories.CreateProductWriteRepository();

        Product product = new()
        {
            Id = ProductId,
            Sku = "DEV-SMOKE-OFFICIAL-OUTPUT",
            Barcode = RequireSuccess(Barcode.Create(ProductBarcode), "DevCandidate.ProductBarcode"),
            Name = ProductName,
            Description = "Dev-only official output text-file candidate product.",
            UnitId = unitId,
            CostPrice = Money.FromCents(900),
            SellingPrice = Money.FromCents(UnitPriceCents),
            StockOnHand = RequireSuccess(Quantity.FromX1000(100_000), "DevCandidate.ProductStock"),
            ReorderLevel = RequireSuccess(Quantity.FromX1000(1_000), "DevCandidate.ProductReorderLevel"),
            TrackStock = false,
            AllowDiscount = true,
            IsActive = true,
            Notes = "Created by official-output-text-file-dev-candidate smoke command."
        };

        RequireSuccess(
            productWrite.AddAsync(product).GetAwaiter().GetResult(),
            "DevCandidate.CreateProduct");

        return ProductId;
    }

    private static Guid EnsureSequence(
        SqliteConnectionFactory connectionFactory,
        Guid profileId,
        int year)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();

        Guid? existing = ReadGuid(
            connection,
            """
            SELECT id
            FROM bir_official_document_sequences
            WHERE bir_machine_profile_id = $profileId
              AND document_kind = $documentKind
              AND sequence_scope_code = $scope
              AND year = $year
              AND is_active = 1
              AND is_exhausted = 0
            LIMIT 1;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
                command.Parameters.AddWithValue("$documentKind", DocumentKind);
                command.Parameters.AddWithValue("$scope", RegistrationScopeCode);
                command.Parameters.AddWithValue("$year", year);
            });

        if (existing.HasValue)
        {
            return existing.Value;
        }

        Execute(
            connection,
            """
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
                $documentKind,
                $scope,
                $prefix,
                $year,
                1,
                0,
                999999,
                6,
                1,
                0,
                $now
            );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", SequenceId.ToString("D"));
                command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
                command.Parameters.AddWithValue("$documentKind", DocumentKind);
                command.Parameters.AddWithValue("$scope", RegistrationScopeCode);
                command.Parameters.AddWithValue("$prefix", SequencePrefix);
                command.Parameters.AddWithValue("$year", year);
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            });

        return SequenceId;
    }

    private static OfficialOutputTextFileDevCandidate? FindExistingCandidate(SqliteConnectionFactory connectionFactory)
    {
        using SqliteConnection connection = connectionFactory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                e.id,
                e.sale_id,
                e.bir_machine_profile_id,
                m.registration_scope_code,
                e.document_kind,
                e.official_document_number,
                e.created_at,
                e.terminal_id
            FROM bir_electronic_journal_entries e
            INNER JOIN bir_electronic_journal_entry_events ev
                ON ev.journal_entry_id = e.id
               AND ev.event_type = 'ORIGINAL'
               AND ev.original_marker = 1
            INNER JOIN bir_machine_profiles m
                ON m.id = e.bir_machine_profile_id
            WHERE m.registration_scope_code = $scope
              AND e.document_kind = $documentKind
            ORDER BY e.created_at DESC, e.official_document_number DESC, e.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scope", RegistrationScopeCode);
        command.Parameters.AddWithValue("$documentKind", DocumentKind);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new OfficialOutputTextFileDevCandidate(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static void PrintCandidate(
        DbPaths paths,
        OfficialOutputTextFileDevCandidate candidate)
    {
        Console.WriteLine($"Development database path: {paths.DatabasePath}");
        Console.WriteLine($"Machine profile id: {candidate.BirMachineProfileId:D}");
        Console.WriteLine($"Registration scope: {candidate.RegistrationScopeCode}");
        Console.WriteLine($"Official document number: {candidate.OfficialDocumentNumber}");
        Console.WriteLine($"Document kind: {candidate.DocumentKind}");
        Console.WriteLine($"Journal entry id: {candidate.JournalEntryId:D}");
        Console.WriteLine($"Sale id: {candidate.SaleId:D}");
        Console.WriteLine($"Appended at: {candidate.AppendedAt:O}");
        Console.WriteLine($"Terminal id: {candidate.TerminalId ?? string.Empty}");
        Console.WriteLine("No text-file output was executed.");
        Console.WriteLine();
    }

    private static Guid? ReadGuid(
        SqliteConnection connection,
        string sql,
        Action<SqliteCommand> configure)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);

        object? scalar = command.ExecuteScalar();
        string? value = Convert.ToString(scalar, CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        Action<SqliteCommand> configure)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        command.ExecuteNonQuery();
    }

    private static T RequireSuccess<T>(Result<T> result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.ErrorDetail.Code} {result.Error}");
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result, string label)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{label} failed: {result.ErrorDetail.Code} {result.Error}");
        }
    }

    private sealed record OfficialOutputTextFileDevCandidate(
        Guid JournalEntryId,
        Guid SaleId,
        Guid BirMachineProfileId,
        string RegistrationScopeCode,
        string DocumentKind,
        string OfficialDocumentNumber,
        DateTimeOffset AppendedAt,
        string? TerminalId);
}

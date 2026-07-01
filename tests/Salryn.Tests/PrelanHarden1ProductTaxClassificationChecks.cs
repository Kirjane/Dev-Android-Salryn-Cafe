using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PrelanHarden1ProductTaxClassificationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string boundarySource = Read(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Checkout",
            "OfficialCheckoutCompletionBoundary.cs");
        string factorySource = Read(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Initialization",
            "DefaultDatabaseInitializationFactory.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireNotContains(
            boundarySource,
            ".Select(_ => OfficialDocumentLineTaxClassification.Vatable)",
            "Official checkout must not default every line to VATable.");
        RequireContains(
            boundarySource,
            "trustedSale.LineTaxClassifications",
            "Official checkout must use trusted sale line tax classifications.");
        RequireContains(factorySource, "new ProductTaxClassificationSchemaMigration()", "Database initialization must register product tax classification migration.");
        RequireContains(coreSmoke, "PrelanHarden1ProductTaxClassificationChecks.Run();", "CoreSmoke must run PRELAN-HARDEN-1 product tax classification checks.");

        RequireMigrationDefaultsExistingProducts();
        RequireRepositoryRoundTrip();
        RequireTrustedBuilderUsesProductClassification();
        RequireMixedClassificationOfficialBucketsAndJournalEvidence();
        RequireManualLineFallbackDocumented();
    }

    private static string CreateTempProjectRoot()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "salryn-prelan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectRoot);
        return projectRoot;
    }

    private static void RequireMigrationDefaultsExistingProducts()
    {
        string projectRoot = CreateTempProjectRoot();
        string databasePath = DbPaths.CreateDevelopment(projectRoot).DatabasePath;

        try
        {
            SqliteConnectionFactory connectionFactory = new(DbPaths.CreateDevelopment(projectRoot));
            MigrationRunner runner = new(
                connectionFactory,
                new IDatabaseMigration[]
                {
                    new InitialSchemaMigration(),
                    new BirMachineProfileSchemaMigration(),
                    new BirOfficialDocumentSequenceSchemaMigration(),
                    new BirElectronicJournalSchemaMigration(),
                    new BirElectronicJournalSchemaHardeningMigration(),
                    new BirZReadingSchemaMigration(),
                    new BirElectronicJournalCorrectionEventSchemaMigration(),
                    new ProductTaxClassificationSchemaMigration()
                });

            runner.RunPendingMigrations();

            using SqliteConnection connection = connectionFactory.CreateConnection();
            connection.Open();

            using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(products);";

            bool hasColumn = false;
            using (SqliteDataReader reader = pragma.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "tax_classification", StringComparison.Ordinal))
                    {
                        hasColumn = true;
                        break;
                    }
                }
            }

            if (!hasColumn)
            {
                throw new InvalidOperationException("Migration must add products.tax_classification column.");
            }

            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO products (
                    id, name, unit_id, selling_price_cents, stock_on_hand_x1000, reorder_level_x1000,
                    track_stock, allow_discount, is_active, created_at
                )
                VALUES (
                    'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
                    'Legacy Product',
                    'bbbbbbbb-cccc-dddd-eeee-ffffffff0001',
                    1000,
                    0,
                    0,
                    1,
                    1,
                    1,
                    '2026-01-01T00:00:00.0000000+00:00'
                );
                """;

            SeedUnit(connection);
            insert.ExecuteNonQuery();

            using SqliteCommand read = connection.CreateCommand();
            read.CommandText = "SELECT tax_classification FROM products WHERE id = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';";
            int storedValue = Convert.ToInt32(read.ExecuteScalar());

            if (storedValue != (int)ProductTaxClassification.Vatable)
            {
                throw new InvalidOperationException("Existing products must default to VATable classification.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort temp cleanup only.
                }
            }
        }
    }

    private static void SeedUnit(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO units (id, name, allows_decimal_quantity, is_active)
            VALUES ('bbbbbbbb-cccc-dddd-eeee-ffffffff0001', 'pc', 0, 1);
            """;
        command.ExecuteNonQuery();
    }

    private static void RequireRepositoryRoundTrip()
    {
        string projectRoot = CreateTempProjectRoot();

        try
        {
            DatabaseInitializationOrchestrator orchestrator =
                CreateOrchestrator(projectRoot);

            RequireSuccess(orchestrator.InitializeDatabase(), "Database initialization should succeed.");

            SqliteRepositoryFactory repositories = new(new SqliteConnectionFactory(DbPaths.CreateDevelopment(projectRoot)));
            IProductWriteRepository writes = repositories.CreateProductWriteRepository();
            IProductReadRepository reads = repositories.CreateProductReadRepository();
            ICatalogReadRepository catalogs = repositories.CreateCatalogReadRepository();

            Guid unitId = catalogs.GetUnitsAsync().GetAwaiter().GetResult().Value[0].Id;
            Guid productId = Guid.NewGuid();

            Product product = new()
            {
                Id = productId,
                Name = "VAT-exempt smoke product",
                UnitId = unitId,
                SellingPrice = Money.FromCents(5000),
                TaxClassification = ProductTaxClassification.VatExempt,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            RequireSuccess(writes.AddAsync(product).GetAwaiter().GetResult(), "Product write should succeed.");

            ProductDetailReadModel? detail = reads.FindDetailByIdAsync(productId).GetAwaiter().GetResult().Value;

            if (detail is null || detail.TaxClassification != ProductTaxClassification.VatExempt)
            {
                throw new InvalidOperationException("Product repository must persist and read tax classification.");
            }

            ProductLookupReadModel? lookup = reads.FindByIdAsync(productId).GetAwaiter().GetResult().Value;

            if (lookup is null || lookup.TaxClassification != ProductTaxClassification.VatExempt)
            {
                throw new InvalidOperationException("Product lookup must expose tax classification.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort temp cleanup only.
                }
            }
        }
    }

    private static void RequireTrustedBuilderUsesProductClassification()
    {
        string projectRoot = CreateTempProjectRoot();

        try
        {
            DatabaseInitializationOrchestrator orchestrator =
                CreateOrchestrator(projectRoot);

            RequireSuccess(orchestrator.InitializeDatabase(), "Database initialization should succeed.");

            SqliteRepositoryFactory repositories = new(new SqliteConnectionFactory(DbPaths.CreateDevelopment(projectRoot)));
            IProductWriteRepository writes = repositories.CreateProductWriteRepository();
            IProductReadRepository reads = repositories.CreateProductReadRepository();
            ICatalogReadRepository catalogs = repositories.CreateCatalogReadRepository();

            Guid unitId = catalogs.GetUnitsAsync().GetAwaiter().GetResult().Value[0].Id;
            Guid productId = Guid.NewGuid();

            Product product = new()
            {
                Id = productId,
                Name = "Zero-rated trusted product",
                UnitId = unitId,
                SellingPrice = Money.FromCents(3000),
                TaxClassification = ProductTaxClassification.ZeroRated,
                TrackStock = false,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            RequireSuccess(writes.AddAsync(product).GetAwaiter().GetResult(), "Product write should succeed.");

            TrustedPosCheckoutSaleBuilder builder = new(reads);
            PosCheckoutApplicationRequest request = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "PRELAN-TAX-001",
                new[]
                {
                    new PosCheckoutLineInput(
                        productId,
                        "Untrusted name",
                        null,
                        "untrusted-unit",
                        1000,
                        1,
                        1,
                        0,
                        false,
                        ProductTaxClassification.Vatable)
                },
                new[] { new PosCheckoutPaymentInput(PaymentMethod.Cash, 3000) },
                0,
                0,
                null);

            TrustedPosCheckoutSaleBuildResult buildResult = RequireSuccessValue(
                builder.BuildAsync(request).GetAwaiter().GetResult(),
                "Trusted builder should succeed.");

            if (buildResult.LineTaxClassifications.Count != 1 ||
                buildResult.LineTaxClassifications[0] != OfficialDocumentLineTaxClassification.ZeroRated)
            {
                throw new InvalidOperationException("Trusted builder must use persisted product tax classification, not untrusted line input.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort temp cleanup only.
                }
            }
        }
    }

    private static void RequireMixedClassificationOfficialBucketsAndJournalEvidence()
    {
        OfficialDocumentTaxBucketEngine engine = new();
        OfficialDocumentSnapshotDraftBuilder builder = new();
        OfficialDocumentSnapshotEvidenceBuilder evidenceBuilder = new();

        Sale sale = BuildSale(22500, 0);
        SaleItem vatableItem = BuildItem(sale.Id, 11200);
        SaleItem exemptItem = BuildItem(sale.Id, 5000);
        SaleItem zeroRatedItem = BuildItem(sale.Id, 3000);
        SaleItem nonVatItem = BuildItem(sale.Id, 3300);

        IReadOnlyList<OfficialDocumentLineTaxClassification> classifications = new[]
        {
            OfficialDocumentLineTaxClassification.Vatable,
            OfficialDocumentLineTaxClassification.VatExempt,
            OfficialDocumentLineTaxClassification.ZeroRated,
            OfficialDocumentLineTaxClassification.NonVat
        };

        Result<OfficialDocumentSnapshotDraft> snapshotResult = builder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            BuildProfile(),
            sale,
            new[] { vatableItem, exemptItem, zeroRatedItem, nonVatItem },
            new[] { BuildPayment(sale.Id, 22500) },
            "OR-PRELAN-000001",
            "cashier-1",
            "POS-1",
            SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
            SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
            classifications,
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered));

        RequireSuccess(snapshotResult, "Mixed classification snapshot should build.");

        Result<OfficialDocumentTaxBucketDraft> bucketResult = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable),
                new OfficialDocumentTaxBucketCalculationLine(5000, 0, OfficialDocumentLineTaxClassification.VatExempt),
                new OfficialDocumentTaxBucketCalculationLine(3000, 0, OfficialDocumentLineTaxClassification.ZeroRated),
                new OfficialDocumentTaxBucketCalculationLine(3300, 0, OfficialDocumentLineTaxClassification.NonVat)
            },
            0));

        RequireSuccess(bucketResult, "Mixed classification VAT buckets should compute.");
        RequireEqual(5000L, BucketSales(bucketResult.Value, OfficialDocumentTaxBucketDraftKind.VatExemptSales), "VAT-exempt bucket.");
        RequireEqual(3000L, BucketSales(bucketResult.Value, OfficialDocumentTaxBucketDraftKind.ZeroRatedSales), "Zero-rated bucket.");

        Result<ElectronicJournalSnapshotEvidence> allVatableEvidence =
            evidenceBuilder.Build(snapshotResult.Value with
            {
                TaxBuckets = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
                    OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                    new[] { new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable) },
                    0)).Value
            });

        Result<ElectronicJournalSnapshotEvidence> mixedEvidence = evidenceBuilder.Build(snapshotResult.Value);

        RequireSuccess(allVatableEvidence, "All-VATable evidence should build.");
        RequireSuccess(mixedEvidence, "Mixed evidence should build.");

        if (string.Equals(allVatableEvidence.Value.PayloadHash, mixedEvidence.Value.PayloadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Journal evidence hash must change when line tax classifications change.");
        }

        RequireContains(mixedEvidence.Value.PayloadJson, "\"taxBuckets\"", "Journal evidence must include tax bucket payload.");
    }

    private static void RequireManualLineFallbackDocumented()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string trustedBuilderSource = Read(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "TrustedPosCheckoutSaleBuilder.cs");
        string lineInputSource = Read(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "PosCheckoutApplicationService.cs");

        RequireContains(
            lineInputSource,
            "ProductTaxClassification TaxClassification",
            "Manual/no-repository checkout lines must declare tax classification.");
        RequireContains(
            lineInputSource,
            "ProductTaxClassification.Vatable",
            "Manual/no-repository checkout lines must default explicitly to VATable.");
        RequireContains(
            trustedBuilderSource,
            "FromInput(line)",
            "Trusted builder must document manual line fallback through FromInput.");
        RequireContains(
            trustedBuilderSource,
            "line.TaxClassification",
            "Trusted builder must carry manual line tax classification through FromInput.");
    }

    private static DatabaseInitializationOrchestrator CreateOrchestrator(string projectRoot)
    {
        SqliteConnectionFactory connectionFactory = new(DbPaths.CreateDevelopment(projectRoot));
        MigrationRunner migrationRunner = new(
            connectionFactory,
            new IDatabaseMigration[]
            {
                new InitialSchemaMigration(),
                new BirMachineProfileSchemaMigration(),
                new BirOfficialDocumentSequenceSchemaMigration(),
                new BirElectronicJournalSchemaMigration(),
                new BirElectronicJournalSchemaHardeningMigration(),
                new BirZReadingSchemaMigration(),
                new BirElectronicJournalCorrectionEventSchemaMigration(),
                new ProductTaxClassificationSchemaMigration()
            });

        return new DatabaseInitializationOrchestrator(
            connectionFactory,
            migrationRunner,
            new Salryn.Infrastructure.Data.Seeding.DatabaseSeeder(connectionFactory),
            new Salryn.Infrastructure.Data.Bootstrap.FirstRunBootstrapper(
                connectionFactory,
                new Salryn.Infrastructure.Security.Pbkdf2PasswordHasher()));
    }

    private static Sale BuildSale(long totalCents, long discountCents)
    {
        Guid saleId = Guid.NewGuid();

        return new Sale
        {
            Id = saleId,
            ReceiptNumber = "PRELAN-001",
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = Guid.NewGuid(),
            CashierSessionId = Guid.NewGuid(),
            Subtotal = Money.FromCents(totalCents + discountCents),
            Discount = Money.FromCents(discountCents),
            Tax = Money.Zero,
            Total = Money.FromCents(totalCents),
            AmountPaid = Money.FromCents(totalCents),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static SaleItem BuildItem(Guid saleId, long lineTotalCents)
    {
        return new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "PRELAN item",
            UnitNameSnapshot = "pc",
            Quantity = Quantity.FromUnits(1).Value,
            UnitPrice = Money.FromCents(lineTotalCents),
            Discount = Money.Zero,
            LineTotal = Money.FromCents(lineTotalCents)
        };
    }

    private static Payment BuildPayment(Guid saleId, long amountCents)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(amountCents),
            PaidAt = DateTimeOffset.UtcNow
        };
    }

    private static OfficialDocumentSnapshotDraftMachineProfileInput BuildProfile()
    {
        return new OfficialDocumentSnapshotDraftMachineProfileInput(
            "Test Taxpayer",
            "Test Store",
            "000-000-000-000",
            "00000",
            "Test Address",
            "SERIAL",
            "MIN",
            "ACC",
            "PTU");
    }

    private static long BucketSales(OfficialDocumentTaxBucketDraft draft, OfficialDocumentTaxBucketDraftKind kind)
    {
        return draft.Buckets.First(bucket => bucket.Kind == kind).SalesAmountCents;
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

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }

    private static void RequireNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Unexpected: " + needle);
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + " " + result.Error);
        }
    }

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + " " + result.Error);
        }
    }

    private static T RequireSuccessValue<T>(Result<T> result, string message)
    {
        RequireSuccess(result, message);
        return result.Value;
    }

    private static void RequireEqual(long expected, long actual, string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(message + $" Expected {expected}, got {actual}.");
        }
    }
}

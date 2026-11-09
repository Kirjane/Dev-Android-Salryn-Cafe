using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// TRAD-M1: taxpayer mode + invoice policy spine. Not accreditation. Not live official posting.
/// </summary>
public static class RmoBirTradM1TaxInvoicePolicyChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        AssertDocs(projectRoot);
        AssertSourceInvariants(projectRoot);
        AssertPolicyMatrix();
        AssertTaxEngineFailClosed();
        AssertMigrationAndSetupPersistence();
        Console.WriteLine("[TRAD-M1] Taxpayer mode + invoice policy spine checks passed.");
    }

    private static void AssertDocs(string projectRoot)
    {
        string handoff = Path.Combine(projectRoot, "00-HANDOFF");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M1_TAX_INVOICE_POLICY_STUDY.md")), "Study required.");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M1_GO_STOP_DECISION.md")), "GO/STOP required.");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRAD_M1_OFFICIAL_SOURCE_REGISTER.md")), "Source register required.");
        RequireTrue(File.Exists(Path.Combine(handoff, "RMO_BIR_TRADITIONAL_CRM_POS_PROGRAM_LEDGER_2026-07-10.md")), "Program ledger required.");
    }

    private static void AssertSourceInvariants(string projectRoot)
    {
        string catalog = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));
        string factory = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs"));
        string migration = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Migrations", "VatRegistrationModeSchemaMigration.cs"));
        string setupService = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSetupService.cs"));
        string setupPage = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string dbA2 = File.ReadAllText(Path.Combine(projectRoot, "tests", "Salryn.Tests", "DbA2DatabaseSafetyChecks.cs"));
        string coreSmoke = File.ReadAllText(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string engine = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "OfficialDocumentTaxBucketEngine.cs"));
        string invoicePolicy = File.ReadAllText(Path.Combine(projectRoot, "src", "Salryn.Core", "Services", "SalrynInvoiceDocumentPolicy.cs"));

        RequireContains(catalog, "SupportedMigrationVersion = 24", "Catalog must include the current additive migrations.");
        RequireContains(factory, "VatRegistrationModeSchemaMigration", "Factory must register v15.");
        RequireContains(migration, "Version => 15", "Migration version must be 15.");
        RequireContains(migration, "vat_registration_mode", "Migration must add vat_registration_mode.");
        RequireContains(setupService, "vat_registration_mode = $vatMode", "Setup must persist mode.");
        RequireContains(setupService, "vat_registration_mode", "Setup must read mode.");
        RequireFalse(setupService.Contains("VatRegistrationMode: null", StringComparison.Ordinal),
            "Setup must not hard-code VatRegistrationMode null.");
        RequireContains(setupPage, "VAT_REGISTERED", "UI must expose VAT_REGISTERED.");
        RequireContains(setupPage, "NON_VAT_PERCENTAGE_TAX", "UI must expose NON_VAT_PERCENTAGE_TAX.");
        RequireContains(setupPage, "NON_VAT_EXEMPT", "UI must expose NON_VAT_EXEMPT.");
        RequireContains(setupPage, "vatRegistrationMode", "UI must bind vatRegistrationMode.");
        RequireContains(dbA2, "verification.Value.SupportedMigrationVersion + 1", "DB-A2 future probe must be supported+1.");
        RequireContains(coreSmoke, "rmo-bir-trad-tax-invoice-policy", "CoreSmoke gate required.");
        RequireContains(coreSmoke, "RmoBirTradM1TaxInvoicePolicyChecks.Run()", "CoreSmoke must run TRAD-M1 checks.");
        RequireContains(engine, "SsptSales", "Engine must model SSPT bucket.");
        RequireContains(engine, "NonVatExemptSales", "Engine must model non-VAT exempt bucket.");
        RequireContains(engine, "ClassificationNotAllowedForMode", "Engine must fail closed on bad classifications.");
        RequireContains(invoicePolicy, "OFFICIAL_INVOICE", "Invoice-first primary document required.");
        RequireContains(invoicePolicy, "IssueInvoiceForEveryCompletedPosSale", "Operational always-issue policy required.");
        RequireContains(invoicePolicy, "ReferenceNonVatStatutoryThresholdCentavosOrientationOnly",
            "Statutory threshold must remain orientation-only, not a buried checkout constant name only.");
        RequireFalse(invoicePolicy.Contains("is accredited", StringComparison.OrdinalIgnoreCase), "No accreditation claim.");
        RequireFalse(setupPage.Contains("is compliant", StringComparison.OrdinalIgnoreCase), "UI no compliance claim.");
        RequireFalse(setupPage.Contains("is accredited", StringComparison.OrdinalIgnoreCase), "UI no accreditation claim.");
    }

    private static void AssertPolicyMatrix()
    {
        RequireTrue(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.VatRegistered,
                ProductTaxClassification.Vatable),
            "VAT mode allows VATable.");
        RequireTrue(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.VatRegistered,
                ProductTaxClassification.VatExempt),
            "VAT mode allows exempt.");
        RequireTrue(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.VatRegistered,
                ProductTaxClassification.ZeroRated),
            "VAT mode allows zero-rated.");
        RequireFalse(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.VatRegistered,
                ProductTaxClassification.NonVat),
            "VAT mode must reject SSPT product classification.");
        RequireFalse(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.NonVatPercentageTax,
                ProductTaxClassification.Vatable),
            "Non-VAT percentage tax must reject VATable.");
        RequireTrue(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.NonVatPercentageTax,
                ProductTaxClassification.NonVat),
            "Non-VAT percentage tax allows SSPT.");
        RequireTrue(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.NonVatPercentageTax,
                ProductTaxClassification.NonVatExempt),
            "Non-VAT percentage tax allows exempt.");
        RequireFalse(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.NonVatExempt,
                ProductTaxClassification.NonVat),
            "Non-VAT exempt mode must reject SSPT.");
        RequireTrue(
            SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                TaxpayerRegistrationMode.NonVatExempt,
                ProductTaxClassification.NonVatExempt),
            "Non-VAT exempt mode allows exempt lines.");

        OfficialTaxPolicySnapshot freeze = SalrynTaxpayerTaxPolicy.Freeze(TaxpayerRegistrationMode.VatRegistered);
        RequireTrue(freeze.VatRegistrationModeCode == TaxpayerRegistrationModeCodes.VatRegistered, "Freeze mode code.");
        RequireTrue(freeze.PrimarySalesDocumentKind == SalrynInvoiceDocumentPolicy.PrimarySalesDocumentKind, "Freeze invoice-first.");
        RequireTrue(freeze.PolicyVersion == SalrynTaxpayerTaxPolicy.PolicyVersion, "Freeze policy version.");

        Result<TaxpayerRegistrationMode> ok = TaxpayerRegistrationModeCodes.Parse("VAT_REGISTERED");
        RequireTrue(ok.IsSuccess, "Parse VAT_REGISTERED.");
        Result<TaxpayerRegistrationMode> bad = TaxpayerRegistrationModeCodes.Parse("VAT_MAYBE");
        RequireTrue(bad.IsFailure, "Reject unknown mode.");
    }

    private static void AssertTaxEngineFailClosed()
    {
        OfficialDocumentTaxBucketEngine engine = new();

        Result<OfficialDocumentTaxBucketDraft> vatOk = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(11200, 0, OfficialDocumentLineTaxClassification.Vatable),
                new OfficialDocumentTaxBucketCalculationLine(5000, 0, OfficialDocumentLineTaxClassification.VatExempt)
            },
            SaleLevelDiscountCents: 0));
        RequireTrue(vatOk.IsSuccess, "VAT mixed sale must compute: " + (vatOk.IsFailure ? vatOk.ErrorDetail.Message : ""));
        RequireTrue(HasBucket(vatOk.Value, OfficialDocumentTaxBucketDraftKind.VatableSales), "VAT sale needs vatable bucket.");
        RequireTrue(HasBucket(vatOk.Value, OfficialDocumentTaxBucketDraftKind.VatAmount), "VAT sale needs VAT amount.");
        RequireTrue(HasBucket(vatOk.Value, OfficialDocumentTaxBucketDraftKind.VatExemptSales), "VAT mixed needs exempt bucket.");

        Result<OfficialDocumentTaxBucketDraft> vatRejectNonVat = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(1000, 0, OfficialDocumentLineTaxClassification.NonVat)
            },
            0));
        RequireTrue(vatRejectNonVat.IsFailure, "VAT mode must reject non-VAT line classification.");

        Result<OfficialDocumentTaxBucketDraft> sspt = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(8000, 0, OfficialDocumentLineTaxClassification.NonVat),
                new OfficialDocumentTaxBucketCalculationLine(2000, 0, OfficialDocumentLineTaxClassification.NonVatExempt)
            },
            0));
        RequireTrue(sspt.IsSuccess, "Non-VAT mixed must compute: " + (sspt.IsFailure ? sspt.ErrorDetail.Message : ""));
        RequireTrue(HasBucket(sspt.Value, OfficialDocumentTaxBucketDraftKind.SsptSales), "Needs SSPT bucket.");
        RequireTrue(HasBucket(sspt.Value, OfficialDocumentTaxBucketDraftKind.NonVatExemptSales), "Needs non-VAT exempt bucket.");
        RequireFalse(HasBucket(sspt.Value, OfficialDocumentTaxBucketDraftKind.VatAmount), "Non-VAT must not create VAT amount.");

        Result<OfficialDocumentTaxBucketDraft> nonVatRejectVat = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(1000, 0, OfficialDocumentLineTaxClassification.Vatable)
            },
            0));
        RequireTrue(nonVatRejectVat.IsFailure, "Non-VAT percentage tax must reject VATable lines.");

        Result<OfficialDocumentTaxBucketDraft> exemptOnly = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(3000, 0, OfficialDocumentLineTaxClassification.NonVatExempt)
            },
            0));
        RequireTrue(exemptOnly.IsSuccess, "Non-VAT exempt path must compute.");
        RequireTrue(HasBucket(exemptOnly.Value, OfficialDocumentTaxBucketDraftKind.NonVatExemptSales), "Exempt path bucket.");

        Result<OfficialDocumentTaxBucketDraft> exemptRejectSspt = engine.Calculate(new OfficialDocumentTaxBucketCalculationRequest(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt,
            new[]
            {
                new OfficialDocumentTaxBucketCalculationLine(1000, 0, OfficialDocumentLineTaxClassification.NonVat)
            },
            0));
        RequireTrue(exemptRejectSspt.IsFailure, "Non-VAT exempt mode must reject SSPT lines.");

        // Inclusive VAT: 11200 gross @ 12% => VAT 1200, net 10000
        long vat = OfficialDocumentTaxBucketEngine.ComputeInclusiveVatAmountCents(11200);
        RequireTrue(vat == 1200, "Inclusive VAT half-away rounding for 11200 must be 1200, was " + vat);
    }

    private static void AssertMigrationAndSetupPersistence()
    {
        string qaRoot = Path.Combine(Path.GetTempPath(), "Salryn-TRAD-M1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(qaRoot);
        try
        {
            DbPaths paths = CreateIsolatedPaths(qaRoot, "persist");
            Result<DatabaseInitializationSummary> init =
                DefaultDatabaseInitializationFactory.Create(paths).InitializeDatabase();
            RequireTrue(init.IsSuccess, "Init must succeed: " + (init.IsFailure ? init.ErrorDetail.Message : ""));
            RequireTrue(ReadMaxMigration(paths.DatabasePath) == 27, "Max migration must be 27.");
            RequireTrue(ColumnExists(paths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
                "vat_registration_mode column required.");

            SqliteHostOfficialSetupService setup = new(new SqliteConnectionFactory(paths));
            Result<OfficialSetupSnapshotContract> saved = setup.SaveBusinessAsync(
                new OfficialSetupBusinessSaveRequest(
                    RegisteredTaxpayerName: "TRAD M1 Taxpayer",
                    BusinessName: "TRAD Store",
                    RegisteredAddress: "1 Test St",
                    BranchAddress: null,
                    TaxpayerIdentificationNumber: "123-456-789-000",
                    BranchCode: "00001",
                    VatRegistrationMode: TaxpayerRegistrationModeCodes.NonVatPercentageTax,
                    ContactNote: null),
                updatedByUserId: null).GetAwaiter().GetResult();
            RequireTrue(saved.IsSuccess, "Save business must succeed: " + (saved.IsFailure ? saved.ErrorDetail.Message : ""));
            RequireTrue(
                saved.Value.Business?.VatRegistrationMode == TaxpayerRegistrationModeCodes.NonVatPercentageTax,
                "Saved snapshot must return NON_VAT_PERCENTAGE_TAX.");

            Result<OfficialSetupSnapshotContract> reread = setup.GetSnapshotAsync().GetAwaiter().GetResult();
            RequireTrue(reread.IsSuccess, "Reread must succeed.");
            RequireTrue(
                reread.Value.Business?.VatRegistrationMode == TaxpayerRegistrationModeCodes.NonVatPercentageTax,
                "Reread must persist NON_VAT_PERCENTAGE_TAX.");

            Result<OfficialSetupSnapshotContract> badMode = setup.SaveBusinessAsync(
                new OfficialSetupBusinessSaveRequest(
                    "X", null, "Y", null, "1", "00002", "VAT_MAYBE", null),
                null).GetAwaiter().GetResult();
            RequireTrue(badMode.IsFailure, "Unknown VAT mode must fail closed.");

            // v14 → v15 upgrade preserves data
            DbPaths upgradePaths = CreateIsolatedPaths(qaRoot, "upgrade");
            upgradePaths.EnsureDirectories();
            IDatabaseMigration[] through14 =
            {
                new InitialSchemaMigration(),
                new BirMachineProfileSchemaMigration(),
                new BirOfficialDocumentSequenceSchemaMigration(),
                new BirElectronicJournalSchemaMigration(),
                new BirElectronicJournalSchemaHardeningMigration(),
                new BirZReadingSchemaMigration(),
                new BirElectronicJournalCorrectionEventSchemaMigration(),
                new ProductTaxClassificationSchemaMigration(),
                new DeploymentIdentitySchemaMigration(),
                new TerminalRegistrySchemaMigration(),
                new LanRemoteTransportSchemaMigration(),
                new TerminalCredentialHandshakeSchemaMigration(),
                new OfficialReceiptProfileSchemaMigration(),
                new OfficialDryRunPackageSchemaMigration()
            };
            new MigrationRunner(new SqliteConnectionFactory(upgradePaths), through14).RunPendingMigrations();
            RequireTrue(ReadMaxMigration(upgradePaths.DatabasePath) == 14, "Fixture v14.");
            RequireFalse(ColumnExists(upgradePaths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
                "v14 has no vat mode column.");

            Result<DatabaseInitializationSummary> upgraded =
                DefaultDatabaseInitializationFactory.Create(upgradePaths).InitializeDatabase();
            RequireTrue(upgraded.IsSuccess, "v14→v15 must succeed.");
            RequireTrue(ReadMaxMigration(upgradePaths.DatabasePath) == 27, "After upgrade max 27.");
            RequireTrue(ColumnExists(upgradePaths.DatabasePath, "bir_machine_profiles", "vat_registration_mode"),
                "v15 column after upgrade.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try
            {
                if (Directory.Exists(qaRoot))
                {
                    Directory.Delete(qaRoot, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine("[TRAD-M1] WARNING cleanup: " + ex.Message);
            }
        }
    }

    private static bool HasBucket(OfficialDocumentTaxBucketDraft draft, OfficialDocumentTaxBucketDraftKind kind)
    {
        foreach (OfficialDocumentTaxBucketDraftBucket bucket in draft.Buckets)
        {
            if (bucket.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadMaxMigration(string dbPath)
    {
        using SqliteConnection connection = new("Data Source=" + dbPath);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static bool ColumnExists(string dbPath, string table, string column)
    {
        using SqliteConnection connection = new("Data Source=" + dbPath);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(" + table + ");";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static DbPaths CreateIsolatedPaths(string qaRoot, string leaf)
    {
        // DbPaths.CreateDevelopment places data under root/.salryn-dev; use unique roots under TEMP.
        string root = Path.Combine(qaRoot, leaf);
        Directory.CreateDirectory(root);
        return DbPaths.CreateDevelopment(root);
    }

    private static string FindProjectRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln from " + start);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool condition, string message) => RequireTrue(!condition, message);

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }
}

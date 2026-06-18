using Salryn.Core.Models;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentTaxBucketDraftChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Models",
            "OfficialDocumentTaxBucketDraft.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_4C_C05_TAX_BUCKET_DRAFT_CONTRACTS.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(contractText, "OfficialDocumentTaxBucketDraft", "J21-4C must define OfficialDocumentTaxBucketDraft.");
        RequireContains(contractText, "J21-4C owns C05 tax bucket draft contracts only.", "J21-4C contract must explain ownership.");
        RequireContains(contractText, "These records describe VAT/non-VAT/exempt/zero-rated bucket shape.", "J21-4C contract must explain C05 shape.");
        RequireContains(contractText, "No calculator engine, renderer, printer, checkout wiring, sale snapshot write", "J21-4C contract must block scope creep.");
        RequireContains(contractText, "OfficialDocumentTaxBucketDraftTaxpayerMode", "J21-4C must define taxpayer mode shape.");
        RequireContains(contractText, "VatRegistered", "J21-4C must include VAT-registered mode.");
        RequireContains(contractText, "NonVatRegistered", "J21-4C must include non-VAT mode.");
        RequireContains(contractText, "OfficialDocumentTaxBucketDraftKind", "J21-4C must define tax bucket kind.");
        RequireContains(contractText, "VatableSales", "J21-4C must include VATable sales bucket.");
        RequireContains(contractText, "VatAmount", "J21-4C must include VAT amount bucket.");
        RequireContains(contractText, "VatExemptSales", "J21-4C must include VAT-exempt sales bucket.");
        RequireContains(contractText, "ZeroRatedSales", "J21-4C must include zero-rated sales bucket.");
        RequireContains(contractText, "NonVatSales", "J21-4C must include non-VAT sales bucket.");
        RequireContains(contractText, "SalesAmountCents", "J21-4C bucket must carry sales amount shape.");
        RequireContains(contractText, "TaxAmountCents", "J21-4C bucket must carry tax amount shape.");
        RequireDoesNotContain(contractText, "Microsoft.Data.Sqlite", "J21-4C contracts must not depend on SQLite.");
        RequireDoesNotContain(contractText, "SqliteConnection", "J21-4C contracts must not open database connections.");
        RequireDoesNotContain(contractText, "ReceiptNumber.Create", "J21-4C contracts must not allocate sequence numbers.");
        RequireDoesNotContain(contractText, "AllocateNextAsync", "J21-4C contracts must not call sequence allocation.");
        RequireDoesNotContain(contractText, "System.Drawing.Printing", "J21-4C contracts must not depend on printing.");
        RequireDoesNotContain(contractText, "PrintDocument", "J21-4C contracts must not create print documents.");
        RequireDoesNotContain(contractText, "receipt_snapshot", "J21-4C contracts must not write saved sale receipt_snapshot fields.");
        RequireDoesNotContain(contractText, "Build(", "J21-4C contracts must not add a builder or engine.");
        RequireDoesNotContain(contractText, "Calculate", "J21-4C contracts must not add tax calculation logic.");
        RequireDoesNotContain(contractText, "Compute", "J21-4C contracts must not add tax computation logic.");
        RequireContains(matrixText, "## J21-4C C05 Tax Bucket Draft Contracts", "Compliance matrix must include J21-4C section.");
        RequireContains(handoffText, "Status: contracts only.", "J21-4C handoff must preserve contracts-only scope.");
        RequireContains(handoffText, "No calculator engine.", "J21-4C handoff must block calculator engine work.");
        RequireContains(handoffText, "No renderer.", "J21-4C handoff must block renderer work.");
        RequireContains(handoffText, "No checkout wiring.", "J21-4C handoff must block checkout wiring.");
        RequireContains(handoffText, "No saved sale receipt_snapshot writes.", "J21-4C handoff must block saved snapshot writes.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentTaxBucketDraftChecks.Run();", "Core smoke must run J21-4C checks.");

        OfficialDocumentTaxBucketDraft draft = new(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatableSales,
                    SalesAmountCents: 10000,
                    TaxAmountCents: 0,
                    Notes: "Shape only."),
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatAmount,
                    SalesAmountCents: 0,
                    TaxAmountCents: 1200,
                    Notes: "Shape only."),
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatExemptSales,
                    SalesAmountCents: 5000,
                    TaxAmountCents: 0,
                    Notes: "Shape only."),
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.ZeroRatedSales,
                    SalesAmountCents: 2500,
                    TaxAmountCents: 0,
                    Notes: "Shape only."),
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.NonVatSales,
                    SalesAmountCents: 0,
                    TaxAmountCents: 0,
                    Notes: "Not used for this VAT sample.")
            },
            NonVatStatement: null,
            new[]
            {
                "J21-4C tax bucket draft contracts only.",
                "No VAT bucket engine.",
                "No official output rendering."
            });

        RequireEqual(OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered, draft.TaxpayerMode, "Taxpayer mode must be readable.");
        RequireEqual(5, draft.Buckets.Count, "All C05 bucket shapes must be constructible.");
        RequireEqual(OfficialDocumentTaxBucketDraftKind.VatableSales, draft.Buckets[0].Kind, "VATable bucket kind must be readable.");
        RequireEqual(10000L, draft.Buckets[0].SalesAmountCents, "VATable sales amount shape must be readable.");
        RequireEqual(1200L, draft.Buckets[1].TaxAmountCents, "VAT amount shape must be readable.");
        RequireEqual(true, draft.DeferredComplianceNotes.Any(note => note.Contains("No VAT bucket engine.", StringComparison.Ordinal)), "Deferred notes must preserve no-engine guard.");

        OfficialDocumentTaxBucketDraft nonVatDraft = new(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.NonVatSales,
                    SalesAmountCents: 10000,
                    TaxAmountCents: 0,
                    Notes: "Shape only.")
            },
            NonVatStatement: "NON-VAT draft statement placeholder.",
            new[]
            {
                "No official output rendering."
            });

        RequireEqual(OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered, nonVatDraft.TaxpayerMode, "Non-VAT taxpayer mode must be readable.");
        RequireEqual(OfficialDocumentTaxBucketDraftKind.NonVatSales, nonVatDraft.Buckets[0].Kind, "Non-VAT bucket kind must be readable.");
        RequireEqual("NON-VAT draft statement placeholder.", nonVatDraft.NonVatStatement!, "Non-VAT statement placeholder must be readable.");
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

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message);
        }
    }
}
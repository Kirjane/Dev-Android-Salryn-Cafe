using Salryn.Core.Models;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentSnapshotDraftChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Models",
            "OfficialDocumentSnapshotDraft.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_4A_OFFICIAL_DOCUMENT_SNAPSHOT_DRAFT_CONTRACTS.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(contractText, "OfficialDocumentSnapshotDraft", "J21-4A must define OfficialDocumentSnapshotDraft.");
        RequireContains(contractText, "J21-4A owns neutral official-document snapshot draft contracts only.", "J21-4A contract must explain ownership.");
        RequireContains(contractText, "OfficialDocumentSnapshotDraft is a data shape, not rendered output.", "J21-4A contract must block output claims.");
        RequireContains(contractText, "No renderer, printer, checkout wiring, sequence allocation, database persistence", "J21-4A contract must block scope creep.");
        RequireContains(contractText, "OfficialDocumentSnapshotDraftHeader", "J21-4A must define C03 header shape.");
        RequireContains(contractText, "RegisteredTaxpayerName", "J21-4A header must include registered taxpayer name.");
        RequireContains(contractText, "TaxpayerIdentificationNumber", "J21-4A header must include TIN.");
        RequireContains(contractText, "BranchCode", "J21-4A header must include branch code.");
        RequireContains(contractText, "RegisteredBusinessAddress", "J21-4A header must include registered business address.");
        RequireContains(contractText, "TransactionDateTime", "J21-4A header must include transaction date/time shape.");
        RequireContains(contractText, "DocumentNumber", "J21-4A header must include future receipt/invoice number shape.");
        RequireContains(contractText, "MachineSerialNumber", "J21-4A header must include machine serial number shape.");
        RequireContains(contractText, "MachineIdentificationNumber", "J21-4A header must include MIN shape.");
        RequireContains(contractText, "AccreditationNumber", "J21-4A header must include accreditation number shape.");
        RequireContains(contractText, "PermitToUseNumber", "J21-4A header must include PTU number shape.");
        RequireContains(contractText, "CashierIdentifier", "J21-4A header must include cashier identifier shape.");
        RequireContains(contractText, "TerminalIdentifier", "J21-4A header must include terminal identifier shape.");
        RequireContains(contractText, "OfficialDocumentSnapshotDraftLine", "J21-4A must define C04 line shape.");
        RequireContains(contractText, "QuantityX1000", "J21-4A line must include quantity shape.");
        RequireContains(contractText, "UnitPriceCents", "J21-4A line must include unit price shape.");
        RequireContains(contractText, "LineTotalCents", "J21-4A line must include line total shape.");
        RequireContains(contractText, "OfficialDocumentSnapshotDraftAmounts", "J21-4A must define C04 amount shape.");
        RequireContains(contractText, "AmountPaidCents", "J21-4A amounts must include amount paid.");
        RequireContains(contractText, "ChangeCents", "J21-4A amounts must include change.");
        RequireContains(contractText, "OfficialDocumentSnapshotDraftPayment", "J21-4A must define C04 payment shape.");
        RequireContains(contractText, "OfficialDocumentSnapshotDraftTaxMode", "J21-4A must define C05 placeholder shape.");
        RequireContains(contractText, "VAT/non-VAT bucket computation is intentionally deferred", "J21-4A must defer C05 VAT bucket engine.");
        RequireDoesNotContain(contractText, "Microsoft.Data.Sqlite", "J21-4A contracts must not depend on SQLite.");
        RequireDoesNotContain(contractText, "SqliteConnection", "J21-4A contracts must not open database connections.");
        RequireDoesNotContain(contractText, "ReceiptNumber.Create", "J21-4A contracts must not allocate sequence numbers.");
        RequireDoesNotContain(contractText, "AllocateNextAsync", "J21-4A contracts must not call sequence allocation.");
        RequireDoesNotContain(contractText, "receipt_snapshot", "J21-4A contracts must not write saved sale receipt_snapshot fields.");
        RequireDoesNotContain(contractText, "System.Drawing.Printing", "J21-4A contracts must not depend on printing.");
        RequireContains(matrixText, "## J21-4A Official Document Snapshot Draft Contracts", "Compliance matrix must include J21-4A section.");
        RequireContains(handoffText, "Status: contracts only.", "J21-4A handoff must preserve contracts-only scope.");
        RequireContains(handoffText, "No renderer.", "J21-4A handoff must block renderer work.");
        RequireContains(handoffText, "No saved sale receipt_snapshot writes.", "J21-4A handoff must block saved snapshot writes.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentSnapshotDraftChecks.Run();", "Core smoke must run J21-4A checks.");

        OfficialDocumentSnapshotDraft draft = new(
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            new OfficialDocumentSnapshotDraftHeader(
                RegisteredTaxpayerName: "Kirch Test Taxpayer",
                BusinessName: "Kirch Test Store",
                TaxpayerIdentificationNumber: "000-000-000-000",
                BranchCode: "00000",
                RegisteredBusinessAddress: "Test Registered Address",
                TransactionDateTime: new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero),
                DocumentNumber: "OR-2026-000001",
                MachineSerialNumber: "TEST-SERIAL-001",
                MachineIdentificationNumber: "MIN-TEST-001",
                AccreditationNumber: "ACC-TEST",
                PermitToUseNumber: "PTU-TEST",
                CashierIdentifier: "cashier@test",
                TerminalIdentifier: "POS-1"),
            new[]
            {
                new OfficialDocumentSnapshotDraftLine(
                    Description: "Sample Product",
                    Barcode: "1234567890",
                    UnitName: "pc",
                    QuantityX1000: 1000,
                    UnitPriceCents: 10000,
                    DiscountCents: 0,
                    LineTotalCents: 10000)
            },
            new OfficialDocumentSnapshotDraftAmounts(
                SubtotalCents: 10000,
                DiscountCents: 0,
                TaxCents: 0,
                TotalCents: 10000,
                AmountPaidCents: 10000,
                ChangeCents: 0),
            new[]
            {
                new OfficialDocumentSnapshotDraftPayment(
                    Method: PaymentMethod.Cash,
                    AmountCents: 10000,
                    PaidAt: new DateTimeOffset(2026, 6, 18, 8, 1, 0, TimeSpan.Zero))
            },
            new OfficialDocumentSnapshotDraftTaxMode(
                TaxModeCode: "VAT_BUCKET_ENGINE_DEFERRED",
                VatBucketEngineDeferred: true,
                Notes: "C05 is deferred."),
            new[]
            {
                "Draft contract only.",
                "No official output rendering."
            });

        RequireEqual(OfficialDocumentSnapshotDraftKind.OfficialReceipt, draft.DocumentKind, "Draft kind must be constructible.");
        RequireEqual("Kirch Test Taxpayer", draft.Header.RegisteredTaxpayerName, "Header taxpayer name must be readable.");
        RequireEqual("OR-2026-000001", draft.Header.DocumentNumber!, "Header document number field must be readable.");
        RequireEqual(1, draft.Lines.Count, "Draft line collection must be readable.");
        RequireEqual(10000L, draft.Amounts.TotalCents, "Draft amount total must be readable.");
        RequireEqual(PaymentMethod.Cash, draft.Payments[0].Method, "Draft payment method must be readable.");
        RequireEqual(true, draft.TaxMode.VatBucketEngineDeferred, "Draft tax mode must preserve C05 deferral flag.");
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
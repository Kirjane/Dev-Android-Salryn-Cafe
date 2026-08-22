using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentSnapshotEvidenceBuilderChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string evidenceBuilderText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialDocumentSnapshotEvidenceBuilder.cs");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(evidenceBuilderText, "OfficialDocumentSnapshotEvidenceBuilder", "Snapshot evidence builder must exist.");
        RequireContains(evidenceBuilderText, "Utf8JsonWriter", "Snapshot evidence must write canonical JSON explicitly.");
        RequireContains(evidenceBuilderText, "SHA256.HashData", "Snapshot evidence must hash the payload.");
        RequireContains(evidenceBuilderText, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1", "Snapshot evidence must version its payload schema.");
        RequireContains(evidenceBuilderText, "Kirch: why - journal evidence needs stable payload/hash data", "Snapshot evidence builder must explain its compliance reason.");
        RequireDoesNotContain(evidenceBuilderText, "Microsoft.Data.Sqlite", "Snapshot evidence builder must not depend on SQLite.");
        RequireDoesNotContain(evidenceBuilderText, "SqliteConnection", "Snapshot evidence builder must not open database connections.");
        RequireDoesNotContain(evidenceBuilderText, "PrintDocument", "Snapshot evidence builder must not create print documents.");
        RequireDoesNotContain(evidenceBuilderText, "Crystal", "Snapshot evidence builder must not wire reports.");
        RequireDoesNotContain(evidenceBuilderText, "CompleteSale", "Snapshot evidence builder must not wire checkout.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentSnapshotEvidenceBuilderChecks.Run();", "CoreSmoke must run snapshot evidence checks.");

        OfficialDocumentSnapshotDraft draft = CreateDraft("OR-2026-000001", totalCents: 9500);
        OfficialDocumentSnapshotEvidenceBuilder builder = new();

        Result<ElectronicJournalSnapshotEvidence> firstResult = builder.Build(draft);
        Result<ElectronicJournalSnapshotEvidence> secondResult = builder.Build(draft);

        RequireSuccess(firstResult, "Snapshot evidence build must pass.");
        RequireSuccess(secondResult, "Repeated snapshot evidence build must pass.");

        ElectronicJournalSnapshotEvidence first = firstResult.Value;
        ElectronicJournalSnapshotEvidence second = secondResult.Value;

        RequireEqual(first.PayloadJson, second.PayloadJson, "Snapshot evidence JSON must be deterministic.");
        RequireEqual(first.PayloadHash, second.PayloadHash, "Snapshot evidence hash must be deterministic.");
        RequireEqual("SHA-256", first.HashAlgorithm, "Snapshot evidence must name SHA-256.");
        RequireEqual(64, first.PayloadHash.Length, "SHA-256 hex hash must be 64 characters.");
        RequireContains(first.PayloadJson, "\"schema\":\"SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1\"", "Payload must include schema version.");
        RequireContains(first.PayloadJson, "\"documentNumber\":\"OR-2026-000001\"", "Payload must include official document number.");
        RequireContains(first.PayloadJson, "\"documentKind\":\"OfficialReceipt\"", "Payload must include document kind.");

        Result<ElectronicJournalSnapshotEvidence> changedResult =
            builder.Build(CreateDraft("OR-2026-000001", totalCents: 9600));

        RequireSuccess(changedResult, "Changed snapshot evidence build must pass.");

        if (string.Equals(first.PayloadHash, changedResult.Value.PayloadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Snapshot evidence hash must change when payload amounts change.");
        }

        RequireFailureCode(
            builder.Build(CreateDraft(null, totalCents: 9500)),
            "OfficialDocumentSnapshotEvidence.DocumentNumberRequired",
            "Snapshot evidence must require allocated official document number.");
    }

    private static OfficialDocumentSnapshotDraft CreateDraft(
        string? documentNumber,
        long totalCents)
    {
        Sale sale = new()
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ReceiptNumber = "POS-DEV-NONOFFICIAL",
            SaleDate = new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.Zero),
            CashierUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CashierSessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Subtotal = Money.FromCents(totalCents),
            Discount = Money.FromCents(0),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(totalCents),
            AmountPaid = Money.FromCents(totalCents),
            Change = Money.FromCents(0),
            Status = SaleStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 6, 24, 8, 0, 1, TimeSpan.Zero)
        };

        SaleItem item = new()
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            SaleId = sale.Id,
            ProductId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            ProductNameSnapshot = "Sample Product",
            BarcodeSnapshot = "1234567890",
            UnitNameSnapshot = "pc",
            Quantity = Quantity.FromX1000(1000).Value,
            UnitPrice = Money.FromCents(totalCents),
            CostPrice = Money.FromCents(7000),
            Discount = Money.FromCents(0),
            LineTotal = Money.FromCents(totalCents)
        };

        Payment payment = new()
        {
            Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            SaleId = sale.Id,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(totalCents),
            PaidAt = new DateTimeOffset(2026, 6, 24, 8, 1, 0, TimeSpan.Zero)
        };

        OfficialDocumentSnapshotDraftBuilder draftBuilder = new();

        Result<OfficialDocumentSnapshotDraft> result = draftBuilder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            new OfficialDocumentSnapshotDraftMachineProfileInput(
                RegisteredTaxpayerName: "Kirch Test Taxpayer",
                BusinessName: "Kirch Test Store",
                TaxpayerIdentificationNumber: "000-000-000-000",
                BranchCode: "00000",
                RegisteredBusinessAddress: "Test Registered Address",
                MachineSerialNumber: "TEST-SERIAL-001",
                MachineIdentificationNumber: "MIN-TEST-001",
                AccreditationNumber: "ACC-TEST",
                PermitToUseNumber: "PTU-TEST"),
            sale,
            new[] { item },
            new[] { payment },
            AllocatedDocumentNumber: documentNumber,
            CashierIdentifier: "cashier@test",
            TerminalIdentifier: "POS-1",
            TaxModeCode: "VAT_BUCKET_ENGINE_DEFERRED",
            TaxModeNotes: "C05 is deferred."));

        RequireSuccess(result, "Draft build must pass before evidence build.");

        return result.Value;
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

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.Error}");
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
            throw new InvalidOperationException(message);
        }
    }
}

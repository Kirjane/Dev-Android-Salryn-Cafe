using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentJournalAppendContractBuilderChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string builderText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialDocumentJournalAppendContractBuilder.cs");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(builderText, "OfficialDocumentJournalAppendContractBuilder", "Journal contract builder must exist.");
        RequireContains(builderText, "OfficialDocumentSnapshotEvidenceBuilder", "Journal contract builder must consume snapshot evidence.");
        RequireContains(builderText, "ElectronicJournalAppendContractValidator", "Journal contract builder must validate the final contract.");
        RequireContains(builderText, "SnapshotPayloadHashPersistence", "Journal contract builder must include snapshot hash boundary step.");
        RequireContains(builderText, "DocumentNumberMismatch", "Journal contract builder must block number drift.");
        RequireContains(builderText, "Kirch: why - official checkout needs one validated handoff", "Journal contract builder must explain its compliance reason.");
        RequireDoesNotContain(builderText, "Microsoft.Data.Sqlite", "Journal contract builder must not depend on SQLite.");
        RequireDoesNotContain(builderText, "SqliteConnection", "Journal contract builder must not open database connections.");
        RequireDoesNotContain(builderText, "PrintDocument", "Journal contract builder must not create print documents.");
        RequireDoesNotContain(builderText, "Crystal", "Journal contract builder must not wire reports.");
        RequireDoesNotContain(builderText, "AddCompletedSaleAsync", "Journal contract builder must not persist sales.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentJournalAppendContractBuilderChecks.Run();", "CoreSmoke must run journal contract builder checks.");

        OfficialDocumentSnapshotDraft draft = CreateDraft("OR-2026-000001");
        OfficialDocumentJournalAppendContractBuilder builder = new();
        Guid machineProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid saleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid sequenceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid cashierId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid eventId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        DateTimeOffset eventCreatedAt = new(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);

        Result<ElectronicJournalAppendContract> result = builder.Build(
            new OfficialDocumentJournalAppendContractBuildRequest(
                machineProfileId,
                saleId,
                sequenceId,
                "OR-2026-000001",
                draft,
                cashierId,
                " POS-1 ",
                eventId,
                eventCreatedAt));

        RequireSuccess(result, "Journal contract build must pass.");

        ElectronicJournalAppendContract contract = result.Value;

        RequireEqual(machineProfileId, contract.BirMachineProfileId, "Machine profile id must map.");
        RequireEqual(saleId, contract.SaleId, "Sale id must map.");
        RequireEqual(sequenceId, contract.OfficialDocumentSequenceId, "Sequence id must map.");
        RequireEqual("OR-2026-000001", contract.OfficialDocumentNumber, "Official number must map.");
        RequireEqual(ElectronicJournalDocumentKind.OfficialReceipt, contract.DocumentKind, "Document kind must map.");
        RequireEqual(draft.Header.TransactionDateTime, contract.TransactionTimestamp, "Transaction timestamp must map.");
        RequireEqual(cashierId, contract.CashierUserId, "Cashier id must map.");
        RequireEqual("POS-1", contract.TerminalId!, "Terminal id must normalize.");
        RequireEqual(eventId, contract.OriginalEvent.EventId, "Original event id must map.");
        RequireEqual(eventCreatedAt, contract.OriginalEvent.CreatedAt, "Original event timestamp must map.");
        RequireEqual("SHA-256", contract.SnapshotEvidence?.HashAlgorithm, "Snapshot evidence must be included.");
        RequireEqual(4, contract.AtomicBoundarySteps.Count, "Snapshot evidence contract must include four boundary steps.");
        RequireEqual(ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation, contract.AtomicBoundarySteps[0], "Sequence step must be first.");
        RequireEqual(ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence, contract.AtomicBoundarySteps[1], "Snapshot step must follow sequence.");
        RequireEqual(ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert, contract.AtomicBoundarySteps[2], "Journal anchor must follow snapshot.");
        RequireEqual(ElectronicJournalAtomicBoundaryStep.OriginalEventInsert, contract.AtomicBoundarySteps[3], "Original event must follow journal anchor.");

        ElectronicJournalAppendContractValidator validator = new();
        RequireSuccess(validator.Validate(contract), "Built journal contract must pass append validator.");

        RequireFailureCode(
            builder.Build(new OfficialDocumentJournalAppendContractBuildRequest(
                machineProfileId,
                saleId,
                sequenceId,
                "OR-2026-999999",
                draft,
                cashierId,
                "POS-1",
                eventId,
                eventCreatedAt)),
            "OfficialDocumentJournalContract.DocumentNumberMismatch",
            "Journal contract builder must reject official number mismatch.");

        RequireFailureCode(
            builder.Build(new OfficialDocumentJournalAppendContractBuildRequest(
                machineProfileId,
                saleId,
                sequenceId,
                "OR-2026-000001",
                draft,
                cashierId,
                "POS-1",
                Guid.Empty,
                eventCreatedAt)),
            "OfficialDocumentJournalContract.OriginalEventRequired",
            "Journal contract builder must require ORIGINAL event id.");
    }

    private static OfficialDocumentSnapshotDraft CreateDraft(string documentNumber)
    {
        Sale sale = new()
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ReceiptNumber = "POS-DEV-NONOFFICIAL",
            SaleDate = new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.Zero),
            CashierUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CashierSessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Subtotal = Money.FromCents(9500),
            Discount = Money.FromCents(0),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(9500),
            AmountPaid = Money.FromCents(10000),
            Change = Money.FromCents(500),
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
            UnitPrice = Money.FromCents(9500),
            CostPrice = Money.FromCents(7000),
            Discount = Money.FromCents(0),
            LineTotal = Money.FromCents(9500)
        };

        Payment payment = new()
        {
            Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            SaleId = sale.Id,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(10000),
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

        RequireSuccess(result, "Draft build must pass before journal contract build.");

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

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.Error}");
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

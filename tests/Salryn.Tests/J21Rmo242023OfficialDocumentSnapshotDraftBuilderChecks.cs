using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentSnapshotDraftBuilderChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string builderText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialDocumentSnapshotDraftBuilder.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_4B_OFFICIAL_DOCUMENT_SNAPSHOT_DRAFT_BUILDER.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(builderText, "OfficialDocumentSnapshotDraftBuilder", "J21-4B must define the draft builder.");
        RequireContains(builderText, "J21-4B owns in-memory draft building only.", "J21-4B builder must explain ownership.");
        RequireContains(builderText, "It must not render, print, persist, allocate sequence numbers, wire checkout", "J21-4B builder must block scope creep.");
        RequireContains(builderText, "This builder does not allocate, reserve, increment, or validate official sequence ownership.", "J21-4B builder must not allocate sequence numbers.");
        RequireContains(builderText, "OfficialDocumentSnapshotDraftMachineProfileInput", "J21-4B must define machine-profile input.");
        RequireContains(builderText, "OfficialDocumentSnapshotDraftBuildRequest", "J21-4B must define build request.");
        RequireContains(builderText, "Sale", "J21-4B must map sale input.");
        RequireContains(builderText, "SaleItem", "J21-4B must map sale item input.");
        RequireContains(builderText, "Payment", "J21-4B must map payment input.");
        RequireContains(builderText, "QuantityX1000: item.Quantity.ValueX1000", "J21-4B must map item quantity.");
        RequireContains(builderText, "UnitPriceCents: item.UnitPrice.Cents", "J21-4B must map item unit price.");
        RequireContains(builderText, "LineTotalCents: item.LineTotal.Cents", "J21-4B must map item line total.");
        RequireContains(builderText, "TotalCents: sale.Total.Cents", "J21-4B must map sale total.");
        RequireContains(builderText, "VatBucketEngineDeferred: true", "J21-4B must keep VAT bucket engine deferred.");
        RequireDoesNotContain(builderText, "Microsoft.Data.Sqlite", "J21-4B builder must not depend on SQLite.");
        RequireDoesNotContain(builderText, "SqliteConnection", "J21-4B builder must not open database connections.");
        RequireDoesNotContain(builderText, "ReceiptNumber.Create", "J21-4B builder must not allocate or format sequence numbers.");
        RequireDoesNotContain(builderText, "AllocateNextAsync", "J21-4B builder must not call sequence allocation.");
        RequireDoesNotContain(builderText, "System.Drawing.Printing", "J21-4B builder must not depend on printing.");
        RequireDoesNotContain(builderText, "PrintDocument", "J21-4B builder must not create print documents.");
        RequireContains(matrixText, "## J21-4B Official Document Snapshot Draft Builder", "Compliance matrix must include J21-4B section.");
        RequireContains(handoffText, "Status: builder only.", "J21-4B handoff must preserve builder-only scope.");
        RequireContains(handoffText, "No renderer.", "J21-4B handoff must block renderer work.");
        RequireContains(handoffText, "No checkout wiring.", "J21-4B handoff must block checkout wiring.");
        RequireContains(handoffText, "No saved sale receipt_snapshot writes.", "J21-4B handoff must block saved snapshot writes.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentSnapshotDraftBuilderChecks.Run();", "Core smoke must run J21-4B checks.");

        OfficialDocumentSnapshotDraftBuilder builder = new();

        Sale sale = new()
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ReceiptNumber = "POS-DEV-NONOFFICIAL",
            SaleDate = new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero),
            CashierUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CashierSessionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Subtotal = Money.FromCents(10000),
            Discount = Money.FromCents(500),
            Tax = Money.FromCents(0),
            Total = Money.FromCents(9500),
            AmountPaid = Money.FromCents(10000),
            Change = Money.FromCents(500),
            Status = SaleStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 6, 18, 8, 0, 1, TimeSpan.Zero)
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
            UnitPrice = Money.FromCents(10000),
            CostPrice = Money.FromCents(7500),
            Discount = Money.FromCents(500),
            LineTotal = Money.FromCents(9500)
        };

        Payment payment = new()
        {
            Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            SaleId = sale.Id,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(10000),
            PaidAt = new DateTimeOffset(2026, 6, 18, 8, 1, 0, TimeSpan.Zero)
        };

        Result<OfficialDocumentSnapshotDraft> result = builder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
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
            AllocatedDocumentNumber: "OR-2026-000001",
            CashierIdentifier: "cashier@test",
            TerminalIdentifier: "POS-1",
            TaxModeCode: "vat_bucket_engine_deferred",
            TaxModeNotes: "C05 is deferred."));

        RequireSuccess(result, "Draft builder should create an in-memory draft.");

        OfficialDocumentSnapshotDraft draft = result.Value;

        RequireEqual(OfficialDocumentSnapshotDraftKind.OfficialReceipt, draft.DocumentKind, "Draft kind must map.");
        RequireEqual("Kirch Test Taxpayer", draft.Header.RegisteredTaxpayerName, "Taxpayer name must map.");
        RequireEqual("OR-2026-000001", draft.Header.DocumentNumber!, "Provided document number must map without allocation.");
        RequireEqual("TEST-SERIAL-001", draft.Header.MachineSerialNumber!, "Machine serial number must map.");
        RequireEqual("cashier@test", draft.Header.CashierIdentifier, "Cashier identifier must map.");
        RequireEqual("POS-1", draft.Header.TerminalIdentifier!, "Terminal identifier must map.");
        RequireEqual(1, draft.Lines.Count, "Lines must map.");
        RequireEqual("Sample Product", draft.Lines[0].Description, "Line description must map.");
        RequireEqual(1000L, draft.Lines[0].QuantityX1000, "Line quantity must map.");
        RequireEqual(10000L, draft.Lines[0].UnitPriceCents, "Line unit price must map.");
        RequireEqual(9500L, draft.Lines[0].LineTotalCents, "Line total must map.");
        RequireEqual(9500L, draft.Amounts.TotalCents, "Sale total must map.");
        RequireEqual(10000L, draft.Amounts.AmountPaidCents, "Amount paid must map.");
        RequireEqual(500L, draft.Amounts.ChangeCents, "Change must map.");
        RequireEqual(1, draft.Payments.Count, "Payments must map.");
        RequireEqual(PaymentMethod.Cash, draft.Payments[0].Method, "Payment method must map.");
        RequireEqual(10000L, draft.Payments[0].AmountCents, "Payment amount must map.");
        RequireEqual("VAT_BUCKET_ENGINE_DEFERRED", draft.TaxMode.TaxModeCode, "Tax mode code must normalize.");
        RequireEqual(true, draft.TaxMode.VatBucketEngineDeferred, "VAT bucket engine must remain deferred.");
        RequireEqual(true, draft.DeferredComplianceNotes.Any(note => note.Contains("No official document rendering.", StringComparison.Ordinal)), "Deferred notes must preserve no-rendering guard.");

        Result<OfficialDocumentSnapshotDraft> invalidProfile = builder.Build(new OfficialDocumentSnapshotDraftBuildRequest(
            OfficialDocumentSnapshotDraftKind.OfficialReceipt,
            new OfficialDocumentSnapshotDraftMachineProfileInput(
                RegisteredTaxpayerName: "",
                BusinessName: null,
                TaxpayerIdentificationNumber: "000-000-000-000",
                BranchCode: "00000",
                RegisteredBusinessAddress: "Test Registered Address",
                MachineSerialNumber: null,
                MachineIdentificationNumber: null,
                AccreditationNumber: null,
                PermitToUseNumber: null),
            sale,
            new[] { item },
            new[] { payment },
            AllocatedDocumentNumber: null,
            CashierIdentifier: "cashier@test",
            TerminalIdentifier: null,
            TaxModeCode: "VAT_BUCKET_ENGINE_DEFERRED",
            TaxModeNotes: null));

        RequireFailureCode(
            invalidProfile,
            "OfficialDocumentDraft.TaxpayerNameRequired",
            "Builder must reject missing taxpayer name.");
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
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialXReadingFoundationChecks
{
    private static readonly Guid MachineProfileId = Guid.Parse("21111111-1111-1111-1111-111111111111");
    private static readonly Guid SequenceId = Guid.Parse("21111111-1111-1111-1111-111111111112");
    private static readonly Guid CashierUserId = Guid.Parse("21111111-1111-1111-1111-111111111113");

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string xReadingText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialXReadingService.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(xReadingText, "IOfficialXReadingService", "X-reading foundation must expose a Core service contract.");
        RequireContains(xReadingText, "IOfficialEvidenceReadService", "X-reading must consume official evidence read service.");
        RequireContains(xReadingText, "EvidenceHashMismatch", "X-reading must fail closed on evidence hash mismatch.");
        RequireContains(xReadingText, "OfficialXReadingDocumentRange", "X-reading must expose official document range.");
        RequireContains(xReadingText, "OfficialXReadingPaymentSummary", "X-reading must expose payment summaries.");
        RequireContains(xReadingText, "OfficialXReadingTaxModeSummary", "X-reading must expose tax-mode summaries.");
        RequireNotContains(xReadingText, "receipt_snapshot", "X-reading must not read sales.receipt_snapshot.");
        RequireNotContains(xReadingText, "LocalHostCompleteSale", "X-reading must not wire Local Host complete-sale.");
        RequireNotContains(xReadingText, "Crystal", "X-reading must not wire reporting output.");
        RequireNotContains(xReadingText, "ESC/POS", "X-reading must not wire printer output.");
        RequireContains(appServicesText, "public IOfficialXReadingService OfficialXReading { get; }", "AppServices must expose X-reading through the Core contract.");
        RequireContains(appServicesText, "new OfficialXReadingService(OfficialEvidenceRead)", "AppServices must compose X-reading over official evidence.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialXReadingFoundationChecks.Run();", "CoreSmoke must run X-reading foundation checks.");

        VerifyRuntimeAggregationAndGuards();
    }

    private static void VerifyRuntimeAggregationAndGuards()
    {
        OfficialEvidenceExportResult evidence = BuildEvidenceExport(
            BuildEvidenceEntry("OR-2026-000001", 1000, 100, 50, 120, 1070, new[] { new SnapshotPayment(PaymentMethod.Cash, 1070) }),
            BuildEvidenceEntry("OR-2026-000002", 2000, 0, 0, 0, 2000, new[] { new SnapshotPayment(PaymentMethod.Cash, 1500), new SnapshotPayment(PaymentMethod.Cash, 500) }));

        OfficialXReadingService service = new(new FakeOfficialEvidenceReadService(evidence));
        Result<OfficialXReadingResult> result =
            service.ReadAsync(CreateRequest()).GetAwaiter().GetResult();

        RequireSuccess(result, "X-reading runtime aggregation should succeed.");
        RequireEqual(2, result.Value.Summary.EntryCount, "X-reading must count official evidence entries.");
        RequireEqual(3150L, result.Value.Summary.GrossSalesCents, "X-reading gross total must be evidence-derived.");
        RequireEqual(3000L, result.Value.Summary.SubtotalCents, "X-reading subtotal must be evidence-derived.");
        RequireEqual(150L, result.Value.Summary.DiscountTotalCents, "X-reading discount total must include line and sale discounts.");
        RequireEqual(120L, result.Value.Summary.TaxTotalCents, "X-reading tax total must be evidence-derived.");
        RequireEqual(2950L, result.Value.Summary.NetSalesCents, "X-reading net sales must be evidence-derived.");
        RequireEqual(3070L, result.Value.Summary.TotalSalesCents, "X-reading total sales must be evidence-derived.");
        RequireEqual("OR-2026-000001", result.Value.DocumentRange?.FirstOfficialDocumentNumber, "X-reading first document number must be deterministic.");
        RequireEqual("OR-2026-000002", result.Value.DocumentRange?.LastOfficialDocumentNumber, "X-reading last document number must be deterministic.");
        RequireEqual(1, result.Value.Payments.Count, "X-reading must summarize payment methods deterministically.");
        RequireEqual("Cash", result.Value.Payments[0].Method, "X-reading payment order must be deterministic.");
        RequireEqual(3070L, result.Value.Payments[0].AmountCents, "X-reading cash total must be evidence-derived.");
        RequireEqual(3, result.Value.Payments[0].PaymentCount, "X-reading must count payment evidence rows.");
        RequireEqual(1, result.Value.TaxModes.Count, "X-reading must summarize tax mode notes without inventing VAT buckets.");
        RequireTrue(result.Value.TaxModes[0].VatBucketEngineDeferred, "X-reading must preserve deferred VAT-bucket status.");

        Result<OfficialXReadingResult> emptyResult =
            new OfficialXReadingService(new FakeOfficialEvidenceReadService(BuildEvidenceExport()))
                .ReadAsync(CreateRequest()).GetAwaiter().GetResult();

        RequireSuccess(emptyResult, "Empty X-reading must succeed.");
        RequireEqual(0, emptyResult.Value.Summary.EntryCount, "Empty X-reading must have zero entries.");
        RequireEqual(null, emptyResult.Value.DocumentRange, "Empty X-reading must not invent a document range.");
        RequireEqual(0L, emptyResult.Value.Summary.TotalSalesCents, "Empty X-reading must have zero totals.");

        OfficialEvidenceExportResult mismatchedEvidence = evidence with
        {
            Manifest = evidence.Manifest with { AllSnapshotHashesMatch = false },
            Entries = evidence.Entries.Select((entry, index) => index == 0
                ? entry with { SnapshotHashMatches = false }
                : entry).ToArray()
        };

        ExpectFailure(
            new OfficialXReadingService(new FakeOfficialEvidenceReadService(mismatchedEvidence))
                .ReadAsync(CreateRequest()).GetAwaiter().GetResult(),
            "OfficialXReading.EvidenceHashMismatch");

        ExpectFailure(
            service.ReadAsync(CreateRequest() with
            {
                RangeStartedAt = CreateRequest().RangeEndedAt,
                RangeEndedAt = CreateRequest().RangeStartedAt
            }).GetAwaiter().GetResult(),
            "OfficialXReading.InvalidDateRange");

        ExpectFailure(
            service.ReadAsync(CreateRequest() with
            {
                BirMachineProfileId = null,
                RegistrationScopeCode = null
            }).GetAwaiter().GetResult(),
            "OfficialXReading.MachineProfileOrScopeRequired");

        ExpectFailure(
            service.ReadAsync(CreateRequest() with
            {
                DocumentKind = (ElectronicJournalDocumentKind)999
            }).GetAwaiter().GetResult(),
            "OfficialXReading.UnsupportedDocumentKind");
    }

    private static OfficialXReadingRequest CreateRequest()
    {
        return new OfficialXReadingRequest(
            RangeStartedAt: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            RangeEndedAt: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            BirMachineProfileId: MachineProfileId,
            RegistrationScopeCode: null,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt);
    }

    private static OfficialEvidenceExportResult BuildEvidenceExport(params OfficialEvidenceEntryReadModel[] entries)
    {
        Result<OfficialEvidenceExportResult> result =
            new OfficialEvidenceExportBuilder().Build(
                new OfficialEvidenceReadRequest(
                    new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                    MachineProfileId,
                    null,
                    ElectronicJournalDocumentKind.OfficialReceipt),
                entries,
                new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero));

        RequireSuccess(result, "Evidence export fixture should build.");

        return result.Value;
    }

    private static OfficialEvidenceEntryReadModel BuildEvidenceEntry(
        string documentNumber,
        long subtotalCents,
        long lineDiscountCents,
        long saleDiscountCents,
        long taxCents,
        long totalCents,
        IReadOnlyList<SnapshotPayment> payments)
    {
        Result<ElectronicJournalSnapshotEvidence> evidenceResult =
            new OfficialDocumentSnapshotEvidenceBuilder().Build(new OfficialDocumentSnapshotDraft(
                DocumentKind: OfficialDocumentSnapshotDraftKind.OfficialReceipt,
                Header: new OfficialDocumentSnapshotDraftHeader(
                    RegisteredTaxpayerName: "Kirjane Labs Test Taxpayer",
                    BusinessName: "Salryn Test Store",
                    TaxpayerIdentificationNumber: "000-000-000-000",
                    BranchCode: "00001",
                    RegisteredBusinessAddress: "Test Address",
                    TransactionDateTime: new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero),
                    DocumentNumber: documentNumber,
                    MachineSerialNumber: "SERIAL-X",
                    MachineIdentificationNumber: "MIN-X",
                    AccreditationNumber: "ACC-X",
                    PermitToUseNumber: "PTU-X",
                    CashierIdentifier: "cashier-x",
                    TerminalIdentifier: "POS-X"),
                Lines: new[]
                {
                    new OfficialDocumentSnapshotDraftLine(
                        Description: "X-reading evidence product",
                        Barcode: "4900000000000",
                        UnitName: "pc",
                        QuantityX1000: 1000,
                        UnitPriceCents: checked(subtotalCents + lineDiscountCents),
                        DiscountCents: lineDiscountCents,
                        LineTotalCents: subtotalCents)
                },
                Amounts: new OfficialDocumentSnapshotDraftAmounts(
                    SubtotalCents: subtotalCents,
                    DiscountCents: saleDiscountCents,
                    TaxCents: taxCents,
                    TotalCents: totalCents,
                    AmountPaidCents: totalCents,
                    ChangeCents: 0),
                Payments: payments
                    .Select(payment => new OfficialDocumentSnapshotDraftPayment(
                        payment.Method,
                        payment.AmountCents,
                        new DateTimeOffset(2026, 6, 25, 9, 1, 0, TimeSpan.Zero)))
                    .ToArray(),
                TaxMode: new OfficialDocumentSnapshotDraftTaxMode(
                    TaxModeCode: "VAT_DEFERRED",
                    VatBucketEngineDeferred: true,
                    Notes: "Detailed VAT bucket classification deferred."),
                DeferredComplianceNotes: new[] { "No VAT bucket engine." }));

        RequireSuccess(evidenceResult, "Snapshot evidence fixture should build.");

        return new OfficialEvidenceEntryReadModel(
            JournalEntryId: Guid.NewGuid(),
            SaleId: Guid.NewGuid(),
            BirMachineProfileId: MachineProfileId,
            DocumentKind: ElectronicJournalDocumentKind.OfficialReceipt,
            OfficialDocumentNumber: documentNumber,
            OfficialDocumentSequenceId: SequenceId,
            SnapshotPayloadJson: evidenceResult.Value.PayloadJson,
            SnapshotPayloadHash: evidenceResult.Value.PayloadHash,
            SnapshotHashAlgorithm: evidenceResult.Value.HashAlgorithm,
            SnapshotHashMatches: true,
            RecomputedSnapshotPayloadHash: evidenceResult.Value.PayloadHash,
            OriginalEventId: Guid.NewGuid(),
            AppendedAt: new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero).AddMinutes(int.Parse(documentNumber[^1..])),
            CashierUserId: CashierUserId,
            TerminalId: "POS-X");
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

    private static void ExpectFailure(Result<OfficialXReadingResult> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected X-reading failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected X-reading validation code.");
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
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    private sealed record SnapshotPayment(PaymentMethod Method, long AmountCents);

    private sealed class FakeOfficialEvidenceReadService : IOfficialEvidenceReadService
    {
        private readonly OfficialEvidenceExportResult _result;

        public FakeOfficialEvidenceReadService(OfficialEvidenceExportResult result)
        {
            _result = result;
        }

        public Task<Result<OfficialEvidenceExportResult>> ExportAsync(
            OfficialEvidenceReadRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result<OfficialEvidenceExportResult>.Success(_result));
        }
    }
}

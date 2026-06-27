using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileTrustedPackageProducerChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SaleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MachineProfileId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SequenceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CashierUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OriginalEventId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 27, 10, 0, 0, TimeSpan.FromHours(8));

    public static async Task RunAsync()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string producer = Read(root, "src", "Salryn.Core", "Services", "OfficialOutputTextFileTrustedPackageProducer.cs");
        string supplyBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFilePackageSupplyBoundary.cs");
        string uiBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "OfficialOutput", "OfficialOutputTextFileWorkflowUiBoundary.cs");
        string appServices = Read(root, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string checkoutPresenter = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "Checkout" + "Presenter.cs");
        string checkoutBoundary = Read(root, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutLocalHostProductSearchBoundary.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(
            producer,
            supplyBoundary,
            uiBoundary,
            appServices,
            checkoutPresenter,
            checkoutBoundary,
            coreSmoke);
        await VerifyUnavailableSourceFailsSafelyAsync();
        await VerifyValidTrustedSourceBuildsDeterministicPackageAsync();
    }

    private static void VerifySourceGuards(
        string producer,
        string supplyBoundary,
        string uiBoundary,
        string appServices,
        string checkoutPresenter,
        string checkoutBoundary,
        string coreSmoke)
    {
        RequireContains(producer, "public interface IOfficialOutputTextFileTrustedPackageProducer", "RMO-M1-A producer interface must exist.");
        RequireContains(producer, "public sealed class OfficialOutputTextFileTrustedPackageProducer", "RMO-M1-A producer implementation must exist.");
        RequireContains(producer, "IOfficialOutputSourceReadService", "RMO-M1-A producer must depend on official output source evidence.");
        RequireContains(producer, "OfficialOutputSourceReadRequest", "RMO-M1-A producer must require an official output source request.");
        RequireContains(producer, "OfficialPrintableDocumentBuilder", "RMO-M1-A producer must build printable DTOs from official source.");
        RequireContains(producer, "OfficialReportBindingBuilder", "RMO-M1-A producer must build report binding data.");
        RequireContains(producer, "OfficialTextPreviewReportRenderer", "RMO-M1-A producer must build text preview artifacts.");
        RequireContains(producer, "OfficialOutputArtifactManifestBuilder", "RMO-M1-A producer must build the manifest package.");
        RequireContains(producer, "OfficialOutputExportPackageBuilder", "RMO-M1-A producer must build the export package.");
        RequireContains(producer, "OfficialOutputArtifactManifestBuilder.FromReceiptInvoicePreview", "RMO-M1-A producer must manifest receipt/invoice preview artifacts.");
        RequireContains(producer, "OfficialOutputExportPackageBuilder.FromReceiptInvoicePreview", "RMO-M1-A producer must package receipt/invoice preview payloads.");
        RequireContains(producer, "OfficialOutputTextFileTrustedPackageProducer.SourceUnavailable", "RMO-M1-A producer must fail safely when official source is unavailable.");
        RequireContains(producer, "OfficialOutputSourceRead", "RMO-M1-A producer metadata must identify the official output source.");

        RequireDoesNotContain(producer, "Checkout" + "Presenter", "RMO-M1-A producer must not depend on checkout presenter state.");
        RequireDoesNotContain(producer, "CheckoutLocalHostProductSearchBoundary", "RMO-M1-A producer must not depend on checkout UI/search state.");
        RequireDoesNotContain(producer, "IAppOfficialOutputTextFileWorkflowGateway", "RMO-M1-A producer must not execute the text-file workflow.");
        RequireDoesNotContain(producer, "OfficialExternalOutputTextFileWorkflowRunner", "RMO-M1-A producer must not call the external workflow runner.");
        RequireDoesNotContain(producer, "DispatchAsync", "RMO-M1-A producer must not dispatch Local Host output.");

        RequireContains(supplyBoundary, "IOfficialOutputTextFileTrustedPackageProducer? _trustedPackageProducer", "RMO-M1-A supply boundary must hold the trusted package producer.");
        RequireContains(supplyBoundary, "OfficialOutputTextFileTrustedPackageProducerRequest? _producerRequest", "RMO-M1-A supply boundary must hold the official source request.");
        RequireContains(supplyBoundary, "FromTrustedPackageProducer", "RMO-M1-A supply boundary must expose producer-backed supply creation.");
        RequireContains(supplyBoundary, "GetAvailablePackageSummaryAsync", "RMO-M1-A supply boundary must expose async summary behavior.");
        RequireContains(supplyBoundary, "TryGetPackageAsync", "RMO-M1-A supply boundary must expose async package supply.");
        RequireContains(supplyBoundary, "TryBuildProducerRequest(selectedSourceRequest)", "RMO-M1-B3 supply boundary must resolve the producer request before production.");
        RequireContains(supplyBoundary, "producer.ProduceAsync(producerRequest.Value, cancellationToken)", "RMO-M1-A/B3 supply boundary must use the producer with the resolved producer request.");
        RequireContains(supplyBoundary, "sourceSelectionBoundary.SelectSource(selectedSourceRequest)", "RMO-M1-B3 supply boundary must convert selected source requests through the source selection boundary.");
        RequireContains(supplyBoundary, "OfficialOutputTextFilePackageSupply.ProducerRequestRequired", "RMO-M1-A supply boundary must stay unavailable when no source request exists.");

        RequireContains(uiBoundary, "TryGetSuppliedPackageAsync", "RMO-M1-A UI shell must use async package supply before execution.");
        RequireContains(uiBoundary, "packageSupplyBoundary.TryGetPackageAsync(selectedSourceRequest, cancellationToken)", "RMO-M1-A/B3 UI shell must obtain produced packages through supply boundary with the selected source request.");
        RequireDoesNotContain(uiBoundary, "request?.ExportPackage", "RMO-M1-A UI shell must not accept package data from UI state.");

        RequireContains(appServices, "public IOfficialOutputTextFileTrustedPackageProducer OfficialOutputTextFileTrustedPackageProducer { get; }", "RMO-M1-A AppServices must expose the trusted package producer.");
        RequireContains(appServices, "new OfficialOutputTextFileTrustedPackageProducer(OfficialOutputSourceRead)", "RMO-M1-A AppServices must wire producer to official output source.");
        RequireContains(appServices, "trustedPackageProducer: OfficialOutputTextFileTrustedPackageProducer", "RMO-M1-A package supply must be connected to the producer.");

        RequireDoesNotContain(checkoutPresenter, "OfficialOutputTextFileTrustedPackageProducer", "RMO-M1-A must not wire package production into checkout presenter.");
        RequireDoesNotContain(checkoutBoundary, "OfficialOutputTextFileTrustedPackageProducer", "RMO-M1-A must not wire package production into checkout product search.");

        foreach (string text in new[] { producer, supplyBoundary, uiBoundary })
        {
            foreach (string marker in ForbiddenMarkers())
            {
                RequireDoesNotContain(text, marker, "RMO-M1-A source must not contain forbidden marker: " + marker);
            }
        }

        foreach (string marker in RawOutputExposureMarkers())
        {
            RequireDoesNotContain(uiBoundary, marker, "RMO-M1-A UI shell must not expose raw output payload data: " + marker);
            RequireDoesNotContain(supplyBoundary, marker, "RMO-M1-A package supply summary must not expose raw output payload data: " + marker);
        }

        RequireContains(coreSmoke, "J22OfficialOutputTextFileTrustedPackageProducerChecks.RunAsync().GetAwaiter().GetResult();", "CoreSmoke must run RMO-M1-A trusted package producer checks.");
        RequireDoesNotContain(coreSmoke, "TryRunAsync(", "CoreSmoke must not execute official output text-file workflow UI commands.");
    }

    private static async Task VerifyUnavailableSourceFailsSafelyAsync()
    {
        OfficialOutputTextFileTrustedPackageProducer producer = new(
            new FakeOfficialOutputSourceReadService(Result<OfficialOutputSourceResult>.Fail(Error.NotFound(
                "FakeOfficialOutputSource.NotFound",
                "No official output source is available."))));

        Result<OfficialOutputExportPackage> result =
            await producer.ProduceAsync(CreateRequest());

        if (result.IsSuccess ||
            result.ErrorDetail.Code != "OfficialOutputTextFileTrustedPackageProducer.SourceUnavailable")
        {
            throw new InvalidOperationException("RMO-M1-A producer must fail safely when official source is unavailable.");
        }
    }

    private static async Task VerifyValidTrustedSourceBuildsDeterministicPackageAsync()
    {
        OfficialOutputSourceResult source = CreateOfficialSourceResult();
        OfficialOutputTextFileTrustedPackageProducer producer = new(
            new FakeOfficialOutputSourceReadService(Result<OfficialOutputSourceResult>.Success(source)));

        OfficialOutputTextFileTrustedPackageProducerRequest request = CreateRequest();

        Result<OfficialOutputExportPackage> firstResult =
            await producer.ProduceAsync(request);
        Result<OfficialOutputExportPackage> secondResult =
            await producer.ProduceAsync(request);

        OfficialOutputExportPackage first = RequireValue(firstResult);
        OfficialOutputExportPackage second = RequireValue(secondResult);

        RequireEqual(OfficialOutputExportPackageKind.PreviewTextBundle, first.PackageKind, "RMO-M1-A package kind must remain the preview text bundle.");
        RequireEqual(1, first.EntryCount, "RMO-M1-A package must contain the receipt/invoice manifest entry.");
        RequireEqual(1, first.PayloadCount, "RMO-M1-A package must contain the receipt/invoice payload.");
        RequireEqual(1, first.Items.Count, "RMO-M1-A package items must contain one receipt/invoice item.");
        RequireEqual(OfficialOutputArtifactFamily.ReceiptInvoice, first.Items[0].Payload.ArtifactFamily, "RMO-M1-A package must use receipt/invoice artifact family.");
        RequireStartsWith(first.PackageReference, "salryn://official-output-export-package/", "RMO-M1-A package reference must use the official export package prefix.");
        RequireEqual("salryn://official-output-export-package/" + first.PackageHash, first.PackageReference, "RMO-M1-A package reference must be hash-addressed.");
        RequireEqual(first.PackageHash, second.PackageHash, "RMO-M1-A package hash must be deterministic for the same trusted source.");
        RequireEqual(first.PackageReference, second.PackageReference, "RMO-M1-A package reference must be deterministic for the same trusted source.");
        RequireEqual(first.TextResult.Text, second.TextResult.Text, "RMO-M1-A package text must be deterministic for the same trusted source.");
        RequireContains(first.ManifestPackage.TextResult.Text, "SourceKind=OfficialOutputSourceRead", "RMO-M1-A manifest must identify the official output source.");
        RequireContains(first.ManifestPackage.TextResult.Text, "OfficialDocumentNumber=OR-2026-000001", "RMO-M1-A manifest must carry official document identity.");
    }

    private static OfficialOutputTextFileTrustedPackageProducerRequest CreateRequest()
    {
        return new OfficialOutputTextFileTrustedPackageProducerRequest(
            SourceReadRequest: new OfficialOutputSourceReadRequest(
                JournalEntryId,
                OfficialDocumentNumber: null,
                DocumentKind: null,
                BirMachineProfileId: null,
                RegistrationScopeCode: null),
            RequestedAt: Timestamp,
            RequestedByUserId: CashierUserId,
            TerminalId: "POS-TRUSTED-01",
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["phase"] = "RMO-M1-A"
            });
    }

    private static OfficialOutputSourceResult CreateOfficialSourceResult()
    {
        OfficialOutputDocumentHeader header = new(
            JournalEntryId,
            SaleId,
            MachineProfileId,
            SequenceId,
            ElectronicJournalDocumentKind.OfficialReceipt,
            "OR-2026-000001",
            Timestamp,
            CashierUserId,
            "cashier-trusted",
            "POS-TRUSTED-01",
            "POS-TRUSTED-01");

        OfficialOutputMachineProfile machine = new(
            MachineProfileId,
            "LOCAL_SINGLE_STORE",
            "Kirch Test Taxpayer",
            "Kirch Test Store",
            "123 Test Street",
            "Branch 001",
            "123-456-789-000",
            "001",
            "MIN-001",
            "MSN-001",
            "POS-001",
            "ACC-001",
            "PTU-001",
            "Salryn",
            "1.0.0",
            "build-test",
            "FOUNDATION");

        OfficialOutputEvidence evidence = new(
            OriginalEventId,
            "{\"schema\":\"fixture\"}",
            "snapshot-hash",
            "snapshot-hash",
            "SHA-256",
            SnapshotHashMatches: true);

        OfficialOutputLifecycle lifecycle = new(
            OfficialDocumentLifecycleState.Active,
            CorrectionCount: 0,
            CorrectionNotes: Array.Empty<string>(),
            IsCoveredByFinalizedZReading: false,
            CoveringZReadingId: null,
            Issues: Array.Empty<OfficialDocumentLifecycleIssue>(),
            Events: new[]
            {
                new OfficialDocumentLifecycleEventReadModel(
                    OriginalEventId,
                    "ORIGINAL",
                    OriginalMarker: true,
                    ReprintMarker: false,
                    VoidMarker: false,
                    RefundMarker: false,
                    CancelMarker: false,
                    CashierUserId,
                    Reason: null,
                    CreatedAt: Timestamp,
                    TerminalId: "POS-TRUSTED-01",
                    MetadataJson: null)
            });

        return new OfficialOutputSourceResult(
            header,
            new[]
            {
                new OfficialOutputLineItem(
                    "Trusted Milk",
                    "480000000001",
                    "PCS",
                    QuantityX1000: 1000,
                    UnitPriceCents: 12000,
                    DiscountCents: 0,
                    LineTotalCents: 12000)
            },
            new[]
            {
                new OfficialOutputPayment(
                    PaymentMethod.Cash,
                    AmountCents: 12000,
                    PaidAt: Timestamp)
            },
            new OfficialOutputTotals(
                SubtotalCents: 12000,
                DiscountCents: 0,
                TaxCents: 0,
                TotalCents: 12000,
                AmountPaidCents: 12000,
                ChangeCents: 0,
                TaxModeCode: "VAT_DEFERRED",
                VatBucketEngineDeferred: true,
                TaxModeNotes: "Deferred VAT bucket foundation.",
                DeferredComplianceNotes: new[] { "VAT bucket engine deferred." }),
            machine,
            evidence,
            lifecycle);
    }

    private static IEnumerable<string> RawOutputExposureMarkers()
    {
        yield return "Text" + "Result";
        yield return "Payload" + "Text";
        yield return "Written" + "File";
        yield return "Written" + "Artifact";
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "." + "rpt";
        yield return "System.Windows." + "Forms";
        yield return "Microsoft.Data." + "Sql" + "ite";
        yield return "Sql" + "ite";
        yield return "SQL" + "ite";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
        yield return "System.Drawing." + "Printing";
        yield return "Pdf" + "Document";
        yield return "Export" + "ToDisk";
        yield return "Checkout" + "Presenter";
        yield return "Official" + "Checkout";
        yield return "Electronic" + "Journal" + "Append";
        yield return "Official" + "Document" + "Lifecycle";
        yield return "Official" + "ZReading" + "Service";
        yield return "Finalize" + "Async";
        yield return "Save" + "Async";
        yield return "IN" + "SERT";
        yield return "UP" + "DATE";
        yield return "DE" + "LETE";
    }

    private static T RequireValue<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException("Expected RMO-M1-A success, got " + result.ErrorDetail.Code + ": " + result.Error);
        }

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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;
        foreach (string part in parts) path = Path.Combine(path, part);
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

    private static void RequireStartsWith(string text, string value, string message)
    {
        if (!text.StartsWith(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message + " Expected: " + expected + ". Actual: " + actual + ".");
        }
    }

    private sealed class FakeOfficialOutputSourceReadService : IOfficialOutputSourceReadService
    {
        private readonly Result<OfficialOutputSourceResult> _result;

        public FakeOfficialOutputSourceReadService(Result<OfficialOutputSourceResult> result)
        {
            _result = result;
        }

        public Task<Result<OfficialOutputSourceResult>> ReadAsync(
            OfficialOutputSourceReadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}

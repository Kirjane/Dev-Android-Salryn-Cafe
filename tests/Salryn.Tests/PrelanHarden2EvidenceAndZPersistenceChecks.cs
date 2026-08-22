using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class PrelanHarden2EvidenceAndZPersistenceChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string producerSource = Read(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialOutputTextFileTrustedPackageProducer.cs");
        string appServicesSource = Read(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");
        string zServiceSource = Read(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialZReadingService.cs");
        string zSqliteSource = Read(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteOfficialZReadingService.cs");
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(producerSource, "OfficialEngineeringEvidencePackageAssembler", "Trusted producer must wire engineering evidence assembler.");
        RequireContains(producerSource, "FromEngineeringEvidencePackage", "Trusted producer must export engineering evidence artifacts.");
        RequireContains(appServicesSource, "OfficialEngineeringEvidencePackageAssembler", "AppServices must wire engineering evidence assembler.");
        RequireContains(zServiceSource, "OfficialZReadingEnrichmentPayload", "Z-reading must persist enrichment payload.");
        RequireContains(zSqliteSource, "metadata_json", "Z-reading SQLite persistence must store enrichment metadata.");
        RequireContains(coreSmoke, "PrelanHarden2EvidenceAndZPersistenceChecks.Run();", "CoreSmoke must run PRELAN-HARDEN-2 guard.");

        foreach (string marker in ForbiddenOutputMarkers())
        {
            RequireNotContains(producerSource, marker, "PRELAN-HARDEN-2 must not introduce forbidden output stack marker: " + marker);
        }

        foreach (string marker in ForbiddenLanMarkers())
        {
            RequireNotContains(producerSource, marker, "PRELAN-HARDEN-2 must not introduce LAN marker: " + marker);
            RequireNotContains(appServicesSource, marker, "PRELAN-HARDEN-2 must not introduce LAN marker: " + marker);
        }

        RequireEngineeringEvidencePackageWiring();
        RequireEnrichedZPersistenceRoundTrip();
        RequirePersistedZReportUsesEnrichment();
    }

    private static void RequireEngineeringEvidencePackageWiring()
    {
        OfficialOutputSourceResult source = CreateSourceResult();
        OfficialEngineeringEvidencePackageAssembler assembler = new();

        Result<OfficialEngineeringEvidencePackageResult> result =
            assembler.AssembleAsync(source).GetAwaiter().GetResult();

        RequireSuccess(result, "Engineering evidence assembler should succeed.");

        if (!result.Value.PackageText.Contains("NON_CERTIFICATION_FOOTER", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Engineering evidence package must include non-certification footer.");
        }

        if (!result.Value.PackageText.Contains("VAT_BUCKETS=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Engineering evidence package must include VAT bucket section.");
        }

        if (!result.Value.PackageText.Contains("LIFECYCLE_EVENTS=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Engineering evidence package must include lifecycle section.");
        }
    }

    private static void RequireEnrichedZPersistenceRoundTrip()
    {
        OfficialXReadingResult xReading = new(
            new OfficialXReadingSummary(
                new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                null,
                ElectronicJournalDocumentKind.OfficialReceipt,
                2,
                3150,
                3000,
                150,
                120,
                2950,
                3070,
                "manifest-hash",
                true),
            new OfficialXReadingDocumentRange("OR-1", "OR-2"),
            Array.Empty<OfficialXReadingPaymentSummary>(),
            Array.Empty<OfficialXReadingTaxModeSummary>(),
            new OfficialXReadingLifecycleSummary(1, 2, 3, 4, 5),
            new[] { new OfficialXReadingVatBucketSummary("Vatable", 3000, 120, 2) },
            new OfficialXReadingAdjustedSummary(2, 3150, 3000, 150, 120, 3070));

        OfficialZReadingEnrichmentPayload enrichment = new(
            OfficialZReadingEnrichmentPayload.SchemaV1,
            xReading.Lifecycle,
            xReading.AdjustedSummary,
            xReading.VatBuckets);

        string metadataJson = OfficialZReadingEnrichmentSerializer.Serialize(enrichment);
        Result<OfficialZReadingEnrichmentPayload?> roundTrip =
            OfficialZReadingEnrichmentSerializer.TryDeserialize(metadataJson);

        RequireSuccess(roundTrip, "Enrichment serializer round-trip should succeed.");
        RequireEqual(5, roundTrip.Value!.Lifecycle.RefundCount, "Refund count must round-trip.");
        RequireEqual(1, roundTrip.Value.VatBuckets.Count, "VAT bucket count must round-trip.");
        RequireEqual(3070L, roundTrip.Value.AdjustedSummary.TotalSalesCents, "Adjusted total must round-trip.");

        Result<OfficialZReadingEnrichmentPayload?> legacy =
            OfficialZReadingEnrichmentSerializer.TryDeserialize(null);

        RequireSuccess(legacy, "Legacy null metadata must deserialize safely.");
        if (legacy.Value is not null)
        {
            throw new InvalidOperationException("Legacy null metadata must remain base-only.");
        }
    }

    private static void RequirePersistedZReportUsesEnrichment()
    {
        OfficialZReadingSummary summary = new(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffff0001"),
            new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 26, 0, 5, 0, TimeSpan.Zero),
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            null,
            ElectronicJournalDocumentKind.OfficialReceipt,
            2,
            "OR-1",
            "OR-2",
            3150,
            3000,
            150,
            120,
            2950,
            3070,
            "manifest-hash",
            true,
            null,
            "POS-1");

        OfficialZReadingPersistedRecord record = new(
            summary,
            Array.Empty<OfficialXReadingPaymentSummary>(),
            new[]
            {
                new OfficialXReadingTaxModeSummary("VAT", 2, 3000, 150, 120, 3070, false, null)
            },
            new OfficialZReadingEnrichmentPayload(
                OfficialZReadingEnrichmentPayload.SchemaV1,
                new OfficialXReadingLifecycleSummary(1, 2, 3, 4, 5),
                new OfficialXReadingAdjustedSummary(2, 3150, 3000, 150, 120, 3070),
                new[] { new OfficialXReadingVatBucketSummary("Vatable", 3000, 120, 2) }));

        OfficialReadingReportInput report = OfficialZReadingPersistedReportInputBuilder.Build(record);

        if (report.SalesTotals?.VoidedCount != 2)
        {
            throw new InvalidOperationException("Z report input must use enriched void count.");
        }

        if (report.SalesTotals?.CorrectedCount != 4)
        {
            throw new InvalidOperationException("Z report input must use enriched correction count.");
        }

        if (report.TaxTotals?.DeferredComplianceNotes is null ||
            report.TaxTotals.DeferredComplianceNotes.Count == 0)
        {
            throw new InvalidOperationException("Z report input must surface enriched VAT bucket notes.");
        }
    }

    private static OfficialOutputSourceResult CreateSourceResult()
    {
        Guid journalEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        string snapshotJson = """
            {
              "schema": "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1",
              "documentKind": "OfficialReceipt",
              "header": {
                "documentNumber": "OR-2026-000001",
                "cashierIdentifier": "cashier",
                "terminalIdentifier": "POS-1"
              },
              "lines": [],
              "totals": {
                "subtotalCents": 1000,
                "discountCents": 0,
                "taxCents": 120,
                "totalCents": 1120,
                "amountPaidCents": 1120,
                "changeCents": 0
              },
              "payments": [],
              "taxMode": {
                "taxModeCode": "VAT",
                "vatBucketEngineDeferred": false
              },
              "taxBuckets": {
                "taxpayerMode": "VatRegistered",
                "nonVatStatement": "",
                "buckets": [
                  { "bucketKind": "Vatable", "salesAmountCents": 1000, "taxAmountCents": 120 }
                ],
                "deferredComplianceNotes": []
              }
            }
            """;

        return new OfficialOutputSourceResult(
            new OfficialOutputDocumentHeader(
                journalEntryId,
                Guid.NewGuid(),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.NewGuid(),
                ElectronicJournalDocumentKind.OfficialReceipt,
                "OR-2026-000001",
                new DateTimeOffset(2026, 6, 27, 10, 0, 0, TimeSpan.Zero),
                Guid.NewGuid(),
                "cashier",
                "POS-1",
                "POS-1"),
            Array.Empty<OfficialOutputLineItem>(),
            Array.Empty<OfficialOutputPayment>(),
            new OfficialOutputTotals(1000, 0, 120, 1120, 1120, 0, "VAT", false, null, Array.Empty<string>()),
            new OfficialOutputMachineProfile(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "LOCAL",
                "Taxpayer",
                "Store",
                "Address",
                null,
                "123",
                "001",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            new OfficialOutputEvidence(
                Guid.NewGuid(),
                snapshotJson,
                "hash",
                "hash",
                "SHA-256",
                true),
            new OfficialOutputLifecycle(
                OfficialDocumentLifecycleState.Active,
                0,
                Array.Empty<string>(),
                false,
                null,
                Array.Empty<OfficialDocumentLifecycleIssue>(),
                Array.Empty<OfficialDocumentLifecycleEventReadModel>()));
    }

    private static IEnumerable<string> ForbiddenOutputMarkers()
    {
        yield return "Crystal";
        yield return "ESC/POS";
        yield return "EscPos";
        yield return "PdfSharp";
        yield return "iText";
    }

    private static IEnumerable<string> ForbiddenLanMarkers()
    {
        yield return "SignalR";
        yield return "WebSocket";
        yield return "pairing token";
        yield return "Host Server";
        yield return "Client Terminal";
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
            throw new InvalidOperationException(message + " " + (result.ErrorDetail?.Message ?? result.Error?.ToString()));
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

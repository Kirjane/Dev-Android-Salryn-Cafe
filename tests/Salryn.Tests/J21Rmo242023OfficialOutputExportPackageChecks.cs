using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialOutputExportPackageChecks
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 6, 26, 15, 30, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string packageText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputExportPackageBuilder.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(packageText, coreProjectText, coreSmokeText);
        VerifyPackageIncludesAllPayloadFamilies();
        VerifyPackageCountsAndMarkers();
        VerifyPackageDeterminismAndHashBehavior();
        VerifyDeterministicItemOrder();
        VerifyFactoryPreservation();
        VerifyValidationFailures();
    }

    private static void VerifySourceGuards(
        string packageText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(packageText, "OfficialOutputExportPackageKind", "09L must expose export package kind.");
        RequireContains(packageText, "OfficialOutputExportPayload", "09L must expose export payload.");
        RequireContains(packageText, "OfficialOutputExportPackageItem", "09L must expose export package item.");
        RequireContains(packageText, "OfficialOutputExportPackage", "09L must expose export package.");
        RequireContains(packageText, "OfficialOutputExportPackageBuilder", "09L must expose export package builder.");
        RequireContains(packageText, "OfficialOutputExportPackageTextRenderResult", "09L must expose export package text result.");
        RequireContains(packageText, "OfficialOutputExportPackageTextRenderer", "09L must expose export package text renderer.");
        RequireContains(packageText, "FromReceiptInvoicePreview", "09L must provide receipt/invoice payload factory.");
        RequireContains(packageText, "FromReadingReportPreview", "09L must provide reading report payload factory.");
        RequireContains(packageText, "FromAuditEvidenceReportPreview", "09L must provide audit/evidence payload factory.");
        RequireContains(packageText, "SALRYN OFFICIAL OUTPUT EXPORT PACKAGE", "09L text renderer must include stable title.");
        RequireContains(packageText, "SHA256.HashData(Encoding.UTF8.GetBytes", "09L package must hash UTF-8 rendered package text.");
        RequireContains(packageText, "Convert.ToHexString(hash).ToLowerInvariant()", "09L package must use lowercase SHA-256 hex.");
        RequireContains(packageText, "salryn://official-output-export-package/", "09L package must use stable package references.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialOutputExportPackageChecks.Run();", "CoreSmoke must run 09L export package checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialOutputArtifactManifestChecks.Run();",
            StringComparison.Ordinal);
        int packageIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialOutputExportPackageChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || packageIndex < 0 || packageIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09L must run after 09K.");
        }

        string[] forbidden =
        {
            "CrystalDecisions",
            ".rpt",
            "System.Windows.Forms",
            "Salryn.Infrastructure",
            "Microsoft.Data.Sqlite",
            "Sqlite",
            "SQLite",
            "File.Write",
            "FileStream",
            "StreamWriter",
            "Process.Start",
            "ESC/POS",
            "EscPos",
            "PrintDocument",
            "PrinterSettings",
            "System.Drawing.Printing",
            "PdfDocument",
            "ExportToDisk",
            "GetTempPath",
            "GetTempFileName",
            "CompleteSale",
            "OfficialCheckout",
            "CheckoutCompletion",
            "ElectronicJournalAppend",
            "AppendOriginal",
            "CommitOriginal",
            "JournalAnchorInsert",
            "OfficialZReadingService",
            "IOfficialZReadingPersistence",
            "FinalizeAsync",
            "SaveAsync",
            "InsertZReading",
            "FiscalReset",
            "SELECT ",
            "INSERT ",
            "UPDATE ",
            "DELETE "
        };

        foreach (string marker in forbidden)
        {
            RequireDoesNotContain(packageText, marker, $"09L export package must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09L must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09L must not add PDF packages to Core.");
    }

    private static void VerifyPackageIncludesAllPayloadFamilies()
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());

        RequireEqual(5, package.Items.Count, "09L package must include all current preview payloads.");
        RequireEqual(1, package.Items.Count(item => item.Payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice), "09L package must include receipt/invoice payload.");
        RequireEqual(2, package.Items.Count(item => item.Payload.ArtifactFamily == OfficialOutputArtifactFamily.ReadingReport), "09L package must include X and Z reading payloads.");
        RequireEqual(2, package.Items.Count(item => item.Payload.ArtifactFamily == OfficialOutputArtifactFamily.AuditEvidenceReport), "09L package must include audit trail and electronic journal payloads.");
        RequireEqual("salryn://official-text-preview/receipt", package.Items[0].Payload.ArtifactReference, "09L package order must follow manifest order.");
    }

    private static void VerifyPackageCountsAndMarkers()
    {
        Fixture fixture = CreateFixture();
        OfficialOutputExportPackage package = BuildPackage(fixture);

        RequireEqual(OfficialOutputExportPackageKind.PreviewTextBundle, package.PackageKind, "09L package kind must be stable.");
        RequireEqual(5, package.EntryCount, "09L package must report entry count.");
        RequireEqual(5, package.PayloadCount, "09L package must report payload count.");
        RequireEqual(fixture.Payloads.Sum(payload => payload.LineCount), package.TotalLineCount, "09L package must report total line count.");
        RequireEqual(package.Items.Sum(item => item.Payload.LineCount), package.TotalLineCount, "09L total line count must match package items.");
        RequireStartsWith(package.PackageReference, "salryn://official-output-export-package/", "09L package reference must use stable prefix.");
        RequireEqual("salryn://official-output-export-package/" + package.PackageHash, package.PackageReference, "09L package reference must be hash-addressed.");
        RequireEqual(ExpectedHash(package.TextResult.Text), package.PackageHash, "09L package hash must be SHA-256 over UTF-8 package text.");
        RequireEqual(package.PackageHash.ToLowerInvariant(), package.PackageHash, "09L package hash must be lowercase.");
        RequireEqual(64, package.PackageHash.Length, "09L package hash must be 64 lowercase hex characters.");
        RequireDoesNotContain(package.TextResult.Text, "\r", "09L package text must use stable line feeds.");

        foreach (string marker in new[]
        {
            "SALRYN OFFICIAL OUTPUT EXPORT PACKAGE",
            "[PACKAGE]",
            "[MANIFEST]",
            "[PAYLOADS]",
            "[EVIDENCE]"
        })
        {
            RequireContains(package.TextResult.Text, marker, $"09L package text must include marker {marker}.");
        }

        RequireContains(package.TextResult.Text, "ManifestReference: " + fixture.ManifestPackage.ManifestReference, "09L package text must include manifest reference.");
        RequireContains(package.TextResult.Text, "ManifestText[1]: SALRYN OFFICIAL OUTPUT ARTIFACT MANIFEST", "09L package text must include manifest text bundle.");
        RequireContains(package.TextResult.Text, "PayloadText[1]: receipt-line-1", "09L package text must include receipt payload text.");
        RequireContains(package.TextResult.Text, "PayloadText[1]: x-reading-line", "09L package text must include X-reading payload text.");
        RequireContains(package.TextResult.Text, "PayloadText[1]: z-reading-line", "09L package text must include Z-reading payload text.");
        RequireContains(package.TextResult.Text, "PayloadText[1]: audit-line", "09L package text must include audit trail payload text.");
        RequireContains(package.TextResult.Text, "PayloadText[1]: electronic-journal-line", "09L package text must include electronic journal payload text.");
        RequireContains(package.TextResult.Text, "OutputSideEffectMarker: EXPORT_PACKAGE_IN_MEMORY_ONLY", "09L package must mark in-memory-only behavior.");
    }

    private static void VerifyPackageDeterminismAndHashBehavior()
    {
        Fixture fixture = CreateFixture();
        OfficialOutputExportPackage first = BuildPackage(fixture);
        OfficialOutputExportPackage second = BuildPackage(fixture);

        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09L package text must be deterministic for same inputs.");
        RequireEqual(first.PackageHash, second.PackageHash, "09L package hash must be deterministic for same inputs.");
        RequireEqual(first.PackageReference, second.PackageReference, "09L package reference must be deterministic for same inputs.");

        Fixture changedPayloadFixture = fixture with
        {
            Payloads = fixture.Payloads
                .Select((payload, index) => index == 0
                    ? payload with
                    {
                        Text = "receipt-line-1\nreceipt-line-2\nreceipt-line-3",
                        ContentHash = Hash("receipt-line-1\nreceipt-line-2\nreceipt-line-3"),
                        LineCount = 3
                    }
                    : payload)
                .ToArray()
        };
        changedPayloadFixture = changedPayloadFixture with
        {
            ManifestPackage = BuildManifestPackage(EntriesFromPayloads(changedPayloadFixture.Payloads))
        };

        OfficialOutputExportPackage changedPayload = BuildPackage(changedPayloadFixture);

        if (string.Equals(first.PackageHash, changedPayload.PackageHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09L package hash must change when payload text changes.");
        }

        OfficialOutputArtifactManifestPackage changedManifest = BuildManifestPackage(
            fixture.ManifestPackage.Entries
                .Select((entry, index) => index == 0
                    ? entry with { ArtifactLabel = "OFFICIAL_TEXT_PREVIEW_CHANGED" }
                    : entry)
                .ToArray());
        OfficialOutputExportPackage changedManifestPackage = BuildPackage(
            fixture with
            {
                ManifestPackage = changedManifest,
                Payloads = fixture.Payloads
                    .Select(payload => string.Equals(
                        payload.ArtifactReference,
                        "salryn://official-text-preview/receipt",
                        StringComparison.Ordinal)
                        ? payload with { ArtifactLabel = "OFFICIAL_TEXT_PREVIEW_CHANGED" }
                        : payload)
                    .ToArray()
            });

        if (string.Equals(first.PackageHash, changedManifestPackage.PackageHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09L package hash must change when manifest text changes.");
        }
    }

    private static void VerifyDeterministicItemOrder()
    {
        Fixture fixture = CreateFixture();
        OfficialOutputExportPackage package = BuildPackage(fixture with
        {
            Payloads = fixture.Payloads.Reverse().ToArray()
        });

        string[] expectedReferences =
        {
            "salryn://official-text-preview/receipt",
            "salryn://official-reading-preview/XReading/x",
            "salryn://official-reading-preview/ZReading/z",
            "salryn://official-audit-evidence-preview/AuditTrail/audit",
            "salryn://official-audit-evidence-preview/ElectronicJournal/ej"
        };

        for (int index = 0; index < expectedReferences.Length; index++)
        {
            RequireEqual(expectedReferences[index], package.Items[index].Payload.ArtifactReference, "09L package items must be ordered by manifest order regardless of payload input order.");
        }
    }

    private static void VerifyFactoryPreservation()
    {
        OfficialOutputExportPayload receipt = CreateReceiptPayload();
        OfficialOutputExportPayload xReading = CreateReadingPayload(OfficialReadingReportKind.XReading);
        OfficialOutputExportPayload audit = CreateAuditEvidencePayload(OfficialAuditEvidenceReportKind.AuditTrail);

        RequireEqual(OfficialOutputArtifactFamily.ReceiptInvoice, receipt.ArtifactFamily, "09L receipt factory must preserve family.");
        RequireEqual("OFFICIAL_TEXT_PREVIEW", receipt.ArtifactLabel, "09L receipt factory must preserve artifact label.");
        RequireEqual("salryn://official-text-preview/receipt", receipt.ArtifactReference, "09L receipt factory must preserve reference.");
        RequireEqual(Hash("receipt-line-1\nreceipt-line-2"), receipt.ContentHash, "09L receipt factory must preserve hash.");
        RequireEqual(2, receipt.LineCount, "09L receipt factory must preserve line count.");
        RequireEqual("receipt-line-1\nreceipt-line-2", receipt.Text, "09L receipt factory must preserve text.");

        RequireEqual(OfficialOutputArtifactFamily.ReadingReport, xReading.ArtifactFamily, "09L reading factory must preserve family.");
        RequireEqual("OFFICIAL_X_READING_PREVIEW", xReading.ArtifactLabel, "09L reading factory must preserve label.");
        RequireEqual("salryn://official-reading-preview/XReading/x", xReading.ArtifactReference, "09L reading factory must preserve reference.");
        RequireEqual(Hash("x-reading-line"), xReading.ContentHash, "09L reading factory must preserve hash.");

        RequireEqual(OfficialOutputArtifactFamily.AuditEvidenceReport, audit.ArtifactFamily, "09L audit factory must preserve family.");
        RequireEqual("OFFICIAL_AUDIT_TRAIL_PREVIEW", audit.ArtifactLabel, "09L audit factory must preserve label.");
        RequireEqual("salryn://official-audit-evidence-preview/AuditTrail/audit", audit.ArtifactReference, "09L audit factory must preserve reference.");
        RequireEqual(Hash("audit-line"), audit.ContentHash, "09L audit factory must preserve hash.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialOutputExportPackageBuilder builder = new();
        Fixture fixture = CreateFixture();

        RequireFailureCode(
            builder.Build(null, fixture.Payloads),
            "OfficialOutputExportPackage.ManifestRequired",
            "09L builder must require manifest package.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads.Skip(1).ToArray()),
            "OfficialOutputExportPackage.ManifestEntryWithoutPayload",
            "09L builder must reject missing payload for manifest entry.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads.Concat(new[]
            {
                fixture.Payloads[0] with { ArtifactReference = "salryn://official-text-preview/not-in-manifest" }
            }).ToArray()),
            "OfficialOutputExportPackage.PayloadWithoutManifestEntry",
            "09L builder must reject payload with no manifest entry.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads.Concat(new[] { fixture.Payloads[0] }).ToArray()),
            "OfficialOutputExportPackage.DuplicatePayloadArtifactReference",
            "09L builder must reject duplicate payload references.");

        RequireFailureCode(
            builder.Build(
                BuildManifestPackage(fixture.ManifestPackage.Entries
                    .Select((entry, index) => index == 0 ? entry with { ContentHash = Hash("different") } : entry)
                    .ToArray()),
                fixture.Payloads),
            "OfficialOutputExportPackage.ContentHashMismatch",
            "09L builder must reject hash mismatch between payload and manifest.");

        RequireFailureCode(
            builder.Build(
                BuildManifestPackage(fixture.ManifestPackage.Entries
                    .Select((entry, index) => index == 0 ? entry with { HashAlgorithm = "SHA-512" } : entry)
                    .ToArray()),
                fixture.Payloads),
            "OfficialOutputExportPackage.HashAlgorithmMismatch",
            "09L builder must reject algorithm mismatch between payload and manifest.");

        RequireFailureCode(
            builder.Build(
                BuildManifestPackage(fixture.ManifestPackage.Entries
                    .Select((entry, index) => index == 0 ? entry with { LineCount = 99 } : entry)
                    .ToArray()),
                fixture.Payloads),
            "OfficialOutputExportPackage.LineCountMismatch",
            "09L builder must reject line count mismatch between payload and manifest.");

        RequireFailureCode(
            builder.Build(
                BuildManifestPackage(fixture.ManifestPackage.Entries
                    .Select((entry, index) => index == 0 ? entry with { ArtifactFamily = OfficialOutputArtifactFamily.ReadingReport } : entry)
                    .ToArray()),
                fixture.Payloads),
            "OfficialOutputExportPackage.ArtifactFamilyMismatch",
            "09L builder must reject family mismatch between payload and manifest.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads
                .Select((payload, index) => index == 0 ? payload with { Text = " " } : payload)
                .ToArray()),
            "OfficialOutputExportPackage.PayloadTextRequired",
            "09L builder must reject empty payload text.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads
                .Select((payload, index) => index == 0 ? payload with { ArtifactLabel = " " } : payload)
                .ToArray()),
            "OfficialOutputExportPackage.ArtifactLabelRequired",
            "09L builder must reject empty payload artifact label.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads
                .Select((payload, index) => index == 0 ? payload with { ArtifactReference = " " } : payload)
                .ToArray()),
            "OfficialOutputExportPackage.ArtifactReferenceRequired",
            "09L builder must reject empty payload artifact reference.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads
                .Select((payload, index) => index == 0 ? payload with { ContentHash = " " } : payload)
                .ToArray()),
            "OfficialOutputExportPackage.ContentHashRequired",
            "09L builder must reject empty payload content hash.");

        RequireFailureCode(
            builder.Build(fixture.ManifestPackage, fixture.Payloads
                .Select((payload, index) => index == 0 ? payload with { LineCount = -1 } : payload)
                .ToArray()),
            "OfficialOutputExportPackage.InvalidLineCount",
            "09L builder must reject negative line count.");
    }

    private static Fixture CreateFixture()
    {
        IReadOnlyList<OfficialOutputExportPayload> payloads = new[]
        {
            CreateAuditEvidencePayload(OfficialAuditEvidenceReportKind.ElectronicJournal),
            CreateReadingPayload(OfficialReadingReportKind.ZReading),
            CreateReceiptPayload(),
            CreateAuditEvidencePayload(OfficialAuditEvidenceReportKind.AuditTrail),
            CreateReadingPayload(OfficialReadingReportKind.XReading)
        };

        return new Fixture(
            BuildManifestPackage(EntriesFromPayloads(payloads)),
            payloads);
    }

    private static IReadOnlyList<OfficialOutputArtifactManifestEntry> EntriesFromPayloads(
        IReadOnlyList<OfficialOutputExportPayload> payloads)
    {
        return payloads.Select(payload =>
        {
            OfficialOutputArtifactManifestSource source = CreateSource(payload);

            return new OfficialOutputArtifactManifestEntry(
                payload.ArtifactFamily,
                payload.ArtifactLabel,
                payload.ArtifactReference,
                payload.ContentHash,
                payload.HashAlgorithm,
                payload.LineCount,
                "Preview",
                source,
                new Dictionary<string, string> { ["entry"] = source.SourceKind });
        }).ToArray();
    }

    private static OfficialOutputArtifactManifestSource CreateSource(
        OfficialOutputExportPayload payload)
    {
        if (payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice)
        {
            return new OfficialOutputArtifactManifestSource(
                SourceKind: "OfficialTextPreview",
                SourceReference: "receipt:OR-2026-000001",
                DocumentKind: "OfficialReceipt",
                OfficialDocumentNumber: "OR-2026-000001",
                ReportKind: null,
                ReportNumber: null,
                ReferenceNumber: null,
                TerminalId: "POS-PACKAGE-01",
                CashierIdentifier: "cashier-package",
                BusinessDate: new DateOnly(2026, 6, 26),
                PeriodStart: null,
                PeriodEnd: null,
                IsCopyOrReprint: false,
                LifecycleState: "Active",
                CreatedAt,
                Metadata: null);
        }

        if (payload.ArtifactFamily == OfficialOutputArtifactFamily.ReadingReport)
        {
            bool isXReading = payload.ArtifactReference.Contains("/XReading/", StringComparison.Ordinal);

            return new OfficialOutputArtifactManifestSource(
                SourceKind: "OfficialReadingReportPreview",
                SourceReference: isXReading ? "reading:XR-2026-000001" : "reading:ZR-2026-000001",
                DocumentKind: null,
                OfficialDocumentNumber: null,
                ReportKind: isXReading ? "XReading" : "ZReading",
                ReportNumber: isXReading ? "XR-2026-000001" : "ZR-2026-000001",
                ReferenceNumber: null,
                TerminalId: "POS-PACKAGE-01",
                CashierIdentifier: "cashier-package",
                BusinessDate: new DateOnly(2026, 6, 26),
                PeriodStart: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                PeriodEnd: new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
                IsCopyOrReprint: null,
                LifecycleState: null,
                CreatedAt: null,
                Metadata: null);
        }

        bool isAudit = payload.ArtifactReference.Contains("/AuditTrail/", StringComparison.Ordinal);

        return new OfficialOutputArtifactManifestSource(
            SourceKind: "OfficialAuditEvidenceReportPreview",
            SourceReference: isAudit ? "audit:AT-2026-000001" : "audit:EJ-2026-000001",
            DocumentKind: null,
            OfficialDocumentNumber: null,
            ReportKind: isAudit ? "AuditTrail" : "ElectronicJournal",
            ReportNumber: isAudit ? "AT-2026-000001" : "EJ-2026-000001",
            ReferenceNumber: isAudit ? "AUDIT-REF-2026-000001" : "EJ-REF-2026-000001",
            TerminalId: "POS-PACKAGE-01",
            CashierIdentifier: "cashier-package",
            BusinessDate: new DateOnly(2026, 6, 26),
            PeriodStart: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
            IsCopyOrReprint: null,
            LifecycleState: null,
            CreatedAt: null,
            Metadata: null);
    }

    private static OfficialOutputExportPayload CreateReceiptPayload()
    {
        return RequireValue(OfficialOutputExportPackageBuilder.FromReceiptInvoicePreview(CreateReceiptPreviewResult()));
    }

    private static OfficialOutputExportPayload CreateReadingPayload(
        OfficialReadingReportKind reportKind)
    {
        return RequireValue(OfficialOutputExportPackageBuilder.FromReadingReportPreview(CreateReadingPreviewResult(reportKind)));
    }

    private static OfficialOutputExportPayload CreateAuditEvidencePayload(
        OfficialAuditEvidenceReportKind reportKind)
    {
        return RequireValue(OfficialOutputExportPackageBuilder.FromAuditEvidenceReportPreview(CreateAuditEvidencePreviewResult(reportKind)));
    }

    private static OfficialTextPreviewRenderResult CreateReceiptPreviewResult()
    {
        string textValue = "receipt-line-1\nreceipt-line-2";
        OfficialReceiptInvoiceLayoutDocument layout = new(new[]
        {
            new OfficialReceiptInvoiceLayoutSection(
                "DocumentHeader",
                new[]
                {
                    new OfficialReceiptInvoiceLayoutLine("OfficialDocumentNumber", "OR-2026-000001")
                })
        });
        OfficialReceiptInvoiceTextRenderResult text = new(
            textValue.Split('\n'),
            textValue);
        OfficialReportRenderResult contractResult = new(
            new OfficialReportRenderedArtifact(
                OfficialReportRenderFormat.Preview,
                "OFFICIAL_TEXT_PREVIEW",
                "salryn://official-text-preview/receipt",
                CreatedAt),
            OfficialTextPreviewReportRenderer.RendererContract);
        OfficialTextPreviewArtifactPackage package = new(
            contractResult,
            layout,
            text,
            "salryn://official-text-preview/receipt",
            Hash(textValue),
            text.Lines.Count,
            OfficialTextPreviewReportRenderer.RendererContract);

        return new OfficialTextPreviewRenderResult(
            contractResult,
            layout,
            text,
            package,
            "salryn://official-text-preview/receipt",
            Hash(textValue),
            text.Lines.Count,
            OfficialTextPreviewReportRenderer.RendererContract);
    }

    private static OfficialReadingReportPreviewResult CreateReadingPreviewResult(
        OfficialReadingReportKind reportKind)
    {
        string textValue = reportKind == OfficialReadingReportKind.XReading
            ? "x-reading-line"
            : "z-reading-line";
        string reference = reportKind == OfficialReadingReportKind.XReading
            ? "salryn://official-reading-preview/XReading/x"
            : "salryn://official-reading-preview/ZReading/z";
        string label = reportKind == OfficialReadingReportKind.XReading
            ? "OFFICIAL_X_READING_PREVIEW"
            : "OFFICIAL_Z_READING_PREVIEW";
        OfficialReadingReportTextRenderResult text = new(
            textValue.Split('\n'),
            textValue);
        OfficialReadingReportLayoutDocument layout = new(
            reportKind,
            Array.Empty<OfficialReadingReportLayoutSection>());
        OfficialReadingReportPreviewArtifactPackage package = new(
            reportKind,
            label,
            reference,
            Hash(textValue),
            text.Lines.Count,
            text);

        return new OfficialReadingReportPreviewResult(
            reportKind,
            layout,
            text,
            package,
            label,
            reference,
            Hash(textValue),
            text.Lines.Count);
    }

    private static OfficialAuditEvidenceReportPreviewResult CreateAuditEvidencePreviewResult(
        OfficialAuditEvidenceReportKind reportKind)
    {
        string textValue = reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "audit-line"
            : "electronic-journal-line";
        string reference = reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "salryn://official-audit-evidence-preview/AuditTrail/audit"
            : "salryn://official-audit-evidence-preview/ElectronicJournal/ej";
        string label = reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "OFFICIAL_AUDIT_TRAIL_PREVIEW"
            : "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW";
        OfficialAuditEvidenceReportTextRenderResult text = new(
            textValue.Split('\n'),
            textValue);
        OfficialAuditEvidenceReportLayoutDocument layout = new(
            reportKind,
            Array.Empty<OfficialAuditEvidenceReportLayoutSection>());
        OfficialAuditEvidenceReportPreviewArtifactPackage package = new(
            reportKind,
            label,
            reference,
            Hash(textValue),
            text.Lines.Count,
            text);

        return new OfficialAuditEvidenceReportPreviewResult(
            reportKind,
            layout,
            text,
            package,
            label,
            reference,
            Hash(textValue),
            text.Lines.Count);
    }

    private static OfficialOutputArtifactManifestPackage BuildManifestPackage(
        IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return RequireValue(new OfficialOutputArtifactManifestBuilder().Build(
            entries,
            CreatedAt,
            new Dictionary<string, string>
            {
                ["phase"] = "09L"
            }));
    }

    private static OfficialOutputExportPackage BuildPackage(
        Fixture fixture)
    {
        return RequireValue(new OfficialOutputExportPackageBuilder().Build(
            fixture.ManifestPackage,
            fixture.Payloads));
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string ExpectedHash(string text)
    {
        return Hash(text);
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

    private static T RequireValue<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Expected 09L success, got {result.ErrorDetail.Code}: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireFailureCode<T>(
        Result<T> result,
        string expectedCode,
        string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireStartsWith(
        string text,
        string value,
        string message)
    {
        if (!text.StartsWith(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    private sealed record Fixture(
        OfficialOutputArtifactManifestPackage ManifestPackage,
        IReadOnlyList<OfficialOutputExportPayload> Payloads);
}

using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialOutputArtifactManifestChecks
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 6, 26, 14, 20, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string manifestText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputArtifactManifestBuilder.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(manifestText, coreProjectText, coreSmokeText);
        VerifyManifestCanIncludeAllArtifactFamilies();
        VerifyManifestPackageAndText();
        VerifyManifestDeterminismAndHashBehavior();
        VerifyDeterministicOrdering();
        VerifyFactoryPreservation();
        VerifyValidationFailures();
    }

    private static void VerifySourceGuards(
        string manifestText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(manifestText, "OfficialOutputArtifactFamily", "09K must expose artifact family enum.");
        RequireContains(manifestText, "ReceiptInvoice", "09K must support receipt/invoice artifacts.");
        RequireContains(manifestText, "ReadingReport", "09K must support reading report artifacts.");
        RequireContains(manifestText, "AuditEvidenceReport", "09K must support audit/evidence report artifacts.");
        RequireContains(manifestText, "OfficialOutputArtifactManifestEntry", "09K must expose manifest entries.");
        RequireContains(manifestText, "OfficialOutputArtifactManifestSource", "09K must expose manifest source data.");
        RequireContains(manifestText, "OfficialOutputArtifactManifestPackage", "09K must expose manifest package.");
        RequireContains(manifestText, "OfficialOutputArtifactManifestBuilder", "09K must expose manifest builder.");
        RequireContains(manifestText, "OfficialOutputArtifactManifestTextRenderResult", "09K must expose text render result.");
        RequireContains(manifestText, "OfficialOutputArtifactManifestTextRenderer", "09K must expose text renderer.");
        RequireContains(manifestText, "FromReceiptInvoicePreview", "09K must provide receipt/invoice preview integration.");
        RequireContains(manifestText, "FromReadingReportPreview", "09K must provide reading report preview integration.");
        RequireContains(manifestText, "FromAuditEvidenceReportPreview", "09K must provide audit/evidence preview integration.");
        RequireContains(manifestText, "SALRYN OFFICIAL OUTPUT ARTIFACT MANIFEST", "09K manifest text must include stable title.");
        RequireContains(manifestText, "SHA256.HashData(Encoding.UTF8.GetBytes(text))", "09K manifest must hash UTF-8 rendered text.");
        RequireContains(manifestText, "Convert.ToHexString(hash).ToLowerInvariant()", "09K manifest must use lowercase SHA-256 hex.");
        RequireContains(manifestText, "salryn://official-output-manifest/", "09K manifest must use stable manifest references.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialOutputArtifactManifestChecks.Run();", "CoreSmoke must run 09K manifest checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialAuditEvidenceReportLayoutProofChecks.Run();",
            StringComparison.Ordinal);
        int manifestIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialOutputArtifactManifestChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || manifestIndex < 0 || manifestIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09K must run after 09J.");
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
            RequireDoesNotContain(manifestText, marker, $"09K manifest must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09K must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09K must not add PDF packages to Core.");
    }

    private static void VerifyManifestCanIncludeAllArtifactFamilies()
    {
        OfficialOutputArtifactManifestPackage package = BuildPackage(CreateAllEntries());

        RequireEqual(5, package.EntryCount, "09K manifest must include all current preview artifact entries.");
        RequireEqual(1, package.Entries.Count(entry => entry.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice), "09K manifest must include receipt/invoice artifact.");
        RequireEqual(2, package.Entries.Count(entry => entry.ArtifactFamily == OfficialOutputArtifactFamily.ReadingReport), "09K manifest must include X and Z reading artifacts.");
        RequireEqual(2, package.Entries.Count(entry => entry.ArtifactFamily == OfficialOutputArtifactFamily.AuditEvidenceReport), "09K manifest must include audit trail and electronic journal artifacts.");
        RequireEqual("ReceiptInvoice", package.Entries[0].ArtifactFamily.ToString(), "09K first artifact family should follow stable enum order.");
    }

    private static void VerifyManifestPackageAndText()
    {
        OfficialOutputArtifactManifestPackage package = BuildPackage(CreateAllEntries());

        RequireEqual(5, package.EntryCount, "09K package must report entry count.");
        RequireEqual(package.Entries.Count, package.EntryCount, "09K entry count must match package entries.");
        RequireEqual(CreatedAt, package.CreatedAt, "09K package must preserve created/requested timestamp.");
        RequireStartsWith(package.ManifestReference, "salryn://official-output-manifest/", "09K manifest reference must use stable prefix.");
        RequireEqual("salryn://official-output-manifest/" + package.ManifestHash, package.ManifestReference, "09K manifest reference must be hash-addressed.");
        RequireEqual(ExpectedHash(package.TextResult.Text), package.ManifestHash, "09K manifest hash must be SHA-256 over UTF-8 manifest text.");
        RequireEqual(package.ManifestHash.ToLowerInvariant(), package.ManifestHash, "09K manifest hash must be lowercase.");
        RequireEqual(64, package.ManifestHash.Length, "09K SHA-256 manifest hash must have 64 lowercase hex characters.");
        RequireDoesNotContain(package.TextResult.Text, "\r", "09K manifest text must use stable line feeds.");

        foreach (string marker in new[]
        {
            "SALRYN OFFICIAL OUTPUT ARTIFACT MANIFEST",
            "[MANIFEST]",
            "[ARTIFACTS]",
            "[EVIDENCE]"
        })
        {
            RequireContains(package.TextResult.Text, marker, $"09K manifest text must include marker {marker}.");
        }

        RequireContains(package.TextResult.Text, "Metadata: alpha=one | phase=09K", "09K manifest metadata must render in deterministic key order.");
        RequireContains(package.TextResult.Text, "HashAlgorithm: SHA-256", "09K evidence section must include hash algorithm.");
        RequireContains(package.TextResult.Text, "ManifestReferencePrefix: salryn://official-output-manifest/", "09K evidence section must include manifest reference prefix.");
        RequireContains(package.TextResult.Text, "OutputSideEffectMarker: MANIFEST_INDEX_ONLY_NO_OUTPUT_SIDE_EFFECTS", "09K manifest must mark no-output-side-effect behavior.");
    }

    private static void VerifyManifestDeterminismAndHashBehavior()
    {
        OfficialOutputArtifactManifestBuilder builder = new();
        OfficialOutputArtifactManifestPackage first = RequireValue(builder.Build(
            CreateAllEntries(),
            CreatedAt,
            ManifestMetadata()));
        OfficialOutputArtifactManifestPackage second = RequireValue(builder.Build(
            CreateAllEntries(),
            CreatedAt,
            ManifestMetadata()));

        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09K manifest text must be deterministic for same entries.");
        RequireEqual(first.ManifestHash, second.ManifestHash, "09K manifest hash must be deterministic for same entries.");
        RequireEqual(first.ManifestReference, second.ManifestReference, "09K manifest reference must be deterministic for same entries.");

        IReadOnlyList<OfficialOutputArtifactManifestEntry> changedEntries = CreateAllEntries()
            .Select((entry, index) => index == 0
                ? entry with { ContentHash = Hash("receipt-changed") }
                : entry)
            .ToArray();
        OfficialOutputArtifactManifestPackage changed = RequireValue(builder.Build(
            changedEntries,
            CreatedAt,
            ManifestMetadata()));

        if (string.Equals(first.ManifestHash, changed.ManifestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09K manifest hash must change when a meaningful artifact field changes.");
        }
    }

    private static void VerifyDeterministicOrdering()
    {
        OfficialOutputArtifactManifestBuilder builder = new();
        IReadOnlyList<OfficialOutputArtifactManifestEntry> entries = CreateAllEntries();
        OfficialOutputArtifactManifestPackage ordered = RequireValue(builder.Build(
            entries.Reverse().ToArray(),
            CreatedAt,
            ManifestMetadata()));

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
            RequireEqual(expectedReferences[index], ordered.Entries[index].ArtifactReference, "09K manifest entries must be ordered deterministically.");
        }
    }

    private static void VerifyFactoryPreservation()
    {
        OfficialOutputArtifactManifestEntry receipt = CreateReceiptEntry();
        OfficialOutputArtifactManifestEntry xReading = CreateReadingEntry(OfficialReadingReportKind.XReading);
        OfficialOutputArtifactManifestEntry audit = CreateAuditEvidenceEntry(OfficialAuditEvidenceReportKind.AuditTrail);

        RequireEqual(OfficialOutputArtifactFamily.ReceiptInvoice, receipt.ArtifactFamily, "09K receipt factory must set receipt/invoice family.");
        RequireEqual("OFFICIAL_TEXT_PREVIEW", receipt.ArtifactLabel, "09K receipt factory must preserve artifact label.");
        RequireEqual("salryn://official-text-preview/receipt", receipt.ArtifactReference, "09K receipt factory must preserve artifact reference.");
        RequireEqual(Hash("receipt"), receipt.ContentHash, "09K receipt factory must preserve content hash.");
        RequireEqual(2, receipt.LineCount, "09K receipt factory must preserve line count.");
        RequireEqual("OfficialReceipt", receipt.Source!.DocumentKind, "09K receipt factory must preserve document kind.");
        RequireEqual("OR-2026-000001", receipt.Source.OfficialDocumentNumber, "09K receipt factory must preserve official document number.");

        RequireEqual(OfficialOutputArtifactFamily.ReadingReport, xReading.ArtifactFamily, "09K reading factory must set reading report family.");
        RequireEqual("XReading", xReading.Source!.ReportKind, "09K reading factory must preserve report kind.");
        RequireEqual("salryn://official-reading-preview/XReading/x", xReading.ArtifactReference, "09K reading factory must preserve artifact reference.");
        RequireEqual(Hash("x-reading"), xReading.ContentHash, "09K reading factory must preserve content hash.");

        RequireEqual(OfficialOutputArtifactFamily.AuditEvidenceReport, audit.ArtifactFamily, "09K audit factory must set audit/evidence family.");
        RequireEqual("AuditTrail", audit.Source!.ReportKind, "09K audit factory must preserve report kind.");
        RequireEqual("salryn://official-audit-evidence-preview/AuditTrail/audit", audit.ArtifactReference, "09K audit factory must preserve artifact reference.");
        RequireEqual(Hash("audit"), audit.ContentHash, "09K audit factory must preserve content hash.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialOutputArtifactManifestBuilder builder = new();
        OfficialOutputArtifactManifestEntry receipt = CreateReceiptEntry();

        RequireFailureCode(
            builder.Build(null),
            "OfficialOutputArtifactManifest.EntriesRequired",
            "09K builder must reject null entries.");

        RequireFailureCode(
            builder.Build(new[] { receipt, receipt with { Source = receipt.Source! with { SourceReference = "receipt:duplicate" } } }),
            "OfficialOutputArtifactManifest.DuplicateArtifactReference",
            "09K builder must reject duplicate artifact references.");

        RequireFailureCode(
            builder.Build(new[] { receipt with { ArtifactLabel = " " } }),
            "OfficialOutputArtifactManifest.ArtifactLabelRequired",
            "09K builder must reject empty artifact labels.");

        RequireFailureCode(
            builder.Build(new[] { receipt with { ArtifactReference = " " } }),
            "OfficialOutputArtifactManifest.ArtifactReferenceRequired",
            "09K builder must reject empty artifact references.");

        RequireFailureCode(
            builder.Build(new[] { receipt with { ContentHash = " " } }),
            "OfficialOutputArtifactManifest.ContentHashRequired",
            "09K builder must reject empty content hashes.");

        RequireFailureCode(
            builder.Build(new[] { receipt with { HashAlgorithm = " " } }),
            "OfficialOutputArtifactManifest.HashAlgorithmRequired",
            "09K builder must reject empty hash algorithms.");

        RequireFailureCode(
            builder.Build(new[] { receipt with { LineCount = -1 } }),
            "OfficialOutputArtifactManifest.InvalidLineCount",
            "09K builder must reject negative line counts.");

        RequireFailureCode(
            builder.Build(new[]
            {
                receipt,
                CreateReceiptEntry(
                    artifactReference: "salryn://official-text-preview/receipt-2",
                    sourceReference: receipt.Source!.SourceReference)
            }),
            "OfficialOutputArtifactManifest.DuplicateSourceReference",
            "09K builder must reject duplicate source reference plus family.");

        RequireFailureCode(
            builder.Build(
                new[] { receipt },
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["phase"] = "09K",
                    [" phase "] = "duplicate"
                }),
            "OfficialOutputArtifactManifest.DuplicateMetadataKey",
            "09K builder must reject duplicate manifest metadata keys after trim.");

        RequireFailureCode(
            builder.Build(new[]
            {
                receipt with
                {
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["entry"] = "one",
                        [" entry "] = "two"
                    }
                }
            }),
            "OfficialOutputArtifactManifest.DuplicateMetadataKey",
            "09K builder must reject duplicate entry metadata keys after trim.");
    }

    private static IReadOnlyList<OfficialOutputArtifactManifestEntry> CreateAllEntries()
    {
        return new[]
        {
            CreateAuditEvidenceEntry(OfficialAuditEvidenceReportKind.ElectronicJournal),
            CreateReadingEntry(OfficialReadingReportKind.ZReading),
            CreateReceiptEntry(),
            CreateAuditEvidenceEntry(OfficialAuditEvidenceReportKind.AuditTrail),
            CreateReadingEntry(OfficialReadingReportKind.XReading)
        };
    }

    private static OfficialOutputArtifactManifestEntry CreateReceiptEntry(
        string artifactReference = "salryn://official-text-preview/receipt",
        string sourceReference = "receipt:OR-2026-000001")
    {
        OfficialTextPreviewRenderResult result = CreateReceiptPreviewResult(artifactReference);

        return RequireValue(OfficialOutputArtifactManifestBuilder.FromReceiptInvoicePreview(
            result,
            new OfficialOutputArtifactManifestSource(
                SourceKind: "OfficialTextPreview",
                SourceReference: sourceReference,
                DocumentKind: "OfficialReceipt",
                OfficialDocumentNumber: "OR-2026-000001",
                ReportKind: null,
                ReportNumber: null,
                ReferenceNumber: null,
                TerminalId: "POS-MANIFEST-01",
                CashierIdentifier: "cashier-manifest",
                BusinessDate: new DateOnly(2026, 6, 26),
                PeriodStart: null,
                PeriodEnd: null,
                IsCopyOrReprint: false,
                LifecycleState: "Active",
                CreatedAt: null,
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = "receipt"
                }),
            new Dictionary<string, string>
            {
                ["entry"] = "receipt"
            }));
    }

    private static OfficialOutputArtifactManifestEntry CreateReadingEntry(
        OfficialReadingReportKind reportKind)
    {
        OfficialReadingReportPreviewResult result = CreateReadingPreviewResult(reportKind);
        string reportCode = reportKind == OfficialReadingReportKind.XReading ? "XR-2026-000001" : "ZR-2026-000001";

        return RequireValue(OfficialOutputArtifactManifestBuilder.FromReadingReportPreview(
            result,
            new OfficialOutputArtifactManifestSource(
                SourceKind: "OfficialReadingReportPreview",
                SourceReference: "reading:" + reportCode,
                DocumentKind: null,
                OfficialDocumentNumber: null,
                ReportKind: null,
                ReportNumber: reportCode,
                ReferenceNumber: null,
                TerminalId: "POS-MANIFEST-01",
                CashierIdentifier: "cashier-manifest",
                BusinessDate: new DateOnly(2026, 6, 26),
                PeriodStart: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                PeriodEnd: new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
                IsCopyOrReprint: null,
                LifecycleState: null,
                CreatedAt: null,
                Metadata: null),
            new Dictionary<string, string>
            {
                ["entry"] = reportKind.ToString()
            }));
    }

    private static OfficialOutputArtifactManifestEntry CreateAuditEvidenceEntry(
        OfficialAuditEvidenceReportKind reportKind)
    {
        OfficialAuditEvidenceReportPreviewResult result = CreateAuditEvidencePreviewResult(reportKind);
        string sourceReference = reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "audit:AT-2026-000001"
            : "audit:EJ-2026-000001";

        return RequireValue(OfficialOutputArtifactManifestBuilder.FromAuditEvidenceReportPreview(
            result,
            new OfficialOutputArtifactManifestSource(
                SourceKind: "OfficialAuditEvidenceReportPreview",
                SourceReference: sourceReference,
                DocumentKind: null,
                OfficialDocumentNumber: null,
                ReportKind: null,
                ReportNumber: reportKind == OfficialAuditEvidenceReportKind.AuditTrail ? "AT-2026-000001" : "EJ-2026-000001",
                ReferenceNumber: reportKind == OfficialAuditEvidenceReportKind.AuditTrail ? "AUDIT-REF-2026-000001" : "EJ-REF-2026-000001",
                TerminalId: "POS-MANIFEST-01",
                CashierIdentifier: "cashier-manifest",
                BusinessDate: new DateOnly(2026, 6, 26),
                PeriodStart: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
                PeriodEnd: new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
                IsCopyOrReprint: null,
                LifecycleState: null,
                CreatedAt: null,
                Metadata: null),
            new Dictionary<string, string>
            {
                ["entry"] = reportKind.ToString()
            }));
    }

    private static OfficialTextPreviewRenderResult CreateReceiptPreviewResult(
        string artifactReference)
    {
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
            new[] { "receipt-line-1", "receipt-line-2" },
            "receipt-line-1\nreceipt-line-2");
        OfficialReportRenderResult contractResult = new(
            new OfficialReportRenderedArtifact(
                OfficialReportRenderFormat.Preview,
                "OFFICIAL_TEXT_PREVIEW",
                artifactReference,
                CreatedAt),
            OfficialTextPreviewReportRenderer.RendererContract);
        OfficialTextPreviewArtifactPackage package = new(
            contractResult,
            layout,
            text,
            artifactReference,
            Hash("receipt"),
            text.Lines.Count,
            OfficialTextPreviewReportRenderer.RendererContract);

        return new OfficialTextPreviewRenderResult(
            contractResult,
            layout,
            text,
            package,
            artifactReference,
            Hash("receipt"),
            text.Lines.Count,
            OfficialTextPreviewReportRenderer.RendererContract);
    }

    private static OfficialReadingReportPreviewResult CreateReadingPreviewResult(
        OfficialReadingReportKind reportKind)
    {
        string name = reportKind == OfficialReadingReportKind.XReading ? "x-reading" : "z-reading";
        string reference = reportKind == OfficialReadingReportKind.XReading
            ? "salryn://official-reading-preview/XReading/x"
            : "salryn://official-reading-preview/ZReading/z";
        string label = reportKind == OfficialReadingReportKind.XReading
            ? "OFFICIAL_X_READING_PREVIEW"
            : "OFFICIAL_Z_READING_PREVIEW";
        OfficialReadingReportTextRenderResult text = new(
            new[] { name + "-line" },
            name + "-line");
        OfficialReadingReportLayoutDocument layout = new(
            reportKind,
            Array.Empty<OfficialReadingReportLayoutSection>());
        OfficialReadingReportPreviewArtifactPackage package = new(
            reportKind,
            label,
            reference,
            Hash(name),
            text.Lines.Count,
            text);

        return new OfficialReadingReportPreviewResult(
            reportKind,
            layout,
            text,
            package,
            label,
            reference,
            Hash(name),
            text.Lines.Count);
    }

    private static OfficialAuditEvidenceReportPreviewResult CreateAuditEvidencePreviewResult(
        OfficialAuditEvidenceReportKind reportKind)
    {
        string name = reportKind == OfficialAuditEvidenceReportKind.AuditTrail ? "audit" : "electronic-journal";
        string reference = reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "salryn://official-audit-evidence-preview/AuditTrail/audit"
            : "salryn://official-audit-evidence-preview/ElectronicJournal/ej";
        string label = reportKind == OfficialAuditEvidenceReportKind.AuditTrail
            ? "OFFICIAL_AUDIT_TRAIL_PREVIEW"
            : "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW";
        OfficialAuditEvidenceReportTextRenderResult text = new(
            new[] { name + "-line" },
            name + "-line");
        OfficialAuditEvidenceReportLayoutDocument layout = new(
            reportKind,
            Array.Empty<OfficialAuditEvidenceReportLayoutSection>());
        OfficialAuditEvidenceReportPreviewArtifactPackage package = new(
            reportKind,
            label,
            reference,
            Hash(name),
            text.Lines.Count,
            text);

        return new OfficialAuditEvidenceReportPreviewResult(
            reportKind,
            layout,
            text,
            package,
            label,
            reference,
            Hash(name),
            text.Lines.Count);
    }

    private static OfficialOutputArtifactManifestPackage BuildPackage(
        IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return RequireValue(new OfficialOutputArtifactManifestBuilder().Build(
            entries,
            CreatedAt,
            ManifestMetadata()));
    }

    private static IReadOnlyDictionary<string, string> ManifestMetadata()
    {
        return new Dictionary<string, string>
        {
            ["phase"] = "09K",
            ["alpha"] = "one"
        };
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
            throw new InvalidOperationException($"Expected 09K success, got {result.ErrorDetail.Code}: {result.Error}");
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
}

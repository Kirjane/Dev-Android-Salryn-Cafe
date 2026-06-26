using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialOutputDeliveryReceiptLedgerChecks
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 26, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 26, 17, 5, 0, TimeSpan.Zero);
    private static readonly Guid RequestedByUserId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string ledgerText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputDeliveryReceiptLedgerBuilder.cs");
        string deliveryText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialInMemoryOutputDeliveryAdapter.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(ledgerText, deliveryText, coreProjectText, coreSmokeText);
        VerifyBuildsAndPreservesFacts();
        VerifyMultipleAndOrderDeterminism();
        VerifyValidationFailures();
        VerifyTextHashAndChangeBehavior();
        VerifyMetadataNormalization();
        VerifyDeliveryAdapterBehaviorRemainsIntact(deliveryText);
    }

    private static void VerifySourceGuards(string ledgerText, string deliveryText, string coreProjectText, string coreSmokeText)
    {
        foreach (string marker in new[]
        {
            "OfficialOutputDeliveryLedgerEntry",
            "OfficialOutputDeliveryLedgerPackage",
            "OfficialOutputDeliveryLedgerTextRenderResult",
            "OfficialOutputDeliveryReceiptLedgerBuilder",
            "OfficialOutputDeliveryReceiptLedgerTextRenderer",
            "OfficialOutputDeliveryReceiptLedgerValidator",
            "SALRYN OFFICIAL OUTPUT DELIVERY RECEIPT LEDGER",
            "DELIVERY_LEDGER_IN_MEMORY_ONLY_NO_PERSISTENCE",
            "salryn://official-output-delivery-ledger/",
            "SHA256.HashData(Encoding.UTF8.GetBytes",
            "Convert.ToHexString(hash).ToLowerInvariant()"
        })
        {
            RequireContains(ledgerText, marker, $"09N ledger source must contain {marker}.");
        }

        RequireContains(deliveryText, "DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT", "09M delivery marker must remain in source.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialOutputDeliveryReceiptLedgerChecks.Run();", "CoreSmoke must run 09N ledger checks.");

        int deliveryIndex = coreSmokeText.IndexOf("J21Rmo242023OfficialInMemoryOutputDeliveryAdapterChecks.Run();", StringComparison.Ordinal);
        int ledgerIndex = coreSmokeText.IndexOf("J21Rmo242023OfficialOutputDeliveryReceiptLedgerChecks.Run();", StringComparison.Ordinal);

        if (deliveryIndex < 0 || ledgerIndex < 0 || ledgerIndex < deliveryIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09N must run after 09M.");
        }

        foreach (string marker in new[]
        {
            "CrystalDecisions", ".rpt", "System.Windows.Forms", "Salryn.Infrastructure", "Microsoft.Data.Sqlite",
            "Sqlite", "SQLite", "File.Write", "FileStream", "StreamWriter", "Process.Start", "ESC/POS",
            "PrintDocument", "PrinterSettings", "System.Drawing.Printing", "PdfDocument", "ExportToDisk",
            "CompleteSale", "OfficialCheckout", "ElectronicJournalAppend", "OfficialZReadingService",
            "FinalizeAsync", "SaveAsync", "INSERT ", "UPDATE ", "DELETE "
        })
        {
            RequireDoesNotContain(ledgerText, marker, $"09N ledger must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09N must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09N must not add PDF packages to Core.");
    }

    private static void VerifyBuildsAndPreservesFacts()
    {
        OfficialOutputDeliveryResult delivery = CreateDelivery("single", "POS-LEDGER-01");
        OfficialOutputDeliveryLedgerPackage ledger = BuildLedger(new[] { delivery });
        OfficialOutputDeliveryLedgerEntry entry = ledger.Entries[0];
        OfficialOutputDeliveryArtifact artifact = delivery.Artifact;

        RequireEqual(1, ledger.EntryCount, "09N ledger must report one entry.");
        RequireEqual(1, entry.EntryNumber, "09N ledger must number entry.");
        RequireStartsWith(ledger.LedgerReference, "salryn://official-output-delivery-ledger/", "09N ledger reference must use stable prefix.");
        RequireEqual("salryn://official-output-delivery-ledger/" + ledger.LedgerHash, ledger.LedgerReference, "09N ledger reference must be hash-addressed.");
        RequireEqual(64, ledger.LedgerHash.Length, "09N ledger hash must be 64 lowercase hex characters.");
        RequireEqual(ledger.LedgerHash.ToLowerInvariant(), ledger.LedgerHash, "09N ledger hash must be lowercase.");
        RequireEqual(CreatedAt, ledger.CreatedAt, "09N ledger must preserve created timestamp.");
        RequireEqual("09N", ledger.Metadata["phase"], "09N ledger must preserve package metadata.");
        RequireDoesNotContain(ledger.TextResult.Text, "\r", "09N ledger text must use LF-only output.");

        RequireEqual(artifact.Target, entry.DeliveryTarget, "09N ledger must preserve delivery target.");
        RequireEqual(artifact.DeliveryReference, entry.DeliveryReference, "09N ledger must preserve delivery reference.");
        RequireEqual(artifact.DeliveryHash, entry.DeliveryHash, "09N ledger must preserve delivery hash.");
        RequireEqual(artifact.HashAlgorithm, entry.HashAlgorithm, "09N ledger must preserve hash algorithm.");
        RequireEqual(artifact.AdapterName, entry.AdapterName, "09N ledger must preserve adapter name.");
        RequireEqual(artifact.SourcePackageReference, entry.PackageReference, "09N ledger must preserve package reference.");
        RequireEqual(artifact.SourcePackageHash, entry.PackageHash, "09N ledger must preserve package hash.");
        RequireEqual(artifact.SourceManifestReference, entry.ManifestReference, "09N ledger must preserve manifest reference.");
        RequireEqual(artifact.SourceManifestHash, entry.ManifestHash, "09N ledger must preserve manifest hash.");
        RequireEqual(artifact.EntryCount, entry.EntryCount, "09N ledger must preserve entry count.");
        RequireEqual(artifact.PayloadCount, entry.PayloadCount, "09N ledger must preserve payload count.");
        RequireEqual(artifact.TotalLineCount, entry.TotalLineCount, "09N ledger must preserve total line count.");
        RequireEqual(artifact.SideEffectMarker, entry.SideEffectMarker, "09N ledger must preserve delivery marker.");
        RequireEqual(artifact.DeliveredAt, entry.DeliveredAt, "09N ledger must preserve delivery timestamp.");
        RequireEqual(artifact.TerminalId, entry.TerminalId, "09N ledger must preserve terminal id.");
        RequireEqual((Guid?)null, entry.RequestedByUserId, "09N requester id remains null because 09M result does not expose it.");
    }

    private static void VerifyMultipleAndOrderDeterminism()
    {
        OfficialOutputDeliveryResult first = CreateDelivery("order-a", "POS-LEDGER-01");
        OfficialOutputDeliveryResult second = CreateDelivery("order-b", "POS-LEDGER-02");
        OfficialOutputDeliveryResult third = CreateDelivery("order-c", "POS-LEDGER-03");
        OfficialOutputDeliveryLedgerPackage forward = BuildLedger(new[] { first, second, third });
        OfficialOutputDeliveryLedgerPackage reverse = BuildLedger(new[] { third, second, first });

        RequireEqual(3, forward.EntryCount, "09N ledger must include all delivery results.");
        RequireEqual(1, forward.Entries[0].EntryNumber, "09N ledger must number first deterministic entry.");
        RequireEqual(2, forward.Entries[1].EntryNumber, "09N ledger must number second deterministic entry.");
        RequireEqual(3, forward.Entries[2].EntryNumber, "09N ledger must number third deterministic entry.");
        RequireEqual(forward.TextResult.Text, reverse.TextResult.Text, "09N ledger text must be deterministic regardless of input order.");
        RequireEqual(forward.LedgerHash, reverse.LedgerHash, "09N ledger hash must be deterministic regardless of input order.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialOutputDeliveryReceiptLedgerBuilder builder = new();
        OfficialOutputDeliveryResult delivery = CreateDelivery("validation", "POS-LEDGER-01");

        RequireFailureCode(builder.Build(null), "OfficialOutputDeliveryLedger.ResultsRequired", "09N must reject null delivery result list.");
        RequireFailureCode(builder.Build(Array.Empty<OfficialOutputDeliveryResult>()), "OfficialOutputDeliveryLedger.ResultRequired", "09N must reject empty delivery result list.");
        RequireFailureCode(builder.Build(new OfficialOutputDeliveryResult[] { null! }), "OfficialOutputDeliveryLedger.InvalidDeliveryResult", "09N must reject null delivery result.");
        RequireFailureCode(builder.Build(new[] { delivery, delivery }), "OfficialOutputDeliveryLedger.DuplicateDeliveryReference", "09N must reject duplicate delivery reference.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { DeliveryReference = " " }) }), "OfficialOutputDeliveryLedger.DeliveryReferenceRequired", "09N must reject empty delivery reference.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { DeliveryHash = " " }) }), "OfficialOutputDeliveryLedger.DeliveryHashRequired", "09N must reject empty delivery hash.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { HashAlgorithm = " " }) }), "OfficialOutputDeliveryLedger.HashAlgorithmRequired", "09N must reject empty hash algorithm.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { SourcePackageReference = " " }) }), "OfficialOutputDeliveryLedger.PackageReferenceRequired", "09N must reject empty package reference.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { SourcePackageHash = " " }) }), "OfficialOutputDeliveryLedger.PackageHashRequired", "09N must reject empty package hash.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { EntryCount = -1 }) }), "OfficialOutputDeliveryLedger.InvalidCounts", "09N must reject negative entry count.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { PayloadCount = -1 }) }), "OfficialOutputDeliveryLedger.InvalidCounts", "09N must reject negative payload count.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { TotalLineCount = -1 }) }), "OfficialOutputDeliveryLedger.InvalidCounts", "09N must reject negative total line count.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { Target = (OfficialOutputDeliveryTarget)999 }) }), "OfficialOutputDeliveryLedger.UnsupportedTarget", "09N must reject unsupported target.");
        RequireFailureCode(builder.Build(new[] { WithArtifact(delivery, delivery.Artifact with { SideEffectMarker = " " }) }), "OfficialOutputDeliveryLedger.DeliveryMarkerRequired", "09N must require 09M marker.");
    }

    private static void VerifyTextHashAndChangeBehavior()
    {
        OfficialOutputDeliveryLedgerPackage first = BuildLedger(new[] { CreateDelivery("hash-a", "POS-LEDGER-01") });
        OfficialOutputDeliveryLedgerPackage second = BuildLedger(new[] { CreateDelivery("hash-a", "POS-LEDGER-01") });

        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09N ledger text must be deterministic for same input.");
        RequireEqual(first.LedgerHash, second.LedgerHash, "09N ledger hash must be deterministic for same input.");
        RequireEqual(first.LedgerReference, second.LedgerReference, "09N ledger reference must be deterministic for same input.");

        foreach (string marker in new[] { "SALRYN OFFICIAL OUTPUT DELIVERY RECEIPT LEDGER", "[LEDGER]", "[DELIVERIES]", "[EVIDENCE]", "DELIVERY_LEDGER_IN_MEMORY_ONLY_NO_PERSISTENCE" })
        {
            RequireContains(first.TextResult.Text, marker, $"09N ledger text must include marker {marker}.");
        }

        OfficialOutputDeliveryResult delivery = CreateDelivery("change", "POS-LEDGER-01");
        OfficialOutputDeliveryLedgerPackage unchanged = BuildLedger(new[] { delivery });
        string changedHash = Hash("changed-delivery-hash");
        OfficialOutputDeliveryLedgerPackage changed = BuildLedger(new[]
        {
            WithArtifact(delivery, delivery.Artifact with
            {
                DeliveryHash = changedHash,
                DeliveryReference = "salryn://official-output-delivery/in-memory/" + changedHash
            })
        });

        if (string.Equals(unchanged.LedgerHash, changed.LedgerHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09N ledger hash must change when delivery hash/reference changes.");
        }
    }

    private static void VerifyMetadataNormalization()
    {
        OfficialOutputDeliveryResult delivery = CreateDelivery("metadata", "POS-LEDGER-01");
        OfficialOutputDeliveryResult firstDelivery = WithArtifact(delivery, delivery.Artifact with
        {
            Metadata = new Dictionary<string, string> { [" z-key "] = " z-value ", ["a-key"] = " a-value " }
        });
        OfficialOutputDeliveryResult secondDelivery = WithArtifact(delivery, delivery.Artifact with
        {
            Metadata = new Dictionary<string, string> { ["a-key"] = " a-value ", [" z-key "] = " z-value " }
        });
        OfficialOutputDeliveryLedgerPackage first = BuildLedger(new[] { firstDelivery }, new Dictionary<string, string> { [" z-ledger "] = " z-value ", ["a-ledger"] = " a-value " });
        OfficialOutputDeliveryLedgerPackage second = BuildLedger(new[] { secondDelivery }, new Dictionary<string, string> { ["a-ledger"] = " a-value ", [" z-ledger "] = " z-value " });

        RequireEqual("a-value", first.Metadata["a-ledger"], "09N ledger package metadata values must be trimmed.");
        RequireEqual("z-value", first.Metadata["z-ledger"], "09N ledger package metadata keys must be trimmed.");
        RequireEqual("a-value", first.Entries[0].Metadata["a-key"], "09N ledger entry metadata values must be trimmed.");
        RequireEqual("z-value", first.Entries[0].Metadata["z-key"], "09N ledger entry metadata keys must be trimmed.");
        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09N ledger metadata normalization must be deterministic.");
        RequireEqual(first.LedgerHash, second.LedgerHash, "09N ledger metadata ordering must not destabilize hash.");
    }

    private static void VerifyDeliveryAdapterBehaviorRemainsIntact(string deliveryText)
    {
        OfficialOutputDeliveryResult delivery = CreateDelivery("delivery-intact", "POS-LEDGER-01");

        RequireContains(deliveryText, "OfficialInMemoryOutputDeliveryAdapter", "09M delivery adapter must remain in source.");
        RequireEqual(OfficialOutputDeliveryTarget.InMemory, delivery.Artifact.Target, "09M delivery target must remain in-memory.");
        RequireEqual("DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT", delivery.Artifact.SideEffectMarker, "09M delivery marker must remain intact.");
        RequireStartsWith(delivery.Artifact.DeliveryReference, "salryn://official-output-delivery/in-memory/", "09M delivery reference must remain intact.");
    }

    private static OfficialOutputDeliveryLedgerPackage BuildLedger(IReadOnlyList<OfficialOutputDeliveryResult> deliveries, IReadOnlyDictionary<string, string>? metadata = null)
    {
        return RequireValue(new OfficialOutputDeliveryReceiptLedgerBuilder().Build(deliveries, CreatedAt, metadata ?? new Dictionary<string, string> { ["phase"] = "09N" }));
    }

    private static OfficialOutputDeliveryResult CreateDelivery(string metadataValue, string terminalId)
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());
        OfficialOutputDeliveryRequest request = new(package, OfficialOutputDeliveryTarget.InMemory, RequestedByUserId, RequestedAt, terminalId, new Dictionary<string, string> { ["delivery"] = metadataValue });

        return RequireValue(new OfficialInMemoryOutputDeliveryAdapter().DeliverAsync(request).GetAwaiter().GetResult());
    }

    private static OfficialOutputDeliveryResult WithArtifact(OfficialOutputDeliveryResult result, OfficialOutputDeliveryArtifact artifact)
    {
        return result with { Artifact = artifact };
    }

    private static Fixture CreateFixture()
    {
        IReadOnlyList<OfficialOutputExportPayload> payloads = new[]
        {
            CreatePayload(OfficialOutputArtifactFamily.ReceiptInvoice, "OFFICIAL_TEXT_PREVIEW", "salryn://official-text-preview/receipt", "receipt-line-1\nreceipt-line-2"),
            CreatePayload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_X_READING_PREVIEW", "salryn://official-reading-preview/XReading/x", "x-reading-line"),
            CreatePayload(OfficialOutputArtifactFamily.ReadingReport, "OFFICIAL_Z_READING_PREVIEW", "salryn://official-reading-preview/ZReading/z", "z-reading-line"),
            CreatePayload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_AUDIT_TRAIL_PREVIEW", "salryn://official-audit-evidence-preview/AuditTrail/audit", "audit-line"),
            CreatePayload(OfficialOutputArtifactFamily.AuditEvidenceReport, "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW", "salryn://official-audit-evidence-preview/ElectronicJournal/ej", "electronic-journal-line")
        };

        return new Fixture(BuildManifestPackage(EntriesFromPayloads(payloads)), payloads);
    }

    private static OfficialOutputExportPayload CreatePayload(OfficialOutputArtifactFamily family, string label, string reference, string text)
    {
        return new OfficialOutputExportPayload(family, label, reference, Hash(text), "SHA-256", text.Split('\n').Length, text);
    }

    private static IReadOnlyList<OfficialOutputArtifactManifestEntry> EntriesFromPayloads(IReadOnlyList<OfficialOutputExportPayload> payloads)
    {
        return payloads.Select(payload =>
        {
            OfficialOutputArtifactManifestSource source = CreateSource(payload);
            return new OfficialOutputArtifactManifestEntry(payload.ArtifactFamily, payload.ArtifactLabel, payload.ArtifactReference, payload.ContentHash, payload.HashAlgorithm, payload.LineCount, "Preview", source, new Dictionary<string, string> { ["entry"] = source.SourceKind });
        }).ToArray();
    }

    private static OfficialOutputArtifactManifestSource CreateSource(OfficialOutputExportPayload payload)
    {
        if (payload.ArtifactFamily == OfficialOutputArtifactFamily.ReceiptInvoice)
        {
            return new OfficialOutputArtifactManifestSource("OfficialTextPreview", "receipt:OR-2026-000001", "OfficialReceipt", "OR-2026-000001", null, null, null, "POS-LEDGER-01", "cashier-ledger", new DateOnly(2026, 6, 26), null, null, false, "Active", CreatedAt, null);
        }

        if (payload.ArtifactFamily == OfficialOutputArtifactFamily.ReadingReport)
        {
            bool isXReading = payload.ArtifactReference.Contains("/XReading/", StringComparison.Ordinal);
            return new OfficialOutputArtifactManifestSource("OfficialReadingReportPreview", isXReading ? "reading:XR-2026-000001" : "reading:ZR-2026-000001", null, null, isXReading ? "XReading" : "ZReading", isXReading ? "XR-2026-000001" : "ZR-2026-000001", null, "POS-LEDGER-01", "cashier-ledger", new DateOnly(2026, 6, 26), new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero), null, null, null, null);
        }

        bool isAudit = payload.ArtifactReference.Contains("/AuditTrail/", StringComparison.Ordinal);
        return new OfficialOutputArtifactManifestSource("OfficialAuditEvidenceReportPreview", isAudit ? "audit:AT-2026-000001" : "audit:EJ-2026-000001", null, null, isAudit ? "AuditTrail" : "ElectronicJournal", isAudit ? "AT-2026-000001" : "EJ-2026-000001", isAudit ? "AUDIT-REF-2026-000001" : "EJ-REF-2026-000001", "POS-LEDGER-01", "cashier-ledger", new DateOnly(2026, 6, 26), new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero), null, null, null, null);
    }

    private static OfficialOutputArtifactManifestPackage BuildManifestPackage(IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return RequireValue(new OfficialOutputArtifactManifestBuilder().Build(entries, CreatedAt, new Dictionary<string, string> { ["phase"] = "09N" }));
    }

    private static OfficialOutputExportPackage BuildPackage(Fixture fixture)
    {
        return RequireValue(new OfficialOutputExportPackageBuilder().Build(fixture.ManifestPackage, fixture.Payloads));
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;
        foreach (string pathPart in pathParts) path = Path.Combine(path, pathPart);
        if (!File.Exists(path)) throw new InvalidOperationException($"Required file was not found: {path}");
        return File.ReadAllText(path);
    }

    private static T RequireValue<T>(Result<T> result)
    {
        if (result.IsFailure) throw new InvalidOperationException($"Expected 09N success, got {result.ErrorDetail.Code}: {result.Error}");
        return result.Value;
    }

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode) throw new InvalidOperationException(message);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireStartsWith(string text, string value, string message)
    {
        if (!text.StartsWith(value, StringComparison.Ordinal)) throw new InvalidOperationException(message);
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
    }

    private sealed record Fixture(OfficialOutputArtifactManifestPackage ManifestPackage, IReadOnlyList<OfficialOutputExportPayload> Payloads);
}

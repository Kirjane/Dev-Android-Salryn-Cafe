using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialInMemoryOutputDeliveryAdapterChecks
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 6, 26, 16, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestedAt =
        new(2026, 6, 26, 16, 5, 0, TimeSpan.Zero);

    private static readonly Guid RequestedByUserId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string deliveryText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialInMemoryOutputDeliveryAdapter.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string exportPackageText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputExportPackageBuilder.cs");

        VerifySourceGuards(deliveryText, coreProjectText, coreSmokeText);
        VerifyAdapterExposesInMemoryTargetOnly();
        VerifyValidPackageDeliversInMemoryResult();
        VerifyDeliveryPreservesPackageIdentity();
        VerifyDeliveryReferenceAndHashBehavior();
        VerifyPackageChangeChangesDeliveryHash();
        VerifyValidationFailures();
        VerifyMetadataNormalization();
        VerifyDeliveryMarker();
        VerifyExportPackageBehaviorRemainsIntact(exportPackageText);
    }

    private static void VerifySourceGuards(
        string deliveryText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(deliveryText, "OfficialOutputDeliveryTarget", "09M must expose delivery target.");
        RequireContains(deliveryText, "OfficialOutputDeliveryRequest", "09M must expose delivery request.");
        RequireContains(deliveryText, "OfficialOutputDeliveryArtifact", "09M must expose delivery artifact.");
        RequireContains(deliveryText, "OfficialOutputDeliveryResult", "09M must expose delivery result.");
        RequireContains(deliveryText, "OfficialOutputDeliveryAdapterContract", "09M must expose delivery adapter contract.");
        RequireContains(deliveryText, "IOfficialOutputDeliveryAdapter", "09M must expose delivery adapter interface.");
        RequireContains(deliveryText, "OfficialOutputDeliveryAdapterValidator", "09M must expose static validator.");
        RequireContains(deliveryText, "OfficialInMemoryOutputDeliveryAdapter", "09M must expose in-memory adapter.");
        RequireContains(deliveryText, "DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT", "09M must expose in-memory delivery marker.");
        RequireContains(deliveryText, "salryn://official-output-delivery/in-memory/", "09M must use stable in-memory delivery references.");
        RequireContains(deliveryText, "SHA256.HashData(Encoding.UTF8.GetBytes", "09M delivery must hash UTF-8 deterministic text.");
        RequireContains(deliveryText, "Convert.ToHexString(hash).ToLowerInvariant()", "09M delivery must use lowercase SHA-256 hex.");

        RequireContains(coreSmokeText, "J21Rmo242023OfficialInMemoryOutputDeliveryAdapterChecks.Run();", "CoreSmoke must run 09M delivery checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialOutputExportPackageChecks.Run();",
            StringComparison.Ordinal);
        int deliveryIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialInMemoryOutputDeliveryAdapterChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || deliveryIndex < 0 || deliveryIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09M must run after 09L.");
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
            "PrintDocument",
            "PrinterSettings",
            "System.Drawing.Printing",
            "PdfDocument",
            "ExportToDisk",
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
            "INSERT ",
            "UPDATE ",
            "DELETE "
        };

        foreach (string marker in forbidden)
        {
            RequireDoesNotContain(deliveryText, marker, $"09M in-memory delivery adapter must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09M must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09M must not add PDF packages to Core.");
    }

    private static void VerifyAdapterExposesInMemoryTargetOnly()
    {
        OfficialOutputDeliveryAdapterContract contract =
            OfficialInMemoryOutputDeliveryAdapter.AdapterContract;

        RequireEqual(nameof(OfficialInMemoryOutputDeliveryAdapter), contract.AdapterName, "09M adapter contract must expose stable adapter name.");
        RequireEqual(1, contract.SupportedTargets.Count, "09M adapter must expose one safe delivery target only.");
        RequireEqual(OfficialOutputDeliveryTarget.InMemory, contract.SupportedTargets[0], "09M adapter target must be in-memory.");
        RequireEqual(OfficialInMemoryOutputDeliveryAdapter.DeliverySideEffectMarker, contract.SideEffectMarker, "09M adapter contract must expose side-effect marker.");
    }

    private static void VerifyValidPackageDeliversInMemoryResult()
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());
        OfficialOutputDeliveryResult result = Deliver(package);

        RequireEqual(OfficialOutputDeliveryTarget.InMemory, result.Artifact.Target, "09M delivery target must be in-memory.");
        RequireEqual(nameof(OfficialInMemoryOutputDeliveryAdapter), result.Artifact.AdapterName, "09M delivery result must include adapter name.");
        RequireStartsWith(result.Artifact.DeliveryReference, "salryn://official-output-delivery/in-memory/", "09M delivery reference must use in-memory prefix.");
        RequireEqual("salryn://official-output-delivery/in-memory/" + result.Artifact.DeliveryHash, result.Artifact.DeliveryReference, "09M delivery reference must be hash-addressed.");
        RequireEqual(64, result.Artifact.DeliveryHash.Length, "09M delivery hash must be 64 lowercase hex characters.");
        RequireEqual(result.Artifact.DeliveryHash.ToLowerInvariant(), result.Artifact.DeliveryHash, "09M delivery hash must be lowercase.");
        RequireEqual("SHA-256", result.Artifact.HashAlgorithm, "09M delivery must expose SHA-256 algorithm.");
        RequireEqual(OfficialInMemoryOutputDeliveryAdapter.DeliverySideEffectMarker, result.Artifact.SideEffectMarker, "09M delivery result must include in-memory marker.");
        RequireEqual(OfficialInMemoryOutputDeliveryAdapter.AdapterContract, result.AdapterContract, "09M delivery result must include adapter contract.");
        RequireEqual(package, result.Package, "09M delivery result must preserve package object.");
    }

    private static void VerifyDeliveryPreservesPackageIdentity()
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());
        OfficialOutputDeliveryResult result = Deliver(package);

        RequireEqual(package.PackageReference, result.Artifact.SourcePackageReference, "09M delivery must preserve package reference.");
        RequireEqual(package.PackageHash, result.Artifact.SourcePackageHash, "09M delivery must preserve package hash.");
        RequireEqual(package.ManifestPackage.ManifestReference, result.Artifact.SourceManifestReference, "09M delivery must preserve manifest reference.");
        RequireEqual(package.ManifestPackage.ManifestHash, result.Artifact.SourceManifestHash, "09M delivery must preserve manifest hash.");
        RequireEqual(package.EntryCount, result.Artifact.EntryCount, "09M delivery must preserve entry count.");
        RequireEqual(package.PayloadCount, result.Artifact.PayloadCount, "09M delivery must preserve payload count.");
        RequireEqual(package.TotalLineCount, result.Artifact.TotalLineCount, "09M delivery must preserve total line count.");
    }

    private static void VerifyDeliveryReferenceAndHashBehavior()
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());
        OfficialOutputDeliveryRequest request = CreateRequest(package);

        OfficialOutputDeliveryResult first = Deliver(request);
        OfficialOutputDeliveryResult second = Deliver(request);

        RequireEqual(first.Artifact.DeliveryHash, second.Artifact.DeliveryHash, "09M delivery hash must be deterministic for same package and request.");
        RequireEqual(first.Artifact.DeliveryReference, second.Artifact.DeliveryReference, "09M delivery reference must be deterministic for same package and request.");
    }

    private static void VerifyPackageChangeChangesDeliveryHash()
    {
        Fixture fixture = CreateFixture();
        OfficialOutputExportPackage firstPackage = BuildPackage(fixture);

        IReadOnlyList<OfficialOutputExportPayload> changedPayloads = fixture.Payloads
            .Select(payload => string.Equals(
                payload.ArtifactReference,
                "salryn://official-text-preview/receipt",
                StringComparison.Ordinal)
                ? payload with
                {
                    Text = "receipt-line-1\nreceipt-line-2\nreceipt-line-3",
                    ContentHash = Hash("receipt-line-1\nreceipt-line-2\nreceipt-line-3"),
                    LineCount = 3
                }
                : payload)
            .ToArray();

        OfficialOutputExportPackage changedPackage = BuildPackage(new Fixture(
            BuildManifestPackage(EntriesFromPayloads(changedPayloads)),
            changedPayloads));

        OfficialOutputDeliveryResult first = Deliver(firstPackage);
        OfficialOutputDeliveryResult changed = Deliver(changedPackage);

        if (string.Equals(first.Artifact.DeliveryHash, changed.Artifact.DeliveryHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09M delivery hash must change when package text/hash changes.");
        }
    }

    private static void VerifyValidationFailures()
    {
        OfficialInMemoryOutputDeliveryAdapter adapter = new();
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());

        RequireFailureCode(
            adapter.DeliverAsync(null!).GetAwaiter().GetResult(),
            "OfficialOutputDelivery.RequestRequired",
            "09M adapter must reject null request.");

        RequireFailureCode(
            adapter.DeliverAsync(CreateRequest(null)).GetAwaiter().GetResult(),
            "OfficialOutputDelivery.PackageRequired",
            "09M adapter must reject null package.");

        RequireFailureCode(
            adapter.DeliverAsync(CreateRequest(package) with
            {
                Target = (OfficialOutputDeliveryTarget)999
            }).GetAwaiter().GetResult(),
            "OfficialOutputDelivery.UnsupportedTarget",
            "09M adapter must reject unsupported target.");

        RequireFailureCode(
            adapter.DeliverAsync(CreateRequest(package) with
            {
                RequestedByUserId = Guid.Empty
            }).GetAwaiter().GetResult(),
            "OfficialOutputDelivery.InvalidRequestedByUserId",
            "09M adapter must reject invalid requester id.");

        RequireFailureCode(
            adapter.DeliverAsync(CreateRequest(package with
            {
                PackageHash = Hash("different")
            })).GetAwaiter().GetResult(),
            "OfficialOutputDelivery.PackageHashMismatch",
            "09M adapter must reject package hash mismatch.");

        RequireFailureCode(
            adapter.DeliverAsync(CreateRequest(package with
            {
                PackageReference = "salryn://official-output-export-package/not-the-hash"
            })).GetAwaiter().GetResult(),
            "OfficialOutputDelivery.PackageReferenceMismatch",
            "09M adapter must reject package reference mismatch.");
    }

    private static void VerifyMetadataNormalization()
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());
        OfficialOutputDeliveryRequest firstRequest = CreateRequest(package) with
        {
            Metadata = new Dictionary<string, string>
            {
                [" z-key "] = " z-value ",
                ["a-key"] = " a-value "
            }
        };
        OfficialOutputDeliveryRequest secondRequest = CreateRequest(package) with
        {
            Metadata = new Dictionary<string, string>
            {
                ["a-key"] = " a-value ",
                [" z-key "] = " z-value "
            }
        };

        OfficialOutputDeliveryResult first = Deliver(firstRequest);
        OfficialOutputDeliveryResult second = Deliver(secondRequest);

        RequireEqual("a-value", first.Artifact.Metadata["a-key"], "09M delivery metadata values must be trimmed.");
        RequireEqual("z-value", first.Artifact.Metadata["z-key"], "09M delivery metadata keys must be trimmed.");
        RequireEqual(first.Artifact.DeliveryHash, second.Artifact.DeliveryHash, "09M delivery metadata normalization must be deterministic.");
        RequireEqual(first.Artifact.DeliveryReference, second.Artifact.DeliveryReference, "09M delivery metadata ordering must not destabilize references.");
    }

    private static void VerifyDeliveryMarker()
    {
        OfficialOutputDeliveryResult result = Deliver(BuildPackage(CreateFixture()));

        RequireEqual("DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT", result.Artifact.SideEffectMarker, "09M delivery marker must be present.");
    }

    private static void VerifyExportPackageBehaviorRemainsIntact(
        string exportPackageText)
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());

        RequireContains(exportPackageText, "EXPORT_PACKAGE_IN_MEMORY_ONLY", "09L export package marker must remain in source.");
        RequireEqual(OfficialOutputExportPackageKind.PreviewTextBundle, package.PackageKind, "09L package kind must remain intact.");
        RequireContains(package.TextResult.Text, "OutputSideEffectMarker: EXPORT_PACKAGE_IN_MEMORY_ONLY", "09L package text marker must remain intact.");
        RequireStartsWith(package.PackageReference, "salryn://official-output-export-package/", "09L package reference must remain intact.");
    }

    private static OfficialOutputDeliveryResult Deliver(
        OfficialOutputExportPackage package)
    {
        return Deliver(CreateRequest(package));
    }

    private static OfficialOutputDeliveryResult Deliver(
        OfficialOutputDeliveryRequest request)
    {
        return RequireValue(new OfficialInMemoryOutputDeliveryAdapter()
            .DeliverAsync(request)
            .GetAwaiter()
            .GetResult());
    }

    private static OfficialOutputDeliveryRequest CreateRequest(
        OfficialOutputExportPackage? package)
    {
        return new OfficialOutputDeliveryRequest(
            package,
            OfficialOutputDeliveryTarget.InMemory,
            RequestedByUserId,
            RequestedAt,
            "POS-DELIVERY-01",
            new Dictionary<string, string>
            {
                ["phase"] = "09M"
            });
    }

    private static Fixture CreateFixture()
    {
        IReadOnlyList<OfficialOutputExportPayload> payloads = new[]
        {
            CreatePayload(
                OfficialOutputArtifactFamily.ReceiptInvoice,
                "OFFICIAL_TEXT_PREVIEW",
                "salryn://official-text-preview/receipt",
                "receipt-line-1\nreceipt-line-2"),
            CreatePayload(
                OfficialOutputArtifactFamily.ReadingReport,
                "OFFICIAL_X_READING_PREVIEW",
                "salryn://official-reading-preview/XReading/x",
                "x-reading-line"),
            CreatePayload(
                OfficialOutputArtifactFamily.ReadingReport,
                "OFFICIAL_Z_READING_PREVIEW",
                "salryn://official-reading-preview/ZReading/z",
                "z-reading-line"),
            CreatePayload(
                OfficialOutputArtifactFamily.AuditEvidenceReport,
                "OFFICIAL_AUDIT_TRAIL_PREVIEW",
                "salryn://official-audit-evidence-preview/AuditTrail/audit",
                "audit-line"),
            CreatePayload(
                OfficialOutputArtifactFamily.AuditEvidenceReport,
                "OFFICIAL_ELECTRONIC_JOURNAL_PREVIEW",
                "salryn://official-audit-evidence-preview/ElectronicJournal/ej",
                "electronic-journal-line")
        };

        return new Fixture(
            BuildManifestPackage(EntriesFromPayloads(payloads)),
            payloads);
    }

    private static OfficialOutputExportPayload CreatePayload(
        OfficialOutputArtifactFamily family,
        string label,
        string reference,
        string text)
    {
        return new OfficialOutputExportPayload(
            family,
            label,
            reference,
            Hash(text),
            "SHA-256",
            text.Split('\n').Length,
            text);
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
                TerminalId: "POS-DELIVERY-01",
                CashierIdentifier: "cashier-delivery",
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
                TerminalId: "POS-DELIVERY-01",
                CashierIdentifier: "cashier-delivery",
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
            TerminalId: "POS-DELIVERY-01",
            CashierIdentifier: "cashier-delivery",
            BusinessDate: new DateOnly(2026, 6, 26),
            PeriodStart: new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero),
            IsCopyOrReprint: null,
            LifecycleState: null,
            CreatedAt: null,
            Metadata: null);
    }

    private static OfficialOutputArtifactManifestPackage BuildManifestPackage(
        IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return RequireValue(new OfficialOutputArtifactManifestBuilder().Build(
            entries,
            CreatedAt,
            new Dictionary<string, string>
            {
                ["phase"] = "09M"
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
            throw new InvalidOperationException($"Expected 09M success, got {result.ErrorDetail.Code}: {result.Error}");
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

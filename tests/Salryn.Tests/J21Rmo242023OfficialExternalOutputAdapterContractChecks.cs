using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialExternalOutputAdapterContractChecks
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 26, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 26, 18, 5, 0, TimeSpan.Zero);
    private static readonly Guid RequestedByUserId = Guid.Parse("cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa");

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialExternalOutputAdapterContracts.cs");
        string deliveryText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialInMemoryOutputDeliveryAdapter.cs");
        string ledgerText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialOutputDeliveryReceiptLedgerBuilder.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(contractText, deliveryText, ledgerText, coreProjectText, coreSmokeText);
        VerifyBuildsContractOnlyPlansForAllChannels();
        VerifyIdentityPreservation();
        VerifyValidationFailures();
        VerifyTextHashAndChangeBehavior();
        VerifyDeterministicOrdering();
        VerifyMetadataNormalization();
        VerifyPriorBehaviorRemainsIntact(deliveryText, ledgerText);
    }

    private static void VerifySourceGuards(string contractText, string deliveryText, string ledgerText, string coreProjectText, string coreSmokeText)
    {
        foreach (string marker in new[]
        {
            "OfficialExternalOutputChannel",
            "OfficialExternalOutputIntent",
            "OfficialExternalOutputCapability",
            "OfficialExternalOutputAdapterContract",
            "OfficialExternalOutputDispatchRequest",
            "OfficialExternalOutputDispatchPlanItem",
            "OfficialExternalOutputDispatchPlan",
            "OfficialExternalOutputDispatchPlanTextRenderResult",
            "IOfficialExternalOutputAdapter",
            "OfficialExternalOutputAdapterContractValidator",
            "OfficialExternalOutputDispatchPlanBuilder",
            "OfficialExternalOutputDispatchPlanTextRenderer",
            "SALRYN OFFICIAL EXTERNAL OUTPUT DISPATCH PLAN",
            "EXTERNAL_OUTPUT_CONTRACT_ONLY_NO_SIDE_EFFECTS",
            "salryn://official-external-output-dispatch-plan/",
            "SHA256.HashData(Encoding.UTF8.GetBytes",
            "Convert.ToHexString(hash).ToLowerInvariant()"
        })
        {
            RequireContains(contractText, marker, $"09O source must contain {marker}.");
        }

        foreach (string channel in new[] { "InMemory", "File", "Pdf", "Print", "Crystal" })
        {
            RequireContains(contractText, channel, $"09O must declare {channel} channel.");
        }

        RequireContains(deliveryText, "DELIVERY_IN_MEMORY_ONLY_NO_EXTERNAL_OUTPUT", "09M delivery marker must remain in source.");
        RequireContains(ledgerText, "DELIVERY_LEDGER_IN_MEMORY_ONLY_NO_PERSISTENCE", "09N ledger marker must remain in source.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialExternalOutputAdapterContractChecks.Run();", "CoreSmoke must run 09O checks.");

        int ledgerIndex = coreSmokeText.IndexOf("J21Rmo242023OfficialOutputDeliveryReceiptLedgerChecks.Run();", StringComparison.Ordinal);
        int externalIndex = coreSmokeText.IndexOf("J21Rmo242023OfficialExternalOutputAdapterContractChecks.Run();", StringComparison.Ordinal);
        if (ledgerIndex < 0 || externalIndex < 0 || externalIndex < ledgerIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09O must run after 09N.");
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
            RequireDoesNotContain(contractText, marker, $"09O contract boundary must not contain forbidden marker: {marker}");
        }

        RequireDoesNotContain(coreProjectText, "Crystal", "09O must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09O must not add PDF packages to Core.");
    }

    private static void VerifyBuildsContractOnlyPlansForAllChannels()
    {
        foreach (OfficialExternalOutputChannel channel in new[]
        {
            OfficialExternalOutputChannel.InMemory,
            OfficialExternalOutputChannel.File,
            OfficialExternalOutputChannel.Pdf,
            OfficialExternalOutputChannel.Print,
            OfficialExternalOutputChannel.Crystal
        })
        {
            OfficialExternalOutputDispatchPlan plan = BuildPlan(channel);
            OfficialExternalOutputDispatchPlanItem item = plan.Items[0];

            RequireEqual(1, plan.ItemCount, "09O dispatch plan must contain one item for single request.");
            RequireEqual(channel, item.Channel, "09O dispatch plan must preserve requested channel.");
            RequireEqual(OfficialExternalOutputIntent.ContractOnly, item.Intent, "09O dispatch plan must remain contract-only.");
            RequireEqual("ContractOnly", item.Status, "09O dispatch plan item status must remain contract-only.");
            RequireEqual("EXTERNAL_OUTPUT_CONTRACT_ONLY_NO_SIDE_EFFECTS", item.SideEffectMarker, "09O dispatch plan must expose no-side-effects marker.");
            RequireStartsWith(plan.DispatchPlanReference, "salryn://official-external-output-dispatch-plan/", "09O dispatch plan reference must use stable prefix.");
            RequireEqual("salryn://official-external-output-dispatch-plan/" + plan.DispatchPlanHash, plan.DispatchPlanReference, "09O dispatch plan reference must be hash-addressed.");
        }
    }

    private static void VerifyIdentityPreservation()
    {
        Pipeline pipeline = CreatePipeline();
        OfficialExternalOutputDispatchPlan plan = BuildPlan(CreateRequest(pipeline, OfficialExternalOutputChannel.Pdf));
        OfficialExternalOutputDispatchPlanItem item = plan.Items[0];

        RequireEqual(pipeline.Package.PackageReference, item.PackageReference, "09O must preserve package reference.");
        RequireEqual(pipeline.Package.PackageHash, item.PackageHash, "09O must preserve package hash.");
        RequireEqual(pipeline.Ledger.LedgerReference, item.LedgerReference, "09O must preserve ledger reference.");
        RequireEqual(pipeline.Ledger.LedgerHash, item.LedgerHash, "09O must preserve ledger hash.");
        RequireEqual(pipeline.Package.ManifestPackage.ManifestReference, item.ManifestReference, "09O must preserve manifest reference.");
        RequireEqual(pipeline.Package.ManifestPackage.ManifestHash, item.ManifestHash, "09O must preserve manifest hash.");
        RequireEqual(pipeline.Package.EntryCount, item.EntryCount, "09O must preserve entry count.");
        RequireEqual(pipeline.Package.PayloadCount, item.PayloadCount, "09O must preserve payload count.");
        RequireEqual(pipeline.Package.TotalLineCount, item.TotalLineCount, "09O must preserve total line count.");
        RequireEqual(CreatedAt, plan.CreatedAt, "09O dispatch plan must preserve created timestamp.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialExternalOutputDispatchPlanBuilder builder = new();
        Pipeline pipeline = CreatePipeline();
        OfficialExternalOutputDispatchRequest request = CreateRequest(pipeline, OfficialExternalOutputChannel.File);

        RequireFailureCode(builder.Build((OfficialExternalOutputDispatchRequest?)null), "OfficialExternalOutputDispatch.RequestRequired", "09O must reject null request.");
        RequireFailureCode(builder.Build(request with { ExportPackage = null }), "OfficialExternalOutputDispatch.ExportPackageRequired", "09O must reject null export package.");
        RequireFailureCode(builder.Build(request with { LedgerPackage = null }), "OfficialExternalOutputDispatch.LedgerPackageRequired", "09O must reject null ledger package.");
        RequireFailureCode(builder.Build(request with { ExportPackage = pipeline.Package with { PackageReference = " " } }), "OfficialExternalOutputDispatch.PackageReferenceRequired", "09O must reject empty package reference.");
        RequireFailureCode(builder.Build(request with { ExportPackage = pipeline.Package with { PackageHash = " " } }), "OfficialExternalOutputDispatch.PackageHashRequired", "09O must reject empty package hash.");
        RequireFailureCode(builder.Build(request with { LedgerPackage = pipeline.Ledger with { LedgerReference = " " } }), "OfficialExternalOutputDispatch.LedgerReferenceRequired", "09O must reject empty ledger reference.");
        RequireFailureCode(builder.Build(request with { LedgerPackage = pipeline.Ledger with { LedgerHash = " " } }), "OfficialExternalOutputDispatch.LedgerHashRequired", "09O must reject empty ledger hash.");
        RequireFailureCode(builder.Build(request with { Channel = (OfficialExternalOutputChannel)999 }), "OfficialExternalOutputDispatch.UnsupportedChannel", "09O must reject unsupported channel.");
        RequireFailureCode(builder.Build(request with { AdapterContract = CreateContract(OfficialExternalOutputChannel.InMemory) }), "OfficialExternalOutputDispatch.UnsupportedAdapterCapability", "09O must reject invalid adapter capability combination.");

        OfficialOutputDeliveryLedgerPackage mismatchedLedger = pipeline.Ledger with
        {
            Entries = pipeline.Ledger.Entries.Select(entry => entry with { PackageHash = Hash("different-package") }).ToArray()
        };
        RequireFailureCode(builder.Build(request with { LedgerPackage = mismatchedLedger }), "OfficialExternalOutputDispatch.PackageLedgerMismatch", "09O must reject package and ledger mismatch.");

        OfficialOutputDeliveryLedgerPackage markerlessLedger = pipeline.Ledger with
        {
            TextResult = pipeline.Ledger.TextResult with
            {
                Text = pipeline.Ledger.TextResult.Text.Replace("DELIVERY_LEDGER_IN_MEMORY_ONLY_NO_PERSISTENCE", "MISSING_LEDGER_MARKER", StringComparison.Ordinal)
            }
        };
        RequireFailureCode(builder.Build(request with { LedgerPackage = markerlessLedger }), "OfficialExternalOutputDispatch.LedgerMarkerRequired", "09O must reject missing 09N ledger marker.");
    }

    private static void VerifyTextHashAndChangeBehavior()
    {
        Pipeline pipeline = CreatePipeline();
        OfficialExternalOutputDispatchPlan first = BuildPlan(CreateRequest(pipeline, OfficialExternalOutputChannel.InMemory));
        OfficialExternalOutputDispatchPlan second = BuildPlan(CreateRequest(pipeline, OfficialExternalOutputChannel.InMemory));

        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09O dispatch plan text must be deterministic.");
        RequireEqual(first.DispatchPlanHash, second.DispatchPlanHash, "09O dispatch plan hash must be deterministic.");
        RequireEqual(first.DispatchPlanReference, second.DispatchPlanReference, "09O dispatch plan reference must be deterministic.");
        RequireDoesNotContain(first.TextResult.Text, "\r", "09O dispatch plan text must be LF-only.");

        foreach (string marker in new[]
        {
            "SALRYN OFFICIAL EXTERNAL OUTPUT DISPATCH PLAN",
            "[DISPATCH PLAN]",
            "[CONTRACT]",
            "[ITEMS]",
            "[EVIDENCE]",
            "EXTERNAL_OUTPUT_CONTRACT_ONLY_NO_SIDE_EFFECTS"
        })
        {
            RequireContains(first.TextResult.Text, marker, $"09O dispatch plan text must include {marker}.");
        }

        OfficialExternalOutputDispatchPlan changedChannel = BuildPlan(CreateRequest(pipeline, OfficialExternalOutputChannel.Crystal));
        if (string.Equals(first.DispatchPlanHash, changedChannel.DispatchPlanHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09O dispatch plan hash must change when channel changes.");
        }

        OfficialOutputExportPackage changedPackage = pipeline.Package with
        {
            PackageHash = Hash("changed-package"),
            PackageReference = "salryn://official-output-export-package/" + Hash("changed-package")
        };
        OfficialOutputDeliveryLedgerPackage changedLedger = pipeline.Ledger with
        {
            Entries = pipeline.Ledger.Entries.Select(entry => entry with
            {
                PackageHash = changedPackage.PackageHash,
                PackageReference = changedPackage.PackageReference
            }).ToArray()
        };
        OfficialExternalOutputDispatchPlan changedPackagePlan = BuildPlan(CreateRequest(new Pipeline(changedPackage, changedLedger), OfficialExternalOutputChannel.InMemory));
        if (string.Equals(first.DispatchPlanHash, changedPackagePlan.DispatchPlanHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09O dispatch plan hash must change when package facts change.");
        }
    }

    private static void VerifyDeterministicOrdering()
    {
        Pipeline pipeline = CreatePipeline();
        OfficialExternalOutputDispatchRequest file = CreateRequest(pipeline, OfficialExternalOutputChannel.File);
        OfficialExternalOutputDispatchRequest memory = CreateRequest(pipeline, OfficialExternalOutputChannel.InMemory);
        OfficialExternalOutputDispatchRequest print = CreateRequest(pipeline, OfficialExternalOutputChannel.Print);

        OfficialExternalOutputDispatchPlan forward = BuildPlan(new[] { memory, file, print });
        OfficialExternalOutputDispatchPlan reverse = BuildPlan(new[] { print, file, memory });

        RequireEqual(3, forward.ItemCount, "09O dispatch plan must support multiple contract-only items.");
        RequireEqual(forward.TextResult.Text, reverse.TextResult.Text, "09O dispatch plan order must be deterministic.");
        RequireEqual(forward.DispatchPlanHash, reverse.DispatchPlanHash, "09O dispatch plan hash must be deterministic regardless of request order.");
    }

    private static void VerifyMetadataNormalization()
    {
        Pipeline pipeline = CreatePipeline();
        OfficialExternalOutputDispatchRequest firstRequest = CreateRequest(pipeline, OfficialExternalOutputChannel.Pdf) with
        {
            Metadata = new Dictionary<string, string> { [" z-key "] = " z-value ", ["a-key"] = " a-value " }
        };
        OfficialExternalOutputDispatchRequest secondRequest = CreateRequest(pipeline, OfficialExternalOutputChannel.Pdf) with
        {
            Metadata = new Dictionary<string, string> { ["a-key"] = " a-value ", [" z-key "] = " z-value " }
        };
        OfficialExternalOutputDispatchPlan first = BuildPlan(firstRequest, new Dictionary<string, string> { [" z-plan "] = " z-value ", ["a-plan"] = " a-value " });
        OfficialExternalOutputDispatchPlan second = BuildPlan(secondRequest, new Dictionary<string, string> { ["a-plan"] = " a-value ", [" z-plan "] = " z-value " });

        RequireEqual("a-value", first.Metadata["a-plan"], "09O plan metadata values must be trimmed.");
        RequireEqual("z-value", first.Metadata["z-plan"], "09O plan metadata keys must be trimmed.");
        RequireEqual("a-value", first.Items[0].Metadata["a-key"], "09O item metadata values must be trimmed.");
        RequireEqual("z-value", first.Items[0].Metadata["z-key"], "09O item metadata keys must be trimmed.");
        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09O metadata normalization must be deterministic.");
        RequireEqual(first.DispatchPlanHash, second.DispatchPlanHash, "09O metadata order must not destabilize hash.");
    }

    private static void VerifyPriorBehaviorRemainsIntact(string deliveryText, string ledgerText)
    {
        Pipeline pipeline = CreatePipeline();
        OfficialOutputDeliveryResult delivery = CreateDelivery("prior-intact", "POS-09O-01");
        OfficialOutputDeliveryLedgerPackage ledger = BuildLedger(new[] { delivery });

        RequireContains(deliveryText, "OfficialInMemoryOutputDeliveryAdapter", "09M delivery adapter must remain in source.");
        RequireContains(ledgerText, "OfficialOutputDeliveryReceiptLedgerBuilder", "09N ledger builder must remain in source.");
        RequireEqual(OfficialOutputDeliveryTarget.InMemory, delivery.Artifact.Target, "09M delivery target must remain in-memory.");
        RequireStartsWith(delivery.Artifact.DeliveryReference, "salryn://official-output-delivery/in-memory/", "09M delivery reference must remain intact.");
        RequireStartsWith(ledger.LedgerReference, "salryn://official-output-delivery-ledger/", "09N ledger reference must remain intact.");
        RequireEqual(pipeline.Package.PackageReference, pipeline.Ledger.Entries[0].PackageReference, "09N ledger package identity must remain intact.");
    }

    private static OfficialExternalOutputDispatchPlan BuildPlan(OfficialExternalOutputChannel channel)
    {
        return BuildPlan(CreateRequest(CreatePipeline(), channel));
    }

    private static OfficialExternalOutputDispatchPlan BuildPlan(OfficialExternalOutputDispatchRequest request, IReadOnlyDictionary<string, string>? metadata = null)
    {
        return RequireValue(new OfficialExternalOutputDispatchPlanBuilder().Build(request, CreatedAt, metadata ?? new Dictionary<string, string> { ["phase"] = "09O" }));
    }

    private static OfficialExternalOutputDispatchPlan BuildPlan(IReadOnlyList<OfficialExternalOutputDispatchRequest> requests)
    {
        return RequireValue(new OfficialExternalOutputDispatchPlanBuilder().Build(requests, CreatedAt, new Dictionary<string, string> { ["phase"] = "09O" }));
    }

    private static OfficialExternalOutputDispatchRequest CreateRequest(Pipeline pipeline, OfficialExternalOutputChannel channel)
    {
        return new OfficialExternalOutputDispatchRequest(
            pipeline.Package,
            pipeline.Ledger,
            channel,
            OfficialExternalOutputIntent.ContractOnly,
            channel.ToString(),
            CreateContract(channel),
            RequestedAt,
            RequestedByUserId,
            "POS-09O-01",
            new Dictionary<string, string> { ["channel"] = channel.ToString() });
    }

    private static OfficialExternalOutputAdapterContract CreateContract(OfficialExternalOutputChannel channel)
    {
        return new OfficialExternalOutputAdapterContract(
            "contract-" + channel.ToString().ToLowerInvariant(),
            "ContractOnly" + channel + "Adapter",
            new[] { new OfficialExternalOutputCapability(channel, OfficialExternalOutputIntent.ContractOnly, "Contract-only capability for " + channel) },
            OfficialExternalOutputAdapterContractValidator.ContractOnlySideEffectMarker);
    }

    private static Pipeline CreatePipeline()
    {
        OfficialOutputExportPackage package = BuildPackage(CreateFixture());
        OfficialOutputDeliveryResult delivery = CreateDelivery(package, "pipeline", "POS-09O-01");
        OfficialOutputDeliveryLedgerPackage ledger = BuildLedger(new[] { delivery });
        return new Pipeline(package, ledger);
    }

    private static OfficialOutputDeliveryResult CreateDelivery(string metadataValue, string terminalId)
    {
        return CreateDelivery(BuildPackage(CreateFixture()), metadataValue, terminalId);
    }

    private static OfficialOutputDeliveryResult CreateDelivery(OfficialOutputExportPackage package, string metadataValue, string terminalId)
    {
        OfficialOutputDeliveryRequest request = new(package, OfficialOutputDeliveryTarget.InMemory, RequestedByUserId, RequestedAt, terminalId, new Dictionary<string, string> { ["delivery"] = metadataValue });
        return RequireValue(new OfficialInMemoryOutputDeliveryAdapter().DeliverAsync(request).GetAwaiter().GetResult());
    }

    private static OfficialOutputDeliveryLedgerPackage BuildLedger(IReadOnlyList<OfficialOutputDeliveryResult> deliveries)
    {
        return RequireValue(new OfficialOutputDeliveryReceiptLedgerBuilder().Build(deliveries, CreatedAt, new Dictionary<string, string> { ["phase"] = "09O" }));
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
            return new OfficialOutputArtifactManifestSource("OfficialTextPreview", "receipt:OR-2026-000001", "OfficialReceipt", "OR-2026-000001", null, null, null, "POS-09O-01", "cashier-09o", new DateOnly(2026, 6, 26), null, null, false, "Active", CreatedAt, null);
        }

        if (payload.ArtifactFamily == OfficialOutputArtifactFamily.ReadingReport)
        {
            bool isXReading = payload.ArtifactReference.Contains("/XReading/", StringComparison.Ordinal);
            return new OfficialOutputArtifactManifestSource("OfficialReadingReportPreview", isXReading ? "reading:XR-2026-000001" : "reading:ZR-2026-000001", null, null, isXReading ? "XReading" : "ZReading", isXReading ? "XR-2026-000001" : "ZR-2026-000001", null, "POS-09O-01", "cashier-09o", new DateOnly(2026, 6, 26), new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero), null, null, null, null);
        }

        bool isAudit = payload.ArtifactReference.Contains("/AuditTrail/", StringComparison.Ordinal);
        return new OfficialOutputArtifactManifestSource("OfficialAuditEvidenceReportPreview", isAudit ? "audit:AT-2026-000001" : "audit:EJ-2026-000001", null, null, isAudit ? "AuditTrail" : "ElectronicJournal", isAudit ? "AT-2026-000001" : "EJ-2026-000001", isAudit ? "AUDIT-REF-2026-000001" : "EJ-REF-2026-000001", "POS-09O-01", "cashier-09o", new DateOnly(2026, 6, 26), new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero), null, null, null, null);
    }

    private static OfficialOutputArtifactManifestPackage BuildManifestPackage(IReadOnlyList<OfficialOutputArtifactManifestEntry> entries)
    {
        return RequireValue(new OfficialOutputArtifactManifestBuilder().Build(entries, CreatedAt, new Dictionary<string, string> { ["phase"] = "09O" }));
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
        if (result.IsFailure) throw new InvalidOperationException($"Expected 09O success, got {result.ErrorDetail.Code}: {result.Error}");
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
    private sealed record Pipeline(OfficialOutputExportPackage Package, OfficialOutputDeliveryLedgerPackage Ledger);
}

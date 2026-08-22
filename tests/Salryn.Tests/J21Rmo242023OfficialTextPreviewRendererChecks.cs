using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialTextPreviewRendererChecks
{
    private static readonly Guid RequestedByUserId = Guid.Parse("25666666-6666-6666-6666-666666666661");
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string rendererText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialTextPreviewReportRenderer.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(rendererText, coreProjectText, coreSmokeText);
        VerifyRendererContract();
        VerifyValidPreviewPackage();
        VerifyDeterministicReferenceAndHash();
        VerifyContentChangeChangesHash();
        VerifyArtifactLabels();
        VerifyValidationFailures();
        VerifyPreviewTextContent();
        VerifyPriorRendererAndLayoutBehaviorRemainIntact();
    }

    private static void VerifySourceGuards(
        string rendererText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(rendererText, "OfficialTextPreviewReportRenderer : IOfficialReportRenderer", "09H must add a concrete preview renderer.");
        RequireContains(rendererText, "RenderPreviewAsync", "09H must expose a rich preview render method.");
        RequireContains(rendererText, "RenderAsync", "09H must preserve the official renderer interface method.");
        RequireContains(rendererText, "OfficialTextPreviewRenderResult", "09H must expose rich preview result.");
        RequireContains(rendererText, "OfficialTextPreviewArtifactPackage", "09H must expose preview artifact package.");
        RequireContains(rendererText, "OfficialReceiptInvoiceLayoutBuilder", "09H renderer must use the 09G layout builder.");
        RequireContains(rendererText, "OfficialReceiptInvoiceTextRenderer", "09H renderer must use the 09G text renderer.");
        RequireContains(rendererText, "OfficialReportRendererContractValidator.ValidateRequest(request)", "09H renderer must validate requests through the official validator.");
        RequireContains(rendererText, "OfficialReportRendererContractValidator.NormalizeRequest(request)", "09H renderer must normalize requests through the official validator.");
        RequireContains(rendererText, "OfficialReportRendererContractValidator.ValidateContract(RendererContract)", "09H renderer must validate its renderer contract.");
        RequireContains(rendererText, "SHA256.HashData(Encoding.UTF8.GetBytes(text))", "09H renderer must hash UTF-8 rendered text.");
        RequireContains(rendererText, "Convert.ToHexString(hash).ToLowerInvariant()", "09H renderer must pin lowercase SHA-256 hex.");
        RequireContains(rendererText, "salryn://official-text-preview/", "09H renderer must create deterministic preview artifact references.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialTextPreviewRendererChecks.Run();", "CoreSmoke must run 09H text preview renderer checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialReceiptInvoiceLayoutProofChecks.Run();",
            StringComparison.Ordinal);
        int previewIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialTextPreviewRendererChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || previewIndex < 0 || previewIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09H must run after 09G.");
        }

        RequireDoesNotContain(rendererText, "CrystalDecisions", "09H must not reference Crystal Reports.");
        RequireDoesNotContain(rendererText, ".rpt", "09H must not reference report templates.");
        RequireDoesNotContain(rendererText, "System.Windows.Forms", "09H must not reference WinForms.");
        RequireDoesNotContain(rendererText, "Salryn.Infrastructure", "09H must not reference Infrastructure.");
        RequireDoesNotContain(rendererText, "Microsoft.Data.Sqlite", "09H must not reference SQLite packages.");
        RequireDoesNotContain(rendererText, "Sqlite", "09H must not mention SQLite.");
        RequireDoesNotContain(rendererText, "SQLite", "09H must not mention SQLite.");
        RequireDoesNotContain(rendererText, "File.Write", "09H must not write files.");
        RequireDoesNotContain(rendererText, "FileStream", "09H must not open file streams.");
        RequireDoesNotContain(rendererText, "Process.Start", "09H must not launch external processes.");
        RequireDoesNotContain(rendererText, "ESC/POS", "09H must not add ESC/POS output.");
        RequireDoesNotContain(rendererText, "EscPos", "09H must not add ESC/POS output.");
        RequireDoesNotContain(rendererText, "PrintDocument", "09H must not depend on printing.");
        RequireDoesNotContain(rendererText, "PrinterSettings", "09H must not depend on printing.");
        RequireDoesNotContain(rendererText, "System.Drawing.Printing", "09H must not depend on printing.");
        RequireDoesNotContain(rendererText, "PdfDocument", "09H must not generate PDFs.");
        RequireDoesNotContain(rendererText, "ExportToDisk", "09H must not export files.");
        RequireDoesNotContain(rendererText, "GetTempPath", "09H must not create temp path output.");
        RequireDoesNotContain(rendererText, "GetTempFileName", "09H must not create temp file output.");

        RequireDoesNotContain(coreProjectText, "Crystal", "09H must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09H must not add PDF packages to Core.");
    }

    private static void VerifyRendererContract()
    {
        OfficialReportRendererContract contract = OfficialTextPreviewReportRenderer.RendererContract;

        RequireEqual(nameof(OfficialTextPreviewReportRenderer), contract.RendererName, "09H renderer name must be stable.");
        RequireEqual(1, contract.Capabilities.Count, "09H renderer must expose Preview capability only.");
        RequireEqual(OfficialReportRenderFormat.Preview, contract.Capabilities[0].Format, "09H renderer capability must be Preview.");
        RequireSuccess(
            OfficialReportRendererContractValidator.ValidateContract(contract),
            "09H renderer contract must validate through existing validator.");
    }

    private static void VerifyValidPreviewPackage()
    {
        OfficialTextPreviewRenderResult result =
            RenderPreview(CreateValidRequest(OfficialReportRenderFormat.Preview));

        RequireEqual(OfficialTextPreviewReportRenderer.RendererContract, result.RendererContract, "09H rich result must expose renderer contract.");
        RequireEqual(OfficialTextPreviewReportRenderer.RendererContract, result.ContractResult.RendererContract, "09H contract result must expose renderer contract.");
        RequireEqual(OfficialReportRenderFormat.Preview, result.ContractResult.Artifact.Format, "09H contract artifact must be Preview.");
        RequireEqual("OFFICIAL_TEXT_PREVIEW", result.ContractResult.Artifact.ArtifactLabel, "09H default artifact label must be stable.");
        RequireEqual(result.ArtifactReference, result.ContractResult.Artifact.ArtifactReference, "09H contract artifact reference must match rich result.");
        RequireEqual(RequestedAt, result.ContractResult.Artifact.CreatedAt, "09H contract artifact must preserve requested timestamp.");
        RequireEqual(result.ContractResult, result.ArtifactPackage.ContractResult, "09H package must include contract result.");
        RequireEqual(result.LayoutDocument, result.ArtifactPackage.LayoutDocument, "09H package must include layout document.");
        RequireEqual(result.TextResult, result.ArtifactPackage.TextResult, "09H package must include text result.");
        RequireEqual(result.ArtifactReference, result.ArtifactPackage.ArtifactReference, "09H package must include artifact reference.");
        RequireEqual(result.ContentHash, result.ArtifactPackage.ContentHash, "09H package must include content hash.");
        RequireEqual(result.LineCount, result.ArtifactPackage.LineCount, "09H package must include line count.");
        RequireEqual(result.RendererContract, result.ArtifactPackage.RendererContract, "09H package must include renderer contract.");
        RequireEqual(result.TextResult.Lines.Count, result.LineCount, "09H line count must match rendered text output.");
        RequireEqual(ExpectedHash(result.TextResult.Text), result.ContentHash, "09H content hash must be SHA-256 over UTF-8 text.");
        RequireEqual(result.ContentHash, result.ContentHash.ToLowerInvariant(), "09H content hash must use lowercase hex.");
        RequireEqual(64, result.ContentHash.Length, "09H SHA-256 hex hash must have 64 characters.");
        RequireStartsWith(result.ArtifactReference, "salryn://official-text-preview/", "09H artifact reference prefix must be stable.");
        RequireEqual("salryn://official-text-preview/" + result.ContentHash, result.ArtifactReference, "09H artifact reference must be hash-addressed.");

        Result<OfficialReportRenderResult> contractOnly =
            new OfficialTextPreviewReportRenderer()
                .RenderAsync(CreateValidRequest(OfficialReportRenderFormat.Preview))
                .GetAwaiter()
                .GetResult();

        RequireSuccess(contractOnly, "09H interface render method must return contract-compatible result.");
        RequireEqual(result.ContractResult, contractOnly.Value, "09H interface method must delegate to rich preview result.");
    }

    private static void VerifyDeterministicReferenceAndHash()
    {
        OfficialReportRenderRequest request = CreateValidRequest(OfficialReportRenderFormat.Preview);
        OfficialTextPreviewRenderResult first = RenderPreview(request);
        OfficialTextPreviewRenderResult second = RenderPreview(request);

        RequireEqual(first.ContentHash, second.ContentHash, "09H content hash must be deterministic for same input.");
        RequireEqual(first.ArtifactReference, second.ArtifactReference, "09H artifact reference must be deterministic for same input.");
        RequireEqual(first.TextResult.Text, second.TextResult.Text, "09H rendered text must be deterministic for same input.");
    }

    private static void VerifyContentChangeChangesHash()
    {
        OfficialTextPreviewRenderResult first =
            RenderPreview(CreateValidRequest(OfficialReportRenderFormat.Preview));
        OfficialTextPreviewRenderResult changed =
            RenderPreview(CreateValidRequest(
                OfficialReportRenderFormat.Preview,
                sourceDocument: CreateBindingDocument(firstItemDescription: "CHARLIE TEA")));

        if (string.Equals(first.ContentHash, changed.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09H content hash must change when rendered text changes.");
        }

        if (string.Equals(first.ArtifactReference, changed.ArtifactReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("09H artifact reference must change when rendered text changes.");
        }
    }

    private static void VerifyArtifactLabels()
    {
        OfficialTextPreviewRenderResult defaultLabel =
            RenderPreview(CreateValidRequest(OfficialReportRenderFormat.Preview) with
            {
                ArtifactLabel = null
            });

        RequireEqual("OFFICIAL_TEXT_PREVIEW", defaultLabel.ContractResult.Artifact.ArtifactLabel, "09H default label must be stable.");

        OfficialTextPreviewRenderResult customLabel =
            RenderPreview(CreateValidRequest(OfficialReportRenderFormat.Preview) with
            {
                ArtifactLabel = "  CUSTOM PREVIEW LABEL  "
            });

        RequireEqual("CUSTOM PREVIEW LABEL", customLabel.ContractResult.Artifact.ArtifactLabel, "09H custom artifact label must be normalized.");
    }

    private static void VerifyValidationFailures()
    {
        OfficialTextPreviewReportRenderer renderer = new();

        RequireFailureCode(
            renderer.RenderPreviewAsync(null!).GetAwaiter().GetResult(),
            "OfficialReportRenderer.RequestRequired",
            "09H renderer must reject null requests through existing validator.");

        RequireFailureCode(
            renderer.RenderPreviewAsync(CreateValidRequest(
                OfficialReportRenderFormat.Preview,
                sourceDocument: CreateBindingDocumentWithout("Evidence"))).GetAwaiter().GetResult(),
            "OfficialReportRenderer.RequiredBindingTableMissing",
            "09H renderer must reject malformed binding documents through existing validator.");

        RequireFailureCode(
            renderer.RenderPreviewAsync(CreateValidRequest(OfficialReportRenderFormat.Pdf)).GetAwaiter().GetResult(),
            "OfficialTextPreview.UnsupportedFormat",
            "09H renderer must reject PDF requests.");

        RequireFailureCode(
            renderer.RenderPreviewAsync(CreateValidRequest(OfficialReportRenderFormat.PrintJob)).GetAwaiter().GetResult(),
            "OfficialTextPreview.UnsupportedFormat",
            "09H renderer must reject print-job requests.");

        RequireFailureCode(
            renderer.RenderPreviewAsync(CreateValidRequest(OfficialReportRenderFormat.Export)).GetAwaiter().GetResult(),
            "OfficialTextPreview.UnsupportedFormat",
            "09H renderer must reject export requests.");
    }

    private static void VerifyPreviewTextContent()
    {
        OfficialTextPreviewRenderResult result =
            RenderPreview(CreateValidRequest(OfficialReportRenderFormat.Preview));

        RequireContains(result.TextResult.Text, "[ITEMS]", "09H preview text must include items section.");
        RequireContains(result.TextResult.Text, "LineItem: LineNumber=1 | Description=ALPHA COFFEE", "09H preview text must preserve first item order.");
        RequireContains(result.TextResult.Text, "LineItem: LineNumber=2 | Description=BRAVO MILK", "09H preview text must preserve second item order.");
        RequireContains(result.TextResult.Text, "[PAYMENTS]", "09H preview text must include payments section.");
        RequireContains(result.TextResult.Text, "Payment: PaymentNumber=1 | Method=Cash", "09H preview text must preserve first payment order.");
        RequireContains(result.TextResult.Text, "Payment: PaymentNumber=2 | Method=Card", "09H preview text must preserve second payment order.");
        RequireContains(result.TextResult.Text, "[LIFECYCLE]", "09H preview text must include lifecycle section.");
        RequireContains(result.TextResult.Text, "State: Active", "09H preview text must include lifecycle state.");
        RequireContains(result.TextResult.Text, "[COPY]", "09H preview text must include copy audit section.");
        RequireContains(result.TextResult.Text, "OutputMarker: ORIGINAL", "09H preview text must include copy output marker.");
        RequireContains(result.TextResult.Text, "[EVIDENCE]", "09H preview text must include evidence section.");
        RequireContains(result.TextResult.Text, "SnapshotPayloadHash: SHA256:09H-SNAPSHOT-HASH", "09H preview text must include evidence hash.");
    }

    private static void VerifyPriorRendererAndLayoutBehaviorRemainIntact()
    {
        OfficialNoOutputReportRenderer noOutputRenderer = new();
        Result<OfficialReportRenderResult> noOutputResult =
            noOutputRenderer.RenderAsync(CreateValidRequest(OfficialReportRenderFormat.Preview) with
            {
                ArtifactLabel = null,
                RequestedAt = null
            }).GetAwaiter().GetResult();

        RequireSuccess(noOutputResult, "09H must not break 09F no-output renderer.");
        RequireEqual("SALRYN official preview placeholder (10 binding tables)", noOutputResult.Value.Artifact.ArtifactLabel, "09H must preserve no-output renderer behavior.");

        Result<OfficialReceiptInvoiceLayoutDocument> layoutResult =
            new OfficialReceiptInvoiceLayoutBuilder().Build(CreateBindingDocument());

        RequireSuccess(layoutResult, "09H must not break 09G layout builder.");
        RequireEqual(8, layoutResult.Value.Sections.Count, "09H must preserve 09G section count.");

        Result<OfficialReceiptInvoiceTextRenderResult> textResult =
            new OfficialReceiptInvoiceTextRenderer().Render(layoutResult.Value);

        RequireSuccess(textResult, "09H must not break 09G text renderer.");
        RequireContains(textResult.Value.Text, "SALRYN OFFICIAL RECEIPT/INVOICE LAYOUT PROOF", "09H must preserve 09G text output title.");
    }

    private static OfficialTextPreviewRenderResult RenderPreview(
        OfficialReportRenderRequest request)
    {
        Result<OfficialTextPreviewRenderResult> result =
            new OfficialTextPreviewReportRenderer()
                .RenderPreviewAsync(request)
                .GetAwaiter()
                .GetResult();

        return RequireValue(result);
    }

    private static OfficialReportRenderRequest CreateValidRequest(
        OfficialReportRenderFormat format,
        OfficialReportBindingDocument? sourceDocument = null)
    {
        return new OfficialReportRenderRequest(
            SourceDocument: sourceDocument ?? CreateBindingDocument(),
            Format: format,
            ArtifactLabel: format == OfficialReportRenderFormat.Preview ? null : "OUTPUT-INTENT-LABEL",
            RequestedByUserId,
            RequestedAt,
            TerminalId: "POS-RENDER",
            CopyModeMarker: "ORIGINAL",
            Metadata: new Dictionary<string, string>
            {
                [" phase "] = " 09H "
            });
    }

    private static OfficialReportBindingDocument CreateBindingDocument(
        string firstItemDescription = "ALPHA COFFEE")
    {
        return new OfficialReportBindingDocument(new[]
        {
            Table("Header", Row(
                ("DocumentKind", "OfficialReceipt"),
                ("OfficialDocumentNumber", "OR-2026-000001"),
                ("TransactionTimestamp", "2026-06-26T12:00:00.0000000+00:00"),
                ("JournalEntryId", "11111111-1111-1111-1111-111111111111"),
                ("SaleId", "22222222-2222-2222-2222-222222222222"),
                ("CashierUserId", "33333333-3333-3333-3333-333333333333"),
                ("CashierIdentifier", "cashier-a"),
                ("TerminalId", "POS-01"))),
            Table("MachineProfile", Row(
                ("BirMachineProfileId", "44444444-4444-4444-4444-444444444444"),
                ("RegistrationScopeCode", "STORE"),
                ("RegisteredTaxpayerName", "KIRCH-NAIRU TRADING"),
                ("BusinessName", "SALRYN TEST STORE"),
                ("RegisteredAddress", "123 Sample Registered Address"),
                ("BranchAddress", "456 Sample Branch Address"),
                ("TaxpayerIdentificationNumber", "123-456-789-000"),
                ("BranchCode", "001"),
                ("MachineIdentificationNumber", "MIN-09H-001"),
                ("MachineSerialNumber", "MSN-09H-001"),
                ("PosTerminalNumber", "PTN-09H-001"),
                ("AccreditationNumber", "ACC-09H-001"),
                ("PermitToUseNumber", "PTU-09H-001"),
                ("SoftwareName", "SALRYN"),
                ("SoftwareVersion", "0.9H"),
                ("SoftwareBuild", "fdecac2"),
                ("ComplianceMode", "FoundationOnly"))),
            Table("LineItems",
                Row(
                    ("LineNumber", "1"),
                    ("Description", firstItemDescription),
                    ("Barcode", "480000000001"),
                    ("UnitName", "pc"),
                    ("QuantityX1000", "1000"),
                    ("UnitPriceCents", "12500"),
                    ("DiscountCents", "0"),
                    ("LineTotalCents", "12500")),
                Row(
                    ("LineNumber", "2"),
                    ("Description", "BRAVO MILK"),
                    ("Barcode", "480000000002"),
                    ("UnitName", "pc"),
                    ("QuantityX1000", "2000"),
                    ("UnitPriceCents", "9900"),
                    ("DiscountCents", "100"),
                    ("LineTotalCents", "19800"))),
            Table("Payments",
                Row(
                    ("PaymentNumber", "1"),
                    ("Method", "Cash"),
                    ("AmountCents", "20000"),
                    ("PaidAt", "2026-06-26T12:01:00.0000000+00:00")),
                Row(
                    ("PaymentNumber", "2"),
                    ("Method", "Card"),
                    ("AmountCents", "20000"),
                    ("PaidAt", "2026-06-26T12:02:00.0000000+00:00"))),
            Table("Totals", Row(
                ("SubtotalCents", "32300"),
                ("DiscountCents", "100"),
                ("TaxCents", "3450"),
                ("TotalCents", "35650"),
                ("AmountPaidCents", "40000"),
                ("ChangeCents", "4350"),
                ("TaxModeCode", "VAT-DEFERRED"),
                ("VatBucketEngineDeferred", "true"),
                ("TaxModeNotes", "Deterministic cents only."),
                ("DeferredComplianceNotes", "VAT bucket engine deferred for 09H proof."))),
            Table("Evidence", Row(
                ("OriginalEventId", "55555555-5555-5555-5555-555555555555"),
                ("SnapshotPayloadHash", "SHA256:09H-SNAPSHOT-HASH"),
                ("SnapshotHashAlgorithm", "SHA-256"),
                ("SnapshotHashMatches", "true"))),
            Table("Lifecycle", Row(
                ("State", "Active"),
                ("NoticeCode", "ACTIVE-NOTICE"),
                ("NoticeText", "Active lifecycle notice."),
                ("CorrectionCount", "1"),
                ("IsCoveredByFinalizedZReading", "false"),
                ("CoveringZReadingId", ""))),
            Table("Corrections", Row(
                ("CorrectionNumber", "1"),
                ("CorrectionNote", "First correction note."))),
            Table("LifecycleIssues"),
            Table("CopyAudit", Row(
                ("CopyMode", "ORIGINAL"),
                ("OutputMarker", "ORIGINAL"),
                ("IsCopyOrReprint", "false"),
                ("PriorReprintEventCount", "0")))
        });
    }

    private static OfficialReportBindingDocument CreateBindingDocumentWithout(
        string tableName)
    {
        return new OfficialReportBindingDocument(CreateBindingDocument().Tables
            .Where(table => !string.Equals(table.Name, tableName, StringComparison.Ordinal))
            .ToArray());
    }

    private static OfficialReportBindingTable Table(
        string tableName,
        params IReadOnlyDictionary<string, string>[] rows)
    {
        IReadOnlyList<string> columns = OfficialReportRendererContractValidator.RequiredTableColumns[tableName];

        return new OfficialReportBindingTable(
            tableName,
            columns,
            rows.Select(row => new OfficialReportBindingRow(columns
                    .Select(column => new OfficialReportBindingCell(
                        column,
                        row.TryGetValue(column, out string? value) ? value : string.Empty))
                    .ToArray()))
                .ToArray());
    }

    private static IReadOnlyDictionary<string, string> Row(
        params (string Key, string Value)[] values)
    {
        return values.ToDictionary(
            static value => value.Key,
            static value => value.Value,
            StringComparer.Ordinal);
    }

    private static string ExpectedHash(string text)
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
            throw new InvalidOperationException($"Expected 09H success, got {result.ErrorDetail.Code}: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
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

using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialReceiptInvoiceLayoutProofChecks
{
    private static readonly IReadOnlyList<string> RequiredSections = new[]
    {
        "TaxpayerHeader",
        "DocumentHeader",
        "LineItems",
        "Totals",
        "Payments",
        "LifecycleNotice",
        "CopyAudit",
        "EvidenceFooter"
    };

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string layoutText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialReceiptInvoiceLayoutBuilder.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        VerifySourceGuards(layoutText, coreProjectText, coreSmokeText);
        VerifyValidBindingBuildsAllRequiredSections();
        VerifyTaxpayerHeaderIncludesMachineProfileFacts();
        VerifyDocumentHeaderIncludesOfficialIdentity();
        VerifyLineItemOrderIsPreserved();
        VerifyPaymentOrderIsPreserved();
        VerifyTotalsIncludeRequiredCentFields();
        VerifyLifecycleStatesAndIssues();
        VerifyCorrectionOrderIsPreserved();
        VerifyCopyAuditMarkers();
        VerifyEvidenceFooterIncludesHashFacts();
        VerifyTextRendererIsDeterministic();
        VerifyTextRendererSectionMarkers();
        VerifyBuilderRejectsInvalidBindingDocuments();
    }

    private static void VerifySourceGuards(
        string layoutText,
        string coreProjectText,
        string coreSmokeText)
    {
        RequireContains(layoutText, "OfficialReceiptInvoiceLayoutDocument", "09G must expose a layout document DTO.");
        RequireContains(layoutText, "OfficialReceiptInvoiceLayoutSection", "09G must expose layout sections.");
        RequireContains(layoutText, "OfficialReceiptInvoiceLayoutLine", "09G must expose layout lines.");
        RequireContains(layoutText, "OfficialReceiptInvoiceLayoutBuilder", "09G must expose a layout builder.");
        RequireContains(layoutText, "OfficialReceiptInvoiceTextRenderResult", "09G must expose a text render result.");
        RequireContains(layoutText, "OfficialReceiptInvoiceTextRenderer", "09G must expose a text renderer.");
        RequireContains(layoutText, "OfficialReportBindingDocument? bindingDocument", "09G builder must consume only official report binding documents.");
        RequireContains(layoutText, "OfficialReportRendererContractValidator.ValidateBindingDocument(bindingDocument)", "09G builder must validate with the existing hardened binding validator.");
        RequireContains(layoutText, "SALRYN OFFICIAL RECEIPT/INVOICE LAYOUT PROOF", "09G text renderer must include deterministic output title.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialReceiptInvoiceLayoutProofChecks.Run();", "CoreSmoke must run 09G layout proof checks.");

        int previousIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialRendererImplementationShellChecks.Run();",
            StringComparison.Ordinal);
        int layoutIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialReceiptInvoiceLayoutProofChecks.Run();",
            StringComparison.Ordinal);

        if (previousIndex < 0 || layoutIndex < 0 || layoutIndex < previousIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09G must run after 09F.");
        }

        RequireDoesNotContain(layoutText, "OfficialPrintableDocument", "09G layout builder must not consume printable documents directly.");
        RequireDoesNotContain(layoutText, "CrystalDecisions", "09G must not reference Crystal Reports.");
        RequireDoesNotContain(layoutText, ".rpt", "09G must not reference report templates.");
        RequireDoesNotContain(layoutText, "System.Windows.Forms", "09G must not reference WinForms.");
        RequireDoesNotContain(layoutText, "Salryn.Infrastructure", "09G must not reference Infrastructure.");
        RequireDoesNotContain(layoutText, "Microsoft.Data.Sqlite", "09G must not reference SQLite packages.");
        RequireDoesNotContain(layoutText, "Sqlite", "09G must not mention SQLite.");
        RequireDoesNotContain(layoutText, "SQLite", "09G must not mention SQLite.");
        RequireDoesNotContain(layoutText, "File.Write", "09G must not write files.");
        RequireDoesNotContain(layoutText, "FileStream", "09G must not open file streams.");
        RequireDoesNotContain(layoutText, "Process.Start", "09G must not launch external processes.");
        RequireDoesNotContain(layoutText, "ESC/POS", "09G must not add ESC/POS output.");
        RequireDoesNotContain(layoutText, "EscPos", "09G must not add ESC/POS output.");
        RequireDoesNotContain(layoutText, "PrintDocument", "09G must not depend on printing.");
        RequireDoesNotContain(layoutText, "PrinterSettings", "09G must not depend on printing.");
        RequireDoesNotContain(layoutText, "System.Drawing.Printing", "09G must not depend on printing.");
        RequireDoesNotContain(layoutText, "PdfDocument", "09G must not generate PDFs.");
        RequireDoesNotContain(layoutText, "ExportToDisk", "09G must not export files.");

        RequireDoesNotContain(coreProjectText, "Crystal", "09G must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09G must not add PDF packages to Core.");
    }

    private static void VerifyValidBindingBuildsAllRequiredSections()
    {
        OfficialReceiptInvoiceLayoutDocument layout = BuildLayout(CreateBindingDocument());

        RequireEqual(RequiredSections.Count, layout.Sections.Count, "09G layout must contain exactly the required sections.");

        for (int index = 0; index < RequiredSections.Count; index++)
        {
            RequireEqual(RequiredSections[index], layout.Sections[index].Name, "09G layout section order must be stable.");
        }
    }

    private static void VerifyTaxpayerHeaderIncludesMachineProfileFacts()
    {
        OfficialReceiptInvoiceLayoutSection taxpayer =
            RequireSection(BuildLayout(CreateBindingDocument()), "TaxpayerHeader");

        RequireLineValue(taxpayer, "RegisteredTaxpayerName", "KIRCH-NAIRU TRADING");
        RequireLineValue(taxpayer, "BusinessName", "SALRYN TEST STORE");
        RequireLineValue(taxpayer, "RegisteredAddress", "123 Sample Registered Address");
        RequireLineValue(taxpayer, "BranchAddress", "456 Sample Branch Address");
        RequireLineValue(taxpayer, "TaxpayerIdentificationNumber", "123-456-789-000");
        RequireLineValue(taxpayer, "BranchCode", "001");
        RequireLineValue(taxpayer, "MachineIdentificationNumber", "MIN-09G-001");
        RequireLineValue(taxpayer, "MachineSerialNumber", "MSN-09G-001");
        RequireLineValue(taxpayer, "PosTerminalNumber", "PTN-09G-001");
        RequireLineValue(taxpayer, "AccreditationNumber", "ACC-09G-001");
        RequireLineValue(taxpayer, "PermitToUseNumber", "PTU-09G-001");
        RequireLineValue(taxpayer, "SoftwareName", "SALRYN");
        RequireLineValue(taxpayer, "SoftwareVersion", "0.9G");
        RequireLineValue(taxpayer, "SoftwareBuild", "b148f13");
    }

    private static void VerifyDocumentHeaderIncludesOfficialIdentity()
    {
        OfficialReceiptInvoiceLayoutSection document =
            RequireSection(BuildLayout(CreateBindingDocument()), "DocumentHeader");

        RequireLineValue(document, "DocumentKind", "OfficialReceipt");
        RequireLineValue(document, "OfficialDocumentNumber", "OR-2026-000001");
        RequireLineValue(document, "TransactionTimestamp", "2026-06-26T12:00:00.0000000+00:00");
        RequireLineValue(document, "CashierIdentifier", "cashier-a");
        RequireLineValue(document, "TerminalId", "POS-01");
    }

    private static void VerifyLineItemOrderIsPreserved()
    {
        OfficialReceiptInvoiceLayoutSection items =
            RequireSection(BuildLayout(CreateBindingDocument()), "LineItems");

        RequireEqual(2, items.Lines.Count, "09G layout must preserve the sample item count.");
        RequireContains(items.Lines[0].Value, "LineNumber=1", "First item must keep line number 1.");
        RequireContains(items.Lines[0].Value, "Description=ALPHA COFFEE", "First item must keep first description.");
        RequireContains(items.Lines[0].Value, "Barcode=480000000001", "First item must include barcode when available.");
        RequireContains(items.Lines[0].Value, "QuantityX1000=1000", "First item must include quantity x1000.");
        RequireContains(items.Lines[0].Value, "UnitPriceCents=12500", "First item must include unit price cents.");
        RequireContains(items.Lines[0].Value, "DiscountCents=0", "First item must include discount cents.");
        RequireContains(items.Lines[0].Value, "LineTotalCents=12500", "First item must include line total cents.");

        RequireContains(items.Lines[1].Value, "LineNumber=2", "Second item must keep line number 2.");
        RequireContains(items.Lines[1].Value, "Description=BRAVO MILK", "Second item must keep second description.");
    }

    private static void VerifyPaymentOrderIsPreserved()
    {
        OfficialReceiptInvoiceLayoutSection payments =
            RequireSection(BuildLayout(CreateBindingDocument()), "Payments");

        RequireEqual(2, payments.Lines.Count, "09G layout must preserve the sample payment count.");
        RequireContains(payments.Lines[0].Value, "PaymentNumber=1", "First payment must keep payment number 1.");
        RequireContains(payments.Lines[0].Value, "Method=Cash", "First payment must keep payment method.");
        RequireContains(payments.Lines[0].Value, "AmountCents=20000", "First payment must include amount cents.");
        RequireContains(payments.Lines[0].Value, "PaidAt=2026-06-26T12:01:00.0000000+00:00", "First payment must include timestamp.");

        RequireContains(payments.Lines[1].Value, "PaymentNumber=2", "Second payment must keep payment number 2.");
        RequireContains(payments.Lines[1].Value, "Method=Card", "Second payment must keep payment method.");
    }

    private static void VerifyTotalsIncludeRequiredCentFields()
    {
        OfficialReceiptInvoiceLayoutSection totals =
            RequireSection(BuildLayout(CreateBindingDocument()), "Totals");

        RequireLineValue(totals, "SubtotalCents", "32300");
        RequireLineValue(totals, "DiscountCents", "100");
        RequireLineValue(totals, "TaxCents", "3450");
        RequireLineValue(totals, "TotalCents", "35650");
        RequireLineValue(totals, "AmountPaidCents", "40000");
        RequireLineValue(totals, "ChangeCents", "4350");
        RequireLineValue(totals, "TaxModeCode", "VAT-DEFERRED");
        RequireLineValue(totals, "VatBucketEngineDeferred", "true");
        RequireLineValue(totals, "DeferredComplianceNotes", "VAT bucket engine deferred for 09G proof.");
    }

    private static void VerifyLifecycleStatesAndIssues()
    {
        foreach (string state in new[] { "Active", "Voided", "Cancelled" })
        {
            OfficialReceiptInvoiceLayoutSection lifecycle =
                RequireSection(BuildLayout(CreateBindingDocument(lifecycleState: state)), "LifecycleNotice");

            RequireLineValue(lifecycle, "State", state);
            RequireLineValue(lifecycle, "NoticeCode", state.ToUpperInvariant() + "-NOTICE");
            RequireLineValue(lifecycle, "NoticeText", state + " lifecycle notice.");
        }

        OfficialReceiptInvoiceLayoutSection invalid =
            RequireSection(BuildLayout(CreateBindingDocument(
                lifecycleState: "Invalid",
                lifecycleIssues: new[]
                {
                    ("1", "INVALID_SNAPSHOT", "Snapshot hash mismatch."),
                    ("2", "LIFECYCLE_GAP", "Lifecycle event gap.")
                })), "LifecycleNotice");

        RequireLineValue(invalid, "State", "Invalid");

        OfficialReceiptInvoiceLayoutLine[] issues = invalid.Lines
            .Where(line => string.Equals(line.Label, "LifecycleIssue", StringComparison.Ordinal))
            .ToArray();

        RequireEqual(2, issues.Length, "Invalid lifecycle proof must include lifecycle issues.");
        RequireContains(issues[0].Value, "IssueNumber=1", "First issue order must be preserved.");
        RequireContains(issues[0].Value, "Code=INVALID_SNAPSHOT", "First issue code must be preserved.");
        RequireContains(issues[1].Value, "IssueNumber=2", "Second issue order must be preserved.");
        RequireContains(issues[1].Value, "Code=LIFECYCLE_GAP", "Second issue code must be preserved.");
    }

    private static void VerifyCorrectionOrderIsPreserved()
    {
        OfficialReceiptInvoiceLayoutSection lifecycle =
            RequireSection(BuildLayout(CreateBindingDocument(
                correctionNotes: new[] { "First correction note.", "Second correction note." })), "LifecycleNotice");

        OfficialReceiptInvoiceLayoutLine[] corrections = lifecycle.Lines
            .Where(line => string.Equals(line.Label, "CorrectionNote", StringComparison.Ordinal))
            .ToArray();

        RequireEqual(2, corrections.Length, "09G layout must include correction notes.");
        RequireEqual(
            "CorrectionNumber=1 | CorrectionNote=First correction note.",
            corrections[0].Value,
            "First correction note order must be preserved.");
        RequireEqual(
            "CorrectionNumber=2 | CorrectionNote=Second correction note.",
            corrections[1].Value,
            "Second correction note order must be preserved.");
    }

    private static void VerifyCopyAuditMarkers()
    {
        foreach (string marker in new[] { "ORIGINAL", "COPY", "COPY/REPRINT" })
        {
            OfficialReceiptInvoiceLayoutSection copy =
                RequireSection(BuildLayout(CreateBindingDocument(copyMode: marker, outputMarker: marker)), "CopyAudit");

            RequireLineValue(copy, "CopyMode", marker);
            RequireLineValue(copy, "OutputMarker", marker);
            RequireLineValue(copy, "IsCopyOrReprint", marker == "ORIGINAL" ? "false" : "true");
        }
    }

    private static void VerifyEvidenceFooterIncludesHashFacts()
    {
        OfficialReceiptInvoiceLayoutSection evidence =
            RequireSection(BuildLayout(CreateBindingDocument()), "EvidenceFooter");

        RequireLineValue(evidence, "OriginalEventId", "55555555-5555-5555-5555-555555555555");
        RequireLineValue(evidence, "SnapshotPayloadHash", "SHA256:09G-SNAPSHOT-HASH");
        RequireLineValue(evidence, "SnapshotHashAlgorithm", "SHA-256");
        RequireLineValue(evidence, "SnapshotHashMatches", "true");
    }

    private static void VerifyTextRendererIsDeterministic()
    {
        OfficialReceiptInvoiceLayoutDocument layout = BuildLayout(CreateBindingDocument());
        OfficialReceiptInvoiceTextRenderer renderer = new();

        Result<OfficialReceiptInvoiceTextRenderResult> first = renderer.Render(layout);
        Result<OfficialReceiptInvoiceTextRenderResult> second = renderer.Render(layout);

        RequireSuccess(first, "09G text renderer must render valid layout.");
        RequireSuccess(second, "09G text renderer must render valid layout repeatedly.");
        RequireEqual(first.Value.Text, second.Value.Text, "09G text renderer output must be deterministic.");
        RequireEqual(first.Value.Lines.Count, second.Value.Lines.Count, "09G text renderer line count must be deterministic.");
        RequireContains(first.Value.Text, "SALRYN OFFICIAL RECEIPT/INVOICE LAYOUT PROOF", "09G text output must include title.");
        RequireDoesNotContain(first.Value.Text, "\r", "09G text output must use stable line feeds.");
        RequireContains(first.Value.Text, "LineItem: LineNumber=1 | Description=ALPHA COFFEE", "09G text output must include first item in order.");
        RequireContains(first.Value.Text, "Payment: PaymentNumber=1 | Method=Cash", "09G text output must include first payment in order.");
    }

    private static void VerifyTextRendererSectionMarkers()
    {
        OfficialReceiptInvoiceTextRenderResult text =
            RequireValue(new OfficialReceiptInvoiceTextRenderer().Render(BuildLayout(CreateBindingDocument())));

        foreach (string marker in new[]
        {
            "[TAXPAYER]",
            "[DOCUMENT]",
            "[ITEMS]",
            "[TOTALS]",
            "[PAYMENTS]",
            "[LIFECYCLE]",
            "[COPY]",
            "[EVIDENCE]"
        })
        {
            if (!text.Lines.Contains(marker))
            {
                throw new InvalidOperationException($"09G text renderer is missing section marker {marker}.");
            }
        }
    }

    private static void VerifyBuilderRejectsInvalidBindingDocuments()
    {
        OfficialReceiptInvoiceLayoutBuilder builder = new();

        RequireFailureCode(
            builder.Build(null),
            "OfficialReportRenderer.SourceDocumentRequired",
            "09G builder must reject null binding documents through the existing validator.");

        RequireFailureCode(
            builder.Build(CreateBindingDocumentWithout("Evidence")),
            "OfficialReportRenderer.RequiredBindingTableMissing",
            "09G builder must reject malformed binding documents through the existing validator.");
    }

    private static OfficialReceiptInvoiceLayoutDocument BuildLayout(
        OfficialReportBindingDocument document)
    {
        return RequireValue(new OfficialReceiptInvoiceLayoutBuilder().Build(document));
    }

    private static OfficialReportBindingDocument CreateBindingDocument(
        string lifecycleState = "Active",
        IReadOnlyList<string>? correctionNotes = null,
        IReadOnlyList<(string Number, string Code, string Message)>? lifecycleIssues = null,
        string copyMode = "ORIGINAL",
        string outputMarker = "ORIGINAL")
    {
        string[] safeCorrectionNotes = (correctionNotes ?? new[] { "First correction note.", "Second correction note." })
            .ToArray();
        IReadOnlyList<(string Number, string Code, string Message)> safeLifecycleIssues =
            lifecycleIssues ?? Array.Empty<(string Number, string Code, string Message)>();
        string isCopyOrReprint = string.Equals(outputMarker, "ORIGINAL", StringComparison.Ordinal) ? "false" : "true";

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
                ("MachineIdentificationNumber", "MIN-09G-001"),
                ("MachineSerialNumber", "MSN-09G-001"),
                ("PosTerminalNumber", "PTN-09G-001"),
                ("AccreditationNumber", "ACC-09G-001"),
                ("PermitToUseNumber", "PTU-09G-001"),
                ("SoftwareName", "SALRYN"),
                ("SoftwareVersion", "0.9G"),
                ("SoftwareBuild", "b148f13"),
                ("ComplianceMode", "FoundationOnly"))),
            Table("LineItems",
                Row(
                    ("LineNumber", "1"),
                    ("Description", "ALPHA COFFEE"),
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
                ("DeferredComplianceNotes", "VAT bucket engine deferred for 09G proof."))),
            Table("Evidence", Row(
                ("OriginalEventId", "55555555-5555-5555-5555-555555555555"),
                ("SnapshotPayloadHash", "SHA256:09G-SNAPSHOT-HASH"),
                ("SnapshotHashAlgorithm", "SHA-256"),
                ("SnapshotHashMatches", "true"))),
            Table("Lifecycle", Row(
                ("State", lifecycleState),
                ("NoticeCode", lifecycleState.ToUpperInvariant() + "-NOTICE"),
                ("NoticeText", lifecycleState + " lifecycle notice."),
                ("CorrectionCount", safeCorrectionNotes.Length.ToString()),
                ("IsCoveredByFinalizedZReading", "false"),
                ("CoveringZReadingId", ""))),
            Table("Corrections", safeCorrectionNotes
                .Select((note, index) => Row(
                    ("CorrectionNumber", (index + 1).ToString()),
                    ("CorrectionNote", note)))
                .ToArray()),
            Table("LifecycleIssues", safeLifecycleIssues
                .Select(issue => Row(
                    ("IssueNumber", issue.Number),
                    ("Code", issue.Code),
                    ("Message", issue.Message)))
                .ToArray()),
            Table("CopyAudit", Row(
                ("CopyMode", copyMode),
                ("OutputMarker", outputMarker),
                ("IsCopyOrReprint", isCopyOrReprint),
                ("PriorReprintEventCount", isCopyOrReprint == "true" ? "1" : "0")))
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

    private static OfficialReceiptInvoiceLayoutSection RequireSection(
        OfficialReceiptInvoiceLayoutDocument document,
        string sectionName)
    {
        OfficialReceiptInvoiceLayoutSection? section =
            document.Sections.FirstOrDefault(item => string.Equals(item.Name, sectionName, StringComparison.Ordinal));

        if (section is null)
        {
            throw new InvalidOperationException($"Expected 09G layout section {sectionName}.");
        }

        return section;
    }

    private static void RequireLineValue(
        OfficialReceiptInvoiceLayoutSection section,
        string label,
        string expectedValue)
    {
        OfficialReceiptInvoiceLayoutLine? line =
            section.Lines.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));

        if (line is null)
        {
            throw new InvalidOperationException($"Expected 09G layout line {section.Name}.{label}.");
        }

        RequireEqual(expectedValue, line.Value, $"Unexpected 09G layout line value for {section.Name}.{label}.");
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
            throw new InvalidOperationException($"Expected 09G success, got {result.ErrorDetail.Code}: {result.Error}");
        }

        return result.Value;
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

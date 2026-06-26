using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialReportBindingProofChecks
{
    private static readonly Guid JournalEntryId = Guid.Parse("25555555-5555-5555-5555-555555555551");
    private static readonly Guid SaleId = Guid.Parse("25555555-5555-5555-5555-555555555552");
    private static readonly Guid MachineProfileId = Guid.Parse("25555555-5555-5555-5555-555555555553");
    private static readonly Guid CashierUserId = Guid.Parse("25555555-5555-5555-5555-555555555554");
    private static readonly Guid OriginalEventId = Guid.Parse("25555555-5555-5555-5555-555555555555");
    private static readonly Guid ZReadingId = Guid.Parse("25555555-5555-5555-5555-555555555556");
    private static readonly DateTimeOffset TransactionTimestamp = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string builderText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialReportBindingDocumentBuilder.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(builderText, "OfficialReportBindingDocument", "Report binding proof must expose a document DTO.");
        RequireContains(builderText, "OfficialReportBindingTable", "Report binding proof must expose table DTOs.");
        RequireContains(builderText, "OfficialReportBindingRow", "Report binding proof must expose row DTOs.");
        RequireContains(builderText, "OfficialReportBindingCell", "Report binding proof must expose cell DTOs.");
        RequireContains(builderText, "OfficialReportBindingBuilder", "Report binding proof must expose a builder.");
        RequireContains(builderText, "OfficialPrintableDocument", "Report binding builder must consume printable documents.");
        RequireContains(builderText, "\"Header\"", "Report binding builder must build Header table.");
        RequireContains(builderText, "\"MachineProfile\"", "Report binding builder must build MachineProfile table.");
        RequireContains(builderText, "\"LineItems\"", "Report binding builder must build LineItems table.");
        RequireContains(builderText, "\"Payments\"", "Report binding builder must build Payments table.");
        RequireContains(builderText, "\"Totals\"", "Report binding builder must build Totals table.");
        RequireContains(builderText, "\"Evidence\"", "Report binding builder must build Evidence table.");
        RequireContains(builderText, "\"Lifecycle\"", "Report binding builder must build Lifecycle table.");
        RequireContains(builderText, "\"Corrections\"", "Report binding builder must build Corrections table.");
        RequireContains(builderText, "\"LifecycleIssues\"", "Report binding builder must build LifecycleIssues table.");
        RequireContains(builderText, "\"CopyAudit\"", "Report binding builder must build CopyAudit table.");
        RequireContains(builderText, "CultureInfo.InvariantCulture", "Report binding values must be culture-stable.");
        RequireContains(builderText, "ToString(\"O\"", "Report binding timestamps must be round-trip stable.");
        RequireNotContains(builderText, "Salryn.Infrastructure", "Report binding builder must not depend on Infrastructure.");
        RequireNotContains(builderText, "Sqlite", "Report binding builder must not read SQLite.");
        RequireNotContains(builderText, "CrystalDecisions", "Report binding builder must not reference a report engine package.");
        RequireNotContains(builderText, ".rpt", "Report binding builder must not mention report templates.");
        RequireNotContains(builderText, "System.Windows.Forms", "Report binding builder must not depend on WinForms.");
        RequireNotContains(builderText, "ESC/POS", "Report binding builder must not depend on printer output.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialReportBindingProofChecks.Run();", "CoreSmoke must run report binding proof checks.");

        VerifyRuntimeBindingBehavior();
    }

    private static void VerifyRuntimeBindingBehavior()
    {
        OfficialReportBindingBuilder builder = new();
        OfficialPrintableDocument document = CreatePrintableDocument(OfficialDocumentLifecycleState.Active, "ORIGINAL");

        Result<OfficialReportBindingDocument> result = builder.Build(document);
        RequireSuccess(result, "Report binding should build for an active printable document.");

        OfficialReportBindingDocument binding = result.Value;
        RequireEqual(
            "Header|MachineProfile|LineItems|Payments|Totals|Evidence|Lifecycle|Corrections|LifecycleIssues|CopyAudit",
            string.Join("|", binding.Tables.Select(table => table.Name)),
            "Report binding table order must be deterministic.");

        OfficialReportBindingTable header = RequireTable(binding, "Header");
        RequireEqual("OR-2026-BIND-001", Cell(header, 0, "OfficialDocumentNumber"), "Header table must contain official number.");
        RequireEqual("OfficialReceipt", Cell(header, 0, "DocumentKind"), "Header table must contain document kind.");
        RequireEqual(TransactionTimestamp.ToString("O"), Cell(header, 0, "TransactionTimestamp"), "Header table must contain stable timestamp.");
        RequireEqual("Binding Cashier", Cell(header, 0, "CashierIdentifier"), "Header table must contain cashier identity.");
        RequireEqual("POS-BINDING", Cell(header, 0, "TerminalId"), "Header table must contain terminal identity.");

        OfficialReportBindingTable profile = RequireTable(binding, "MachineProfile");
        RequireEqual("Binding Taxpayer", Cell(profile, 0, "RegisteredTaxpayerName"), "MachineProfile table must contain taxpayer facts.");
        RequireEqual("000-444-555-666", Cell(profile, 0, "TaxpayerIdentificationNumber"), "MachineProfile table must contain TIN.");
        RequireEqual("MIN-BINDING", Cell(profile, 0, "MachineIdentificationNumber"), "MachineProfile table must contain machine identity.");

        OfficialReportBindingTable lines = RequireTable(binding, "LineItems");
        RequireEqual(2, lines.Rows.Count, "LineItems table must include all rows.");
        RequireEqual("First Binding Item", Cell(lines, 0, "Description"), "LineItems table must preserve first item order.");
        RequireEqual("Second Binding Item", Cell(lines, 1, "Description"), "LineItems table must preserve second item order.");
        RequireEqual("2", Cell(lines, 1, "LineNumber"), "LineItems table must use deterministic line numbers.");

        OfficialReportBindingTable payments = RequireTable(binding, "Payments");
        RequireEqual(2, payments.Rows.Count, "Payments table must include all rows.");
        RequireEqual("Cash", Cell(payments, 0, "Method"), "Payments table must preserve first payment order.");
        RequireEqual("1500", Cell(payments, 1, "AmountCents"), "Payments table must preserve second payment order.");

        OfficialReportBindingTable totals = RequireTable(binding, "Totals");
        RequireEqual("5000", Cell(totals, 0, "TotalCents"), "Totals table must contain cent values.");
        RequireEqual("VAT-DEFERRED", Cell(totals, 0, "TaxModeCode"), "Totals table must contain tax mode.");
        RequireEqual("true", Cell(totals, 0, "VatBucketEngineDeferred"), "Totals table must contain tax mode flags.");

        OfficialReportBindingTable evidence = RequireTable(binding, "Evidence");
        RequireEqual("binding-hash", Cell(evidence, 0, "SnapshotPayloadHash"), "Evidence table must contain snapshot hash.");
        RequireEqual("SHA-256", Cell(evidence, 0, "SnapshotHashAlgorithm"), "Evidence table must contain hash algorithm.");

        OfficialReportBindingTable lifecycle = RequireTable(binding, "Lifecycle");
        RequireEqual("Active", Cell(lifecycle, 0, "State"), "Lifecycle table must include active state.");
        RequireEqual("ACTIVE", Cell(lifecycle, 0, "NoticeCode"), "Lifecycle table must include notice.");
        RequireEqual("true", Cell(lifecycle, 0, "IsCoveredByFinalizedZReading"), "Lifecycle table must include Z coverage flag.");
        RequireEqual(ZReadingId.ToString("D"), Cell(lifecycle, 0, "CoveringZReadingId"), "Lifecycle table must include Z coverage id.");

        OfficialReportBindingTable corrections = RequireTable(binding, "Corrections");
        RequireEqual("First correction.", Cell(corrections, 0, "CorrectionNote"), "Corrections table must preserve first note.");
        RequireEqual("Second correction.", Cell(corrections, 1, "CorrectionNote"), "Corrections table must preserve second note.");

        OfficialReportBindingTable issues = RequireTable(binding, "LifecycleIssues");
        RequireEqual(0, issues.Rows.Count, "Active lifecycle should not invent issue rows.");

        OfficialReportBindingTable copyAudit = RequireTable(binding, "CopyAudit");
        RequireEqual("ORIGINAL", Cell(copyAudit, 0, "OutputMarker"), "CopyAudit table must expose original marker.");
        RequireEqual("false", Cell(copyAudit, 0, "IsCopyOrReprint"), "CopyAudit table must expose original copy flag.");

        VerifyLifecycleStates(builder);
        VerifyCopyMarkers(builder);

        ExpectFailure(
            builder.Build(null),
            "OfficialReportBinding.DocumentRequired");
    }

    private static void VerifyLifecycleStates(OfficialReportBindingBuilder builder)
    {
        RequireEqual(
            "Voided",
            Cell(RequireTable(RequireValue(builder.Build(CreatePrintableDocument(OfficialDocumentLifecycleState.Voided, "ORIGINAL"))), "Lifecycle"), 0, "State"),
            "Lifecycle table must include voided state.");

        RequireEqual(
            "Cancelled",
            Cell(RequireTable(RequireValue(builder.Build(CreatePrintableDocument(OfficialDocumentLifecycleState.Cancelled, "ORIGINAL"))), "Lifecycle"), 0, "State"),
            "Lifecycle table must include cancelled state.");

        OfficialReportBindingDocument invalid =
            RequireValue(builder.Build(CreatePrintableDocument(
                OfficialDocumentLifecycleState.Invalid,
                "ORIGINAL",
                issueCodes: new[] { "OfficialDocumentLifecycle.MarkerTypeMismatch" })));

        OfficialReportBindingTable lifecycle = RequireTable(invalid, "Lifecycle");
        OfficialReportBindingTable issues = RequireTable(invalid, "LifecycleIssues");
        RequireEqual("Invalid", Cell(lifecycle, 0, "State"), "Lifecycle table must include invalid state.");
        RequireEqual("OfficialDocumentLifecycle.MarkerTypeMismatch", Cell(issues, 0, "Code"), "LifecycleIssues table must expose invalid issue code.");
    }

    private static void VerifyCopyMarkers(OfficialReportBindingBuilder builder)
    {
        OfficialReportBindingTable copy =
            RequireTable(RequireValue(builder.Build(CreatePrintableDocument(OfficialDocumentLifecycleState.Active, "COPY"))), "CopyAudit");
        RequireEqual("COPY", Cell(copy, 0, "OutputMarker"), "CopyAudit table must expose copy marker.");
        RequireEqual("true", Cell(copy, 0, "IsCopyOrReprint"), "CopyAudit table must expose copy flag.");

        OfficialReportBindingTable reprint =
            RequireTable(RequireValue(builder.Build(CreatePrintableDocument(OfficialDocumentLifecycleState.Active, "COPY/REPRINT"))), "CopyAudit");
        RequireEqual("COPY/REPRINT", Cell(reprint, 0, "OutputMarker"), "CopyAudit table must expose copy/reprint marker.");
        RequireEqual("3", Cell(reprint, 0, "PriorReprintEventCount"), "CopyAudit table must expose prior reprint count.");
    }

    private static OfficialPrintableDocument CreatePrintableDocument(
        OfficialDocumentLifecycleState state,
        string outputMarker,
        IReadOnlyList<string>? issueCodes = null)
    {
        OfficialPrintableDocumentCopyMode copyMode = outputMarker switch
        {
            "COPY" => OfficialPrintableDocumentCopyMode.Copy,
            "COPY/REPRINT" => OfficialPrintableDocumentCopyMode.Reprint,
            _ => OfficialPrintableDocumentCopyMode.Original
        };

        return new OfficialPrintableDocument(
            new OfficialPrintableDocumentHeader(
                ElectronicJournalDocumentKind.OfficialReceipt,
                "OR-2026-BIND-001",
                TransactionTimestamp,
                JournalEntryId,
                SaleId,
                CashierUserId,
                "Binding Cashier",
                "POS-BINDING"),
            new OfficialPrintableMachineProfile(
                MachineProfileId,
                RegistrationScopeCode: "LOCAL_SINGLE_STORE",
                RegisteredTaxpayerName: "Binding Taxpayer",
                BusinessName: "Binding Store",
                RegisteredAddress: "Binding Registered Address",
                BranchAddress: "Binding Branch Address",
                TaxpayerIdentificationNumber: "000-444-555-666",
                BranchCode: "00001",
                MachineIdentificationNumber: "MIN-BINDING",
                MachineSerialNumber: "SERIAL-BINDING",
                PosTerminalNumber: "POS-001",
                AccreditationNumber: "ACC-BINDING",
                PermitToUseNumber: "PTU-BINDING",
                SoftwareName: "Salryn",
                SoftwareVersion: "TEST",
                SoftwareBuild: "SMOKE",
                ComplianceMode: "ACCREDITED"),
            new[]
            {
                new OfficialPrintableLineItem(
                    "First Binding Item",
                    "4800000000101",
                    "pc",
                    1000,
                    3500,
                    0,
                    3500),
                new OfficialPrintableLineItem(
                    "Second Binding Item",
                    "4800000000102",
                    "pc",
                    1000,
                    1500,
                    0,
                    1500)
            },
            new[]
            {
                new OfficialPrintablePayment(
                    PaymentMethod.Cash,
                    3500,
                    TransactionTimestamp),
                new OfficialPrintablePayment(
                    PaymentMethod.Cash,
                    1500,
                    TransactionTimestamp.AddMinutes(1))
            },
            new OfficialPrintableTotals(
                SubtotalCents: 5000,
                DiscountCents: 0,
                TaxCents: 0,
                TotalCents: 5000,
                AmountPaidCents: 5000,
                ChangeCents: 0,
                TaxModeCode: "VAT-DEFERRED",
                VatBucketEngineDeferred: true,
                TaxModeNotes: "Detailed tax bucket classification is deferred.",
                DeferredComplianceNotes: new[] { "Deferred tax bucket detail." }),
            new OfficialPrintableEvidenceFooter(
                OriginalEventId,
                SnapshotPayloadHash: "binding-hash",
                SnapshotHashAlgorithm: "SHA-256",
                SnapshotHashMatches: true),
            new OfficialPrintableLifecycleNotice(
                state,
                state.ToString().ToUpperInvariant(),
                state + " lifecycle notice.",
                CorrectionCount: 2,
                CorrectionNotes: new[] { "First correction.", "Second correction." },
                IsCoveredByFinalizedZReading: true,
                CoveringZReadingId: ZReadingId,
                Issues: (issueCodes ?? Array.Empty<string>())
                    .Select(static code => new OfficialDocumentLifecycleIssue(code, code + " message."))
                    .ToArray()),
            new OfficialPrintableCopyInfo(
                copyMode,
                outputMarker,
                IsCopyOrReprint: copyMode != OfficialPrintableDocumentCopyMode.Original,
                PriorReprintEventCount: copyMode == OfficialPrintableDocumentCopyMode.Reprint ? 3 : 0));
    }

    private static OfficialReportBindingTable RequireTable(
        OfficialReportBindingDocument document,
        string tableName)
    {
        OfficialReportBindingTable? table =
            document.Tables.SingleOrDefault(candidate => string.Equals(candidate.Name, tableName, StringComparison.Ordinal));

        if (table is null)
        {
            throw new InvalidOperationException($"Expected report binding table {tableName}.");
        }

        return table;
    }

    private static string Cell(
        OfficialReportBindingTable table,
        int rowIndex,
        string columnName)
    {
        if (rowIndex >= table.Rows.Count)
        {
            throw new InvalidOperationException($"Report binding table {table.Name} is missing row {rowIndex}.");
        }

        OfficialReportBindingCell? cell = table.Rows[rowIndex].Cells
            .SingleOrDefault(candidate => string.Equals(candidate.ColumnName, columnName, StringComparison.Ordinal));

        if (cell is null)
        {
            throw new InvalidOperationException($"Report binding table {table.Name} is missing column {columnName}.");
        }

        return cell.Value;
    }

    private static OfficialReportBindingDocument RequireValue(Result<OfficialReportBindingDocument> result)
    {
        RequireSuccess(result, "Report binding should build.");

        return result.Value;
    }

    private static void ExpectFailure(Result<OfficialReportBindingDocument> result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected report binding failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected report binding validation code.");
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

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }
}

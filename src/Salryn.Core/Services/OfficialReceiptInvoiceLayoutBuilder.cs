using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialReceiptInvoiceLayoutDocument(
    IReadOnlyList<OfficialReceiptInvoiceLayoutSection> Sections);

public sealed record OfficialReceiptInvoiceLayoutSection(
    string Name,
    IReadOnlyList<OfficialReceiptInvoiceLayoutLine> Lines);

public sealed record OfficialReceiptInvoiceLayoutLine(
    string Label,
    string Value);

public sealed record OfficialReceiptInvoiceTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

// Kirch: why - creates a stable Core layout reference over hardened report binding tables before any concrete output adapter exists.
public sealed class OfficialReceiptInvoiceLayoutBuilder
{
    public Result<OfficialReceiptInvoiceLayoutDocument> Build(
        OfficialReportBindingDocument? bindingDocument)
    {
        Result validation =
            OfficialReportRendererContractValidator.ValidateBindingDocument(bindingDocument);

        if (validation.IsFailure)
        {
            return Result<OfficialReceiptInvoiceLayoutDocument>.Fail(validation.ErrorDetail);
        }

        OfficialReportBindingDocument source = bindingDocument!;

        OfficialReceiptInvoiceLayoutDocument layoutDocument = new(new[]
        {
            BuildTaxpayerHeader(RequireTable(source, "MachineProfile")),
            BuildDocumentHeader(RequireTable(source, "Header")),
            BuildLineItems(RequireTable(source, "LineItems")),
            BuildTotals(RequireTable(source, "Totals")),
            BuildPayments(RequireTable(source, "Payments")),
            BuildLifecycleNotice(
                RequireTable(source, "Lifecycle"),
                RequireTable(source, "Corrections"),
                RequireTable(source, "LifecycleIssues")),
            BuildCopyAudit(RequireTable(source, "CopyAudit")),
            BuildEvidenceFooter(RequireTable(source, "Evidence"))
        });

        return Result<OfficialReceiptInvoiceLayoutDocument>.Success(layoutDocument);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildTaxpayerHeader(
        OfficialReportBindingTable table)
    {
        IReadOnlyDictionary<string, string> profile = FirstRow(table);
        List<OfficialReceiptInvoiceLayoutLine> lines = new();

        AddRequired(lines, "RegisteredTaxpayerName", Value(profile, "RegisteredTaxpayerName"));
        AddOptional(lines, "BusinessName", Value(profile, "BusinessName"));
        AddRequired(lines, "RegisteredAddress", Value(profile, "RegisteredAddress"));
        AddOptional(lines, "BranchAddress", Value(profile, "BranchAddress"));
        AddRequired(lines, "TaxpayerIdentificationNumber", Value(profile, "TaxpayerIdentificationNumber"));
        AddRequired(lines, "BranchCode", Value(profile, "BranchCode"));
        AddOptional(lines, "MachineIdentificationNumber", Value(profile, "MachineIdentificationNumber"));
        AddOptional(lines, "MachineSerialNumber", Value(profile, "MachineSerialNumber"));
        AddOptional(lines, "PosTerminalNumber", Value(profile, "PosTerminalNumber"));
        AddOptional(lines, "AccreditationNumber", Value(profile, "AccreditationNumber"));
        AddOptional(lines, "PermitToUseNumber", Value(profile, "PermitToUseNumber"));
        AddOptional(lines, "SoftwareName", Value(profile, "SoftwareName"));
        AddOptional(lines, "SoftwareVersion", Value(profile, "SoftwareVersion"));
        AddOptional(lines, "SoftwareBuild", Value(profile, "SoftwareBuild"));

        return Section("TaxpayerHeader", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildDocumentHeader(
        OfficialReportBindingTable table)
    {
        IReadOnlyDictionary<string, string> header = FirstRow(table);
        List<OfficialReceiptInvoiceLayoutLine> lines = new();

        AddRequired(lines, "DocumentKind", Value(header, "DocumentKind"));
        AddRequired(lines, "OfficialDocumentNumber", Value(header, "OfficialDocumentNumber"));
        AddRequired(lines, "TransactionTimestamp", Value(header, "TransactionTimestamp"));
        AddRequired(lines, "CashierIdentifier", Value(header, "CashierIdentifier"));
        AddOptional(lines, "TerminalId", Value(header, "TerminalId"));

        return Section("DocumentHeader", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildLineItems(
        OfficialReportBindingTable table)
    {
        OfficialReceiptInvoiceLayoutLine[] lines = table.Rows
            .Select(row =>
            {
                IReadOnlyDictionary<string, string> item = RowValues(row);
                List<(string Name, string Value)> fields = new()
                {
                    Field("LineNumber", item),
                    Field("Description", item)
                };

                AddOptionalField(fields, "Barcode", item);
                fields.Add(Field("QuantityX1000", item));
                fields.Add(Field("UnitPriceCents", item));
                fields.Add(Field("DiscountCents", item));
                fields.Add(Field("LineTotalCents", item));

                return CompoundLine("LineItem", fields);
            })
            .ToArray();

        return Section("LineItems", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildTotals(
        OfficialReportBindingTable table)
    {
        IReadOnlyDictionary<string, string> totals = FirstRow(table);
        List<OfficialReceiptInvoiceLayoutLine> lines = new();

        AddRequired(lines, "SubtotalCents", Value(totals, "SubtotalCents"));
        AddRequired(lines, "DiscountCents", Value(totals, "DiscountCents"));
        AddRequired(lines, "TaxCents", Value(totals, "TaxCents"));
        AddRequired(lines, "TotalCents", Value(totals, "TotalCents"));
        AddRequired(lines, "AmountPaidCents", Value(totals, "AmountPaidCents"));
        AddRequired(lines, "ChangeCents", Value(totals, "ChangeCents"));
        AddRequired(lines, "TaxModeCode", Value(totals, "TaxModeCode"));
        AddRequired(lines, "VatBucketEngineDeferred", Value(totals, "VatBucketEngineDeferred"));
        AddOptional(lines, "TaxModeNotes", Value(totals, "TaxModeNotes"));
        AddOptional(lines, "DeferredComplianceNotes", Value(totals, "DeferredComplianceNotes"));

        return Section("Totals", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildPayments(
        OfficialReportBindingTable table)
    {
        OfficialReceiptInvoiceLayoutLine[] lines = table.Rows
            .Select(row =>
            {
                IReadOnlyDictionary<string, string> payment = RowValues(row);

                return CompoundLine("Payment", new[]
                {
                    Field("PaymentNumber", payment),
                    Field("Method", payment),
                    Field("AmountCents", payment),
                    Field("PaidAt", payment)
                });
            })
            .ToArray();

        return Section("Payments", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildLifecycleNotice(
        OfficialReportBindingTable lifecycleTable,
        OfficialReportBindingTable correctionsTable,
        OfficialReportBindingTable lifecycleIssuesTable)
    {
        IReadOnlyDictionary<string, string> lifecycle = FirstRow(lifecycleTable);
        List<OfficialReceiptInvoiceLayoutLine> lines = new();

        AddRequired(lines, "State", Value(lifecycle, "State"));
        AddRequired(lines, "NoticeCode", Value(lifecycle, "NoticeCode"));
        AddRequired(lines, "NoticeText", Value(lifecycle, "NoticeText"));
        AddRequired(lines, "CorrectionCount", Value(lifecycle, "CorrectionCount"));
        AddRequired(lines, "IsCoveredByFinalizedZReading", Value(lifecycle, "IsCoveredByFinalizedZReading"));
        AddOptional(lines, "CoveringZReadingId", Value(lifecycle, "CoveringZReadingId"));

        foreach (OfficialReportBindingRow row in correctionsTable.Rows)
        {
            IReadOnlyDictionary<string, string> correction = RowValues(row);

            lines.Add(CompoundLine("CorrectionNote", new[]
            {
                Field("CorrectionNumber", correction),
                Field("CorrectionNote", correction)
            }));
        }

        foreach (OfficialReportBindingRow row in lifecycleIssuesTable.Rows)
        {
            IReadOnlyDictionary<string, string> issue = RowValues(row);

            lines.Add(CompoundLine("LifecycleIssue", new[]
            {
                Field("IssueNumber", issue),
                Field("Code", issue),
                Field("Message", issue)
            }));
        }

        return Section("LifecycleNotice", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildCopyAudit(
        OfficialReportBindingTable table)
    {
        IReadOnlyDictionary<string, string> copy = FirstRow(table);
        List<OfficialReceiptInvoiceLayoutLine> lines = new();

        AddRequired(lines, "CopyMode", Value(copy, "CopyMode"));
        AddRequired(lines, "OutputMarker", Value(copy, "OutputMarker"));
        AddRequired(lines, "IsCopyOrReprint", Value(copy, "IsCopyOrReprint"));
        AddRequired(lines, "PriorReprintEventCount", Value(copy, "PriorReprintEventCount"));

        return Section("CopyAudit", lines);
    }

    private static OfficialReceiptInvoiceLayoutSection BuildEvidenceFooter(
        OfficialReportBindingTable table)
    {
        IReadOnlyDictionary<string, string> evidence = FirstRow(table);
        List<OfficialReceiptInvoiceLayoutLine> lines = new();

        AddRequired(lines, "OriginalEventId", Value(evidence, "OriginalEventId"));
        AddRequired(lines, "SnapshotPayloadHash", Value(evidence, "SnapshotPayloadHash"));
        AddRequired(lines, "SnapshotHashAlgorithm", Value(evidence, "SnapshotHashAlgorithm"));
        AddRequired(lines, "SnapshotHashMatches", Value(evidence, "SnapshotHashMatches"));

        return Section("EvidenceFooter", lines);
    }

    private static OfficialReportBindingTable RequireTable(
        OfficialReportBindingDocument document,
        string tableName)
    {
        return document.Tables.First(table => string.Equals(table.Name, tableName, StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> FirstRow(
        OfficialReportBindingTable table)
    {
        return table.Rows.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : RowValues(table.Rows[0]);
    }

    private static IReadOnlyDictionary<string, string> RowValues(
        OfficialReportBindingRow row)
    {
        return row.Cells.ToDictionary(
            static cell => cell.ColumnName,
            static cell => Normalize(cell.Value),
            StringComparer.Ordinal);
    }

    private static OfficialReceiptInvoiceLayoutSection Section(
        string name,
        IReadOnlyList<OfficialReceiptInvoiceLayoutLine> lines)
    {
        return new OfficialReceiptInvoiceLayoutSection(name, lines.ToArray());
    }

    private static void AddRequired(
        List<OfficialReceiptInvoiceLayoutLine> lines,
        string label,
        string value)
    {
        lines.Add(new OfficialReceiptInvoiceLayoutLine(label, Normalize(value)));
    }

    private static void AddOptional(
        List<OfficialReceiptInvoiceLayoutLine> lines,
        string label,
        string value)
    {
        string normalized = Normalize(value);

        if (normalized.Length > 0)
        {
            lines.Add(new OfficialReceiptInvoiceLayoutLine(label, normalized));
        }
    }

    private static void AddOptionalField(
        List<(string Name, string Value)> fields,
        string name,
        IReadOnlyDictionary<string, string> values)
    {
        string value = Value(values, name);

        if (value.Length > 0)
        {
            fields.Add((name, value));
        }
    }

    private static OfficialReceiptInvoiceLayoutLine CompoundLine(
        string label,
        IReadOnlyList<(string Name, string Value)> fields)
    {
        return new OfficialReceiptInvoiceLayoutLine(
            label,
            string.Join(" | ", fields.Select(static field => field.Name + "=" + Normalize(field.Value))));
    }

    private static (string Name, string Value) Field(
        string name,
        IReadOnlyDictionary<string, string> values)
    {
        return (name, Value(values, name));
    }

    private static string Value(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out string? value)
            ? Normalize(value)
            : string.Empty;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

public sealed class OfficialReceiptInvoiceTextRenderer
{
    private const string Title = "SALRYN OFFICIAL RECEIPT/INVOICE LAYOUT PROOF";

    private static readonly IReadOnlyList<string> SectionOrder = new[]
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

    private static readonly IReadOnlyDictionary<string, string> SectionMarkers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TaxpayerHeader"] = "[TAXPAYER]",
            ["DocumentHeader"] = "[DOCUMENT]",
            ["LineItems"] = "[ITEMS]",
            ["Totals"] = "[TOTALS]",
            ["Payments"] = "[PAYMENTS]",
            ["LifecycleNotice"] = "[LIFECYCLE]",
            ["CopyAudit"] = "[COPY]",
            ["EvidenceFooter"] = "[EVIDENCE]"
        };

    public Result<OfficialReceiptInvoiceTextRenderResult> Render(
        OfficialReceiptInvoiceLayoutDocument? document)
    {
        Result validation = ValidateDocument(document);

        if (validation.IsFailure)
        {
            return Result<OfficialReceiptInvoiceTextRenderResult>.Fail(validation.ErrorDetail);
        }

        List<string> lines = new()
        {
            Title
        };

        foreach (OfficialReceiptInvoiceLayoutSection section in OrderedSections(document!))
        {
            lines.Add(SectionMarker(section.Name));

            foreach (OfficialReceiptInvoiceLayoutLine line in section.Lines)
            {
                lines.Add(RenderLine(line));
            }
        }

        return Result<OfficialReceiptInvoiceTextRenderResult>.Success(
            new OfficialReceiptInvoiceTextRenderResult(
                lines,
                string.Join("\n", lines)));
    }

    private static Result ValidateDocument(
        OfficialReceiptInvoiceLayoutDocument? document)
    {
        if (document is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReceiptInvoiceText.LayoutRequired",
                "Official receipt/invoice text rendering requires a layout document."));
        }

        if (document.Sections is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialReceiptInvoiceText.SectionsRequired",
                "Official receipt/invoice text rendering requires layout sections."));
        }

        HashSet<string> sectionNames = new(StringComparer.Ordinal);

        foreach (OfficialReceiptInvoiceLayoutSection section in document.Sections)
        {
            if (section is null ||
                string.IsNullOrWhiteSpace(section.Name) ||
                section.Lines is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialReceiptInvoiceText.InvalidSection",
                    "Official receipt/invoice text rendering found an invalid layout section."));
            }

            if (!sectionNames.Add(section.Name))
            {
                return Result.Fail(Error.Validation(
                    "OfficialReceiptInvoiceText.DuplicateSection",
                    "Official receipt/invoice text rendering found duplicate layout sections."));
            }

            foreach (OfficialReceiptInvoiceLayoutLine line in section.Lines)
            {
                if (line is null || string.IsNullOrWhiteSpace(line.Label))
                {
                    return Result.Fail(Error.Validation(
                        "OfficialReceiptInvoiceText.InvalidLine",
                        "Official receipt/invoice text rendering found an invalid layout line."));
                }
            }
        }

        return Result.Success();
    }

    private static IReadOnlyList<OfficialReceiptInvoiceLayoutSection> OrderedSections(
        OfficialReceiptInvoiceLayoutDocument document)
    {
        Dictionary<string, OfficialReceiptInvoiceLayoutSection> sectionsByName =
            document.Sections.ToDictionary(
                static section => section.Name,
                static section => section,
                StringComparer.Ordinal);

        List<OfficialReceiptInvoiceLayoutSection> ordered = new();

        foreach (string sectionName in SectionOrder)
        {
            if (sectionsByName.TryGetValue(sectionName, out OfficialReceiptInvoiceLayoutSection? section))
            {
                ordered.Add(section);
            }
        }

        HashSet<string> knownSectionNames = new(SectionOrder, StringComparer.Ordinal);

        ordered.AddRange(document.Sections
            .Where(section => !knownSectionNames.Contains(section.Name))
            .OrderBy(static section => section.Name, StringComparer.Ordinal));

        return ordered;
    }

    private static string SectionMarker(string sectionName)
    {
        return SectionMarkers.TryGetValue(sectionName, out string? marker)
            ? marker
            : "[" + Normalize(sectionName).ToUpperInvariant() + "]";
    }

    private static string RenderLine(OfficialReceiptInvoiceLayoutLine line)
    {
        string value = Normalize(line.Value);

        return value.Length == 0
            ? Normalize(line.Label) + ":"
            : Normalize(line.Label) + ": " + value;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}

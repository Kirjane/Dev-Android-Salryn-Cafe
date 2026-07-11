using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports.NativePdf;

public static class NativePdfDocumentBuilder
{
    static NativePdfDocumentBuilder()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public static byte[] BuildPdfBytes(
        ProductionReportDatasetEnvelope dataset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        cancellationToken.ThrowIfCancellationRequested();

        Document document = BuildDocument(dataset, cancellationToken);
        PdfDocumentRenderer renderer = new()
        {
            Document = document
        };

        cancellationToken.ThrowIfCancellationRequested();
        renderer.RenderDocument();
        renderer.PdfDocument.Info.Title = dataset.ReportTitle;
        renderer.PdfDocument.Info.Subject = dataset.ReportKind;
        renderer.PdfDocument.Info.Creator = NativePdfRendererIdentity.RendererName;
        renderer.PdfDocument.Info.CreationDate = dataset.GeneratedAt.UtcDateTime;
        renderer.PdfDocument.Info.ModificationDate = dataset.GeneratedAt.UtcDateTime;

        using MemoryStream stream = new();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static Document BuildDocument(
        ProductionReportDatasetEnvelope dataset,
        CancellationToken cancellationToken)
    {
        Document document = new();
        document.Info.Title = dataset.ReportTitle;
        document.Info.Subject = dataset.ReportKind;
        document.Info.Author = "Salryn";

        Style normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;

        Section section = document.AddSection();
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.2);

        Paragraph title = section.AddParagraph(dataset.ReportTitle);
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromCentimeter(0.2);

        AddKeyValueTable(section, "Report", new[]
        {
            Pair("Report kind", dataset.ReportKind),
            Pair("Schema", dataset.SchemaCode + " v" + dataset.SchemaVersion.ToString(CultureInfo.InvariantCulture)),
            Pair("Generated at", FormatDate(dataset.GeneratedAt)),
            Pair("Content SHA-256", dataset.ContentSha256),
            Pair("Dataset SHA-256", dataset.DatasetSha256),
            Pair("Summary", dataset.Summary)
        });

        cancellationToken.ThrowIfCancellationRequested();
        AddKeyValueTable(section, "Scope", new[]
        {
            Pair("Range start", FormatDate(dataset.Scope.RangeStart)),
            Pair("Range end", FormatDate(dataset.Scope.RangeEnd)),
            Pair("BIR machine profile", dataset.Scope.BirMachineProfileId?.ToString("D") ?? string.Empty),
            Pair("Registration scope", dataset.Scope.RegistrationScopeCode),
            Pair("Document kind", dataset.Scope.DocumentKind),
            Pair("Actor user", dataset.Scope.ActorUserId?.ToString("D") ?? string.Empty),
            Pair("Terminal", dataset.Scope.TerminalId ?? string.Empty)
        });

        cancellationToken.ThrowIfCancellationRequested();
        AddKeyValueTable(section, "Safety flags", new[]
        {
            Pair("Dataset available", Bool(dataset.SafetyFlags.DatasetAvailable)),
            Pair("Uses canonical M5 projection", Bool(dataset.SafetyFlags.UsesCanonicalM5Projection)),
            Pair("Production activation enabled", Bool(dataset.SafetyFlags.ProductionActivationEnabled)),
            Pair("BIR eSales schema confirmed", Bool(dataset.SafetyFlags.BirESalesSchemaConfirmed)),
            Pair("eSales transmission enabled", Bool(dataset.SafetyFlags.ESalesTransmissionEnabled)),
            Pair("BIR accreditation claimed", Bool(dataset.SafetyFlags.BirAccreditationClaimed)),
            Pair("Crystal runtime available", Bool(dataset.SafetyFlags.CrystalRuntimeAvailable)),
            Pair("Renderer can produce PDF", Bool(dataset.SafetyFlags.RendererCanProducePdf)),
            Pair("Boundary note", dataset.SafetyFlags.BoundaryNote)
        });

        cancellationToken.ThrowIfCancellationRequested();
        AddControlTotals(section, dataset.ControlTotals);
        AddSourceHashes(section, dataset.SourceHashes);
        AddRows(section, dataset.Rows, cancellationToken);
        return document;
    }

    private static void AddHeading(Section section, string text)
    {
        Paragraph heading = section.AddParagraph(text);
        heading.Format.Font.Size = 12;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromCentimeter(0.25);
        heading.Format.SpaceAfter = Unit.FromCentimeter(0.1);
    }

    private static void AddKeyValueTable(
        Section section,
        string heading,
        IEnumerable<KeyValuePair<string, string>> values)
    {
        AddHeading(section, heading);
        Table table = section.AddTable();
        table.Borders.Width = 0.25;
        table.AddColumn(Unit.FromCentimeter(5.0));
        table.AddColumn(Unit.FromCentimeter(12.0));

        foreach (KeyValuePair<string, string> pair in values)
        {
            Row row = table.AddRow();
            row.Cells[0].AddParagraph(pair.Key);
            row.Cells[0].Format.Font.Bold = true;
            row.Cells[1].AddParagraph(pair.Value ?? string.Empty);
        }
    }

    private static void AddControlTotals(
        Section section,
        IReadOnlyList<ProductionReportControlTotal> totals)
    {
        AddHeading(section, "Control totals");
        Table table = section.AddTable();
        table.Borders.Width = 0.25;
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(6.0));
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(3.0));

        Row header = table.AddRow();
        AddHeader(header, 0, "Code");
        AddHeader(header, 1, "Label");
        AddHeader(header, 2, "Value cents");
        AddHeader(header, 3, "Count");

        foreach (ProductionReportControlTotal total in totals)
        {
            Row row = table.AddRow();
            row.Cells[0].AddParagraph(total.Code);
            row.Cells[1].AddParagraph(total.Label);
            row.Cells[2].AddParagraph(total.ValueCents.ToString(CultureInfo.InvariantCulture));
            row.Cells[3].AddParagraph(total.Count.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddSourceHashes(
        Section section,
        IReadOnlyList<ProductionReportSourceHash> hashes)
    {
        AddHeading(section, "Source hashes");
        Table table = section.AddTable();
        table.Borders.Width = 0.25;
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(6.5));
        table.AddColumn(Unit.FromCentimeter(6.5));

        Row header = table.AddRow();
        AddHeader(header, 0, "Source kind");
        AddHeader(header, 1, "SHA-256");
        AddHeader(header, 2, "Description");

        foreach (ProductionReportSourceHash hash in hashes)
        {
            Row row = table.AddRow();
            row.Cells[0].AddParagraph(hash.SourceKind);
            row.Cells[1].AddParagraph(hash.HashSha256);
            row.Cells[2].AddParagraph(hash.Description);
        }
    }

    private static void AddRows(
        Section section,
        IReadOnlyList<ProductionReportRow> rows,
        CancellationToken cancellationToken)
    {
        AddHeading(section, "Rows");
        Table table = section.AddTable();
        table.Borders.Width = 0.25;
        table.AddColumn(Unit.FromCentimeter(2.0));
        table.AddColumn(Unit.FromCentimeter(3.5));
        table.AddColumn(Unit.FromCentimeter(11.5));

        Row header = table.AddRow();
        AddHeader(header, 0, "Sort");
        AddHeader(header, 1, "Type");
        AddHeader(header, 2, "Values");

        foreach (ProductionReportRow reportRow in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Row row = table.AddRow();
            row.Cells[0].AddParagraph(reportRow.SortOrdinal.ToString(CultureInfo.InvariantCulture));
            row.Cells[1].AddParagraph(reportRow.RowType);
            row.Cells[2].AddParagraph(string.Join("\n", reportRow.Values.Select(x => x.Key + ": " + x.Value)));
        }
    }

    private static void AddHeader(Row row, int index, string text)
    {
        row.Cells[index].AddParagraph(text);
        row.Cells[index].Format.Font.Bold = true;
    }

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value ?? string.Empty);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

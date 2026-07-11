using MigraDoc.DocumentObjectModel;
using PdfSharp.Pdf;

namespace Salryn.Infrastructure.Reports.NativePdf;

/// <summary>
/// REPORTING-CLOSURE-M2: type-load probe for the free native PDF engine candidate.
/// This is intentionally not a renderer. It creates no files, accepts no paths, and renders no Salryn datasets.
/// </summary>
public static class NativePdfPackageProbe
{
    public const string PackageId = "PDFsharp-MigraDoc";
    public const string RequestedPackageVersion = "6.2.4";

    public static NativePdfPackageProbeResult Read()
    {
        Type pdfSharpType = typeof(PdfDocument);
        Type migraDocType = typeof(Document);

        return new NativePdfPackageProbeResult(
            PackageId,
            RequestedPackageVersion,
            pdfSharpType.FullName ?? pdfSharpType.Name,
            pdfSharpType.Assembly.GetName().Name ?? string.Empty,
            pdfSharpType.Assembly.GetName().Version?.ToString() ?? string.Empty,
            migraDocType.FullName ?? migraDocType.Name,
            migraDocType.Assembly.GetName().Name ?? string.Empty,
            migraDocType.Assembly.GetName().Version?.ToString() ?? string.Empty,
            RequiresFileOutput: false,
            IsRenderer: false,
            ClaimsPdfProof: false,
            Notes: "Type-load probe only. No file output, artifact creation, endpoint, UI, DI registration, or Salryn dataset rendering is performed in M2.");
    }
}

public sealed record NativePdfPackageProbeResult(
    string PackageId,
    string RequestedPackageVersion,
    string PdfSharpTypeName,
    string PdfSharpAssemblyName,
    string PdfSharpAssemblyVersion,
    string MigraDocTypeName,
    string MigraDocAssemblyName,
    string MigraDocAssemblyVersion,
    bool RequiresFileOutput,
    bool IsRenderer,
    bool ClaimsPdfProof,
    string Notes);

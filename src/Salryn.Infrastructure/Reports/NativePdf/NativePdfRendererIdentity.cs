using System.Reflection;

namespace Salryn.Infrastructure.Reports.NativePdf;

public static class NativePdfRendererIdentity
{
    public const string RendererName = "Salryn.NativePdf.PDFsharpMigraDoc";

    public const string StatusAvailable = "NativePdf.Available";
    public const string StatusUnsupportedOutputKind = "NativePdf.UnsupportedOutputKind";
    public const string StatusInvalidDataset = "NativePdf.InvalidDataset";
    public const string StatusRenderFailed = "NativePdf.RenderFailed";
    public const string StatusCancelled = "NativePdf.Cancelled";

    public static string RendererVersion
    {
        get
        {
            Assembly assembly = typeof(NativePdfPackageProbe).Assembly;
            string assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";
            NativePdfPackageProbeResult package = NativePdfPackageProbe.Read();
            return $"pdfsharp-migradoc:{package.RequestedPackageVersion};salryn-infrastructure:{assemblyVersion}";
        }
    }
}

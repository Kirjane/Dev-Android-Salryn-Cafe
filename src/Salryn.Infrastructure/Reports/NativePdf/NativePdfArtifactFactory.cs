using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports.NativePdf;

public static class NativePdfArtifactFactory
{
    public const string PdfContentType = "application/pdf";

    public static ProductionReportArtifactMetadata Create(
        ProductionReportDatasetEnvelope dataset,
        byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(pdfBytes);

        string fileName = CreateFileName(dataset.ReportKind, dataset.GeneratedAt);
        string hash = Sha256(pdfBytes);

        return new ProductionReportArtifactMetadata(
            fileName,
            PdfContentType,
            pdfBytes.LongLength,
            hash,
            dataset.GeneratedAt,
            NativePdfRendererIdentity.RendererName,
            NativePdfRendererIdentity.RendererVersion,
            dataset.DatasetSha256);
    }

    public static string CreateFileName(string reportKind, DateTimeOffset generatedAt)
    {
        string safeReportKind = SanitizeFileToken(reportKind);
        string stamp = generatedAt.ToUniversalTime().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"{safeReportKind}-{stamp}.pdf";
    }

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "report";
        }

        StringBuilder builder = new(value.Length);
        foreach (char ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch is '-' or '_' or '.')
            {
                builder.Append('-');
            }
        }

        string token = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(token) ? "report" : token;
    }
}

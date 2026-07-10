using Salryn.Core.Common;

namespace Salryn.Core.Services.Reports;

public static class ProductionReportOutputKinds
{
    public const string Pdf = "pdf";

    public static IReadOnlyList<string> All => new[] { Pdf };

    public static bool TryNormalize(string? value, out string normalized, out Error? error)
    {
        normalized = Pdf;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string candidate = value.Trim().ToLowerInvariant();
        if (string.Equals(candidate, Pdf, StringComparison.Ordinal))
        {
            normalized = Pdf;
            return true;
        }

        error = Error.Validation(
            "Reports.UnsupportedOutputKind",
            "Only PDF output is allow-listed for the Crystal M1 worker boundary. Rendering remains unavailable until a licensed runtime is wired and verified.");
        return false;
    }
}

public sealed record ProductionReportRendererStatus(
    string RendererName,
    string RendererVersion,
    bool Available,
    bool CanRenderPdf,
    bool CanPreview,
    bool CanPrint,
    bool RequiresExternalRuntime,
    string StatusCode,
    string Message);

public sealed record ProductionReportRendererCapability(
    string RendererName,
    string RendererVersion,
    bool DirectInProcessSupported,
    bool IsolatedWorkerSupported,
    bool RuntimeInstalled,
    bool LicensedRuntimeConfirmed,
    IReadOnlyList<string> SupportedOutputKinds,
    string RuntimeSupportNote);

public sealed record ProductionReportRenderRequest(
    ProductionReportDatasetEnvelope Dataset,
    string RequestedOutputKind,
    int TimeoutMilliseconds,
    CancellationToken CancellationToken);

public sealed record ProductionReportRenderResult(
    bool Success,
    ProductionReportArtifactMetadata? Artifact,
    string RendererName,
    string RendererVersion,
    string? ErrorCode,
    string? ErrorMessage);

public interface IProductionReportRenderer
{
    ProductionReportRendererStatus GetStatus();

    Task<Result<ProductionReportRenderResult>> RenderAsync(
        ProductionReportRenderRequest request,
        CancellationToken cancellationToken = default);
}

using Salryn.Core.Common;

namespace Salryn.Core.Services.Reports;

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

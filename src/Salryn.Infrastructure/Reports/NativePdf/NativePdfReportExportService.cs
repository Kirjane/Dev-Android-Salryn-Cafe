using Salryn.Core.Common;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports.NativePdf;

public sealed record NativePdfReportExportRequest(
    ProductionReportDatasetEnvelope Dataset,
    string? RequestedOutputKind,
    string ExportScope,
    CancellationToken RequestCancellationToken);

public sealed class NativePdfReportExportService
{
    private readonly NativePdfReportRenderer _renderer;
    private readonly NativePdfArtifactStore _store;

    public NativePdfReportExportService(
        NativePdfReportRenderer? renderer = null,
        NativePdfArtifactStore? store = null)
    {
        _renderer = renderer ?? new NativePdfReportRenderer();
        _store = store ?? new NativePdfArtifactStore();
    }

    public NativePdfArtifactStore Store => _store;

    public async Task<Result<ProductionReportArtifactExportResult>> ExportAsync(
        NativePdfReportExportRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            request.RequestCancellationToken.ThrowIfCancellationRequested();

            if (!ProductionReportOutputKinds.TryNormalize(request.RequestedOutputKind, out string outputKind, out Error? outputError))
            {
                return Result<ProductionReportArtifactExportResult>.Fail(Error.Validation(
                    "Reports.NativePdfUnsupportedOutputKind",
                    outputError?.Message ?? "Only PDF output is allow-listed for native report export."));
            }

            if (!string.Equals(outputKind, ProductionReportOutputKinds.Pdf, StringComparison.Ordinal))
            {
                return Result<ProductionReportArtifactExportResult>.Fail(Error.Validation(
                    "Reports.NativePdfUnsupportedOutputKind",
                    "Only PDF output is allow-listed for native report export."));
            }

            Result<ProductionReportRenderResult> render = await _renderer.RenderAsync(
                new ProductionReportRenderRequest(
                    request.Dataset,
                    outputKind,
                    TimeoutMilliseconds: 120000,
                    request.RequestCancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (render.IsFailure)
            {
                return Result<ProductionReportArtifactExportResult>.Fail(render.ErrorDetail);
            }

            byte[] pdfBytes = NativePdfDocumentBuilder.BuildPdfBytes(request.Dataset, request.RequestCancellationToken);
            return await _store.SaveAsync(
                request.Dataset,
                pdfBytes,
                request.ExportScope,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result<ProductionReportArtifactExportResult>.Fail(Error.Failure(
                "Reports.NativePdfExportCancelled",
                "Native PDF export was cancelled."));
        }
    }
}

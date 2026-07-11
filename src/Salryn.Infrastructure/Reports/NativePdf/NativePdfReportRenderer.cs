using Salryn.Core.Common;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports.NativePdf;

public sealed class NativePdfReportRenderer : IProductionReportRenderer
{
    public ProductionReportRendererStatus GetStatus() => new(
        NativePdfRendererIdentity.RendererName,
        NativePdfRendererIdentity.RendererVersion,
        Available: true,
        CanRenderPdf: true,
        CanPreview: false,
        CanPrint: false,
        RequiresExternalRuntime: false,
        StatusCode: NativePdfRendererIdentity.StatusAvailable,
        Message: "Native PDF renderer is available for in-memory PDF generation. Controlled artifact export is handled by the M5 export service. No print, installer, UI preview/download, Crystal runtime, or final reporting freeze is claimed here.");

    public ProductionReportRendererCapability GetCapability() => new(
        NativePdfRendererIdentity.RendererName,
        NativePdfRendererIdentity.RendererVersion,
        DirectInProcessSupported: true,
        IsolatedWorkerSupported: false,
        RuntimeInstalled: true,
        LicensedRuntimeConfirmed: false,
        SupportedOutputKinds: ProductionReportOutputKinds.All,
        RuntimeSupportNote: "Native PDFsharp/MigraDoc renderer uses the Salryn process and does not require SAP Crystal runtime. MIT package license is package-level only; release/legal approval is not claimed here.");

    public async Task<Result<ProductionReportRenderResult>> RenderAsync(
        ProductionReportRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            request.CancellationToken.ThrowIfCancellationRequested();

            if (!ProductionReportOutputKinds.TryNormalize(request.RequestedOutputKind, out string outputKind, out Error? outputError))
            {
                return Result<ProductionReportRenderResult>.Fail(Error.Validation(
                    NativePdfRendererIdentity.StatusUnsupportedOutputKind,
                    outputError?.Message ?? "Only PDF output is allow-listed for the native PDF renderer core."));
            }

            if (!string.Equals(outputKind, ProductionReportOutputKinds.Pdf, StringComparison.Ordinal))
            {
                return Result<ProductionReportRenderResult>.Fail(Error.Validation(
                    NativePdfRendererIdentity.StatusUnsupportedOutputKind,
                    "Only PDF output is allow-listed for the native PDF renderer core."));
            }

            if (!IsValidDataset(request.Dataset, out string invalidReason))
            {
                return Result<ProductionReportRenderResult>.Fail(Error.Validation(
                    NativePdfRendererIdentity.StatusInvalidDataset,
                    invalidReason));
            }

            byte[] pdfBytes = await Task.Run(
                () => NativePdfDocumentBuilder.BuildPdfBytes(request.Dataset, request.CancellationToken),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            request.CancellationToken.ThrowIfCancellationRequested();

            ProductionReportArtifactMetadata artifact = NativePdfArtifactFactory.Create(request.Dataset, pdfBytes);
            ProductionReportRenderResult result = new(
                Success: true,
                Artifact: artifact,
                RendererName: NativePdfRendererIdentity.RendererName,
                RendererVersion: NativePdfRendererIdentity.RendererVersion,
                ErrorCode: null,
                ErrorMessage: null);

            return Result<ProductionReportRenderResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            return Result<ProductionReportRenderResult>.Fail(Error.Failure(
                NativePdfRendererIdentity.StatusCancelled,
                "Native PDF rendering was cancelled."));
        }
        catch (Exception ex)
        {
            return Result<ProductionReportRenderResult>.Fail(Error.Failure(
                NativePdfRendererIdentity.StatusRenderFailed,
                "Native PDF rendering failed: " + ex.Message));
        }
    }

    private static bool IsValidDataset(
        ProductionReportDatasetEnvelope? dataset,
        out string reason)
    {
        if (dataset is null)
        {
            reason = "Dataset is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataset.SchemaCode))
        {
            reason = "Dataset schema code is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataset.ReportKind))
        {
            reason = "Dataset report kind is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataset.ReportTitle))
        {
            reason = "Dataset report title is required.";
            return false;
        }

        if (dataset.Scope is null)
        {
            reason = "Dataset scope is required.";
            return false;
        }

        if (dataset.ControlTotals is null || dataset.Rows is null || dataset.SourceHashes is null)
        {
            reason = "Dataset collections are required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataset.ContentSha256))
        {
            reason = "Dataset content hash is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataset.DatasetSha256))
        {
            reason = "Dataset envelope hash is required.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

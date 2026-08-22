using Salryn.Core.Common;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports;

public sealed class UnavailableCrystalReportRenderer : IProductionReportRenderer
{
    public const string RendererNameValue = "SAP Crystal Reports";
    public const string RendererVersionValue = "unavailable-crystal-m1-worker-pending";

    public ProductionReportRendererStatus GetStatus() => new(
        RendererNameValue,
        RendererVersionValue,
        Available: false,
        CanRenderPdf: false,
        CanPreview: false,
        CanPrint: false,
        RequiresExternalRuntime: true,
        StatusCode: "Crystal.WorkerPending",
        Message: "Crystal rendering is intentionally unavailable. Crystal M1 selects an isolated Windows worker boundary; no SAP packages, templates, binaries, printer logic, or PDF success claim are present.");

    public ProductionReportRendererCapability GetCapability() => new(
        RendererNameValue,
        RendererVersionValue,
        DirectInProcessSupported: false,
        IsolatedWorkerSupported: false,
        RuntimeInstalled: false,
        LicensedRuntimeConfirmed: false,
        SupportedOutputKinds: ProductionReportOutputKinds.All,
        RuntimeSupportNote: "SAP primary sources do not support direct in-process use from Salryn net10.0. Future work must wire a separately licensed isolated Windows worker.");

    public Task<Result<ProductionReportRenderResult>> RenderAsync(
        ProductionReportRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ProductionReportOutputKinds.TryNormalize(request.RequestedOutputKind, out _, out Error? outputKindError))
        {
            return Task.FromResult(Result<ProductionReportRenderResult>.Fail(outputKindError!));
        }

        ProductionReportRenderResult result = new(
            Success: false,
            Artifact: null,
            RendererName: RendererNameValue,
            RendererVersion: RendererVersionValue,
            ErrorCode: "Crystal.WorkerPending",
            ErrorMessage: "Fail-closed: isolated Crystal worker/runtime is not wired. No artifact was created.");

        return Task.FromResult(Result<ProductionReportRenderResult>.Fail(Error.Failure(
            result.ErrorCode,
            result.ErrorMessage)));
    }
}

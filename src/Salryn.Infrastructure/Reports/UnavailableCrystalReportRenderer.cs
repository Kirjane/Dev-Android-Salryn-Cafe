using Salryn.Core.Common;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports;

public sealed class UnavailableCrystalReportRenderer : IProductionReportRenderer
{
    public const string RendererNameValue = "SAP Crystal Reports";
    public const string RendererVersionValue = "unavailable-precrystal-m0";

    public ProductionReportRendererStatus GetStatus() => new(
        RendererNameValue,
        RendererVersionValue,
        Available: false,
        CanRenderPdf: false,
        CanPreview: false,
        CanPrint: false,
        RequiresExternalRuntime: true,
        StatusCode: "Crystal.Unavailable",
        Message: "Crystal rendering is intentionally unavailable in PRE-CRYSTAL M0. No SAP packages, .rpt files, binaries, printer logic, or PDF claims are present.");

    public Task<Result<ProductionReportRenderResult>> RenderAsync(
        ProductionReportRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProductionReportRenderResult result = new(
            Success: false,
            Artifact: null,
            RendererName: RendererNameValue,
            RendererVersion: RendererVersionValue,
            ErrorCode: "Crystal.Unavailable",
            ErrorMessage: "Fail-closed: Crystal runtime/worker is not wired. No artifact was created.");

        return Task.FromResult(Result<ProductionReportRenderResult>.Fail(Error.Failure(
            result.ErrorCode,
            result.ErrorMessage)));
    }
}

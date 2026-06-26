using System.Globalization;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

// Kirch: why - proves renderer execution over official binding DTOs without producing files, PDFs, print jobs, or templates.
public sealed class OfficialNoOutputReportRenderer : IOfficialReportRenderer
{
    private const string PreviewPlaceholderLabel = "SALRYN official preview placeholder";

    public static OfficialReportRendererContract RendererContract { get; } = new(
        "SALRYN official no-output renderer shell",
        new[]
        {
            new OfficialReportRendererCapability(
                OfficialReportRenderFormat.Preview,
                "Validates the official binding document and returns a preview placeholder only."),
            new OfficialReportRendererCapability(
                OfficialReportRenderFormat.Pdf,
                "Validates the official binding document and returns a PDF intent placeholder only."),
            new OfficialReportRendererCapability(
                OfficialReportRenderFormat.PrintJob,
                "Validates the official binding document and returns a print-job intent placeholder only."),
            new OfficialReportRendererCapability(
                OfficialReportRenderFormat.Export,
                "Validates the official binding document and returns an export intent placeholder only.")
        });

    public Task<Result<OfficialReportRenderResult>> RenderAsync(
        OfficialReportRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result contractValidation =
            OfficialReportRendererContractValidator.ValidateContract(RendererContract);

        if (contractValidation.IsFailure)
        {
            return Task.FromResult(Result<OfficialReportRenderResult>.Fail(contractValidation.ErrorDetail));
        }

        Result requestValidation =
            OfficialReportRendererContractValidator.ValidateRequest(request);

        if (requestValidation.IsFailure)
        {
            return Task.FromResult(Result<OfficialReportRenderResult>.Fail(requestValidation.ErrorDetail));
        }

        OfficialReportRenderRequest normalizedRequest =
            OfficialReportRendererContractValidator.NormalizeRequest(request);

        OfficialReportBindingDocument sourceDocument = normalizedRequest.SourceDocument!;

        OfficialReportRenderResult result = new(
            new OfficialReportRenderedArtifact(
                normalizedRequest.Format,
                ResolveArtifactLabel(normalizedRequest, sourceDocument),
                ArtifactReference: null,
                CreatedAt: normalizedRequest.RequestedAt),
            RendererContract);

        return Task.FromResult(Result<OfficialReportRenderResult>.Success(result));
    }

    private static string ResolveArtifactLabel(
        OfficialReportRenderRequest request,
        OfficialReportBindingDocument sourceDocument)
    {
        if (!string.IsNullOrWhiteSpace(request.ArtifactLabel))
        {
            return request.ArtifactLabel;
        }

        return PreviewPlaceholderLabel +
            " (" +
            sourceDocument.Tables.Count.ToString(CultureInfo.InvariantCulture) +
            " binding tables)";
    }
}

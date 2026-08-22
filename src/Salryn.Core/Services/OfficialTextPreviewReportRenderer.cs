using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialTextPreviewRenderResult(
    OfficialReportRenderResult ContractResult,
    OfficialReceiptInvoiceLayoutDocument LayoutDocument,
    OfficialReceiptInvoiceTextRenderResult TextResult,
    OfficialTextPreviewArtifactPackage ArtifactPackage,
    string ArtifactReference,
    string ContentHash,
    int LineCount,
    OfficialReportRendererContract RendererContract);

public sealed record OfficialTextPreviewArtifactPackage(
    OfficialReportRenderResult ContractResult,
    OfficialReceiptInvoiceLayoutDocument LayoutDocument,
    OfficialReceiptInvoiceTextRenderResult TextResult,
    string ArtifactReference,
    string ContentHash,
    int LineCount,
    OfficialReportRendererContract RendererContract);

// Kirch: why - concrete Core preview output can now be inspected without files, printing, templates, or adapter runtime dependencies.
public sealed class OfficialTextPreviewReportRenderer : IOfficialReportRenderer
{
    private const string DefaultArtifactLabel = "OFFICIAL_TEXT_PREVIEW";
    private const string ArtifactReferencePrefix = "salryn://official-text-preview/";

    private readonly OfficialReceiptInvoiceLayoutBuilder _layoutBuilder;
    private readonly OfficialReceiptInvoiceTextRenderer _textRenderer;

    public OfficialTextPreviewReportRenderer(
        OfficialReceiptInvoiceLayoutBuilder? layoutBuilder = null,
        OfficialReceiptInvoiceTextRenderer? textRenderer = null)
    {
        _layoutBuilder = layoutBuilder ?? new OfficialReceiptInvoiceLayoutBuilder();
        _textRenderer = textRenderer ?? new OfficialReceiptInvoiceTextRenderer();
    }

    public static OfficialReportRendererContract RendererContract { get; } = new(
        nameof(OfficialTextPreviewReportRenderer),
        new[]
        {
            new OfficialReportRendererCapability(
                OfficialReportRenderFormat.Preview,
                "Builds deterministic in-memory official receipt/invoice text preview artifacts.")
        });

    public async Task<Result<OfficialReportRenderResult>> RenderAsync(
        OfficialReportRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        Result<OfficialTextPreviewRenderResult> previewResult =
            await RenderPreviewAsync(request, cancellationToken);

        return previewResult.IsFailure
            ? Result<OfficialReportRenderResult>.Fail(previewResult.ErrorDetail)
            : Result<OfficialReportRenderResult>.Success(previewResult.Value.ContractResult);
    }

    public Task<Result<OfficialTextPreviewRenderResult>> RenderPreviewAsync(
        OfficialReportRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result contractValidation =
            OfficialReportRendererContractValidator.ValidateContract(RendererContract);

        if (contractValidation.IsFailure)
        {
            return Task.FromResult(Result<OfficialTextPreviewRenderResult>.Fail(contractValidation.ErrorDetail));
        }

        Result requestValidation =
            OfficialReportRendererContractValidator.ValidateRequest(request);

        if (requestValidation.IsFailure)
        {
            return Task.FromResult(Result<OfficialTextPreviewRenderResult>.Fail(requestValidation.ErrorDetail));
        }

        OfficialReportRenderRequest normalizedRequest =
            OfficialReportRendererContractValidator.NormalizeRequest(request);

        if (normalizedRequest.Format != OfficialReportRenderFormat.Preview)
        {
            return Task.FromResult(Result<OfficialTextPreviewRenderResult>.Fail(Error.Validation(
                "OfficialTextPreview.UnsupportedFormat",
                "Official text preview renderer supports Preview format only.")));
        }

        Result<OfficialReceiptInvoiceLayoutDocument> layoutResult =
            _layoutBuilder.Build(normalizedRequest.SourceDocument);

        if (layoutResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialTextPreviewRenderResult>.Fail(layoutResult.ErrorDetail));
        }

        Result<OfficialReceiptInvoiceTextRenderResult> textResult =
            _textRenderer.Render(layoutResult.Value);

        if (textResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialTextPreviewRenderResult>.Fail(textResult.ErrorDetail));
        }

        string contentHash = ComputeContentHash(textResult.Value.Text);
        string artifactReference = ArtifactReferencePrefix + contentHash;

        OfficialReportRenderResult contractResult = new(
            new OfficialReportRenderedArtifact(
                OfficialReportRenderFormat.Preview,
                normalizedRequest.ArtifactLabel ?? DefaultArtifactLabel,
                artifactReference,
                normalizedRequest.RequestedAt),
            RendererContract);

        OfficialTextPreviewArtifactPackage package = new(
            contractResult,
            layoutResult.Value,
            textResult.Value,
            artifactReference,
            contentHash,
            textResult.Value.Lines.Count,
            RendererContract);

        OfficialTextPreviewRenderResult result = new(
            contractResult,
            layoutResult.Value,
            textResult.Value,
            package,
            artifactReference,
            contentHash,
            textResult.Value.Lines.Count,
            RendererContract);

        return Task.FromResult(Result<OfficialTextPreviewRenderResult>.Success(result));
    }

    private static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

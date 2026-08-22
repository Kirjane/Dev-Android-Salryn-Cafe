using Salryn.Core.Common;

namespace Salryn.Core.Services.InvoiceOutput;

public interface IOfficialInvoiceSnapshotReadService
{
    Task<Result<OfficialInvoiceSnapshotIdentity>> GetBySaleIdAsync(
        Guid saleId,
        CancellationToken cancellationToken = default);

    Task<Result<OfficialInvoiceSnapshotIdentity>> GetByOfficialDocumentNumberAsync(
        string officialDocumentNumber,
        CancellationToken cancellationToken = default);
}

public interface IInvoiceOutputService
{
    Task<Result<InvoiceOutputPackage>> GenerateAsync(
        InvoiceOutputRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-facing invoice output orchestration. Reads immutable snapshot only; never mutates sale/sequence/EJ.
/// </summary>
public sealed class InvoiceOutputService : IInvoiceOutputService
{
    private readonly IOfficialInvoiceSnapshotReadService _snapshotRead;
    private readonly InvoiceRenderModelBuilder _modelBuilder = new();
    private readonly InvoiceHtmlRenderer _htmlRenderer = new();
    private readonly InvoiceThermalTextRenderer _thermalRenderer = new();

    public InvoiceOutputService(IOfficialInvoiceSnapshotReadService snapshotRead)
    {
        _snapshotRead = snapshotRead ?? throw new ArgumentNullException(nameof(snapshotRead));
    }

    /// <summary>Pure path for unit/golden tests without SQLite.</summary>
    public Result<InvoiceOutputPackage> GenerateFromIdentity(
        OfficialInvoiceSnapshotIdentity identity,
        InvoiceOutputMode mode,
        IReadOnlyList<InvoiceOutputFormat> formats)
    {
        Result<ParsedOfficialInvoiceSnapshot> parsed = OfficialInvoiceSnapshotParser.ParseAndVerify(identity);
        if (parsed.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(parsed.ErrorDetail);
        }

        Result<InvoiceRenderModel> modelResult = _modelBuilder.Build(parsed.Value, mode);
        if (modelResult.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(modelResult.ErrorDetail);
        }

        InvoiceRenderModel model = modelResult.Value;
        List<InvoiceOutputArtifact> artifacts = new();
        IReadOnlyList<InvoiceOutputFormat> requested = formats is { Count: > 0 }
            ? formats
            : new[] { InvoiceOutputFormat.Html, InvoiceOutputFormat.ThermalText };

        foreach (InvoiceOutputFormat format in requested.Distinct())
        {
            Result<InvoiceOutputArtifact> artifact = format switch
            {
                InvoiceOutputFormat.Html => _htmlRenderer.Render(model),
                InvoiceOutputFormat.ThermalText => _thermalRenderer.Render(model),
                _ => Result<InvoiceOutputArtifact>.Fail(Error.Validation(
                    "InvoiceOutput.UnsupportedFormat",
                    "Unsupported invoice output format."))
            };

            if (artifact.IsFailure)
            {
                return Result<InvoiceOutputPackage>.Fail(artifact.ErrorDetail);
            }

            artifacts.Add(artifact.Value);
        }

        // Manifest primary hash = HTML if present, else first artifact.
        InvoiceOutputArtifact primary = artifacts.FirstOrDefault(a => a.Format == InvoiceOutputFormat.Html)
            ?? artifacts[0];

        string packageId = "invout-" + identity.SaleId.ToString("N")[..12] + "-" + primary.ContentSha256[..12];
        InvoiceOutputManifest manifest = new(
            PackageId: packageId,
            SaleId: identity.SaleId,
            JournalEntryId: identity.JournalEntryId,
            OfficialDocumentNumber: model.OfficialDocumentNumber,
            TaxpayerMode: model.TaxpayerMode,
            InvoiceVariant: model.Variant.ToString(),
            RendererName: primary.Format == InvoiceOutputFormat.Html
                ? InvoiceOutputConstants.HtmlRendererName
                : InvoiceOutputConstants.ThermalRendererName,
            RendererVersion: InvoiceOutputConstants.RendererVersion,
            MediaFormat: primary.ContentType,
            CanonicalContentSha256: primary.ContentSha256,
            OutputMode: mode,
            InternalWatermarkApplied: model.RequiresInternalWatermark,
            ProductionActivationEnabled: ProductionOfficialPostingActivation.IsEnabled,
            LiveOfficialPostingAllowed: false,
            ComplianceClaimed: false,
            MissingOptionalFields: model.MissingOptionalFields,
            ValidationNotes: model.BoundaryNotes,
            NoComplianceClaimNote: InvoiceOutputConstants.NoComplianceClaimNote);

        return Result<InvoiceOutputPackage>.Success(new InvoiceOutputPackage(model, manifest, artifacts));
    }

    public async Task<Result<InvoiceOutputPackage>> GenerateAsync(
        InvoiceOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request is null)
        {
            return Result<InvoiceOutputPackage>.Fail(Error.Validation(
                "InvoiceOutput.RequestRequired",
                "Invoice output request is required."));
        }

        Result<OfficialInvoiceSnapshotIdentity> identityResult;
        if (request.SaleId is Guid saleId && saleId != Guid.Empty)
        {
            identityResult = await _snapshotRead.GetBySaleIdAsync(saleId, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(request.OfficialDocumentNumber))
        {
            identityResult = await _snapshotRead
                .GetByOfficialDocumentNumberAsync(request.OfficialDocumentNumber.Trim(), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            return Result<InvoiceOutputPackage>.Fail(Error.Validation(
                "InvoiceOutput.IdentityRequired",
                "Sale ID or official document number is required."));
        }

        if (identityResult.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(identityResult.ErrorDetail);
        }

        return GenerateFromIdentity(
            identityResult.Value,
            request.Mode,
            request.Formats ?? Array.Empty<InvoiceOutputFormat>());
    }
}

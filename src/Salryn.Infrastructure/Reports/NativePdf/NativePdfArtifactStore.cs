using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Services.Reports;

namespace Salryn.Infrastructure.Reports.NativePdf;

public sealed class NativePdfArtifactStore
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly NativePdfArtifactRootPolicy _rootPolicy;
    private readonly Func<DateTimeOffset> _clock;

    public NativePdfArtifactStore(
        NativePdfArtifactRootPolicy? rootPolicy = null,
        Func<DateTimeOffset>? clock = null)
    {
        _rootPolicy = rootPolicy ?? NativePdfArtifactRootPolicy.CreateDefault();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public NativePdfArtifactRootPolicy RootPolicy => _rootPolicy;

    public async Task<Result<ProductionReportArtifactExportResult>> SaveAsync(
        ProductionReportDatasetEnvelope dataset,
        byte[] pdfBytes,
        string exportScope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(dataset);
            ArgumentNullException.ThrowIfNull(pdfBytes);
            cancellationToken.ThrowIfCancellationRequested();

            if (pdfBytes.Length == 0)
            {
                return Result<ProductionReportArtifactExportResult>.Fail(Error.Validation(
                    "Reports.EmptyPdfArtifact",
                    "Native PDF export produced an empty PDF byte array."));
            }

            string artifactId = Guid.NewGuid().ToString("N");
            DateTimeOffset createdAt = _clock();
            string relativeArtifactPath = _rootPolicy.CreateRelativeArtifactPath(dataset.ReportKind, createdAt, artifactId);
            string relativeManifestPath = _rootPolicy.CreateRelativeManifestPath(relativeArtifactPath);
            string artifactPath = _rootPolicy.ResolveControlledPath(relativeArtifactPath);
            string manifestPath = _rootPolicy.ResolveControlledPath(relativeManifestPath);

            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

            await File.WriteAllBytesAsync(artifactPath, pdfBytes, cancellationToken).ConfigureAwait(false);
            string writtenHash = NativePdfArtifactFactory.Sha256(await File.ReadAllBytesAsync(artifactPath, cancellationToken).ConfigureAwait(false));
            string expectedHash = NativePdfArtifactFactory.Sha256(pdfBytes);
            if (!string.Equals(writtenHash, expectedHash, StringComparison.Ordinal))
            {
                return Result<ProductionReportArtifactExportResult>.Fail(Error.Failure(
                    "Reports.ArtifactHashMismatch",
                    "Written PDF artifact hash did not match the source PDF bytes."));
            }

            FileInfo artifactInfo = new(artifactPath);
            ProductionReportArtifactManifest manifest = new(
                ArtifactId: artifactId,
                ReportKind: dataset.ReportKind,
                FileName: Path.GetFileName(artifactPath),
                ContentType: NativePdfArtifactFactory.PdfContentType,
                ByteLength: artifactInfo.Length,
                PdfSha256: writtenHash,
                DatasetContentSha256: dataset.ContentSha256,
                DatasetSha256: dataset.DatasetSha256,
                RendererName: NativePdfRendererIdentity.RendererName,
                RendererVersion: NativePdfRendererIdentity.RendererVersion,
                CreatedAt: createdAt,
                ControlledRootPolicyLabel: _rootPolicy.PolicyLabel,
                RelativeArtifactPath: NormalizeForManifest(relativeArtifactPath),
                RelativeManifestPath: NormalizeForManifest(relativeManifestPath),
                ExportScope: string.IsNullOrWhiteSpace(exportScope) ? "local-report-export" : exportScope.Trim(),
                SafetyFlags: Snapshot(dataset.SafetyFlags),
                CallerPathAccepted: false,
                RetentionCleanupNote: NativePdfArtifactRootPolicy.DefaultRetentionCleanupNote);

            string json = JsonSerializer.Serialize(manifest, ManifestJson);
            await File.WriteAllTextAsync(manifestPath, json, cancellationToken).ConfigureAwait(false);

            ProductionReportArtifactManifest roundTrip = JsonSerializer.Deserialize<ProductionReportArtifactManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false),
                ManifestJson) ?? throw new InvalidOperationException("Report artifact manifest round-trip failed.");

            if (!string.Equals(roundTrip.PdfSha256, writtenHash, StringComparison.Ordinal))
            {
                return Result<ProductionReportArtifactExportResult>.Fail(Error.Failure(
                    "Reports.ManifestHashMismatch",
                    "Sidecar manifest hash did not match the written PDF artifact hash."));
            }

            return Result<ProductionReportArtifactExportResult>.Success(new ProductionReportArtifactExportResult(roundTrip));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<ProductionReportArtifactExportResult>.Fail(Error.Failure(
                "Reports.ArtifactWriteFailed",
                "Native PDF artifact export failed: " + ex.Message));
        }
    }

    private static ProductionReportArtifactSafetySnapshot Snapshot(ProductionReportSafetyFlags flags) => new(
        flags.DatasetAvailable,
        flags.UsesCanonicalM5Projection,
        flags.ProductionActivationEnabled,
        flags.BirESalesSchemaConfirmed,
        flags.ESalesTransmissionEnabled,
        flags.BirAccreditationClaimed,
        flags.CrystalRuntimeAvailable,
        flags.RendererCanProducePdf,
        flags.BoundaryNote);

    private static string NormalizeForManifest(string relativePath) =>
        relativePath.Replace('\\', '/');
}

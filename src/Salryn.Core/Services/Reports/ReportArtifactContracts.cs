namespace Salryn.Core.Services.Reports;

public sealed record ProductionReportArtifactSafetySnapshot(
    bool DatasetAvailable,
    bool UsesCanonicalM5Projection,
    bool ProductionActivationEnabled,
    bool BirESalesSchemaConfirmed,
    bool ESalesTransmissionEnabled,
    bool BirAccreditationClaimed,
    bool CrystalRuntimeAvailable,
    bool RendererCanProducePdf,
    string BoundaryNote);

public sealed record ProductionReportArtifactManifest(
    string ArtifactId,
    string ReportKind,
    string FileName,
    string ContentType,
    long ByteLength,
    string PdfSha256,
    string DatasetContentSha256,
    string DatasetSha256,
    string RendererName,
    string RendererVersion,
    DateTimeOffset CreatedAt,
    string ControlledRootPolicyLabel,
    string RelativeArtifactPath,
    string RelativeManifestPath,
    string ExportScope,
    ProductionReportArtifactSafetySnapshot SafetyFlags,
    bool CallerPathAccepted,
    string RetentionCleanupNote);

public sealed record ProductionReportArtifactExportResult(
    ProductionReportArtifactManifest Manifest);

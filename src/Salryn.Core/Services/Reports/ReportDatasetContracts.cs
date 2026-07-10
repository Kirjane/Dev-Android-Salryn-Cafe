namespace Salryn.Core.Services.Reports;

public static class ProductionReportDatasetLimits
{
    public const int MaxRangeDays = 31;
    public const int MaxRows = 5000;

    public static TimeSpan MaxRange => TimeSpan.FromDays(MaxRangeDays);
}

public sealed record ProductionReportDatasetRequest(
    string ReportKind,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    string? DocumentKind,
    Guid? ActorUserId,
    string? TerminalId);

public sealed record ProductionReportScope(
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    Guid? BirMachineProfileId,
    string RegistrationScopeCode,
    string DocumentKind,
    Guid? ActorUserId,
    string? TerminalId);

public sealed record ProductionReportControlTotal(
    string Code,
    string Label,
    long ValueCents,
    int Count);

public sealed record ProductionReportSourceHash(
    string SourceKind,
    string HashSha256,
    string Description);

public sealed record ProductionReportSafetyFlags(
    bool DatasetAvailable,
    bool UsesCanonicalM5Projection,
    bool ProductionActivationEnabled,
    bool BirESalesSchemaConfirmed,
    bool ESalesTransmissionEnabled,
    bool BirAccreditationClaimed,
    bool CrystalRuntimeAvailable,
    bool RendererCanProducePdf,
    string BoundaryNote);

public sealed record ProductionReportRow(
    string RowType,
    int SortOrdinal,
    IReadOnlyDictionary<string, string> Values);

public sealed record ProductionReportDatasetEnvelope(
    string SchemaCode,
    int SchemaVersion,
    string ReportKind,
    string ReportTitle,
    DateTimeOffset GeneratedAt,
    ProductionReportScope Scope,
    IReadOnlyList<ProductionReportControlTotal> ControlTotals,
    IReadOnlyList<ProductionReportRow> Rows,
    IReadOnlyList<ProductionReportSourceHash> SourceHashes,
    ProductionReportSafetyFlags SafetyFlags,
    string ContentSha256,
    string DatasetSha256,
    string Summary);

public sealed record ProductionReportArtifactMetadata(
    string FileName,
    string ContentType,
    long ByteLength,
    string Sha256,
    DateTimeOffset CreatedAt,
    string RendererName,
    string RendererVersion,
    string SourceDatasetSha256);

public static class CrystalWorkerProtocol
{
    public const string CurrentProtocolVersion = "SALRYN_CRYSTAL_WORKER_PROTOCOL_V1";
    public const string RequiredDeploymentMode = "isolated-windows-worker";
}

public sealed record CrystalWorkerDatasetPackage(
    string ProtocolVersion,
    string ReportKind,
    string ReportTitle,
    string DatasetJson,
    string DatasetContentSha256,
    string DatasetEnvelopeSha256,
    IReadOnlyList<ProductionReportSourceHash> SourceHashes,
    DateTimeOffset PackagedAt);

public sealed record CrystalWorkerArtifactPolicy(
    string OutputKind,
    string ControlledOutputRootPolicy,
    bool AllowCallerSuppliedOutputPath,
    bool AllowTemplateUpload,
    bool AllowArbitrarySql,
    long MaxArtifactBytes);

public sealed record CrystalWorkerLaunchPolicy(
    string DeploymentMode,
    string RequiredOSFamily,
    string RequiredFrameworkFamily,
    int TimeoutMilliseconds,
    bool KillWorkerOnTimeout,
    bool RequireRuntimeDiscovery,
    bool RequireTemplateAllowList);

public sealed record CrystalWorkerRuntimeReadiness(
    bool RuntimeInstalled,
    bool RuntimeLicensed,
    bool TemplateAllowListConfigured,
    bool CanRenderPdf,
    string RendererName,
    string RendererVersion,
    string StatusCode,
    string Message);

public sealed record CrystalWorkerReportRequest(
    string ProtocolVersion,
    string ReportKind,
    string DatasetJson,
    string DatasetSha256,
    string RequestedOutputKind,
    int TimeoutMilliseconds,
    string RendererName,
    string RendererVersion,
    DateTimeOffset RequestedAt);

public sealed record CrystalWorkerReportResponse(
    bool Success,
    ProductionReportArtifactMetadata? Artifact,
    string RendererName,
    string RendererVersion,
    string? ErrorCode,
    string? ErrorMessage,
    bool TimedOut,
    DateTimeOffset CompletedAt);

namespace Salryn.Core.Services.Reports;

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

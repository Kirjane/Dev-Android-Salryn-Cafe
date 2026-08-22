using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;

namespace Salryn.Core.Services.Reporting;

/// <summary>
/// M5-RC2 canonical reporting projection over immutable EJ + M4-RC2 lifecycle accounting.
/// Shared by X, Z, EJ export and traditional eSales draft.
/// </summary>
public sealed record OfficialReportingProjectionRequest(
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind);

public sealed record OfficialReportingDocumentRow(
    Guid JournalEntryId,
    Guid? SaleId,
    string OfficialDocumentNumber,
    DateTimeOffset PostedAt,
    string SnapshotSchema,
    string SnapshotPayloadHash,
    bool SnapshotHashMatches,
    string LifecycleState,
    long GrossTotalCents,
    long RefundedTotalCents,
    long NetTotalCents,
    bool IsVoided,
    long VatableBaseCents,
    long VatAmountCents,
    long VatExemptCents,
    long ZeroRatedCents,
    long SsptCents,
    long NonVatExemptCents,
    long RefundedVatableBaseCents,
    long RefundedVatAmountCents,
    long RefundedVatExemptCents,
    long RefundedZeroRatedCents,
    long RefundedSsptCents,
    long RefundedNonVatExemptCents,
    int ReprintCount,
    int VoidCount,
    int RefundCount,
    int AdjustmentCount);

public sealed record OfficialReportingEventRow(
    Guid EventId,
    Guid JournalEntryId,
    string OfficialDocumentNumber,
    string EventType,
    DateTimeOffset CreatedAt,
    string? Reason,
    Guid? ActorUserId,
    string? MetadataJson);

public sealed record OfficialReportingProjection(
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    IReadOnlyList<OfficialReportingDocumentRow> Documents,
    IReadOnlyList<OfficialReportingEventRow> Events,
    long GrossPostedSalesCents,
    long VoidedSalesCents,
    long RefundedValueCents,
    long NetSalesCents,
    long VatableSalesCents,
    long VatAmountCents,
    long VatExemptSalesCents,
    long ZeroRatedSalesCents,
    long SsptSalesCents,
    long NonVatExemptSalesCents,
    long CashSettlementRefundCents,
    int DocumentCount,
    int VoidedDocumentCount,
    int RefundEventCount,
    int ReprintEventCount,
    int AdjustmentEventCount,
    string ProjectionContentSha256,
    bool AllSnapshotHashesMatch,
    bool ProductionActivationEnabled,
    bool BirAuthoritativeESalesSchemaConfirmed,
    bool TransmissionEnabled,
    string Summary);

public interface IOfficialReportingProjectionService
{
    Task<Result<OfficialReportingProjection>> ProjectAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sealed adapter for future authoritative BIR eSales schema confirmation.
/// Default provider is unconfirmed and transmission-disabled.
/// </summary>
public interface ITraditionalESalesSchemaProvider
{
    string InternalSchemaCode { get; }
    bool BirAuthoritativeSchemaConfirmed { get; }
    bool TransmissionEnabled { get; }
    string SchemaBoundaryNote { get; }
}

public sealed class UnconfirmedTraditionalESalesSchemaProvider : ITraditionalESalesSchemaProvider
{
    public const string DraftSchemaCode = "SALRYN_TRAD_ESALES_DRAFT_V2";

    public string InternalSchemaCode => DraftSchemaCode;

    public bool BirAuthoritativeSchemaConfirmed => false;

    public bool TransmissionEnabled => false;

    public string SchemaBoundaryNote =>
        "Internal traditional eSales draft for engineering/demo. BIR authoritative transmission schema is not confirmed; " +
        "do not treat this file as a submitted BIR eSales package. Transmission is disabled.";
}

public sealed record TraditionalReportingExportPackage(
    string Kind,
    string SchemaCode,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd,
    string Utf8Content,
    string ContentSha256,
    int RowOrEventCount,
    long ControlTotalCents,
    bool BirAuthoritativeSchemaConfirmed,
    bool TransmissionEnabled,
    string Summary,
    OfficialReportingProjection Projection);

public sealed record TraditionalXReadingView(
    OfficialReportingProjection Projection,
    string Summary);

public sealed record TraditionalZReadingView(
    Guid ZReadingId,
    string EvidenceManifestHash,
    string ProjectionContentSha256,
    OfficialReportingProjection Projection,
    string Summary);

public interface IHostTraditionalReportingService
{
    Result<TraditionalReportingStatus> GetStatus();

    Task<Result<TraditionalXReadingView>> PreviewXReadingAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TraditionalZReadingView>> FinalizeZReadingAsync(
        OfficialReportingProjectionRequest request,
        Guid actorUserId,
        string? terminalId,
        string? failureInjectionStage = null,
        CancellationToken cancellationToken = default);

    Task<Result<TraditionalReportingExportPackage>> ExportEjAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TraditionalReportingExportPackage>> ExportESalesDraftAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TraditionalGrandTotalSnapshot>> GetGrandTotalSnapshotAsync(
        CancellationToken cancellationToken = default);
}

public sealed record TraditionalReportingStatus(
    bool ProductionActivationEnabled,
    bool LiveOfficialPostingAllowed,
    bool XReadingEnginePresent,
    bool ZReadingEnginePresent,
    bool EjExportImplemented,
    bool TraditionalESalesDraftImplemented,
    bool BirESalesSchemaConfirmed,
    bool TransmissionEnabled,
    bool GrandTotalResetImplemented,
    bool LifecycleAwareTotals,
    string Summary,
    IReadOnlyList<string> Notes);

public sealed record TraditionalGrandTotalSnapshot(
    long OfficialDocumentCount,
    long AccumulatedGrossPostedCents,
    long AccumulatedNetSalesCents,
    long AccumulatedRefundedCents,
    long AccumulatedVoidedCents,
    bool ResetCounterImplemented,
    string Summary);

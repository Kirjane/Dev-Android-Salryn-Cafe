namespace Salryn.Core.Models;

public enum CafePrinterStationKind
{
    CustomerReceipt = 1,
    Kitchen = 2,
    BarDrinks = 3,
    OtherPreparation = 4
}

public enum CafePrintDocumentKind
{
    CustomerReceipt = 1,
    KitchenTicket = 2
}

public enum CafePrintCopyKind
{
    Original = 1,
    Reprint = 2,
    VoidedReprint = 3
}

public enum CafePrintAttemptStatus
{
    Succeeded = 1,
    Failed = 2,
    DisabledUnavailable = 3
}

public sealed record CafePrinterStation(
    Guid StationId,
    Guid StoreId,
    string Name,
    CafePrinterStationKind StationKind,
    string? RouteKey,
    bool IsActive,
    long Version,
    string ContentHash,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    Guid? UpdatedByUserId,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CafePrintJob(
    Guid JobId,
    Guid OperationId,
    Guid StoreId,
    Guid DeviceId,
    Guid ActorUserId,
    Guid StationId,
    CafePrintDocumentKind DocumentKind,
    string SourceType,
    Guid SourceId,
    string? SourceVersion,
    string SnapshotText,
    string SnapshotHash,
    CafePrintCopyKind CopyKind,
    Guid? ParentJobId,
    string PayloadHash,
    DateTimeOffset CreatedAtUtc);

public sealed record CafePrintAttempt(
    Guid AttemptId,
    Guid JobId,
    Guid DeviceId,
    int AttemptNumber,
    string AdapterName,
    string AdapterMode,
    CafePrintAttemptStatus Status,
    string ResultCode,
    string ResultMessage,
    string? ErrorMessage,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset CompletedAtUtc);

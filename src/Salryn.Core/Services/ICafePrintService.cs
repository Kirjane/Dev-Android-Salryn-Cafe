using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record CafePrinterStationMutation(
    Guid OperationId,
    Guid StationId,
    Guid StoreId,
    Guid ActorUserId,
    string Name,
    CafePrinterStationKind StationKind,
    string? RouteKey,
    bool IsActive,
    long ExpectedVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeReceiptPrintJobMutation(
    Guid OperationId,
    Guid JobId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    Guid StationId,
    Guid SaleId,
    CafePrintCopyKind CopyKind,
    Guid? ParentJobId,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeKitchenPrintJobMutation(
    Guid OperationId,
    Guid JobId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    Guid StationId,
    Guid OrderId,
    Guid OrderRevisionId,
    CafePrintCopyKind CopyKind,
    Guid? ParentJobId,
    DateTimeOffset OccurredAtUtc);

public sealed record CafePrintJobMutationResult(
    CafePrintJob Job,
    bool IsReplay);

public sealed record CafePrinterStationMutationResult(
    CafePrinterStation Station,
    bool IsReplay);

public sealed record CafePrintAttemptResult(
    CafePrintAttempt Attempt,
    CafePrintJob Job);

public interface ICafePrintService
{
    Result<CafePrinterStationMutationResult> PublishStation(
        CafePrinterStationMutation mutation);

    Result<IReadOnlyList<CafePrinterStation>> ListStations(
        Guid storeId,
        Guid actorUserId);

    Task<Result<CafePrintJobMutationResult>> CreateReceiptJobAsync(
        CafeReceiptPrintJobMutation mutation,
        CancellationToken cancellationToken = default);

    Result<CafePrintJobMutationResult> CreateKitchenTicketJob(
        CafeKitchenPrintJobMutation mutation);

    Task<Result<CafePrintAttemptResult>> ExecuteAttemptAsync(
        Guid jobId,
        Guid deviceId,
        Guid actorUserId,
        IReceiptPrinterAdapter printerAdapter,
        CancellationToken cancellationToken = default);

    Result<CafePrintJob?> FindJob(
        Guid jobId,
        Guid actorUserId);
}

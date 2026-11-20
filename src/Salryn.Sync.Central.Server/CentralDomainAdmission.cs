using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

/// <summary>
/// Transport admission only. NONE of these contracts is currently applied to a Cafe
/// financial/domain projection by Central.Server; all recognized envelopes are retained
/// as NeedsReview until the Integrator reconciles the single authoritative processor.
/// </summary>
internal static class CentralDomainAdmission
{
    private static readonly HashSet<string> V1Types = new(StringComparer.Ordinal)
    {
        SaleCommittedV1Contract.OperationType,
        CafeOrderOperationContract.OrderRevisionCommittedV1,
        CafeOrderOperationContract.OrderParkedV1,
        CafeOrderOperationContract.OrderResumedV1,
        CafeOrderOperationContract.OrderPreparationSubmittedV1,
        CafeOrderOperationContract.OrderPreparationStatusChangedV1,
        CafeOrderOperationContract.OrderCancelledV1,
        CafeOrderOperationContract.OrderCompletedV1,
        CafeInventoryOperationContract.PurchaseReceiptCommittedV1,
        CafeInventoryOperationContract.InventoryWasteCommittedV1,
        CafeInventoryOperationContract.InventoryCountFinalizedV1,
        CafeInventoryOperationContract.InventoryAdjustmentCommittedV1,
        CafeCashMovementSyncContract.OperationType,
        CafeRefundSyncContract.Requested,
        CafeRefundSyncContract.Resolved,
        CafeRefundSyncContract.Committed,
        CafeDeliverySyncContract.DeliveryConfiguredV1,
        CafeDeliverySyncContract.DeliveryDispatchedV1,
        CafeDeliverySyncContract.DeliveryResolvedV1,
        CafeDeliverySyncContract.DeliveryCancelledV1,
        CafeBookingOperationContract.BookingRevisionCommittedV1,
        CafeBookingOperationContract.BookingStatusAdvancedV1,
        CafeBookingOperationContract.BookingCancelledV1,
        CafeBookingOperationContract.BookingDepositReceivedV1,
        CafeBookingOperationContract.BookingDepositRefundedV1,
        CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1,
        CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1,
        CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1,
        CafeAttendanceIncidentSyncContract.IncidentResolvedV1,
        IngredientConsumptionCommittedV1Contract.OperationType
    };

    internal static bool IsRecognized(string operationType, int schemaVersion) =>
        (schemaVersion == 1 && V1Types.Contains(operationType)) ||
        (schemaVersion == SaleCommittedV2Contract.SchemaVersion &&
         string.Equals(operationType, SaleCommittedV2Contract.OperationType, StringComparison.Ordinal)) ||
        (schemaVersion == SaleCommittedV3Contract.SchemaVersion &&
         string.Equals(operationType, SaleCommittedV3Contract.OperationType, StringComparison.Ordinal));

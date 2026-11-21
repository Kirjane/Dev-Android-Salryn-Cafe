namespace Salryn.Sync.Contracts;

public static class AppliedBusinessChangeContract
{
    public const string CafeShiftOpenedAppliedV1 = "CafeShiftOpenedAppliedV1";
    public const string CafeShiftClosedAppliedV1 = "CafeShiftClosedAppliedV1";
    public const string SaleCommittedV1AppliedV1 = "SaleCommittedV1AppliedV1";
    public const int SaleCommittedV1AppliedSchemaVersion = 1;
    public const string SaleCommittedV2AppliedV1 = "SaleCommittedV2AppliedV1";
    public const int SaleCommittedV2AppliedSchemaVersion = 1;
    public const string IngredientConsumptionCommittedV1AppliedV1 = "IngredientConsumptionCommittedV1AppliedV1";
    public const int IngredientConsumptionCommittedV1AppliedSchemaVersion = 1;
    public const string PurchaseReceiptCommittedV1AppliedV1 = "PurchaseReceiptCommittedV1AppliedV1";
    public const int PurchaseReceiptCommittedV1AppliedSchemaVersion = 1;
    public const string InventoryWasteCommittedV1AppliedV1 = "InventoryWasteCommittedV1AppliedV1";
    public const int InventoryWasteCommittedV1AppliedSchemaVersion = 1;
    public const string InventoryCountFinalizedV1AppliedV1 = "InventoryCountFinalizedV1AppliedV1";
    public const int InventoryCountFinalizedV1AppliedSchemaVersion = 1;
    public const string InventoryAdjustmentCommittedV1AppliedV1 = "InventoryAdjustmentCommittedV1AppliedV1";
    public const int InventoryAdjustmentCommittedV1AppliedSchemaVersion = 1;

    public const string OrderRevisionCommittedV1AppliedV1 = "OrderRevisionCommittedV1AppliedV1";
    public const string OrderParkedV1AppliedV1 = "OrderParkedV1AppliedV1";
    public const string OrderResumedV1AppliedV1 = "OrderResumedV1AppliedV1";
    public const string OrderPreparationSubmittedV1AppliedV1 = "OrderPreparationSubmittedV1AppliedV1";
    public const string OrderPreparationStatusChangedV1AppliedV1 = "OrderPreparationStatusChangedV1AppliedV1";
    public const string OrderCancelledV1AppliedV1 = "OrderCancelledV1AppliedV1";
    public const string OrderCompletedV1AppliedV1 = "OrderCompletedV1AppliedV1";
    public const int CafeOrderAppliedSchemaVersion = 1;

    public const string SaleRefundRequestedV1AppliedV1 = "SaleRefundRequestedV1AppliedV1";
    public const string SaleRefundResolvedV1AppliedV1 = "SaleRefundResolvedV1AppliedV1";
    public const string SaleRefundCommittedV1AppliedV1 = "SaleRefundCommittedV1AppliedV1";
    public const int CafeRefundAppliedSchemaVersion = 1;

    public const string CashMovementPostedV1AppliedV1 = "CashMovementPostedV1AppliedV1";
    public const int CafeCashMovementAppliedSchemaVersion = 1;

    public const string DeliveryConfiguredV1AppliedV1 = "DeliveryConfiguredV1AppliedV1";
    public const string DeliveryDispatchedV1AppliedV1 = "DeliveryDispatchedV1AppliedV1";
    public const string DeliveryResolvedV1AppliedV1 = "DeliveryResolvedV1AppliedV1";
    public const string DeliveryCancelledV1AppliedV1 = "DeliveryCancelledV1AppliedV1";
    public const int CafeDeliveryAppliedSchemaVersion = 1;

    public const string BookingRevisionCommittedV1AppliedV1 = "BookingRevisionCommittedV1AppliedV1";
    public const string BookingStatusAdvancedV1AppliedV1 = "BookingStatusAdvancedV1AppliedV1";
    public const string BookingCancelledV1AppliedV1 = "BookingCancelledV1AppliedV1";
    public const string BookingDepositReceivedV1AppliedV1 = "BookingDepositReceivedV1AppliedV1";
    public const string BookingDepositRefundedV1AppliedV1 = "BookingDepositRefundedV1AppliedV1";
    public const string BookingFulfillmentOrderBoundV1AppliedV1 = "BookingFulfillmentOrderBoundV1AppliedV1";
    public const int CafeBookingAppliedSchemaVersion = 1;

    public const string SaleCommittedV3AppliedV1 = "SaleCommittedV3AppliedV1";
    public const int SaleCommittedV3AppliedSchemaVersion = 1;

    public const string AttendanceEventRecordedV1AppliedV1 = "AttendanceEventRecordedV1AppliedV1";
    public const string AttendanceEventCorrectedV1AppliedV1 = "AttendanceEventCorrectedV1AppliedV1";
    public const string IncidentRevisionCommittedV1AppliedV1 = "IncidentRevisionCommittedV1AppliedV1";
    public const string IncidentResolvedV1AppliedV1 = "IncidentResolvedV1AppliedV1";
    public const int CafeAttendanceIncidentAppliedSchemaVersion = 1;

    public const string Received = "Received";
    public const string Uninitialized = "Uninitialized";
    public const string Ready = "Ready";
    public const string BootstrapRequired = "BootstrapRequired";
    public const string ReconciliationRequired = "ReconciliationRequired";
}

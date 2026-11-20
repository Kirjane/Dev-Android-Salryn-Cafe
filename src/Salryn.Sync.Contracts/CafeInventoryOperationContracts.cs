namespace Salryn.Sync.Contracts;

public static class CafeInventoryOperationContract
{
    public const int SchemaVersion = 1;
    public const string PurchaseReceiptCommittedV1 = "PurchaseReceiptCommittedV1";
    public const string InventoryWasteCommittedV1 = "InventoryWasteCommittedV1";
    public const string InventoryCountFinalizedV1 = "InventoryCountFinalizedV1";
    public const string InventoryAdjustmentCommittedV1 = "InventoryAdjustmentCommittedV1";

    public static bool IsSupported(string operationType) =>
        string.Equals(operationType, PurchaseReceiptCommittedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, InventoryWasteCommittedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, InventoryCountFinalizedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, InventoryAdjustmentCommittedV1, StringComparison.Ordinal);
}

public sealed record CafeInventoryTargetDeltaV1(
    Guid LineId,
    string StockTargetType,
    Guid StockTargetId,
    string NameSnapshot,
    string StockUnitSnapshot,
    long QuantityDeltaX1000,
    long? AcquisitionCostCents = null,
    string? PurchasePackageLabel = null,
    long? PurchasePackageQuantityX1000 = null,
    long? PurchasePackageContentX1000 = null,
    long? ExpectedBeforeX1000 = null,
    long? CountedX1000 = null);

public sealed record PurchaseReceiptCommittedV1(
    Guid PurchaseReceiptId,
    Guid SupplierId,
    string SupplierNameSnapshot,
    string? ExternalReference,
    DateTimeOffset ReceivedAtUtc,
    Guid ActorUserId,
    string? Notes,
    long TotalAcquisitionCostCents,
    Guid? CorrectionOfPurchaseReceiptId,
    IReadOnlyList<CafeInventoryTargetDeltaV1> Lines);

public sealed record InventoryWasteCommittedV1(
    Guid WasteEventId,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? Notes,
    Guid? CorrectionOfWasteEventId,
    IReadOnlyList<CafeInventoryTargetDeltaV1> Lines);

public sealed record InventoryAdjustmentCommittedV1(
    Guid AdjustmentEventId,
    string StockTargetType,
    Guid StockTargetId,
    string NameSnapshot,
    string StockUnitSnapshot,
    long QuantityDeltaX1000,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    Guid? CorrectionOfReferenceId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record InventoryCountFinalizedV1(
    Guid CountSessionId,
    Guid FinalizedByUserId,
    DateTimeOffset FinalizedAtUtc,
    IReadOnlyList<CafeInventoryTargetDeltaV1> Lines);

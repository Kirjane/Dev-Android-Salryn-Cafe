namespace Salryn.Core.Models;

public static class CafeStockTargetTypes
{
    public const string Ingredient = "Ingredient";
    public const string Product = "Product";

    public static bool IsValid(string value) =>
        string.Equals(value, Ingredient, StringComparison.Ordinal) ||
        string.Equals(value, Product, StringComparison.Ordinal);
}

public static class CafeInventoryWasteReasons
{
    public const string Waste = "Waste";
    public const string Spoilage = "Spoilage";
    public const string Damage = "Damage";
    public const string Expired = "Expired";

    public static bool IsValid(string value) =>
        string.Equals(value, Waste, StringComparison.Ordinal) ||
        string.Equals(value, Spoilage, StringComparison.Ordinal) ||
        string.Equals(value, Damage, StringComparison.Ordinal) ||
        string.Equals(value, Expired, StringComparison.Ordinal);
}

public static class CafeInventoryCountStates
{
    public const string Draft = "Draft";
    public const string Finalized = "Finalized";
}

public static class CafeInventoryMovementKinds
{
    public const string PurchaseReceiving = "PurchaseReceiving";
    public const string PurchaseCorrection = "PurchaseCorrection";
    public const string Waste = "Waste";
    public const string WasteCorrection = "WasteCorrection";
    public const string CountCorrection = "CountCorrection";
    public const string ManualAdjustment = "ManualAdjustment";
}

public sealed record SupplierConfig(
    Guid Id,
    string Name,
    string? ContactNumber,
    string? Notes,
    bool IsActive);

public sealed record SupplierMutation(
    Guid? Id,
    string Name,
    string? ContactNumber,
    string? Notes,
    bool IsActive);

public sealed record SupplierPublicationSummary(
    Guid RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    DateTimeOffset PublishedAtUtc);

public sealed record SupplierSnapshot(
    Guid SupplierId,
    string Name,
    string? ContactNumber,
    string? Notes,
    bool IsActive,
    int DisplayOrder);

public sealed record SupplierDirectoryPublication(
    SupplierPublicationSummary Summary,
    IReadOnlyList<SupplierSnapshot> Suppliers);

public sealed record SupplierPublicationApplyResult(
    bool Applied,
    bool Idempotent,
    SupplierPublicationSummary CurrentPublication);

public sealed record PurchaseReceiptLine(
    Guid LineId,
    string StockTargetType,
    Guid StockTargetId,
    string NameSnapshot,
    string StockUnitSnapshot,
    long QuantityReceivedX1000,
    string? PurchasePackageLabel,
    long? PurchasePackageQuantityX1000,
    long? PurchasePackageContentX1000,
    long LineAcquisitionCostCents);

public sealed record PurchaseReceiptEvidence(
    Guid PurchaseReceiptId,
    Guid OperationId,
    Guid StoreId,
    Guid SupplierId,
    string SupplierNameSnapshot,
    string? ExternalReference,
    DateTimeOffset ReceivedAtUtc,
    Guid ActorUserId,
    string? Notes,
    long TotalAcquisitionCostCents,
    string ContentHash,
    Guid? CorrectionOfPurchaseReceiptId,
    IReadOnlyList<PurchaseReceiptLine> Lines);

public sealed record PurchaseReceiptLineMutation(
    Guid? LineId,
    string StockTargetType,
    Guid StockTargetId,
    long QuantityReceivedX1000,
    long LineAcquisitionCostCents,
    string? PurchasePackageLabel = null,
    long? PurchasePackageQuantityX1000 = null,
    long? PurchasePackageContentX1000 = null);

public sealed record PurchaseReceiptMutation(
    Guid OperationId,
    Guid PurchaseReceiptId,
    Guid DeviceId,
    Guid StoreId,
    Guid SupplierId,
    Guid ActorUserId,
    DateTimeOffset ReceivedAtUtc,
    IReadOnlyList<PurchaseReceiptLineMutation> Lines,
    string? ExternalReference = null,
    string? Notes = null,
    Guid? CorrectionOfPurchaseReceiptId = null);

public sealed record InventoryWasteLine(
    Guid LineId,
    string StockTargetType,
    Guid StockTargetId,
    string NameSnapshot,
    string StockUnitSnapshot,
    long QuantityWastedX1000);

public sealed record InventoryWasteEvidence(
    Guid WasteEventId,
    Guid OperationId,
    Guid StoreId,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? Notes,
    string ContentHash,
    Guid? CorrectionOfWasteEventId,
    IReadOnlyList<InventoryWasteLine> Lines);

public sealed record InventoryWasteLineMutation(
    Guid? LineId,
    string StockTargetType,
    Guid StockTargetId,
    long QuantityWastedX1000);

public sealed record InventoryWasteMutation(
    Guid OperationId,
    Guid WasteEventId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    IReadOnlyList<InventoryWasteLineMutation> Lines,
    string? Notes = null,
    Guid? CorrectionOfWasteEventId = null);

public sealed record InventoryAdjustmentEvidence(
    Guid AdjustmentEventId,
    Guid OperationId,
    Guid StoreId,
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
    DateTimeOffset OccurredAtUtc,
    string ContentHash);

public sealed record InventoryAdjustmentMutation(
    Guid OperationId,
    Guid AdjustmentEventId,
    Guid DeviceId,
    Guid StoreId,
    string StockTargetType,
    Guid StockTargetId,
    long QuantityDeltaX1000,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? ReferenceType = null,
    Guid? ReferenceId = null,
    Guid? CorrectionOfReferenceId = null);

public sealed record InventoryCountLine(
    Guid LineId,
    string StockTargetType,
    Guid StockTargetId,
    string NameSnapshot,
    string StockUnitSnapshot,
    long ExpectedBeforeX1000,
    long CountedX1000,
    long QuantityDeltaX1000);

public sealed record InventoryCountSession(
    Guid CountSessionId,
    Guid StoreId,
    string State,
    Guid StartedByUserId,
    DateTimeOffset StartedAtUtc,
    Guid? FinalizedByUserId,
    DateTimeOffset? FinalizedAtUtc,
    string? Notes,
    Guid? FinalizeOperationId,
    string? FinalizedContentHash,
    IReadOnlyList<InventoryCountLine> Lines);

public sealed record InventoryCountLineMutation(
    Guid? LineId,
    string StockTargetType,
    Guid StockTargetId,
    long ExpectedBeforeX1000,
    long CountedX1000);

public sealed record InventoryCountStartMutation(
    Guid CountSessionId,
    Guid StoreId,
    Guid StartedByUserId,
    DateTimeOffset StartedAtUtc,
    string? Notes,
    IReadOnlyList<InventoryCountLineMutation> Lines);

public sealed record InventoryCountFinalizeMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid StoreId,
    Guid CountSessionId,
    Guid FinalizedByUserId,
    DateTimeOffset FinalizedAtUtc);

public sealed record CafeLowStockItem(
    string StockTargetType,
    Guid StockTargetId,
    string Name,
    string StockUnit,
    long StockOnHandX1000,
    long ReorderLevelX1000);

public sealed record CafeInventoryCommitResult<T>(
    T Evidence,
    bool IsReplay);

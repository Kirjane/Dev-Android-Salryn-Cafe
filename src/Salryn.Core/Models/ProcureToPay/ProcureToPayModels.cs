namespace Salryn.Core.Models.ProcureToPay;

public static class PurchaseClassifications
{
    public const string Resale = "Resale";
    public const string Consumable = "Consumable";
    public const string RawMaterial = "RawMaterial";
    public const string FixedAsset = "FixedAsset";
    public const string Service = "Service";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Resale,
        Consumable,
        RawMaterial,
        FixedAsset,
        Service
    };

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        All.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Resale;
        foreach (string item in All)
        {
            if (string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }
        return Resale;
    }

    public static bool CanAffectSellableStock(string classification) =>
        string.Equals(classification, Resale, StringComparison.OrdinalIgnoreCase);
}

public static class SupplierInvoiceMatchStatuses
{
    public const string PendingDocuments = "PendingDocuments";
    public const string Matched = "Matched";
    public const string VarianceFlagged = "VarianceFlagged";
    public const string PriceVariance = "PriceVariance";
    public const string QtyVariance = "QtyVariance";
    public const string QtyAndPriceVariance = "QtyAndPriceVariance";
    public const string Overbilled = "Overbilled";
}

public static class SupplierPaymentStatuses
{
    public const string PendingApproval = "PendingApproval";
    public const string Settled = "Settled";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

public static class StockBucketStatuses
{
    public const string Available = "Available";
    public const string Reserved = "Reserved";
    public const string Quarantine = "Quarantine";
    public const string Damaged = "Damaged";
    public const string InTransit = "InTransit";
    public const string OnHold = "OnHold";
}

public sealed record LpiSupplier(
    Guid Id,
    string? Code,
    string Name,
    string? ContactPerson,
    string? ContactNumber,
    string? Email,
    string? Address,
    string? Tin,
    int PaymentTermsDays,
    int LeadTimeDays,
    string DefaultCurrency,
    string? Notes,
    bool IsActive,
    long OpenPayableBalanceCents,
    int ActivePurchaseOrderCount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CreateLpiSupplierRequest(
    string? Code,
    string Name,
    string? ContactPerson,
    string? ContactNumber,
    string? Email,
    string? Address,
    string? Tin,
    int PaymentTermsDays,
    int LeadTimeDays,
    string? Notes,
    Guid ActorUserId);

public sealed record UpdateLpiSupplierRequest(
    string? Code,
    string Name,
    string? ContactPerson,
    string? ContactNumber,
    string? Email,
    string? Address,
    string? Tin,
    int PaymentTermsDays,
    int LeadTimeDays,
    string? Notes,
    bool IsActive,
    Guid ActorUserId);

public sealed record InventoryLocationModel(
    Guid Id,
    string Code,
    string Name,
    string LocationType,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record ProductStockBucketModel(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? Sku,
    Guid LocationId,
    string LocationCode,
    string LocationName,
    string BucketStatus,
    long QuantityX1000,
    DateTimeOffset UpdatedAt);

public sealed record LpiFoundationCatalog(
    IReadOnlyList<LpiSupplier> Suppliers,
    IReadOnlyList<InventoryLocationModel> Locations,
    IReadOnlyList<string> PurchaseClassifications,
    IReadOnlyDictionary<string, long> SequenceCounters);

// S1: Purchase Requisitions (PR), Quotations & Purchase Orders (PO)
public sealed record PurchaseRequisitionLineModel(
    Guid Id,
    Guid RequisitionId,
    int LineNumber,
    Guid? ProductId,
    string ItemCode,
    string ItemDescription,
    string CategoryName,
    string PurchaseClassification,
    string UnitName,
    long RequestedQtyX1000,
    long ApprovedQtyX1000,
    long ConvertedPoQtyX1000,
    long RemainingQtyToConvertX1000,
    long EstimatedUnitCostCents,
    long EstimatedLineTotalCents,
    Guid? PreferredSupplierId,
    string? PreferredSupplierName,
    string? Specifications,
    string? Notes);

public sealed record PurchaseRequisitionActionModel(
    Guid Id,
    Guid RequisitionId,
    string ActionType,
    Guid ActorUserId,
    string ActorDisplayName,
    string PreviousStatus,
    string NewStatus,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record PurchaseRequisitionModel(
    Guid Id,
    string PrNumber,
    string DepartmentName,
    Guid RequestedByUserId,
    string RequestedByName,
    DateOnly DateRequested,
    DateOnly DateNeeded,
    string Priority,
    string Purpose,
    string Status,
    long EstimatedTotalCents,
    Guid? ApprovedByUserId,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    string? RejectionOrRevisionReason,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseRequisitionLineModel> Lines,
    IReadOnlyList<PurchaseRequisitionActionModel> Actions);

public sealed record CreatePurchaseRequisitionLineInput(
    Guid? ProductId,
    string? ItemCode,
    string ItemDescription,
    string? CategoryName,
    string PurchaseClassification,
    string UnitName,
    long RequestedQtyX1000,
    long EstimatedUnitCostCents,
    Guid? PreferredSupplierId,
    string? Specifications,
    string? Notes);

public sealed record CreatePurchaseRequisitionRequest(
    string DepartmentName,
    DateOnly DateNeeded,
    string Priority,
    string Purpose,
    string? Notes,
    bool SubmitImmediately,
    IReadOnlyList<CreatePurchaseRequisitionLineInput> Lines,
    Guid RequestedByUserId);

public sealed record ReviewPurchaseRequisitionLineDecision(
    Guid RequisitionLineId,
    long ApprovedQtyX1000);

public sealed record ReviewPurchaseRequisitionRequest(
    Guid RequisitionId,
    string Decision, // "Approve", "RequestRevision", "Reject"
    string? Reason,
    IReadOnlyList<ReviewPurchaseRequisitionLineDecision>? LineDecisions,
    Guid ReviewerUserId);

public sealed record SupplierQuotationModel(
    Guid Id,
    Guid? RequisitionId,
    Guid? RequisitionLineId,
    Guid SupplierId,
    string SupplierName,
    Guid? ProductId,
    string ItemDescription,
    long QuotedUnitCostCents,
    long MinimumOrderQtyX1000,
    int LeadTimeDays,
    int PaymentTermsDays,
    DateOnly? ValidityDate,
    bool IsSelected,
    string? SelectionReason,
    Guid RecordedByUserId,
    DateTimeOffset CreatedAt);

public sealed record RecordSupplierQuotationRequest(
    Guid? RequisitionId,
    Guid? RequisitionLineId,
    Guid SupplierId,
    Guid? ProductId,
    string ItemDescription,
    long QuotedUnitCostCents,
    long MinimumOrderQtyX1000,
    int LeadTimeDays,
    int PaymentTermsDays,
    DateOnly? ValidityDate,
    bool IsSelected,
    string? SelectionReason,
    Guid ActorUserId);

public sealed record PurchaseOrderLineModel(
    Guid Id,
    Guid PurchaseOrderId,
    int LineNumber,
    Guid? RequisitionLineId,
    Guid? ProductId,
    string ItemCode,
    string ItemDescription,
    string PurchaseClassification,
    string UnitName,
    long OrderedQtyX1000,
    long ReceivedAcceptedQtyX1000,
    long ReceivedRejectedQtyX1000,
    long CancelledQtyX1000,
    long OutstandingQtyX1000,
    long UnitCostCents,
    long DiscountCents,
    int TaxRateBp,
    long TaxAmountCents,
    long LineTotalCents,
    string? Notes);

public sealed record PurchaseOrderHistoryModel(
    Guid Id,
    Guid PurchaseOrderId,
    string ActionType,
    Guid ActorUserId,
    string ActorDisplayName,
    string PreviousStatus,
    string NewStatus,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record PurchaseOrderModel(
    Guid Id,
    string PoNumber,
    Guid SupplierId,
    string SupplierName,
    string? SupplierCode,
    Guid? SourceRequisitionId,
    string? SourcePrNumber,
    DateOnly OrderDate,
    DateOnly ExpectedDeliveryDate,
    Guid DeliveryLocationId,
    string DeliveryLocationName,
    int PaymentTermsDays,
    string Status,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long ShippingCents,
    long TotalAmountCents,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid? ApprovedByUserId,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? ConfirmedAt,
    Guid? CancelledByUserId,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    string? TermsAndConditions,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PurchaseOrderLineModel> Lines,
    IReadOnlyList<PurchaseOrderHistoryModel> History);

public sealed record CreatePurchaseOrderLineInput(
    Guid? RequisitionLineId,
    Guid? ProductId,
    string? ItemCode,
    string ItemDescription,
    string PurchaseClassification,
    string UnitName,
    long OrderedQtyX1000,
    long UnitCostCents,
    long DiscountCents,
    int TaxRateBp,
    string? Notes);

public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    Guid? SourceRequisitionId,
    DateOnly ExpectedDeliveryDate,
    Guid? DeliveryLocationId,
    int? PaymentTermsDays,
    long ShippingCents,
    string? TermsAndConditions,
    string? Notes,
    bool SubmitAndApproveImmediately,
    IReadOnlyList<CreatePurchaseOrderLineInput> Lines,
    Guid ActorUserId);

public sealed record PurchaseOrderStatusActionRequest(
    Guid PurchaseOrderId,
    string Action, // "Submit", "Approve", "MarkSent", "MarkConfirmed", "Cancel", "Close"
    string? Reason,
    Guid ActorUserId);

// S2: Receiving Reports (RR), Stock Posting, Count Adjustments & Supplier Returns
public sealed record ReceivingReportLineModel(
    Guid Id,
    Guid ReceivingReportId,
    Guid PurchaseOrderLineId,
    Guid? ProductId,
    string ItemCode,
    string ItemDescription,
    string PurchaseClassification,
    string UnitName,
    long OrderedQtyX1000,
    long PreviouslyReceivedQtyX1000,
    long ActualDeliveredQtyX1000,
    long AcceptedQtyX1000,
    long RejectedQtyX1000,
    long ShortageQtyX1000,
    long OverdeliveryQtyX1000,
    long UnitCostCents,
    long AcceptedLineTotalCents,
    string? RejectionReason,
    string RejectionDisposition,
    string? LotOrBatchNo,
    DateOnly? ExpiryDate,
    Guid? StockMovementId);

public sealed record ReceivingReportModel(
    Guid Id,
    string RrNumber,
    Guid PurchaseOrderId,
    string PoNumber,
    Guid SupplierId,
    string SupplierName,
    Guid LocationId,
    string LocationName,
    string DeliveryReceiptNo,
    string? SupplierInvoiceRef,
    string? CarrierOrDriver,
    DateOnly ReceivedDate,
    string Status,
    bool HasOverdelivery,
    Guid? OverdeliveryApprovedByUserId,
    string? OverdeliveryApprovalReason,
    Guid ReceivedByUserId,
    string ReceivedByName,
    Guid? InspectedByUserId,
    Guid? PostedByUserId,
    DateTimeOffset? PostedAt,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ReceivingReportLineModel> Lines,
    string? RequestPayloadHash = null);

public sealed record CreateReceivingReportLineInput(
    Guid PurchaseOrderLineId,
    long ActualDeliveredQtyX1000,
    long AcceptedQtyX1000,
    long RejectedQtyX1000,
    string? RejectionReason,
    string? RejectionDisposition,
    string? LotOrBatchNo,
    DateOnly? ExpiryDate);

public sealed record CreateReceivingReportRequest(
    Guid PurchaseOrderId,
    Guid? LocationId,
    string DeliveryReceiptNo,
    string? SupplierInvoiceRef,
    string? CarrierOrDriver,
    DateOnly? ReceivedDate,
    bool PostImmediately,
    Guid? OverdeliveryApproverUserId,
    string? OverdeliveryApprovalReason,
    string? ClientRequestId,
    string? Notes,
    IReadOnlyList<CreateReceivingReportLineInput> Lines,
    Guid ReceivedByUserId,
    string? RequestPayloadHash = null);

public sealed record PostReceivingReportRequest(
    Guid ReceivingReportId,
    Guid? OverdeliveryApproverUserId,
    string? OverdeliveryApprovalReason,
    string? ClientRequestId,
    Guid ActorUserId,
    string? RequestPayloadHash = null);

public sealed record OverdeliveryApprovedLineModel(
    Guid PurchaseOrderLineId,
    Guid? ProductId,
    string ItemDescription,
    long ApprovedQtyX1000);

public sealed record OverdeliveryApprovalModel(
    Guid Id,
    Guid ReceivingReportId,
    Guid PurchaseOrderId,
    Guid ApproverUserId,
    string ApproverName,
    string ApprovalReason,
    IReadOnlyList<OverdeliveryApprovedLineModel> ApprovedLines,
    DateTimeOffset ApprovedAt,
    bool IsRevoked);

public sealed record ApproveOverdeliveryRequest(
    Guid ReceivingReportId,
    string ApprovalReason,
    Guid ApproverUserId);

public sealed record StockCountAdjustmentModel(
    Guid Id,
    string AdjustmentNumber,
    Guid ProductId,
    string ProductName,
    string? Sku,
    Guid LocationId,
    string LocationName,
    long SystemQtyX1000,
    long ActualCountedQtyX1000,
    long VarianceQtyX1000,
    long UnitCostCents,
    long VarianceValueCents,
    string ReasonCode,
    string? Notes,
    string Status,
    Guid CountedByUserId,
    string CountedByName,
    Guid ApprovedByUserId,
    string ApprovedByName,
    Guid? StockMovementId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt);

public sealed record RecordStockCountAdjustmentRequest(
    Guid ProductId,
    Guid? LocationId,
    long ActualCountedQtyX1000,
    string ReasonCode,
    string? Notes,
    Guid CountedByUserId,
    Guid ApprovedByUserId);

public sealed record SupplierReturnLineModel(
    Guid Id,
    Guid SupplierReturnId,
    Guid ReceivingReportLineId,
    Guid ProductId,
    string ProductName,
    long ReturnQtyX1000,
    long UnitCostCents,
    long LineTotalCents,
    string Reason,
    Guid? StockMovementId);

public sealed record SupplierReturnModel(
    Guid Id,
    string ReturnNumber,
    Guid SupplierId,
    string SupplierName,
    Guid ReceivingReportId,
    string RrNumber,
    Guid PurchaseOrderId,
    string PoNumber,
    Guid? SupplierInvoiceId,
    DateOnly ReturnDate,
    string Status,
    long TotalReturnAmountCents,
    string Reason,
    Guid CreatedByUserId,
    Guid ApprovedByUserId,
    DateTimeOffset PostedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SupplierReturnLineModel> Lines);

public sealed record CreateSupplierReturnLineInput(
    Guid ReceivingReportLineId,
    long ReturnQtyX1000,
    string Reason);

public sealed record CreateSupplierReturnRequest(
    Guid ReceivingReportId,
    Guid? SupplierInvoiceId,
    string Reason,
    IReadOnlyList<CreateSupplierReturnLineInput> Lines,
    Guid CreatedByUserId,
    Guid ApprovedByUserId);

// S3: Supplier Accounts Payable (AP), 3-Way Match & Payments
public sealed record SupplierInvoiceLineModel(
    Guid Id,
    Guid SupplierInvoiceId,
    Guid? PurchaseOrderLineId,
    Guid? ReceivingReportLineId,
    Guid? ProductId,
    string ItemDescription,
    long PoOrderedQtyX1000,
    long RrAcceptedQtyX1000,
    long InvoicedQtyX1000,
    long PoUnitCostCents,
    long InvoicedUnitCostCents,
    long TaxCents,
    long LineTotalCents,
    long QtyVarianceX1000,
    long UnitCostVarianceCents,
    string LineMatchStatus);

public sealed record SupplierInvoiceModel(
    Guid Id,
    string ApVoucherNumber,
    Guid SupplierId,
    string SupplierName,
    string SupplierInvoiceNo,
    Guid? PurchaseOrderId,
    string? PoNumber,
    Guid? ReceivingReportId,
    string? RrNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Status,
    string MatchStatus,
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long WithholdingTaxCents,
    long TotalInvoiceCents,
    long CreditMemoAppliedCents,
    long PaidAmountCents,
    long RemainingBalanceCents,
    int DaysPastDue,
    string AgingBucket,
    string? VarianceNotes,
    Guid? VarianceApprovedByUserId,
    string? VarianceApprovalReason,
    Guid RecordedByUserId,
    Guid? PostedByUserId,
    DateTimeOffset? PostedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SupplierInvoiceLineModel> Lines);

public sealed record CreateSupplierInvoiceLineInput(
    Guid? PurchaseOrderLineId,
    Guid? ReceivingReportLineId,
    Guid? ProductId,
    string ItemDescription,
    long InvoicedQtyX1000,
    long InvoicedUnitCostCents,
    long TaxCents);

public sealed record CreateSupplierInvoiceRequest(
    Guid SupplierId,
    string SupplierInvoiceNo,
    Guid? PurchaseOrderId,
    Guid? ReceivingReportId,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    long DiscountCents,
    long WithholdingTaxCents,
    bool PostToApIfMatched,
    IReadOnlyList<CreateSupplierInvoiceLineInput> Lines,
    Guid RecordedByUserId);

public sealed record ApproveInvoiceVarianceRequest(
    Guid SupplierInvoiceId,
    string ApprovalReason,
    bool PostToApImmediately,
    Guid ApproverUserId);

public sealed record SupplierPayableEntryModel(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    Guid SupplierInvoiceId,
    string ApVoucherNumber,
    string EntryType,
    string ReferenceNumber,
    long DebitCents,
    long CreditCents,
    long BalanceAfterCents,
    string? Note,
    Guid ActorUserId,
    DateTimeOffset CreatedAt);

public sealed record SupplierPaymentModel(
    Guid Id,
    string PaymentNumber,
    Guid SupplierInvoiceId,
    string ApVoucherNumber,
    string SupplierInvoiceNo,
    Guid SupplierId,
    string SupplierName,
    DateOnly PaymentDate,
    long AmountCents,
    string PaymentMethod,
    string DisbursementSource,
    string ReferenceNo,
    string? CheckNo,
    string? BankName,
    string? Remarks,
    string Status,
    Guid RecordedByUserId,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    string? RequestPayloadHash,
    DateTimeOffset CreatedAt);

public sealed record RecordSupplierPaymentRequest(
    Guid SupplierInvoiceId,
    DateOnly? PaymentDate,
    long AmountCents,
    string PaymentMethod,
    string DisbursementSource,
    string ReferenceNo,
    string? CheckNo,
    string? BankName,
    string? Remarks,
    string? ClientRequestId,
    Guid RecordedByUserId,
    Guid? ApprovedByUserId,
    string? RequestPayloadHash = null);

public sealed record ApproveSupplierPaymentRequest(
    Guid PaymentId,
    Guid ApproverUserId,
    string? Remarks = null);

public sealed record ApAgingSummaryModel(
    long TotalOpenPayablesCents,
    long CurrentBucketCents,
    long Days1To30Cents,
    long Days31To60Cents,
    long Days61To90Cents,
    long DaysOver90Cents,
    int OpenInvoiceCount,
    int OverdueInvoiceCount,
    int VarianceFlaggedInvoiceCount,
    IReadOnlyList<SupplierInvoiceModel> OpenInvoices);

public sealed record ProductStockCardEntryModel(
    Guid MovementId,
    DateTimeOffset OccurredAt,
    string MovementType,
    long QuantityChangeX1000,
    long QuantityBeforeX1000,
    long QuantityAfterX1000,
    long? UnitCostCents,
    string? ReferenceType,
    string? ReferenceId,
    string? DocumentNumber,
    string? Notes,
    string CreatedByName);

public sealed record ProductStockCardModel(
    Guid ProductId,
    string ProductName,
    string? Sku,
    string? Barcode,
    string UnitName,
    long CurrentStockOnHandX1000,
    long ReorderLevelX1000,
    long CurrentCostPriceCents,
    long SellingPriceCents,
    long InventoryValuationCents,
    bool IsLowStock,
    IReadOnlyList<ProductStockBucketModel> Buckets,
    IReadOnlyList<ProductStockCardEntryModel> Movements);

public sealed record LpiDashboardOverviewModel(
    int ActiveSupplierCount,
    int PendingRequisitionCount,
    int ApprovedRequisitionCount,
    int OpenPurchaseOrderCount,
    int PartiallyReceivedPoCount,
    int PostedReceivingReportCount,
    int PendingExceptionRrCount,
    int LowStockProductCount,
    long TotalInventoryValuationCents,
    long TotalOpenPayablesCents,
    long OverduePayablesCents,
    int VarianceFlaggedInvoiceCount,
    long TotalPaymentsDisbursedCents,
    ApAgingSummaryModel ApAging);

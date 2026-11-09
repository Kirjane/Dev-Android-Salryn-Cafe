using Salryn.Core.Common;
using Salryn.Core.Models.ProcureToPay;

namespace Salryn.Core.Services;

public interface IProcureToPayService
{
    // S0: Foundation (Suppliers, Locations, Sequences, Catalog)
    Task<Result<LpiFoundationCatalog>> GetFoundationCatalogAsync(
        bool includeInactiveSuppliers = true,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LpiSupplier>>> GetSuppliersAsync(
        string? search = null,
        bool includeInactive = true,
        CancellationToken cancellationToken = default);

    Task<Result<LpiSupplier>> CreateSupplierAsync(
        CreateLpiSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<LpiSupplier>> UpdateSupplierAsync(
        Guid supplierId,
        UpdateLpiSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<string>> AllocateNextDocumentNumberAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    // S1: Procurement (Purchase Requisitions, Quotations, Purchase Orders)
    Task<Result<IReadOnlyList<PurchaseRequisitionModel>>> GetPurchaseRequisitionsAsync(
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseRequisitionModel>> GetPurchaseRequisitionByIdAsync(
        Guid requisitionId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseRequisitionModel>> CreatePurchaseRequisitionAsync(
        CreatePurchaseRequisitionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseRequisitionModel>> SubmitPurchaseRequisitionAsync(
        Guid requisitionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseRequisitionModel>> ReviewPurchaseRequisitionAsync(
        ReviewPurchaseRequisitionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SupplierQuotationModel>>> GetSupplierQuotationsAsync(
        Guid? requisitionId = null,
        Guid? productId = null,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierQuotationModel>> RecordSupplierQuotationAsync(
        RecordSupplierQuotationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PurchaseOrderModel>>> GetPurchaseOrdersAsync(
        string? status = null,
        Guid? supplierId = null,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderModel>> GetPurchaseOrderByIdAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderModel>> CreatePurchaseOrderAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderModel>> TransitionPurchaseOrderStatusAsync(
        PurchaseOrderStatusActionRequest request,
        CancellationToken cancellationToken = default);

    // S2: Receiving & Inventory Control
    Task<Result<IReadOnlyList<ReceivingReportModel>>> GetReceivingReportsAsync(
        Guid? purchaseOrderId = null,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingReportModel>> GetReceivingReportByIdAsync(
        Guid receivingReportId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingReportModel>> CreateReceivingReportAsync(
        CreateReceivingReportRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingReportModel>> PostReceivingReportAsync(
        PostReceivingReportRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<StockCountAdjustmentModel>>> GetStockCountAdjustmentsAsync(
        Guid? productId = null,
        CancellationToken cancellationToken = default);

    Task<Result<StockCountAdjustmentModel>> RecordStockCountAdjustmentAsync(
        RecordStockCountAdjustmentRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SupplierReturnModel>>> GetSupplierReturnsAsync(
        Guid? supplierId = null,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnModel>> CreateSupplierReturnAsync(
        CreateSupplierReturnRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ProductStockCardModel>> GetProductStockCardAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ProductStockBucketModel>>> GetProductStockBucketsAsync(
        Guid? productId = null,
        CancellationToken cancellationToken = default);

    // S3: Supplier Accounts Payable, 3-Way Match & Payments
    Task<Result<IReadOnlyList<SupplierInvoiceModel>>> GetSupplierInvoicesAsync(
        Guid? supplierId = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierInvoiceModel>> GetSupplierInvoiceByIdAsync(
        Guid supplierInvoiceId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierInvoiceModel>> CreateSupplierInvoiceAsync(
        CreateSupplierInvoiceRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierInvoiceModel>> ApproveInvoiceVarianceAsync(
        ApproveInvoiceVarianceRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierInvoiceModel>> PostSupplierInvoiceToApAsync(
        Guid supplierInvoiceId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierPaymentModel>> RecordSupplierPaymentAsync(
        RecordSupplierPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SupplierPaymentModel>>> GetSupplierPaymentsAsync(
        Guid? supplierId = null,
        Guid? supplierInvoiceId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SupplierPayableEntryModel>>> GetSupplierPayableLedgerAsync(
        Guid? supplierId = null,
        CancellationToken cancellationToken = default);

    Task<Result<ApAgingSummaryModel>> GetApAgingSummaryAsync(
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default);

    Task<Result<LpiDashboardOverviewModel>> GetDashboardOverviewAsync(
        CancellationToken cancellationToken = default);
}

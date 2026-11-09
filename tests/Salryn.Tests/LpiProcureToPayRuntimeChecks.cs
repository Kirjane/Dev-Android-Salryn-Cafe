using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class LpiProcureToPayRuntimeChecks
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-lpi-runtime-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);

            Guid requesterUserId = SeedUser(paths.DatabasePath, "lpi-requester", "LPI Requester");
            Guid approverUserId = SeedUser(paths.DatabasePath, "lpi-approver", "LPI Approver");
            Guid receiverUserId = SeedUser(paths.DatabasePath, "lpi-receiver", "LPI Receiver");
            Guid apClerkUserId = SeedUser(paths.DatabasePath, "lpi-apclerk", "LPI AP Clerk");
            Guid financeManagerUserId = SeedUser(paths.DatabasePath, "lpi-finance", "LPI Finance Manager");

            Guid productAlphaId = SeedProduct(paths.DatabasePath, "LPI-ALPHA-001", "9900001000001", "Alpha Coffee Beans 1kg", 45_000, 75_000, 20_000, 10_000);
            Guid productBetaId = SeedProduct(paths.DatabasePath, "LPI-BETA-002", "9900001000002", "Beta Oat Milk 1L", 12_000, 22_000, 50_000, 15_000);
            Guid productGammaId = SeedProduct(paths.DatabasePath, "LPI-GAMMA-003", "9900001000003", "Gamma Takeout Cup Pack", 8_000, 15_000, 30_000, 5_000);

            // S0: Foundation (Suppliers, Locations, Classifications, Sequences)
            LpiFoundationCatalog foundation = RequireSuccess(await service.GetFoundationCatalogAsync(), "S0 foundation catalog");
            Require(foundation.Locations.Any(loc => loc.Code == "MAIN-STORE") &&
                    foundation.Locations.Any(loc => loc.Code == "QUARANTINE-HOLD"),
                "S0 default inventory locations (MAIN-STORE, QUARANTINE) must be seeded by v27 migration.");
            Guid mainWarehouseId = foundation.Locations.First(loc => loc.Code == "MAIN-STORE").Id;

            LpiSupplier supplierOne = RequireSuccess(
                await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                    "SUP-APEX-01",
                    "Apex Roasters & Dairy Supply Inc.",
                    "Maria Santos",
                    "09171234501",
                    "orders@apexroasters.ph",
                    "Makati Industrial Park, Metro Manila",
                    "123-456-789-000",
                    30,
                    5,
                    "Primary coffee and dairy vendor",
                    requesterUserId)),
                "S0 create supplier 1");

            LpiSupplier supplierTwo = RequireSuccess(
                await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                    "SUP-PACK-02",
                    "Metro Packaging Solutions Corp.",
                    "Carlo Reyes",
                    "09171234502",
                    "sales@metropack.ph",
                    "Pasig Logistics Hub, Metro Manila",
                    "987-654-321-000",
                    15,
                    3,
                    "Packaging and consumables vendor",
                    requesterUserId)),
                "S0 create supplier 2");

            Require((await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-APEX-01", "Duplicate Code Vendor", null, null, null, null, null, 30, 5, null, requesterUserId))).IsFailure,
                "S0 duplicate supplier code must be rejected.");

            // Scenario 1 & 2: PR creation, self-approval denial, approval, and partial conversion to multiple POs
            PurchaseRequisitionModel pr = RequireSuccess(
                await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                    "Store Operations",
                    new DateOnly(2026, 11, 20),
                    "Normal",
                    "Monthly restock for coffee bar and packaging",
                    "Split vendor restock request",
                    true,
                    new[]
                    {
                        new CreatePurchaseRequisitionLineInput(
                            productAlphaId, "LPI-ALPHA-001", "Alpha Coffee Beans 1kg", "Beverage", "Resale",
                            "bag", 10_000, 45_000, supplierOne.Id, "Arabica medium roast", "Urgent coffee stock"),
                        new CreatePurchaseRequisitionLineInput(
                            productGammaId, "LPI-GAMMA-003", "Gamma Takeout Cup Pack", "Packaging", "Consumable",
                            "pack", 20_000, 8_000, supplierTwo.Id, "12oz double-wall cups", "Cup restock")
                    },
                    requesterUserId)),
                "S1 create and submit PR");

            Require(pr.Status == "Submitted" && pr.PrNumber.StartsWith("PR-", StringComparison.Ordinal),
                "S1 submitted PR must allocate PR number and enter PendingApproval status.");
            Require(ReadStockOnHand(paths.DatabasePath, productAlphaId) == 20_000,
                "S1 PR creation must never change stock on hand.");

            // Self-approval denial gate
            Result<PurchaseRequisitionModel> selfApproveAttempt = await service.ReviewPurchaseRequisitionAsync(
                new ReviewPurchaseRequisitionRequest(pr.Id, "Approve", "Self-approving my own PR", null, requesterUserId));
            Require(selfApproveAttempt.IsFailure && selfApproveAttempt.ErrorDetail.Code == "Lpi.Requisition.SelfApprovalDenied",
                "S1 requester must be blocked from approving their own Purchase Requisition.");

            // Authorized approval by distinct approver
            pr = RequireSuccess(
                await service.ReviewPurchaseRequisitionAsync(
                    new ReviewPurchaseRequisitionRequest(pr.Id, "Approve", "Approved for split supplier sourcing", null, approverUserId)),
                "S1 approve PR");
            Require(pr.Status == "Approved" && pr.ApprovedByUserId == approverUserId,
                "S1 approved PR must record approver identity.");

            // Record supplier quotations for comparison
            SupplierQuotationModel quoteOne = RequireSuccess(
                await service.RecordSupplierQuotationAsync(new RecordSupplierQuotationRequest(
                    pr.Id, pr.Lines[0].Id, supplierOne.Id, productAlphaId, "Alpha Coffee Beans 1kg",
                    45_000, 5_000, 5, 30, new DateOnly(2026, 12, 31), true, "Best lead time and terms", requesterUserId)),
                "S1 record supplier quotation");
            Require(quoteOne.IsSelected, "S1 selected supplier quotation must persist.");

            // Partial PR conversion #1 -> PO for Supplier One (Line 0: Alpha Coffee Beans)
            PurchaseRequisitionLineModel prLineAlpha = pr.Lines.Single(l => l.ProductId == productAlphaId);
            PurchaseRequisitionLineModel prLineGamma = pr.Lines.Single(l => l.ProductId == productGammaId);

            PurchaseOrderModel poAlpha = RequireSuccess(
                await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                    supplierOne.Id,
                    pr.Id,
                    new DateOnly(2026, 11, 18),
                    mainWarehouseId,
                    30,
                    0,
                    "Deliver to rear receiving bay",
                    "PO #1 from PR split conversion",
                    false,
                    new[]
                    {
                        new CreatePurchaseOrderLineInput(
                            prLineAlpha.Id, productAlphaId, prLineAlpha.ItemCode, prLineAlpha.ItemDescription,
                            prLineAlpha.PurchaseClassification, prLineAlpha.UnitName, 10_000, 45_000, 0, 120, "1.2% freight tax")
                    },
                    requesterUserId)),
                "S1 create PO Alpha from PR line 1");

            PurchaseRequisitionModel prAfterFirstPo = RequireSuccess(
                await service.GetPurchaseRequisitionByIdAsync(pr.Id), "S1 reload PR after first PO");
            Require(prAfterFirstPo.Status == "PartiallyConverted",
                $"S1 PR with remaining unconverted lines must be PartiallyConverted (actual: {prAfterFirstPo.Status}).");

            // Partial PR conversion #2 -> PO for Supplier Two (Line 1: Gamma Takeout Cups)
            PurchaseOrderModel poGamma = RequireSuccess(
                await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                    supplierTwo.Id,
                    pr.Id,
                    new DateOnly(2026, 11, 16),
                    mainWarehouseId,
                    15,
                    0,
                    "Standard delivery",
                    "PO #2 from PR split conversion",
                    false,
                    new[]
                    {
                        new CreatePurchaseOrderLineInput(
                            prLineGamma.Id, productGammaId, prLineGamma.ItemCode, prLineGamma.ItemDescription,
                            prLineGamma.PurchaseClassification, prLineGamma.UnitName, 20_000, 8_000, 0, 0, null)
                    },
                    requesterUserId)),
                "S1 create PO Gamma from PR line 2");

            PurchaseRequisitionModel prAfterSecondPo = RequireSuccess(
                await service.GetPurchaseRequisitionByIdAsync(pr.Id), "S1 reload PR after second PO");
            Require(prAfterSecondPo.Status == "ConvertedToPo",
                $"S1 PR with all lines converted must transition to ConvertedToPo (actual: {prAfterSecondPo.Status}).");

            // Submit, Approve, and MarkSent PO Alpha
            poAlpha = RequireSuccess(
                await service.TransitionPurchaseOrderStatusAsync(
                    new PurchaseOrderStatusActionRequest(poAlpha.Id, "Submit", "Submitted for approval", requesterUserId)),
                "S1 submit PO Alpha");
            poAlpha = RequireSuccess(
                await service.TransitionPurchaseOrderStatusAsync(
                    new PurchaseOrderStatusActionRequest(poAlpha.Id, "Approve", "Approved by procurement manager", approverUserId)),
                "S1 approve PO Alpha");
            poAlpha = RequireSuccess(
                await service.TransitionPurchaseOrderStatusAsync(
                    new PurchaseOrderStatusActionRequest(poAlpha.Id, "MarkSent", "Dispatched to supplier email", requesterUserId)),
                "S1 mark PO Alpha sent");
            Require(poAlpha.Status == "Sent", "S1 PO Alpha must be Sent.");
            Require(ReadStockOnHand(paths.DatabasePath, productAlphaId) == 20_000,
                "S1 PO approval and dispatch must NOT modify inventory stock_on_hand_x1000.");

            // Scenario 10: PO cancellation before delivery with mandatory reason
            PurchaseOrderModel poToCancel = RequireSuccess(
                await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                    supplierTwo.Id, null, new DateOnly(2026, 11, 25), mainWarehouseId, 15, 0, null, "Temporary order",
                    true,
                    new[]
                    {
                        new CreatePurchaseOrderLineInput(
                            null, productBetaId, "LPI-BETA-002", "Beta Oat Milk 1L", "Resale", "box", 5_000, 12_000, 0, 0, null)
                    },
                    approverUserId)),
                "S1 create cancellable PO");

            Require((await service.TransitionPurchaseOrderStatusAsync(
                new PurchaseOrderStatusActionRequest(poToCancel.Id, "Cancel", "", approverUserId))).IsFailure,
                "S1 PO cancellation without a reason must fail.");

            poToCancel = RequireSuccess(
                await service.TransitionPurchaseOrderStatusAsync(
                    new PurchaseOrderStatusActionRequest(poToCancel.Id, "Cancel", "Supplier out of stock for oat milk batch", approverUserId)),
                "S1 cancel PO before delivery");
            Require(poToCancel.Status == "Cancelled" && poToCancel.CancellationReason!.Contains("out of stock", StringComparison.Ordinal),
                "S1 cancelled PO must retain cancellation reason and history.");

            // Scenario 3 & 4: Partial Goods Receiving (RR #1) + Rejected/Damaged items routed to Quarantine
            Guid poAlphaLineId = poAlpha.Lines.Single().Id;
            // Ordered: 10 units (10_000). First delivery: 7 delivered (7_000) -> 6 accepted (6_000), 1 rejected/damaged (1_000).
            ReceivingReportModel rrOne = RequireSuccess(
                await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                    poAlpha.Id,
                    mainWarehouseId,
                    "DR-APEX-2026-001",
                    "SI-APEX-2026-001",
                    "Apex Logistics Van #4",
                    new DateOnly(2026, 11, 10),
                    true,
                    null,
                    null,
                    "rr-req-1",
                    "First partial delivery; 1 bag punctured in transit",
                    new[]
                    {
                        new CreateReceivingReportLineInput(
                            poAlphaLineId,
                            7_000,
                            6_000,
                            1_000,
                            "Bag punctured during courier handling",
                            "Quarantine",
                            "BATCH-ALPHA-11A",
                            new DateOnly(2027, 5, 30))
                    },
                    receiverUserId)),
                "S2 post partial RR #1 with rejected item");

            Require(rrOne.Status == "Posted" && !rrOne.HasOverdelivery,
                "S2 partial RR #1 within ordered quantity must post immediately.");
            Require(ReadStockOnHand(paths.DatabasePath, productAlphaId) == 26_000,
                $"S2 only accepted quantity (6_000) must increase sellable stock_on_hand_x1000 from 20_000 to 26_000 (actual: {ReadStockOnHand(paths.DatabasePath, productAlphaId)}).");

            IReadOnlyList<ProductStockBucketModel> alphaBucketsAfterRr1 = RequireSuccess(
                await service.GetProductStockBucketsAsync(productAlphaId), "S2 stock buckets after RR #1");
            Require(alphaBucketsAfterRr1.Any(b => b.BucketStatus == "Quarantine" && b.QuantityX1000 == 1_000),
                "S2 rejected/damaged quantity (1_000) must be recorded in Quarantine stock bucket without inflating sellable stock.");

            poAlpha = RequireSuccess(await service.GetPurchaseOrderByIdAsync(poAlpha.Id), "S2 reload PO Alpha after RR #1");
            Require(poAlpha.Status == "PartiallyReceived" &&
                    poAlpha.Lines.Single().ReceivedAcceptedQtyX1000 == 6_000 &&
                    poAlpha.Lines.Single().OutstandingQtyX1000 == 4_000,
                "S2 PO Alpha must be PartiallyReceived with 6_000 accepted and 4_000 outstanding.");

            // Scenario 4b: Overdelivery exception gate (attempting to receive 5_000 when only 4_000 remains without exception approver)
            Result<ReceivingReportModel> overdeliveryBlocked = await service.CreateReceivingReportAsync(
                new CreateReceivingReportRequest(
                    poAlpha.Id,
                    mainWarehouseId,
                    "DR-APEX-OVER",
                    null,
                    null,
                    new DateOnly(2026, 11, 11),
                    true,
                    null,
                    null,
                    "rr-req-over",
                    "Unapproved overdelivery attempt",
                    new[]
                    {
                        new CreateReceivingReportLineInput(
                            poAlphaLineId, 5_000, 5_000, 0, null, null, "BATCH-OVER", null)
                    },
                    receiverUserId));
            Require(overdeliveryBlocked.IsFailure && overdeliveryBlocked.ErrorDetail.Code == "Lpi.Receiving.OverdeliveryApprovalRequired",
                "S2 overdelivery without exception approver must be blocked when postImmediately=true.");

            // Scenario 3 completion: Second and final delivery (RR #2) for remaining 4_000 accepted
            ReceivingReportModel rrTwo = RequireSuccess(
                await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                    poAlpha.Id,
                    mainWarehouseId,
                    "DR-APEX-2026-002",
                    "SI-APEX-2026-001",
                    "Apex Logistics Van #4",
                    new DateOnly(2026, 11, 12),
                    true,
                    null,
                    null,
                    "rr-req-2",
                    "Second delivery completing PO Alpha",
                    new[]
                    {
                        new CreateReceivingReportLineInput(
                            poAlphaLineId,
                            4_000,
                            4_000,
                            0,
                            null,
                            null,
                            "BATCH-ALPHA-11B",
                            new DateOnly(2027, 6, 15))
                    },
                    receiverUserId)),
                "S2 post final RR #2");

            Require(rrTwo.Status == "Posted", "S2 final RR #2 must post.");
            Require(ReadStockOnHand(paths.DatabasePath, productAlphaId) == 30_000,
                "S2 sellable stock_on_hand_x1000 must reach 30_000 after 6_000 + 4_000 accepted.");

            poAlpha = RequireSuccess(await service.GetPurchaseOrderByIdAsync(poAlpha.Id), "S2 reload PO Alpha after RR #2");
            Require(poAlpha.Status == "FullyReceived" && poAlpha.Lines.Single().OutstandingQtyX1000 == 0,
                "S2 PO Alpha must transition to FullyReceived with 0 outstanding quantity.");

            // Scenario 9: Physical count adjustment with approval & audit
            StockCountAdjustmentModel countAdj = RequireSuccess(
                await service.RecordStockCountAdjustmentAsync(new RecordStockCountAdjustmentRequest(
                    productBetaId,
                    mainWarehouseId,
                    48_000, // system is 50_000 -> -2_000 shrinkage variance
                    "CycleCountShrinkage",
                    "2 cartons damaged during shelf reorganization",
                    receiverUserId,
                    approverUserId)),
                "S2 physical count adjustment");
            Require(countAdj.VarianceQtyX1000 == -2_000 && ReadStockOnHand(paths.DatabasePath, productBetaId) == 48_000,
                "S2 physical count adjustment must update stock_on_hand_x1000 and record variance.");

            // Scenario 5 & 6: Supplier Invoice recording, duplicate invoice guard, and 3-Way Match price discrepancy flagging
            poGamma = RequireSuccess(
                await service.TransitionPurchaseOrderStatusAsync(
                    new PurchaseOrderStatusActionRequest(poGamma.Id, "Submit", "Submit PO Gamma", requesterUserId)),
                "S3 submit PO Gamma");
            poGamma = RequireSuccess(
                await service.TransitionPurchaseOrderStatusAsync(
                    new PurchaseOrderStatusActionRequest(poGamma.Id, "Approve", "Approve PO Gamma", approverUserId)),
                "S3 approve PO Gamma");

            ReceivingReportModel rrGamma = RequireSuccess(
                await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                    poGamma.Id, mainWarehouseId, "DR-PACK-001", "INV-PACK-2026-001", null, new DateOnly(2026, 11, 10),
                    true, null, null, "rr-req-gamma", "Full cup delivery",
                    new[]
                    {
                        new CreateReceivingReportLineInput(
                            poGamma.Lines.Single().Id, 20_000, 20_000, 0, null, null, "LOT-CUP-1", null)
                    },
                    receiverUserId)),
                "S3 receive PO Gamma");

            SupplierInvoiceModel invoiceWithVariance = RequireSuccess(
                await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                    supplierTwo.Id,
                    "INV-PACK-2026-001",
                    poGamma.Id,
                    rrGamma.Id,
                    new DateOnly(2026, 11, 10),
                    new DateOnly(2026, 11, 25),
                    0,
                    0,
                    true,
                    new[]
                    {
                        new CreateSupplierInvoiceLineInput(
                            poGamma.Lines.Single().Id,
                            rrGamma.Lines.Single().Id,
                            productGammaId,
                            "Gamma Takeout Cup Pack",
                            20_000,
                            8_500, // 500 cents higher than PO unit cost of 8_000
                            0)
                    },
                    apClerkUserId)),
                "S3 create supplier invoice with unit price discrepancy");

            Require(invoiceWithVariance.MatchStatus == "PriceVariance" && invoiceWithVariance.Status == "VarianceFlagged",
                $"S3 3-way match must flag PriceVariance and hold invoice in DiscrepancyReview (actual match={invoiceWithVariance.MatchStatus}, status={invoiceWithVariance.Status}).");

            // Approve variance and post to AP
            invoiceWithVariance = RequireSuccess(
                await service.ApproveInvoiceVarianceAsync(new ApproveInvoiceVarianceRequest(
                    invoiceWithVariance.Id, "Approved 5.00/pack freight surcharge per addendum", true, financeManagerUserId)),
                "S3 approve invoice variance");
            Require(invoiceWithVariance.Status == "PostedOpen" && invoiceWithVariance.MatchStatus == "VarianceApproved",
                "S3 variance approval with PostToApImmediately=true must post the supplier invoice and create AP ledger liability.");

            // Duplicate supplier invoice number for same supplier must be blocked
            Result<SupplierInvoiceModel> duplicateInvoiceAttempt = await service.CreateSupplierInvoiceAsync(
                new CreateSupplierInvoiceRequest(
                    supplierTwo.Id, "INV-PACK-2026-001", poGamma.Id, rrGamma.Id,
                    new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 25), 0, 0, true,
                    new[]
                    {
                        new CreateSupplierInvoiceLineInput(
                            poGamma.Lines.Single().Id, rrGamma.Lines.Single().Id, productGammaId, "Gamma Takeout Cup Pack", 20_000, 8_500, 0)
                    },
                    apClerkUserId));
            Require(duplicateInvoiceAttempt.IsFailure && duplicateInvoiceAttempt.ErrorDetail.Code == "Lpi.Payables.DuplicateSupplierInvoice",
                "S3 duplicate supplier invoice number for the same supplier must be rejected.");

            // Exact 3-way match for PO Alpha (6_000 accepted on RR #1 @ 45_000 cents + 3_240 tax = 273_240 cents)
            SupplierInvoiceModel invoiceAlpha = RequireSuccess(
                await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                    supplierOne.Id,
                    "SI-APEX-2026-001",
                    poAlpha.Id,
                    rrOne.Id,
                    new DateOnly(2026, 11, 10),
                    new DateOnly(2026, 12, 10),
                    0,
                    0,
                    true,
                    new[]
                    {
                        new CreateSupplierInvoiceLineInput(
                            poAlphaLineId,
                            rrOne.Lines.Single().Id,
                            productAlphaId,
                            "Alpha Coffee Beans 1kg",
                            6_000,
                            45_000,
                            3_240)
                    },
                    apClerkUserId)),
                "S3 create exact 3-way matched supplier invoice for PO Alpha RR #1");

            Require(invoiceAlpha.MatchStatus == "Matched" && invoiceAlpha.Status == "PostedOpen" && invoiceAlpha.RemainingBalanceCents == 273_240,
                $"S3 exact 3-way matched invoice must post immediately with balance 273_240 (actual match={invoiceAlpha.MatchStatus}, status={invoiceAlpha.Status}, balance={invoiceAlpha.RemainingBalanceCents}).");

            // Scenario 8: Post-acceptance Supplier Return (SRET) decreasing sellable stock and crediting AP balance
            // Return 1 bag (1_000) of Alpha Coffee Beans (cost 45_000 cents) linked to invoiceAlpha
            SupplierReturnModel supplierReturn = RequireSuccess(
                await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                    rrOne.Id,
                    invoiceAlpha.Id,
                    "Hidden moisture defect discovered upon opening 1 bag",
                    new[]
                    {
                        new CreateSupplierReturnLineInput(
                            rrOne.Lines.Single().Id,
                            1_000,
                            "Moisture defect")
                    },
                    receiverUserId,
                    approverUserId)),
                "S2/S3 post-acceptance supplier return");

            Require(supplierReturn.TotalReturnAmountCents == 45_000 &&
                    ReadStockOnHand(paths.DatabasePath, productAlphaId) == 29_000,
                "S2/S3 post-acceptance supplier return must deduct 1_000 from sellable stock_on_hand_x1000 (30_000 -> 29_000).");

            invoiceAlpha = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoiceAlpha.Id), "S3 reload invoice Alpha after return");
            Require(invoiceAlpha.RemainingBalanceCents == 228_240,
                $"S3 supplier return credit must reduce invoice Alpha balance from 273_240 to 228_240 (actual: {invoiceAlpha.RemainingBalanceCents}).");

            // Scenario 7: Partial payment followed by full settlement + W4 cash drawer non-interference proof
            HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
            CashDrawerReconciliationService drawer = new(composition);
            var shiftOpened = await host.ShiftService.OpenShiftAsync(requesterUserId, 15_000);
            Require(shiftOpened.Ok, "W4 cashier shift must open alongside LPI operations.");

            // Partial payment: 100_000 cents via BankTransfer
            SupplierPaymentModel paymentOne = RequireSuccess(
                await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                    invoiceAlpha.Id,
                    new DateOnly(2026, 11, 12),
                    100_000,
                    "BankTransfer",
                    "OperatingBank",
                    "BPI-TRF-9001",
                    null,
                    "BPI Corporate",
                    "Partial settlement #1",
                    "lpi-pay-req-1",
                    apClerkUserId,
                    financeManagerUserId)),
                "S3 partial supplier payment");

            // Idempotent replay check
            SupplierPaymentModel paymentOneReplay = RequireSuccess(
                await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                    invoiceAlpha.Id,
                    new DateOnly(2026, 11, 12),
                    100_000,
                    "BankTransfer",
                    "OperatingBank",
                    "BPI-TRF-9001",
                    null,
                    "BPI Corporate",
                    "Partial settlement #1",
                    "lpi-pay-req-1",
                    apClerkUserId,
                    financeManagerUserId)),
                "S3 idempotent replay of partial supplier payment");
            Require(paymentOne.Id == paymentOneReplay.Id, "S3 duplicate payment client_request_id must return original payment record.");

            invoiceAlpha = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoiceAlpha.Id), "S3 reload invoice Alpha after partial payment");
            Require(invoiceAlpha.Status == "PartiallyPaid" && invoiceAlpha.RemainingBalanceCents == 128_240,
                $"S3 partial payment must leave 128_240 balance in PartiallyPaid status (actual status={invoiceAlpha.Status}, balance={invoiceAlpha.RemainingBalanceCents}).");

            // Overpayment guard check
            Require((await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                invoiceAlpha.Id, new DateOnly(2026, 11, 13), 200_000, "BankTransfer", "OperatingBank", "OVERPAY-REF",
                null, "BPI", "Overpayment attempt", "lpi-pay-over", apClerkUserId, financeManagerUserId))).IsFailure,
                "S3 payment exceeding remaining invoice balance must be rejected.");

            // Full final payment: remaining 128_240 cents
            SupplierPaymentModel paymentTwo = RequireSuccess(
                await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                    invoiceAlpha.Id,
                    new DateOnly(2026, 11, 14),
                    128_240,
                    "Check",
                    "CheckClearingAccount",
                    "CHK-004412",
                    "004412",
                    "Metrobank",
                    "Final settlement for SI-APEX-2026-001",
                    "lpi-pay-req-2",
                    apClerkUserId,
                    financeManagerUserId)),
                "S3 final supplier payment");
            Require(paymentTwo.AmountCents == 128_240, "S3 final payment must record exact settlement amount.");

            invoiceAlpha = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoiceAlpha.Id), "S3 reload invoice Alpha after full payment");
            Require(invoiceAlpha.Status == "Paid" && invoiceAlpha.RemainingBalanceCents == 0,
                "S3 fully settled invoice must have Status=Paid and RemainingBalanceCents=0.");

            // Verify W4 Cash Drawer expected cash is completely untouched by AP supplier disbursements
            var drawerClose = drawer.Close(requesterUserId, new(15_000, true, "lpi-w4-isolation-close", "Close shift after AP disbursements"));
            Require(drawerClose.IsSuccess && drawerClose.Value.ExpectedCashCents == 15_000 && drawerClose.Value.VarianceCents == 0,
                "S3 AP supplier disbursements must never mutate W4 POS cashier drawer expected cash.");

            // Verify Stock Card & Dashboard Overview
            ProductStockCardModel stockCard = RequireSuccess(
                await service.GetProductStockCardAsync(productAlphaId), "S2/S3 product stock card");
            Require(stockCard.CurrentStockOnHandX1000 == 29_000 && stockCard.Movements.Count >= 3,
                "S2/S3 stock card must reflect RR #1 (+6), RR #2 (+4), and Supplier Return (-1) movements.");

            LpiDashboardOverviewModel overview = RequireSuccess(
                await service.GetDashboardOverviewAsync(), "S3/S4 LPI dashboard overview");
            Require(overview.ActiveSupplierCount >= 2 &&
                    overview.TotalOpenPayablesCents == 170_000 && // invoiceWithVariance for PO Gamma (20 * 8_500 = 170_000) remains open
                    overview.VarianceFlaggedInvoiceCount == 0,
                $"S3/S4 dashboard overview must report accurate KPIs and remaining AP exposure (actual TotalOpenPayablesCents={overview.TotalOpenPayablesCents}).");

            // Verify Backup & Restore preserves all v27 LPI tables
            SqliteBackupRecoveryService backupService = new(paths);
            string backupPath = RequireSuccess(backupService.CreateBackup(), "LPI backup creation");
            var verifiedBackup = RequireSuccess(backupService.VerifyBackup(backupPath), "LPI backup verification");
            Require(verifiedBackup.IsRestorable, "LPI v27 database backup must pass verification.");

            Console.WriteLine(
                "LPI runtime: S0-S3 suppliers, PR split conversion, self-approval denial, PO lifecycle/cancel, partial/final RR, quarantine bucket, count adjustment, supplier return, 3-way match, duplicate invoice guard, partial/full AP payment, W4 isolation, and backup verification PASS.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal static Guid SeedUser(string databasePath, string username, string displayName)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (id,username,display_name,password_hash,role_id,status,created_at)
            VALUES ($id,$username,$display,'not-used',
                    '00000000-0000-0000-0000-000000000001',1,$at);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$display", displayName);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    internal static Guid SeedProduct(
        string databasePath,
        string sku,
        string barcode,
        string name,
        long costPriceCents,
        long sellingPriceCents,
        long initialStockX1000,
        long reorderLevelX1000)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO products
                (id,sku,barcode,name,unit_id,cost_price_cents,selling_price_cents,
                 stock_on_hand_x1000,reorder_level_x1000,track_stock,allow_discount,
                 is_active,created_at)
            VALUES
                ($id,$sku,$barcode,$name,
                 (SELECT id FROM units WHERE is_active=1 ORDER BY name LIMIT 1),
                 $cost,$sell,$stock,$reorder,1,1,1,$at);

            INSERT OR IGNORE INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
            SELECT
                lower(hex(randomblob(16))),
                $id,
                loc.id,
                'Available',
                $stock,
                $at
            FROM inventory_locations loc
            WHERE loc.code = 'MAIN-STORE';
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$sku", sku);
        command.Parameters.AddWithValue("$barcode", barcode);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$cost", costPriceCents);
        command.Parameters.AddWithValue("$sell", sellingPriceCents);
        command.Parameters.AddWithValue("$stock", initialStockX1000);
        command.Parameters.AddWithValue("$reorder", reorderLevelX1000);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static long ReadStockOnHand(string databasePath, Guid productId)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $id;";
        command.Parameters.AddWithValue("$id", productId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed [{result.ErrorDetail.Code}]: {result.ErrorDetail.Message}");
        }

        return result.Value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

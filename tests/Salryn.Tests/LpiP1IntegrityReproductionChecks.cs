using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// Authoritative test suite verifying reproduction, isolation, and regression closure
/// for the six P1 findings:
///   P1-01: 3-Way Matching Integrity (source-free unposted, relationship checks, double-billing prevention)
///   P1-02: Payment Authorization & Settlement Integrity (explicit confirmation, exactly-once AP posting, cash drawer & U1 isolation)
///   P1-03: Financial Data Authorization (Payables.View HTTP matrix & overview metric redaction)
///   P1-04: Transaction Idempotency (exact SHA256 payload binding & conflict rejection)
///   P1-05: Inventory Consistency Across POS (v27 bucket backfill, atomic bucket sync with POS checkout/void/stock-in/adjust)
///   P1-06: Overdelivery Approval Authority (independent supervisory approval event & line tamper prevention)
/// </summary>
public static class LpiP1IntegrityReproductionChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

    private static string P1TransferDateToday() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-lpi-p1-integrity-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            _ = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "LPI Integrity Test Store", null, null, null, null, 80, "admin", "LPI Administrator", "StrongP1Admin123!")), "bootstrap admin");

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);

            Guid requesterId = SeedUser(paths.DatabasePath, "p1-requester", "Procurement Requester");
            Guid supervisorId = SeedUser(paths.DatabasePath, "p1-supervisor", "Warehouse Supervisor");
            Guid receiverId = SeedUser(paths.DatabasePath, "p1-receiver", "Receiving Clerk");
            Guid apClerkId = SeedUser(paths.DatabasePath, "p1-apclerk", "AP Clerk");
            Guid financeMgrId = SeedUser(paths.DatabasePath, "p1-financemgr", "Finance Manager");

            Guid resaleProdId = SeedProduct(paths.DatabasePath, "P1-PROD-RESALE", "9900002000001", "P1 Commercial Resale Item", 50_000, 80_000, 10_000, 2_000);
            Guid nonResaleProdId = SeedProduct(paths.DatabasePath, "P1-PROD-SUPPLY", "9900002000002", "P1 Store Consumable Cleaning Fluid", 20_000, 0, 5_000, 1_000);

            // Execute P1 test suites
            await TestP101ThreeWayMatchingIntegrityAsync(service, paths, apClerkId, resaleProdId);
            await TestP102PaymentAuthorizationSeparationOfDutiesAsync(service, paths, apClerkId, financeMgrId, resaleProdId);
            await TestP103FinancialDataAuthorizationHttpAsync(paths);
            await TestP104TransactionIdempotencyAsync(service, apClerkId, financeMgrId, receiverId, resaleProdId);
            await TestP105InventoryConsistencyAcrossPosAsync(composition, service, paths, receiverId, supervisorId, resaleProdId, nonResaleProdId);
            await TestP106OverdeliveryApprovalAuthorityAsync(service, paths, receiverId, supervisorId, resaleProdId);
            await TestP1R202ForwardMigrationV28Async(paths);
            await TestP1R301FirstStockInDoublesLocationInventoryAsync(composition, service, paths, receiverId, supervisorId);
            await TestP1R302MigrationV28NoStockDuplicationAsync();
            await TestW5R2QuotationPreservationAndContractFallbacksAsync(service, paths, requesterId, supervisorId, resaleProdId);
            await TestW5R21ReconciliationIntegrityAsync(service, paths, requesterId, supervisorId, apClerkId, resaleProdId);
            await TestW5R21DuplicateProductAmbiguityAndFinancialIntegrityAsync(service, paths, requesterId, supervisorId, apClerkId, resaleProdId);
            await TestP102CrossSupplierPaymentLineageHttpAsync(composition, service, paths, resaleProdId);
            await TestP102DisbursementDateCustodyAsync(composition, paths, apClerkId, resaleProdId);
            await TestIssue45And59DuplicateReceivingLineIntegrityAsync();
            await TestIssue44SupplierReturnInvoiceLineageGuardsAsync(composition, service, paths, apClerkId, receiverId, supervisorId, resaleProdId, nonResaleProdId);

            Console.WriteLine("LPI P1 Integrity: All findings (P1-01 through P1-06 and P1-R3-01/02, Issues #44, #45 & #59) verified with full regression closure PASS.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// P1-01: Three-way matching integrity.
    /// Invariants:
    /// - Source-free invoices must receive PendingDocuments status and must NOT post to AP.
    /// - Cross-supplier or mismatched PO/RR links must be rejected.
    /// - Cumulative unbilled quantity cannot exceed accepted delivered quantity (double-billing blocked).
    /// </summary>
    private static async Task TestP101ThreeWayMatchingIntegrityAsync(
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid apClerkId,
        Guid productId)
    {
        LpiSupplier supplierA = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-SUP-A", "Supplier Alpha Ltd", "John", "0917000001", "a@test.com", "Manila", "111-222-333", 30, 5, null, apClerkId)),
            "P1-01 seed supplier A");

        LpiSupplier supplierB = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-SUP-B", "Supplier Beta Ltd", "Jane", "0917000002", "b@test.com", "Cebu", "444-555-666", 30, 5, null, apClerkId)),
            "P1-01 seed supplier B");

        // 1. Source-free invoice (no PO and no RR)
        SupplierInvoiceModel sourceFreeInvoice = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplierA.Id,
                "INV-SOURCE-FREE-01",
                PurchaseOrderId: null,
                ReceivingReportId: null,
                InvoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
                DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                DiscountCents: 0,
                WithholdingTaxCents: 0,
                PostToApIfMatched: true,
                Lines: new[]
                {
                    new CreateSupplierInvoiceLineInput(null, null, productId, "Unlinked Service Fee", 1_000, 50_000, 0)
                },
                RecordedByUserId: apClerkId)),
            "P1-01 create source-free invoice");

        Require(sourceFreeInvoice.MatchStatus == SupplierInvoiceMatchStatuses.PendingDocuments,
            $"Source-free invoice must receive match status PendingDocuments, got '{sourceFreeInvoice.MatchStatus}'.");
        Require(sourceFreeInvoice.Status == "PendingVerification",
            $"Source-free invoice must remain in PendingVerification status, got '{sourceFreeInvoice.Status}'.");
        Require(sourceFreeInvoice.RemainingBalanceCents == 50_000,
            "Source-free invoice balance must remain intact.");

        // Verify it was NOT posted to AP ledger
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM supplier_payable_entries WHERE supplier_invoice_id = $ref;";
            cmd.Parameters.AddWithValue("$ref", sourceFreeInvoice.Id.ToString("D"));
            long count = Convert.ToInt64(cmd.ExecuteScalar());
            Require(count == 0, "Source-free invoice must NOT post any entries to supplier_payable_entries.");
        }

        // P1-R2-01: Manual-posting bypass rejection:
        // Attempting to manually post the unverified / source-free invoice must fail closed.
        Result<SupplierInvoiceModel> manualPostBypass = await service.PostSupplierInvoiceToApAsync(sourceFreeInvoice.Id, apClerkId);
        Require(manualPostBypass.IsFailure, "Manual posting of source-free invoice must fail.");
        Require(manualPostBypass.ErrorDetail.Code == "Lpi.Payables.UnverifiedPostingForbidden" || manualPostBypass.ErrorDetail.Code == "Lpi.Payables.MissingSourceDocuments",
            $"Manual posting must be rejected with UnverifiedPostingForbidden/MissingSourceDocuments, got '{manualPostBypass.ErrorDetail?.Code}'.");

        // Verify AP ledger remains 0
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM supplier_payable_entries WHERE supplier_invoice_id = $ref;";
            cmd.Parameters.AddWithValue("$ref", sourceFreeInvoice.Id.ToString("D"));
            long count = Convert.ToInt64(cmd.ExecuteScalar());
            Require(count == 0, "Supplier payable entries must remain 0 after rejected manual posting bypass attempt.");
        }

        // 2. Setup genuine PO and RR for Supplier A (10 units ordered and accepted)
        PurchaseOrderModel po = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplierA.Id,
                SourceRequisitionId: null,
                ExpectedDeliveryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                DeliveryLocationId: null,
                PaymentTermsDays: 30,
                ShippingCents: 0,
                TermsAndConditions: "Standard",
                Notes: "P1 three-way matching direct purchase fixture",
                SubmitAndApproveImmediately: true,
                Lines: new[]
                {
                    new CreatePurchaseOrderLineInput(null, productId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 10_000, 50_000, 0, 0, null)
                },
                ActorUserId: apClerkId)),
            "P1-01 create PO");

        ReceivingReportModel rr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                po.Id,
                LocationId: null,
                DeliveryReceiptNo: "DR-P1-01",
                SupplierInvoiceRef: null,
                CarrierOrDriver: "Truck A",
                ReceivedDate: DateOnly.FromDateTime(DateTime.UtcNow),
                PostImmediately: true,
                OverdeliveryApproverUserId: null,
                OverdeliveryApprovalReason: null,
                ClientRequestId: "RR-P1-01-REQ",
                Notes: "P1 duplicate-product direct purchase fixture",
                Lines: new[]
                {
                    new CreateReceivingReportLineInput(po.Lines[0].Id, 10_000, 10_000, 0, null, null, null, null)
                },
                ReceivedByUserId: apClerkId)),
            "P1-01 receive RR");

        // 3. Mismatch relationship test: Invoice for Supplier B referencing PO of Supplier A
        Result<SupplierInvoiceModel> mismatchResult = await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            supplierB.Id, // Supplier B!
            "INV-MISMATCH-01",
            PurchaseOrderId: po.Id, // PO belongs to Supplier A!
            ReceivingReportId: rr.Id,
            InvoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            DiscountCents: 0,
            WithholdingTaxCents: 0,
            PostToApIfMatched: true,
            Lines: new[]
            {
                new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id, productId, "P1 Commercial Resale Item", 10_000, 50_000, 0)
            },
            RecordedByUserId: apClerkId));

        Require(mismatchResult.IsFailure && (mismatchResult.ErrorDetail.Code == "Lpi.Payables.RelationshipMismatch" || mismatchResult.ErrorDetail.Code == "Lpi.Payables.InvalidPo"),
            $"Invoice referencing PO belonging to different supplier must be rejected with InvalidPo/RelationshipMismatch, got '{mismatchResult.ErrorDetail.Code}'.");

        PurchaseOrderModel poOther = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplierA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", "P1 PO/RR mismatch fixture", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 5_000, 50_000, 0, 0, null) },
                apClerkId)), "create other PO");

        Result<SupplierInvoiceModel> poRrMismatch = await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            supplierA.Id, "INV-PO-RR-MISMATCH", poOther.Id, rr.Id, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            0, 0, true,
            new[] { new CreateSupplierInvoiceLineInput(poOther.Lines[0].Id, rr.Lines[0].Id, productId, "P1 Commercial Resale Item", 5_000, 50_000, 0) },
            apClerkId));
        Require(poRrMismatch.IsFailure && (poRrMismatch.ErrorDetail.Code == "Lpi.Payables.PoRrMismatch" || poRrMismatch.ErrorDetail.Code == "Lpi.Payables.RelationshipMismatch"),
            $"Invoice referencing RR from a different PO must be rejected with PoRrMismatch/RelationshipMismatch, got '{poRrMismatch.ErrorDetail.Code}'.");

        // 4. First invoice billing partial quantity (6 units of 10 accepted)
        SupplierInvoiceModel invoicePart1 = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplierA.Id,
                "INV-PART-01",
                PurchaseOrderId: po.Id,
                ReceivingReportId: rr.Id,
                InvoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
                DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                DiscountCents: 0,
                WithholdingTaxCents: 0,
                PostToApIfMatched: true,
                Lines: new[]
                {
                    new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id, productId, "P1 Commercial Resale Item", 6_000, 50_000, 0)
                },
                RecordedByUserId: apClerkId)),
            "P1-01 create partial invoice 1");

        Require(invoicePart1.MatchStatus == SupplierInvoiceMatchStatuses.Matched && invoicePart1.Status == "PostedOpen",
            $"Partial invoice within received qty must be Matched and PostedOpen, got match='{invoicePart1.MatchStatus}', status='{invoicePart1.Status}'.");

        // 5. Second invoice attempting double-billing / overbilling: bills 5 units (6 + 5 = 11 > 10 accepted)
        SupplierInvoiceModel invoiceOverbilled = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplierA.Id,
                "INV-OVERBILL-02",
                PurchaseOrderId: po.Id,
                ReceivingReportId: rr.Id,
                InvoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
                DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                DiscountCents: 0,
                WithholdingTaxCents: 0,
                PostToApIfMatched: true,
                Lines: new[]
                {
                    new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id, productId, "P1 Commercial Resale Item", 5_000, 50_000, 0)
                },
                RecordedByUserId: apClerkId)),
            "P1-01 create overbilled invoice 2");

        Require(invoiceOverbilled.MatchStatus == SupplierInvoiceMatchStatuses.Overbilled || invoiceOverbilled.MatchStatus == SupplierInvoiceMatchStatuses.QtyVariance,
            $"Overbilling against cumulative accepted qty must flag Overbilled or QtyVariance, got '{invoiceOverbilled.MatchStatus}'.");
        Require(invoiceOverbilled.Status == "VarianceFlagged",
            $"Overbilled invoice must NOT automatically post to AP; must be VarianceFlagged, got '{invoiceOverbilled.Status}'.");
    }

    /// <summary>
    /// P1-02: Payment authorization and settlement integrity.
    /// Invariants:
    /// - Recorded payment enters PendingApproval status.
    /// - Recording cannot immediately settle by supplying an approver identity.
    /// - Explicit YES confirmation transitions to Settled and posts to AP; same-actor settlement is legitimate.
    /// - Concurrent settlement cannot overpay or post duplicate AP entries.
    /// - Supplier payments never touch POS cash drawer events or U1 customer receivables.
    /// </summary>
    private static async Task TestP102PaymentAuthorizationSeparationOfDutiesAsync(
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid apClerkId,
        Guid financeMgrId,
        Guid productId)
    {
        LpiSupplier supplier = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-SUP-PAY", "Payment Integrity Supplier", "Elena", "0918000001", "pay@test.com", "Pasig", "777-888-999", 30, 5, null, apClerkId)),
            "P1-02 seed supplier");

        PurchaseOrderModel po = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", "P1 payment integrity fixture", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "P1-PROD-RESALE", "Resale Item", PurchaseClassifications.Resale, "pcs", 4_000, 50_000, 0, 0, null) },
                apClerkId)),
            "P1-02 create PO");

        ReceivingReportModel rr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                po.Id, null, "DR-PAY-01", null, "Carrier", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "RR-PAY-01", null,
                new[] { new CreateReceivingReportLineInput(po.Lines[0].Id, 4_000, 4_000, 0, null, null, null, null) },
                apClerkId)),
            "P1-02 receive RR");

        SupplierInvoiceModel invoice = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplier.Id, "INV-PAY-01", po.Id, rr.Id, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id, productId, "Resale Item", 4_000, 50_000, 0) },
                apClerkId)),
            "P1-02 create invoice");

        Require(invoice.Status == "PostedOpen" && invoice.RemainingBalanceCents == 200_000,
            "Invoice must be PostedOpen with 200_000 balance.");

        Result<SupplierPaymentModel> immediateSettlementBypass = await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
            invoice.Id, DateOnly.FromDateTime(DateTime.UtcNow), 100_000, "BankTransfer", "OperatingBank", "REF-PAY-BYPASS",
            null, "BDO", "Must not settle during recording", "REQ-PAY-P102-BYPASS", apClerkId, financeMgrId));
        Require(immediateSettlementBypass.IsFailure && immediateSettlementBypass.ErrorDetail.Code == "Lpi.Payment.SeparateSettlementRequired",
            "Supplying an approver to the recording service must not bypass explicit settlement confirmation.");

        // 1. Record payment by apClerkId (non-finalized payment workflow)
        SupplierPaymentModel pendingPayment = RequireSuccess(
            await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                invoice.Id,
                DateOnly.FromDateTime(DateTime.UtcNow),
                100_000,
                "BankTransfer",
                "OperatingBank",
                "REF-PAY-01",
                null,
                "BDO",
                "Initial 50% disbursement",
                "REQ-PAY-P102-01",
                RecordedByUserId: apClerkId,
                ApprovedByUserId: null)),
            "P1-02 record supplier payment");

        Require(pendingPayment.Status == SupplierPaymentStatuses.PendingApproval,
            $"Newly recorded payment must enter PendingApproval status, got '{pendingPayment.Status}'.");
        Require(pendingPayment.ApprovedByUserId == null,
            "ApprovedByUserId must be null for non-finalized payment.");

        foreach (string invalidConfirmation in new[] { "", "yes", "YES PLEASE" })
        {
            Result<SupplierPaymentModel> invalid = await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
                pendingPayment.Id, apClerkId, invalidConfirmation, "Invalid confirmation must not settle", ActualDisbursementDate: P1TransferDateToday()));
            Require(invalid.IsFailure && invalid.ErrorDetail.Code == "Lpi.Payment.SettlementConfirmationRequired",
                $"Confirmation '{invalidConfirmation}' must be rejected.");
        }

        // Verify invoice balance has NOT been deducted yet
        SupplierInvoiceModel invoiceAfterRecord = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoice.Id), "reload invoice");
        Require(invoiceAfterRecord.RemainingBalanceCents == 200_000,
            $"Invoice balance must remain 200_000 while payment is pending approval, got {invoiceAfterRecord.RemainingBalanceCents}.");

        // 2. Contract R3: the same authorized actor may confirm actual settlement.
        SupplierPaymentModel approvedPayment = RequireSuccess(
            await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
                pendingPayment.Id,
                ApproverUserId: apClerkId,
                SettlementConfirmation: "YES",
                Remarks: "Actual bank transfer confirmed", ActualDisbursementDate: P1TransferDateToday())),
            "P1-02 self-settle supplier payment");

        Require(approvedPayment.Status == SupplierPaymentStatuses.Settled,
            $"Approved payment status must be Settled, got '{approvedPayment.Status}'.");
        Require(approvedPayment.ApprovedByUserId == apClerkId,
            "ApprovedByUserId must match the actual settlement actor.");

        Result<SupplierPaymentModel> replayResult = await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
            pendingPayment.Id,
            ApproverUserId: financeMgrId,
            SettlementConfirmation: "YES",
            Remarks: "Replay must not post twice", ActualDisbursementDate: P1TransferDateToday()));
        Require(replayResult.IsFailure && replayResult.ErrorDetail.Code == "Lpi.Payment.AlreadyFinalized",
            "Repeated settlement must be rejected without a second AP movement.");

        using (SqliteConnection auditConnection = new($"Data Source={paths.DatabasePath}"))
        {
            auditConnection.Open();
            using SqliteCommand auditCommand = auditConnection.CreateCommand();
            auditCommand.CommandText = """
                SELECT user_id, old_value_json, new_value_json, created_at
                FROM audit_logs
                WHERE entity_type='SupplierPayment'
                  AND entity_id=$paymentId
                  AND action='AP_PAYMENT_CONFIRMED_AND_SETTLED';
                """;
            auditCommand.Parameters.AddWithValue("$paymentId", pendingPayment.Id.ToString("D"));
            using SqliteDataReader auditReader = auditCommand.ExecuteReader();
            Require(auditReader.Read(), "Partial settlement must persist a structured settlement audit event.");
            Require(auditReader.GetString(0) == apClerkId.ToString("D"), "Settlement audit user_id must be the actual settlement actor.");
            using JsonDocument previousAudit = JsonDocument.Parse(auditReader.GetString(1));
            using JsonDocument newAudit = JsonDocument.Parse(auditReader.GetString(2));
            JsonElement previous = previousAudit.RootElement;
            JsonElement current = newAudit.RootElement;
            Require(previous.GetProperty("PaymentStatus").GetString() == "PendingApproval" &&
                    previous.GetProperty("InvoiceStatus").GetString() == "PostedOpen" &&
                    previous.GetProperty("PaidAmountCents").GetInt64() == 0 &&
                    previous.GetProperty("OutstandingBalanceCents").GetInt64() == 200_000,
                "Partial settlement audit must preserve the exact previous payment and invoice accounting state.");
            Require(current.GetProperty("PaymentId").GetString() == pendingPayment.Id.ToString("D") &&
                    current.GetProperty("SupplierId").GetString() == supplier.Id.ToString("D") &&
                    current.GetProperty("SupplierInvoiceId").GetString() == invoice.Id.ToString("D") &&
                    current.GetProperty("SupplierInvoiceReference").GetString() == "INV-PAY-01" &&
                    current.GetProperty("AmountCents").GetInt64() == 100_000 &&
                    current.GetProperty("PaymentMethod").GetString() == "BankTransfer" &&
                    current.GetProperty("DisbursementSource").GetString() == "OperatingBank" &&
                    current.GetProperty("PaymentTransactionReference").GetString() == "REF-PAY-01" &&
                    current.GetProperty("RecordedByUserId").GetString() == apClerkId.ToString("D") &&
                    current.GetProperty("SettlementActorUserId").GetString() == apClerkId.ToString("D") &&
                    current.GetProperty("SameUserRecordedAndSettled").GetBoolean() &&
                    current.GetProperty("OperatorConfirmedActualPayment").GetBoolean() &&
                    current.GetProperty("PreviousInvoiceStatus").GetString() == "PostedOpen" &&
                    current.GetProperty("NewInvoiceStatus").GetString() == "PartiallyPaid" &&
                    current.GetProperty("PreviousPaidAmountCents").GetInt64() == 0 &&
                    current.GetProperty("NewPaidAmountCents").GetInt64() == 100_000 &&
                    current.GetProperty("PreviousOutstandingBalanceCents").GetInt64() == 200_000 &&
                    current.GetProperty("NewOutstandingBalanceCents").GetInt64() == 100_000 &&
                    current.GetProperty("AppliedApLedgerAmountCents").GetInt64() == 100_000 &&
                    current.GetProperty("ApLedgerReference").GetString() == pendingPayment.PaymentNumber &&
                    current.GetProperty("SettlementTimestamp").GetString() == auditReader.GetString(3),
                "Partial settlement audit must preserve complete transaction identity, custody, and before/after accounting values.");
            Require(!auditReader.Read(), "Settlement replay must not append a second audit event.");
        }

        // Verify invoice balance is now deducted
        SupplierInvoiceModel invoiceAfterApprove = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoice.Id), "reload invoice after approval");
        Require(invoiceAfterApprove.RemainingBalanceCents == 100_000 && invoiceAfterApprove.Status == "PartiallyPaid",
            $"Invoice balance must be deducted to 100_000 and status PartiallyPaid, got balance={invoiceAfterApprove.RemainingBalanceCents}, status={invoiceAfterApprove.Status}.");

        SupplierPaymentModel competingA = RequireSuccess(await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
            invoice.Id, DateOnly.FromDateTime(DateTime.UtcNow), 100_000, "BankTransfer", "OperatingBank", "REF-PAY-RACE-A",
            null, "BDO", "Concurrent settlement A", "REQ-PAY-P102-RACE-A", apClerkId, null)), "record competing payment A");
        SupplierPaymentModel competingB = RequireSuccess(await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
            invoice.Id, DateOnly.FromDateTime(DateTime.UtcNow), 100_000, "BankTransfer", "OperatingBank", "REF-PAY-RACE-B",
            null, "BDO", "Concurrent settlement B", "REQ-PAY-P102-RACE-B", financeMgrId, null)), "record competing payment B");

        Task<Result<SupplierPaymentModel>> settleA = service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
            competingA.Id, apClerkId, "YES", "Actual transfer A confirmed", ActualDisbursementDate: P1TransferDateToday()));
        Task<Result<SupplierPaymentModel>> settleB = service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
            competingB.Id, financeMgrId, "YES", "Actual transfer B confirmed", ActualDisbursementDate: P1TransferDateToday()));
        Result<SupplierPaymentModel>[] concurrentResults = await Task.WhenAll(settleA, settleB);
        Require(concurrentResults.Count(result => result.IsSuccess) == 1 && concurrentResults.Count(result => result.IsFailure) == 1,
            "Two simultaneous payments against one remaining liability must produce exactly one settlement.");

        SupplierInvoiceModel fullyPaid = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoice.Id), "reload invoice after concurrent settlement");
        Require(fullyPaid.RemainingBalanceCents == 0 && fullyPaid.Status == "Paid",
            "Concurrent settlement must leave the invoice exactly Paid with no negative balance.");
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM supplier_payable_entries WHERE supplier_invoice_id=$invoice AND entry_type='PaymentApplied';";
            cmd.Parameters.AddWithValue("$invoice", invoice.Id.ToString("D"));
            Require(Convert.ToInt64(cmd.ExecuteScalar()) == 2, "Initial and winning concurrent settlement must create exactly two AP payment entries.");
            cmd.CommandText = "SELECT COALESCE(SUM(debit_cents),0) FROM supplier_payable_entries WHERE supplier_invoice_id=$invoice AND entry_type='PaymentApplied';";
            Require(Convert.ToInt64(cmd.ExecuteScalar()) == 200_000, "AP payment debits must total the exact invoice liability once.");
            cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type='SupplierPayment' AND action='AP_PAYMENT_CONFIRMED_AND_SETTLED';";
            Require(Convert.ToInt64(cmd.ExecuteScalar()) >= 2, "Each successful settlement must retain its authenticated audit event.");
        }

        // 4. Verify POS cashier drawer balances and U1 customer receivables are completely untouched
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM cash_drawer_events;";
            long drawerCount = Convert.ToInt64(cmd.ExecuteScalar());
            Require(drawerCount == 0, $"Supplier payments must NOT write any cash_drawer_events (actual={drawerCount}).");

            cmd.CommandText = "SELECT COUNT(*) FROM accounts_receivable_entries;";
            long custAccounts = Convert.ToInt64(cmd.ExecuteScalar());
            Require(custAccounts == 0, "Supplier payments must NOT mutate customer accounts receivable entries.");
        }
    }


    /// <summary>
    /// Issue #70: an intentionally corrupted, disposable payment supplier must never settle
    /// a different supplier's invoice. The same 372,000-cent payment succeeds after the
    /// fixture is restored, proving the fail-closed guard does not block legitimate custody.
    /// This is a persisted-row corruption test, NOT an ordinary-API privilege exploit.
    /// </summary>
    private static async Task TestP102CrossSupplierPaymentLineageHttpAsync(
        SalrynLocalComposition composition,
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid productId)
    {
        Require(paths.DatabasePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            "Issue #70 may run ONLY against a nonce-disposable temporary SQLite database.");

        Guid actorId = SeedUser(paths.DatabasePath, "p1-issue70-manager", "Issue70 Manager");
        using (SqliteConnection identityConnection = new($"Data Source={paths.DatabasePath}"))
        {
            identityConnection.Open();
            using SqliteCommand roleUpdate = identityConnection.CreateCommand();
            roleUpdate.CommandText = "UPDATE users SET role_id = $roleId WHERE id = $actorId;";
            roleUpdate.Parameters.AddWithValue("$roleId", Salryn.Infrastructure.Data.Seeding.DefaultSeedData.ManagerRoleId);
            roleUpdate.Parameters.AddWithValue("$actorId", actorId.ToString("D"));
            Require(roleUpdate.ExecuteNonQuery() == 1,
                "Issue70 authenticated Manager identity must have a persisted Manager role.");
        }

        LpiSupplier supplierA = RequireSuccess(await service.CreateSupplierAsync(
            new CreateLpiSupplierRequest("P1-I70-A", "Issue 70 Supplier A", "A", "0918000070", "issue70a@test.local",
                "Disposable A", null, 30, 5, null, actorId)), "Issue70 supplier A");
        LpiSupplier supplierB = RequireSuccess(await service.CreateSupplierAsync(
            new CreateLpiSupplierRequest("P1-I70-B", "Issue 70 Supplier B", "B", "0918000071", "issue70b@test.local",
                "Disposable B", null, 30, 5, null, actorId)), "Issue70 supplier B");

        // Four units at PHP 930.00 each: PHP 3,720.00 / 372,000 integer cents.
        PurchaseOrderModel po = RequireSuccess(await service.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(
                supplierA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30,
                0, "Terms", "Issue70 disposable lineage regression", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "P1-I70-ITEM",
                    "Issue70 regression item", PurchaseClassifications.Resale, "pcs",
                    4_000, 93_000, 0, 0, null) }, actorId)), "Issue70 PO");
        ReceivingReportModel rr = RequireSuccess(await service.CreateReceivingReportAsync(
            new CreateReceivingReportRequest(
                po.Id, null, "DR-I70-01", null, "Issue70 Carrier", DateOnly.FromDateTime(DateTime.UtcNow),
                true, null, null, "RR-I70-01", null,
                new[] { new CreateReceivingReportLineInput(po.Lines[0].Id,
                    4_000, 4_000, 0, null, null, null, null) }, actorId)), "Issue70 RR");
        SupplierInvoiceModel invoice = RequireSuccess(await service.CreateSupplierInvoiceAsync(
            new CreateSupplierInvoiceRequest(
                supplierA.Id, "INV-I70-3720", po.Id, rr.Id, DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id,
                    productId, "Issue70 regression item", 4_000, 93_000, 0) }, actorId)),
            "Issue70 invoice");
        Require(invoice.Status == "PostedOpen" && invoice.PaidAmountCents == 0 &&
                invoice.RemainingBalanceCents == 372_000 && invoice.SupplierId == supplierA.Id,
            "Issue70 genuine Supplier A invoice must be open for exactly 372,000 cents.");

        SupplierPaymentModel payment = RequireSuccess(await service.RecordSupplierPaymentAsync(
            new RecordSupplierPaymentRequest(invoice.Id, DateOnly.FromDateTime(DateTime.UtcNow),
                372_000, "BankTransfer", "OperatingBank", "TXN-I70-3720", null, "Test Bank",
                "Disposable matching-supplier payment", "REQ-PAY-I70-01", actorId, null)),
            "Issue70 pending payment");
        Require(payment.SupplierId == supplierA.Id && payment.Status == "PendingApproval" &&
                payment.ApprovedByUserId is null && payment.ApprovedAt is null,
            "Issue70 recording must begin as a valid, unsettled Supplier A payment.");

        // The ONLY intentionally inconsistent write is against this temporary test DB.
        // No product migration, historical-data repair, or customer account is involved.
        void ChangeDisposablePaymentSupplier(Guid fromSupplier, Guid toSupplier)
        {
            using SqliteConnection connection = new($"Data Source={paths.DatabasePath}");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE supplier_payments
                SET supplier_id = $toSupplier
                WHERE id = $paymentId
                  AND supplier_id = $fromSupplier
                  AND supplier_invoice_id = $invoiceId
                  AND status = 'PendingApproval';
                """;
            command.Parameters.AddWithValue("$toSupplier", toSupplier.ToString("D"));
            command.Parameters.AddWithValue("$fromSupplier", fromSupplier.ToString("D"));
            command.Parameters.AddWithValue("$paymentId", payment.Id.ToString("D"));
            command.Parameters.AddWithValue("$invoiceId", invoice.Id.ToString("D"));
            Require(command.ExecuteNonQuery() == 1, "Issue70 fixture supplier mutation must affect exactly one pending row.");
        }

        string Snapshot()
        {
            using SqliteConnection connection = new($"Data Source={paths.DatabasePath}");
            connection.Open();
            string[] row;
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT p.status, p.supplier_id, p.supplier_invoice_id, p.amount_cents,
                           p.approved_by_user_id, p.approved_at, p.remarks,
                           i.supplier_id, i.status, i.paid_amount_cents, i.remaining_balance_cents,
                           p.actual_disbursement_date
                    FROM supplier_payments p
                    JOIN supplier_invoices i ON i.id = p.supplier_invoice_id
                    WHERE p.id = $paymentId;
                    """;
                command.Parameters.AddWithValue("$paymentId", payment.Id.ToString("D"));
                using SqliteDataReader reader = command.ExecuteReader();
                Require(reader.Read(), "Issue70 snapshot must find linked payment and invoice.");
                row = Enumerable.Range(0, reader.FieldCount)
                    .Select(index => reader.IsDBNull(index) ? "<NULL>" : reader.GetValue(index).ToString() ?? "")
                    .ToArray();
                Require(!reader.Read(), "Issue70 snapshot must select exactly one linked row.");
            }

            long Scalar(string sql, Guid? id = null)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                if (id.HasValue) command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
                return Convert.ToInt64(command.ExecuteScalar());
            }

            return JsonSerializer.Serialize(new
            {
                paymentAndInvoice = row,
                ledgerEntryCount = Scalar("SELECT COUNT(*) FROM supplier_payable_entries WHERE supplier_invoice_id=$id", invoice.Id),
                ledgerDebitsCents = Scalar("SELECT COALESCE(SUM(debit_cents),0) FROM supplier_payable_entries WHERE supplier_invoice_id=$id", invoice.Id),
                ledgerCreditsCents = Scalar("SELECT COALESCE(SUM(credit_cents),0) FROM supplier_payable_entries WHERE supplier_invoice_id=$id", invoice.Id),
                paymentAppliedCount = Scalar("SELECT COUNT(*) FROM supplier_payable_entries WHERE supplier_invoice_id=$id AND entry_type='PaymentApplied'", invoice.Id),
                settlementAuditCount = Scalar("SELECT COUNT(*) FROM audit_logs WHERE entity_type='SupplierPayment' AND entity_id=$id AND action='AP_PAYMENT_CONFIRMED_AND_SETTLED'", payment.Id),
                paymentAuditCount = Scalar("SELECT COUNT(*) FROM audit_logs WHERE entity_type='SupplierPayment' AND entity_id=$id", payment.Id),
                cashDrawerEventCount = Scalar("SELECT COUNT(*) FROM cash_drawer_events")
            });
        }

        static async Task<long> DisbursedFromHttpAsync(HttpClient client)
        {
            using HttpResponseMessage result = await client.GetAsync("/api/lpi/overview");
            Require(result.StatusCode == HttpStatusCode.OK,
                $"Issue70 finance overview HTTP must be 200; got {(int)result.StatusCode}.");
            using JsonDocument json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("totalPaymentsDisbursedCents").GetInt64();
        }

        static async Task<HttpResponseMessage> SettleThroughHttpAsync(HttpClient client, Guid paymentId)
        {
            return await client.PostAsync($"/api/lpi/payments/{paymentId:D}/approve",
                new StringContent(JsonSerializer.Serialize(new { settlementConfirmation = "YES", actualDisbursementDate = P1TransferDateToday(), remarks = "Issue70 actual transfer verified by fixture operator" }),
                    System.Text.Encoding.UTF8, "application/json"));
        }

        // Register a real authenticated Manager session, not a caller-provided approver ID.
        InMemoryLocalApiSessionStore store = new();
        LocalApiSession manager = store.Create(new AuthenticatedUserReadModel(
            actorId, "issue70-manager", "Issue70 Manager", "Manager", UserStatus.Active,
            new[] { SalrynPermissions.PayablesView, SalrynPermissions.PayablesApprovePayment })
        {
            RoleId = Guid.Parse(Salryn.Infrastructure.Data.Seeding.DefaultSeedData.ManagerRoleId)
        });

        string nonce = "ISSUE70-DISPOSABLE-" + Guid.NewGuid().ToString("N");
        string markerPath = Path.Combine(Path.GetDirectoryName(paths.DatabasePath)!, "issue70-disposable-host-marker.json");
        File.WriteAllText(markerPath, JsonSerializer.Serialize(new
        {
            token = nonce,
            processId = Environment.ProcessId,
            databasePath = paths.DatabasePath,
            isDisposableTest = true
        }));
        using (JsonDocument marker = JsonDocument.Parse(File.ReadAllText(markerPath)))
        {
            Require(marker.RootElement.GetProperty("token").GetString() == nonce &&
                    marker.RootElement.GetProperty("processId").GetInt32() == Environment.ProcessId &&
                    marker.RootElement.GetProperty("databasePath").GetString() == paths.DatabasePath &&
                    marker.RootElement.GetProperty("isDisposableTest").GetBoolean(),
                "Issue70 nonce/PID/SQLite marker custody failed.");
        }

        ChangeDisposablePaymentSupplier(supplierA.Id, supplierB.Id);
        string before = Snapshot();
        using (JsonDocument snapshot = JsonDocument.Parse(before))
        {
            string?[] row = snapshot.RootElement.GetProperty("paymentAndInvoice")
                .EnumerateArray().Select(element => element.GetString()).ToArray();
            Require(row[0] == "PendingApproval" &&
                    row[1] == supplierB.Id.ToString("D") &&
                    row[2] == invoice.Id.ToString("D") &&
                    row[3] == "372000" && row[4] == "<NULL>" && row[5] == "<NULL>" &&
                    row[7] == supplierA.Id.ToString("D") && row[8] == "PostedOpen" &&
                    row[9] == "0" && row[10] == "372000" && row[11] == "<NULL>" &&
                    snapshot.RootElement.GetProperty("paymentAppliedCount").GetInt64() == 0 &&
                    snapshot.RootElement.GetProperty("settlementAuditCount").GetInt64() == 0,
                "Issue70 precondition must isolate the intentionally mismatched unpaid PHP 3,720 payment.");
        }

        long disbursedBeforeRejection = -1;
        await using (WebApplication host = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                SessionStore = store,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false,
                TestHostNonce = nonce
            }).ConfigureAwait(false))
        {
            await host.StartAsync().ConfigureAwait(false);
            string bound = host.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            Require(!bound.EndsWith(":0", StringComparison.Ordinal),
                "Issue70 mutating HTTP must target a dynamically allocated loopback port.");
            using HttpClient client = CreateClient(new Uri(bound), manager);
            long disbursedBefore = await DisbursedFromHttpAsync(client);
            disbursedBeforeRejection = disbursedBefore;
            using (HttpResponseMessage denied = await SettleThroughHttpAsync(client, payment.Id))
            {
                string responseBody = await denied.Content.ReadAsStringAsync();
                Require(denied.StatusCode == HttpStatusCode.Conflict,
                    $"Issue70 mismatched supplier must return HTTP 409, got {(int)denied.StatusCode}: {responseBody}");
                using JsonDocument error = JsonDocument.Parse(responseBody);
                Require(error.RootElement.GetProperty("code").GetString() == "Lpi.Payment.SupplierInvoiceMismatch" &&
                        !string.IsNullOrWhiteSpace(error.RootElement.GetProperty("error").GetString()),
                    $"Issue70 mismatch must return the exact stable domain code and explanation: {responseBody}");
                Console.WriteLine($"[Issue70 NEGATIVE HTTP] status=409 body={responseBody}");
            }
            Require(Snapshot() == before,
                "Issue70 mismatched settlement must preserve payment, invoice, ledger, audit and cash drawer EXACTLY.");
            Require(await DisbursedFromHttpAsync(client) == disbursedBefore,
                "Issue70 mismatched settlement must not increase dashboard disbursements.");
            Console.WriteLine($"[Issue70 NEGATIVE SQLITE BEFORE/AFTER] {before}; disbursedCents={disbursedBefore}");
            await host.StopAsync().ConfigureAwait(false);
        }

        // Host restart uses the SAME disposable database and the same nonce-verified session.
        SalrynLocalComposition restartedComposition = SalrynLocalComposition.Create(paths);
        await using (WebApplication restartedHost = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = restartedComposition,
                SessionStore = store,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false,
                TestHostNonce = nonce
            }).ConfigureAwait(false))
        {
            await restartedHost.StartAsync().ConfigureAwait(false);
            string restartedUrl = restartedHost.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            using HttpClient client = CreateClient(new Uri(restartedUrl), manager);
            Require(Snapshot() == before,
                "Issue70 failed settlement must persist unchanged through a fresh Local API host restart.");
            long disbursedAfterRestart = await DisbursedFromHttpAsync(client);
            Require(disbursedAfterRestart == disbursedBeforeRejection,
                "Issue70 rejected settlement must keep dashboard disbursement unchanged after restart.");
            Console.WriteLine($"[Issue70 RESTART] unchanged={Snapshot() == before}; disbursedCents={disbursedAfterRestart}");

            // Positive control: restore only the intentionally corrupted TEST fixture row,
            // then settle the original genuine Supplier A PHP 3,720 payment over real HTTP.
            ChangeDisposablePaymentSupplier(supplierB.Id, supplierA.Id);
            using (HttpResponseMessage accepted = await SettleThroughHttpAsync(client, payment.Id))
            {
                string responseBody = await accepted.Content.ReadAsStringAsync();
                Require(accepted.StatusCode == HttpStatusCode.OK,
                    $"Issue70 matching-supplier settlement must succeed with HTTP 200: {responseBody}");
                using JsonDocument result = JsonDocument.Parse(responseBody);
                Require(result.RootElement.GetProperty("status").GetString() == "Settled" &&
                        result.RootElement.GetProperty("approvedByUserId").GetGuid() == actorId,
                    "Issue70 legitimate settlement must attribute the actual authenticated Manager.");
                Console.WriteLine($"[Issue70 POSITIVE HTTP] status=200 body={responseBody}");
            }

            string settledSnapshot = Snapshot();
            using (JsonDocument snapshot = JsonDocument.Parse(settledSnapshot))
            {
                string?[] row = snapshot.RootElement.GetProperty("paymentAndInvoice")
                    .EnumerateArray().Select(element => element.GetString()).ToArray();
                using JsonDocument originalSnapshot = JsonDocument.Parse(before);
                Require(row[0] == "Settled" &&
                        row[1] == supplierA.Id.ToString("D") &&
                        row[4] == actorId.ToString("D") && row[5] != "<NULL>" &&
                        row[8] == "Paid" && row[9] == "372000" && row[10] == "0" &&
                        row[11] == P1TransferDateToday() &&
                        snapshot.RootElement.GetProperty("paymentAppliedCount").GetInt64() == 1 &&
                        snapshot.RootElement.GetProperty("ledgerDebitsCents").GetInt64() == 372_000 &&
                        snapshot.RootElement.GetProperty("settlementAuditCount").GetInt64() == 1 &&
                        snapshot.RootElement.GetProperty("cashDrawerEventCount").GetInt64() ==
                            originalSnapshot.RootElement.GetProperty("cashDrawerEventCount").GetInt64(),
                    "Issue70 legitimate PHP 3,720 settlement must post once with true Manager custody.");
            }
            using (HttpResponseMessage replay = await SettleThroughHttpAsync(client, payment.Id))
            {
                Require(replay.StatusCode == HttpStatusCode.Conflict,
                    $"Issue70 settled-payment replay must be HTTP 409, got {(int)replay.StatusCode}.");
                using JsonDocument error = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
                Require(error.RootElement.GetProperty("code").GetString() == "Lpi.Payment.AlreadyFinalized",
                    "Issue70 settled-payment replay must return AlreadyFinalized.");
            }
            Require(Snapshot() == settledSnapshot,
                "Issue70 replay must not create a second ledger or audit entry.");
            Require(await DisbursedFromHttpAsync(client) == checked(disbursedBeforeRejection + 372_000),
                "Issue70 valid full settlement must increase disbursed total exactly once by 372,000 cents.");
            Console.WriteLine($"[Issue70 POSITIVE SQLITE] {settledSnapshot}");
            await restartedHost.StopAsync().ConfigureAwait(false);
        }

        Console.WriteLine("Issue70 cross-supplier persisted-lineage HTTP 409 / SQLite no-write / restart / legitimate 372000-cent settlement and replay: PASS.");
    }


    private sealed class Issue71FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    /// <summary>
    /// Issue #71: one disposable invoice, distinct request/transfer/confirmation dates,
    /// UTC+08 midnight boundary, backdating, pending future plan, partial/full payments,
    /// exact negative responses, once-only accounting, and migration's historical NULL.
    /// </summary>
    private static async Task TestP102DisbursementDateCustodyAsync(
        SalrynLocalComposition composition,
        DbPaths paths,
        Guid actorId,
        Guid productId)
    {
        Require(paths.DatabasePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            "Issue71 must use only the GUID-disposable temporary SQLite path.");

        DateTimeOffset recordedInstant = new(2026, 11, 10, 6, 0, 0, TimeSpan.Zero); // Manila Nov 10, 14:00
        DateTimeOffset confirmedInstant = new(2026, 11, 12, 16, 30, 0, TimeSpan.Zero); // Manila Nov 13, 00:30
        SqliteProcureToPayService dayA = new(composition.ConnectionFactory, new Issue71FixedTimeProvider(recordedInstant));
        SqliteProcureToPayService dayC = new(composition.ConnectionFactory, new Issue71FixedTimeProvider(confirmedInstant));

        LpiSupplier supplier = RequireSuccess(await dayA.CreateSupplierAsync(
            new CreateLpiSupplierRequest("P1-I71-SUP", "Issue71 Date Custody Supplier", "Accountant",
                "0918000171", "issue71@test.local", "Disposable Manila", null, 30, 5, null, actorId)), "Issue71 supplier");
        PurchaseOrderModel po = RequireSuccess(await dayA.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(supplier.Id, null, new DateOnly(2026, 11, 20), null,
                30, 0, "Issue71 terms", "Date custody test", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "P1-I71-ITEM",
                    "Supplier item", PurchaseClassifications.Resale, "pcs",
                    4_000, 93_000, 0, 0, null) }, actorId)), "Issue71 PO");
        ReceivingReportModel rr = RequireSuccess(await dayA.CreateReceivingReportAsync(
            new CreateReceivingReportRequest(po.Id, null, "I71-DR", null,
                "Issue71 carrier", new DateOnly(2026, 11, 10), true, null, null,
                "I71-RR", null,
                new[] { new CreateReceivingReportLineInput(po.Lines[0].Id, 4_000, 4_000, 0, null, null, null, null) },
                actorId)), "Issue71 RR");
        SupplierInvoiceModel invoice = RequireSuccess(await dayA.CreateSupplierInvoiceAsync(
            new CreateSupplierInvoiceRequest(supplier.Id, "I71-INV", po.Id, rr.Id,
                new DateOnly(2026, 11, 10), new DateOnly(2026, 12, 10),
                0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id,
                    productId, "Supplier item", 4_000, 93_000, 0) }, actorId)), "Issue71 invoice");
        Require(invoice.Status == "PostedOpen" && invoice.RemainingBalanceCents == 372_000,
            "Issue71 must create an actual PHP 3,720 unpaid supplier invoice.");

        SupplierPaymentModel partial = RequireSuccess(await dayA.RecordSupplierPaymentAsync(
            new RecordSupplierPaymentRequest(invoice.Id, new DateOnly(2026, 11, 10),
                100_000, "BankTransfer", "OperatingBank", "I71-PARTIAL",
                null, "Test Bank", "Prepared Nov 10", "I71-REQ-A", actorId, null)),
            "Issue71 pending partial");
        Require(partial.PaymentDate == new DateOnly(2026, 11, 10) &&
                partial.CreatedAt == recordedInstant && partial.ActualDisbursementDate is null &&
                partial.ApprovedAt is null && partial.Status == SupplierPaymentStatuses.PendingApproval,
            "Issue71 day-A pending record must preserve plan and creation timestamp with no real transfer date.");

        foreach (var (value, code) in new[]
        {
            ((string?)null, "Lpi.Payment.ActualDisbursementDateRequired"),
            ("", "Lpi.Payment.ActualDisbursementDateRequired"),
            ("2026-02-30", "Lpi.Payment.ActualDisbursementDateInvalid"),
            ("2026-11-14", "Lpi.Payment.ActualDisbursementDateFuture")
        })
        {
            Result<SupplierPaymentModel> denied = await dayC.ApproveSupplierPaymentAsync(
                new ApproveSupplierPaymentRequest(partial.Id, actorId, "YES", "Reject-before-write", value));
            Require(denied.IsFailure && denied.ErrorDetail.Code == code,
                $"Issue71 invalid transfer date '{value}' must fail with {code}.");
        }
        Result<SupplierPaymentModel> wrongYes = await dayC.ApproveSupplierPaymentAsync(
            new ApproveSupplierPaymentRequest(partial.Id, actorId, "yes", "No lowercase", "2026-11-12"));
        Require(wrongYes.IsFailure && wrongYes.ErrorDetail.Code == "Lpi.Payment.SettlementConfirmationRequired",
            "Issue71 lowercase confirmation must be denied even with a valid actual date.");

        IReadOnlyList<SupplierPaymentModel> pendingReadback = RequireSuccess(
            await dayC.GetSupplierPaymentsAsync(supplier.Id, invoice.Id), "Issue71 reload pending list");
        SupplierPaymentModel stillPending = pendingReadback.Single(p => p.Id == partial.Id);
        Require(stillPending.Status == "PendingApproval" && stillPending.ActualDisbursementDate is null &&
                stillPending.ApprovedAt is null,
            "Issue71 rejected transfer date/YES requests must leave the payment unchanged.");
        SupplierInvoiceModel unchangedInvoice = RequireSuccess(await dayC.GetSupplierInvoiceByIdAsync(invoice.Id), "Issue71 unpaid invoice");
        Require(unchangedInvoice.PaidAmountCents == 0 && unchangedInvoice.RemainingBalanceCents == 372_000,
            "Issue71 invalid dates must not mutate invoice financial state.");
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM supplier_payable_entries WHERE supplier_invoice_id=$id AND entry_type='PaymentApplied';";
            cmd.Parameters.AddWithValue("$id", invoice.Id.ToString("D"));
            Require(Convert.ToInt64(cmd.ExecuteScalar()) == 0, "Issue71 invalid date must not append AP payment ledger entries.");
            cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type='SupplierPayment' AND entity_id=$id AND action='AP_PAYMENT_CONFIRMED_AND_SETTLED';";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$id", partial.Id.ToString("D"));
            Require(Convert.ToInt64(cmd.ExecuteScalar()) == 0, "Issue71 invalid date must not append settlement audit.");
        }

        SupplierPaymentModel settledPartial = RequireSuccess(await dayC.ApproveSupplierPaymentAsync(
            new ApproveSupplierPaymentRequest(partial.Id, actorId, "YES", "Transferred Nov 12", "2026-11-12")),
            "Issue71 day B partial settlement affirmed on day C");
        Require(settledPartial.ActualDisbursementDate == new DateOnly(2026, 11, 12) &&
                settledPartial.PaymentDate == new DateOnly(2026, 11, 10) &&
                settledPartial.ApprovedAt == confirmedInstant && settledPartial.ApprovedByUserId == actorId,
            "Issue71 requested, actual funds-transfer and confirmation instants must be three independent facts.");
        SupplierInvoiceModel partialInvoice = RequireSuccess(await dayC.GetSupplierInvoiceByIdAsync(invoice.Id), "Issue71 partial invoice");
        Require(partialInvoice.Status == "PartiallyPaid" && partialInvoice.RemainingBalanceCents == 272_000,
            "Issue71 partial settlement must debit AP exactly 100,000 cents.");

        // Requested FUTURE date is valid for a pending plan. Actual date is separately
        // affirmed as Nov 13 Manila (still Nov 12 UTC at 16:30Z).
        SupplierPaymentModel plannedTomorrow = RequireSuccess(await dayC.RecordSupplierPaymentAsync(
            new RecordSupplierPaymentRequest(invoice.Id, new DateOnly(2026, 11, 14),
                272_000, "BankTransfer", "OperatingBank", "I71-FINAL",
                null, "Test Bank", "Future planned date, not evidence of transfer", "I71-REQ-B", actorId, null)),
            "Issue71 planned future payment");
        Require(plannedTomorrow.PaymentDate == new DateOnly(2026, 11, 14) &&
                plannedTomorrow.ActualDisbursementDate is null &&
                plannedTomorrow.Status == "PendingApproval",
            "Issue71 future requested payment date may remain pending but cannot imply disbursement.");
        SupplierPaymentModel settledFinal = RequireSuccess(await dayC.ApproveSupplierPaymentAsync(
            new ApproveSupplierPaymentRequest(plannedTomorrow.Id, actorId, "YES", "Local midnight transfer", "2026-11-13")),
            "Issue71 business-local rollover accepts Nov13 while UTC still Nov12");
        Require(settledFinal.ActualDisbursementDate == new DateOnly(2026, 11, 13) &&
                settledFinal.PaymentDate == new DateOnly(2026, 11, 14) &&
                settledFinal.ApprovedAt == confirmedInstant,
            "Issue71 must distinguish future planned date, local actual date, and UTC confirmation timestamp.");

        Result<SupplierPaymentModel> replay = await dayC.ApproveSupplierPaymentAsync(
            new ApproveSupplierPaymentRequest(partial.Id, actorId, "YES", "Duplicate", "2026-11-12"));
        Require(replay.IsFailure && replay.ErrorDetail.Code == "Lpi.Payment.AlreadyFinalized",
            "Issue71 duplicate settlement must reject without duplicate posting.");
        SupplierInvoiceModel paidInvoice = RequireSuccess(await dayC.GetSupplierInvoiceByIdAsync(invoice.Id), "Issue71 paid invoice");
        Require(paidInvoice.Status == "Paid" && paidInvoice.PaidAmountCents == 372_000 &&
                paidInvoice.RemainingBalanceCents == 0, "Issue71 two settlements must fully reconcile 372,000 cents.");
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(debit_cents),0) FROM supplier_payable_entries WHERE supplier_invoice_id=$id AND entry_type='PaymentApplied';";
            cmd.Parameters.AddWithValue("$id", invoice.Id.ToString("D"));
            using SqliteDataReader reader = cmd.ExecuteReader();
            Require(reader.Read() && reader.GetInt64(0) == 2 && reader.GetInt64(1) == 372_000,
                "Issue71 partial/full/replay must produce exactly two AP postings of 372,000 cents combined.");
            reader.Close();
            cmd.CommandText = "SELECT new_value_json FROM audit_logs WHERE entity_type='SupplierPayment' AND entity_id=$id AND action='AP_PAYMENT_CONFIRMED_AND_SETTLED';";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$id", partial.Id.ToString("D"));
            using SqliteDataReader audit = cmd.ExecuteReader();
            Require(audit.Read(), "Issue71 must persist authenticated settlement audit.");
            using JsonDocument doc = JsonDocument.Parse(audit.GetString(0));
            Require(doc.RootElement.GetProperty("RequestedPaymentDate").GetString() == "2026-11-10" &&
                    doc.RootElement.GetProperty("ActualDisbursementDate").GetString() == "2026-11-12" &&
                    doc.RootElement.GetProperty("SettlementTimestamp").GetDateTimeOffset() == confirmedInstant &&
                    doc.RootElement.GetProperty("SettlementActorUserId").GetString() == actorId.ToString("D"),
                "Issue71 structured audit must preserve three distinct actual dates and responsible operator.");
            Require(!audit.Read(), "Issue71 audit must not duplicate the partial settlement event.");
        }

        // Create fresh composition as restart custody proof; migration runner must be idempotent.
        SalrynLocalComposition restartedComposition = SalrynLocalComposition.Create(paths);
        await restartedComposition.InitializeDatabaseAsync().ConfigureAwait(false);
        SqliteProcureToPayService afterRestart = new(restartedComposition.ConnectionFactory);
        IReadOnlyList<SupplierPaymentModel> persisted = RequireSuccess(
            await afterRestart.GetSupplierPaymentsAsync(supplier.Id, invoice.Id), "Issue71 persisted payments");
        Require(persisted.Count == 2 &&
                persisted.Single(p => p.Id == partial.Id).ActualDisbursementDate == new DateOnly(2026, 11, 12) &&
                persisted.Single(p => p.Id == plannedTomorrow.Id).ActualDisbursementDate == new DateOnly(2026, 11, 13) &&
                persisted.Single(p => p.Id == partial.Id).PaymentDate == new DateOnly(2026, 11, 10) &&
                persisted.Single(p => p.Id == plannedTomorrow.Id).PaymentDate == new DateOnly(2026, 11, 14),
            "Issue71 exact recording and actual dates must survive restart with no inferred backfill.");

        // Forward-only migration on an isolated historical miniature: no inferred transfer date,
        // even if status is Settled and approved_at exists.
        using (SqliteConnection historical = new("Data Source=:memory:"))
        {
            historical.Open();
            using (SqliteCommand setup = historical.CreateCommand())
            {
                setup.CommandText = """
                    CREATE TABLE supplier_payments (id TEXT PRIMARY KEY, payment_date TEXT NOT NULL,
                        status TEXT NOT NULL, approved_at TEXT NULL);
                    INSERT INTO supplier_payments VALUES ('old-settled', '2026-11-10', 'Settled', '2026-11-12T16:30:00Z');
                    INSERT INTO supplier_payments VALUES ('old-pending', '2026-11-14', 'PendingApproval', NULL);
                    """;
                setup.ExecuteNonQuery();
            }
            using (SqliteTransaction txn = historical.BeginTransaction())
            {
                new SupplierPaymentActualDisbursementDateMigration().Up(historical, txn);
                txn.Commit();
            }
            using (SqliteTransaction txn = historical.BeginTransaction())
            {
                new SupplierPaymentActualDisbursementDateMigration().Up(historical, txn);
                txn.Commit();
            }
            using SqliteCommand check = historical.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM supplier_payments WHERE actual_disbursement_date IS NULL;";
            Require(Convert.ToInt64(check.ExecuteScalar()) == 2,
                "Issue71 legacy settled and pending records must retain NULL actual date through migration and replay.");
        }

        Console.WriteLine("[ISSUE71] disposable SQLite dates A=2026-11-10 B=2026-11-12 C=2026-11-13 Manila; UTC rollover, negative dates, partial/full, audit, restart, historical NULL: PASS.");
    }

    /// <summary>
    /// P1-03: Financial data authorization.
    /// Invariants:
    /// - Anonymous receives 401 Unauthorized.
    /// - Users with only Inventory.View or Reports.View receive 403 Forbidden on invoices, payments, aging, ledger.
    /// - Users with Payables.View receive 200 OK.
    /// - Dashboard overview redacts financial AP metrics for users without Payables.View.
    /// </summary>
    private static async Task TestP103FinancialDataAuthorizationHttpAsync(DbPaths paths)
    {
        InMemoryLocalApiSessionStore sessionStore = new();
        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    SessionStore = sessionStore,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = new(app.Urls.First(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)));

            // 1. Anonymous request
            using HttpClient anon = new() { BaseAddress = baseAddress };
            await AssertStatusAsync(anon, HttpMethod.Get, "/api/lpi/invoices", HttpStatusCode.Unauthorized, "Anon GET /api/lpi/invoices");
            await AssertStatusAsync(anon, HttpMethod.Get, "/api/lpi/payments", HttpStatusCode.Unauthorized, "Anon GET /api/lpi/payments");
            await AssertStatusAsync(anon, HttpMethod.Get, "/api/lpi/payables/ledger", HttpStatusCode.Unauthorized, "Anon GET /api/lpi/payables/ledger");
            await AssertStatusAsync(anon, HttpMethod.Get, "/api/lpi/payables/aging", HttpStatusCode.Unauthorized, "Anon GET /api/lpi/payables/aging");

            // 2. Inventory-only operator (Inventory.View, Receiving.View)
            LocalApiSession invSession = CreateSession(sessionStore, "InventoryClerk", new[]
            {
                SalrynPermissions.InventoryView,
                SalrynPermissions.ReceivingView,
                SalrynPermissions.ProcurementView
            });
            using HttpClient invClient = CreateClient(baseAddress, invSession);

            await AssertStatusAsync(invClient, HttpMethod.Get, "/api/lpi/invoices", HttpStatusCode.Forbidden, "InvClerk GET /api/lpi/invoices must be 403");
            await AssertStatusAsync(invClient, HttpMethod.Get, "/api/lpi/payments", HttpStatusCode.Forbidden, "InvClerk GET /api/lpi/payments must be 403");
            await AssertStatusAsync(invClient, HttpMethod.Get, "/api/lpi/payables/ledger", HttpStatusCode.Forbidden, "InvClerk GET /api/lpi/payables/ledger must be 403");
            await AssertStatusAsync(invClient, HttpMethod.Get, "/api/lpi/payables/aging", HttpStatusCode.Forbidden, "InvClerk GET /api/lpi/payables/aging must be 403");

            // Overview redaction test: Inventory clerk CAN view overview, but financial metrics are redacted to 0
            using HttpResponseMessage overviewResp = await invClient.GetAsync("/api/lpi/overview");
            Require(overviewResp.StatusCode == HttpStatusCode.OK, "InvClerk can access /api/lpi/overview.");
            using JsonDocument doc = JsonDocument.Parse(await overviewResp.Content.ReadAsStringAsync());
            long openPayables = doc.RootElement.GetProperty("totalOpenPayablesCents").GetInt64();
            long paymentsDisbursed = doc.RootElement.GetProperty("totalPaymentsDisbursedCents").GetInt64();
            Require(openPayables == 0, $"Overview totalOpenPayablesCents must be redacted to 0 for non-finance user (got {openPayables}).");
            Require(paymentsDisbursed == 0, $"Overview totalPaymentsDisbursedCents must be redacted to 0 for non-finance user (got {paymentsDisbursed}).");

            // 3. Reports-only operator (Reports.View)
            LocalApiSession reportsSession = CreateSession(sessionStore, "ReportsAuditor", new[]
            {
                SalrynPermissions.ReportsView
            });
            using HttpClient reportsClient = CreateClient(baseAddress, reportsSession);

            await AssertStatusAsync(reportsClient, HttpMethod.Get, "/api/lpi/invoices", HttpStatusCode.Forbidden, "Reports GET /api/lpi/invoices must be 403");
            await AssertStatusAsync(reportsClient, HttpMethod.Get, "/api/lpi/payments", HttpStatusCode.Forbidden, "Reports GET /api/lpi/payments must be 403");
            await AssertStatusAsync(reportsClient, HttpMethod.Get, "/api/lpi/payables/aging", HttpStatusCode.Forbidden, "Reports GET /api/lpi/payables/aging must be 403");

            // 4. Authorized finance user (Payables.View)
            LocalApiSession financeSession = CreateSession(sessionStore, "FinanceOfficer", new[]
            {
                SalrynPermissions.PayablesView
            });
            using HttpClient financeClient = CreateClient(baseAddress, financeSession);

            await AssertStatusAsync(financeClient, HttpMethod.Get, "/api/lpi/invoices", HttpStatusCode.OK, "Finance GET /api/lpi/invoices must be 200");
            await AssertStatusAsync(financeClient, HttpMethod.Get, "/api/lpi/payments", HttpStatusCode.OK, "Finance GET /api/lpi/payments must be 200");
            await AssertStatusAsync(financeClient, HttpMethod.Get, "/api/lpi/payables/ledger", HttpStatusCode.OK, "Finance GET /api/lpi/payables/ledger must be 200");
            await AssertStatusAsync(financeClient, HttpMethod.Get, "/api/lpi/payables/aging", HttpStatusCode.OK, "Finance GET /api/lpi/payables/aging must be 200");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// P1-04: Transaction idempotency.
    /// Invariants:
    /// - Client request ID is bound to canonical SHA256 payload hash.
    /// - Replay with identical payload returns original transaction record.
    /// - Replay with altered payload returns deterministic conflict error.
    /// </summary>
    private static async Task TestP104TransactionIdempotencyAsync(
        SqliteProcureToPayService service,
        Guid apClerkId,
        Guid financeMgrId,
        Guid receiverId,
        Guid productId)
    {
        LpiSupplier supplier = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-SUP-IDEM", "Idempotency Supplier Inc", "Mark", "0919000002", "idem@test.com", "Taguig", "888-999-000", 30, 5, null, apClerkId)),
            "P1-04 seed supplier");

        PurchaseOrderModel po = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", "P1 idempotency fixture", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "P1-PROD-RESALE", "Resale Item", PurchaseClassifications.Resale, "pcs", 10_000, 50_000, 0, 0, null) },
                apClerkId)),
            "P1-04 create PO");

        // 1. Receiving Report Idempotency
        const string rrClientId = "CLIENT-RR-IDEM-001";
        CreateReceivingReportRequest rrReqOriginal = new(
            po.Id, null, "DR-IDEM-01", null, "Carrier", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, rrClientId, null,
            new[] { new CreateReceivingReportLineInput(po.Lines[0].Id, 5_000, 5_000, 0, null, null, null, null) },
            receiverId);

        ReceivingReportModel rr1 = RequireSuccess(await service.CreateReceivingReportAsync(rrReqOriginal), "first RR submit");

        // Replay identical RR request
        ReceivingReportModel rrReplay = RequireSuccess(await service.CreateReceivingReportAsync(rrReqOriginal), "replayed identical RR submit");
        Require(rr1.Id == rrReplay.Id, "Idempotent replay with identical payload must return original RR.");

        // Altered payload with same client_request_id (modified quantity to 6_000)
        CreateReceivingReportRequest rrReqAltered = new(
            po.Id, null, "DR-IDEM-01", null, "Carrier", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, rrClientId, null,
            new[] { new CreateReceivingReportLineInput(po.Lines[0].Id, 6_000, 6_000, 0, null, null, null, null) },
            receiverId);

        Result<ReceivingReportModel> rrConflict = await service.CreateReceivingReportAsync(rrReqAltered);
        Require(rrConflict.IsFailure && rrConflict.ErrorDetail.Code == "Lpi.Receiving.IdempotencyConflict",
            $"Altered RR payload with same client_request_id must return Lpi.Receiving.IdempotencyConflict, got '{rrConflict.ErrorDetail.Code}'.");

        // 2. Supplier Payment Idempotency
        SupplierInvoiceModel invoice = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplier.Id, "INV-IDEM-01", po.Id, rr1.Id, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr1.Lines[0].Id, productId, "Resale Item", 5_000, 50_000, 0) },
                apClerkId)),
            "P1-04 create invoice");

        const string payClientId = "CLIENT-PAY-IDEM-001";
        RecordSupplierPaymentRequest payReqOriginal = new(
            invoice.Id, DateOnly.FromDateTime(DateTime.UtcNow), 100_000, "BankTransfer", "OperatingBank", "TXN-001", null, "BDO", "Disbursement 1", payClientId, apClerkId, null);

        SupplierPaymentModel pay1 = RequireSuccess(await service.RecordSupplierPaymentAsync(payReqOriginal), "first payment submit");

        // Replay identical Payment request
        SupplierPaymentModel payReplay = RequireSuccess(await service.RecordSupplierPaymentAsync(payReqOriginal), "replayed identical payment submit");
        Require(pay1.Id == payReplay.Id, "Idempotent replay with identical payload must return original payment.");

        // Altered payload with same client_request_id (modified amount to 150_000)
        RecordSupplierPaymentRequest payReqAltered = new(
            invoice.Id, DateOnly.FromDateTime(DateTime.UtcNow), 150_000, "BankTransfer", "OperatingBank", "TXN-001", null, "BDO", "Disbursement 1", payClientId, apClerkId, null);

        Result<SupplierPaymentModel> payConflict = await service.RecordSupplierPaymentAsync(payReqAltered);
        Require(payConflict.IsFailure && payConflict.ErrorDetail.Code == "Lpi.Payables.IdempotencyConflict",
            $"Altered payment payload with same client_request_id must return Lpi.Payables.IdempotencyConflict, got '{payConflict.ErrorDetail.Code}'.");
    }

    /// <summary>
    /// P1-05: Inventory consistency across the entire POS.
    /// Invariants:
    /// - Migration v27 backfills product_stock_buckets in MAIN-STORE Available for active products.
    /// - Non-resale purchases do NOT increment sellable stock or Available bucket.
    /// - POS checkout, void, stock-in, stock adjustment, physical counting, and supplier returns
    ///   keep products.stock_on_hand_x1000 and product_stock_buckets in MAIN-STORE strictly equal.
    /// </summary>
    private static async Task TestP105InventoryConsistencyAcrossPosAsync(
        SalrynLocalComposition composition,
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid receiverId,
        Guid supervisorId,
        Guid _resaleProdId,
        Guid _nonResaleProdId)
    {
        Guid resaleProdId = SeedProduct(paths.DatabasePath, "P1-05-RESALE", "9900003000001", "P1-05 Resale Item", 50_000, 80_000, 10_000, 2_000);
        Guid nonResaleProdId = SeedProduct(paths.DatabasePath, "P1-05-SUPPLY", "9900003000002", "P1-05 Store Consumable", 20_000, 0, 5_000, 1_000);

        // 1. Verify migration v27 backfill seeded the Available bucket in MAIN-STORE
        long resaleOnHand = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long resaleBucketQty = ReadLocationBucketQty(paths.DatabasePath, resaleProdId, "MAIN-STORE", "Available");
        Require(resaleOnHand == resaleBucketQty && resaleOnHand == 10_000,
            $"Migration v27 backfill must synchronize initial stock in MAIN-STORE Available (onHand={resaleOnHand}, bucket={resaleBucketQty}).");

        // 2. Non-resale purchase classification does NOT touch sellable stock or Available bucket
        LpiSupplier nonResaleSup = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-SUP-NONRESALE", "Store Supplies Co", "Carlo", "0919000003", "supplies@test.com", "Mandaluyong", "999-000-111", 30, 5, null, receiverId)),
            "seed non-resale supplier");

        PurchaseOrderModel nonResalePo = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                nonResaleSup.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", "P1 non-resale receiving fixture", true,
                new[] { new CreatePurchaseOrderLineInput(null, nonResaleProdId, "P1-PROD-SUPPLY", "Consumable Fluid", PurchaseClassifications.Consumable, "bottles", 5_000, 20_000, 0, 0, null) },
                receiverId)),
            "create non-resale PO");

        long nonResaleOnHandBefore = ReadStockOnHand(paths.DatabasePath, nonResaleProdId);
        ReceivingReportModel nonResaleRr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                nonResalePo.Id, null, "DR-SUPPLY-01", null, "Carrier", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "RR-SUPPLY-01", null,
                new[] { new CreateReceivingReportLineInput(nonResalePo.Lines[0].Id, 5_000, 5_000, 0, null, null, null, null) },
                receiverId)),
            "receive non-resale goods");

        long nonResaleOnHandAfter = ReadStockOnHand(paths.DatabasePath, nonResaleProdId);
        Require(nonResaleOnHandBefore == nonResaleOnHandAfter,
            $"Non-resale receipt must NOT increase sellable stock (before={nonResaleOnHandBefore}, after={nonResaleOnHandAfter}).");

        // 3. POS Checkout Sale: Sell 3 units of resale product
        Guid cashierId = SeedUser(paths.DatabasePath, "p1-cashier", "POS Cashier");
        Guid saleId = Guid.NewGuid();
        Quantity soldQty = Quantity.FromX1000(3_000).Value;

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "OR-P1-00001",
            CashierUserId = cashierId,
            Subtotal = Money.FromCents(240_000),
            Total = Money.FromCents(240_000),
            AmountPaid = Money.FromCents(240_000),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "Snapshot"
        };
        SaleItem saleItem = new()
        {
            SaleId = saleId,
            ProductId = resaleProdId,
            ProductNameSnapshot = "Commercial Resale Item",
            BarcodeSnapshot = "9900002000001",
            UnitNameSnapshot = "pcs",
            Quantity = soldQty,
            UnitPrice = Money.FromCents(80_000),
            CostPrice = Money.FromCents(50_000),
            LineTotal = Money.FromCents(240_000)
        };
        Payment payment = new() { SaleId = saleId, Amount = Money.FromCents(240_000) };
        StockMovement movement = new()
        {
            ProductId = resaleProdId,
            MovementType = StockMovementType.Sale,
            QuantityChange = soldQty,
            CostPrice = Money.FromCents(50_000),
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "POS Checkout Sale",
            UserId = cashierId
        };

        Result saleResult = await composition.SaleWriteRepository.AddCompletedSaleAsync(
            sale, new[] { saleItem }, new[] { payment }, new[] { movement });
        Require(saleResult.IsSuccess, "POS checkout sale must succeed.");

        long stockAfterSale = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long bucketAfterSale = ReadLocationBucketQty(paths.DatabasePath, resaleProdId, "MAIN-STORE", "Available");
        Require(stockAfterSale == 7_000 && bucketAfterSale == 7_000,
            $"POS checkout sale must atomically decrease both product stock and MAIN-STORE bucket (stock={stockAfterSale}, bucket={bucketAfterSale}).");

        // 4. POS Sale Void: Void the sale and restore stock
        InMemoryLocalApiSessionStore store = new();
        LocalApiSession cashierSession = CreateSession(store, "Cashier", new[] { SalrynPermissions.SalesVoid }, cashierId);
        SaleVoidApiService voidService = new(composition);

        Result<SaleVoidResponse> voidRes = await voidService.VoidAsync(saleId, new SaleVoidRequest("Customer returned goods at counter"), cashierSession);
        Require(voidRes.IsSuccess, $"Sale void must succeed: [{voidRes.ErrorDetail?.Code}] {voidRes.ErrorDetail?.Message}");

        long stockAfterVoid = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long bucketAfterVoid = ReadLocationBucketQty(paths.DatabasePath, resaleProdId, "MAIN-STORE", "Available");
        Require(stockAfterVoid == 10_000 && bucketAfterVoid == 10_000,
            $"POS sale void must atomically restore both product stock and MAIN-STORE bucket (stock={stockAfterVoid}, bucket={bucketAfterVoid}).");

        // 5. Existing Stock-In: Add 2 units via InventoryManagementApiService
        InventoryManagementApiService invService = new(composition);
        LocalApiSession invSession = CreateSession(store, "Manager", new[] { SalrynPermissions.InventoryAdjust }, cashierId);

        Result<StockWriteResponse> stockInRes = await invService.StockInAsync(new StockInRequest(resaleProdId, 2.0m, "Restock from secondary store", "REF-IN"), invSession);
        Require(stockInRes.IsSuccess, "Legacy Stock-In must succeed.");

        long stockAfterStockIn = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long bucketAfterStockIn = ReadLocationBucketQty(paths.DatabasePath, resaleProdId, "MAIN-STORE", "Available");
        Require(stockAfterStockIn == 12_000 && bucketAfterStockIn == 12_000,
            $"Legacy Stock-In must atomically update both stock and MAIN-STORE bucket (stock={stockAfterStockIn}, bucket={bucketAfterStockIn}).");

        // 6. Existing Stock Adjustment: Remove 1 unit via InventoryManagementApiService
        Result<StockWriteResponse> adjRes = await invService.AdjustAsync(new StockAdjustmentRequest(resaleProdId, "remove", 1.0m, "Damaged in store display", "REF-ADJ"), invSession);
        Require(adjRes.IsSuccess, $"Legacy Stock Adjustment must succeed: [{adjRes.ErrorDetail?.Code}] {adjRes.ErrorDetail?.Message}");

        long stockAfterAdj = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long bucketAfterAdj = ReadLocationBucketQty(paths.DatabasePath, resaleProdId, "MAIN-STORE", "Available");
        Require(stockAfterAdj == 11_000 && bucketAfterAdj == 11_000,
            $"Legacy Stock Adjustment must atomically update both stock and MAIN-STORE bucket (stock={stockAfterAdj}, bucket={bucketAfterAdj}).");

        // 7. LPI Physical Count Adjustment: Count 9.5 units
        LpiFoundationCatalog foundation = RequireSuccess(await service.GetFoundationCatalogAsync(), "foundation");
        Guid mainStoreLocId = foundation.Locations.First(l => l.Code == "MAIN-STORE").Id;

        StockCountAdjustmentModel countAdj = RequireSuccess(
            await service.RecordStockCountAdjustmentAsync(new RecordStockCountAdjustmentRequest(
                resaleProdId, mainStoreLocId, 9_500, "ANNUAL_COUNT", "Cycle count discrepancy reconciliation", receiverId, supervisorId)),
            "LPI physical count adjustment");

        long stockAfterCount = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long bucketAfterCount = ReadLocationBucketQty(paths.DatabasePath, resaleProdId, "MAIN-STORE", "Available");
        Require(stockAfterCount == 9_500 && bucketAfterCount == 9_500,
            $"LPI physical count adjustment must atomically reconcile stock and MAIN-STORE bucket (stock={stockAfterCount}, bucket={bucketAfterCount}).");

        // 8. P1-R2-03 Multi-Location POS stock isolation and no silent clamping:
        // Create product with 0 in MAIN-STORE, receive stock exclusively in WAREHOUSE-1.
        using (SqliteConnection locConn = new($"Data Source={paths.DatabasePath}"))
        {
            locConn.Open();
            using SqliteCommand locCmd = locConn.CreateCommand();
            locCmd.CommandText = "INSERT OR IGNORE INTO inventory_locations (id, code, name, location_type, is_active, created_at) VALUES ('00000000-0000-0000-0000-000000000505', 'WAREHOUSE-1', 'Secondary Warehouse', 'SellableStorage', 1, '2026-01-01T00:00:00Z');";
            locCmd.ExecuteNonQuery();
        }
        foundation = RequireSuccess(await service.GetFoundationCatalogAsync(), "foundation refresh");
        Guid warehouse1LocId = foundation.Locations.First(l => l.Code == "WAREHOUSE-1").Id;
        Guid multiLocProdId = SeedProduct(paths.DatabasePath, "P1-05-MULTILOC", "9900003000099", "Multi-Location Test Item", 30_000, 60_000, 0, 1_000);

        PurchaseOrderModel whPo = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                nonResaleSup.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), warehouse1LocId, 30, 0, "Standard", "P1 multi-location fixture", true,
                new[] { new CreatePurchaseOrderLineInput(null, multiLocProdId, "P1-05-MULTILOC", "Multi-Location Test Item", PurchaseClassifications.Resale, "pcs", 10_000, 30_000, 0, 0, null) },
                receiverId)),
            "seed multi-loc PO");

        ReceivingReportModel whRr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                whPo.Id, warehouse1LocId, "DR-WH1-01", null, "Carrier", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "RR-WH1-01", null,
                new[] { new CreateReceivingReportLineInput(whPo.Lines[0].Id, 10_000, 10_000, 0, null, null, null, null) },
                receiverId)),
            "receive goods at WAREHOUSE-1");

        long mlStockTotal = ReadStockOnHand(paths.DatabasePath, multiLocProdId);
        long mlWh1Bucket = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "WAREHOUSE-1", "Available");
        long mlMainBucket = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "MAIN-STORE", "Available");

        Require(mlStockTotal == 10_000, $"Total stock on hand must be 10, got {mlStockTotal}.");
        Require(mlWh1Bucket == 10_000, $"WAREHOUSE-1 bucket must have 10, got {mlWh1Bucket}.");
        Require(mlMainBucket == 0, $"MAIN-STORE bucket must have 0, got {mlMainBucket}.");

        // Attempt POS checkout sale of 2 units: since MAIN-STORE has 0 available, sale must fail closed with InsufficientLocationStock!
        Guid mlSaleId = Guid.NewGuid();
        Sale mlSale = new()
        {
            Id = mlSaleId,
            ReceiptNumber = "OR-P1-00002",
            CashierUserId = cashierId,
            Subtotal = Money.FromCents(120_000),
            Total = Money.FromCents(120_000),
            AmountPaid = Money.FromCents(120_000),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "Snapshot"
        };
        SaleItem mlSaleItem = new()
        {
            SaleId = mlSaleId,
            ProductId = multiLocProdId,
            ProductNameSnapshot = "Multi-Location Test Item",
            BarcodeSnapshot = "9900003000099",
            UnitNameSnapshot = "pcs",
            Quantity = Quantity.FromX1000(2_000).Value,
            UnitPrice = Money.FromCents(60_000),
            CostPrice = Money.FromCents(30_000),
            LineTotal = Money.FromCents(120_000)
        };
        Payment mlPayment = new() { SaleId = mlSaleId, Amount = Money.FromCents(120_000) };
        StockMovement mlMovement = new()
        {
            ProductId = multiLocProdId,
            MovementType = StockMovementType.Sale,
            QuantityChange = Quantity.FromX1000(2_000).Value,
            CostPrice = Money.FromCents(30_000),
            ReferenceType = "SALE",
            ReferenceId = mlSaleId,
            Reason = "POS Sale Insufficient Test",
            UserId = cashierId
        };

        Result mlSaleResult = await composition.SaleWriteRepository.AddCompletedSaleAsync(
            mlSale, new[] { mlSaleItem }, new[] { mlPayment }, new[] { mlMovement });

        Require(mlSaleResult.IsFailure && mlSaleResult.ErrorDetail.Code == "Sales.InsufficientLocationStock",
            $"POS checkout sale must fail with Sales.InsufficientLocationStock when MAIN-STORE bucket has 0, got '{mlSaleResult.ErrorDetail?.Code}'.");

        // Verify stock and buckets did NOT change
        Require(ReadStockOnHand(paths.DatabasePath, multiLocProdId) == 10_000, "Total stock must remain unchanged after failed sale.");
        Require(ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "WAREHOUSE-1", "Available") == 10_000, "WAREHOUSE-1 bucket must remain 10.");
        Require(ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "MAIN-STORE", "Available") == 0, "MAIN-STORE bucket must remain 0.");

        // Now stock-in 5 units to MAIN-STORE
        Result<StockWriteResponse> mlStockIn = await invService.StockInAsync(
            new StockInRequest(multiLocProdId, 5.0m, "Stock-in to main store", "REF-ML-IN"), invSession);
        Require(mlStockIn.IsSuccess, "Stock-in to main store must succeed.");

        long mlStockAfterIn = ReadStockOnHand(paths.DatabasePath, multiLocProdId);
        long mlMainAfterIn = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "MAIN-STORE", "Available");
        long mlWh1AfterIn = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "WAREHOUSE-1", "Available");
        Require(mlStockAfterIn == 15_000 && mlMainAfterIn == 5_000 && mlWh1AfterIn == 10_000,
            $"After stock-in: total={mlStockAfterIn}, MAIN-STORE={mlMainAfterIn}, WAREHOUSE-1={mlWh1AfterIn}.");
        Require(mlMainAfterIn + mlWh1AfterIn == mlStockAfterIn, "Sum of sellable buckets must equal total stock on hand.");

        // Now POS checkout sale of 2 units succeeds
        Result mlSaleSuccess = await composition.SaleWriteRepository.AddCompletedSaleAsync(
            mlSale, new[] { mlSaleItem }, new[] { mlPayment }, new[] { mlMovement });
        Require(mlSaleSuccess.IsSuccess, "POS sale must succeed when MAIN-STORE has sufficient stock.");

        long mlStockAfterSale = ReadStockOnHand(paths.DatabasePath, multiLocProdId);
        long mlMainAfterSale = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "MAIN-STORE", "Available");
        long mlWh1AfterSale = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "WAREHOUSE-1", "Available");
        Require(mlStockAfterSale == 13_000 && mlMainAfterSale == 3_000 && mlWh1AfterSale == 10_000,
            $"After sale: total={mlStockAfterSale}, MAIN-STORE={mlMainAfterSale}, WAREHOUSE-1={mlWh1AfterSale}.");
        Require(mlMainAfterSale + mlWh1AfterSale == mlStockAfterSale, "Sum of sellable buckets must strictly equal total stock on hand.");

        // Void the sale
        Result<SaleVoidResponse> mlVoidRes = await voidService.VoidAsync(mlSaleId, new SaleVoidRequest("Customer return"), cashierSession);
        Require(mlVoidRes.IsSuccess, "Sale void must succeed.");
        Require(ReadStockOnHand(paths.DatabasePath, multiLocProdId) == 15_000, "Total stock restored to 15.");
        Require(ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "MAIN-STORE", "Available") == 5_000, "MAIN-STORE restored to 5.");

        // Physical count at WAREHOUSE-1: count 8 units (variance = -2_000)
        StockCountAdjustmentModel whCountAdj = RequireSuccess(
            await service.RecordStockCountAdjustmentAsync(new RecordStockCountAdjustmentRequest(
                multiLocProdId, warehouse1LocId, 8_000, "CYCLE_COUNT", "Warehouse count discrepancy", receiverId, supervisorId)),
            "WAREHOUSE-1 physical count adjustment");

        long mlStockFinal = ReadStockOnHand(paths.DatabasePath, multiLocProdId);
        long mlWh1Final = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "WAREHOUSE-1", "Available");
        long mlMainFinal = ReadLocationBucketQty(paths.DatabasePath, multiLocProdId, "MAIN-STORE", "Available");
        Require(mlWh1Final == 8_000, $"WAREHOUSE-1 bucket must be reconciled to 8_000, got {mlWh1Final}.");
        Require(mlMainFinal == 5_000, $"MAIN-STORE bucket must remain 5_000, got {mlMainFinal}.");
        Require(mlStockFinal == 13_000, $"Total stock must be 13_000, got {mlStockFinal}.");
        Require(mlMainFinal + mlWh1Final == mlStockFinal, "Sum of sellable buckets must strictly match total stock on hand.");
    }

    /// <summary>
    /// P1-06: Overdelivery approval authority.
    /// Invariants:
    /// - Overdelivery receiving reports cannot post without an explicit approved exception.
    /// - Receiving operator cannot self-approve overdelivery.
    /// - An independent supervisor must approve the exception in lpi_overdelivery_approvals.
    /// - Quantities changed after approval are detected and rejected.
    /// </summary>
    private static async Task TestP106OverdeliveryApprovalAuthorityAsync(
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid receiverId,
        Guid supervisorId,
        Guid _productId)
    {
        Guid productId = SeedProduct(paths.DatabasePath, "P1-06-RESALE", "9900004000001", "P1-06 Resale Item", 50_000, 80_000, 5_000, 1_000);
        LpiSupplier supplier = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-SUP-OVER", "Overdelivery Vendor Corp", "Dante", "0919000004", "over@test.com", "Quezon City", "000-111-222", 30, 5, null, receiverId)),
            "P1-06 seed supplier");

        PurchaseOrderModel po = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", "P1 overdelivery fixture", true,
                new[] { new CreatePurchaseOrderLineInput(null, productId, "P1-PROD-RESALE", "Resale Item", PurchaseClassifications.Resale, "pcs", 10_000, 50_000, 0, 0, null) },
                receiverId)),
            "P1-06 create PO (10 ordered)");

        // 1. Submit receiving report with overdelivery: Ordered 10, Delivered 12
        ReceivingReportModel rr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                po.Id, null, "DR-OVER-01", null, "Carrier", DateOnly.FromDateTime(DateTime.UtcNow),
                PostImmediately: false, // Remains draft/pending
                OverdeliveryApproverUserId: null,
                OverdeliveryApprovalReason: null,
                ClientRequestId: "RR-P1-06-01",
                Notes: "Vendor overdelivered 2 extra units",
                Lines: new[]
                {
                    new CreateReceivingReportLineInput(po.Lines[0].Id, 12_000, 12_000, 0, null, null, null, null)
                },
                ReceivedByUserId: receiverId)),
            "P1-06 submit overdelivered RR");

        Require(rr.HasOverdelivery, "Receiving report must have HasOverdelivery = true.");
        Require(rr.Status == "PendingExceptionApproval" || rr.Status == "Draft", $"Unapproved overdelivery RR must remain in PendingExceptionApproval or Draft, got '{rr.Status}'.");

        // 2. Attempt to post without approval -> must fail
        Result<ReceivingReportModel> postWithoutApproval = await service.PostReceivingReportAsync(new PostReceivingReportRequest(
            rr.Id, null, null, null, receiverId));
        Require(postWithoutApproval.IsFailure && postWithoutApproval.ErrorDetail.Code == "Lpi.Receiving.OverdeliveryApprovalRequired",
            $"Posting overdelivery without approval must fail with Lpi.Receiving.OverdeliveryApprovalRequired, got '{postWithoutApproval.ErrorDetail.Code}'.");

        // 3. Self-approval attempt: receiverId attempts to approve overdelivery
        Result<ReceivingReportModel> selfApprove = await service.ApproveOverdeliveryAsync(new ApproveOverdeliveryRequest(
            rr.Id, "Receiver self-approving overdelivery", receiverId));
        Require(selfApprove.IsFailure && (selfApprove.ErrorDetail.Code == "Lpi.Receiving.SelfApprovalForbidden" || selfApprove.ErrorDetail.Code == "ProcureToPay.SelfApprovalForbidden"),
            $"Receiver self-approving overdelivery must fail with SelfApprovalForbidden, got '{selfApprove.ErrorDetail.Code}'.");

        // 4. Independent supervisor approval by supervisorId
        ReceivingReportModel approvedRr = RequireSuccess(
            await service.ApproveOverdeliveryAsync(new ApproveOverdeliveryRequest(
                rr.Id, "Supervisory approval for bulk packaging variance", supervisorId)),
            "P1-06 supervisor approval");

        Require(approvedRr.OverdeliveryApprovedByUserId == supervisorId, "OverdeliveryApprovedByUserId must match supervisor.");

        // P1-R2-04: Overdelivery approval snapshot validation fail-closed tests
        // 4a. Tampered snapshot: Malformed/corrupted JSON
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand tamperCmd = conn.CreateCommand();
            tamperCmd.CommandText = "UPDATE lpi_overdelivery_approvals SET approved_lines_json = '{ corrupted json' WHERE receiving_report_id = $rrId;";
            tamperCmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            tamperCmd.ExecuteNonQuery();
        }

        Result<ReceivingReportModel> postCorrupted = await service.PostReceivingReportAsync(new PostReceivingReportRequest(
            rr.Id, null, null, null, receiverId));
        Require(postCorrupted.IsFailure && postCorrupted.ErrorDetail.Code == "Lpi.Receiving.InvalidApprovalSnapshot",
            $"Posting with corrupted snapshot JSON must fail with InvalidApprovalSnapshot, got '{postCorrupted.ErrorDetail?.Code}'.");

        // 4b. Tampered snapshot: Empty array []
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand tamperCmd = conn.CreateCommand();
            tamperCmd.CommandText = "UPDATE lpi_overdelivery_approvals SET approved_lines_json = '[]' WHERE receiving_report_id = $rrId;";
            tamperCmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            tamperCmd.ExecuteNonQuery();
        }

        Result<ReceivingReportModel> postEmpty = await service.PostReceivingReportAsync(new PostReceivingReportRequest(
            rr.Id, null, null, null, receiverId));
        Require(postEmpty.IsFailure && (postEmpty.ErrorDetail.Code == "Lpi.Receiving.InvalidApprovalSnapshot" || postEmpty.ErrorDetail.Code == "Lpi.Receiving.UnapprovedLineDetected"),
            $"Posting with empty snapshot lines must fail, got '{postEmpty.ErrorDetail?.Code}'.");

        // 4c. Tampered snapshot: Approved qty less than RR delivered qty (11_000 approved vs 12_000 delivered)
        string lowerQtyJson = JsonSerializer.Serialize(new[]
        {
            new OverdeliveryApprovedLineModel(po.Lines[0].Id, productId, "P1-06 Resale Item", 11_000L)
        });
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand tamperCmd = conn.CreateCommand();
            tamperCmd.CommandText = "UPDATE lpi_overdelivery_approvals SET approved_lines_json = $json WHERE receiving_report_id = $rrId;";
            tamperCmd.Parameters.AddWithValue("$json", lowerQtyJson);
            tamperCmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            tamperCmd.ExecuteNonQuery();
        }

        Result<ReceivingReportModel> postLowerQty = await service.PostReceivingReportAsync(new PostReceivingReportRequest(
            rr.Id, null, null, null, receiverId));
        Require(postLowerQty.IsFailure && postLowerQty.ErrorDetail.Code == "Lpi.Receiving.OverdeliveryQuantityChanged",
            $"Posting with approved quantity lower than delivered must fail with OverdeliveryQuantityChanged, got '{postLowerQty.ErrorDetail?.Code}'.");

        // 4d. Restore authentic snapshot
        string validSnapshotJson = JsonSerializer.Serialize(new[]
        {
            new OverdeliveryApprovedLineModel(po.Lines[0].Id, productId, "P1-06 Resale Item", 12_000L)
        });
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand restoreCmd = conn.CreateCommand();
            restoreCmd.CommandText = "UPDATE lpi_overdelivery_approvals SET approved_lines_json = $json WHERE receiving_report_id = $rrId;";
            restoreCmd.Parameters.AddWithValue("$json", validSnapshotJson);
            restoreCmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            restoreCmd.ExecuteNonQuery();
        }

        // 5. Post approved receiving report
        ReceivingReportModel postedRr = RequireSuccess(
            await service.PostReceivingReportAsync(new PostReceivingReportRequest(
                rr.Id, null, null, null, receiverId)),
            "P1-06 post approved RR");

        Require(postedRr.Status == "Posted", $"Approved RR must transition to Posted, got '{postedRr.Status}'.");

        // Verify explicit approval audit record in lpi_overdelivery_approvals table
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT approver_user_id, approval_reason, is_revoked
                FROM lpi_overdelivery_approvals
                WHERE receiving_report_id = $rrId;
                """;
            cmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            using SqliteDataReader reader = cmd.ExecuteReader();
            Require(reader.Read(), "lpi_overdelivery_approvals audit record must exist.");
            Guid recordedApprover = Guid.Parse(reader.GetString(0));
            string recordedReason = reader.GetString(1);
            long revoked = reader.GetInt64(2);
            Require(recordedApprover == supervisorId, "Recorded approver must match supervisorId.");
            Require(revoked == 0, "Approval must be active (is_revoked = 0).");
        }
    }

    /// <summary>
    /// P1-R2-02: Forward migration v28 verification.
    /// Invariants:
    /// - Database at v27 contains LPI base tables but NOT lpi_overdelivery_approvals or v28 columns.
    /// - Running migration v28 applies schema changes cleanly and backfills legacy product stock buckets.
    /// - Supported migration version is 28.
    /// - Database backup and restore succeeds on v28.
    /// </summary>
    private static async Task TestP1R202ForwardMigrationV28Async(DbPaths originalPaths)
    {
        string testDir = Path.Combine(Path.GetTempPath(), "salryn-migration-v28-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            DbPaths tempPaths = DbPaths.CreateDevelopment(testDir);
            SqliteConnectionFactory connectionFactory = new(tempPaths);

            // 1. Run migrations up to v27 only (ProcureToPaySchemaMigration)
            IDatabaseMigration[] v27Migrations = new IDatabaseMigration[]
            {
                new InitialSchemaMigration(),
                new BirMachineProfileSchemaMigration(),
                new BirOfficialDocumentSequenceSchemaMigration(),
                new BirElectronicJournalSchemaMigration(),
                new BirElectronicJournalSchemaHardeningMigration(),
                new BirZReadingSchemaMigration(),
                new BirElectronicJournalCorrectionEventSchemaMigration(),
                new ProductTaxClassificationSchemaMigration(),
                new DeploymentIdentitySchemaMigration(),
                new TerminalRegistrySchemaMigration(),
                new LanRemoteTransportSchemaMigration(),
                new TerminalCredentialHandshakeSchemaMigration(),
                new OfficialReceiptProfileSchemaMigration(),
                new OfficialDryRunPackageSchemaMigration(),
                new VatRegistrationModeSchemaMigration(),
                new OfficialDocumentLifecycleCompletionSchemaMigration(),
                new OfficialDocumentLifecycleAccountingSchemaMigration(),
                new StorePrinterModeSchemaMigration(),
                new WindowsPrinterNameSchemaMigration(),
                new StoreLogoBrandingSchemaMigration(),
                new RemotePosCheckoutRequestSchemaMigration(),
                new AccountsReceivableSchemaMigration(),
                new HostCheckoutRequestSchemaMigration(),
                new ReceivableCollectionRequestSchemaMigration(),
                new CashDrawerReconciliationSchemaMigration(),
                new CashDrawerCloseReplaySchemaMigration(),
                new SaleVoidAuditReconciliationMigration(),
                new ProcureToPaySchemaMigration() // v27!
            };

            MigrationRunner runnerV27 = new(connectionFactory, v27Migrations);
            runnerV27.RunPendingMigrations();

            // Verify v27 state: lpi_overdelivery_approvals does NOT exist yet
            using (SqliteConnection conn = connectionFactory.CreateConnection())
            {
                conn.Open();
                using SqliteCommand checkTable = conn.CreateCommand();
                checkTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='lpi_overdelivery_approvals';";
                long tableCount = Convert.ToInt64(checkTable.ExecuteScalar());
                Require(tableCount == 0, "lpi_overdelivery_approvals must NOT exist in v27 baseline.");

                // Seed a unit and a legacy product in v27 without product_stock_buckets row
                using SqliteCommand seedUnit = conn.CreateCommand();
                seedUnit.CommandText = "INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active) VALUES ('00000000-0000-0000-0000-000000000101', 'Piece', 'pcs', 0, 1);";
                seedUnit.ExecuteNonQuery();

                using SqliteCommand seedProd = conn.CreateCommand();
                seedProd.CommandText = """
                    INSERT INTO products (id, sku, barcode, name, unit_id, cost_price_cents, selling_price_cents, stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount, is_active, created_at, updated_at)
                    VALUES ('11111111-1111-1111-1111-111111111111', 'LEGACY-SKU', '9999999999999', 'Legacy Product',
                            (SELECT id FROM units LIMIT 1), 10000, 20000, 7500, 1000, 1, 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                    """;
                seedProd.ExecuteNonQuery();

                // Check bucket count for this product before v28: 0
                using SqliteCommand checkBucket = conn.CreateCommand();
                checkBucket.CommandText = "SELECT COUNT(*) FROM product_stock_buckets WHERE product_id = '11111111-1111-1111-1111-111111111111';";
                long bCountBefore = Convert.ToInt64(checkBucket.ExecuteScalar());
                Require(bCountBefore == 0, "Legacy product has no bucket before v28.");
            }

            // 2. Apply v28 migration
            MigrationRunner runnerV28 = new(connectionFactory, new IDatabaseMigration[] { new ProcureToPayR2HardeningMigration() });
            runnerV28.RunPendingMigrations();

            // Verify v28 state: lpi_overdelivery_approvals EXISTS, columns exist, legacy product backfilled
            using (SqliteConnection conn = connectionFactory.CreateConnection())
            {
                conn.Open();
                using SqliteCommand checkTable = conn.CreateCommand();
                checkTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='lpi_overdelivery_approvals';";
                long tableCount = Convert.ToInt64(checkTable.ExecuteScalar());
                Require(tableCount == 1, "lpi_overdelivery_approvals MUST exist after v28 migration.");

                // Check legacy product was backfilled in product_stock_buckets with 7500
                using SqliteCommand checkBucket = conn.CreateCommand();
                checkBucket.CommandText = """
                    SELECT quantity_x1000 FROM product_stock_buckets
                    WHERE product_id = '11111111-1111-1111-1111-111111111111' AND bucket_status = 'Available';
                    """;
                object? val = checkBucket.ExecuteScalar();
                Require(val is not null && Convert.ToInt64(val) == 7500, $"Legacy product must be backfilled with quantity 7500, got '{val}'.");

                // Check receiving_reports.request_payload_hash exists
                using SqliteCommand checkCol = conn.CreateCommand();
                checkCol.CommandText = "PRAGMA table_info(receiving_reports);";
                using SqliteDataReader r = checkCol.ExecuteReader();
                bool hasHash = false;
                while (r.Read())
                {
                    if (string.Equals(r.GetString(1), "request_payload_hash", StringComparison.OrdinalIgnoreCase)) hasHash = true;
                }
                Require(hasHash, "receiving_reports must have request_payload_hash column after v28.");
            }

            // 3. Test backup and restore verification using SqliteBackupRecoveryService
            SqliteBackupRecoveryService recovery = new(tempPaths);
            Result<string> backupRes = recovery.CreateBackup();
            Require(backupRes.IsSuccess, $"Backup on v28 database must succeed: {backupRes.ErrorDetail?.Message}");
            string backupPath = backupRes.Value;
            Require(File.Exists(backupPath), "Backup file must exist on disk.");

            Result<BackupVerificationSummary> verifyRes = recovery.VerifyBackup(backupPath);
            Require(verifyRes.IsSuccess && verifyRes.Value.MigrationVersionCompatible,
                $"Backup verification must pass on v28 schema: {verifyRes.ErrorDetail?.Message}");

            Console.WriteLine("P1-R2-02: Forward migration v27 -> v28 and backup verified PASS.");
            await Task.CompletedTask;
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }
    }


    private static async Task TestP1R301FirstStockInDoublesLocationInventoryAsync(
        SalrynLocalComposition composition,
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid cashierId,
        Guid supervisorId)
    {
        Console.WriteLine("Running P1-R3-01: First Stock-In Doubles Location Inventory verification...");
        InMemoryLocalApiSessionStore store = new();
        LocalApiSession cashierSession = CreateSession(store, "Cashier", new[] {
            "Products.Manage",
            SalrynPermissions.InventoryAdjust,
            SalrynPermissions.SalesVoid
        }, cashierId);

        Guid unitId;
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM units WHERE is_active = 1 LIMIT 1;";
            unitId = Guid.Parse((string)cmd.ExecuteScalar()!);
        }

        FastAddProductApiService fastAdd = new(composition);

        // 1. Fast Add with zero initial stock
        string zeroBarcode = "8800000000010";
        var zeroRes = await fastAdd.FastAddAsync(new FastAddProductRequest(
            zeroBarcode, "Fast Add Zero Stock Item", unitId, 150.00m, 0.0m), cashierSession);
        Require(zeroRes.IsSuccess, $"Fast Add with zero stock must succeed: {zeroRes.ErrorDetail?.Message}");
        Guid zeroProdId = zeroRes.Value.Product.Id;

        long zeroProdStock = ReadStockOnHand(paths.DatabasePath, zeroProdId);
        long zeroBucketStock = ReadLocationBucketQty(paths.DatabasePath, zeroProdId, "MAIN-STORE", "Available");
        Require(zeroProdStock == 0, $"Fast Add zero stock: product stock must be 0, got {zeroProdStock}");
        Require(zeroBucketStock == 0, $"Fast Add zero stock: MAIN-STORE bucket must be 0, got {zeroBucketStock}");

        // 2. Fast Add with five initial units (5.0m)
        string fiveBarcode = "8800000000011";
        var fiveRes = await fastAdd.FastAddAsync(new FastAddProductRequest(
            fiveBarcode, "Fast Add Five Stock Item", unitId, 250.00m, 5.0m), cashierSession);
        Require(fiveRes.IsSuccess, $"Fast Add with 5 units must succeed: {fiveRes.ErrorDetail?.Message}");
        Guid fiveProdId = fiveRes.Value.Product.Id;

        long fiveProdStock = ReadStockOnHand(paths.DatabasePath, fiveProdId);
        long fiveBucketStock = ReadLocationBucketQty(paths.DatabasePath, fiveProdId, "MAIN-STORE", "Available");
        Require(fiveProdStock == 5_000, $"Fast Add 5 units: product stock must be 5000, got {fiveProdStock}");
        Require(fiveBucketStock == 5_000,
            $"Fast Add 5 units (P1-R3-01): MAIN-STORE bucket must be 5000 (NOT doubled to 10000), got {fiveBucketStock}");

        // Verify stock_movements has exactly one StockIn movement
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), SUM(qty_change_x1000) FROM stock_movements WHERE product_id = $id;";
            cmd.Parameters.AddWithValue("$id", fiveProdId.ToString("D"));
            using var reader = cmd.ExecuteReader();
            Require(reader.Read(), "Reader must read movements");
            long movCount = reader.GetInt64(0);
            long movQty = reader.GetInt64(1);
            Require(movCount == 1 && movQty == 5_000,
                $"Fast Add 5 units: expected 1 StockIn movement with 5000, got count={movCount}, qty={movQty}");
        }

        // 3. Stock-In for an existing product without a location bucket
        Guid legacyProdId = Guid.NewGuid();
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO products (
                    id, sku, barcode, name, unit_id, cost_price_cents, selling_price_cents,
                    stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount,
                    is_active, created_at, updated_at
                ) VALUES (
                    $id, 'NO-BUCKET-SKU', '8800000000012', 'No Bucket Legacy Item', $unitId,
                    5000, 10000, 10000, 1000, 1, 1, 1, datetime('now'), datetime('now')
                );
                """;
            cmd.Parameters.AddWithValue("$id", legacyProdId.ToString("D"));
            cmd.Parameters.AddWithValue("$unitId", unitId.ToString("D"));
            cmd.ExecuteNonQuery();

            // Verify 0 buckets exist
            using SqliteCommand checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM product_stock_buckets WHERE product_id = $id;";
            checkCmd.Parameters.AddWithValue("$id", legacyProdId.ToString("D"));
            Require(Convert.ToInt64(checkCmd.ExecuteScalar()) == 0, "Product must have 0 buckets initially.");
        }

        // Apply Stock-In +5 units (5_000)
        InventoryManagementApiService invService = new(composition);
        LocalApiSession invSession = CreateSession(store, "Manager", new[] { SalrynPermissions.InventoryAdjust }, cashierId);
        var stockInRes = await invService.StockInAsync(
            new StockInRequest(legacyProdId, 5.0m, "Restock legacy product", "REF-LEGACY-IN"), invSession);
        Require(stockInRes.IsSuccess, $"Stock-In on no-bucket product must succeed: {stockInRes.ErrorDetail?.Message}");

        long legacyProdStockAfterIn = ReadStockOnHand(paths.DatabasePath, legacyProdId);
        long legacyBucketStockAfterIn = ReadLocationBucketQty(paths.DatabasePath, legacyProdId, "MAIN-STORE", "Available");
        Require(legacyProdStockAfterIn == 15_000,
            $"Stock-In on legacy product: product stock must be 15000, got {legacyProdStockAfterIn}");
        Require(legacyBucketStockAfterIn == 15_000,
            $"Stock-In on legacy product (P1-R3-01): MAIN-STORE bucket must be 15000 (pre-movement 10k + 5k, NOT 20k), got {legacyBucketStockAfterIn}");

        // 4. Subsequent sale, void, stock adjustment and restart
        // 4a. Sale of 2 units (2_000)
        Guid saleId = Guid.NewGuid();
        Guid saleItemId = Guid.NewGuid();
        Guid movId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = "OR-P1R3-00001",
            CashierUserId = cashierId,
            Subtotal = Money.FromCents(200_000),
            Total = Money.FromCents(200_000),
            AmountPaid = Money.FromCents(200_000),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "Snapshot"
        };
        SaleItem saleItem = new()
        {
            SaleId = saleId,
            ProductId = legacyProdId,
            ProductNameSnapshot = "Legacy Item",
            BarcodeSnapshot = "8800000000012",
            UnitNameSnapshot = "pcs",
            Quantity = Quantity.FromX1000(2_000).Value,
            UnitPrice = Money.FromCents(100_000),
            CostPrice = Money.FromCents(50_000),
            LineTotal = Money.FromCents(200_000)
        };
        Payment payment = new() { SaleId = saleId, Amount = Money.FromCents(200_000) };
        StockMovement saleMov = new()
        {
            ProductId = legacyProdId,
            MovementType = StockMovementType.Sale,
            QuantityChange = Quantity.FromX1000(2_000).Value,
            CostPrice = Money.FromCents(50_000),
            ReferenceType = "SALE",
            ReferenceId = saleId,
            Reason = "POS Checkout Sale",
            UserId = cashierId
        };

        Result saleRes = await composition.SaleWriteRepository.AddCompletedSaleAsync(
            sale, new[] { saleItem }, new[] { payment }, new[] { saleMov });
        Require(saleRes.IsSuccess, $"Sale of 2 units must succeed: {saleRes.ErrorDetail?.Message}");

        long stockAfterSale = ReadStockOnHand(paths.DatabasePath, legacyProdId);
        long bucketAfterSale = ReadLocationBucketQty(paths.DatabasePath, legacyProdId, "MAIN-STORE", "Available");
        Require(stockAfterSale == 13_000 && bucketAfterSale == 13_000,
            $"Sale must decrease both product stock and bucket to 13000 (stock={stockAfterSale}, bucket={bucketAfterSale})");

        // 4b. Void of the sale
        SaleVoidApiService voidService = new(composition);
        var voidRes = await voidService.VoidAsync(saleId, new SaleVoidRequest("Customer return P1R3"), cashierSession);
        Require(voidRes.IsSuccess, $"Void must succeed: {voidRes.ErrorDetail?.Message}");

        long stockAfterVoid = ReadStockOnHand(paths.DatabasePath, legacyProdId);
        long bucketAfterVoid = ReadLocationBucketQty(paths.DatabasePath, legacyProdId, "MAIN-STORE", "Available");
        Require(stockAfterVoid == 15_000 && bucketAfterVoid == 15_000,
            $"Void must restore both product stock and bucket to 15000 (stock={stockAfterVoid}, bucket={bucketAfterVoid})");

        // 4c. Physical count adjustment to 18 units (18_000)
        LpiFoundationCatalog foundation = RequireSuccess(await service.GetFoundationCatalogAsync(), "foundation catalog");
        Guid mainStoreLocId = foundation.Locations.First(l => l.Code == "MAIN-STORE").Id;

        var adjRes = await service.RecordStockCountAdjustmentAsync(new RecordStockCountAdjustmentRequest(
            legacyProdId,
            mainStoreLocId,
            18_000,
            "FoundUnrecordedStock",
            "P1-R3-01 verification count adjustment",
            cashierId,
            supervisorId));
        Require(adjRes.IsSuccess, $"Count adjustment must succeed: {adjRes.ErrorDetail?.Message}");

        long stockAfterAdj = ReadStockOnHand(paths.DatabasePath, legacyProdId);
        long bucketAfterAdj = ReadLocationBucketQty(paths.DatabasePath, legacyProdId, "MAIN-STORE", "Available");
        Require(stockAfterAdj == 18_000 && bucketAfterAdj == 18_000,
            $"Adjustment must update both product stock and bucket to 18000 (stock={stockAfterAdj}, bucket={bucketAfterAdj})");

        // 4d. Restart verification: create fresh composition from paths
        SalrynLocalComposition restartedComposition = SalrynLocalComposition.Create(paths);
        long stockRestart = ReadStockOnHand(paths.DatabasePath, legacyProdId);
        long bucketRestart = ReadLocationBucketQty(paths.DatabasePath, legacyProdId, "MAIN-STORE", "Available");
        Require(stockRestart == 18_000 && bucketRestart == 18_000,
            $"After restart: product stock and bucket must remain 18000 (stock={stockRestart}, bucket={bucketRestart})");

        Console.WriteLine("P1-R3-01: First Stock-In Doubles Location Inventory verified PASS.");
    }

    private static async Task TestP1R302MigrationV28NoStockDuplicationAsync()
    {
        Console.WriteLine("Running P1-R3-02: Migration v28 Duplicates Existing Warehouse Stock verification...");
        string testDir = Path.Combine(Path.GetTempPath(), "salryn-p1r3-v28-upgrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths tempPaths = DbPaths.CreateDevelopment(testDir);
            Directory.CreateDirectory(testDir);
            SqliteConnectionFactory connectionFactory = new(tempPaths);

            // 1. Build genuine v27 database using the exact 27 schema migrations
            IDatabaseMigration[] v27Migrations = new IDatabaseMigration[]
            {
                new InitialSchemaMigration(),
                new BirMachineProfileSchemaMigration(),
                new BirOfficialDocumentSequenceSchemaMigration(),
                new BirElectronicJournalSchemaMigration(),
                new BirElectronicJournalSchemaHardeningMigration(),
                new BirZReadingSchemaMigration(),
                new BirElectronicJournalCorrectionEventSchemaMigration(),
                new ProductTaxClassificationSchemaMigration(),
                new DeploymentIdentitySchemaMigration(),
                new TerminalRegistrySchemaMigration(),
                new LanRemoteTransportSchemaMigration(),
                new TerminalCredentialHandshakeSchemaMigration(),
                new OfficialReceiptProfileSchemaMigration(),
                new OfficialDryRunPackageSchemaMigration(),
                new VatRegistrationModeSchemaMigration(),
                new OfficialDocumentLifecycleCompletionSchemaMigration(),
                new OfficialDocumentLifecycleAccountingSchemaMigration(),
                new StorePrinterModeSchemaMigration(),
                new WindowsPrinterNameSchemaMigration(),
                new StoreLogoBrandingSchemaMigration(),
                new RemotePosCheckoutRequestSchemaMigration(),
                new AccountsReceivableSchemaMigration(),
                new HostCheckoutRequestSchemaMigration(),
                new ReceivableCollectionRequestSchemaMigration(),
                new CashDrawerReconciliationSchemaMigration(),
                new CashDrawerCloseReplaySchemaMigration(),
                new SaleVoidAuditReconciliationMigration(),
                new ProcureToPaySchemaMigration() // v27!
            };

            MigrationRunner runnerV27 = new(connectionFactory, v27Migrations);
            runnerV27.RunPendingMigrations();

            // Insert a unit and a secondary warehouse location (WAREHOUSE-1)
            Guid wh1LocId = Guid.NewGuid();
            Guid mainStoreLocId;
            Guid pWarehouseOnly = Guid.NewGuid();
            Guid pLegacyNoBucket = Guid.NewGuid();
            Guid pAlreadyConsistent = Guid.NewGuid();
            Guid pHistoricalDiscrepancy = Guid.NewGuid();

            using (SqliteConnection conn = connectionFactory.CreateConnection())
            {
                conn.Open();
                using SqliteCommand unitCmd = conn.CreateCommand();
                unitCmd.CommandText = "INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active) VALUES ('00000000-0000-0000-0000-000000000101', 'Piece', 'pcs', 0, 1);";
                unitCmd.ExecuteNonQuery();

                using SqliteCommand locCmd = conn.CreateCommand();
                locCmd.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
                mainStoreLocId = Guid.Parse((string)locCmd.ExecuteScalar()!);

                using SqliteCommand whCmd = conn.CreateCommand();
                whCmd.CommandText = """
                    INSERT INTO inventory_locations (id, code, name, location_type, is_active, created_at)
                    VALUES ($id, 'WAREHOUSE-1', 'Main Distribution Warehouse', 'Warehouse', 1, datetime('now'));
                    """;
                whCmd.Parameters.AddWithValue("$id", wh1LocId.ToString("D"));
                whCmd.ExecuteNonQuery();

                // 1. Genuine v27 database containing warehouse-only received stock:
                // Product 1: stock_on_hand = 10_000, bucket in WAREHOUSE-1 = 10_000, 0 in MAIN-STORE.
                using SqliteCommand p1Cmd = conn.CreateCommand();
                p1Cmd.CommandText = """
                    INSERT INTO products (id, sku, barcode, name, unit_id, cost_price_cents, selling_price_cents,
                                          stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount, is_active, created_at, updated_at)
                    VALUES ($id, 'WH-ONLY-SKU', '8800000000021', 'Warehouse Only Item', '00000000-0000-0000-0000-000000000101', 5000, 10000, 10000, 1000, 1, 1, 1, datetime('now'), datetime('now'));

                    INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
                    VALUES ($bId, $id, $locId, 'Available', 10000, datetime('now'));
                    """;
                p1Cmd.Parameters.AddWithValue("$id", pWarehouseOnly.ToString("D"));
                p1Cmd.Parameters.AddWithValue("$bId", Guid.NewGuid().ToString("D"));
                p1Cmd.Parameters.AddWithValue("$locId", wh1LocId.ToString("D"));
                p1Cmd.ExecuteNonQuery();

                // 2. Legacy product with no stock-location buckets anywhere:
                // Product 2: stock_on_hand = 7_500, no buckets anywhere.
                using SqliteCommand p2Cmd = conn.CreateCommand();
                p2Cmd.CommandText = """
                    INSERT INTO products (id, sku, barcode, name, unit_id, cost_price_cents, selling_price_cents,
                                          stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount, is_active, created_at, updated_at)
                    VALUES ($id, 'LEGACY-NO-BUCKET', '8800000000022', 'Legacy No Bucket Item', '00000000-0000-0000-0000-000000000101', 4000, 8000, 7500, 1000, 1, 1, 1, datetime('now'), datetime('now'));
                    """;
                p2Cmd.Parameters.AddWithValue("$id", pLegacyNoBucket.ToString("D"));
                p2Cmd.ExecuteNonQuery();

                // 3. Already consistent multiple-location inventory:
                // Product 3: stock_on_hand = 12_000, MAIN-STORE = 4_000, WAREHOUSE-1 = 8_000.
                using SqliteCommand p3Cmd = conn.CreateCommand();
                p3Cmd.CommandText = """
                    INSERT INTO products (id, sku, barcode, name, unit_id, cost_price_cents, selling_price_cents,
                                          stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount, is_active, created_at, updated_at)
                    VALUES ($id, 'CONSISTENT-MULTI', '8800000000023', 'Consistent Multi Location Item', '00000000-0000-0000-0000-000000000101', 6000, 12000, 12000, 1000, 1, 1, 1, datetime('now'), datetime('now'));

                    INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
                    VALUES ($bMain, $id, $locMain, 'Available', 4000, datetime('now')),
                           ($bWh, $id, $locWh, 'Available', 8000, datetime('now'));
                    """;
                p3Cmd.Parameters.AddWithValue("$id", pAlreadyConsistent.ToString("D"));
                p3Cmd.Parameters.AddWithValue("$bMain", Guid.NewGuid().ToString("D"));
                p3Cmd.Parameters.AddWithValue("$locMain", mainStoreLocId.ToString("D"));
                p3Cmd.Parameters.AddWithValue("$bWh", Guid.NewGuid().ToString("D"));
                p3Cmd.Parameters.AddWithValue("$locWh", wh1LocId.ToString("D"));
                p3Cmd.ExecuteNonQuery();

                // 4. Inconsistent multi-location inventory (historical discrepancy):
                // Product 4: stock_on_hand = 15_000, WAREHOUSE-1 = 9_000 (discrepancy).
                using SqliteCommand p4Cmd = conn.CreateCommand();
                p4Cmd.CommandText = """
                    INSERT INTO products (id, sku, barcode, name, unit_id, cost_price_cents, selling_price_cents,
                                          stock_on_hand_x1000, reorder_level_x1000, track_stock, allow_discount, is_active, created_at, updated_at)
                    VALUES ($id, 'DISCREPANCY-MULTI', '8800000000024', 'Historical Discrepancy Item', '00000000-0000-0000-0000-000000000101', 7000, 14000, 15000, 1000, 1, 1, 1, datetime('now'), datetime('now'));

                    INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
                    VALUES ($bWh, $id, $locWh, 'Available', 9000, datetime('now'));
                    """;
                p4Cmd.Parameters.AddWithValue("$id", pHistoricalDiscrepancy.ToString("D"));
                p4Cmd.Parameters.AddWithValue("$bWh", Guid.NewGuid().ToString("D"));
                p4Cmd.Parameters.AddWithValue("$locWh", wh1LocId.ToString("D"));
                p4Cmd.ExecuteNonQuery();
            }

            // 2. Apply v28 migration
            MigrationRunner runnerV28 = new(connectionFactory, new IDatabaseMigration[] { new ProcureToPayR2HardeningMigration() });
            runnerV28.RunPendingMigrations();

            // 3. Verify assertions on all 4 products
            using (SqliteConnection conn = connectionFactory.CreateConnection())
            {
                conn.Open();

                // Product 1: Warehouse only
                long p1Stock = ReadStockOnHand(tempPaths.DatabasePath, pWarehouseOnly);
                long p1WhBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pWarehouseOnly, "WAREHOUSE-1", "Available");
                long p1MainBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pWarehouseOnly, "MAIN-STORE", "Available");
                Require(p1Stock == 10_000, $"Product 1 stock must be 10000, got {p1Stock}");
                Require(p1WhBucket == 10_000, $"Product 1 WAREHOUSE-1 bucket must remain 10000, got {p1WhBucket}");
                Require(p1MainBucket == 0,
                    $"Product 1 MAIN-STORE bucket must be 0 (P1-R3-02: must NOT duplicate warehouse stock into MAIN-STORE), got {p1MainBucket}");

                // Product 2: Legacy product without buckets
                long p2Stock = ReadStockOnHand(tempPaths.DatabasePath, pLegacyNoBucket);
                long p2MainBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pLegacyNoBucket, "MAIN-STORE", "Available");
                Require(p2Stock == 7_500, $"Product 2 stock must be 7500, got {p2Stock}");
                Require(p2MainBucket == 7_500, $"Product 2 MAIN-STORE bucket must be backfilled with 7500, got {p2MainBucket}");

                // Product 3: Already consistent multiple locations
                long p3Stock = ReadStockOnHand(tempPaths.DatabasePath, pAlreadyConsistent);
                long p3MainBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pAlreadyConsistent, "MAIN-STORE", "Available");
                long p3WhBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pAlreadyConsistent, "WAREHOUSE-1", "Available");
                Require(p3Stock == 12_000, $"Product 3 stock must be 12000, got {p3Stock}");
                Require(p3MainBucket == 4_000, $"Product 3 MAIN-STORE bucket must remain 4000, got {p3MainBucket}");
                Require(p3WhBucket == 8_000, $"Product 3 WAREHOUSE-1 bucket must remain 8000, got {p3WhBucket}");

                // Product 4: Historical discrepancy preserved without manufacturing fake stock
                long p4Stock = ReadStockOnHand(tempPaths.DatabasePath, pHistoricalDiscrepancy);
                long p4WhBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pHistoricalDiscrepancy, "WAREHOUSE-1", "Available");
                long p4MainBucket = ReadLocationBucketQty(tempPaths.DatabasePath, pHistoricalDiscrepancy, "MAIN-STORE", "Available");
                Require(p4Stock == 15_000, $"Product 4 stock must remain 15000, got {p4Stock}");
                Require(p4WhBucket == 9_000, $"Product 4 WAREHOUSE-1 bucket must remain 9000, got {p4WhBucket}");
                Require(p4MainBucket == 0,
                    $"Product 4 MAIN-STORE bucket must remain 0 (no fake stock manufactured to force reconciliation), got {p4MainBucket}");
            }

            // 4. Backup and restore verification
            SqliteBackupRecoveryService recovery = new(tempPaths);
            Result<string> backupRes = recovery.CreateBackup();
            Require(backupRes.IsSuccess, $"Backup on upgraded v28 database must succeed: {backupRes.ErrorDetail?.Message}");
            string backupPath = backupRes.Value;
            Require(File.Exists(backupPath), "Backup file must exist on disk.");

            Result<BackupVerificationSummary> verifyRes = recovery.VerifyBackup(backupPath);
            Require(verifyRes.IsSuccess && verifyRes.Value.MigrationVersionCompatible,
                $"Backup verification must pass on v28 schema: {verifyRes.ErrorDetail?.Message}");

            Console.WriteLine("P1-R3-02: Migration v28 Duplicates Existing Warehouse Stock verified PASS.");
            await Task.CompletedTask;
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(testDir))
            {
                try { Directory.Delete(testDir, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// W5-R2: Winning Quotation Preservation & API Contract Fallbacks.
    /// Invariants:
    /// - PR to PO conversion must bind winning supplier quotation unit cost, payment terms, and lead time.
    /// - API request records must resolve contract aliases for RR, variance approval, and supplier payment.
    /// </summary>
    private static async Task TestW5R2QuotationPreservationAndContractFallbacksAsync(
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid requesterId,
        Guid supervisorId,
        Guid productId)
    {
        // 1. Seed winning quotation supplier
        LpiSupplier quoteSupplier = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "W5-SUP-QUOTE", "Competitive Quotation Supplier Corp", "Alex", "0918000001", "quotes@supplier.ph",
                "Cebu IT Park", "555-666-777", 45, 7, "Preferred quote test supplier", requesterId)),
            "W5-R2 seed quote supplier");

        // 2. Create PR with baseline estimated unit cost = 50,000 cents
        PurchaseRequisitionModel pr = RequireSuccess(
            await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                "Engineering QA",
                new DateOnly(2026, 12, 1),
                "Normal",
                "Quotation preservation regression test PR",
                "W5 R2 unit cost preservation proof",
                true,
                new[]
                {
                    new CreatePurchaseRequisitionLineInput(
                        productId, "W5-RESALE-01", "P1 Commercial Resale Item", "Commercial", "Resale",
                        "box", 10_000, 50_000, quoteSupplier.Id, "Item quotation test", "Preserve winning price")
                },
                requesterId)),
            "W5-R2 create and submit PR");

        // 3. Supervisor approves PR
        pr = RequireSuccess(
            await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                pr.Id, "Approve", "Approved for quotation comparison", null, supervisorId)),
            "W5-R2 approve PR");

        // 4. Record winning quotation with lower negotiated unit cost (42,000 cents), 45 payment terms, 7 lead time days
        SupplierQuotationModel quote = RequireSuccess(
            await service.RecordSupplierQuotationAsync(new RecordSupplierQuotationRequest(
                pr.Id,
                pr.Lines[0].Id,
                quoteSupplier.Id,
                productId,
                "P1 Commercial Resale Item",
                42_000, // Negotiated unit cost
                5_000,  // Minimum order quantity
                7,      // Lead time days
                45,     // Payment terms days
                new DateOnly(2027, 1, 31),
                true,   // Winning quotation!
                "Winning negotiated quotation at 42,000 cents",
                requesterId)),
            "W5-R2 record winning quotation");

        Require(quote.IsSelected, "W5-R2 quotation must be selected as winner.");

        // 5. Convert Requisition to PO using ConvertRequisitionToPurchaseOrderAsync
        PurchaseOrderModel po = RequireSuccess(
            await service.ConvertRequisitionToPurchaseOrderAsync(
                pr.Id,
                quoteSupplier.Id,
                requisitionLineIds: null,
                deliveryLocationId: null,
                expectedDeliveryDate: null,
                actorUserId: requesterId),
            "W5-R2 convert PR to PO");

        // 6. Verify that PO line unit cost matches the winning quotation (42,000 cents), NOT estimated PR cost (50,000 cents)
        Require(po.Lines.Count == 1, "W5-R2 PO must contain 1 converted line.");
        PurchaseOrderLineModel poLine = po.Lines[0];
        Require(poLine.UnitCostCents == 42_000,
            $"W5-R2 PO line unit cost MUST preserve winning quotation price of 42,000 cents (actual: {poLine.UnitCostCents}).");
        Require(poLine.LineTotalCents == 420_000,
            $"W5-R2 PO line total must be 420,000 cents (actual: {poLine.LineTotalCents}).");
        Require(po.TotalAmountCents == 420_000,
            $"W5-R2 PO total amount must reflect winning quotation total (actual: {po.TotalAmountCents}).");
        Require(po.PaymentTermsDays == 45,
            $"W5-R2 PO payment terms must reflect winning quotation terms of 45 days (actual: {po.PaymentTermsDays}).");

        DateOnly expectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        Require(po.ExpectedDeliveryDate == expectedDelivery,
            $"W5-R2 PO expected delivery date must calculate based on lead time of 7 days (actual: {po.ExpectedDeliveryDate}, expected: {expectedDelivery}).");

        // 7. Verify API request contract aliases
        LocalProcureToPayEndpoints.CreateReceivingReportLineApiInput rrLineInput = new(
            poLine.Id, null, 10_000, null, 10_000, null, 0, null, null, null, null, null);
        Require(rrLineInput.PurchaseOrderLineId == poLine.Id && rrLineInput.ActualDeliveredQtyX1000 == 10_000,
            "W5-R2 rrLineInput must bind aliases correctly.");

        LocalProcureToPayEndpoints.CreateReceivingReportApiRequest rrReq = new(
            po.Id, Guid.NewGuid(), "DR-LEGACY-01", null, "SI-LEGACY-01", null, "Carrier-1", null,
            new DateOnly(2026, 12, 5), true, null, null, "req-1", "notes", new[] { rrLineInput }, null);
        Require(rrReq.ResolvedDeliveryReceiptNo == "DR-LEGACY-01" && rrReq.ResolvedCarrierOrDriver == "Carrier-1",
            "W5-R2 rrReq must resolve legacy delivery receipt and carrier aliases.");

        LocalProcureToPayEndpoints.ApproveVarianceApiRequest varianceReq = new(ApprovalReason: null, Notes: null, Reason: "Legacy reason notes", PostToApImmediately: false);
        Require(varianceReq.ResolvedApprovalReason == "Legacy reason notes",
            "W5-R2 varianceReq must resolve Reason alias.");

        LocalProcureToPayEndpoints.RecordSupplierPaymentApiRequest payReq = new(SupplierInvoiceId: null, InvoiceId: Guid.NewGuid(), PaymentDate: new DateOnly(2026, 12, 10), AmountCents: 10_000, PaymentMethod: "Check", DisbursementSource: null, ReferenceNo: null, ReferenceNumber: "REF-123", CheckNo: null, CheckNumber: "CHK-123", BankName: null, BankAccount: "BDO", Remarks: null, Notes: "Payment note", ClientRequestId: "idem-1");
        Require(payReq.ResolvedBankName == "BDO" && payReq.ResolvedCheckNo == "CHK-123" && payReq.ResolvedRemarks == "Payment note",
            "W5-R2 payReq must resolve bank, check, and remarks aliases.");
    }

    /// <summary>
    /// R2.1 Verification:
    /// 1. Multi-line PR with the same product quoted differently: strict line-level quote matching with zero price leakage across lines, and max lead time preservation.
    /// 2. Mandatory source identifiers & justifications: HTTP 400 Bad Request on missing DR#, Invoice#, Payment Reference#, or Variance Reason.
    /// 3. Cashier drawer protection: HTTP 400 Bad Request on CashierShiftDrawer disbursement source.
    /// 4. Payments query parameter: Both supplierInvoiceId and invoiceId filter correctly.
    /// </summary>
    private static async Task TestW5R21ReconciliationIntegrityAsync(
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid requesterId,
        Guid supervisorId,
        Guid apClerkId,
        Guid productId)
    {
        // 1. Multi-line PR with SAME product quoted differently
        LpiSupplier multiQuoteSupplier = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "P1-R21-SUP", "Multi-Quote Precision Supplier", "Agent", "09178889999", "precision@test.com", "Taguig", "888-999-000", 30, 7, null, requesterId)),
            "R21 create supplier");

        PurchaseRequisitionModel pr = RequireSuccess(
            await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                "Procurement Department",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                "High",
                "R2.1 Multi-line Quote Test",
                "Testing line-level pricing isolation for identical products",
                true,
                new[]
                {
                    new CreatePurchaseRequisitionLineInput(productId, "P1-PROD-RESALE", "Resale Item Retail Batch", "Commercial", "Resale", "pcs", 5_000, 110_000, multiQuoteSupplier.Id, "Retail batch", "Line 1"),
                    new CreatePurchaseRequisitionLineInput(productId, "P1-PROD-RESALE", "Resale Item Bulk Batch", "Commercial", "Resale", "pcs", 50_000, 90_000, multiQuoteSupplier.Id, "Bulk batch", "Line 2")
                },
                requesterId)),
            "R21 create PR");

        PurchaseRequisitionModel reviewedPr = RequireSuccess(
            await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                pr.Id,
                "Approve",
                "Approved all batches",
                null,
                supervisorId)),
            "R21 review PR");

        // Record Quote 1: Line 0 (Retail batch) -> 100,000 cents, 3 days lead time, 30 days terms, selected
        SupplierQuotationModel quote1 = RequireSuccess(
            await service.RecordSupplierQuotationAsync(new RecordSupplierQuotationRequest(
                reviewedPr.Id,
                reviewedPr.Lines[0].Id,
                multiQuoteSupplier.Id,
                productId,
                "Retail batch quote",
                100_000,
                5_000,
                3,
                30,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                true,
                "Selected retail tier quote",
                requesterId)),
            "R21 quote 1");

        // Record Quote 2: Line 1 (Bulk batch) -> 80,000 cents, 14 days lead time, 45 days terms, selected
        SupplierQuotationModel quote2 = RequireSuccess(
            await service.RecordSupplierQuotationAsync(new RecordSupplierQuotationRequest(
                reviewedPr.Id,
                reviewedPr.Lines[1].Id,
                multiQuoteSupplier.Id,
                productId,
                "Bulk batch quote",
                80_000,
                50_000,
                14,
                45,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                true,
                "Selected bulk tier quote",
                requesterId)),
            "R21 quote 2");

        // Convert PR to PO
        PurchaseOrderModel po = RequireSuccess(
            await service.ConvertRequisitionToPurchaseOrderAsync(
                reviewedPr.Id,
                multiQuoteSupplier.Id,
                requisitionLineIds: null,
                deliveryLocationId: null,
                expectedDeliveryDate: null,
                actorUserId: supervisorId),
            "R21 convert PR to PO");

        Require(po.Lines.Count == 2, $"PO must have 2 converted lines, got {po.Lines.Count}");
        PurchaseOrderLineModel poLine1 = po.Lines.First(l => l.RequisitionLineId == reviewedPr.Lines[0].Id);
        PurchaseOrderLineModel poLine2 = po.Lines.First(l => l.RequisitionLineId == reviewedPr.Lines[1].Id);

        Require(poLine1.UnitCostCents == 100_000,
            $"Line 1 unit cost must be exactly 100,000 cents from quote 1, got {poLine1.UnitCostCents} (no quote leakage!).");
        Require(poLine2.UnitCostCents == 80_000,
            $"Line 2 unit cost must be exactly 80,000 cents from quote 2, got {poLine2.UnitCostCents} (no quote leakage!).");

        DateOnly expectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));
        Require(po.ExpectedDeliveryDate == expectedDelivery,
            $"PO expected delivery must be max lead time (14 days = {expectedDelivery}), got {po.ExpectedDeliveryDate}.");
        Require(po.PaymentTermsDays == 30,
            $"PO payment terms must be conservative minimum (30 days from quote 1, not 45 from quote 2), got {po.PaymentTermsDays}.");

        // Setup 2 real invoices and 2 real payments for R21-REVIEW-03 scoped filtering
        // Note: ConvertRequisitionToPurchaseOrderAsync creates the PO already in Approved status.

        ReceivingReportModel p1Rr = RequireSuccess(await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
            po.Id,
            LocationId: null,
            DeliveryReceiptNo: "DR-P1-R21-001",
            SupplierInvoiceRef: null,
            CarrierOrDriver: "Precision Logistics",
            ReceivedDate: DateOnly.FromDateTime(DateTime.UtcNow),
            PostImmediately: true,
            OverdeliveryApproverUserId: null,
            OverdeliveryApprovalReason: null,
            ClientRequestId: "rr-p1-r21-req",
            Notes: null,
            Lines: new[]
            {
                new CreateReceivingReportLineInput(poLine1.Id, 5_000, 5_000, 0, null, null, "BATCH-R21-1", null),
                new CreateReceivingReportLineInput(poLine2.Id, 50_000, 50_000, 0, null, null, "BATCH-R21-2", null)
            },
            ReceivedByUserId: requesterId)), "Create RR for multi-invoice testing");

        SupplierInvoiceModel invAlpha = RequireSuccess(await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            multiQuoteSupplier.Id,
            "SI-R21-ALPHA-01",
            PurchaseOrderId: po.Id,
            ReceivingReportId: p1Rr.Id,
            InvoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            DiscountCents: 0,
            WithholdingTaxCents: 0,
            PostToApIfMatched: true,
            Lines: new[]
            {
                new CreateSupplierInvoiceLineInput(poLine1.Id, p1Rr.Lines.First(l => l.PurchaseOrderLineId == poLine1.Id).Id, productId, "Line 1 Goods", 5_000, 100_000, 0)
            },
            RecordedByUserId: apClerkId)), "Create invAlpha");

        SupplierInvoiceModel invBeta = RequireSuccess(await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            multiQuoteSupplier.Id,
            "SI-R21-BETA-02",
            PurchaseOrderId: po.Id,
            ReceivingReportId: p1Rr.Id,
            InvoiceDate: DateOnly.FromDateTime(DateTime.UtcNow),
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)),
            DiscountCents: 0,
            WithholdingTaxCents: 0,
            PostToApIfMatched: true,
            Lines: new[]
            {
                new CreateSupplierInvoiceLineInput(poLine2.Id, p1Rr.Lines.First(l => l.PurchaseOrderLineId == poLine2.Id).Id, productId, "Line 2 Goods", 50_000, 80_000, 0)
            },
            RecordedByUserId: apClerkId)), "Create invBeta");

        SupplierPaymentModel pmtAlpha = RequireSuccess(await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
            invAlpha.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            120_000,
            "BankTransfer",
            "OperatingAccount",
            "REF-R21-ALPHA-PAY",
            null,
            "BPI Main",
            "Settlement for invAlpha",
            "pmt-alpha-req",
            apClerkId,
            ApprovedByUserId: null)), "Seed pmtAlpha pending");
        pmtAlpha = RequireSuccess(await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
            pmtAlpha.Id,
            supervisorId,
            "YES",
            "Settlement for invAlpha", ActualDisbursementDate: P1TransferDateToday())), "Settle pmtAlpha");

        SupplierPaymentModel pmtBeta = RequireSuccess(await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
            invBeta.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            350_000,
            "Cash",
            "TreasuryFund",
            "REF-R21-BETA-PAY",
            null,
            null,
            "Settlement for invBeta",
            "pmt-beta-req",
            apClerkId,
            ApprovedByUserId: null)), "Seed pmtBeta pending");
        pmtBeta = RequireSuccess(await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
            pmtBeta.Id,
            supervisorId,
            "YES",
            "Settlement for invBeta", ActualDisbursementDate: P1TransferDateToday())), "Settle pmtBeta");

        // 2. HTTP Endpoint contract validations (rejections of missing mandatory identities)
        InMemoryLocalApiSessionStore sessionStore = new();
        SalrynLocalComposition comp = SalrynLocalComposition.Create(paths);
        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = comp,
                    SessionStore = sessionStore,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = new(app.Urls.First(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)));
            LocalApiSession fullSession = CreateSession(sessionStore, "ProcurementManager", new[]
            {
                SalrynPermissions.ReceivingManage,
                SalrynPermissions.ReceivingApproveException,
                SalrynPermissions.PayablesManageInvoices,
                SalrynPermissions.PayablesApproveVariance,
                SalrynPermissions.PayablesRecordPayment,
                SalrynPermissions.PayablesView
            });
            using HttpClient client = CreateClient(baseAddress, fullSession);

            static StringContent Json(object obj) => new(JsonSerializer.Serialize(obj), System.Text.Encoding.UTF8, System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"));

            // Rejection of missing DeliveryReceiptNo in Receiving Report
            var emptyDrPayload = new
            {
                PurchaseOrderId = po.Id,
                DeliveryReceiptNo = "",
                DeliveryReceiptNumber = "",
                CarrierOrDriver = "Direct Delivery",
                PostImmediately = false,
                Lines = new[]
                {
                    new { PurchaseOrderLineId = poLine1.Id, ActualDeliveredQtyX1000 = 5_000, AcceptedQtyX1000 = 5_000, RejectedQtyX1000 = 0 }
                }
            };
            using HttpResponseMessage rrEmptyDrResp = await client.PostAsync("/api/lpi/receiving-reports", Json(emptyDrPayload));
            Require(rrEmptyDrResp.StatusCode == HttpStatusCode.BadRequest,
                $"POST /api/lpi/receiving-reports with missing DR# must return 400 Bad Request, got {rrEmptyDrResp.StatusCode}");

            // Rejection of missing InvoiceNo in Supplier Invoice
            var emptyInvPayload = new
            {
                SupplierId = multiQuoteSupplier.Id,
                SupplierInvoiceNo = "",
                InvoiceNumber = "",
                PurchaseOrderId = po.Id,
                Lines = new[]
                {
                    new { PurchaseOrderLineId = poLine1.Id, InvoicedQtyX1000 = 5_000, InvoicedUnitCostCents = 100_000 }
                }
            };
            using HttpResponseMessage invEmptyNoResp = await client.PostAsync("/api/lpi/invoices", Json(emptyInvPayload));
            Require(invEmptyNoResp.StatusCode == HttpStatusCode.BadRequest,
                $"POST /api/lpi/invoices with missing Invoice# must return 400 Bad Request, got {invEmptyNoResp.StatusCode}");

            // Rejection of missing ApprovalReason in Variance Approval
            var emptyVariancePayload = new
            {
                ApprovalReason = "",
                Notes = "",
                Reason = "",
                PostToApImmediately = true
            };
            using HttpResponseMessage varEmptyReasonResp = await client.PostAsync($"/api/lpi/invoices/{Guid.NewGuid()}/approve-variance", Json(emptyVariancePayload));
            Require(varEmptyReasonResp.StatusCode == HttpStatusCode.BadRequest,
                $"POST /api/lpi/invoices/{{id}}/approve-variance with missing reason must return 400 Bad Request, got {varEmptyReasonResp.StatusCode}");

            // Rejection of missing ReferenceNo in Payment
            var emptyRefPaymentPayload = new
            {
                SupplierInvoiceId = Guid.NewGuid(),
                AmountCents = 50_000,
                PaymentMethod = "BankTransfer",
                ReferenceNo = "",
                ReferenceNumber = ""
            };
            using HttpResponseMessage payEmptyRefResp = await client.PostAsync("/api/lpi/payments", Json(emptyRefPaymentPayload));
            Require(payEmptyRefResp.StatusCode == HttpStatusCode.BadRequest,
                $"POST /api/lpi/payments with missing Reference# must return 400 Bad Request, got {payEmptyRefResp.StatusCode}");

            // Rejection of CashierDrawer disbursement source in Payment
            var drawerPaymentPayload = new
            {
                SupplierInvoiceId = Guid.NewGuid(),
                AmountCents = 50_000,
                PaymentMethod = "Cash",
                DisbursementSource = "CashierShiftDrawer",
                ReferenceNo = "PMT-REF-VALID-01"
            };
            using HttpResponseMessage payDrawerResp = await client.PostAsync("/api/lpi/payments", Json(drawerPaymentPayload));
            Require(payDrawerResp.StatusCode == HttpStatusCode.BadRequest,
                $"POST /api/lpi/payments with CashierShiftDrawer must return 400 Bad Request, got {payDrawerResp.StatusCode}");

            // 3. Verification of Payments scoped query filtering (R21-REVIEW-03)
            // Query by supplierInvoiceId -> must return only invAlpha's payment with exact IDs and amounts
            using HttpResponseMessage getPaySupInvResp = await client.GetAsync($"/api/lpi/payments?supplierInvoiceId={invAlpha.Id}");
            Require(getPaySupInvResp.StatusCode == HttpStatusCode.OK,
                $"GET /api/lpi/payments?supplierInvoiceId must return 200 OK, got {getPaySupInvResp.StatusCode}");
            string alphaJson = await getPaySupInvResp.Content.ReadAsStringAsync();
            using JsonDocument alphaDoc = JsonDocument.Parse(alphaJson);
            Require(alphaDoc.RootElement.ValueKind == JsonValueKind.Array, "Response must be a JSON array.");
            Require(alphaDoc.RootElement.GetArrayLength() == 1, $"Filter by invAlpha must return exactly 1 payment, got {alphaDoc.RootElement.GetArrayLength()}.");
            JsonElement pmtAlphaElem = alphaDoc.RootElement[0];
            Require(Guid.Parse(pmtAlphaElem.GetProperty("id").GetString()!) == pmtAlpha.Id,
                $"Returned payment ID must match pmtAlpha {pmtAlpha.Id}, got {pmtAlphaElem.GetProperty("id").GetString()}");
            Require(pmtAlphaElem.GetProperty("amountCents").GetInt64() == 120_000,
                $"Returned payment amount must be 120,000, got {pmtAlphaElem.GetProperty("amountCents").GetInt64()}");
            Require(pmtAlphaElem.GetProperty("referenceNo").GetString() == "REF-R21-ALPHA-PAY",
                $"Returned reference number must match REF-R21-ALPHA-PAY, got {pmtAlphaElem.GetProperty("referenceNo").GetString()}");
            Require(Guid.Parse(pmtAlphaElem.GetProperty("supplierInvoiceId").GetString()!) == invAlpha.Id,
                "Returned supplier invoice ID must match invAlpha.");

            // Query by invoiceId alias -> must return only invBeta's payment with exact IDs and amounts
            using HttpResponseMessage getPayInvResp = await client.GetAsync($"/api/lpi/payments?invoiceId={invBeta.Id}");
            Require(getPayInvResp.StatusCode == HttpStatusCode.OK,
                $"GET /api/lpi/payments?invoiceId must return 200 OK, got {getPayInvResp.StatusCode}");
            string betaJson = await getPayInvResp.Content.ReadAsStringAsync();
            using JsonDocument betaDoc = JsonDocument.Parse(betaJson);
            Require(betaDoc.RootElement.ValueKind == JsonValueKind.Array, "Response must be a JSON array.");
            Require(betaDoc.RootElement.GetArrayLength() == 1, $"Filter by invBeta must return exactly 1 payment, got {betaDoc.RootElement.GetArrayLength()}.");
            JsonElement pmtBetaElem = betaDoc.RootElement[0];
            Require(Guid.Parse(pmtBetaElem.GetProperty("id").GetString()!) == pmtBeta.Id,
                $"Returned payment ID must match pmtBeta {pmtBeta.Id}, got {pmtBetaElem.GetProperty("id").GetString()}");
            Require(pmtBetaElem.GetProperty("amountCents").GetInt64() == 350_000,
                $"Returned payment amount must be 350,000, got {pmtBetaElem.GetProperty("amountCents").GetInt64()}");
            Require(pmtBetaElem.GetProperty("referenceNo").GetString() == "REF-R21-BETA-PAY",
                $"Returned reference number must match REF-R21-BETA-PAY, got {pmtBetaElem.GetProperty("referenceNo").GetString()}");
            Require(Guid.Parse(pmtBetaElem.GetProperty("supplierInvoiceId").GetString()!) == invBeta.Id,
                "Returned supplier invoice ID must match invBeta.");

            // Query by unrelated invoice -> must return empty list (0 payments)
            using HttpResponseMessage getEmptyResp = await client.GetAsync($"/api/lpi/payments?supplierInvoiceId={Guid.NewGuid()}");
            Require(getEmptyResp.StatusCode == HttpStatusCode.OK, "Unrelated invoice query must return 200 OK.");
            using JsonDocument emptyDoc = JsonDocument.Parse(await getEmptyResp.Content.ReadAsStringAsync());
            Require(emptyDoc.RootElement.GetArrayLength() == 0, "Unrelated invoice query must return 0 payments.");

            // Unauthenticated request -> must return 401 Unauthorized
            using HttpClient anonClient = new() { BaseAddress = baseAddress };
            using HttpResponseMessage anonResp = await anonClient.GetAsync($"/api/lpi/payments?supplierInvoiceId={invAlpha.Id}");
            Require(anonResp.StatusCode == HttpStatusCode.Unauthorized,
                $"Unauthenticated GET /api/lpi/payments must return 401 Unauthorized, got {anonResp.StatusCode}");

            // Unauthorized request (session without PayablesView) -> must return 403 Forbidden
            LocalApiSession unauthSession = CreateSession(sessionStore, "ViewerOnly", new[] { SalrynPermissions.ProductsView });
            using HttpClient unauthClient = CreateClient(baseAddress, unauthSession);
            using HttpResponseMessage unauthResp = await unauthClient.GetAsync($"/api/lpi/payments?supplierInvoiceId={invAlpha.Id}");
            Require(unauthResp.StatusCode == HttpStatusCode.Forbidden,
                $"Unauthorized GET /api/lpi/payments without PayablesView must return 403 Forbidden, got {unauthResp.StatusCode}");

            Console.WriteLine("P1-R21-01: Multi-line quotation precision, deterministic payment terms, mandatory identities, and scoped AP payment filtering verified PASS.");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// FINANCIAL-02: Duplicate-product PO/RR line disambiguation, ambiguity rejection, line isolation, and cumulative overbilling protection.
    /// </summary>
    private static async Task TestW5R21DuplicateProductAmbiguityAndFinancialIntegrityAsync(
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid requesterId,
        Guid supervisorId,
        Guid apClerkId,
        Guid resaleProdId)
    {
        // 1. Create a supplier
        LpiSupplier supplier = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-DUPE-PROD-01", "Dupe Product Specialty Roasters", "Ana Tan", "09181234599", "ana@duperoasters.ph",
                "Mandaluyong City", "111-222-333-000", 30, 7, "Duplicate line regression vendor", requesterId)),
            "create dupe supplier");

        // 2. Create PO directly with 2 lines for resaleProdId:
        // Line A: 5 units @ ₱400 (40,000 cents)
        // Line B: 5 units @ ₱500 (50,000 cents)
        PurchaseOrderModel po = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplier.Id,
                SourceRequisitionId: null,
                ExpectedDeliveryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                DeliveryLocationId: null,
                PaymentTermsDays: 30,
                ShippingCents: 0,
                TermsAndConditions: "Standard",
                Notes: "P1 duplicate-product direct purchase fixture",
                SubmitAndApproveImmediately: true,
                Lines: new[]
                {
                    new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item Lot A", PurchaseClassifications.Resale, "pcs", 5_000, 40_000, 0, 0, null),
                    new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item Lot B", PurchaseClassifications.Resale, "pcs", 5_000, 50_000, 0, 0, null)
                },
                ActorUserId: supervisorId)),
            "create dupe PO");

        Require(po.Lines.Count == 2, $"Expected 2 PO lines, got {po.Lines.Count}");
        PurchaseOrderLineModel poLineA = po.Lines[0]; // ₱400
        PurchaseOrderLineModel poLineB = po.Lines[1]; // ₱500
        Require(poLineA.UnitCostCents == 40_000, $"PO Line A cost must be 40000, got {poLineA.UnitCostCents}");
        Require(poLineB.UnitCostCents == 50_000, $"PO Line B cost must be 50000, got {poLineB.UnitCostCents}");

        // 3. Post Receiving Report for both lines
        ReceivingReportModel rr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                po.Id,
                LocationId: null,
                DeliveryReceiptNo: "DR-DUPE-001",
                SupplierInvoiceRef: null,
                CarrierOrDriver: "Fleet-1",
                ReceivedDate: DateOnly.FromDateTime(DateTime.UtcNow),
                PostImmediately: true,
                OverdeliveryApproverUserId: null,
                OverdeliveryApprovalReason: null,
                ClientRequestId: "RR-DUPE-001-REQ",
                Notes: null,
                Lines: new[]
                {
                    new CreateReceivingReportLineInput(poLineA.Id, 5_000, 5_000, 0, null, null, null, null),
                    new CreateReceivingReportLineInput(poLineB.Id, 5_000, 5_000, 0, null, null, null, null)
                },
                ReceivedByUserId: apClerkId)),
            "create dupe RR");

        Require(rr.Lines.Count == 2, $"Expected 2 RR lines, got {rr.Lines.Count}");
        ReceivingReportLineModel rrLineA = rr.Lines.First(l => l.PurchaseOrderLineId == poLineA.Id);
        ReceivingReportLineModel rrLineB = rr.Lines.First(l => l.PurchaseOrderLineId == poLineB.Id);

        // 4. ATTEMPT 1: Ambiguous Invoice Intake (Omits Line IDs, specifies only ProductId)
        // Must FAIL with AmbiguousPoLine error
        Result<SupplierInvoiceModel> ambiguousResult = await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            supplier.Id,
            "INV-AMBIG-001",
            po.Id,
            rr.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            0, 0, true,
            new[]
            {
                new CreateSupplierInvoiceLineInput(null, null, resaleProdId, "P1 Commercial Resale Item", 5_000, 50_000, 0)
            },
            apClerkId));

        Require(ambiguousResult.IsFailure, "Ambiguous invoice intake without explicit line IDs MUST FAIL.");
        Require(ambiguousResult.ErrorDetail?.Code == "Lpi.Payables.AmbiguousPoLine",
            $"Expected error code 'Lpi.Payables.AmbiguousPoLine', got '{ambiguousResult.ErrorDetail?.Code}'");

        // 5. ATTEMPT 2: Mismatched Line Pairing (PO Line A paired with RR Line B)
        // Must FAIL with LinePoMismatch error
        Result<SupplierInvoiceModel> mismatchResult = await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
            supplier.Id,
            "INV-MISMATCH-001",
            po.Id,
            rr.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            0, 0, true,
            new[]
            {
                new CreateSupplierInvoiceLineInput(poLineA.Id, rrLineB.Id, resaleProdId, "P1 Commercial Resale Item", 5_000, 50_000, 0)
            },
            apClerkId));

        Require(mismatchResult.IsFailure, "Mismatched PO and RR line pairing MUST FAIL.");
        Require(mismatchResult.ErrorDetail?.Code == "Lpi.Payables.LinePoMismatch",
            $"Expected error code 'Lpi.Payables.LinePoMismatch', got '{mismatchResult.ErrorDetail?.Code}'");

        // 6. ATTEMPT 3: Valid Disambiguated Invoice for Line B (₱500)
        // Must succeed with Matched status (0 variance against PO Line B's ₱500 cost)
        SupplierInvoiceModel invBeta = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplier.Id,
                "INV-DISAMBIG-BETA-001",
                po.Id,
                rr.Id,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                0, 0, true,
                new[]
                {
                    new CreateSupplierInvoiceLineInput(poLineB.Id, rrLineB.Id, resaleProdId, "P1 Commercial Resale Item", 5_000, 50_000, 0)
                },
                apClerkId)),
            "create disambiguated invoice for Line B");

        Require(invBeta.MatchStatus == SupplierInvoiceMatchStatuses.Matched,
            $"Expected Matched match status for Line B, got {invBeta.MatchStatus}");
        Require(invBeta.TotalInvoiceCents == 250_000,
            $"Expected total invoice 250,000 cents, got {invBeta.TotalInvoiceCents}");

        // 7. ATTEMPT 4: Prove Financial Isolation — Line A is NOT affected or billed by Line B's invoice
        // Line A must still have all 5 units unbilled
        SupplierInvoiceModel invAlpha = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplier.Id,
                "INV-DISAMBIG-ALPHA-001",
                po.Id,
                rr.Id,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                0, 0, true,
                new[]
                {
                    new CreateSupplierInvoiceLineInput(poLineA.Id, rrLineA.Id, resaleProdId, "P1 Commercial Resale Item", 5_000, 45_000, 0)
                },
                apClerkId)),
            "create disambiguated invoice for Line A");

        Require(invAlpha.MatchStatus == SupplierInvoiceMatchStatuses.PriceVariance,
            $"Expected PriceVariance match status for Line A, got {invAlpha.MatchStatus}");
        Require(invAlpha.Status == "VarianceFlagged",
            $"Expected VarianceFlagged status for Line A, got {invAlpha.Status}");

        // Approving Line A's variance does NOT alter Line B's invoice status or balance
        RequireSuccess(
            await service.ApproveInvoiceVarianceAsync(new ApproveInvoiceVarianceRequest(
                invAlpha.Id, "Authorized higher tier cost for lot A", true, supervisorId)),
            "approve variance for Line A");

        SupplierInvoiceModel reloadedBeta = RequireSuccess(
            await service.GetSupplierInvoiceByIdAsync(invBeta.Id),
            "reload invBeta");
        Require(reloadedBeta.MatchStatus == SupplierInvoiceMatchStatuses.Matched,
            "Line B match status must remain Matched after Line A variance approval.");
        Require(reloadedBeta.RemainingBalanceCents == 250_000,
            "Line B remaining balance must remain intact.");

        // 8. ATTEMPT 5: Cumulative Overbilling Prevention on Line B
        SupplierInvoiceModel invBetaDuplicate = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supplier.Id,
                "INV-DISAMBIG-BETA-DUPE",
                po.Id,
                rr.Id,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                0, 0, true,
                new[]
                {
                    new CreateSupplierInvoiceLineInput(poLineB.Id, rrLineB.Id, resaleProdId, "P1 Commercial Resale Item", 5_000, 50_000, 0)
                },
                apClerkId)),
            "attempt duplicate billing on Line B");

        Require(invBetaDuplicate.MatchStatus == SupplierInvoiceMatchStatuses.QtyVariance,
            $"Expected QtyVariance match status for duplicate billing on Line B, got {invBetaDuplicate.MatchStatus}");
        Require(invBetaDuplicate.Status == "VarianceFlagged",
            $"Expected VarianceFlagged status for overbilled invoice, got {invBetaDuplicate.Status}");

        Console.WriteLine("P1-R21-02: Duplicate-product same-PO line ambiguity rejection, line isolation, variance segregation, and cumulative overbilling protection verified PASS.");
    }

    /// <summary>
    /// Issue #76 (Issues #45 and #59): Receiving-report duplicate-line quantity bypass prevention.
    /// Invariants verified over Service, real Local API HTTP, SQLite snapshots, and Host Restart:
    /// - Issue #45: CreateSupplierReturnAsync rejects duplicate ReceivingReportLineId in a single request
    ///   with zero DB writes, zero stock/bucket deductions, zero PO accepted reversals, zero SRET sequence
    ///   advancement, and zero audit entries.
    /// - Issue #59: CreateSupplierInvoiceAsync rejects duplicate ReceivingReportLineId (explicit or resolved)
    ///   in a single request with zero DB writes, zero AP ledger entries, and zero APV sequence advancement.
    /// - Positive control: Legitimate same-SKU rows on distinct ReceivingReportLineId rows succeed in a single
    ///   supplier return and a single supplier invoice with sequential stock/bucket/AP reconciliation.
    /// - Historical cumulative limits remain enforced across sequential returns and invoices.
    /// </summary>
    private static async Task TestIssue45And59DuplicateReceivingLineIntegrityAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-issue45-59-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            _ = RequireValue(composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
                "Issue45/59 Test Store", null, null, null, null, 80, "admin", "Integrity Admin", "StrongI76Admin123!")), "Issue76 bootstrap admin");

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);
            Guid actorId = SeedUser(paths.DatabasePath, "i76-operator", "Issue76 Operator");
            Guid prodSingleId = SeedProduct(paths.DatabasePath, "I76-SKU-SINGLE", "9900007600001", "Issue76 Single-Line Rice 25kg", 10_000, 15_000, 0, 1_000);
            Guid prodMultiId = SeedProduct(paths.DatabasePath, "I76-SKU-MULTI", "9900007600002", "Issue76 Multi-Lot Flour 25kg", 20_000, 30_000, 0, 1_000);

            LpiSupplier supplier = RequireSuccess(await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "I76-SUP-01", "Issue76 Supply Corp", "Manager", "0917000076", "i76@test.local", "Manila", null, 30, 3, null, actorId)),
                "Issue76 supplier");

            // PO #1: 1 line of prodSingle (1000 milliunits @ 10000 cents = 100.00 PHP)
            PurchaseOrderModel poSingle = RequireSuccess(await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), null,
                30, 0, "Standard", "Issue45/59 single RR line fixture", true,
                new[]
                {
                    new CreatePurchaseOrderLineInput(null, prodSingleId, "I76-SKU-SINGLE", "Issue76 Single-Line Rice 25kg",
                        PurchaseClassifications.Resale, "sack", 1_000, 10_000, 0, 0, null)
                }, actorId)), "Issue76 single PO");

            ReceivingReportModel rrSingle = RequireSuccess(await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poSingle.Id, null, "DR-I76-SINGLE", null, "Driver-76", DateOnly.FromDateTime(DateTime.UtcNow),
                true, null, null, "RR-I76-SINGLE", "Single line RR",
                new[]
                {
                    new CreateReceivingReportLineInput(poSingle.Lines[0].Id, 1_000, 1_000, 0, null, null, null, null)
                }, actorId)), "Issue76 single RR");
            Guid rrSingleLineId = rrSingle.Lines.Single().Id;

            // PO #2: 2 distinct PO/RR lines sharing the SAME SKU prodMulti (1000 milliunits each)
            PurchaseOrderModel poMulti = RequireSuccess(await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), null,
                30, 0, "Standard", "Issue45/59 same-SKU multi distinct RR lines", true,
                new[]
                {
                    new CreatePurchaseOrderLineInput(null, prodMultiId, "I76-SKU-MULTI", "Issue76 Multi-Lot Flour Lot A",
                        PurchaseClassifications.Resale, "sack", 1_000, 20_000, 0, 0, null),
                    new CreatePurchaseOrderLineInput(null, prodMultiId, "I76-SKU-MULTI", "Issue76 Multi-Lot Flour Lot B",
                        PurchaseClassifications.Resale, "sack", 1_000, 22_000, 0, 0, null)
                }, actorId)), "Issue76 multi PO");

            ReceivingReportModel rrMulti = RequireSuccess(await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poMulti.Id, null, "DR-I76-MULTI", null, "Driver-76", DateOnly.FromDateTime(DateTime.UtcNow),
                true, null, null, "RR-I76-MULTI", "Multi line RR",
                new[]
                {
                    new CreateReceivingReportLineInput(poMulti.Lines[0].Id, 1_000, 1_000, 0, null, null, null, null),
                    new CreateReceivingReportLineInput(poMulti.Lines[1].Id, 1_000, 1_000, 0, null, null, null, null)
                }, actorId)), "Issue76 multi RR");
            Guid rrMultiLineA = rrMulti.Lines.Single(l => l.PurchaseOrderLineId == poMulti.Lines[0].Id).Id;
            Guid rrMultiLineB = rrMulti.Lines.Single(l => l.PurchaseOrderLineId == poMulti.Lines[1].Id).Id;

            string SnapshotDb()
            {
                using SqliteConnection conn = new($"Data Source={paths.DatabasePath}");
                conn.Open();
                long Scalar(string sql)
                {
                    using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    object? val = cmd.ExecuteScalar();
                    return val is null or DBNull ? 0L : Convert.ToInt64(val);
                }

                return JsonSerializer.Serialize(new
                {
                    returnCount = Scalar("SELECT COUNT(*) FROM supplier_returns"),
                    returnLineCount = Scalar("SELECT COUNT(*) FROM supplier_return_lines"),
                    invoiceCount = Scalar("SELECT COUNT(*) FROM supplier_invoices"),
                    invoiceLineCount = Scalar("SELECT COUNT(*) FROM supplier_invoice_lines"),
                    payableEntryCount = Scalar("SELECT COUNT(*) FROM supplier_payable_entries"),
                    stockMovementCount = Scalar("SELECT COUNT(*) FROM stock_movements"),
                    singleStockX1000 = Scalar($"SELECT stock_on_hand_x1000 FROM products WHERE id = '{prodSingleId:D}'"),
                    multiStockX1000 = Scalar($"SELECT stock_on_hand_x1000 FROM products WHERE id = '{prodMultiId:D}'"),
                    singlePoAcceptedX1000 = Scalar($"SELECT received_accepted_qty_x1000 FROM purchase_order_lines WHERE id = '{poSingle.Lines[0].Id:D}'"),
                    sretSeq = Scalar("SELECT COALESCE((SELECT current_number FROM lpi_document_sequences WHERE prefix='SRET'), 0)"),
                    apvSeq = Scalar("SELECT COALESCE((SELECT current_number FROM lpi_document_sequences WHERE prefix='APV'), 0)"),
                    auditCount = Scalar("SELECT COUNT(*) FROM audit_logs")
                });
            }

            InMemoryLocalApiSessionStore store = new();
            LocalApiSession operatorSession = store.Create(new AuthenticatedUserReadModel(
                actorId, "i76-op", "Issue76 Operator", "Manager", UserStatus.Active,
                new[]
                {
                    SalrynPermissions.InventoryManageReturns,
                    SalrynPermissions.InventoryView,
                    SalrynPermissions.PayablesManageInvoices,
                    SalrynPermissions.PayablesView,
                    SalrynPermissions.ReceivingView
                })
            {
                RoleId = Guid.Parse(Salryn.Infrastructure.Data.Seeding.DefaultSeedData.ManagerRoleId)
            });

            string nonce = "ISSUE76-DISPOSABLE-" + Guid.NewGuid().ToString("N");
            string beforeNegative = SnapshotDb();
            long singleBucketBefore = ReadLocationBucketQty(paths.DatabasePath, prodSingleId, "MAIN-STORE", "Available");
            Require(singleBucketBefore == 1_000, $"Expected initial Available bucket 1000 for single SKU, got {singleBucketBefore}.");

            await using (WebApplication host = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    SessionStore = store,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false,
                    TestHostNonce = nonce
                }).ConfigureAwait(false))
            {
                await host.StartAsync().ConfigureAwait(false);
                string bound = host.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
                using HttpClient client = CreateClient(new Uri(bound), operatorSession);

                // 1. Issue #45 NEGATIVE HTTP: duplicate ReceivingReportLineId (600 + 600 > 1000)
                using (HttpResponseMessage resp = await client.PostAsync("/api/lpi/supplier-returns",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        receivingReportId = rrSingle.Id,
                        reason = "Duplicate RR line over-return exploit test",
                        lines = new[]
                        {
                            new { receivingReportLineId = rrSingleLineId, returnQtyX1000 = 600L, reason = "Part A" },
                            new { receivingReportLineId = rrSingleLineId, returnQtyX1000 = 600L, reason = "Part B" }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    Require(resp.StatusCode == HttpStatusCode.BadRequest,
                        $"Issue45 duplicate RR line must return HTTP 400, got {(int)resp.StatusCode}: {body}");
                    using JsonDocument err = JsonDocument.Parse(body);
                    Require(err.RootElement.GetProperty("code").GetString() == "Lpi.SupplierReturn.DuplicateReceivingLine",
                        $"Expected Lpi.SupplierReturn.DuplicateReceivingLine, got: {body}");
                }
                Require(SnapshotDb() == beforeNegative &&
                        ReadLocationBucketQty(paths.DatabasePath, prodSingleId, "MAIN-STORE", "Available") == singleBucketBefore,
                    "Issue45 duplicate RR line rejection (600+600) must perform zero writes across all tables/buckets/sequences.");

                // 2. Issue #45 NEGATIVE HTTP even when duplicate sum <= accepted (300 + 300 <= 1000)
                using (HttpResponseMessage respSmallDupe = await client.PostAsync("/api/lpi/supplier-returns",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        receivingReportId = rrSingle.Id,
                        reason = "Duplicate RR line contract check",
                        lines = new[]
                        {
                            new { receivingReportLineId = rrSingleLineId, returnQtyX1000 = 300L, reason = "Split 1" },
                            new { receivingReportLineId = rrSingleLineId, returnQtyX1000 = 300L, reason = "Split 2" }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await respSmallDupe.Content.ReadAsStringAsync();
                    Require(respSmallDupe.StatusCode == HttpStatusCode.BadRequest,
                        $"Issue45 duplicate RR line (300+300) must still return HTTP 400, got {(int)respSmallDupe.StatusCode}: {body}");
                    using JsonDocument err = JsonDocument.Parse(body);
                    Require(err.RootElement.GetProperty("code").GetString() == "Lpi.SupplierReturn.DuplicateReceivingLine",
                        $"Expected Lpi.SupplierReturn.DuplicateReceivingLine for 300+300 duplicate, got: {body}");
                }
                Require(SnapshotDb() == beforeNegative,
                    "Issue45 duplicate RR line (300+300) must leave DB untouched.");

                // 3. Issue #59 NEGATIVE HTTP: duplicate explicit ReceivingReportLineId with PostToApIfMatched=true (700 + 700 > 1000)
                using (HttpResponseMessage respInvDupe = await client.PostAsync("/api/lpi/invoices",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        supplierId = supplier.Id,
                        supplierInvoiceNo = "INV-I76-DUPE-EXPLICIT",
                        purchaseOrderId = poSingle.Id,
                        receivingReportId = rrSingle.Id,
                        postToApIfMatched = true,
                        lines = new[]
                        {
                            new { purchaseOrderLineId = poSingle.Lines[0].Id, receivingReportLineId = rrSingleLineId, productId = prodSingleId, itemDescription = "Rice Line 1", invoicedQtyX1000 = 700L, invoicedUnitCostCents = 10_000L, taxCents = 0L },
                            new { purchaseOrderLineId = poSingle.Lines[0].Id, receivingReportLineId = rrSingleLineId, productId = prodSingleId, itemDescription = "Rice Line 2", invoicedQtyX1000 = 700L, invoicedUnitCostCents = 10_000L, taxCents = 0L }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await respInvDupe.Content.ReadAsStringAsync();
                    Require(respInvDupe.StatusCode == HttpStatusCode.BadRequest,
                        $"Issue59 duplicate explicit RR line must return HTTP 400, got {(int)respInvDupe.StatusCode}: {body}");
                    using JsonDocument err = JsonDocument.Parse(body);
                    Require(err.RootElement.GetProperty("code").GetString() == "Lpi.Payables.DuplicateReceivingLine",
                        $"Expected Lpi.Payables.DuplicateReceivingLine, got: {body}");
                }
                Require(SnapshotDb() == beforeNegative,
                    "Issue59 duplicate explicit RR line rejection must perform zero writes and zero AP postings.");

                // 4. Issue #59 NEGATIVE HTTP: duplicate resolved ReceivingReportLineId when ReceivingReportLineId is omitted
                using (HttpResponseMessage respInvResolvedDupe = await client.PostAsync("/api/lpi/invoices",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        supplierId = supplier.Id,
                        supplierInvoiceNo = "INV-I76-DUPE-RESOLVED",
                        purchaseOrderId = poSingle.Id,
                        receivingReportId = rrSingle.Id,
                        postToApIfMatched = true,
                        lines = new[]
                        {
                            new { productId = prodSingleId, itemDescription = "Rice Implicit 1", invoicedQtyX1000 = 600L, invoicedUnitCostCents = 10_000L, taxCents = 0L },
                            new { productId = prodSingleId, itemDescription = "Rice Implicit 2", invoicedQtyX1000 = 600L, invoicedUnitCostCents = 10_000L, taxCents = 0L }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await respInvResolvedDupe.Content.ReadAsStringAsync();
                    Require(respInvResolvedDupe.StatusCode == HttpStatusCode.BadRequest,
                        $"Issue59 duplicate resolved RR line must return HTTP 400, got {(int)respInvResolvedDupe.StatusCode}: {body}");
                    using JsonDocument err = JsonDocument.Parse(body);
                    Require(err.RootElement.GetProperty("code").GetString() == "Lpi.Payables.DuplicateReceivingLine",
                        $"Expected Lpi.Payables.DuplicateReceivingLine on omitted RR line ID, got: {body}");
                }
                Require(SnapshotDb() == beforeNegative,
                    "Issue59 duplicate resolved RR line rejection must leave DB untouched.");

                await host.StopAsync().ConfigureAwait(false);
            }

            // Host Restart Custody Proof: reload same disposable DB and verify zero negative mutations survived
            SalrynLocalComposition restartedComposition = SalrynLocalComposition.Create(paths);
            Require(SnapshotDb() == beforeNegative,
                "Rejected duplicate return/invoice requests must persist unchanged across host restart.");

            await using (WebApplication restartedHost = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = restartedComposition,
                    SessionStore = store,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false,
                    TestHostNonce = nonce
                }).ConfigureAwait(false))
            {
                await restartedHost.StartAsync().ConfigureAwait(false);
                string restartedUrl = restartedHost.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
                using HttpClient client = CreateClient(new Uri(restartedUrl), operatorSession);

                // 5. Issue #45 POSITIVE + CUMULATIVE LIMIT across sequential returns on rrSingleLineId
                using (HttpResponseMessage okRet1 = await client.PostAsync("/api/lpi/supplier-returns",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        receivingReportId = rrSingle.Id,
                        reason = "Partial return 400",
                        lines = new[] { new { receivingReportLineId = rrSingleLineId, returnQtyX1000 = 400L, reason = "Damaged 0.4" } }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await okRet1.Content.ReadAsStringAsync();
                    Require(okRet1.StatusCode == HttpStatusCode.Created,
                        $"Issue45 valid partial return 400 must return HTTP 201, got {(int)okRet1.StatusCode}: {body}");
                }
                Require(ReadStockOnHand(paths.DatabasePath, prodSingleId) == 600 &&
                        ReadLocationBucketQty(paths.DatabasePath, prodSingleId, "MAIN-STORE", "Available") == 600,
                    "Issue45 partial return 400 must leave 600 milliunits on hand and in Available bucket.");

                string afterPartialReturnSnapshot = SnapshotDb();
                using (HttpResponseMessage overRet2 = await client.PostAsync("/api/lpi/supplier-returns",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        receivingReportId = rrSingle.Id,
                        reason = "Exceed cumulative remaining (601 > 600)",
                        lines = new[] { new { receivingReportLineId = rrSingleLineId, returnQtyX1000 = 601L, reason = "Over remaining" } }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await overRet2.Content.ReadAsStringAsync();
                    Require(overRet2.StatusCode == HttpStatusCode.BadRequest,
                        $"Issue45 cumulative over-return (601 > 600) must return HTTP 400, got {(int)overRet2.StatusCode}: {body}");
                    using JsonDocument err = JsonDocument.Parse(body);
                    Require(err.RootElement.GetProperty("code").GetString() == "Lpi.SupplierReturn.InvalidQty",
                        $"Expected Lpi.SupplierReturn.InvalidQty, got: {body}");
                }
                Require(SnapshotDb() == afterPartialReturnSnapshot,
                    "Issue45 cumulative over-return rejection must leave DB untouched.");

                // 6. POSITIVE CONTROL: Same SKU (prodMultiId) across TWO distinct ReceivingReportLineId rows in ONE return and ONE invoice
                using (HttpResponseMessage multiReturnResp = await client.PostAsync("/api/lpi/supplier-returns",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        receivingReportId = rrMulti.Id,
                        reason = "Legitimate same-SKU return across distinct RR lots",
                        lines = new[]
                        {
                            new { receivingReportLineId = rrMultiLineA, returnQtyX1000 = 300L, reason = "Lot A tear" },
                            new { receivingReportLineId = rrMultiLineB, returnQtyX1000 = 400L, reason = "Lot B tear" }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await multiReturnResp.Content.ReadAsStringAsync();
                    Require(multiReturnResp.StatusCode == HttpStatusCode.Created,
                        $"Issue45 positive same-SKU multi-RR-line return must succeed with HTTP 201, got {(int)multiReturnResp.StatusCode}: {body}");
                    using JsonDocument doc = JsonDocument.Parse(body);
                    // 300 * 20_000 / 1000 + 400 * 22_000 / 1000 = 6_000 + 8_800 = 14_800 cents
                    Require(doc.RootElement.GetProperty("totalReturnAmountCents").GetInt64() == 14_800,
                        $"Expected multi-lot return total 14800 cents, got: {body}");
                }
                Require(ReadStockOnHand(paths.DatabasePath, prodMultiId) == 1_300 &&
                        ReadLocationBucketQty(paths.DatabasePath, prodMultiId, "MAIN-STORE", "Available") == 1_300,
                    $"Same-SKU multi-RR-line return (300+400 from 2000) must sequentially deduct to 1300 in both product stock and Available bucket; actual stock={ReadStockOnHand(paths.DatabasePath, prodMultiId)}, bucket={ReadLocationBucketQty(paths.DatabasePath, prodMultiId, "MAIN-STORE", "Available")}.");

                using (HttpResponseMessage multiInvResp = await client.PostAsync("/api/lpi/invoices",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        supplierId = supplier.Id,
                        supplierInvoiceNo = "INV-I76-MULTI-DISTINCT",
                        purchaseOrderId = poMulti.Id,
                        receivingReportId = rrMulti.Id,
                        postToApIfMatched = true,
                        lines = new[]
                        {
                            new { purchaseOrderLineId = poMulti.Lines[0].Id, receivingReportLineId = rrMultiLineA, productId = prodMultiId, itemDescription = "Flour Lot A", invoicedQtyX1000 = 700L, invoicedUnitCostCents = 20_000L, taxCents = 0L },
                            new { purchaseOrderLineId = poMulti.Lines[1].Id, receivingReportLineId = rrMultiLineB, productId = prodMultiId, itemDescription = "Flour Lot B", invoicedQtyX1000 = 600L, invoicedUnitCostCents = 22_000L, taxCents = 0L }
                        }
                    }), System.Text.Encoding.UTF8, "application/json")))
                {
                    string body = await multiInvResp.Content.ReadAsStringAsync();
                    Require(multiInvResp.StatusCode == HttpStatusCode.Created,
                        $"Issue59 positive same-SKU multi-RR-line invoice must succeed with HTTP 201, got {(int)multiInvResp.StatusCode}: {body}");
                    using JsonDocument doc = JsonDocument.Parse(body);
                    // 700 * 20_000 / 1000 + 600 * 22_000 / 1000 = 14_000 + 13_200 = 27_200 cents
                    Require(doc.RootElement.GetProperty("matchStatus").GetString() == SupplierInvoiceMatchStatuses.Matched &&
                            doc.RootElement.GetProperty("status").GetString() == "PostedOpen" &&
                            doc.RootElement.GetProperty("totalInvoiceCents").GetInt64() == 27_200,
                        $"Expected Matched + PostedOpen + 27200 cents for distinct RR-line same-SKU invoice, got: {body}");
                }

                await restartedHost.StopAsync().ConfigureAwait(false);
            }

            Console.WriteLine("Issue #76 (#45 & #59): Duplicate RR-line rejection (zero writes/sequences) + host restart + same-SKU distinct RR-line positive controls verified PASS.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    private static async Task TestIssue44SupplierReturnInvoiceLineageGuardsAsync(
        SalrynLocalComposition composition,
        SqliteProcureToPayService service,
        DbPaths paths,
        Guid apClerkId,
        Guid receiverId,
        Guid supervisorId,
        Guid resaleProdId,
        Guid nonResaleProdId)
    {
        LpiSupplier supA = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-ISS44-A", "Issue 44 Vendor Alpha", "Ana Alpha", "09171110001", "a@iss44.ph",
                "Makati", "111-111-111-000", 30, 5, "Primary supplier", supervisorId)),
            "create supA");
        LpiSupplier supB = RequireSuccess(
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-ISS44-B", "Issue 44 Vendor Beta", "Ben Beta", "09171110002", "b@iss44.ph",
                "Pasig", "222-222-222-000", 30, 5, "Cross-supplier negative fixture", supervisorId)),
            "create supB");

        PurchaseOrderModel poA1 = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, 30, 0, "Std", "Issue 44 PO A1", true,
                new[] { new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 8_000, 50_000, 0, 0, null) },
                supervisorId)),
            "create poA1");
        PurchaseOrderModel poA2 = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, 30, 0, "Std", "Issue 44 PO A2", true,
                new[] { new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 2_000, 50_000, 0, 0, null) },
                supervisorId)),
            "create poA2");
        PurchaseOrderModel poB1 = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supB.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, 30, 0, "Std", "Issue 44 PO B1", true,
                new[] { new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 4_000, 50_000, 0, 0, null) },
                supervisorId)),
            "create poB1");

        ReceivingReportModel rrA1a = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poA1.Id, null, "DR-44-A1A", null, "Truck-1", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "rr-44-a1a", null,
                new[] { new CreateReceivingReportLineInput(poA1.Lines[0].Id, 6_000, 6_000, 0, null, null, null, null) },
                receiverId)),
            "create rrA1a");
        ReceivingReportModel rrA1b = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poA1.Id, null, "DR-44-A1B", null, "Truck-2", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "rr-44-a1b", null,
                new[] { new CreateReceivingReportLineInput(poA1.Lines[0].Id, 2_000, 2_000, 0, null, null, null, null) },
                receiverId)),
            "create rrA1b");
        ReceivingReportModel rrA2 = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poA2.Id, null, "DR-44-A2", null, "Truck-3", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "rr-44-a2", null,
                new[] { new CreateReceivingReportLineInput(poA2.Lines[0].Id, 2_000, 2_000, 0, null, null, null, null) },
                receiverId)),
            "create rrA2");
        ReceivingReportModel rrB1 = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poB1.Id, null, "DR-44-B1", null, "Truck-4", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "rr-44-b1", null,
                new[] { new CreateReceivingReportLineInput(poB1.Lines[0].Id, 4_000, 4_000, 0, null, null, null, null) },
                receiverId)),
            "create rrB1");

        DateOnly invDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
        SupplierInvoiceModel invA1a = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supA.Id, "SI-44-A1A", poA1.Id, rrA1a.Id, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poA1.Lines[0].Id, rrA1a.Lines[0].Id, resaleProdId, "P1 Commercial Resale Item", 4_000, 50_000, 0) },
                apClerkId)),
            "create invA1a");
        SupplierInvoiceModel invA1bPaid = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supA.Id, "SI-44-A1B", poA1.Id, rrA1b.Id, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poA1.Lines[0].Id, rrA1b.Lines[0].Id, resaleProdId, "P1 Commercial Resale Item", 2_000, 50_000, 0) },
                apClerkId)),
            "create invA1bPaid");
        SupplierInvoiceModel invA2 = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supA.Id, "SI-44-A2", poA2.Id, rrA2.Id, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poA2.Lines[0].Id, rrA2.Lines[0].Id, resaleProdId, "P1 Commercial Resale Item", 2_000, 50_000, 0) },
                apClerkId)),
            "create invA2");
        SupplierInvoiceModel invB1 = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supB.Id, "SI-44-B1", poB1.Id, rrB1.Id, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poB1.Lines[0].Id, rrB1.Lines[0].Id, resaleProdId, "P1 Commercial Resale Item", 4_000, 50_000, 0) },
                apClerkId)),
            "create invB1");

        // Pay invA1bPaid in full so we can also assert non-open / Paid rejection.
        SupplierPaymentModel fullPay = RequireSuccess(
            await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                invA1bPaid.Id, invDate, invA1bPaid.RemainingBalanceCents, "Cash", "CashBox", "REF-44-PAID",
                null, "Main", "Settle invA1b", "pay-44-a1b", apClerkId, null)),
            "record pay invA1b");
        _ = RequireSuccess(
            await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
                fullPay.Id, supervisorId, "YES", "Approved", invDate.ToString("yyyy-MM-dd"))),
            "approve pay invA1b");

        (long SretNext, long RetCount, long LedgerCount, long AuditCount) ReadStateCounters()
        {
            using SqliteConnection c = new($"Data Source={paths.DatabasePath}");
            c.Open();
            static long Scalar(SqliteConnection conn, string sql)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
            }
            return (
                Scalar(c, "SELECT COALESCE((SELECT current_number FROM lpi_document_sequences WHERE prefix = 'SRET'), 0);"),
                Scalar(c, "SELECT COUNT(*) FROM supplier_returns;"),
                Scalar(c, "SELECT COUNT(*) FROM supplier_payable_entries;"),
                Scalar(c, "SELECT COUNT(*) FROM audit_logs WHERE action = 'SUPPLIER_RETURN_POSTED';"));
        }

        long stockBefore = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        var before = ReadStateCounters();

        async Task AssertReturnRejectedAsync(Guid rrId, Guid rrLineId, long qtyX1000, Guid? targetInvId, string expectedCode, string scenario)
        {
            Result<SupplierReturnModel> res = await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                rrId,
                targetInvId,
                $"Negative check: {scenario}",
                new[] { new CreateSupplierReturnLineInput(rrLineId, qtyX1000, scenario) },
                receiverId,
                supervisorId));
            Require(res.IsFailure, $"Issue #44 [{scenario}] MUST FAIL closed.");
            Require(res.ErrorDetail?.Code == expectedCode,
                $"Issue #44 [{scenario}] expected '{expectedCode}', got '{res.ErrorDetail?.Code}' ({res.ErrorDetail?.Message}).");
        }

        // 1. Cross-supplier invoice (supB invoice for supA RR) -> denied
        await AssertReturnRejectedAsync(rrA1a.Id, rrA1a.Lines[0].Id, 1_000, invB1.Id, "Lpi.SupplierReturn.InvoiceSupplierMismatch", "cross-supplier");
        // 2. Same supplier, wrong PO -> denied
        await AssertReturnRejectedAsync(rrA1a.Id, rrA1a.Lines[0].Id, 1_000, invA2.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "wrong-PO");
        // 3. Same supplier, same PO, different RR -> denied
        await AssertReturnRejectedAsync(rrA1a.Id, rrA1a.Lines[0].Id, 1_000, invA1bPaid.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "wrong-RR-same-PO");
        // 4. Matched lineage but return credit exceeds invoice remaining balance (250k > 200k) -> denied
        await AssertReturnRejectedAsync(rrA1a.Id, rrA1a.Lines[0].Id, 5_000, invA1a.Id, "Lpi.SupplierReturn.CreditExceedsInvoiceBalance", "over-balance");
        // 5. Matched RR/PO/Supplier but invoice is already Paid -> denied
        await AssertReturnRejectedAsync(rrA1b.Id, rrA1b.Lines[0].Id, 1_000, invA1bPaid.Id, "Lpi.SupplierReturn.InvoiceNotOpen", "paid-invoice");

        var afterNegative = ReadStateCounters();
        Require(stockBefore == ReadStockOnHand(paths.DatabasePath, resaleProdId), "Issue #44 negative rejections must not alter product stock.");
        Require(afterNegative == before, "Issue #44 negative rejections must not burn SRET sequence or mutate return/ledger/audit rows.");

        SupplierInvoiceModel invB1After = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invB1.Id), "reload invB1");
        SupplierInvoiceModel invA1aAfterNeg = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invA1a.Id), "reload invA1a after negative");
        Require(invB1After.RemainingBalanceCents == invB1.RemainingBalanceCents && invB1After.CreditMemoAppliedCents == 0,
            "Cross-supplier invoice invB1 must remain completely untouched.");
        Require(invA1aAfterNeg.RemainingBalanceCents == invA1a.RemainingBalanceCents && invA1aAfterNeg.CreditMemoAppliedCents == 0,
            "Matched invoice invA1a must remain untouched after over-balance rejection.");

        // 6. Positive case: valid matched return against rrA1a + invA1a (1 unit = 50,000 cents <= 200,000 cents)
        SupplierReturnModel okReturn = RequireSuccess(
            await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                rrA1a.Id,
                invA1a.Id,
                "Defective lot unit from RR A1a",
                new[] { new CreateSupplierReturnLineInput(rrA1a.Lines[0].Id, 1_000, "Damaged packaging") },
                receiverId,
                supervisorId)),
            "create valid matched supplier return");

        SupplierInvoiceModel invA1aAfterPos = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invA1a.Id), "reload invA1a after valid return");
        Require(okReturn.TotalReturnAmountCents == 50_000, $"Expected 50,000 cents return total, got {okReturn.TotalReturnAmountCents}.");
        Require(invA1aAfterPos.CreditMemoAppliedCents == 50_000 && invA1aAfterPos.RemainingBalanceCents == 150_000 && invA1aAfterPos.Status == "PartiallyPaid",
            $"Valid return credit must reduce invA1a balance from 200,000 to 150,000 (status PartiallyPaid), got bal={invA1aAfterPos.RemainingBalanceCents}, credit={invA1aAfterPos.CreditMemoAppliedCents}, status={invA1aAfterPos.Status}.");
        Require(ReadStockOnHand(paths.DatabasePath, resaleProdId) == stockBefore - 1_000, "Valid supplier return must deduct stock by 1_000.");

        // 7. Contradictory header-vs-child RR: header explicitly binds invContradictory to rrA1b while child line binds to rrA1a -> MUST FAIL closed.
        SupplierInvoiceModel invContradictory = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supA.Id, "SI-44-CONTRA", poA1.Id, rrA1b.Id, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poA1.Lines[0].Id, rrA1b.Lines[0].Id, resaleProdId, "P1 Commercial Resale Item", 1_000, 50_000, 0) },
                apClerkId)),
            "create invContradictory");
        using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE supplier_invoice_lines SET receiving_report_line_id = $rrLineA1a WHERE supplier_invoice_id = $invId;";
            cmd.Parameters.AddWithValue("$rrLineA1a", rrA1a.Lines[0].Id.ToString("D"));
            cmd.Parameters.AddWithValue("$invId", invContradictory.Id.ToString("D"));
            Require(cmd.ExecuteNonQuery() == 1, "Contradictory RR line fixture update must affect 1 row.");
        }
        var beforeContra = ReadStateCounters();
        long stockBeforeContra = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        await AssertReturnRejectedAsync(rrA1a.Id, rrA1a.Lines[0].Id, 1_000, invContradictory.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "contradictory-header-vs-child-RR");
        Require(ReadStateCounters() == beforeContra && ReadStockOnHand(paths.DatabasePath, resaleProdId) == stockBeforeContra,
            "Contradictory header-vs-child RR rejection must not mutate SRET, stock, ledger, or audit.");

        // 8. RR-less PO-only invoice coverage: same PO, different product MUST FAIL closed; matching PO line/product MUST SUCCEED.
        PurchaseOrderModel poMulti = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, 30, 0, "Std", "Issue 44 Multi-Line PO", true,
                new[]
                {
                    new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 2_000, 50_000, 0, 0, null),
                    new CreatePurchaseOrderLineInput(null, nonResaleProdId, "P1-PROD-SUPPLY", "P1 Store Consumable Cleaning Fluid", PurchaseClassifications.Resale, "pcs", 2_000, 20_000, 0, 0, null)
                },
                supervisorId)),
            "create poMulti");
        ReceivingReportModel rrMulti = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poMulti.Id, null, "DR-44-MULTI", null, "Truck-M", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "rr-44-multi", null,
                new[]
                {
                    new CreateReceivingReportLineInput(poMulti.Lines[0].Id, 2_000, 2_000, 0, null, null, null, null),
                    new CreateReceivingReportLineInput(poMulti.Lines[1].Id, 2_000, 2_000, 0, null, null, null, null)
                },
                receiverId)),
            "create rrMulti");
        SupplierInvoiceModel invPoOnlyProd1 = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supA.Id, "SI-44-POONLY", poMulti.Id, null, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poMulti.Lines[0].Id, null, resaleProdId, "P1 Commercial Resale Item", 2_000, 50_000, 0) },
                apClerkId)),
            "create invPoOnlyProd1");
        using (SqliteConnection postPoOnlyConn = new($"Data Source={paths.DatabasePath}"))
        {
            postPoOnlyConn.Open();
            using var postCmd = postPoOnlyConn.CreateCommand();
            postCmd.CommandText = "UPDATE supplier_invoices SET status = 'PostedOpen' WHERE id = $id;";
            postCmd.Parameters.AddWithValue("$id", invPoOnlyProd1.Id.ToString("D"));
            Require(postCmd.ExecuteNonQuery() == 1, "PO-only posted invoice fixture update must affect 1 row.");
        }

        var beforePoOnlyNeg = ReadStateCounters();
        long stockSupplyBefore = ReadStockOnHand(paths.DatabasePath, nonResaleProdId);
        await AssertReturnRejectedAsync(rrMulti.Id, rrMulti.Lines[1].Id, 1_000, invPoOnlyProd1.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "po-only-different-product");
        Require(ReadStateCounters() == beforePoOnlyNeg && ReadStockOnHand(paths.DatabasePath, nonResaleProdId) == stockSupplyBefore,
            "Same-PO different-product RR-less invoice rejection must not mutate SRET, stock, ledger, or audit.");

        SupplierReturnModel poOnlyValid = RequireSuccess(
            await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                rrMulti.Id,
                invPoOnlyProd1.Id,
                "Legitimate RR-less PO-only matched product return",
                new[] { new CreateSupplierReturnLineInput(rrMulti.Lines[0].Id, 1_000, "Damaged resale item") },
                receiverId,
                supervisorId)),
            "create valid RR-less PO-only return");
        SupplierInvoiceModel invPoOnlyAfter = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invPoOnlyProd1.Id), "reload invPoOnlyProd1");
        Require(poOnlyValid.TotalReturnAmountCents == 50_000 && invPoOnlyAfter.CreditMemoAppliedCents == 50_000 && invPoOnlyAfter.RemainingBalanceCents == 50_000,
            $"Legitimate PO-only return must reduce balance from 100,000 to 50,000, got bal={invPoOnlyAfter.RemainingBalanceCents}, credit={invPoOnlyAfter.CreditMemoAppliedCents}.");

        // 8b. Matching RR header with NULL child receiving_report_line_id:
        // Multi-product RR (resaleProdId + nonResaleProdId) and real posted A-only invoice with explicit RR header + NULL child RR-line ID.
        PurchaseOrderModel poMultiHdr = RequireSuccess(
            await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                supA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, 30, 0, "Std", "Issue 44 RR-Header No-Child PO", true,
                new[]
                {
                    new CreatePurchaseOrderLineInput(null, resaleProdId, "P1-PROD-RESALE", "P1 Commercial Resale Item", PurchaseClassifications.Resale, "pcs", 2_000, 50_000, 0, 0, null),
                    new CreatePurchaseOrderLineInput(null, nonResaleProdId, "P1-PROD-SUPPLY", "P1 Store Consumable Cleaning Fluid", PurchaseClassifications.Resale, "pcs", 2_000, 20_000, 0, 0, null)
                },
                supervisorId)),
            "create poMultiHdr");
        ReceivingReportModel rrMultiHdr = RequireSuccess(
            await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                poMultiHdr.Id, null, "DR-44-HDR-NULLCHILD", null, "Truck-H", DateOnly.FromDateTime(DateTime.UtcNow), true, null, null, "rr-44-hdr-nullchild", null,
                new[]
                {
                    new CreateReceivingReportLineInput(poMultiHdr.Lines[0].Id, 2_000, 2_000, 0, null, null, null, null),
                    new CreateReceivingReportLineInput(poMultiHdr.Lines[1].Id, 2_000, 2_000, 0, null, null, null, null)
                },
                receiverId)),
            "create rrMultiHdr");
        SupplierInvoiceModel invRrHeaderOnlyProdA = RequireSuccess(
            await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                supA.Id, "SI-44-HDR-AONLY", poMultiHdr.Id, rrMultiHdr.Id, invDate, invDate.AddDays(30), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(poMultiHdr.Lines[0].Id, null, resaleProdId, "P1 Commercial Resale Item", 1_000, 50_000, 0) },
                apClerkId)),
            "create invRrHeaderOnlyProdA");
        Require(invRrHeaderOnlyProdA.Status == "PostedOpen" &&
                invRrHeaderOnlyProdA.ReceivingReportId == rrMultiHdr.Id &&
                invRrHeaderOnlyProdA.Lines.Count == 1 &&
                invRrHeaderOnlyProdA.Lines[0].ReceivingReportLineId == rrMultiHdr.Lines[0].Id,
            "PR #79 auto-resolution precondition failed: newly created invoice with RR header must bind child receiving_report_line_id.");
        using (SqliteConnection legacyNullChildConn = new($"Data Source={paths.DatabasePath}"))
        {
            legacyNullChildConn.Open();
            using var nullCmd = legacyNullChildConn.CreateCommand();
            nullCmd.CommandText = "UPDATE supplier_invoice_lines SET receiving_report_line_id = NULL WHERE supplier_invoice_id = $invId;";
            nullCmd.Parameters.AddWithValue("$invId", invRrHeaderOnlyProdA.Id.ToString("D"));
            Require(nullCmd.ExecuteNonQuery() == 1, "Legacy NULL child receiving_report_line_id fixture update must affect 1 row.");
        }
        invRrHeaderOnlyProdA = RequireSuccess(
            await service.GetSupplierInvoiceByIdAsync(invRrHeaderOnlyProdA.Id),
            "reload invRrHeaderOnlyProdA with NULL child RR line");
        Require(invRrHeaderOnlyProdA.Status == "PostedOpen" &&
                invRrHeaderOnlyProdA.ReceivingReportId == rrMultiHdr.Id &&
                invRrHeaderOnlyProdA.Lines.Count == 1 &&
                !invRrHeaderOnlyProdA.Lines[0].ReceivingReportLineId.HasValue,
            "Issue #44 Case 8b precondition failed: legacy A-only invoice fixture must have matching header RR and NULL child receiving_report_line_id.");

        var beforeHdrNullChild = ReadStateCounters();
        long stockResaleBefore8b = ReadStockOnHand(paths.DatabasePath, resaleProdId);
        long stockSupplyBefore8b = ReadStockOnHand(paths.DatabasePath, nonResaleProdId);
        await AssertReturnRejectedAsync(rrMultiHdr.Id, rrMultiHdr.Lines[1].Id, 1_000, invRrHeaderOnlyProdA.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "rr-header-null-child-uninvoiced-B");
        SupplierInvoiceModel inv8bAfterNegB = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invRrHeaderOnlyProdA.Id), "reload inv8b after uninvoiced B rejection");
        Require(ReadStateCounters() == beforeHdrNullChild &&
                ReadStockOnHand(paths.DatabasePath, resaleProdId) == stockResaleBefore8b &&
                ReadStockOnHand(paths.DatabasePath, nonResaleProdId) == stockSupplyBefore8b &&
                inv8bAfterNegB.RemainingBalanceCents == 50_000 &&
                inv8bAfterNegB.CreditMemoAppliedCents == 0,
            "Uninvoiced Product B return against RR-header-matched A-only invoice (NULL child RR-line ID) must fail closed with zero mutations.");

        // Contradictory child PO-line vs ProductId must also fail closed for both lines
        using (SqliteConnection mixConn = new($"Data Source={paths.DatabasePath}"))
        {
            mixConn.Open();
            using var mixCmd = mixConn.CreateCommand();
            mixCmd.CommandText = "UPDATE supplier_invoice_lines SET product_id = $otherProd WHERE supplier_invoice_id = $invId;";
            mixCmd.Parameters.AddWithValue("$otherProd", nonResaleProdId.ToString("D"));
            mixCmd.Parameters.AddWithValue("$invId", invRrHeaderOnlyProdA.Id.ToString("D"));
            Require(mixCmd.ExecuteNonQuery() == 1, "Contradictory child PO-line/ProductId fixture update must affect 1 row.");
        }
        var beforeContradictoryLine = ReadStateCounters();
        await AssertReturnRejectedAsync(rrMultiHdr.Id, rrMultiHdr.Lines[0].Id, 1_000, invRrHeaderOnlyProdA.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "contradictory-child-poline-vs-product-A");
        await AssertReturnRejectedAsync(rrMultiHdr.Id, rrMultiHdr.Lines[1].Id, 1_000, invRrHeaderOnlyProdA.Id, "Lpi.SupplierReturn.InvoiceLineageMismatch", "contradictory-child-poline-vs-product-B");
        Require(ReadStateCounters() == beforeContradictoryLine &&
                ReadStockOnHand(paths.DatabasePath, resaleProdId) == stockResaleBefore8b &&
                ReadStockOnHand(paths.DatabasePath, nonResaleProdId) == stockSupplyBefore8b,
            "Contradictory child PO-line vs ProductId must fail closed with zero mutations.");

        using (SqliteConnection restoreConn = new($"Data Source={paths.DatabasePath}"))
        {
            restoreConn.Open();
            using var restoreCmd = restoreConn.CreateCommand();
            restoreCmd.CommandText = "UPDATE supplier_invoice_lines SET product_id = $resaleProd WHERE supplier_invoice_id = $invId;";
            restoreCmd.Parameters.AddWithValue("$resaleProd", resaleProdId.ToString("D"));
            restoreCmd.Parameters.AddWithValue("$invId", invRrHeaderOnlyProdA.Id.ToString("D"));
            Require(restoreCmd.ExecuteNonQuery() == 1, "Restore Case 8b product_id must affect 1 row.");
        }

        SupplierReturnModel hdrNullChildValidA = RequireSuccess(
            await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                rrMultiHdr.Id,
                invRrHeaderOnlyProdA.Id,
                "Valid return of invoiced Product A against RR-header A-only invoice",
                new[] { new CreateSupplierReturnLineInput(rrMultiHdr.Lines[0].Id, 1_000, "Defective unit A") },
                receiverId,
                supervisorId)),
            "create valid Product A return against RR-header A-only invoice");
        SupplierInvoiceModel inv8bAfterPosA = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invRrHeaderOnlyProdA.Id), "reload inv8b after valid A return");
        Require(hdrNullChildValidA.TotalReturnAmountCents == 50_000 &&
                inv8bAfterPosA.CreditMemoAppliedCents == 50_000 &&
                inv8bAfterPosA.RemainingBalanceCents == 0 &&
                inv8bAfterPosA.Status == "Paid" &&
                ReadStockOnHand(paths.DatabasePath, resaleProdId) == stockResaleBefore8b - 1_000,
            $"Valid return of invoiced Product A must settle invRrHeaderOnlyProdA (credit={inv8bAfterPosA.CreditMemoAppliedCents}, bal={inv8bAfterPosA.RemainingBalanceCents}, status={inv8bAfterPosA.Status}).");

        // 9. Huge-qty validation and arithmetic overflow in receiving return path -> deterministic HTTP 400 / validation denial before any writes or SRET sequence burn.
        using (SqliteConnection roleConn = new($"Data Source={paths.DatabasePath}"))
        {
            roleConn.Open();
            using var roleCmd = roleConn.CreateCommand();
            roleCmd.CommandText = "UPDATE users SET role_id = $roleId WHERE id = $supId;";
            roleCmd.Parameters.AddWithValue("$roleId", Salryn.Infrastructure.Data.Seeding.DefaultSeedData.ManagerRoleId);
            roleCmd.Parameters.AddWithValue("$supId", supervisorId.ToString("D"));
            Require(roleCmd.ExecuteNonQuery() == 1, "Issue44 HTTP supervisor must have persisted Manager role.");
        }

        string overflowNonce = Guid.NewGuid().ToString("N");
        InMemoryLocalApiSessionStore overflowStore = new();
        LocalApiSession managerSession = overflowStore.Create(new AuthenticatedUserReadModel(
            supervisorId, "p1-supervisor", "Warehouse Supervisor", "Manager", UserStatus.Active,
            new[] { SalrynPermissions.InventoryManageReturns })
        {
            RoleId = Guid.Parse(Salryn.Infrastructure.Data.Seeding.DefaultSeedData.ManagerRoleId)
        });
        var beforeOverflow = ReadStateCounters();
        long stockBeforeOverflow = ReadStockOnHand(paths.DatabasePath, resaleProdId);

        await using (WebApplication host = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                SessionStore = overflowStore,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false,
                TestHostNonce = overflowNonce
            }).ConfigureAwait(false))
        {
            await host.StartAsync().ConfigureAwait(false);
            string bound = host.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            using HttpClient client = CreateClient(new Uri(bound), managerSession);

            // Multi-line huge-qty submission exceeding remainingAccepted -> deterministic HTTP 400
            using HttpResponseMessage hugeMultiLineResp = await client.PostAsync(
                "/api/lpi/supplier-returns",
                new StringContent(JsonSerializer.Serialize(new
                {
                    receivingReportId = rrA1a.Id,
                    supplierInvoiceId = invA1a.Id,
                    reason = "Multi-line huge qty check",
                    lines = new[]
                    {
                        new { receivingReportLineId = rrA1a.Lines[0].Id, returnQtyX1000 = long.MaxValue - 1_000L, reason = "Part 1" },
                        new { receivingReportLineId = rrA1a.Lines[0].Id, returnQtyX1000 = 5_000L, reason = "Part 2" }
                    }
                }), System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
            Require(hugeMultiLineResp.StatusCode == HttpStatusCode.BadRequest,
                $"Issue #44 multi-line huge qty must return HTTP 400, got {(int)hugeMultiLineResp.StatusCode}.");

            // Single line huge qty -> deterministic HTTP 400
            using HttpResponseMessage singleHugeQtyResp = await client.PostAsync(
                "/api/lpi/supplier-returns",
                new StringContent(JsonSerializer.Serialize(new
                {
                    receivingReportId = rrA1a.Id,
                    supplierInvoiceId = invA1a.Id,
                    reason = "Single huge qty check",
                    lines = new[]
                    {
                        new { receivingReportLineId = rrA1a.Lines[0].Id, returnQtyX1000 = long.MaxValue, reason = "Huge qty" }
                    }
                }), System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
            Require(singleHugeQtyResp.StatusCode == HttpStatusCode.BadRequest,
                $"Issue #44 single huge qty must return HTTP 400, got {(int)singleHugeQtyResp.StatusCode}.");
        }

        // Duplicate same RR line is blocked first by Issue #45/#79; genuine cumulative-addition overflow across two distinct same-SKU RR lines hits Lpi.SupplierReturn.InvalidQty.
        Guid extraOverflowLineId = Guid.NewGuid();
        using (SqliteConnection ovSumConn = new($"Data Source={paths.DatabasePath}"))
        {
            ovSumConn.Open();
            using var ovSumCmd = ovSumConn.CreateCommand();
            ovSumCmd.CommandText = "UPDATE receiving_report_lines SET accepted_qty_x1000 = $qty, unit_cost_cents = $cost WHERE id = $lineId;";
            ovSumCmd.Parameters.AddWithValue("$qty", long.MaxValue);
            ovSumCmd.Parameters.AddWithValue("$cost", 0L);
            ovSumCmd.Parameters.AddWithValue("$lineId", rrA1b.Lines[0].Id.ToString("D"));
            Require(ovSumCmd.ExecuteNonQuery() == 1, "Cumulative-sum overflow fixture update must affect 1 row.");

            using var extraLineCmd = ovSumConn.CreateCommand();
            extraLineCmd.CommandText = """
                INSERT INTO receiving_report_lines (
                    id, receiving_report_id, purchase_order_line_id, product_id,
                    item_code, item_description, purchase_classification, unit_name,
                    ordered_qty_x1000, previously_received_qty_x1000, actual_delivered_qty_x1000,
                    accepted_qty_x1000, unit_cost_cents, accepted_line_total_cents
                )
                VALUES (
                    $id, $rrId, $poLineId, $prodId,
                    'P1-PROD-RESALE', 'P1 Commercial Resale Item Extra Lot', 'Resale', 'pcs',
                    $qty, 0, $qty,
                    $qty, 0, 0
                );
                """;
            extraLineCmd.Parameters.AddWithValue("$id", extraOverflowLineId.ToString("D"));
            extraLineCmd.Parameters.AddWithValue("$rrId", rrA1b.Id.ToString("D"));
            extraLineCmd.Parameters.AddWithValue("$poLineId", poA1.Lines[0].Id.ToString("D"));
            extraLineCmd.Parameters.AddWithValue("$prodId", resaleProdId.ToString("D"));
            extraLineCmd.Parameters.AddWithValue("$qty", long.MaxValue);
            Require(extraLineCmd.ExecuteNonQuery() == 1, "Cumulative-sum second distinct RR line fixture insert must affect 1 row.");
        }
        Result<SupplierReturnModel> dupLineOverflowRes = await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
            rrA1b.Id,
            null,
            "Negative check: duplicate-line-before-sum-overflow",
            new[]
            {
                new CreateSupplierReturnLineInput(rrA1b.Lines[0].Id, long.MaxValue - 1_000L, "Part 1"),
                new CreateSupplierReturnLineInput(rrA1b.Lines[0].Id, 5_000L, "Part 2")
            },
            receiverId,
            supervisorId));
        Require(dupLineOverflowRes.IsFailure && dupLineOverflowRes.ErrorDetail?.Code == "Lpi.SupplierReturn.DuplicateReceivingLine",
            $"Duplicate RR line before overflow sum must fail with Lpi.SupplierReturn.DuplicateReceivingLine, got '{dupLineOverflowRes.ErrorDetail?.Code}'.");

        Result<SupplierReturnModel> cumOverflowRes = await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
            rrA1b.Id,
            null,
            "Negative check: cumulative-qty-sum-overflow",
            new[]
            {
                new CreateSupplierReturnLineInput(rrA1b.Lines[0].Id, long.MaxValue - 1_000L, "Part 1"),
                new CreateSupplierReturnLineInput(extraOverflowLineId, 5_000L, "Part 2")
            },
            receiverId,
            supervisorId));
        Require(cumOverflowRes.IsFailure && cumOverflowRes.ErrorDetail?.Code == "Lpi.SupplierReturn.InvalidQty",
            $"Genuine cumulative-addition overflow across distinct RR lines must fail with Lpi.SupplierReturn.InvalidQty, got '{cumOverflowRes.ErrorDetail?.Code}'.");

        using (SqliteConnection ovCleanupConn = new($"Data Source={paths.DatabasePath}"))
        {
            ovCleanupConn.Open();
            using var delCmd = ovCleanupConn.CreateCommand();
            delCmd.CommandText = "DELETE FROM receiving_report_lines WHERE id = $id;";
            delCmd.Parameters.AddWithValue("$id", extraOverflowLineId.ToString("D"));
            _ = delCmd.ExecuteNonQuery();
        }

        // Also force a genuine qty * unitCost multiplication overflow by setting rr_line accepted_qty_x1000 & unit_cost_cents in temp DB
        using (SqliteConnection ovConn = new($"Data Source={paths.DatabasePath}"))
        {
            ovConn.Open();
            using var ovCmd = ovConn.CreateCommand();
            ovCmd.CommandText = "UPDATE receiving_report_lines SET accepted_qty_x1000 = $qty, unit_cost_cents = $cost WHERE id = $lineId;";
            ovCmd.Parameters.AddWithValue("$qty", 1_000_000_000_000L);
            ovCmd.Parameters.AddWithValue("$cost", 10_000_000L);
            ovCmd.Parameters.AddWithValue("$lineId", rrA1b.Lines[0].Id.ToString("D"));
            Require(ovCmd.ExecuteNonQuery() == 1, "Overflow cost fixture update must affect 1 row.");
        }
        await AssertReturnRejectedAsync(rrA1b.Id, rrA1b.Lines[0].Id, 1_000_000_000_000L, null, "Lpi.SupplierReturn.AmountOverflow", "qty-times-cost-overflow");

        Require(ReadStateCounters() == beforeOverflow && ReadStockOnHand(paths.DatabasePath, resaleProdId) == stockBeforeOverflow,
            "Issue #44 overflow rejections must not burn SRET sequence or mutate stock/AP/audit.");

        Console.WriteLine("ISSUE-44: Cross-supplier, wrong-PO, wrong-RR, contradictory header-vs-child RR, PO-only line/product coverage, overflow denial, Paid-status, and over-balance guards verified PASS.");
    }

    #region Helpers

    private static HttpClient CreateClient(Uri baseAddress, LocalApiSession? session = null)
    {
        HttpClient client = new() { BaseAddress = baseAddress };
        if (session is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookieName}={session.SessionId}");
        }
        return client;
    }

    private static LocalApiSession CreateSession(
        InMemoryLocalApiSessionStore sessionStore,
        string roleName,
        IReadOnlyList<string> permissions,
        Guid? userId = null)
    {
        AuthenticatedUserReadModel user = new(
            userId ?? Guid.NewGuid(),
            "p1-user-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            "P1 User",
            roleName,
            UserStatus.Active,
            permissions)
        {
            RoleId = Guid.NewGuid()
        };

        return sessionStore.Create(user);
    }

    private static async Task AssertStatusAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpStatusCode expectedStatus,
        string message)
    {
        using HttpRequestMessage request = new(method, path);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException(
                $"{message}: Expected {(int)expectedStatus} ({expectedStatus}), got {(int)response.StatusCode} ({response.StatusCode}).");
        }
    }

    private static Guid SeedUser(string databasePath, string username, string displayName)
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

    private static Guid SeedProduct(
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
                 is_active,created_at,updated_at)
            VALUES
                ($id,$sku,$barcode,$name,
                 (SELECT id FROM units WHERE is_active=1 ORDER BY name LIMIT 1),
                 $cost,$sell,$stock,$reorder,1,1,1,$at,$at);

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

    private static long ReadLocationBucketQty(string databasePath, Guid productId, string locationCode, string bucketType)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(b.quantity_x1000, 0)
            FROM product_stock_buckets b
            JOIN inventory_locations loc ON loc.id = b.location_id
            WHERE b.product_id = $prodId AND loc.code = $code AND b.bucket_status = $bucket;
            """;
        command.Parameters.AddWithValue("$prodId", productId.ToString("D"));
        command.Parameters.AddWithValue("$code", locationCode);
        command.Parameters.AddWithValue("$bucket", bucketType);
        object? val = command.ExecuteScalar();
        return val is null or DBNull ? 0L : Convert.ToInt64(val);
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed [{result.ErrorDetail.Code}]: {result.ErrorDetail.Message}");
        }
        return result.Value;
    }

    private static T RequireValue<T>(T? value, string area) where T : class
    {
        return value ?? throw new InvalidOperationException($"{area} returned null.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    #endregion
}

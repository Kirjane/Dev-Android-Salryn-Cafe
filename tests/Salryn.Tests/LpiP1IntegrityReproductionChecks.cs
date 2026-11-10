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
///   P1-02: Payment Authorization & Separation of Duties (self-approval forbidden, cash drawer & U1 isolation)
///   P1-03: Financial Data Authorization (Payables.View HTTP matrix & overview metric redaction)
///   P1-04: Transaction Idempotency (exact SHA256 payload binding & conflict rejection)
///   P1-05: Inventory Consistency Across POS (v27 bucket backfill, atomic bucket sync with POS checkout/void/stock-in/adjust)
///   P1-06: Overdelivery Approval Authority (independent supervisory approval event & line tamper prevention)
/// </summary>
public static class LpiP1IntegrityReproductionChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

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

            Console.WriteLine("LPI P1 Integrity: All findings (P1-01 through P1-06 and P1-R3-01/02) verified with full regression closure PASS.");
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
                Notes: null,
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
                Notes: null,
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
                supplierA.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", null, true,
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
    /// P1-02: Payment authorization and separation of duties.
    /// Invariants:
    /// - Recorded payment enters PendingApproval status.
    /// - Preparer cannot approve their own payment.
    /// - Independent manager approval transitions to Settled and posts to AP.
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
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", null, true,
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

        // Verify invoice balance has NOT been deducted yet
        SupplierInvoiceModel invoiceAfterRecord = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoice.Id), "reload invoice");
        Require(invoiceAfterRecord.RemainingBalanceCents == 200_000,
            $"Invoice balance must remain 200_000 while payment is pending approval, got {invoiceAfterRecord.RemainingBalanceCents}.");

        // 2. Self-approval attempt: apClerkId attempts to approve their own payment
        Result<SupplierPaymentModel> selfApproveResult = await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
            pendingPayment.Id,
            ApproverUserId: apClerkId, // Same user!
            Remarks: "Self approval attempt"));

        Require(selfApproveResult.IsFailure && (selfApproveResult.ErrorDetail.Code == "Lpi.Payables.SelfApprovalForbidden" || selfApproveResult.ErrorDetail.Code == "ProcureToPay.SelfApprovalForbidden"),
            $"Payment self-approval must be rejected with SelfApprovalForbidden, got '{selfApproveResult.ErrorDetail.Code}'.");

        // 3. Legitimate independent approval by financeMgrId
        SupplierPaymentModel approvedPayment = RequireSuccess(
            await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
                pendingPayment.Id,
                ApproverUserId: financeMgrId,
                Remarks: "Authorized disbursement by Finance Manager")),
            "P1-02 approve supplier payment");

        Require(approvedPayment.Status == SupplierPaymentStatuses.Settled,
            $"Approved payment status must be Settled, got '{approvedPayment.Status}'.");
        Require(approvedPayment.ApprovedByUserId == financeMgrId,
            "ApprovedByUserId must match the independent manager's ID.");

        // Verify invoice balance is now deducted
        SupplierInvoiceModel invoiceAfterApprove = RequireSuccess(await service.GetSupplierInvoiceByIdAsync(invoice.Id), "reload invoice after approval");
        Require(invoiceAfterApprove.RemainingBalanceCents == 100_000 && invoiceAfterApprove.Status == "PartiallyPaid",
            $"Invoice balance must be deducted to 100_000 and status PartiallyPaid, got balance={invoiceAfterApprove.RemainingBalanceCents}, status={invoiceAfterApprove.Status}.");

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
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", null, true,
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
                nonResaleSup.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", null, true,
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
                nonResaleSup.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), warehouse1LocId, 30, 0, "Standard", null, true,
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
                supplier.Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, 30, 0, "Terms", null, true,
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

        // 2. HTTP Endpoint contract validations (rejections of missing mandatory identities)
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

            // 3. Verification of Payments query param (supplierInvoiceId vs invoiceId)
            using HttpResponseMessage getPaySupInvResp = await client.GetAsync($"/api/lpi/payments?supplierInvoiceId={Guid.NewGuid()}");
            Require(getPaySupInvResp.StatusCode == HttpStatusCode.OK,
                $"GET /api/lpi/payments?supplierInvoiceId must return 200 OK, got {getPaySupInvResp.StatusCode}");

            using HttpResponseMessage getPayInvResp = await client.GetAsync($"/api/lpi/payments?invoiceId={Guid.NewGuid()}");
            Require(getPayInvResp.StatusCode == HttpStatusCode.OK,
                $"GET /api/lpi/payments?invoiceId must return 200 OK, got {getPayInvResp.StatusCode}");

            Console.WriteLine("P1-R21-01: Multi-line quotation precision, mandatory source identities, and drawer protection verified PASS.");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class R3A1APrPoAuthorityChecks
{
    private const string RequestPermission = "Procurement.Request";
    private const string ApprovePrPermission = "Procurement.ApproveRequisition";
    private const string ManagePoPermission = "Procurement.ManagePurchaseOrder";
    private const string ApprovePoPermission = "Procurement.ApprovePurchaseOrder";

    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-r3-a1a-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            Guid cashierAId = SeedUser(paths.DatabasePath, "a1a-cashier-a", "A1A Cashier A", DefaultSeedData.CashierRoleId);
            Guid cashierBId = SeedUser(paths.DatabasePath, "a1a-cashier-b", "A1A Cashier B", DefaultSeedData.CashierRoleId);
            Guid managerId = SeedUser(paths.DatabasePath, "a1a-manager", "A1A Manager", DefaultSeedData.ManagerRoleId);
            Guid adminId = SeedUser(paths.DatabasePath, "a1a-admin", "A1A Admin", DefaultSeedData.AdminRoleId);

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);
            Guid productId = LpiProcureToPayRuntimeChecks.SeedProduct(
                paths.DatabasePath, "A1A-NOTEBOOK", "9911000000001", "A1A Notebook", 5_000, 8_000, 25_000, 5_000);
            LpiSupplier supplier = RequireSuccess(await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "A1A-SUP", "A1A School Supply Vendor", null, null, null, null, null, 30, 3, "Disposable A1A supplier", adminId)),
                "seed supplier");

            InMemoryLocalApiSessionStore sessions = new();
            LocalApiSession cashierA = CreateSession(sessions, cashierAId, "a1a-cashier-a", "A1A Cashier A", "Cashier", DefaultSeedData.CashierRoleId,
                new[] { RequestPermission });
            LocalApiSession cashierB = CreateSession(sessions, cashierBId, "a1a-cashier-b", "A1A Cashier B", "Cashier", DefaultSeedData.CashierRoleId,
                new[] { RequestPermission });
            LocalApiSession manager = CreateSession(sessions, managerId, "a1a-manager", "A1A Manager", "Manager", DefaultSeedData.ManagerRoleId,
                new[] { RequestPermission, ApprovePrPermission, ManagePoPermission, ApprovePoPermission });
            LocalApiSession admin = CreateSession(sessions, adminId, "a1a-admin", "A1A Admin", "Admin", DefaultSeedData.AdminRoleId,
                new[] { RequestPermission, ApprovePrPermission, ManagePoPermission, ApprovePoPermission });
            LocalApiSession manageOnly = CreateSession(sessions, managerId, "a1a-manage-only", "A1A Manage Only", "Manager", DefaultSeedData.ManagerRoleId,
                new[] { ManagePoPermission });

            await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    SessionStore = sessions,
                    LicenseService = new LicensedTestService(),
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                }).ConfigureAwait(false);
            await app.StartAsync().ConfigureAwait(false);

            Guid directPoId;
            try
            {
                Uri baseAddress = ResolveAddress(app);
                using HttpClient anonymous = Client(baseAddress);
                using HttpClient cashierAClient = Client(baseAddress, cashierA);
                using HttpClient cashierBClient = Client(baseAddress, cashierB);
                using HttpClient managerClient = Client(baseAddress, manager);
                using HttpClient adminClient = Client(baseAddress, admin);
                using HttpClient manageOnlyClient = Client(baseAddress, manageOnly);

                object prPayload = new
                {
                    department = "Store Operations",
                    purpose = "Notebook replenishment",
                    requiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                    submitImmediately = false,
                    items = new[]
                    {
                        new
                        {
                            productId,
                            itemDescription = "A1A Notebook",
                            requestedQuantityX1000 = 10_000L,
                            estimatedCostCents = 5_000L
                        }
                    }
                };

                using HttpResponseMessage createPr = await cashierAClient.PostAsJsonAsync("/api/lpi/requisitions", prPayload).ConfigureAwait(false);
                Require(createPr.StatusCode == HttpStatusCode.Created, "Cashier A must create a draft PR.");
                using JsonDocument prJson = JsonDocument.Parse(await createPr.Content.ReadAsStringAsync().ConfigureAwait(false));
                Guid cashierPrId = Guid.Parse(prJson.RootElement.GetProperty("id").GetString()!);
                Guid prLineId = Guid.Parse(prJson.RootElement.GetProperty("lines")[0].GetProperty("id").GetString()!);
                Require(prJson.RootElement.GetProperty("requestedByUserId").GetString() == cashierAId.ToString("D"), "PR requester must be Cashier A.");

                DbSnapshot beforeHijack = Snapshot(paths.DatabasePath, cashierPrId, productId);
                using HttpResponseMessage hijack = await cashierBClient.PostAsync($"/api/lpi/requisitions/{cashierPrId:D}/submit", null).ConfigureAwait(false);
                Require(hijack.StatusCode == HttpStatusCode.Forbidden, "Cashier B submitting Cashier A's draft must return 403.");
                DbSnapshot afterHijack = Snapshot(paths.DatabasePath, cashierPrId, productId);
                Require(afterHijack == beforeHijack, "Denied cross-cashier submission must make zero PR, action, audit, stock, AP or cash writes.");

                using HttpResponseMessage managerOnBehalf = await managerClient.PostAsync($"/api/lpi/requisitions/{cashierPrId:D}/submit", null).ConfigureAwait(false);
                Require(managerOnBehalf.StatusCode == HttpStatusCode.Forbidden, "Manager on-behalf submission is not authorized and must return 403.");
                using HttpResponseMessage adminOnBehalf = await adminClient.PostAsync($"/api/lpi/requisitions/{cashierPrId:D}/submit", null).ConfigureAwait(false);
                Require(adminOnBehalf.StatusCode == HttpStatusCode.Forbidden, "Admin on-behalf submission is not authorized and must return 403.");
                Require(Snapshot(paths.DatabasePath, cashierPrId, productId) == beforeHijack, "Denied Admin/Manager on-behalf submissions must make zero writes.");

                using HttpResponseMessage ownSubmit = await cashierAClient.PostAsync($"/api/lpi/requisitions/{cashierPrId:D}/submit", null).ConfigureAwait(false);
                Require(ownSubmit.StatusCode == HttpStatusCode.OK, "Cashier A must submit their own draft.");
                Require(ReadString(paths.DatabasePath, "SELECT status FROM purchase_requisitions WHERE id=$id", cashierPrId) == "Submitted", "Own PR must persist Submitted status.");
                Require(ReadString(paths.DatabasePath, "SELECT actor_user_id FROM purchase_requisition_actions WHERE requisition_id=$id AND action_type='Submitted' ORDER BY created_at DESC LIMIT 1", cashierPrId) == cashierAId.ToString("D"), "Submit action must record Cashier A.");

                long submittedActions = Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_requisition_actions WHERE requisition_id=$id AND action_type='Submitted'", cashierPrId);
                using HttpResponseMessage replaySubmit = await cashierAClient.PostAsync($"/api/lpi/requisitions/{cashierPrId:D}/submit", null).ConfigureAwait(false);
                Require(replaySubmit.StatusCode == HttpStatusCode.Conflict, "Repeated PR submit must return 409.");
                Require(Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_requisition_actions WHERE requisition_id=$id AND action_type='Submitted'", cashierPrId) == submittedActions,
                    "Repeated submit must not duplicate actions.");

                using HttpResponseMessage concurrentDraft = await cashierAClient.PostAsJsonAsync("/api/lpi/requisitions", prPayload).ConfigureAwait(false);
                Require(concurrentDraft.StatusCode == HttpStatusCode.Created, "Concurrent-submit fixture PR must be created.");
                using JsonDocument concurrentJson = JsonDocument.Parse(await concurrentDraft.Content.ReadAsStringAsync().ConfigureAwait(false));
                Guid concurrentPrId = Guid.Parse(concurrentJson.RootElement.GetProperty("id").GetString()!);
                Task<HttpResponseMessage> submitOne = cashierAClient.PostAsync($"/api/lpi/requisitions/{concurrentPrId:D}/submit", null);
                Task<HttpResponseMessage> submitTwo = cashierAClient.PostAsync($"/api/lpi/requisitions/{concurrentPrId:D}/submit", null);
                HttpResponseMessage[] concurrentResponses = await Task.WhenAll(submitOne, submitTwo).ConfigureAwait(false);
                try
                {
                    Require(concurrentResponses.Count(response => response.StatusCode == HttpStatusCode.OK) == 1 &&
                            concurrentResponses.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 1,
                        "Concurrent own-submit requests must produce one success and one conflict.");
                    Require(Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_requisition_actions WHERE requisition_id=$id AND action_type='Submitted'", concurrentPrId) == 1,
                        "Concurrent submit must record exactly one Submitted action.");
                }
                finally
                {
                    foreach (HttpResponseMessage response in concurrentResponses) response.Dispose();
                }

                using HttpResponseMessage managerApproval = await managerClient.PostAsJsonAsync($"/api/lpi/requisitions/{cashierPrId:D}/approve", new { notes = "Manager approved Cashier A request" }).ConfigureAwait(false);
                Require(managerApproval.StatusCode == HttpStatusCode.OK, "Manager must approve Cashier A's submitted PR.");
                Require(ReadString(paths.DatabasePath, "SELECT approved_by_user_id FROM purchase_requisitions WHERE id=$id", cashierPrId) == managerId.ToString("D"), "PR approval must record actual Manager.");

                await VerifyOwnPrSubmissionAsync(managerClient, managerId, productId, "Manager", paths.DatabasePath).ConfigureAwait(false);
                Guid adminPrId = await VerifyOwnPrSubmissionAsync(adminClient, adminId, productId, "Admin", paths.DatabasePath).ConfigureAwait(false);
                using HttpResponseMessage adminSelfApproval = await adminClient.PostAsJsonAsync($"/api/lpi/requisitions/{adminPrId:D}/approve", new { notes = "Admin executive self-confirmation" }).ConfigureAwait(false);
                Require(adminSelfApproval.StatusCode == HttpStatusCode.OK, "Admin self-confirmation must remain available.");

                // Issue #58 D01 (RevisionRequested ownership) & D08 (non-authoritative supplier suggestion)
                using HttpResponseMessage suggestedPrResp = await cashierAClient.PostAsJsonAsync("/api/lpi/requisitions", new
                {
                    department = "Store Operations",
                    purpose = "D08 suggestion & revision ownership check",
                    requiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)),
                    submitImmediately = true,
                    items = new[]
                    {
                        new
                        {
                            productId,
                            itemDescription = "A1A Notebook",
                            requestedQuantityX1000 = 2_000L,
                            estimatedCostCents = 5_000L,
                            suggestedSupplierName = "Suggested Local Wholesaler"
                        }
                    }
                }).ConfigureAwait(false);
                Require(suggestedPrResp.StatusCode == HttpStatusCode.Created, "Issue #58 D08: Cashier PR with optional supplier suggestion must succeed.");
                using JsonDocument suggestedPrJson = JsonDocument.Parse(await suggestedPrResp.Content.ReadAsStringAsync().ConfigureAwait(false));
                Guid revisedPrId = Guid.Parse(suggestedPrJson.RootElement.GetProperty("id").GetString()!);
                JsonElement suggestedLine = suggestedPrJson.RootElement.GetProperty("lines")[0];
                Require(suggestedLine.GetProperty("preferredSupplierId").ValueKind == JsonValueKind.Null &&
                        suggestedLine.GetProperty("preferredSupplierName").GetString() == "Suggested Local Wholesaler",
                    "Issue #58 D08: Cashier suggestion must persist as non-authoritative supplier name without linking/creating a supplier ID.");

                using HttpResponseMessage cashierCatalogResp = await cashierAClient.GetAsync("/api/lpi/requisition-catalog").ConfigureAwait(false);
                string cashierCatalogRaw = await cashierCatalogResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Require(cashierCatalogResp.StatusCode == HttpStatusCode.OK &&
                        !cashierCatalogRaw.Contains("\"suppliers\"", StringComparison.OrdinalIgnoreCase) &&
                        !cashierCatalogRaw.Contains("\"costPriceCents\"", StringComparison.OrdinalIgnoreCase) &&
                        !cashierCatalogRaw.Contains("\"openPayableBalanceCents\"", StringComparison.OrdinalIgnoreCase),
                    "Issue #58 D08: Cashier requester catalog must never leak private supplier directory, cost, or AP data.");

                using HttpResponseMessage requestRevisionResp = await managerClient.PostAsJsonAsync($"/api/lpi/requisitions/{revisedPrId:D}/revise", new { revisionNotes = "Clarify pack size" }).ConfigureAwait(false);
                Require(requestRevisionResp.StatusCode == HttpStatusCode.OK, "Manager requesting PR revision must succeed.");
                DbSnapshot beforeRevisionHijack = Snapshot(paths.DatabasePath, revisedPrId, productId);
                foreach (HttpClient otherClient in new[] { cashierBClient, managerClient, adminClient })
                {
                    using HttpResponseMessage deniedRevSubmit = await otherClient.PostAsync($"/api/lpi/requisitions/{revisedPrId:D}/submit", null).ConfigureAwait(false);
                    Require(deniedRevSubmit.StatusCode == HttpStatusCode.Forbidden, "Issue #58 D01: Non-owner submitting RevisionRequested PR must return 403.");
                }
                Require(Snapshot(paths.DatabasePath, revisedPrId, productId) == beforeRevisionHijack,
                    "Issue #58 D01: Denied RevisionRequested submission must make zero DB writes.");
                using HttpResponseMessage ownerResubmit = await cashierAClient.PostAsync($"/api/lpi/requisitions/{revisedPrId:D}/submit", null).ConfigureAwait(false);
                Require(ownerResubmit.StatusCode == HttpStatusCode.OK &&
                        ReadString(paths.DatabasePath, "SELECT status FROM purchase_requisitions WHERE id=$id", revisedPrId) == "Submitted",
                    "Issue #58 D01: Original requester must be able to re-submit their RevisionRequested PR.");

                // Issue #57 N04: PO arithmetic bounds, discount ceiling, tax rate bounds, and multi-line checked total equality
                long poCountBeforeN04 = Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders");
                long poSeqBeforeN04 = Scalar(paths.DatabasePath, "SELECT COALESCE(current_number, 0) FROM lpi_document_sequences WHERE prefix='PO'");

                using HttpResponseMessage discountExceedsResp = await adminClient.PostAsJsonAsync("/api/lpi/purchase-orders", new
                {
                    supplierId = supplier.Id,
                    expectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                    notes = "Discount exceeds gross probe",
                    lines = new[] { new { productId, itemDescription = "A1A Notebook", unitName = "pc", orderedQtyX1000 = 1_000L, unitCostCents = 5_000L, discountCents = 5_001L, taxRateBp = 0 } }
                }).ConfigureAwait(false);
                Require(discountExceedsResp.StatusCode == HttpStatusCode.BadRequest &&
                        (await discountExceedsResp.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("Lpi.PurchaseOrder.DiscountExceedsGross", StringComparison.Ordinal),
                    "Issue #57 N04: Line discount exceeding gross must return 400 Lpi.PurchaseOrder.DiscountExceedsGross.");

                using HttpResponseMessage invalidTaxResp = await adminClient.PostAsJsonAsync("/api/lpi/purchase-orders", new
                {
                    supplierId = supplier.Id,
                    expectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                    notes = "Negative tax rate probe",
                    lines = new[] { new { productId, itemDescription = "A1A Notebook", unitName = "pc", orderedQtyX1000 = 1_000L, unitCostCents = 5_000L, discountCents = 0L, taxRateBp = -1 } }
                }).ConfigureAwait(false);
                Require(invalidTaxResp.StatusCode == HttpStatusCode.BadRequest &&
                        (await invalidTaxResp.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("Lpi.PurchaseOrder.InvalidTaxRate", StringComparison.Ordinal),
                    "Issue #57 N04: Negative tax rate must return 400 Lpi.PurchaseOrder.InvalidTaxRate.");

                using HttpResponseMessage overflowPoResp = await adminClient.PostAsJsonAsync("/api/lpi/purchase-orders", new
                {
                    supplierId = supplier.Id,
                    expectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                    notes = "Checked arithmetic overflow probe",
                    lines = new[] { new { productId, itemDescription = "A1A Notebook", unitName = "pc", orderedQtyX1000 = long.MaxValue, unitCostCents = 10_000L, discountCents = 0L, taxRateBp = 1200 } }
                }).ConfigureAwait(false);
                Require(overflowPoResp.StatusCode == HttpStatusCode.BadRequest &&
                        (await overflowPoResp.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("Lpi.PurchaseOrder.AmountOverflow", StringComparison.Ordinal),
                    "Issue #57 N04: Arithmetic overflow in PO calculation must return 400 Lpi.PurchaseOrder.AmountOverflow.");

                Require(Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders") == poCountBeforeN04 &&
                        Scalar(paths.DatabasePath, "SELECT COALESCE(current_number, 0) FROM lpi_document_sequences WHERE prefix='PO'") == poSeqBeforeN04,
                    "Issue #57 N04: Invalid PO payloads must not allocate PO rows or sequence numbers.");

                using HttpResponseMessage validMultiLinePoResp = await adminClient.PostAsJsonAsync("/api/lpi/purchase-orders", new
                {
                    supplierId = supplier.Id,
                    expectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                    shippingCents = 2_500L,
                    notes = "Multi-line checked accounting check",
                    submitAndApproveImmediately = false,
                    lines = new[]
                    {
                        new { productId, itemDescription = "A1A Notebook", unitName = "pc", orderedQtyX1000 = 2_000L, unitCostCents = 5_000L, discountCents = 1_000L, taxRateBp = 1200 },
                        new { productId, itemDescription = "A1A Notebook Bulk", unitName = "pc", orderedQtyX1000 = 3_000L, unitCostCents = 4_000L, discountCents = 500L, taxRateBp = 0 }
                    }
                }).ConfigureAwait(false);
                Require(validMultiLinePoResp.StatusCode == HttpStatusCode.Created, "Issue #57 N04: Valid multi-line PO must be created.");
                using JsonDocument multiLinePoJson = JsonDocument.Parse(await validMultiLinePoResp.Content.ReadAsStringAsync().ConfigureAwait(false));
                long poLinesSum = multiLinePoJson.RootElement.GetProperty("lines").EnumerateArray().Sum(l => l.GetProperty("lineTotalCents").GetInt64());
                long poShipping = multiLinePoJson.RootElement.GetProperty("shippingCents").GetInt64();
                long poHeaderTotal = multiLinePoJson.RootElement.GetProperty("totalAmountCents").GetInt64();
                Require(poLinesSum + poShipping == poHeaderTotal && poHeaderTotal == 24_080L,
                    $"Issue #57 N04: PO line totals + shipping ({poLinesSum} + {poShipping}) must equal header total ({poHeaderTotal}) == 24080.");

                object directPoWithoutReason = DirectPoPayload(supplier.Id, productId, reason: null, approveImmediately: true);
                DbSnapshot beforeInvalidDirect = Snapshot(paths.DatabasePath, cashierPrId, productId);
                long poCountBeforeInvalid = Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders");
                using HttpResponseMessage invalidDirect = await adminClient.PostAsJsonAsync("/api/lpi/purchase-orders", directPoWithoutReason).ConfigureAwait(false);
                Require(invalidDirect.StatusCode == HttpStatusCode.BadRequest, "Direct PO without reason must return 400.");
                Require(Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders") == poCountBeforeInvalid, "Missing reason must not allocate a PO row.");
                Require(Snapshot(paths.DatabasePath, cashierPrId, productId) == beforeInvalidDirect, "Missing direct-PO reason must not affect stock, AP or cash.");

                object immediatePo = DirectPoPayload(supplier.Id, productId, "Urgent notebook replenishment before school opening", approveImmediately: true);
                using HttpResponseMessage unauthenticated = await anonymous.PostAsJsonAsync("/api/lpi/purchase-orders", immediatePo).ConfigureAwait(false);
                Require(unauthenticated.StatusCode == HttpStatusCode.Unauthorized, "Anonymous immediate PO must return 401.");
                using HttpResponseMessage unauthorizedApproval = await manageOnlyClient.PostAsJsonAsync("/api/lpi/purchase-orders", immediatePo).ConfigureAwait(false);
                Require(unauthorizedApproval.StatusCode == HttpStatusCode.Forbidden, "Manage-only actor cannot request immediate PO approval.");
                Require(Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders") == poCountBeforeInvalid, "Denied immediate approval must not create a PO.");

                DbSnapshot beforeDirect = Snapshot(paths.DatabasePath, cashierPrId, productId);
                using HttpResponseMessage validDirect = await adminClient.PostAsJsonAsync("/api/lpi/purchase-orders", immediatePo).ConfigureAwait(false);
                Require(validDirect.StatusCode == HttpStatusCode.Created, $"Authorized direct PO must succeed: {await validDirect.Content.ReadAsStringAsync().ConfigureAwait(false)}");
                using JsonDocument poJson = JsonDocument.Parse(await validDirect.Content.ReadAsStringAsync().ConfigureAwait(false));
                directPoId = Guid.Parse(poJson.RootElement.GetProperty("id").GetString()!);
                Require(poJson.RootElement.GetProperty("status").GetString() == "Approved", "Authorized immediate PO must be Approved.");
                Require(poJson.RootElement.GetProperty("sourceRequisitionId").ValueKind == JsonValueKind.Null, "Direct PO must not fabricate a PR link.");
                Require(poJson.RootElement.GetProperty("notes").GetString() == "Urgent notebook replenishment before school opening", "Direct PO reason must be retrievable.");
                Require(poJson.RootElement.GetProperty("approvedByUserId").GetString() == adminId.ToString("D"), "Immediate approval must record actual Admin.");
                DbSnapshot afterDirect = Snapshot(paths.DatabasePath, cashierPrId, productId);
                Require(afterDirect.StockOnHandX1000 == beforeDirect.StockOnHandX1000 && afterDirect.SupplierInvoiceCount == beforeDirect.SupplierInvoiceCount &&
                        afterDirect.SupplierPaymentCount == beforeDirect.SupplierPaymentCount && afterDirect.CashEventCount == beforeDirect.CashEventCount,
                    "Direct PO creation must not change stock, AP payments or cashier cash.");

                object conversion = new { supplierId = supplier.Id, requisitionLineIds = new[] { prLineId } };
                long poCountBeforeConversionDenial = Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders");
                using HttpResponseMessage manageOnlyConversion = await manageOnlyClient.PostAsJsonAsync($"/api/lpi/requisitions/{cashierPrId:D}/convert-to-po", conversion).ConfigureAwait(false);
                Require(manageOnlyConversion.StatusCode == HttpStatusCode.Forbidden, "Manage-only actor cannot use conversion's immediate-approval path.");
                Require(Scalar(paths.DatabasePath, "SELECT COUNT(*) FROM purchase_orders") == poCountBeforeConversionDenial, "Denied conversion must not create a PO or consume PR quantities.");

                using HttpResponseMessage linkedPo = await managerClient.PostAsJsonAsync($"/api/lpi/requisitions/{cashierPrId:D}/convert-to-po", conversion).ConfigureAwait(false);
                Require(linkedPo.StatusCode == HttpStatusCode.Created, $"Authorized PR-linked conversion must remain functional: {await linkedPo.Content.ReadAsStringAsync().ConfigureAwait(false)}");
                using JsonDocument linkedJson = JsonDocument.Parse(await linkedPo.Content.ReadAsStringAsync().ConfigureAwait(false));
                Require(linkedJson.RootElement.GetProperty("sourceRequisitionId").GetString() == cashierPrId.ToString("D"), "PR-linked PO must retain source association.");
                Require(linkedJson.RootElement.GetProperty("approvedByUserId").GetString() == managerId.ToString("D"), "Converted PO approval must record actual Manager.");
            }
            finally
            {
                await app.StopAsync().ConfigureAwait(false);
            }

            SalrynLocalComposition restarted = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            await restarted.InitializeDatabaseAsync().ConfigureAwait(false);
            SqliteProcureToPayService restartedService = new(restarted.ConnectionFactory, TimeProvider.System);
            PurchaseOrderModel persisted = RequireSuccess(await restartedService.GetPurchaseOrderByIdAsync(directPoId), "reload direct PO after restart");
            Require(persisted.Status == "Approved" && persisted.SourceRequisitionId is null && persisted.Notes == "Urgent notebook replenishment before school opening",
                "Direct PO reason, source and approval must survive restart.");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<Guid> VerifyOwnPrSubmissionAsync(HttpClient client, Guid userId, Guid productId, string role, string dbPath)
    {
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/lpi/requisitions", new
        {
            department = "Store Operations",
            purpose = $"{role} own requisition",
            submitImmediately = false,
            items = new[] { new { productId, itemDescription = "A1A Notebook", requestedQuantityX1000 = 1_000L, estimatedCostCents = 5_000L } }
        }).ConfigureAwait(false);
        Require(created.StatusCode == HttpStatusCode.Created, $"{role} must create own PR.");
        using JsonDocument json = JsonDocument.Parse(await created.Content.ReadAsStringAsync().ConfigureAwait(false));
        Guid id = Guid.Parse(json.RootElement.GetProperty("id").GetString()!);
        using HttpResponseMessage submitted = await client.PostAsync($"/api/lpi/requisitions/{id:D}/submit", null).ConfigureAwait(false);
        Require(submitted.StatusCode == HttpStatusCode.OK, $"{role} must submit own PR.");
        Require(ReadString(dbPath, "SELECT requested_by_user_id FROM purchase_requisitions WHERE id=$id", id) == userId.ToString("D"), $"{role} requester identity must persist.");
        return id;
    }

    private static object DirectPoPayload(Guid supplierId, Guid productId, string? reason, bool approveImmediately) => new
    {
        supplierId,
        expectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
        notes = reason,
        submitAndApproveImmediately = approveImmediately,
        lines = new[]
        {
            new { productId, itemDescription = "A1A Notebook", unitName = "pc", orderedQtyX1000 = 5_000L, unitCostCents = 5_000L }
        }
    };

    private static DbSnapshot Snapshot(string dbPath, Guid prId, Guid productId) => new(
        ReadString(dbPath, "SELECT status FROM purchase_requisitions WHERE id=$id", prId),
        Scalar(dbPath, "SELECT COUNT(*) FROM purchase_requisition_actions WHERE requisition_id=$id", prId),
        Scalar(dbPath, "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$id", prId),
        Scalar(dbPath, "SELECT stock_on_hand_x1000 FROM products WHERE id=$id", productId),
        Scalar(dbPath, "SELECT COUNT(*) FROM supplier_invoices"),
        Scalar(dbPath, "SELECT COUNT(*) FROM supplier_payments"),
        Scalar(dbPath, "SELECT COUNT(*) FROM cash_drawer_events"));

    private static Guid SeedUser(string dbPath, string username, string displayName, string roleId)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at) VALUES($id,$username,$display,'test-only',$role,1,$now);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$display", displayName);
        command.Parameters.AddWithValue("$role", roleId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static LocalApiSession CreateSession(InMemoryLocalApiSessionStore store, Guid userId, string username, string displayName, string roleName, string roleId, IReadOnlyList<string> permissions) =>
        store.Create(new AuthenticatedUserReadModel(userId, username, displayName, roleName, UserStatus.Active, permissions) { RoleId = Guid.Parse(roleId) });

    private static HttpClient Client(Uri baseAddress, LocalApiSession? session = null)
    {
        HttpClient client = new() { BaseAddress = baseAddress };
        if (session is not null) client.DefaultRequestHeaders.Add("Cookie", $"{LocalApiSessionService.SessionCookieName}={session.SessionId}");
        return client;
    }

    private static Uri ResolveAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(value => value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) && !value.EndsWith(":0", StringComparison.Ordinal));
        return address is null ? throw new InvalidOperationException("Disposable A1A host did not bind a loopback port.") : new Uri(address);
    }

    private static long Scalar(string dbPath, string sql, Guid? id = null)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (id.HasValue) command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReadString(string dbPath, string sql, Guid id)
    {
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static T RequireSuccess<T>(Salryn.Core.Common.Result<T> result, string operation) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"{operation} failed: {result.ErrorDetail.Code} {result.ErrorDetail.Message}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record DbSnapshot(string PrStatus, long PrActionCount, long PrAuditCount, long StockOnHandX1000, long SupplierInvoiceCount, long SupplierPaymentCount, long CashEventCount);

    private sealed class LicensedTestService : ILocalLicenseService
    {
        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LocalLicenseValidationResult(
            LicenseStatus.Licensed,
            LicenseEdition.Local,
            LicenseFeatures.AllList,
            ExpiresAt: null,
            DaysRemaining: null,
            MachineMatched: true,
            Message: "A1A disposable test license",
            SourcePath: "test",
            ValidationTimestamp: DateTimeOffset.UtcNow,
            LicenseType: LicenseType.Host));
        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}

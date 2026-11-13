using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.IO;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Security;

namespace Salryn.Tests;

public static class PosS1StoreSettingsIdentityPrinterChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-pos-s1-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            var bootstrap = composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest("Old Store", null, null, null, null, 80, "admin", "Admin", "StrongAdmin123!"));
            Require(bootstrap.IsSuccess, "Bootstrap must succeed.");
            SqliteRepositoryFactory factory = new(composition.ConnectionFactory);
            var reader = factory.CreateStoreSettingsReadRepository();
            var writer = factory.CreateStoreSettingsWriteRepository();
            var before = reader.GetCurrentAsync().GetAwaiter().GetResult();
            Require(before.IsSuccess && before.Value is not null && before.Value.PrinterMode == "Disabled", "Printer mode must default to Disabled.");
            long sales = Count(composition, "sales"), payments = Count(composition, "payments"), movements = Count(composition, "stock_movements");
            string oldSnapshot = "IMMUTABLE OLD RECEIPT";
            var update = new StoreSettingsUpdateRequest(before.Value!.Id, "Kirch Store", "Kirch Trading", "Main Street", "09170000000", "123-456-789-000", "PHP", 80, false, true, "Welcome to Kirch Store", "Thank you", "Disabled");
            Require(writer.UpdateAsync(update).GetAwaiter().GetResult().IsSuccess, "Settings update must succeed.");
            var after = reader.GetCurrentAsync().GetAwaiter().GetResult();
            Require(after.IsSuccess && after.Value?.ReceiptHeader == "Welcome to Kirch Store" && after.Value.ReceiptFooter == "Thank you", "Settings must round-trip.");
            Require(!writer.UpdateAsync(update with { StoreName = "" }).GetAwaiter().GetResult().IsSuccess, "Invalid settings must be rejected.");
            Require(!writer.UpdateAsync(update with { PrinterMode = "PhysicalPrinterProven" }).GetAwaiter().GetResult().IsSuccess, "Unknown printer mode must be rejected.");
            string snapshot = PosReceiptTextRenderer.Render(new PosReceiptSnapshotDraft("S1-1", DateTimeOffset.UtcNow, Guid.NewGuid(), Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, SaleStatus.Completed, Array.Empty<SaleItem>(), Array.Empty<Payment>(), after.Value!.StoreName, after.Value.BusinessName, after.Value.Address, after.Value.ContactNumber, after.Value.ReceiptHeader, after.Value.ReceiptFooter));
            Require(snapshot.Contains("Welcome to Kirch Store") && snapshot.Contains("Thank you"), "New receipt must use saved header/footer.");
            Require(oldSnapshot == "IMMUTABLE OLD RECEIPT", "Historical snapshot must remain unchanged.");
            Require(Count(composition, "sales") == sales && Count(composition, "payments") == payments && Count(composition, "stock_movements") == movements, "Settings must not mutate POS ledgers.");

            string endpoints = File.ReadAllText(Path.Combine(FindRoot().FullName, "src", "Salryn.Api.Local", "LocalStoreSettingsEndpoints.cs"));
            Require(endpoints.Contains("LocalApiPermissions.SettingsManage"), "Settings PUT endpoint must still require Settings.Manage.");
            Require(endpoints.Contains("RequireAuthenticatedSession"), "Settings GET endpoint must allow authenticated operators to load letterhead for document printing.");

            VerifyD07DocumentPdfAndStoreReadAsync(composition).GetAwaiter().GetResult();
            VerifyClientDocumentSourceGuards();
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static async Task VerifyD07DocumentPdfAndStoreReadAsync(SalrynLocalComposition composition)
    {
        Guid adminUserId;
        Guid locationId;
        using (SqliteConnection conn = composition.ConnectionFactory.CreateConnection())
        {
            conn.Open();
            using var uCmd = conn.CreateCommand();
            uCmd.CommandText = "SELECT id FROM users WHERE username = 'admin' LIMIT 1;";
            adminUserId = Guid.Parse(Convert.ToString(uCmd.ExecuteScalar())!);

            using var lCmd = conn.CreateCommand();
            lCmd.CommandText = "SELECT id FROM inventory_locations WHERE code = 'MAIN-STORE' LIMIT 1;";
            locationId = Guid.Parse(Convert.ToString(lCmd.ExecuteScalar())!);
        }

        string hash = SalrynPasswordHasher.HashPassword("OpPass123!").Value!;
        Guid managerUserId = SeedUser(composition.DbPaths.DatabasePath, "doc-manager", "Manager Print", hash, Guid.Parse(DefaultSeedData.ManagerRoleId));
        Guid cashierUserId = SeedUser(composition.DbPaths.DatabasePath, "doc-cashier", "Cashier Print", hash, Guid.Parse(DefaultSeedData.CashierRoleId));
        Guid productId = LpiProcureToPayRuntimeChecks.SeedProduct(composition.DbPaths.DatabasePath, "DOC-APPLE", "880001", "Document Test Apple", 10_000, 15_000, 50_000, 5_000);

        SqliteProcureToPayService p2p = new(composition.ConnectionFactory, TimeProvider.System);
        var supplier = RequireOk(await p2p.CreateSupplierAsync(
            new CreateLpiSupplierRequest("DOC-SUP", "D07 Supplier Trading", "Owner", "09171234567", null, "Makati", "123-000-111", 30, 3, "D07 seed", adminUserId)), "supplier");

        // 1. PR
        var pr = RequireOk(await p2p.CreatePurchaseRequisitionAsync(
            new CreatePurchaseRequisitionRequest("Purchasing", new DateOnly(2026, 12, 1), "High", "Restock fast movers", "PR pdf note", true,
                new[] { new CreatePurchaseRequisitionLineInput(productId, "DOC-APPLE", "Document Test Apple", "Produce", "Resale", "pc", 20_000, 10_000, supplier.Id, null, null) },
                managerUserId)), "PR");
        pr = RequireOk(await p2p.ReviewPurchaseRequisitionAsync(
            new ReviewPurchaseRequisitionRequest(pr.Id, "Approve", "Approved for PDF verification", null, adminUserId)), "approve PR");

        // 2. PO with 40 lines to exercise genuine multipage pagination & repeating headers
        var poLines = Enumerable.Range(1, 40)
            .Select(i => new CreatePurchaseOrderLineInput(null, productId, $"DOC-{i:000}", $"Document Test Line Item #{i:00}", "Resale", "pc", 5_000, 10_000, 0, 0, null))
            .ToArray();
        var po = RequireOk(await p2p.CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequest(supplier.Id, null, new DateOnly(2026, 12, 5), locationId, 30, 0, "Net 30", "Multipage PO test", true, poLines, adminUserId)), "multipage PO");
        po = RequireOk(await p2p.TransitionPurchaseOrderStatusAsync(
            new PurchaseOrderStatusActionRequest(po.Id, "MarkSent", "Dispatched for delivery", adminUserId)), "mark PO sent");

        // 3. RR (Receiving first line partially)
        var rr = RequireOk(await p2p.CreateReceivingReportAsync(
            new CreateReceivingReportRequest(po.Id, locationId, "DR-D07-1", "INV-D07-1", "Driver", new DateOnly(2026, 11, 14), true, null, null, "doc-rr-1", "Accepted",
                new[] { new CreateReceivingReportLineInput(po.Lines[0].Id, 5_000, 5_000, 0, null, null, null, null) }, adminUserId)), "RR");

        // 6. AP Invoice + Pending & Settled Disbursement Vouchers
        var inv = RequireOk(await p2p.CreateSupplierInvoiceAsync(
            new CreateSupplierInvoiceRequest(supplier.Id, "INV-D07-1", po.Id, rr.Id, new DateOnly(2026, 11, 14), new DateOnly(2026, 12, 14), 0, 0, true,
                new[] { new CreateSupplierInvoiceLineInput(po.Lines[0].Id, rr.Lines[0].Id, productId, "Document Test Line Item #01", 5_000, 10_000, 0) }, adminUserId)), "AP invoice");
        var pendingPay = RequireOk(await p2p.RecordSupplierPaymentAsync(
            new RecordSupplierPaymentRequest(inv.Id, new DateOnly(2026, 11, 14), 10_000, "Check", "OperatingBank", "CHK-PEND-1", "000101", "BPI", "Pending payment", "pay-pend-1", managerUserId, null)), "pending AP payment");
        var settledPay = RequireOk(await p2p.RecordSupplierPaymentAsync(
            new RecordSupplierPaymentRequest(inv.Id, new DateOnly(2026, 11, 14), 15_000, "BankTransfer", "OperatingBank", "TXN-SET-1", null, "BDO", "Settled payment", "pay-set-1", adminUserId, null)), "settled AP payment");
        settledPay = RequireOk(await p2p.ApproveSupplierPaymentAsync(
            new ApproveSupplierPaymentRequest(settledPay.Id, adminUserId, "YES", "Settled in D07 check", "2026-11-14")), "approve AP payment");

        // 4 & 5. Customer Quotation & Customer Order
        SqliteCustomerOrdersAndQuotesService quotesOrders = new(composition.ConnectionFactory);
        var quotation = RequireOk(await quotesOrders.CreateQuotationAsync(managerUserId, "Manager Print",
            new CreateCustomerQuotationRequest("Client Alpha", "09170001122", "Estimate note", 0, new DateOnly(2026, 12, 15),
                new[] { new CreateCustomerQuotationLineRequest(productId, "Document Test Apple", "880001", "pc", 4_000, 15_000, 0) })), "customer quotation");
        var order = RequireOk(await quotesOrders.CreateOrderAsync(managerUserId, "Manager Print",
            new CreateCustomerOrderRequest(null, "Client Alpha", "09170001122", new DateOnly(2026, 11, 20), "Order booking",
                new[] { new CreateCustomerOrderLineRequest(productId, "Document Test Apple", "880001", "pc", 4_000, 15_000) })), "customer order");

        // 7. AR Collection Acknowledgment
        SqliteAccountsReceivableService ar = new(composition.ConnectionFactory);
        var customer = RequireOk(await ar.CreateCustomerAsync(new CreateReceivableCustomerRequest("Utang Client D07", "09179998877", "Manila", null, 100_000, cashierUserId)), "AR customer");
        _ = RequireOk(await ar.AdjustAsync(new RecordReceivableAdjustmentRequest(customer.Id, 30_000, true, "D07 opening AR debit", adminUserId)), "AR debit seed");
        var collection = RequireOk(await ar.CollectAsync(new RecordReceivableCollectionRequest(customer.Id, 12_500, "Cash", "Partial ack", cashierUserId, Guid.NewGuid().ToString("N"))), "AR collection");

        // Start Host HTTP pipeline
        InMemoryLocalApiSessionStore sessionStore = new();
        LocalApiSession adminSession = sessionStore.Create(new AuthenticatedUserReadModel(adminUserId, "admin", "Admin", "Admin", UserStatus.Active, DefaultSeedData.AdminPermissions) { RoleId = Guid.Parse(DefaultSeedData.AdminRoleId) });
        LocalApiSession managerSession = sessionStore.Create(new AuthenticatedUserReadModel(managerUserId, "doc-manager", "Manager Print", "Manager", UserStatus.Active, DefaultSeedData.ManagerPermissions) { RoleId = Guid.Parse(DefaultSeedData.ManagerRoleId) });
        LocalApiSession cashierSession = sessionStore.Create(new AuthenticatedUserReadModel(cashierUserId, "doc-cashier", "Cashier Print", "Cashier", UserStatus.Active, DefaultSeedData.CashierPermissions) { RoleId = Guid.Parse(DefaultSeedData.CashierRoleId) });

        await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                SessionStore = sessionStore,
                LicenseService = new DocumentPdfTestLicenseService(),
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }).ConfigureAwait(false);
        await app.StartAsync().ConfigureAwait(false);

        try
        {
            string baseUrl = app.Urls.First(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) && !u.EndsWith(":0", StringComparison.Ordinal));
            using HttpClient anon = new() { BaseAddress = new Uri(baseUrl) };
            using HttpClient cashier = ClientWithSession(baseUrl, cashierSession);
            using HttpClient manager = ClientWithSession(baseUrl, managerSession);
            using HttpClient admin = ClientWithSession(baseUrl, adminSession);

            // Store settings GET/PUT RBAC check
            Require((await anon.GetAsync("/api/settings/store")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous /api/settings/store must return 401.");
            using HttpResponseMessage cashierStoreGet = await cashier.GetAsync("/api/settings/store");
            Require(cashierStoreGet.StatusCode == HttpStatusCode.OK, "Cashier /api/settings/store GET must succeed for print letterhead.");
            using HttpResponseMessage managerStoreGet = await manager.GetAsync("/api/settings/store");
            Require(managerStoreGet.StatusCode == HttpStatusCode.OK, "Manager /api/settings/store GET must succeed for print letterhead.");
            string managerStoreJson = await managerStoreGet.Content.ReadAsStringAsync();
            Require(managerStoreJson.Contains("Kirch Store", StringComparison.Ordinal) && managerStoreJson.Contains("123-456-789-000", StringComparison.Ordinal),
                "Authenticated operator GET /api/settings/store must return configured store letterhead.");

            var settingsPayload = JsonContent.Create(new { storeName = "Hijacked", currencyLabel = "PHP", receiptWidthMm = 80, printerMode = "Disabled" });
            Require((await cashier.PutAsync("/api/settings/store", settingsPayload)).StatusCode == HttpStatusCode.Forbidden, "Cashier PUT /api/settings/store must stay 403.");
            Require((await manager.PutAsync("/api/settings/store", settingsPayload)).StatusCode == HttpStatusCode.Forbidden, "Manager without Settings.Manage PUT /api/settings/store must stay 403.");

            // Snapshot SQLite state before generating all 7 PDFs
            Dictionary<string, long> beforeCounts = CaptureCounts(composition);
            long beforeRemaining = ReadLong(composition, "SELECT remaining_balance_cents FROM supplier_invoices WHERE id = $id;", inv.Id);

            // Fetch all 7 PDFs via HTTP and verify valid PDF bytes + page count + RBAC
            await AssertPdfEndpointAsync(manager, $"/api/lpi/requisitions/{pr.Id}/pdf", minPages: 1, "PR PDF");
            Require((await cashier.GetAsync($"/api/lpi/requisitions/{pr.Id}/pdf")).StatusCode == HttpStatusCode.Forbidden, "Cashier must not download another user's PR PDF.");
            await AssertPdfEndpointAsync(admin, $"/api/lpi/purchase-orders/{po.Id}/pdf", minPages: 2, "Multipage PO PDF");
            await AssertPdfEndpointAsync(manager, $"/api/lpi/receiving-reports/{rr.Id}/pdf", minPages: 1, "RR PDF");
            Require((await cashier.GetAsync($"/api/lpi/receiving-reports/{rr.Id}/pdf")).StatusCode == HttpStatusCode.Forbidden, "Cashier RR PDF must remain 403.");
            await AssertPdfEndpointAsync(cashier, $"/api/customer-quotations/{quotation.Id}/pdf", minPages: 1, "Customer Quotation PDF");
            await AssertPdfEndpointAsync(manager, $"/api/customer-orders/{order.Id}/pdf", minPages: 1, "Customer Order PDF");
            Require((await cashier.GetAsync($"/api/customer-orders/{order.Id}/pdf")).StatusCode == HttpStatusCode.Forbidden, "Cashier Customer Order PDF must remain 403.");
            await AssertPdfEndpointAsync(admin, $"/api/lpi/payments/{pendingPay.Id}/pdf", minPages: 1, "Pending AP Voucher PDF");
            await AssertPdfEndpointAsync(admin, $"/api/lpi/payments/{settledPay.Id}/pdf", minPages: 1, "Settled AP Voucher PDF");
            await AssertPdfEndpointAsync(manager, $"/api/receivables/collections/{collection.Id}/pdf", minPages: 1, "AR Collection Acknowledgment PDF");
            Require((await cashier.GetAsync($"/api/receivables/collections/{collection.Id}/pdf")).StatusCode == HttpStatusCode.Forbidden, "Cashier without Receivables.View AR Acknowledgment PDF must remain 403.");

            // Confirm zero state mutation after PDF generation/download
            Dictionary<string, long> afterCounts = CaptureCounts(composition);
            foreach (var kvp in beforeCounts)
            {
                Require(afterCounts[kvp.Key] == kvp.Value, $"PDF generation mutated table {kvp.Key} ({kvp.Value} -> {afterCounts[kvp.Key]}).");
            }
            long afterRemaining = ReadLong(composition, "SELECT remaining_balance_cents FROM supplier_invoices WHERE id = $id;", inv.Id);
            Require(beforeRemaining == afterRemaining, "PDF generation must never alter invoice balance.");
            Require(beforeRemaining == inv.TotalInvoiceCents - 15_000, "Pending AP payment must remain undeducted from invoice balance.");

            // C4-1: collect -> PDF (200) -> void -> PDF (409 Conflict, zero PDF bytes, unchanged DB counts)
            Result voidRes = await ar.VoidCollectionAsync(collection.Id, adminUserId, "Customer check bounced").ConfigureAwait(false);
            Require(voidRes.IsSuccess, $"VoidCollectionAsync must succeed: {voidRes.Error}");
            Dictionary<string, long> countsBeforeVoidPdf = CaptureCounts(composition);
            using (HttpResponseMessage voidedPdfResp = await manager.GetAsync($"/api/receivables/collections/{collection.Id}/pdf").ConfigureAwait(false))
            {
                byte[] bodyBytes = await voidedPdfResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string bodyText = System.Text.Encoding.UTF8.GetString(bodyBytes);
                Require(voidedPdfResp.StatusCode == HttpStatusCode.Conflict,
                    $"Voided AR collection PDF must return 409 Conflict, got {(int)voidedPdfResp.StatusCode}: {bodyText}");
                Require(!bodyText.StartsWith("%PDF", StringComparison.Ordinal) &&
                        bodyText.Contains("Receivables.CollectionVoided", StringComparison.Ordinal),
                    $"Voided AR collection PDF must return deterministic conflict JSON with zero PDF bytes: {bodyText}");
            }
            Dictionary<string, long> countsAfterVoidPdf = CaptureCounts(composition);
            foreach (var kvp in countsBeforeVoidPdf)
            {
                Require(countsAfterVoidPdf[kvp.Key] == kvp.Value, $"Voided collection PDF check mutated table {kvp.Key}.");
            }

            // C4-2: Quotation PDF server-side integrity validation (corrupted line math, corrupted header total, missing/zero lines)
            async Task AssertQuotationPdfRejectedAsync(Guid qId, string scenario)
            {
                Dictionary<string, long> qBefore = CaptureCounts(composition);
                using HttpResponseMessage resp = await cashier.GetAsync($"/api/customer-quotations/{qId}/pdf").ConfigureAwait(false);
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                string text = System.Text.Encoding.UTF8.GetString(bytes);
                Require(resp.StatusCode == HttpStatusCode.Conflict,
                    $"Quotation PDF [{scenario}] must fail with 409 Conflict, got {(int)resp.StatusCode}: {text}");
                Require(!text.StartsWith("%PDF", StringComparison.Ordinal) &&
                        text.Contains("Quotation.IntegrityViolation", StringComparison.Ordinal),
                    $"Quotation PDF [{scenario}] must return zero PDF bytes and Quotation.IntegrityViolation: {text}");
                Dictionary<string, long> qAfter = CaptureCounts(composition);
                foreach (var kvp in qBefore)
                {
                    Require(qAfter[kvp.Key] == kvp.Value, $"Quotation PDF [{scenario}] mutated table {kvp.Key}.");
                }
            }

            // Corrupted line total
            await using (var conn = composition.ConnectionFactory.CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE customer_quotation_lines SET line_total_cents = line_total_cents + 123 WHERE quotation_id = $id;";
                cmd.Parameters.AddWithValue("$id", quotation.Id.ToString("D"));
                Require(await cmd.ExecuteNonQueryAsync().ConfigureAwait(false) >= 1, "Corrupt quotation line fixture must update row.");
            }
            await AssertQuotationPdfRejectedAsync(quotation.Id, "corrupted-line-total");

            // Restore line total, then corrupt header total
            await using (var conn = composition.ConnectionFactory.CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd1 = conn.CreateCommand();
                cmd1.CommandText = "UPDATE customer_quotation_lines SET line_total_cents = line_total_cents - 123 WHERE quotation_id = $id;";
                cmd1.Parameters.AddWithValue("$id", quotation.Id.ToString("D"));
                await cmd1.ExecuteNonQueryAsync().ConfigureAwait(false);
                await using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = "UPDATE customer_quotations SET total_cents = total_cents + 500 WHERE id = $id;";
                cmd2.Parameters.AddWithValue("$id", quotation.Id.ToString("D"));
                Require(await cmd2.ExecuteNonQueryAsync().ConfigureAwait(false) == 1, "Corrupt quotation header fixture must update row.");
            }
            await AssertQuotationPdfRejectedAsync(quotation.Id, "corrupted-header-total");

            // Delete child lines -> zero-line persisted quotation must fail closed
            await using (var conn = composition.ConnectionFactory.CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM customer_quotation_lines WHERE quotation_id = $id;";
                cmd.Parameters.AddWithValue("$id", quotation.Id.ToString("D"));
                Require(await cmd.ExecuteNonQueryAsync().ConfigureAwait(false) >= 1, "Delete quotation lines fixture must remove rows.");
            }
            await AssertQuotationPdfRejectedAsync(quotation.Id, "missing-zero-lines");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }

        Console.WriteLine("POS-S1 + D07 A4 PDF endpoints (7/7), multipage PO pagination, Pending vs Settled AP voucher, Store header RBAC, and zero-mutation verification PASS.");
    }

    private static async Task AssertPdfEndpointAsync(HttpClient client, string url, int minPages, string label)
    {
        using HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
        Require(response.StatusCode == HttpStatusCode.OK, $"{label} HTTP GET {url} failed with {(int)response.StatusCode}.");
        Require(string.Equals(response.Content.Headers.ContentType?.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase),
            $"{label} content-type must be application/pdf.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        Require(bytes.Length > 100 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-", $"{label} response must start with %PDF-.");
        using MemoryStream ms = new(bytes);
        using var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Require(pdf.PageCount >= minPages, $"{label} expected at least {minPages} page(s) but rendered {pdf.PageCount}.");
    }

    private static void VerifyClientDocumentSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string css = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "index.css"));
        Require(css.Contains("@media print", StringComparison.Ordinal), "index.css must define @media print isolation rules.");
        Require(css.Contains("body[data-modal-open] > #root", StringComparison.Ordinal), "index.css must suppress #root app shell when printing modal documents.");
        Require(css.Contains("[data-dialog-header]", StringComparison.Ordinal), "index.css must suppress Dialog title bar during print.");

        string[] modals =
        {
            "PrintablePurchaseRequisitionModal.tsx",
            "PrintablePurchaseOrderModal.tsx",
            "PrintableReceivingReportModal.tsx",
            "PrintableCustomerQuotationModal.tsx",
            "PrintableCustomerOrderModal.tsx",
            "PrintableChequeDisbursementModal.tsx"
        };
        foreach (string modal in modals)
        {
            string src = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "components", "documents", modal));
            Require(src.Contains("PdfDownloadButton", StringComparison.Ordinal), $"{modal} must expose PdfDownloadButton.");
        }

        string arPage = File.ReadAllText(Path.Combine(root.FullName, "src", "Salryn.App.Web", "src", "pages", "ReceivablesPage.tsx"));
        Require(arPage.Contains("/api/receivables/collections/", StringComparison.Ordinal), "ReceivablesPage must expose AR Collection Acknowledgment PDF download.");
    }

    private static Dictionary<string, long> CaptureCounts(SalrynLocalComposition composition)
    {
        string[] tables =
        {
            "purchase_requisitions", "purchase_orders", "receiving_reports",
            "supplier_invoices", "supplier_payments", "supplier_payable_entries",
            "customer_quotations", "customer_orders", "sales",
            "accounts_receivable_entries", "accounts_receivable_collections"
        };
        return tables.ToDictionary(t => t, t => Count(composition, t));
    }

    private static HttpClient ClientWithSession(string baseUrl, LocalApiSession session)
    {
        HttpClient client = new() { BaseAddress = new Uri(baseUrl) };
        client.DefaultRequestHeaders.Add("Cookie", $"{LocalApiSessionService.SessionCookieName}={session.SessionId}");
        return client;
    }

    private static Guid SeedUser(string dbPath, string username, string displayName, string passwordHash, Guid roleId)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection conn = new($"Data Source={dbPath}");
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
            VALUES ($id, $username, $displayName, $hash, $roleId, 'Active', $now, NULL);
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$hash", passwordHash);
        cmd.Parameters.AddWithValue("$roleId", roleId.ToString("D"));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return id;
    }

    private static long ReadLong(SalrynLocalComposition c, string sql, Guid id)
    {
        using var x = c.ConnectionFactory.CreateConnection();
        x.Open();
        using var q = x.CreateCommand();
        q.CommandText = sql;
        q.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(q.ExecuteScalar());
    }

    private static T RequireOk<T>(Result<T> result, string area)
    {
        if (!result.IsSuccess) throw new InvalidOperationException($"{area}: {result.ErrorDetail.Message}");
        return result.Value;
    }

    private static long Count(SalrynLocalComposition c, string table) { using var x = c.ConnectionFactory.CreateConnection(); x.Open(); using var q = x.CreateCommand(); q.CommandText = $"SELECT COUNT(*) FROM {table};"; return Convert.ToInt64(q.ExecuteScalar()); }
    private static DirectoryInfo FindRoot() { DirectoryInfo? d = new(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "Salryn.sln"))) d = d.Parent; return d ?? throw new InvalidOperationException("Root not found."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class DocumentPdfTestLicenseService : ILocalLicenseService
    {
        private static readonly string[] AllFeatures =
        {
            LicenseFeatures.Pos,
            LicenseFeatures.Reports,
            LicenseFeatures.ReceivablesUtangLedger
        };

        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalLicenseValidationResult(
                LicenseStatus.Licensed,
                LicenseEdition.Local,
                AllFeatures,
                ExpiresAt: null,
                DaysRemaining: null,
                MachineMatched: true,
                Message: "D07 test license",
                SourcePath: "test",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));

        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default) =>
            Task.FromResult(AllFeatures.Contains(feature, StringComparer.Ordinal));
    }
}

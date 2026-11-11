using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Security;

namespace Salryn.Tests;

public static class CeoPrToPoReproductionChecks
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-ceo-repro-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);

            // 1. Seed store settings & app metadata
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO store_settings
                        (id, store_name, business_name, address, contact_number, tin, currency_label,
                         receipt_width_mm, receipt_header, receipt_footer, tax_mode_enabled,
                         shift_mode_enabled, allow_negative_stock, created_at, updated_at)
                    VALUES
                        ($id, 'Salryn Flagship Store', 'Salryn Retail Ph', 'Makati Ave, Metro Manila',
                         '0917-100-0000', '123-456-789-000', 'PHP', 80, NULL, NULL, 1, 1, 0, $now, NULL);

                    INSERT OR REPLACE INTO app_metadata (key, value, updated_at) VALUES ('first_run_required', 'false', $now);
                    """;
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            // 2. Seed products
            Guid productAlphaId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "PROD-ALPHA", "880001", "Coffee Beans 1kg", 40_000, 70_000, 10_000, 5_000);

            // 3. Seed users
            string mgrHash = SalrynPasswordHasher.HashPassword("Manager123!").Value!;
            string cashierHash = SalrynPasswordHasher.HashPassword("Cashier123!").Value!;
            string approverHash = SalrynPasswordHasher.HashPassword("Approver123!").Value!;

            Guid mgrId = SeedUserWithPassword(paths.DatabasePath, "ceo-mgr", "CEO Manager", mgrHash, Guid.Parse(DefaultSeedData.PurchasingOfficerRoleId));
            Guid cashierId = SeedUserWithPassword(paths.DatabasePath, "ceo-cashier", "CEO Cashier", cashierHash, Guid.Parse(DefaultSeedData.CashierRoleId));
            Guid approverId = SeedUserWithPassword(paths.DatabasePath, "ceo-approver", "CEO Approver", approverHash, Guid.Parse(DefaultSeedData.AdminRoleId));

            // 4. Seed supplier
            var supResult = await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-CEOTEST-01", "Apex Roasters Test Corp", "Maria Santos", "09171234501", "orders@apex.ph",
                "Makati City", "123-456-789-000", 30, 5, "Test vendor", approverId));
            if (!supResult.IsSuccess) throw new InvalidOperationException("Failed to seed supplier");
            Guid supplierId = supResult.Value.Id;

            // 5. Create and submit PR
            var prResult = await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                "PR for CEO PR-to-PO Reproduction",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                "High",
                "Restock",
                null,
                true, // submit immediately
                new[]
                {
                    new CreatePurchaseRequisitionLineInput(
                        productAlphaId, "PROD-ALPHA", "Coffee Beans 1kg", "Coffee", "Resale", "kg", 10_000, 40_000, supplierId, null, null)
                },
                mgrId));
            if (!prResult.IsSuccess) throw new InvalidOperationException("Failed to seed PR: " + prResult.ErrorDetail.Message);
            Guid prId = prResult.Value.Id;
            Guid prLineId = prResult.Value.Lines[0].Id;

            // Verify Standard Self-Approval Denial: Purchasing Officer mgrId attempts to self-approve
            var selfApprovePoOfficer = await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                prId, "Approve", "Self approving PR as standard requester", null, mgrId));
            if (selfApprovePoOfficer.IsSuccess || selfApprovePoOfficer.ErrorDetail.Code != "Lpi.Requisition.SelfApprovalDenied")
            {
                throw new InvalidOperationException($"Expected SelfApprovalDenied for standard requester, got {selfApprovePoOfficer.ErrorDetail?.Code}");
            }
            Console.WriteLine("[CEO Repro] Standard Requester self-approval successfully denied (403 Forbidden policy verified).");

            // Verify Authorized Executive Self-Approval: Admin approverId creates their own PR and approves it
            var adminPrResult = await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                "Admin Executive Restock PR",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                "High",
                "Executive store restock",
                null,
                true,
                new[]
                {
                    new CreatePurchaseRequisitionLineInput(
                        productAlphaId, "PROD-ALPHA", "Coffee Beans 1kg", "Coffee", "Resale", "kg", 5_000, 40_000, supplierId, null, null)
                },
                approverId));
            if (!adminPrResult.IsSuccess) throw new InvalidOperationException("Failed to create admin PR");
            Guid adminPrId = adminPrResult.Value.Id;

            var adminSelfApprove = await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                adminPrId, "Approve", "Admin executive self-confirmation", null, approverId));
            if (!adminSelfApprove.IsSuccess || adminSelfApprove.Value.Status != "Approved")
            {
                throw new InvalidOperationException($"Admin executive self-approval failed: {adminSelfApprove.ErrorDetail?.Message}");
            }
            Console.WriteLine("[CEO Repro] Admin executive self-approval successfully approved under policy.");

            // Verify audit trail logged PR_SELF_APPROVED_BY_EXECUTIVE
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand auditCmd = conn.CreateCommand();
                auditCmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE action = 'PR_SELF_APPROVED_BY_EXECUTIVE' AND entity_id = $recId;";
                auditCmd.Parameters.AddWithValue("$recId", adminPrId.ToString("D"));
                long auditCount = (long)auditCmd.ExecuteScalar()!;
                if (auditCount == 0) throw new InvalidOperationException("Audit log for PR_SELF_APPROVED_BY_EXECUTIVE was not recorded!");
                Console.WriteLine("[CEO Repro] PR_SELF_APPROVED_BY_EXECUTIVE audit log verified in SQLite database.");
            }

            // Distinct authorized approval of main test PR
            var approveRes = await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                prId, "Approve", "Approved by CEO Approver", null, approverId));
            if (!approveRes.IsSuccess) throw new InvalidOperationException("Failed to approve PR: " + approveRes.ErrorDetail.Message);

            // 6. Start disposable live Host
            string testToken = $"DISPOSABLE-TEST-{Guid.NewGuid():N}";
            await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    LicenseService = new FakeReproLicenseService(),
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false,
                    TestHostNonce = testToken
                }).ConfigureAwait(false);

            await app.StartAsync().ConfigureAwait(false);

            string? boundUrl = app.Urls.FirstOrDefault(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(boundUrl) || boundUrl.EndsWith(":0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Failed to resolve dynamic loopback port.");
            }

            Console.WriteLine($"[CEO Repro] Disposable Host running on {boundUrl}");

            HttpClient CreateClientForUser() => new(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(boundUrl) };

            async Task<string> LoginAsync(string username, string password)
            {
                using var loginClient = CreateClientForUser();
                var loginResp = await loginClient.PostAsJsonAsync("/api/auth/login", new { username, password });
                if (!loginResp.IsSuccessStatusCode) throw new InvalidOperationException($"Login failed for {username}: {loginResp.StatusCode}");
                var setCookie = loginResp.Headers.GetValues("Set-Cookie").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(setCookie)) throw new InvalidOperationException("No Set-Cookie returned");
                return setCookie.Split(';')[0];
            }

            string approverCookie = await LoginAsync("ceo-approver", "Approver123!");
            string cashierCookie = await LoginAsync("ceo-cashier", "Cashier123!");

            using HttpClient client = CreateClientForUser();

            // -------------------------------------------------------------
            // TEST 1: Cashier attempts to convert PR to PO -> Expect 403 Forbidden
            // -------------------------------------------------------------
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lpi/requisitions/{prId}/convert-to-po"))
            {
                req.Headers.Add("Cookie", cashierCookie);
                req.Content = JsonContent.Create(new
                {
                    supplierId = supplierId,
                    requisitionLineIds = new[] { prLineId }
                });
                var resp = await client.SendAsync(req);
                Console.WriteLine($"[CEO Repro] Test 1 (Cashier 403): Status={(int)resp.StatusCode} {resp.StatusCode}");
                if (resp.StatusCode != HttpStatusCode.Forbidden)
                    throw new InvalidOperationException($"Expected 403 Forbidden for Cashier, got {resp.StatusCode}");
            }

            // -------------------------------------------------------------
            // TEST 2: Blank / Empty Guid Supplier ID -> Reproduces HTTP 400
            // -------------------------------------------------------------
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lpi/requisitions/{prId}/convert-to-po"))
            {
                req.Headers.Add("Cookie", approverCookie);
                req.Content = JsonContent.Create(new
                {
                    supplierId = Guid.Empty,
                    requisitionLineIds = new[] { prLineId }
                });
                var resp = await client.SendAsync(req);
                string body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[CEO Repro] Test 2 (Empty Guid Supplier): Status={(int)resp.StatusCode} Body={body}");
                if (resp.StatusCode != HttpStatusCode.BadRequest)
                    throw new InvalidOperationException($"Expected 400 BadRequest for Empty Supplier, got {resp.StatusCode}");
            }

            // -------------------------------------------------------------
            // TEST 3: Invalid empty string supplier ID -> Reproduces JSON Deserialization HTTP 400
            // -------------------------------------------------------------
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lpi/requisitions/{prId}/convert-to-po"))
            {
                req.Headers.Add("Cookie", approverCookie);
                req.Content = new StringContent(
                    JsonSerializer.Serialize(new { supplierId = "", requisitionLineIds = new[] { prLineId } }),
                    System.Text.Encoding.UTF8,
                    "application/json");
                var resp = await client.SendAsync(req);
                string body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[CEO Repro] Test 3 (Blank string Supplier): Status={(int)resp.StatusCode} Body={body}");
                if (resp.StatusCode != HttpStatusCode.BadRequest)
                    throw new InvalidOperationException($"Expected 400 BadRequest for blank string supplier, got {resp.StatusCode}");
            }

            // -------------------------------------------------------------
            // TEST 4: Non-matching Requisition Line ID -> Expect HTTP 400 NoLinesToConvert
            // -------------------------------------------------------------
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lpi/requisitions/{prId}/convert-to-po"))
            {
                req.Headers.Add("Cookie", approverCookie);
                req.Content = JsonContent.Create(new
                {
                    supplierId = supplierId,
                    requisitionLineIds = new Guid[] { Guid.NewGuid() }
                });
                var resp = await client.SendAsync(req);
                string body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[CEO Repro] Test 4 (Invalid Lines): Status={(int)resp.StatusCode} Body={body}");
                if (resp.StatusCode != HttpStatusCode.BadRequest)
                    throw new InvalidOperationException($"Expected 400 BadRequest for invalid lines, got {resp.StatusCode}");
            }

            // -------------------------------------------------------------
            // TEST 5: Valid Convert to PO -> Expect HTTP 201 Created
            // -------------------------------------------------------------
            string poNumber;
            Guid poId;
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lpi/requisitions/{prId}/convert-to-po"))
            {
                req.Headers.Add("Cookie", approverCookie);
                req.Content = JsonContent.Create(new
                {
                    supplierId = supplierId,
                    requisitionLineIds = new[] { prLineId }
                });
                var resp = await client.SendAsync(req);
                string body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[CEO Repro] Test 5 (Valid Conversion): Status={(int)resp.StatusCode} Body={body}");
                if (resp.StatusCode != HttpStatusCode.Created)
                    throw new InvalidOperationException($"Expected 201 Created for valid conversion, got {resp.StatusCode}: {body}");

                using var doc = JsonDocument.Parse(body);
                poNumber = doc.RootElement.GetProperty("poNumber").GetString()!;
                poId = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
                Console.WriteLine($"[CEO Repro] Created PO: {poNumber} (ID: {poId})");
            }

            // -------------------------------------------------------------
            // TEST 6: Duplicate Convert to PO on already converted PR -> Expect HTTP 400 NoLinesToConvert
            // -------------------------------------------------------------
            using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lpi/requisitions/{prId}/convert-to-po"))
            {
                req.Headers.Add("Cookie", approverCookie);
                req.Content = JsonContent.Create(new
                {
                    supplierId = supplierId,
                    requisitionLineIds = new[] { prLineId }
                });
                var resp = await client.SendAsync(req);
                string body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[CEO Repro] Test 6 (Duplicate Conversion): Status={(int)resp.StatusCode} Body={body}");
                if (resp.StatusCode != HttpStatusCode.BadRequest && resp.StatusCode != HttpStatusCode.Conflict)
                    throw new InvalidOperationException($"Expected 400/409 for duplicate conversion, got {resp.StatusCode}");
            }

            await app.StopAsync().ConfigureAwait(false);
            Console.WriteLine("[CEO Repro] ALL REPRODUCTION & CONVERSION CHECKS PASSED!");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static Guid SeedUserWithPassword(string dbPath, string username, string displayName, string passwordHash, Guid roleId)
    {
        Guid userId = Guid.NewGuid();
        using SqliteConnection conn = new($"Data Source={dbPath}");
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO users (id, username, password_hash, display_name, role_id, status, created_at)
            VALUES ($id, $username, $passwordHash, $displayName, $roleId, 1, $now);
            """;
        cmd.Parameters.AddWithValue("$id", userId.ToString("D"));
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$roleId", roleId.ToString("D"));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return userId;
    }

    private sealed class FakeReproLicenseService : ILocalLicenseService
    {
        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LocalLicenseValidationResult(
                LicenseStatus.Licensed,
                LicenseEdition.Local,
                LicenseFeatures.AllList,
                ExpiresAt: null,
                DaysRemaining: null,
                MachineMatched: true,
                Message: "LPI real-host acceptance license.",
                SourcePath: "lpi-acceptance",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));
        }

        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalLicenseInstallResponse(
                "Installed", LicenseStatus.Licensed, "lpi-acceptance", null, null, "Installed", DateTimeOffset.UtcNow));

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}

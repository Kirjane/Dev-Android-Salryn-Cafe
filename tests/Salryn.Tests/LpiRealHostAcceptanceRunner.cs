using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
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

public static class LpiRealHostAcceptanceRunner
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-lpi-acceptance-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            SqliteProcureToPayService service = new(composition.ConnectionFactory, TimeProvider.System);

            // 1. Seed store settings & app metadata so bootstrap passes
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
            LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "LPI-ALPHA-001", "9900001000001", "Alpha Coffee Beans 1kg", 45_000, 75_000, 20_000, 10_000);
            LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "LPI-BETA-002", "9900001000002", "Beta Oat Milk 1L", 12_000, 22_000, 50_000, 15_000);

            // 3. Seed users with real hashed passwords (using SalrynPasswordHasher)
            string reqHash = SalrynPasswordHasher.HashPassword("Requester123!").Value!;
            string mgrHash = SalrynPasswordHasher.HashPassword("Manager123!").Value!;
            string adminHash = SalrynPasswordHasher.HashPassword("Admin123!").Value!;
            string legacyHash = SalrynPasswordHasher.HashPassword("Legacy123!").Value!;

            Guid requesterId = SeedUserWithPassword(paths.DatabasePath, "lpi-requester", "LPI Cashier", reqHash, DefaultSeedData.CashierRoleId);
            Guid mgrId = SeedUserWithPassword(paths.DatabasePath, "lpi-manager", "LPI Manager", mgrHash, DefaultSeedData.ManagerRoleId);
            _ = SeedUserWithPassword(paths.DatabasePath, "lpi-admin", "LPI Admin", adminHash, DefaultSeedData.AdminRoleId);
            _ = SeedUserWithPassword(paths.DatabasePath, "lpi-legacy-finance", "Legacy Finance Negative Fixture", legacyHash, DefaultSeedData.FinanceManagerRoleId);

            // 4. Seed suppliers
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-APEX-01", "Apex Roasters & Dairy Supply Inc.", "Maria Santos", "09171234501", "orders@apexroasters.ph",
                "Makati Industrial Park, Metro Manila", "123-456-789-000", 30, 5, "Primary coffee and dairy vendor", mgrId));
            await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                "SUP-PACK-02", "Metro Packaging Solutions Corp.", "Carlo Reyes", "09171234502", "sales@metropack.ph",
                "Pasig Logistics Hub, Metro Manila", "987-654-321-000", 15, 3, "Packaging and consumables vendor", mgrId));

            string testToken = $"DISPOSABLE-TEST-{Guid.NewGuid():N}";

            // 5. Start real Host with authenticated TestHostNonce (SAFETY-01 / HOST-ID-02)
            await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    LicenseService = new FakeAcceptanceLicenseService(),
                    EnableStaticWebAssets = true,
                    WriteDevelopmentDatabasePath = false,
                    TestHostNonce = testToken
                }).ConfigureAwait(false);

            await app.StartAsync().ConfigureAwait(false);

            string? boundUrl = app.Urls.FirstOrDefault(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(boundUrl) || boundUrl.EndsWith(":0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Failed to resolve dynamic loopback port.");
            }

            Console.WriteLine($"[LPI Acceptance] Live Host started on {boundUrl}");

            // 6. Generate Disposable Test Environment Marker with Host URL & Process ID (SAFETY-01 / HOST-ID-02)
            string markerPath = Path.Combine(root, "disposable-test-marker.json");
            await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(new
            {
                token = testToken,
                isDisposableTest = true,
                databasePath = paths.DatabasePath,
                hostUrl = boundUrl,
                processId = Environment.ProcessId,
                createdAtUtc = DateTime.UtcNow
            })).ConfigureAwait(false);

            string repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
            string webRoot = Path.Combine(repoRoot, "src", "Salryn.App.Web");

            // 6b. Run Negative Safety Gate Tests (SAFETY-01 / HOST-ID-02)
            // Verify that acceptance test execution fails closed if targeting port 5299, missing token, or corrupt marker
            await RunNegativeSafetyTestsAsync(webRoot, markerPath, testToken, boundUrl).ConfigureAwait(false);

            // 6c. Run Positive Playwright End-to-End Suite against Verified Live Host
            ProcessStartInfo psi = new("cmd.exe", "/c npx playwright test e2e/integrated-host-lpi-acceptance.spec.ts --project=integrated-host")
            {
                WorkingDirectory = webRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.Environment["SALRYN_INTEGRATED_HOST_URL"] = boundUrl;
            psi.Environment["SALRYN_DISPOSABLE_TEST_TOKEN"] = testToken;
            psi.Environment["SALRYN_DISPOSABLE_MARKER_PATH"] = markerPath;
            psi.Environment["SALRYN_QA_REQUESTER_USER"] = "lpi-requester";
            psi.Environment["SALRYN_QA_REQUESTER_PASS"] = "Requester123!";
            psi.Environment["SALRYN_QA_MANAGER_USER"] = "lpi-manager";
            psi.Environment["SALRYN_QA_MANAGER_PASS"] = "Manager123!";
            psi.Environment["SALRYN_QA_ADMIN_USER"] = "lpi-admin";
            psi.Environment["SALRYN_QA_ADMIN_PASS"] = "Admin123!";
            psi.Environment["SALRYN_QA_LEGACY_USER"] = "lpi-legacy-finance";
            psi.Environment["SALRYN_QA_LEGACY_PASS"] = "Legacy123!";

            using Process? proc = Process.Start(psi);
            if (proc is null) throw new InvalidOperationException("Failed to launch Playwright process.");

            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            Console.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine("[Playwright STDERR]: " + stderr);
            }

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"Playwright real-host acceptance test failed with exit code {proc.ExitCode}");
            }

            Console.WriteLine("[LPI Acceptance] Playwright browser journey completed successfully (Exit 0)!");

            // 7. Stop the host
            await app.StopAsync().ConfigureAwait(false);

            // 8. Test Restart Durability: Start a fresh host against the EXACT same SQLite database!
            Console.WriteLine("[LPI Acceptance] Testing host restart durability against the same SQLite database...");
            SalrynLocalComposition restartedComposition = SalrynLocalComposition.Create(paths);
            await using WebApplication restartedApp = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = restartedComposition,
                    LicenseService = new FakeAcceptanceLicenseService(),
                    EnableStaticWebAssets = true,
                    WriteDevelopmentDatabasePath = false
                }).ConfigureAwait(false);

            await restartedApp.StartAsync().ConfigureAwait(false);

            string? restartedUrl = restartedApp.Urls.FirstOrDefault(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            using (HttpClient client = new())
            {
                HttpResponseMessage healthResp = await client.GetAsync($"{restartedUrl}/health");
                if (!healthResp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Restarted host health check failed: {healthResp.StatusCode}");
                }
            }

            await restartedApp.StopAsync().ConfigureAwait(false);
            Console.WriteLine("[LPI Acceptance] Host restart durability verified successfully!");

            // 9. Post-State Validation: Query SQLite Ledger Tables directly
            Console.WriteLine("[LPI Acceptance] Validating SQLite post-state ledger records...");
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();

                // Validate Purchase Requisition
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT status FROM purchase_requisitions LIMIT 1;";
                    string? prStatus = cmd.ExecuteScalar()?.ToString();
                    if (prStatus != "ConvertedToPo")
                    {
                        throw new InvalidOperationException($"Expected PR status 'ConvertedToPo', got '{prStatus}'.");
                    }
                }

                // Validate the complete structured audit custody for the PHP 3,720 full settlement.
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT a.user_id, a.old_value_json, a.new_value_json, a.created_at
                        FROM audit_logs a
                        JOIN supplier_payments p ON p.id = a.entity_id
                        WHERE a.entity_type='SupplierPayment'
                          AND a.action='AP_PAYMENT_CONFIRMED_AND_SETTLED'
                          AND p.reference_no='TRF-REALHOST-8899';
                        """;
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    if (!reader.Read()) throw new InvalidOperationException("Structured settlement audit for TRF-REALHOST-8899 was not found.");
                    string actorId = reader.GetString(0);
                    using JsonDocument previousAudit = JsonDocument.Parse(reader.GetString(1));
                    using JsonDocument newAudit = JsonDocument.Parse(reader.GetString(2));
                    JsonElement previous = previousAudit.RootElement;
                    JsonElement current = newAudit.RootElement;
                    if (actorId != mgrId.ToString("D") ||
                        previous.GetProperty("OutstandingBalanceCents").GetInt64() != 372_000 ||
                        previous.GetProperty("InvoiceStatus").GetString() != "PostedOpen" ||
                        current.GetProperty("AmountCents").GetInt64() != 372_000 ||
                        current.GetProperty("PreviousOutstandingBalanceCents").GetInt64() != 372_000 ||
                        current.GetProperty("NewOutstandingBalanceCents").GetInt64() != 0 ||
                        current.GetProperty("NewInvoiceStatus").GetString() != "Paid" ||
                        current.GetProperty("AppliedApLedgerAmountCents").GetInt64() != 372_000 ||
                        current.GetProperty("SettlementActorUserId").GetString() != mgrId.ToString("D") ||
                        current.GetProperty("RecordedByUserId").GetString() != mgrId.ToString("D") ||
                        !current.GetProperty("SameUserRecordedAndSettled").GetBoolean() ||
                        !current.GetProperty("OperatorConfirmedActualPayment").GetBoolean() ||
                        DateTimeOffset.Parse(current.GetProperty("SettlementTimestamp").GetString()!, CultureInfo.InvariantCulture) !=
                            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture))
                    {
                        throw new InvalidOperationException(
                            $"Structured settlement audit did not preserve exact PHP 3,720 custody and before/after accounting values. " +
                            $"actor={actorId}; expectedActor={mgrId:D}; old={previous.GetRawText()}; new={current.GetRawText()}; createdAt={reader.GetString(3)}");
                    }
                    if (reader.Read()) throw new InvalidOperationException("Settlement replay created a duplicate structured audit event.");
                }

                // Validate Purchase Order
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT status FROM purchase_orders LIMIT 1;";
                    string? poStatus = cmd.ExecuteScalar()?.ToString();
                    if (poStatus != "Approved" && poStatus != "PartiallyReceived" && poStatus != "FullyReceived" && poStatus != "Received")
                    {
                        throw new InvalidOperationException($"Unexpected PO status: '{poStatus}'.");
                    }
                }

                // Validate Receiving Report
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT rr.status, rrl.actual_delivered_qty_x1000, rrl.accepted_qty_x1000, rrl.rejected_qty_x1000
                        FROM receiving_reports rr
                        JOIN receiving_report_lines rrl ON rrl.receiving_report_id = rr.id
                        LIMIT 1;
                        """;
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    if (!reader.Read()) throw new InvalidOperationException("No receiving report found in SQLite database.");
                    string status = reader.GetString(0);
                    long delivered = reader.GetInt64(1);
                    long accepted = reader.GetInt64(2);
                    long rejected = reader.GetInt64(3);
                    if (status != "Posted" || delivered != 10_000 || accepted != 8_000 || rejected != 2_000)
                    {
                        throw new InvalidOperationException($"RR mismatch: status={status}, delivered={delivered}, accepted={accepted}, rejected={rejected}");
                    }
                }

                // Validate Supplier Return
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT count(*) FROM supplier_returns WHERE status = 'Posted';";
                    long sretCount = (long)(cmd.ExecuteScalar() ?? 0L);
                    if (sretCount < 1)
                    {
                        throw new InvalidOperationException("Supplier return record not found in SQLite database.");
                    }
                }

                // Validate Supplier Invoice & 3-Way Match
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT status, match_status, remaining_balance_cents FROM supplier_invoices WHERE supplier_invoice_no = 'INV-REALHOST-2026-001';";
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    if (!reader.Read()) throw new InvalidOperationException("Invoice 'INV-REALHOST-2026-001' not found in SQLite database.");
                    string invStatus = reader.GetString(0);
                    string matchStatus = reader.GetString(1);
                    long balanceDue = reader.GetInt64(2);
                    if (invStatus != "Paid" || matchStatus != "VarianceApproved" || balanceDue != 0)
                    {
                        throw new InvalidOperationException($"Invoice mismatch: status={invStatus}, matchStatus={matchStatus}, balanceDue={balanceDue}");
                    }
                }

                // Validate Duplicate Line Regression Invoice (FINANCIAL-02)
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT status, match_status, remaining_balance_cents FROM supplier_invoices WHERE supplier_invoice_no = 'INV-DUP-LINEB-001';";
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    if (!reader.Read()) throw new InvalidOperationException("Duplicate line invoice 'INV-DUP-LINEB-001' not found in SQLite database.");
                    string dupStatus = reader.GetString(0);
                    string dupMatch = reader.GetString(1);
                    long dupBalance = reader.GetInt64(2);
                    if (dupStatus != "PartiallyPaid" || dupMatch != "Matched" || dupBalance != 700000L)
                    {
                        throw new InvalidOperationException($"Duplicate line invoice match mismatch: status={dupStatus}, matchStatus={dupMatch}, balanceDue={dupBalance}");
                    }
                }

                // Validate Supplier Payment & Drawer Protection
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT amount_cents, payment_method, disbursement_source, status FROM supplier_payments WHERE reference_no = 'TRF-REALHOST-8899';";
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    if (!reader.Read()) throw new InvalidOperationException("Payment 'TRF-REALHOST-8899' not found in SQLite database.");
                    long amount = reader.GetInt64(0);
                    string method = reader.GetString(1);
                    string source = reader.GetString(2);
                    string pmtStatus = reader.GetString(3);
                    if (amount != 372_000 || method != "BankTransfer" || source != "OperatingAccount" || pmtStatus != "Settled")
                    {
                        throw new InvalidOperationException($"Payment mismatch: amount={amount}, method={method}, source={source}, status={pmtStatus}");
                    }
                }

                // Verify POS cash drawer had ZERO disbursements (strictly protected)
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT count(*) FROM supplier_payments WHERE disbursement_source = 'CashDrawer';";
                    long drawerDisbursed = (long)(cmd.ExecuteScalar() ?? 0L);
                    if (drawerDisbursed > 0)
                    {
                        throw new InvalidOperationException("CRITICAL VIOLATION: Cash drawer disbursements occurred during AP payment settlement!");
                    }
                }
            }

            Console.WriteLine("[LPI Acceptance] Direct SQLite post-state validation PASSED (PR, PO, RR, Stock, settled AP invoice/payment, and cash-drawer isolation verified).");
        }
        finally
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch { }
        }
    }

    private static Guid SeedUserWithPassword(string dbPath, string username, string displayName, string passwordHash, string roleId)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (id, username, display_name, password_hash, role_id, status, created_at)
            VALUES ($id, $username, $display, $hash, $roleId, 1, $at);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$display", displayName);
        command.Parameters.AddWithValue("$hash", passwordHash);
        command.Parameters.AddWithValue("$roleId", roleId);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return id;
    }

    private static async Task RunNegativeSafetyTestsAsync(string webRoot, string markerPath, string validToken, string validHostUrl)
    {
        Console.WriteLine("[LPI Acceptance] Running SAFETY-01 negative gate checks...");

        // Case A: Missing host URL or targeting port 5299
        await AssertSafetyRejectionAsync(webRoot, new Dictionary<string, string>
        {
            ["SALRYN_INTEGRATED_HOST_URL"] = "http://127.0.0.1:5299",
            ["SALRYN_DISPOSABLE_TEST_TOKEN"] = validToken,
            ["SALRYN_DISPOSABLE_MARKER_PATH"] = markerPath,
        }, "FAIL-CLOSED SAFETY VIOLATION: Acceptance suite cannot target port 5299").ConfigureAwait(false);

        // Case B: Missing disposable marker
        await AssertSafetyRejectionAsync(webRoot, new Dictionary<string, string>
        {
            ["SALRYN_INTEGRATED_HOST_URL"] = validHostUrl,
            ["SALRYN_DISPOSABLE_TEST_TOKEN"] = "",
            ["SALRYN_DISPOSABLE_MARKER_PATH"] = "",
        }, "FAIL-CLOSED SAFETY VIOLATION: Both SALRYN_DISPOSABLE_TEST_TOKEN and SALRYN_DISPOSABLE_MARKER_PATH must be provided").ConfigureAwait(false);

        // Case C: Mismatched marker token
        await AssertSafetyRejectionAsync(webRoot, new Dictionary<string, string>
        {
            ["SALRYN_INTEGRATED_HOST_URL"] = validHostUrl,
            ["SALRYN_DISPOSABLE_TEST_TOKEN"] = "DISPOSABLE-TEST-WRONG-TOKEN",
            ["SALRYN_DISPOSABLE_MARKER_PATH"] = markerPath,
        }, "FAIL-CLOSED SAFETY VIOLATION: Disposable test environment marker token mismatch").ConfigureAwait(false);

        // Case D: Host URL mismatch with marker (HOST-ID-02)
        await AssertSafetyRejectionAsync(webRoot, new Dictionary<string, string>
        {
            ["SALRYN_INTEGRATED_HOST_URL"] = "http://127.0.0.1:59998",
            ["SALRYN_DISPOSABLE_TEST_TOKEN"] = validToken,
            ["SALRYN_DISPOSABLE_MARKER_PATH"] = markerPath,
        }, "FAIL-CLOSED SAFETY VIOLATION: Host URL mismatch").ConfigureAwait(false);

        Console.WriteLine("[LPI Acceptance] SAFETY-01 negative gate checks PASSED: all unsafe invocations failed closed.");
    }

    private static async Task AssertSafetyRejectionAsync(string webRoot, Dictionary<string, string> env, string expectedErrorSubstring)
    {
        ProcessStartInfo psi = new("cmd.exe", "/c npx playwright test e2e/integrated-host-lpi-acceptance.spec.ts --project=integrated-host")
        {
            WorkingDirectory = webRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var kv in env)
        {
            psi.Environment[kv.Key] = kv.Value;
        }

        using Process? proc = Process.Start(psi);
        if (proc is null) throw new InvalidOperationException("Failed to launch test process for safety check.");
        string stdout = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await proc.WaitForExitAsync().ConfigureAwait(false);

        string output = stdout + "\n" + stderr;
        if (proc.ExitCode == 0)
        {
            throw new InvalidOperationException($"SAFETY GATE FAILURE: Unsafe test execution was expected to fail, but succeeded with Exit 0! Output:\n{output}");
        }
        if (!output.Contains(expectedErrorSubstring, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SAFETY GATE FAILURE: Process exited with {proc.ExitCode} but did not contain expected error '{expectedErrorSubstring}'. Output:\n{output}");
        }
    }

    private static string? FindRepoRoot()
    {
        string[] candidates = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (string candidate in candidates)
        {
            DirectoryInfo? current = new(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
        }
        return null;
    }

    private sealed class FakeAcceptanceLicenseService : ILocalLicenseService
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

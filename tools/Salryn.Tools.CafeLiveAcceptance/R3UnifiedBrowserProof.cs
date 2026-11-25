using System.Diagnostics;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tools.CafeLiveAcceptance;

internal static class R3UnifiedBrowserProof
{
    public static async Task RunAsync(DirectoryInfo repoDir, string playwrightPkg, string axePath)
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("CAFE_R3_UNIFIED_ARTIFACT_ROOT");
        string root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? overrideRoot
            : Path.Combine(Path.GetTempPath(), "salryn-r3-unified-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        static void CleanDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
        }

        const string ownerPass = "Synthetic-Owner!2026";
        const string cashierPass = "Synthetic-Cashier!2026";
        const string auditorPass = "Synthetic-Auditor!2026";
        string nonce = Guid.NewGuid().ToString("N");

        // --- PHASE A: Unlicensed Host Fail-Closed Proof (serving production apps/cafe-web/dist directly from Salryn.Api.Local) ---
        string unlicensedRoot = Path.Combine(root, "unlicensed-db");
        string emptyLicenseDir = Path.Combine(root, "empty-license");
        CleanDir(unlicensedRoot);
        CleanDir(emptyLicenseDir);
        var unlicensedDb = C4ProofDatabase.CreateEmpty(unlicensedRoot);
        Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "0");
        Environment.SetEnvironmentVariable("SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT", null);

        var unlicensedService = new LocalLicenseService(
            new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(emptyLicenseDir, LocalLicenseFileStore.DefaultFileName)),
            new LocalLicenseValidator(new LocalLicenseValidatorOptions
            {
                ExpectedApplicationId = "Salryn.Cafe",
                MachineFingerprint = LocalMachineFingerprint.Current(),
                SignatureVerifier = LocalLicenseSignatureVerifier.Verify,
                AllowDevelopmentMode = false,
                DevelopmentFeatures = LicenseFeatures.AllList
            }));

        await using (var unlicensedHost = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(unlicensedDb.Paths),
            LicenseService = unlicensedService,
            Url = "http://127.0.0.1:0",
            TestHostNonce = nonce,
            EnableStaticWebAssets = true,
            WriteDevelopmentDatabasePath = false
        }))
        {
            await unlicensedHost.StartAsync();
            await RunBrowserPhaseAsync(
                playwrightPkg, axePath, unlicensedHost.Urls.Single(), nonce, root,
                phase: "unlicensed",
                ownerPass, cashierPass, auditorPass);
            await unlicensedHost.StopAsync();
        }

        // --- PHASE A2: Clean Empty Licensed DB First-Admin Setup via Real R3 UI ---
        string firstAdminRoot = Path.Combine(root, "first-admin-db");
        CleanDir(firstAdminRoot);
        var firstAdminDb = C4ProofDatabase.CreateEmpty(firstAdminRoot);
        Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");

        await using (var firstAdminHost = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(firstAdminDb.Paths),
            Url = "http://127.0.0.1:0",
            TestHostNonce = nonce,
            EnableStaticWebAssets = true,
            WriteDevelopmentDatabasePath = false
        }))
        {
            await firstAdminHost.StartAsync();
            await RunBrowserPhaseAsync(
                playwrightPkg, axePath, firstAdminHost.Urls.Single(), nonce, root,
                phase: "first-admin",
                ownerPass, cashierPass, auditorPass);
            await firstAdminHost.StopAsync();
        }

        if (firstAdminDb.ScalarLong("SELECT COUNT(*) FROM users WHERE username='gate-d-admin';") != 1)
        {
            throw new InvalidOperationException("First-admin UI bootstrap failed to persist gate-d-admin in SQLite.");
        }

        // --- PHASE B: Licensed Host (DEBUG_DEVELOPMENT_LICENSE) Full Real R3 Operator UI Journeys ---
        string licensedRoot = Path.Combine(root, "licensed-db");
        CleanDir(licensedRoot);
        var db = C4ProofDatabase.Create(licensedRoot);
        string now = DateTimeOffset.UtcNow.ToString("O");

        if (db.ScalarLong("SELECT COUNT(*) FROM store_settings;") == 0)
        {
            db.Execute("""
                INSERT INTO store_settings(
                    id, store_name, business_name, address, contact_number, tin,
                    currency_label, receipt_width_mm, receipt_header, receipt_footer,
                    tax_mode_enabled, shift_mode_enabled, allow_negative_stock, created_at, updated_at)
                VALUES($id, 'Salryn Cafe Unified QA', 'Salryn Cafe Roastery', '100 Cafe Lane', '09170000000', NULL,
                    'PHP', 80, 'Salryn Cafe', 'Synthetic Disposable Proof', 0, 1, 0, $at, NULL);
                """,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$at", now));
        }

        db.Execute(
            "UPDATE users SET password_hash=$hash WHERE id=$id;",
            ("$hash", SalrynPasswordHasher.HashPassword(ownerPass).Value),
            ("$id", C4ProofIds.ActorUserId.ToString("D")));

        Guid cashierId = Guid.NewGuid();
        db.Execute(
            "INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at, updated_at) VALUES($id, 'r3-cashier', 'R3 Unified Cashier', $hash, $role, 1, $at, NULL);",
            ("$id", cashierId.ToString("D")),
            ("$hash", SalrynPasswordHasher.HashPassword(cashierPass).Value),
            ("$role", DefaultSeedData.CashierRoleId),
            ("$at", now));

        string adminRoleId = db.ScalarString("SELECT id FROM roles WHERE name='Admin' LIMIT 1;")
            ?? throw new InvalidOperationException("Admin role missing.");
        Guid auditorId = Guid.NewGuid();
        db.Execute(
            "INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at, updated_at) VALUES($id, 'r3-auditor', 'R3 Independent Auditor', $hash, $role, 1, $at, NULL);",
            ("$id", auditorId.ToString("D")),
            ("$hash", SalrynPasswordHasher.HashPassword(auditorPass).Value),
            ("$role", adminRoleId),
            ("$at", now));

        Guid customerId = Guid.NewGuid();
        db.Execute(
            "INSERT INTO customers(id, full_name, phone, is_active, created_by_user_id, created_at, updated_at) VALUES($id, 'Maria Santos', '0917-555-0100', 1, $actor, $at, $at);",
            ("$id", customerId.ToString("D")),
            ("$actor", C4ProofIds.ActorUserId.ToString("D")),
            ("$at", now));

        db.Execute(
            "INSERT INTO suppliers(id, name, contact_number, notes, is_active) VALUES($id, 'Benguet Roasters', '0917-555-0200', NULL, 1);",
            ("$id", Guid.NewGuid().ToString("D")));

        // Generous product & ingredient stock so multiple POS Cash/AR/Partial/Delivery sales commit cleanly without stock exhaustion.
        db.Execute("UPDATE products SET stock_on_hand_x1000=200000 WHERE id=$id;", ("$id", C4ProofIds.ProductId.ToString("D")));
        db.Execute("UPDATE ingredients SET stock_on_hand_x1000=500000 WHERE id=$id;", ("$id", C4ProofIds.ItemIngredientId.ToString("D")));

        db.Execute("DELETE FROM cash_drawer_events;");
        db.Execute("DELETE FROM cash_drawer_reviews;");
        db.Execute("DELETE FROM cashier_sessions;");

        Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");

        await using (var host1 = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(db.Paths),
            Url = "http://127.0.0.1:0",
            TestHostNonce = nonce,
            EnableStaticWebAssets = true,
            WriteDevelopmentDatabasePath = false
        }))
        {
            await host1.StartAsync();
            await RunBrowserPhaseAsync(
                playwrightPkg, axePath, host1.Urls.Single(), nonce, root,
                phase: "licensed-main",
                ownerPass, cashierPass, auditorPass);
            await host1.StopAsync();
        }

        // --- PHASE C: Host Restart Persistence & Replay Verification over SAME SQLite file ---
        await using (var host2 = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(db.Paths),
            Url = "http://127.0.0.1:0",
            TestHostNonce = nonce,
            EnableStaticWebAssets = true,
            WriteDevelopmentDatabasePath = false
        }))
        {
            await host2.StartAsync();
            await RunBrowserPhaseAsync(
                playwrightPkg, axePath, host2.Urls.Single(), nonce, root,
                phase: "post-restart",
                ownerPass, cashierPass, auditorPass);
            await host2.StopAsync();
        }

        // --- Authoritative SQLite Ledger Verification (15 checks across C1-C11!) ---
        int checks = 0;
        void Check(bool ok, string msg)
        {
            if (!ok) throw new InvalidOperationException("Unified R3 SQLite check failed: " + msg);
            checks++;
        }

        Check(db.ScalarLong("SELECT COUNT(*) FROM cashier_sessions WHERE status=2;") >= 1,
            "Expected at least 1 closed blind shift.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cash_drawer_reviews WHERE decision='Acknowledged';") >= 1,
            "Expected independent non-self variance review acknowledgement.");
        Check(db.ScalarLong($"SELECT COUNT(*) FROM cash_movements WHERE movement_type={Convert.ToInt32(Salryn.Core.Models.CashMovementType.OperationalExpense)};") >= 1,
            "Expected C6 OperationalExpense cash movement persisted.");
        Check(db.ScalarLong($"SELECT COUNT(*) FROM sales WHERE status={Convert.ToInt32(Salryn.Core.Models.SaleStatus.Completed)};") >= 4,
            "Expected >= 4 completed POS sales (Cash+Discount+ServiceFee, AR, Partial Cash+AR, Delivery-bound Takeout).");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_sale_fees WHERE fee_type='Service';") >= 1,
            "Expected C5 Service fee persisted via POS UI.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_sale_fees WHERE fee_type='Delivery';") >= 1,
            "Expected C8 Delivery fee automatically bound & persisted via POS UI.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_sale_refunds WHERE state='Committed';") >= 1,
            "Expected C5 Refund requested, approved, and committed via R3 UI.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_print_jobs;") >= 2,
            "Expected C7 Original + Reprint receipt jobs persisted via R3 POS UI.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_bookings WHERE fulfillment_state IN ('OrderBound','InProgress','Fulfilled');") >= 1,
            "Expected C9 booking confirmed, revised, deposited, marked ready, and fulfillment started.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_bookings WHERE status='Cancelled';") >= 1,
            "Expected C9 cancelled booking persisted via R3 UI.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_facts WHERE fact_type='Received';") >= 2,
            "Expected 2 historical received deposit facts in SQLite.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_facts WHERE fact_type='Refunded';") >= 1,
            "Expected 1 partial multi-deposit refund fact in SQLite.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_allocations;") >= 2,
            "Expected 2 refund allocation rows across both historical deposits.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_delivery_fulfillments WHERE status='Delivered';") >= 1,
            "Expected order-bound delivery created, revised, dispatched, and resolved as Delivered via R3 UI.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_incidents WHERE status='Resolved';") >= 1,
            "Expected C10 incident created and resolved via R3 UI.");

        Console.WriteLine(
            $"PASS: Unified production R3 operator UI proof (Unlicensed gate + First-Admin UI setup + Cashier RBAC + Cash/Expense/Reversal/BlindClose/VarianceReview + POS Cash/AR/Partial/Discount/Fees/Print + Full R3 Refund + Bookings Revise/Deposit/Refund/Fulfill/Cancel + UI Order-Bound Delivery + Inventory Unknown-Outcome Reload Retry & Cross-Tab Lock + Staff/Incident Resolve + Host Restart + Desktop/Tablet/Mobile + axe-core + {checks} SQLite checks). Artifacts: {root}. DEBUG_DEVELOPMENT_LICENSE.");
    }

    private static async Task RunBrowserPhaseAsync(
        string playwrightPkg,
        string axePath,
        string hostUrl,
        string nonce,
        string root,
        string phase,
        string ownerPass,
        string cashierPass,
        string auditorPass)
    {
        var psi = new ProcessStartInfo("node") { UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "r3-unified-browser.mjs"));
        psi.Environment["CAFE_LIVE_PLAYWRIGHT_PACKAGE_JSON"] = playwrightPkg;
        psi.Environment["CAFE_LIVE_AXE_PATH"] = axePath;
        psi.Environment["CAFE_R3_BASE_URL"] = hostUrl;
        psi.Environment["CAFE_R3_NONCE"] = nonce;
        psi.Environment["CAFE_R3_ROOT"] = root;
        psi.Environment["CAFE_R3_PHASE"] = phase;
        psi.Environment["CAFE_R3_OWNER_USER"] = "c4-proof";
        psi.Environment["CAFE_R3_OWNER_PASS"] = ownerPass;
        psi.Environment["CAFE_R3_CASHIER_USER"] = "r3-cashier";
        psi.Environment["CAFE_R3_CASHIER_PASS"] = cashierPass;
        psi.Environment["CAFE_R3_AUDITOR_USER"] = "r3-auditor";
        psi.Environment["CAFE_R3_AUDITOR_PASS"] = auditorPass;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch r3-unified-browser.mjs phase={phase}.");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unified R3 browser phase '{phase}' failed with exit {proc.ExitCode}. Artifacts: {root}");
        }
    }
}

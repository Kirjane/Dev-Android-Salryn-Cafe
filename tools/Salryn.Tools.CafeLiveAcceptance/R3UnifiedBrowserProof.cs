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

        const string ownerPass = "Synthetic-Owner!2026";
        const string cashierPass = "Synthetic-Cashier!2026";
        const string auditorPass = "Synthetic-Auditor!2026";
        string nonce = Guid.NewGuid().ToString("N");

        // --- PHASE A: Unlicensed Host Fail-Closed Proof (serving production apps/cafe-web/dist directly from Salryn.Api.Local) ---
        string unlicensedRoot = Path.Combine(root, "unlicensed-db");
        string emptyLicenseDir = Path.Combine(root, "empty-license");
        Directory.CreateDirectory(emptyLicenseDir);
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

        // --- PHASE B: Licensed Host (DEBUG_DEVELOPMENT_LICENSE) Unified R3 Journeys ---
        string licensedRoot = Path.Combine(root, "licensed-db");
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

        // --- Authoritative SQLite Ledger Verification ---
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
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_bookings WHERE status='Confirmed';") >= 1,
            "Expected confirmed booking persisted in SQLite.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_facts WHERE fact_type='Received';") >= 2,
            "Expected 2 historical received deposit facts in SQLite.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_facts WHERE fact_type='Refunded';") >= 1,
            "Expected 1 partial multi-deposit refund fact in SQLite.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_booking_deposit_allocations;") >= 2,
            "Expected 2 refund allocation rows across both historical deposits.");
        Check(db.ScalarLong("SELECT COUNT(*) FROM cafe_delivery_fulfillments WHERE status='Delivered';") >= 1,
            "Expected order-bound delivery resolved as Delivered.");

        Console.WriteLine(
            $"PASS: Unified production R3 browser proof (Unlicensed gate + Cashier RBAC + Owner Shift/BlindClose/VarianceReview + Historical Multi-Deposit Booking Refund + Order-Bound Delivery + Host Restart + Desktop/Tablet/Mobile + axe-core + {checks} SQLite checks). Artifacts: {root}. DEBUG_DEVELOPMENT_LICENSE.");
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

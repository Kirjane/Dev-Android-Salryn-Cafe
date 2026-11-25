using System.Diagnostics;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Api.Local.Services;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;

#if !DEBUG
throw new InvalidOperationException("This disposable acceptance runner requires Debug. Release licensing is a separate authorized-fixture gate.");
#else
if (args.Any(a => string.Equals(a, "--r3-host-contract-proof", StringComparison.OrdinalIgnoreCase)))
{
    await R3HostContractProof.RunAsync().ConfigureAwait(false);
    return;
}
if (args.Any(a => string.Equals(a, "--r3-reports-browser-proof", StringComparison.OrdinalIgnoreCase)))
{
    var repoDir = ResolveRepositoryRoot();
    string pwPkg = ResolvePlaywrightPackageJson(repoDir);
    string axeFile = Path.Combine(repoDir.FullName, "apps", "cafe-web", "node_modules", "axe-core", "axe.min.js");
    if (!File.Exists(axeFile)) throw new InvalidOperationException("axe-core script not found at: " + axeFile);
    await R3ReportsBrowserProof.RunAsync(repoDir, pwPkg, axeFile).ConfigureAwait(false);
    return;
}
if (args.Any(a => string.Equals(a, "--r3-inventory-browser-proof", StringComparison.OrdinalIgnoreCase)))
{
    var repoDir = ResolveRepositoryRoot();
    string pwPkg = ResolvePlaywrightPackageJson(repoDir);
    string axeFile = Path.Combine(repoDir.FullName, "apps", "cafe-web", "node_modules", "axe-core", "axe.min.js");
    if (!File.Exists(axeFile)) throw new InvalidOperationException("axe-core script not found at: " + axeFile);
    await R3InventoryBrowserProof.RunAsync(repoDir, pwPkg, axeFile).ConfigureAwait(false);
    return;
}
if (args.Any(a => string.Equals(a, "--r3-bookings-deliveries-proof", StringComparison.OrdinalIgnoreCase)))
{
    await R3BookingsDeliveriesProof.RunAsync().ConfigureAwait(false);
    return;
}
if (args.Any(a => string.Equals(a, "--r3-cash-order-proof", StringComparison.OrdinalIgnoreCase)))
{
    await R3CashOrderHardeningProof.RunAsync().ConfigureAwait(false);
    return;
}
if (args.Any(a => string.Equals(a, "--r3-unified-browser-proof", StringComparison.OrdinalIgnoreCase)))
{
    var repoDir = ResolveRepositoryRoot();
    string pwPkg = ResolvePlaywrightPackageJson(repoDir);
    string axeFile = Path.Combine(repoDir.FullName, "apps", "cafe-web", "node_modules", "axe-core", "axe.min.js");
    if (!File.Exists(axeFile)) throw new InvalidOperationException("axe-core script not found at: " + axeFile);
    await Salryn.Tools.CafeLiveAcceptance.R3UnifiedBrowserProof.RunAsync(repoDir, pwPkg, axeFile).ConfigureAwait(false);
    return;
}
if (args.Any(a => string.Equals(a, "--c1-recovery-proof", StringComparison.OrdinalIgnoreCase)))
{
    await RunC1RecoveryProofAsync().ConfigureAwait(false);
    return;
}

await RunDefaultCashierLiveAcceptanceAsync().ConfigureAwait(false);

static async Task RunDefaultCashierLiveAcceptanceAsync()
{
    string root = Path.Combine(Path.GetTempPath(), "salryn-cafe-live-" + Guid.NewGuid().ToString("N"));
    var db = C4ProofDatabase.Create(root);
    const string password = "Synthetic-Cafe-Live!2026";
    db.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;",
        ("$hash", SalrynPasswordHasher.HashPassword(password).Value), ("$id", C4ProofIds.ActorUserId.ToString("D")));
    Guid customer = Guid.NewGuid();
    db.Execute("INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at) VALUES($id,'Synthetic Cafe Account',1,$actor,$at,$at);",
        ("$id", customer.ToString("D")), ("$actor", C4ProofIds.ActorUserId.ToString("D")), ("$at", DateTimeOffset.UtcNow.ToString("O")));

    // Seed Receipt + Kitchen logical print stations and draft menu variant for live B1/B2/B3 acceptance.
    var seedComp = SalrynLocalComposition.Create(db.Paths);
    var printService = new SqliteCafePrintService(db.Factory);
    var receiptStationRes = printService.PublishStation(new CafePrinterStationMutation(
        Guid.NewGuid(), Guid.NewGuid(), C4ProofIds.StoreId, C4ProofIds.ActorUserId,
        "Counter Receipt", CafePrinterStationKind.CustomerReceipt, "front-counter", true, 0, DateTimeOffset.UtcNow));
    if (receiptStationRes.IsFailure) throw new InvalidOperationException("Seed receipt station failed: " + receiptStationRes.ErrorDetail.Message);
    var kitchenStationRes = printService.PublishStation(new CafePrinterStationMutation(
        Guid.NewGuid(), Guid.NewGuid(), C4ProofIds.StoreId, C4ProofIds.ActorUserId,
        "Barista Kitchen", CafePrinterStationKind.Kitchen, "espresso-bar", true, 0, DateTimeOffset.UtcNow));
    if (kitchenStationRes.IsFailure) throw new InvalidOperationException("Seed kitchen station failed: " + kitchenStationRes.ErrorDetail.Message);
    var draftMenu = seedComp.MenuCatalog.GetDraft();
    if (draftMenu.IsSuccess)
    {
        foreach (var item in draftMenu.Value.Items.Where(i => i.IsActive))
        {
            if (!draftMenu.Value.Variants.Any(v => v.ProductId == item.ProductId && v.IsActive))
            {
                var savedVar = seedComp.MenuCatalog.SaveVariant(new MenuVariantMutation(
                    null, item.ProductId, "Regular", $"LIVE-{item.ProductId:N}"[..16], null,
                    item.SellingPriceCents, item.CostPriceCents, true, false, 1));
                if (savedVar.IsFailure) throw new InvalidOperationException("Seed draft variant failed: " + savedVar.ErrorDetail.Message);
            }
        }
    }
    string nonce = Guid.NewGuid().ToString("N");
    Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");
    await using var host = await LocalApiApplicationFactory.CreateAsync([], new()
    {
        Composition = SalrynLocalComposition.Create(db.Paths), Url = "http://127.0.0.1:0", TestHostNonce = nonce,
        EnableStaticWebAssets = true, WriteDevelopmentDatabasePath = false
    });
    await host.StartAsync();
    string url = host.Urls.Single();
    var start = new ProcessStartInfo("node") { UseShellExecute = false };
    start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "browser.mjs"));
    var repo = ResolveRepositoryRoot();
    start.Environment["CAFE_QA_PLAYWRIGHT_PACKAGE"] = ResolvePlaywrightPackageJson(repo);
    start.Environment["CAFE_QA_URL"] = url;
    start.Environment["CAFE_QA_NONCE"] = nonce;
    start.Environment["CAFE_QA_PASSWORD"] = password;
    start.Environment["CAFE_QA_ROOT"] = root;
    using var browser = Process.Start(start) ?? throw new InvalidOperationException("Browser runner did not start.");
    await browser.WaitForExitAsync();
    await host.StopAsync();
    if (browser.ExitCode != 0) throw new InvalidOperationException($"Live browser acceptance failed: {browser.ExitCode}. Synthetic artifacts retained: {root}");
    // Mount a new executable Host over the exact same disposable database and replay
    // the original successful completion requests through real authenticated HTTP.
    await using var restarted = await LocalApiApplicationFactory.CreateAsync([], new()
    {
        Composition = SalrynLocalComposition.Create(db.Paths), Url = "http://127.0.0.1:0", TestHostNonce = nonce,
        EnableStaticWebAssets = true, WriteDevelopmentDatabasePath = false
    });
    await restarted.StartAsync();
    start.Environment["CAFE_QA_URL"] = restarted.Urls.Single();
    start.Environment["CAFE_QA_REPLAY"] = "1";
    using var replayBrowser = Process.Start(start) ?? throw new InvalidOperationException("Restart browser did not start.");
    await replayBrowser.WaitForExitAsync();
    await restarted.StopAsync();
    if (replayBrowser.ExitCode != 0) throw new InvalidOperationException("Restart replay failed; retained synthetic evidence: " + root);
    int checks = 0;
    void Equal(long expected, string sql) { long actual = db.ScalarLong(sql); if (actual != expected) throw new InvalidOperationException($"Ledger assertion failed: {sql}, expected {expected}, actual {actual}"); checks++; }
    Equal(3, "SELECT COUNT(*) FROM sales;");
    Equal(1500, "SELECT SUM(total_cents) FROM sales;");
    Equal(750, "SELECT SUM(debit_cents) FROM accounts_receivable_entries;");
    Equal(7000, "SELECT stock_on_hand_x1000 FROM products WHERE sku='C4-PRODUCT';");
    Equal(3, "SELECT COUNT(*) FROM cafe_orders WHERE state='Completed';");
    Equal(3, "SELECT COUNT(*) FROM sales WHERE receipt_snapshot IS NOT NULL AND length(trim(receipt_snapshot)) > 0;");
    Equal(2, "SELECT COUNT(*) FROM cafe_print_jobs;");
    Equal(2, "SELECT COUNT(*) FROM cafe_print_attempts;");
    Equal(1, "SELECT COUNT(*) FROM store_settings WHERE store_name='Salryn Cafe Live QA';");
    if (db.ScalarLong("SELECT COUNT(*) FROM menu_revisions;") < 2) throw new InvalidOperationException("Expected published menu revision after B3 live publish.");
    checks++;
    Console.WriteLine($"PASS: live React/Host cashier journeys and {checks} persisted SQLite assertions. Root: {root}. DEBUG_DEVELOPMENT_LICENSE; not Release license acceptance.");
}

static async Task RunC1RecoveryProofAsync()
{
    string? overrideRoot = Environment.GetEnvironmentVariable("CAFE_C1_ARTIFACT_ROOT");
    string root = !string.IsNullOrWhiteSpace(overrideRoot)
        ? overrideRoot
        : Path.Combine(Path.GetTempPath(), "salryn-c1-recovery-proof-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string dbRoot = Path.Combine(root, "db");

    var db = C4ProofDatabase.CreateEmpty(dbRoot);
    const string managerPassword = "Synthetic-Cafe-Live!2026";
    const string cashierPassword = "Synthetic-Cashier!2026";
    string now = DateTimeOffset.UtcNow.ToString("O");

    db.Execute(
        "UPDATE deployment_identity SET store_id=$store, updated_at=$at;",
        ("$store", C4ProofIds.StoreId.ToString("D")),
        ("$at", now));

    db.Execute("""
        INSERT INTO store_settings(
            id, store_name, business_name, address, contact_number, tin,
            currency_label, receipt_width_mm, receipt_header, receipt_footer,
            tax_mode_enabled, shift_mode_enabled, allow_negative_stock, created_at, updated_at)
        VALUES($id, 'Salryn Cafe C1 Proof', 'Salryn Cafe Test Roastery', '100 Recovery Lane', '09170000000', NULL,
            'PHP', 80, 'Salryn Cafe', 'Synthetic Disposable Proof', 0, 1, 0, $at, NULL);
        """,
        ("$id", Guid.NewGuid().ToString("D")),
        ("$at", now));

    string adminRoleId = db.ScalarString("SELECT id FROM roles WHERE name='Admin' LIMIT 1;")
        ?? throw new InvalidOperationException("Admin role missing.");
    db.Execute("""
        INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at, updated_at)
        VALUES($id, 'c4-proof', 'C1 Proof Manager', $hash, $role, 1, $at, NULL);
        """,
        ("$id", C4ProofIds.ActorUserId.ToString("D")),
        ("$hash", SalrynPasswordHasher.HashPassword(managerPassword).Value),
        ("$role", adminRoleId),
        ("$at", now));

    Guid cashierUserId = Guid.NewGuid();
    db.Execute("""
        INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at, updated_at)
        VALUES($id, 'c1-cashier', 'C1 Viewer Cashier', $hash, $role, 1, $at, NULL);
        """,
        ("$id", cashierUserId.ToString("D")),
        ("$hash", SalrynPasswordHasher.HashPassword(cashierPassword).Value),
        ("$role", DefaultSeedData.CashierRoleId),
        ("$at", now));

    if (db.ScalarLong("SELECT COUNT(*) FROM products;") != 0 ||
        db.ScalarLong("SELECT COUNT(*) FROM menu_variants;") != 0 ||
        db.ScalarLong("SELECT COUNT(*) FROM menu_revisions;") != 0)
    {
        throw new InvalidOperationException("Expected empty C1 menu catalog prior to proof execution.");
    }

    var repo = ResolveRepositoryRoot();
    string playwrightPkg = ResolvePlaywrightPackageJson(repo);
    string axePath = Path.Combine(repo.FullName, "apps", "cafe-web", "node_modules", "axe-core", "axe.min.js");
    if (!File.Exists(axePath))
    {
        throw new InvalidOperationException("axe-core script not found at: " + axePath);
    }

    string nonce = Guid.NewGuid().ToString("N");
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
        var phase1 = CreateBrowserProcess(
            Path.Combine(AppContext.BaseDirectory, "c1-recovery-browser.mjs"),
            playwrightPkg,
            axePath,
            host1.Urls.Single(),
            nonce,
            managerPassword,
            cashierPassword,
            root,
            phase: "1",
            fencedOperationId: null);
        using var proc1 = Process.Start(phase1) ?? throw new InvalidOperationException("Phase 1 browser failed to start.");
        await proc1.WaitForExitAsync();
        await host1.StopAsync();
        if (proc1.ExitCode != 0)
        {
            throw new InvalidOperationException($"C1 recovery Phase 1 browser failed ({proc1.ExitCode}). Artifacts: {root}");
        }
    }

    // Seed a real server-side in-flight execution fence row matching POST /api/menu/categories
    // for "Fenced Category" by manager c4-proof so Host 2 returns 409 ERR_IDEMPOTENCY_OUTCOME_UNKNOWN.
    Guid fencedOpId = Guid.NewGuid();
    var canonicalFencePayload = new
    {
        route = new Dictionary<string, string?>(StringComparer.Ordinal),
        query = string.Empty,
        args = new List<object?> { new MenuCategoryMutation(null, "Fenced Category", true) }
    };
    string fencedRequestHash = CafeMutationIdempotencyGuard.ComputeRequestHash(
        "POST /api/menu/categories",
        C4ProofIds.ActorUserId,
        canonicalFencePayload);
    db.Execute("""
        INSERT INTO cafe_mutation_execution_fences(operation_id, endpoint_key, actor_user_id, request_hash, created_at_utc)
        VALUES($op, 'POST /api/menu/categories', $actor, $hash, $at);
        """,
        ("$op", fencedOpId.ToString("D")),
        ("$actor", C4ProofIds.ActorUserId.ToString("D")),
        ("$hash", fencedRequestHash),
        ("$at", DateTimeOffset.UtcNow.ToString("O")));

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
        var phase2 = CreateBrowserProcess(
            Path.Combine(AppContext.BaseDirectory, "c1-recovery-browser.mjs"),
            playwrightPkg,
            axePath,
            host2.Urls.Single(),
            nonce,
            managerPassword,
            cashierPassword,
            root,
            phase: "2",
            fencedOperationId: fencedOpId.ToString("D"));
        using var proc2 = Process.Start(phase2) ?? throw new InvalidOperationException("Phase 2 browser failed to start.");
        await proc2.WaitForExitAsync();
        await host2.StopAsync();
        if (proc2.ExitCode != 0)
        {
            throw new InvalidOperationException($"C1 recovery Phase 2 browser failed ({proc2.ExitCode}). Artifacts: {root}");
        }
    }

    int checks = 0;
    void Equal(long expected, string sql)
    {
        long actual = db.ScalarLong(sql);
        if (actual != expected)
        {
            throw new InvalidOperationException($"SQLite assertion failed: {sql}; expected={expected}, actual={actual}");
        }
        checks++;
    }

    Equal(1, "SELECT COUNT(*) FROM categories WHERE name='Signature Espresso' AND is_active=1;");
    Equal(1, "SELECT COUNT(*) FROM categories WHERE name='Iced Classics' AND is_active=1;");
    Equal(1, "SELECT COUNT(*) FROM categories WHERE name='Idempotency Valid Category' AND is_active=1;");
    Equal(0, "SELECT COUNT(*) FROM categories WHERE name IN ('Missing Key Category','Conflicting Payload Category','Fenced Category','Changed After Fence','Unauthorized Category','Signed Out Category');");
    Equal(1, "SELECT COUNT(*) FROM products WHERE sku='CAFE-ISL-001' AND name='Iced Spanish Latte DRAFT UNPUBLISHED' AND selling_price_cents=99900 AND is_active=1;");
    Equal(0, "SELECT COUNT(*) FROM products WHERE name='Spanish Latte';");
    Equal(3, "SELECT COUNT(*) FROM menu_variants WHERE is_active=1;");
    Equal(1, "SELECT COUNT(*) FROM menu_variants WHERE name='12oz Hot' AND selling_price_cents=15000 AND sku='CAFE-ISL-12H';");
    Equal(1, "SELECT COUNT(*) FROM menu_variants WHERE name='16oz Iced' AND selling_price_cents=17500 AND sku='CAFE-ISL-16I';");
    Equal(2, "SELECT COUNT(*) FROM menu_modifier_groups WHERE is_active=1;");
    Equal(4, "SELECT COUNT(*) FROM menu_modifier_options WHERE is_active=1;");
    Equal(1, "SELECT COUNT(*) FROM menu_item_modifier_groups;");
    Equal(1, "SELECT COUNT(*) FROM menu_variant_modifier_groups;");
    Equal(1, "SELECT COUNT(*) FROM menu_revisions;");
    Equal(1, "SELECT COUNT(*) FROM menu_revision_items WHERE name='Iced Spanish Latte' AND selling_price_cents=16000;");
    Equal(0, "SELECT COUNT(*) FROM menu_revision_items WHERE name LIKE '%DRAFT UNPUBLISHED%';");
    Equal(1, "SELECT COUNT(*) FROM menu_current_publication;");
    if (db.ScalarLong("SELECT COUNT(*) FROM cafe_mutation_idempotency;") < 10)
    {
        throw new InvalidOperationException("Expected durable idempotency records in cafe_mutation_idempotency.");
    }
    checks++;

    var summary = new
    {
        mission = "FORGE-SALRYN-CAFE-CANONICAL-RECOVERY-R0-C1-PROOF",
        verdict = "PASS",
        licenseMode = "DEBUG_DEVELOPMENT_LICENSE (SALRYN_LICENSE_DEVELOPMENT_MODE=1; not Release entitlement proof)",
        disposableRoot = root,
        sqliteAssertions = checks,
        publishedRevisionId = db.ScalarString("SELECT revision_id FROM menu_current_publication LIMIT 1;"),
        publishedContentHash = db.ScalarString("SELECT content_hash FROM menu_current_publication LIMIT 1;"),
        publishedPublicationHash = db.ScalarString("SELECT publication_hash FROM menu_current_publication LIMIT 1;"),
        draftItemName = db.ScalarString("SELECT name FROM products WHERE sku='CAFE-ISL-001' LIMIT 1;"),
        draftItemPriceCents = db.ScalarLong("SELECT selling_price_cents FROM products WHERE sku='CAFE-ISL-001' LIMIT 1;"),
        idempotencyRecordCount = db.ScalarLong("SELECT COUNT(*) FROM cafe_mutation_idempotency;")
    };
    File.WriteAllText(
        Path.Combine(root, "c1-sqlite-summary.json"),
        JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"PASS: C1 canonical recovery real Host + Chromium + {checks} SQLite assertions. Root: {root}. DEBUG_DEVELOPMENT_LICENSE.");
}

static ProcessStartInfo CreateBrowserProcess(
    string scriptPath,
    string playwrightPackageJson,
    string axePath,
    string url,
    string nonce,
    string managerPassword,
    string cashierPassword,
    string root,
    string phase,
    string? fencedOperationId)
{
    var start = new ProcessStartInfo("node") { UseShellExecute = false };
    start.ArgumentList.Add(scriptPath);
    start.Environment["CAFE_QA_PLAYWRIGHT_PACKAGE"] = playwrightPackageJson;
    start.Environment["CAFE_QA_AXE_PATH"] = axePath;
    start.Environment["CAFE_QA_URL"] = url;
    start.Environment["CAFE_QA_NONCE"] = nonce;
    start.Environment["CAFE_QA_PASSWORD"] = managerPassword;
    start.Environment["CAFE_QA_CASHIER_PASSWORD"] = cashierPassword;
    start.Environment["CAFE_QA_ROOT"] = root;
    start.Environment["CAFE_QA_PHASE"] = phase;
    if (!string.IsNullOrEmpty(fencedOperationId))
    {
        start.Environment["CAFE_QA_FENCED_OP_ID"] = fencedOperationId;
    }
    return start;
}

static DirectoryInfo ResolveRepositoryRoot()
{
    var repo = new DirectoryInfo(AppContext.BaseDirectory);
    while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Salryn.sln")))
    {
        repo = repo.Parent;
    }
    return repo ?? throw new InvalidOperationException("Repository root required.");
}

static string ResolvePlaywrightPackageJson(DirectoryInfo repo)
{
    string? envOverride = Environment.GetEnvironmentVariable("CAFE_QA_PLAYWRIGHT_PACKAGE");
    if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
    {
        return envOverride;
    }
    string localWeb = Path.Combine(repo.FullName, "src", "Salryn.App.Web", "package.json");
    if (Directory.Exists(Path.Combine(repo.FullName, "src", "Salryn.App.Web", "node_modules")))
    {
        return localWeb;
    }
    string siblingWeb = Path.GetFullPath(Path.Combine(repo.FullName, "..", "i0", "src", "Salryn.App.Web", "package.json"));
    if (File.Exists(siblingWeb) &&
        Directory.Exists(Path.Combine(Path.GetDirectoryName(siblingWeb)!, "node_modules")))
    {
        return siblingWeb;
    }
    return localWeb;
}
#endif

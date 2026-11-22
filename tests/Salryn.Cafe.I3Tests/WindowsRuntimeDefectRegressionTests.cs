using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class WindowsRuntimeDefectRegressionTests
{
    private const string AdminPassword = "Synthetic-Admin!2026";
    private const string CashierPassword = "Synthetic-Cashier!2026";

    [Fact]
    public async Task B1_ReceiptPrint_OrderCompletionPersistsReceiptSnapshot_AndPrintReplayAndMissingSnapshotFailClosed()
    {
        string root = NewTempRoot("win-b1-receipt");
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            SetUserPassword(factory, completion.ActorUserId, AdminPassword);
            string username = ReadUsername(factory, completion.ActorUserId);
            Guid stationId = PublishStation(factory, completion.StoreId, completion.ActorUserId, "Front Receipt", CafePrinterStationKind.CustomerReceipt);

            var coordinator = new SqliteCafeOrderCompletionCoordinator(factory);
            var completed = coordinator.Complete(completion);
            Assert.True(completed.IsSuccess, completed.IsFailure ? completed.ErrorDetail.Message : null);

            string? persistedSnapshot = ScalarString(factory, "SELECT receipt_snapshot FROM sales WHERE id = $id;", ("$id", completed.Value.SaleId.ToString("D")));
            Assert.False(string.IsNullOrWhiteSpace(persistedSnapshot));
            Assert.Contains(completed.Value.ReceiptNumber, persistedSnapshot!, StringComparison.Ordinal);

            // Insert a legacy/corrupted sale with NULL receipt_snapshot to prove fail-closed enforcement remains intact.
            Guid missingSnapshotSaleId = Guid.NewGuid();
            ExecuteSql(factory, """
                INSERT INTO sales(
                    id, receipt_no, sale_date, cashier_user_id, cashier_session_id,
                    subtotal_cents, discount_cents, tax_cents, total_cents, amount_paid_cents, change_cents,
                    status, payment_status, balance_due_cents, receipt_snapshot, created_at)
                SELECT $id, 'MISSING-SNAP-001', sale_date, cashier_user_id, cashier_session_id,
                       subtotal_cents, discount_cents, tax_cents, total_cents, amount_paid_cents, change_cents,
                       status, payment_status, balance_due_cents, NULL, created_at
                FROM sales WHERE id = $source;
                """,
                ("$id", missingSnapshotSaleId.ToString("D")),
                ("$source", completed.Value.SaleId.ToString("D")));

            Guid createOpId = Guid.NewGuid();
            Guid attemptOpId = Guid.NewGuid();
            Guid jobId;

            await using (var host = await StartHostAsync(root))
            {
                using var client = await LoginClientAsync(host, username, AdminPassword);

                // Missing receipt snapshot must still fail closed with 409 CafePrint.ReceiptSnapshotMissing.
                using var missingResp = await client.PostAsJsonAsync("/api/cafe/print/jobs/receipt", new
                {
                    operationId = Guid.NewGuid(),
                    stationId,
                    saleId = missingSnapshotSaleId,
                    copyKind = "Original"
                });
                Assert.Equal(HttpStatusCode.Conflict, missingResp.StatusCode);
                Assert.Contains("CafePrint.ReceiptSnapshotMissing", await missingResp.Content.ReadAsStringAsync(), StringComparison.Ordinal);

                // Valid completed order sale must create receipt print job from persisted snapshot.
                var createPayload = new
                {
                    operationId = createOpId,
                    stationId,
                    saleId = completed.Value.SaleId,
                    copyKind = "Original"
                };
                using var createResp = await client.PostAsJsonAsync("/api/cafe/print/jobs/receipt", createPayload);
                string createJson = await createResp.Content.ReadAsStringAsync();
                Assert.True(createResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on receipt job create, got {(int)createResp.StatusCode}: {createJson}");
                using JsonDocument createDoc = JsonDocument.Parse(createJson);
                jobId = createDoc.RootElement.GetProperty("job").GetProperty("jobId").GetGuid();
                Assert.Equal(persistedSnapshot, createDoc.RootElement.GetProperty("job").GetProperty("snapshotText").GetString());

                // Attempt endpoint requires client operationId and succeeds when provided in body.
                using var attemptMissingOp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { });
                Assert.Equal(HttpStatusCode.BadRequest, attemptMissingOp.StatusCode);

                using var attemptResp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = attemptOpId });
                string attemptJson = await attemptResp.Content.ReadAsStringAsync();
                Assert.True(attemptResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on receipt attempt, got {(int)attemptResp.StatusCode}: {attemptJson}");

                // Replay with identical operationId returns cached response without duplicating attempts.
                using var replayResp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = attemptOpId });
                Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
                Assert.True(replayResp.Headers.TryGetValues("X-Idempotency-Replayed", out var replayVals) && replayVals.Contains("true"));

                // Reusing createOpId on /attempt must conflict (409).
                using var conflictResp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = createOpId });
                Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);

                await host.StopAsync();
            }

            // Restart recovery: replay after fresh Host boot returns persisted idempotent result without adding attempts.
            long attemptsBeforeRestart = ScalarLong(factory, "SELECT COUNT(*) FROM cafe_print_attempts WHERE job_id = $job;", ("$job", jobId.ToString("D")));
            Assert.Equal(1, attemptsBeforeRestart);

            await using (var restarted = await StartHostAsync(root))
            {
                using var client = await LoginClientAsync(restarted, username, AdminPassword);
                using var postRestartReplay = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = attemptOpId });
                Assert.Equal(HttpStatusCode.OK, postRestartReplay.StatusCode);
                Assert.True(postRestartReplay.Headers.TryGetValues("X-Idempotency-Replayed", out var vals) && vals.Contains("true"));
                Assert.Equal(1, ScalarLong(factory, "SELECT COUNT(*) FROM cafe_print_attempts WHERE job_id = $job;", ("$job", jobId.ToString("D"))));
                await restarted.StopAsync();
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task B2_KitchenPrint_AcceptsBodyOperationIdOnAttempt_EnforcesAuthIdempotencyAndConflict()
    {
        string root = NewTempRoot("win-b2-kitchen");
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            SetUserPassword(factory, completion.ActorUserId, AdminPassword);
            string username = ReadUsername(factory, completion.ActorUserId);
            Guid kitchenStationId = PublishStation(factory, completion.StoreId, completion.ActorUserId, "Main Kitchen", CafePrinterStationKind.Kitchen);
            Guid orderRevisionId = Guid.Parse(ScalarString(factory, "SELECT current_revision_id FROM cafe_orders WHERE order_id = $id;", ("$id", completion.OrderId.ToString("D")))!);

            Guid createOpId = Guid.NewGuid();
            Guid attemptOpId = Guid.NewGuid();
            Guid jobId;

            await using (var host = await StartHostAsync(root))
            {
                using var anon = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
                using var client = await LoginClientAsync(host, username, AdminPassword);

                using var createResp = await client.PostAsJsonAsync("/api/cafe/print/jobs/kitchen", new
                {
                    operationId = createOpId,
                    stationId = kitchenStationId,
                    orderId = completion.OrderId,
                    orderRevisionId,
                    copyKind = "Original"
                });
                string createJson = await createResp.Content.ReadAsStringAsync();
                Assert.True(createResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on kitchen job create, got {(int)createResp.StatusCode}: {createJson}");
                using JsonDocument doc = JsonDocument.Parse(createJson);
                jobId = doc.RootElement.GetProperty("job").GetProperty("jobId").GetGuid();

                // Unauthenticated attempt must be 401.
                using var anonAttempt = await anon.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = attemptOpId });
                Assert.Equal(HttpStatusCode.Unauthorized, anonAttempt.StatusCode);

                // Missing operationId must fail closed with 400.
                using var missingOp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { });
                Assert.Equal(HttpStatusCode.BadRequest, missingOp.StatusCode);

                // Valid body operationId must succeed.
                using var attemptResp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = attemptOpId });
                string attemptJson = await attemptResp.Content.ReadAsStringAsync();
                Assert.True(attemptResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on kitchen attempt, got {(int)attemptResp.StatusCode}: {attemptJson}");

                // Exact retry with same operationId replays idempotently without adding another DB attempt.
                using var replayResp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = attemptOpId });
                Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
                Assert.True(replayResp.Headers.TryGetValues("X-Idempotency-Replayed", out var replayVals) && replayVals.Contains("true"));
                Assert.Equal(1, ScalarLong(factory, "SELECT COUNT(*) FROM cafe_print_attempts WHERE job_id = $job;", ("$job", jobId.ToString("D"))));

                // Reusing createOpId on /attempt must return 409 ERR_IDEMPOTENCY_CONFLICT.
                using var conflictResp = await client.PostAsJsonAsync($"/api/cafe/print/jobs/{jobId:D}/attempt", new { operationId = createOpId });
                Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);

                await host.StopAsync();
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task B3_BackofficeMenuAndRecipePublish_AcceptsBodyOperationId_EnforcesAuthIdempotencyAndConflict()
    {
        string root = NewTempRoot("win-b3-publish");
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            SetUserPassword(factory, completion.ActorUserId, AdminPassword);
            string adminUsername = ReadUsername(factory, completion.ActorUserId);

            // Seed valid draft menu variant for every active product so C1 Menu.PublishCurrent invariants hold.
            var setupComposition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root));
            var draftMenu = setupComposition.MenuCatalog.GetDraft();
            Assert.True(draftMenu.IsSuccess);
            foreach (var item in draftMenu.Value.Items.Where(i => i.IsActive))
            {
                if (!draftMenu.Value.Variants.Any(v => v.ProductId == item.ProductId && v.IsActive))
                {
                    var savedVariant = setupComposition.MenuCatalog.SaveVariant(new MenuVariantMutation(
                        Id: null,
                        ProductId: item.ProductId,
                        Name: "Regular",
                        Sku: $"VAR-{item.ProductId:N}"[..16],
                        Barcode: null,
                        SellingPriceCents: item.SellingPriceCents,
                        CostPriceCents: item.CostPriceCents,
                        IsActive: true,
                        IsTemporarilyUnavailable: false,
                        DisplayOrder: 1));
                    Assert.True(savedVariant.IsSuccess, savedVariant.IsFailure ? savedVariant.ErrorDetail.Message : null);
                }
            }

            // Seed a cashier user without Products.Manage or Recipes.Publish to verify 403 does not pin operationId.
            Guid cashierId = Guid.NewGuid();
            ExecuteSql(factory, """
                INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at, updated_at)
                VALUES($id, 'b3-cashier', 'B3 Cashier', $hash, $role, 1, $at, NULL);
                """,
                ("$id", cashierId.ToString("D")),
                ("$hash", SalrynPasswordHasher.HashPassword(CashierPassword).Value),
                ("$role", DefaultSeedData.CashierRoleId),
                ("$at", DateTimeOffset.UtcNow.ToString("O")));

            Guid menuOpId = Guid.NewGuid();
            Guid recipeOpId = Guid.NewGuid();
            long menuRevsBefore = ScalarLong(factory, "SELECT COUNT(*) FROM menu_revisions;");
            long recipeRevsBefore = ScalarLong(factory, "SELECT COUNT(*) FROM recipe_book_revisions;");

            await using (var host = await StartHostAsync(root))
            {
                using var anon = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
                using var cashier = await LoginClientAsync(host, "b3-cashier", CashierPassword);
                using var admin = await LoginClientAsync(host, adminUsername, AdminPassword);

                // Unauthenticated -> 401
                Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/menu/publish", new { operationId = menuOpId })).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/cafe/recipes/publish", new { operationId = recipeOpId })).StatusCode);

                // Missing operationId -> 400
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/menu/publish", new { })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/cafe/recipes/publish", new { })).StatusCode);

                // Unauthorized role -> 403 and must NOT pin operationId
                Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/menu/publish", new { operationId = menuOpId })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/cafe/recipes/publish", new { operationId = recipeOpId })).StatusCode);

                // Authorized publish with body operationId -> 200 OK
                using var menuResp = await admin.PostAsJsonAsync("/api/menu/publish", new { operationId = menuOpId });
                string menuBody = await menuResp.Content.ReadAsStringAsync();
                Assert.True(menuResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on /api/menu/publish, got {(int)menuResp.StatusCode}: {menuBody}");

                using var recipeResp = await admin.PostAsJsonAsync("/api/cafe/recipes/publish", new { operationId = recipeOpId });
                string recipeBody = await recipeResp.Content.ReadAsStringAsync();
                Assert.True(recipeResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on /api/cafe/recipes/publish, got {(int)recipeResp.StatusCode}: {recipeBody}");

                long menuRevsAfter = ScalarLong(factory, "SELECT COUNT(*) FROM menu_revisions;");
                long recipeRevsAfter = ScalarLong(factory, "SELECT COUNT(*) FROM recipe_book_revisions;");
                Assert.Equal(menuRevsBefore + 1, menuRevsAfter);
                Assert.Equal(recipeRevsBefore + 1, recipeRevsAfter);

                // Exact replay must return cached 200 without creating duplicate revisions.
                using var menuReplay = await admin.PostAsJsonAsync("/api/menu/publish", new { operationId = menuOpId });
                Assert.Equal(HttpStatusCode.OK, menuReplay.StatusCode);
                Assert.True(menuReplay.Headers.TryGetValues("X-Idempotency-Replayed", out var mv) && mv.Contains("true"));
                Assert.Equal(menuRevsAfter, ScalarLong(factory, "SELECT COUNT(*) FROM menu_revisions;"));

                using var recipeReplay = await admin.PostAsJsonAsync("/api/cafe/recipes/publish", new { operationId = recipeOpId });
                Assert.Equal(HttpStatusCode.OK, recipeReplay.StatusCode);
                Assert.True(recipeReplay.Headers.TryGetValues("X-Idempotency-Replayed", out var rv) && rv.Contains("true"));
                Assert.Equal(recipeRevsAfter, ScalarLong(factory, "SELECT COUNT(*) FROM recipe_book_revisions;"));

                // Reusing menuOpId on /api/cafe/recipes/publish must conflict (409).
                using var crossEndpointConflict = await admin.PostAsJsonAsync("/api/cafe/recipes/publish", new { operationId = menuOpId });
                Assert.Equal(HttpStatusCode.Conflict, crossEndpointConflict.StatusCode);

                await host.StopAsync();
            }

            // Reload/restart persistence: current publication reflects newly published revisions.
            await using (var restarted = await StartHostAsync(root))
            {
                using var admin = await LoginClientAsync(restarted, adminUsername, AdminPassword);
                using var currentMenu = await admin.GetAsync("/api/menu/publication/current");
                Assert.Equal(HttpStatusCode.OK, currentMenu.StatusCode);
                using var currentRecipes = await admin.GetAsync("/api/cafe/recipes/publication/current");
                Assert.Equal(HttpStatusCode.OK, currentRecipes.StatusCode);
                await restarted.StopAsync();
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task B4_StoreSettings_ReadUpdatePersistAcrossRestart_AndEnforceAuthorization_WhenRowInitiallyAbsent()
    {
        string root = NewTempRoot("win-b4-settings");
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            SetUserPassword(factory, completion.ActorUserId, AdminPassword);
            string adminUsername = ReadUsername(factory, completion.ActorUserId);

            // Seed cashier without Settings.Manage.
            Guid cashierId = Guid.NewGuid();
            ExecuteSql(factory, """
                INSERT INTO users(id, username, display_name, password_hash, role_id, status, created_at, updated_at)
                VALUES($id, 'b4-cashier', 'B4 Cashier', $hash, $role, 1, $at, NULL);
                """,
                ("$id", cashierId.ToString("D")),
                ("$hash", SalrynPasswordHasher.HashPassword(CashierPassword).Value),
                ("$role", DefaultSeedData.CashierRoleId),
                ("$at", DateTimeOffset.UtcNow.ToString("O")));

            // Confirm pre-condition: store_settings has 0 rows (same state as C4ProofDatabase / non-first-run DB).
            Assert.Equal(0, ScalarLong(factory, "SELECT COUNT(*) FROM store_settings;"));

            await using (var host = await StartHostAsync(root))
            {
                using var anon = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
                using var cashier = await LoginClientAsync(host, "b4-cashier", CashierPassword);
                using var admin = await LoginClientAsync(host, adminUsername, AdminPassword);

                // Anonymous -> 401
                Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/settings/store")).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync("/api/settings/store", new
                {
                    storeName = "Anon Hijack",
                    businessName = "Anon",
                    currencyLabel = "PHP",
                    receiptWidthMm = 80,
                    printerMode = "Disabled"
                })).StatusCode);

                // Authenticated GET when store_settings has 0 rows -> 200 OK default snapshot, NO DB mutation on GET!
                using var getResp = await admin.GetAsync("/api/settings/store");
                string getBody = await getResp.Content.ReadAsStringAsync();
                Assert.True(getResp.StatusCode == HttpStatusCode.OK, $"Expected 200 on GET /api/settings/store, got {(int)getResp.StatusCode}: {getBody}");
                Assert.Equal(0, ScalarLong(factory, "SELECT COUNT(*) FROM store_settings;"));

                // Cashier without Settings.Manage PUT -> 403 Forbidden, still 0 rows!
                using var cashierPut = await cashier.PutAsJsonAsync("/api/settings/store", new
                {
                    storeName = "Unauthorized Store",
                    businessName = "Unauthorized",
                    currencyLabel = "PHP",
                    receiptWidthMm = 80,
                    printerMode = "Disabled"
                });
                Assert.Equal(HttpStatusCode.Forbidden, cashierPut.StatusCode);
                Assert.Equal(0, ScalarLong(factory, "SELECT COUNT(*) FROM store_settings;"));

                // Admin PUT persists initial row to canonical store_settings table -> 204 No Content.
                using var adminPut = await admin.PutAsJsonAsync("/api/settings/store", new
                {
                    storeName = "Cafe Yza Main",
                    businessName = "Salryn Cafe Ventures",
                    address = "123 Coffee Street",
                    contactNumber = "0917-555-0100",
                    tin = "123-456-789-000",
                    currencyLabel = "PHP",
                    receiptWidthMm = 80,
                    receiptHeader = "Freshly Brewed",
                    receiptFooter = "Salamat po!",
                    printerMode = "Disabled",
                    windowsPrinterName = (string?)null
                });
                Assert.Equal(HttpStatusCode.NoContent, adminPut.StatusCode);
                Assert.Equal(1, ScalarLong(factory, "SELECT COUNT(*) FROM store_settings;"));
                Assert.True(ScalarLong(factory, "SELECT COUNT(*) FROM audit_logs WHERE entity_type = 'StoreSettings';") >= 1);

                await host.StopAsync();
            }

            // Restart verification: persisted settings survive restart and subsequent update keeps single canonical row.
            await using (var restarted = await StartHostAsync(root))
            {
                using var admin = await LoginClientAsync(restarted, adminUsername, AdminPassword);
                using var afterRestartGet = await admin.GetAsync("/api/settings/store");
                Assert.Equal(HttpStatusCode.OK, afterRestartGet.StatusCode);
                using JsonDocument doc = JsonDocument.Parse(await afterRestartGet.Content.ReadAsStringAsync());
                Assert.Equal("Cafe Yza Main", doc.RootElement.GetProperty("storeName").GetString());
                Assert.Equal("Salryn Cafe Ventures", doc.RootElement.GetProperty("businessName").GetString());
                Assert.Equal("Salamat po!", doc.RootElement.GetProperty("receiptFooter").GetString());

                using var secondUpdate = await admin.PutAsJsonAsync("/api/settings/store", new
                {
                    storeName = "Cafe Yza Roastery",
                    businessName = "Salryn Cafe Ventures",
                    address = "123 Coffee Street",
                    contactNumber = "0917-555-0100",
                    tin = "123-456-789-000",
                    currencyLabel = "PHP",
                    receiptWidthMm = 58,
                    receiptHeader = "Freshly Brewed",
                    receiptFooter = "See you again!",
                    printerMode = "Disabled",
                    windowsPrinterName = (string?)null
                });
                Assert.Equal(HttpStatusCode.NoContent, secondUpdate.StatusCode);
                Assert.Equal(1, ScalarLong(factory, "SELECT COUNT(*) FROM store_settings;"));
                Assert.Equal("Cafe Yza Roastery", ScalarString(factory, "SELECT store_name FROM store_settings LIMIT 1;"));
                await restarted.StopAsync();
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task B1_OriginEcho_V1AndV2_MatchPersistedTrimmedReceiptSnapshot_AndRenderFees()
    {
        // V1 cash-only sale origin-echo verification (exercises LocalSaleCommittedV1FactVerifier.VerifyOriginEcho)
        string rootV1 = NewTempRoot("win-b1-echo-v1");
        try
        {
            var (factory, baseCompletion) = CanonicalOrderCompletionFixture.Create(rootV1);
            var v1Completion = new CafeOrderCompletionMutation(
                baseCompletion.CompletionOperationId,
                baseCompletion.SaleCommittedOperationId,
                baseCompletion.DeviceId,
                baseCompletion.OrderId,
                baseCompletion.StoreId,
                baseCompletion.ExpectedVersion,
                baseCompletion.ActorUserId,
                baseCompletion.OccurredAtUtc,
                new[] { new CafeOrderPaymentMutation(PaymentMethod.Cash, 1000) });

            var coordinator = new SqliteCafeOrderCompletionCoordinator(factory);
            var completed = coordinator.Complete(v1Completion);
            Assert.True(completed.IsSuccess, completed.IsFailure ? completed.ErrorDetail.Message : null);

            string? persisted = ScalarString(factory, "SELECT receipt_snapshot FROM sales WHERE id = $id;", ("$id", completed.Value.SaleId.ToString("D")));
            Assert.False(string.IsNullOrWhiteSpace(persisted));
            Assert.Equal(persisted!.Trim(), persisted);

            await AssertOriginEchoProjectsCleanlyAsync(
                factory,
                v1Completion.StoreId,
                v1Completion.DeviceId,
                v1Completion.SaleCommittedOperationId,
                AppliedBusinessChangeContract.SaleCommittedV1AppliedV1,
                AppliedBusinessChangeContract.SaleCommittedV1AppliedSchemaVersion);
        }
        finally
        {
            Cleanup(rootV1);
        }

        // V2 C5 sale with Service fee (exercises LocalSaleCommittedV2FactVerifier.VerifyOriginEcho + Fees line)
        string rootV2 = NewTempRoot("win-b1-echo-v2");
        try
        {
            var (factory, c5Completion) = CanonicalOrderCompletionFixture.Create(rootV2);
            var coordinator = new SqliteCafeOrderCompletionCoordinator(factory);
            var completed = coordinator.Complete(c5Completion);
            Assert.True(completed.IsSuccess, completed.IsFailure ? completed.ErrorDetail.Message : null);

            string? persisted = ScalarString(factory, "SELECT receipt_snapshot FROM sales WHERE id = $id;", ("$id", completed.Value.SaleId.ToString("D")));
            Assert.False(string.IsNullOrWhiteSpace(persisted));
            Assert.Equal(persisted!.Trim(), persisted);
            Assert.Contains("Fees    : PHP 0.50", persisted, StringComparison.Ordinal);

            await AssertOriginEchoProjectsCleanlyAsync(
                factory,
                c5Completion.StoreId,
                c5Completion.DeviceId,
                c5Completion.SaleCommittedOperationId,
                AppliedBusinessChangeContract.SaleCommittedV2AppliedV1,
                AppliedBusinessChangeContract.SaleCommittedV2AppliedSchemaVersion);
        }
        finally
        {
            Cleanup(rootV2);
        }
    }

    [Fact]
    public async Task B2_KitchenPrint_HeaderOnlyIdempotencyKey_ResolvesOperationIdAndReplays()
    {
        string root = NewTempRoot("win-b2-header-key");
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            SetUserPassword(factory, completion.ActorUserId, AdminPassword);
            string username = ReadUsername(factory, completion.ActorUserId);
            Guid kitchenStationId = PublishStation(factory, completion.StoreId, completion.ActorUserId, "Barista Header Key", CafePrinterStationKind.Kitchen);
            string revisionId = ScalarString(factory, "SELECT current_revision_id FROM cafe_orders WHERE order_id = $id;", ("$id", completion.OrderId.ToString("D")))!;

            await using var host = await StartHostAsync(root);
            using var client = await LoginClientAsync(host, username, AdminPassword);

            Guid headerOpId = Guid.NewGuid();
            using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/cafe/print/jobs/kitchen")
            {
                Content = JsonContent.Create(new
                {
                    stationId = kitchenStationId,
                    orderId = completion.OrderId,
                    orderRevisionId = Guid.Parse(revisionId),
                    copyKind = "Original"
                })
            };
            req1.Headers.Add("Idempotency-Key", headerOpId.ToString("D"));
            using var res1 = await client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
            using var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
            string jobId1 = doc1.RootElement.GetProperty("job").GetProperty("jobId").GetString()!;

            using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/cafe/print/jobs/kitchen")
            {
                Content = JsonContent.Create(new
                {
                    stationId = kitchenStationId,
                    orderId = completion.OrderId,
                    orderRevisionId = Guid.Parse(revisionId),
                    copyKind = "Original"
                })
            };
            req2.Headers.Add("Idempotency-Key", headerOpId.ToString("D"));
            using var res2 = await client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
            using var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
            Assert.Equal(jobId1, doc2.RootElement.GetProperty("job").GetProperty("jobId").GetString());
            Assert.Equal(1, ScalarLong(factory, "SELECT COUNT(*) FROM cafe_print_jobs WHERE operation_id = $op;", ("$op", headerOpId.ToString("D"))));

            await host.StopAsync();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task B4_StoreSettings_ConcurrentInitialPut_SerializesCleanlyWithoutBusyFailure()
    {
        string root = NewTempRoot("win-b4-concurrent");
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            SetUserPassword(factory, completion.ActorUserId, AdminPassword);
            string username = ReadUsername(factory, completion.ActorUserId);
            ExecuteSql(factory, "DELETE FROM store_settings;");

            await using var host = await StartHostAsync(root);
            using var client1 = await LoginClientAsync(host, username, AdminPassword);
            using var client2 = await LoginClientAsync(host, username, AdminPassword);

            object MakePayload(string name) => new
            {
                storeName = name,
                businessName = "Cafe Yza Inc.",
                address = "123 Coffee Ave",
                contactNumber = "09171234567",
                tin = "000-111-222-000",
                currencyLabel = "PHP",
                receiptWidthMm = 58,
                taxModeEnabled = false,
                shiftModeEnabled = true,
                receiptHeader = "Welcome",
                receiptFooter = "Thank you",
                printerMode = "Disabled",
                windowsPrinterName = (string?)null
            };

            var responses = await Task.WhenAll(
                client1.PutAsJsonAsync("/api/settings/store", MakePayload("Cafe Yza A")),
                client2.PutAsJsonAsync("/api/settings/store", MakePayload("Cafe Yza B")));

            foreach (var r in responses)
            {
                Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
                r.Dispose();
            }

            Assert.Equal(1, ScalarLong(factory, "SELECT COUNT(*) FROM store_settings;"));
            await host.StopAsync();
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static async Task AssertOriginEchoProjectsCleanlyAsync(
        SqliteConnectionFactory factory,
        Guid storeId,
        Guid originDeviceId,
        Guid saleCommittedOperationId,
        string changeType,
        int schemaVersion)
    {
        var outbox = new SqliteSyncOutboxStore(factory);
        var item = outbox.Find(saleCommittedOperationId);
        Assert.NotNull(item);

        Guid centralInstanceId = Guid.NewGuid();
        var pullStore = new SqliteSyncPullStore(factory);
        Assert.True(pullStore.InitializeRemoteState(storeId, centralInstanceId).IsSuccess);

        var env = item!.Envelope;
        var change = AppliedBusinessChangeProtocolV1.Create(
            centralInstanceId,
            serverSequence: 1,
            sourceOperationId: env.OperationId,
            originDeviceId: env.DeviceId,
            originLocalSequence: env.LocalSequence,
            storeId: env.StoreId,
            changeType: changeType,
            schemaVersion: schemaVersion,
            aggregateId: env.AggregateId,
            payloadJson: env.PayloadJson,
            createdAtUtc: env.OccurredAtLocal);

        var page = new SyncChangeFeedPageV1(centralInstanceId, 0, 1, new[] { change }, HasMore: false);
        Assert.True(pullStore.AcceptPage(storeId, page).IsSuccess);

        var projector = new SqliteSyncProjectionService(factory);
        var projected = await projector.ProjectNextAsync(storeId, originDeviceId);
        Assert.True(projected.IsSuccess, $"Origin echo failed: {projected.Code} - {projected.Message}");
        Assert.True(projected.IsOriginEcho);
    }

    private static string NewTempRoot(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> StartHostAsync(string root)
    {
        var license = new LocalLicenseService(
            new LocalLicenseFileStore(new(root, "absent-test-license.json")),
            new LocalLicenseValidator(new() { AllowDevelopmentMode = true, DevelopmentFeatures = LicenseFeatures.AllList }));

        var host = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root)),
            LicenseService = license,
            Url = "http://127.0.0.1:0",
            EnableStaticWebAssets = false,
            WriteDevelopmentDatabasePath = false
        });
        await host.StartAsync();
        return host;
    }

    private static async Task<HttpClient> LoginClientAsync(Microsoft.AspNetCore.Builder.WebApplication host, string username, string password)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(host.Urls.Single()) };
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static Guid PublishStation(SqliteConnectionFactory factory, Guid storeId, Guid actorUserId, string name, CafePrinterStationKind kind)
    {
        Guid stationId = Guid.NewGuid();
        var printService = new SqliteCafePrintService(factory);
        var res = printService.PublishStation(new CafePrinterStationMutation(
            Guid.NewGuid(), stationId, storeId, actorUserId, name, kind, name.ToLowerInvariant().Replace(' ', '-'), true, 0, DateTimeOffset.UtcNow));
        Assert.True(res.IsSuccess, res.IsFailure ? res.ErrorDetail.Message : null);
        return stationId;
    }

    private static void SetUserPassword(SqliteConnectionFactory factory, Guid userId, string password) =>
        ExecuteSql(factory, "UPDATE users SET password_hash = $hash WHERE id = $id;",
            ("$hash", SalrynPasswordHasher.HashPassword(password).Value), ("$id", userId.ToString("D")));

    private static string ReadUsername(SqliteConnectionFactory factory, Guid userId) =>
        ScalarString(factory, "SELECT username FROM users WHERE id = $id;", ("$id", userId.ToString("D")))!;

    private static void ExecuteSql(SqliteConnectionFactory factory, string sql, params (string Name, object? Value)[] parameters)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnectionFactory factory, string sql, params (string Name, object? Value)[] parameters)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static string? ScalarString(SqliteConnectionFactory factory, string sql, params (string Name, object? Value)[] parameters)
    {
        using var conn = factory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        object? v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToString(v);
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
    }
}

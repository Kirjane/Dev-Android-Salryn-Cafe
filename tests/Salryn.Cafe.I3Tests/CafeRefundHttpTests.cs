using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class CafeRefundHttpTests
{
    [Fact]
    public async Task RefundLifecycle_RequestApproveCommit_ReversesCashAndRestocks_AndIsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-refund-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            var coordinator = new Salryn.Infrastructure.Data.Services.SqliteCafeOrderCompletionCoordinator(factory);
            var completed = coordinator.Complete(completion);
            Assert.True(completed.IsSuccess, completed.IsFailure ? completed.ErrorDetail.Message : null);

            Guid saleId = completed.Value.SaleId;
            Guid saleItemId;
            Guid productId;
            string username;
            long qtyBeforeRefund;
            using (var conn = factory.CreateConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, product_id FROM sale_items WHERE sale_id = $sale LIMIT 1;";
                cmd.Parameters.AddWithValue("$sale", saleId.ToString("D"));
                using (var reader = cmd.ExecuteReader())
                {
                    Assert.True(reader.Read());
                    saleItemId = Guid.Parse(reader.GetString(0));
                    productId = Guid.Parse(reader.GetString(1));
                }

                cmd.CommandText = "UPDATE products SET track_stock = 1 WHERE id = $pid;";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$pid", productId.ToString("D"));
                cmd.ExecuteNonQuery();

                cmd.CommandText = "UPDATE users SET password_hash = $hash WHERE id = $id;";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$hash", SalrynPasswordHasher.HashPassword("Synthetic-Refund!2026").Value);
                cmd.Parameters.AddWithValue("$id", completion.ActorUserId.ToString("D"));
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT username FROM users WHERE id = $id;";
                username = (string)cmd.ExecuteScalar()!;

                cmd.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $pid;";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$pid", productId.ToString("D"));
                qtyBeforeRefund = Convert.ToInt64(cmd.ExecuteScalar());
            }

            var license = new LocalLicenseService(
                new LocalLicenseFileStore(new(root, "absent-test-license.json")),
                new LocalLicenseValidator(new() { AllowDevelopmentMode = true, DevelopmentFeatures = LicenseFeatures.AllList }));

            await using var host = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root)),
                LicenseService = license,
                Url = "http://127.0.0.1:0",
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            });
            await host.StartAsync();

            using var unauthClient = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await unauthClient.GetAsync("/api/cafe/refunds")).StatusCode);

            using var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(host.Urls.Single()) };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username, password = "Synthetic-Refund!2026" })).StatusCode);

            using var salesLookup = await client.GetAsync("/api/cafe/refunds/sales");
            Assert.Equal(HttpStatusCode.OK, salesLookup.StatusCode);
            using JsonDocument salesDoc = JsonDocument.Parse(await salesLookup.Content.ReadAsStringAsync());
            JsonElement saleRow = Assert.Single(salesDoc.RootElement.EnumerateArray(), e => e.GetProperty("saleId").GetGuid() == saleId);
            long saleCashNet = saleRow.GetProperty("cashNetCents").GetInt64();
            long saleAr = saleRow.GetProperty("arCents").GetInt64();

            // Missing operationId -> 400
            using var missingOpResp = await client.PostAsJsonAsync("/api/cafe/refunds", new
            {
                saleId,
                expectedVersion = 0,
                reason = "Damaged drink",
                lines = new[] { new { saleItemId, quantityX1000 = 1000L, stockDisposition = "Restock" } }
            });
            Assert.Equal(HttpStatusCode.BadRequest, missingOpResp.StatusCode);

            // Over-refund quantity -> 409
            using var overQtyResp = await client.PostAsJsonAsync("/api/cafe/refunds", new
            {
                operationId = Guid.NewGuid(),
                saleId,
                expectedVersion = 0,
                reason = "Over qty test",
                lines = new[] { new { saleItemId, quantityX1000 = 99000L, stockDisposition = "Restock" } }
            });
            Assert.Equal(HttpStatusCode.Conflict, overQtyResp.StatusCode);

            // Request canonical refund
            Guid reqOpId = Guid.NewGuid();
            var reqBody = new
            {
                operationId = reqOpId,
                saleId,
                expectedVersion = 0,
                reason = "Wrong preparation",
                lines = new[] { new { saleItemId, quantityX1000 = 1000L, stockDisposition = "Restock" } }
            };
            using var createResp = await client.PostAsJsonAsync("/api/cafe/refunds", reqBody);
            Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
            using JsonDocument createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
            Guid refundId = createDoc.RootElement.GetProperty("refundId").GetGuid();
            Assert.Equal(1, createDoc.RootElement.GetProperty("version").GetInt32());
            Assert.Equal("Requested", createDoc.RootElement.GetProperty("state").GetString());

            // Idempotent replay -> 200; diverged payload with same operationId -> 409 ERR_IDEMPOTENCY_CONFLICT
            using var replayResp = await client.PostAsJsonAsync("/api/cafe/refunds", reqBody);
            Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
            using var conflictResp = await client.PostAsJsonAsync("/api/cafe/refunds", new
            {
                operationId = reqOpId,
                saleId,
                expectedVersion = 0,
                reason = "Tampered reason",
                lines = reqBody.lines
            });
            Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);

            // Approve
            using var approveResp = await client.PostAsJsonAsync($"/api/cafe/refunds/{refundId:D}/resolve", new
            {
                operationId = Guid.NewGuid(),
                saleId,
                expectedVersion = 1,
                approve = true,
                reason = "Approved by manager"
            });
            Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);

            // List endpoint must expose computed totalRefundCents even before commit
            using var preCommitListResp = await client.GetAsync("/api/cafe/refunds");
            Assert.Equal(HttpStatusCode.OK, preCommitListResp.StatusCode);
            using JsonDocument preCommitDoc = JsonDocument.Parse(await preCommitListResp.Content.ReadAsStringAsync());
            JsonElement approvedSummary = Assert.Single(preCommitDoc.RootElement.EnumerateArray(), e => e.GetProperty("refundId").GetGuid() == refundId);
            Assert.Equal("Approved", approvedSummary.GetProperty("state").GetString());
            Assert.True(approvedSummary.GetProperty("totalRefundCents").GetInt64() > 0);

            // Commit Cash + AR settlement matching sale tender total
            using var detailResp = await client.GetAsync($"/api/cafe/refunds/{refundId:D}");
            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            using JsonDocument detailDoc = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync());
            long lineAmountCents = detailDoc.RootElement.GetProperty("lines")[0].GetProperty("refundAmountCents").GetInt64();
            Assert.True(lineAmountCents > 0);

            List<object> settlements = [];
            long cashPart = Math.Min(lineAmountCents, saleCashNet);
            long arPart = lineAmountCents - cashPart;
            Assert.True(arPart <= saleAr);
            if (cashPart > 0) settlements.Add(new { method = "Cash", amountCents = cashPart });
            if (arPart > 0) settlements.Add(new { method = "AccountsReceivable", amountCents = arPart });

            Guid commitOpId = Guid.NewGuid();
            var commitBody = new
            {
                operationId = commitOpId,
                saleId,
                expectedVersion = 2,
                settlements
            };
            using var commitResp = await client.PostAsJsonAsync($"/api/cafe/refunds/{refundId:D}/commit", commitBody);
            string commitText = await commitResp.Content.ReadAsStringAsync();
            Assert.True(commitResp.StatusCode == HttpStatusCode.OK, $"Commit failed ({(int)commitResp.StatusCode}): {commitText}");
            using JsonDocument commitDoc = JsonDocument.Parse(commitText);
            Assert.Equal("Committed", commitDoc.RootElement.GetProperty("state").GetString());
            Assert.Equal(lineAmountCents, commitDoc.RootElement.GetProperty("totalRefundCents").GetInt64());

            using var commitReplayResp = await client.PostAsJsonAsync($"/api/cafe/refunds/{refundId:D}/commit", commitBody);
            Assert.Equal(HttpStatusCode.OK, commitReplayResp.StatusCode);

            using (var conn = factory.CreateConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT state FROM cafe_sale_refunds WHERE refund_id = $rid;";
                cmd.Parameters.AddWithValue("$rid", refundId.ToString("D"));
                Assert.Equal("Committed", (string)cmd.ExecuteScalar()!);

                cmd.CommandText = "SELECT stock_on_hand_x1000 FROM products WHERE id = $pid;";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$pid", productId.ToString("D"));
                long qtyAfterRefund = Convert.ToInt64(cmd.ExecuteScalar());
                Assert.Equal(qtyBeforeRefund + 1000L, qtyAfterRefund);
            }

            await host.StopAsync();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RefundMutation_RejectsUnauthorizedActor()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-refund-unauth-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (factory, completion) = CanonicalOrderCompletionFixture.Create(root);
            var coordinator = new Salryn.Infrastructure.Data.Services.SqliteCafeOrderCompletionCoordinator(factory);
            var completed = coordinator.Complete(completion);
            Assert.True(completed.IsSuccess);

            Guid saleId = completed.Value.SaleId;
            Guid saleItemId;
            string username;
            using (var conn = factory.CreateConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE users SET password_hash = $hash WHERE id = $id;";
                cmd.Parameters.AddWithValue("$hash", SalrynPasswordHasher.HashPassword("Synthetic-Refund!2026").Value);
                cmd.Parameters.AddWithValue("$id", completion.ActorUserId.ToString("D"));
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT username FROM users WHERE id = $id;";
                username = (string)cmd.ExecuteScalar()!;

                cmd.CommandText = "DELETE FROM role_permissions WHERE permission IN ('Sales.RefundRequest', 'Sales.RefundApprove');";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $sale LIMIT 1;";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$sale", saleId.ToString("D"));
                saleItemId = Guid.Parse((string)cmd.ExecuteScalar()!);
            }

            await using var host = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root)),
                Url = "http://127.0.0.1:0",
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            });
            await host.StartAsync();

            using var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(host.Urls.Single()) };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username, password = "Synthetic-Refund!2026" })).StatusCode);

            using var deniedResp = await client.PostAsJsonAsync("/api/cafe/refunds", new
            {
                operationId = Guid.NewGuid(),
                saleId,
                expectedVersion = 0,
                reason = "Unauthorized refund",
                lines = new[] { new { saleItemId, quantityX1000 = 1000L, stockDisposition = "Discard" } }
            });
            Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

            await host.StopAsync();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Security;

namespace Salryn.Tests;

public static class CeoCustomerOrdersAndQuotesChecks
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-ceo-orders-quotes-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

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

            // 2. Seed test product
            Guid productId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "PROD-QUO-1", "770001", "Industrial Tool Set", 50_000, 85_000, 20_000, 5_000);

            // 3. Seed users
            string cashierHash = SalrynPasswordHasher.HashPassword("Cashier123!").Value!;
            Guid cashierId = SeedUserWithPassword(paths.DatabasePath, "cashier1", "Cashier Jane", cashierHash, Guid.Parse(DefaultSeedData.CashierRoleId));

            // 4. Test Service Layer Directly
            SqliteCustomerOrdersAndQuotesService service = new(composition.ConnectionFactory);

            // 4.1 Create Quotation
            var quoteLines = new List<CreateCustomerQuotationLineRequest>
            {
                new(productId, "Industrial Tool Set", "770001", "pcs", 2000, 85_000, 5_000)
            };
            var createQuoteInput = new CreateCustomerQuotationRequest(
                "Acme Construction Corp",
                "0917-555-8888",
                "Valid for 14 days; Cash terms",
                0,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
                quoteLines);

            var quoteRes = await service.CreateQuotationAsync(cashierId, "Cashier Jane", createQuoteInput, CancellationToken.None);
            Assert(quoteRes.IsSuccess, "Quotation creation failed: " + quoteRes.Error);
            CustomerQuotation quote = quoteRes.Value;
            Assert(quote.QuoteNumber.StartsWith("PQ-"), $"Expected QuoteNumber to start with PQ-, got {quote.QuoteNumber}");
            Assert(quote.CustomerName == "Acme Construction Corp", "Customer name mismatch");
            Assert(quote.TotalCents == 165_000, $"Expected total 165,000 cents (2 * 85000 - 5000), got {quote.TotalCents}");
            Assert(quote.Status == "Issued", $"Expected status Issued, got {quote.Status}");

            // Verify status update
            var convertedRes = await service.UpdateQuotationStatusAsync(quote.Id, "Converted", CancellationToken.None);
            Assert(convertedRes.IsSuccess && convertedRes.Value.Status == "Converted", "Quotation status update failed");

            // CRITICAL ZERO-MUTATION ASSERTION FOR C07
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT
                        (SELECT COUNT(*) FROM sales) AS sales_count,
                        (SELECT COUNT(*) FROM payments) AS payments_count,
                        (SELECT COUNT(*) FROM cash_drawer_events) AS drawer_count,
                        (SELECT COUNT(*) FROM stock_movements) AS stock_count;
                    """;
                using SqliteDataReader reader = cmd.ExecuteReader();
                Assert(reader.Read(), "Could not read mutation counts");
                long salesCount = reader.GetInt64(0);
                long paymentsCount = reader.GetInt64(1);
                long drawerCount = reader.GetInt64(2);
                long stockCount = reader.GetInt64(3);

                Assert(salesCount == 0, $"C07 VIOLATION: Quotation created {salesCount} sales records! Must be zero.");
                Assert(paymentsCount == 0, $"C07 VIOLATION: Quotation created {paymentsCount} payments records! Must be zero.");
                Assert(drawerCount == 0, $"C07 VIOLATION: Quotation created {drawerCount} drawer events! Must be zero.");
                Assert(stockCount == 0, $"C07 VIOLATION: Quotation created {stockCount} stock movements! Must be zero.");
            }

            // 4.2 Create Customer Order (C08)
            var orderLines = new List<CreateCustomerOrderLineRequest>
            {
                new(productId, "Industrial Tool Set", "770001", "pcs", 3000, 85_000)
            };
            var createOrderInput = new CreateCustomerOrderRequest(
                null,
                "Maria Santos",
                "0918-123-4567",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                "Customer requested morning delivery",
                orderLines);

            var orderRes = await service.CreateOrderAsync(cashierId, "Cashier Jane", createOrderInput, CancellationToken.None);
            Assert(orderRes.IsSuccess, "Customer order creation failed: " + orderRes.Error);
            CustomerOrder order = orderRes.Value;
            Assert(order.OrderNumber.StartsWith("CO-"), $"Expected OrderNumber to start with CO-, got {order.OrderNumber}");
            Assert(order.CustomerName == "Maria Santos", "Customer name mismatch");
            Assert(order.TotalCents == 255_000, $"Expected order total 255,000 cents (3 * 85000), got {order.TotalCents}");
            Assert(order.Status == "Confirmed", $"Expected status Confirmed, got {order.Status}");

            // Fulfill Order
            Guid dummySaleId = Guid.NewGuid();
            var fulfilledRes = await service.FulfillOrderAsync(order.Id, dummySaleId, CancellationToken.None);
            Assert(fulfilledRes.IsSuccess, "Fulfill order failed: " + fulfilledRes.Error);
            Assert(fulfilledRes.Value.Status == "Fulfilled", $"Expected status Fulfilled, got {fulfilledRes.Value.Status}");
            Assert(fulfilledRes.Value.SaleId == dummySaleId, "Fulfill saleId mismatch");
            Assert(fulfilledRes.Value.FulfilledAt != null, "FulfilledAt timestamp must be set");

            // Cancel Order Flow
            var orderToCancelRes = await service.CreateOrderAsync(cashierId, "Cashier Jane", createOrderInput, CancellationToken.None);
            var cancelledRes = await service.UpdateOrderStatusAsync(orderToCancelRes.Value.Id, "Cancelled", CancellationToken.None);
            Assert(cancelledRes.IsSuccess && cancelledRes.Value.Status == "Cancelled", "Expected status Cancelled");

            // 5. Test Local HTTP API Endpoints
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

            try
            {
                using HttpClient client = new(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(boundUrl)
                };

                // Unauthenticated check
                HttpResponseMessage unauthQuotes = await client.GetAsync("/api/customer-quotations");
                Assert(unauthQuotes.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 for unauthenticated quotes, got {unauthQuotes.StatusCode}");

                // Login as Cashier
                HttpResponseMessage loginResp = await client.PostAsJsonAsync("/api/auth/login", new { username = "cashier1", password = "Cashier123!" });
                Assert(loginResp.IsSuccessStatusCode, "Cashier login failed");

                string? cookieHeader = loginResp.Headers.TryGetValues("Set-Cookie", out var cv) ? cv.FirstOrDefault() : null;
                Assert(!string.IsNullOrEmpty(cookieHeader), "Session cookie missing");
                string sessionCookie = cookieHeader!.Split(';')[0];

                client.DefaultRequestHeaders.Add("Cookie", sessionCookie);

                // GET quotations
                HttpResponseMessage quotesResp = await client.GetAsync("/api/customer-quotations");
                Assert(quotesResp.IsSuccessStatusCode, $"GET /api/customer-quotations failed with {quotesResp.StatusCode}");
                var quotesList = await quotesResp.Content.ReadFromJsonAsync<List<CustomerQuotation>>();
                Assert(quotesList != null && quotesList.Count >= 1, "Failed to retrieve quotation list via HTTP");

                // POST new quotation via HTTP
                var httpQuotePayload = new
                {
                    customerName = "Http Customer",
                    customerContact = "0917-000-1111",
                    notes = "Http API test",
                    discountCents = 0,
                    lines = new[]
                    {
                        new
                        {
                            productId = productId.ToString("D"),
                            productNameSnapshot = "Industrial Tool Set",
                            unitNameSnapshot = "pcs",
                            quantityX1000 = 1000,
                            unitPriceCents = 85_000,
                            discountCents = 0
                        }
                    }
                };

                HttpResponseMessage createQuoteResp = await client.PostAsJsonAsync("/api/customer-quotations", httpQuotePayload);
                Assert(createQuoteResp.StatusCode == HttpStatusCode.Created, $"POST /api/customer-quotations failed with {createQuoteResp.StatusCode}");

                // GET orders
                HttpResponseMessage ordersResp = await client.GetAsync("/api/customer-orders");
                Assert(ordersResp.IsSuccessStatusCode, $"GET /api/customer-orders failed with {ordersResp.StatusCode}");
                var ordersList = await ordersResp.Content.ReadFromJsonAsync<List<CustomerOrder>>();
                Assert(ordersList != null && ordersList.Count >= 2, "Failed to retrieve orders list via HTTP");

                // POST new order via HTTP
                var httpOrderPayload = new
                {
                    customerName = "Http Order Buyer",
                    customerPhone = "0999-888-7777",
                    notes = "Order placed via HTTP endpoint",
                    lines = new[]
                    {
                        new
                        {
                            productId = productId.ToString("D"),
                            productNameSnapshot = "Industrial Tool Set",
                            unitNameSnapshot = "pcs",
                            quantityX1000 = 2000,
                            unitPriceCents = 85_000
                        }
                    }
                };

                HttpResponseMessage createOrderResp = await client.PostAsJsonAsync("/api/customer-orders", httpOrderPayload);
                Assert(createOrderResp.StatusCode == HttpStatusCode.Created, $"POST /api/customer-orders failed with {createOrderResp.StatusCode}");
                var createdOrder = await createOrderResp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(createdOrder != null, "Created order payload is null");

                // Fulfill order via HTTP
                HttpResponseMessage fulfillResp = await client.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/fulfill", new { saleId = Guid.NewGuid().ToString("D") });
                Assert(fulfillResp.IsSuccessStatusCode, $"Fulfill order via HTTP failed: {fulfillResp.StatusCode}");
                var fulfilledOrder = await fulfillResp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(fulfilledOrder != null && fulfilledOrder.Status == "Fulfilled", "Fulfilled order status check failed");
            }
            finally
            {
                await app.StopAsync().ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
                // ignore temp cleanup
            }
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

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ACCEPTANCE FAILED: " + message);
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
                Message: "Acceptance license.",
                SourcePath: "acceptance",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));
        }

        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalLicenseInstallResponse(
                "Installed", LicenseStatus.Licensed, "acceptance", null, null, "Installed", DateTimeOffset.UtcNow));

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}

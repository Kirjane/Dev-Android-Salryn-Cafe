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
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;
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

            // 2. Seed test products
            Guid productId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "PROD-QUO-1", "770001", "Industrial Tool Set", 50_000, 85_000, 20_000, 5_000);
            Guid otherProductId = LpiProcureToPayRuntimeChecks.SeedProduct(paths.DatabasePath, "PROD-QUO-2", "770002", "Safety Goggles", 10_000, 15_000, 10_000, 5_000);

            // 3. Seed users & customers
            string cashierHash = SalrynPasswordHasher.HashPassword("Cashier123!").Value!;
            Guid cashierId = SeedUserWithPassword(paths.DatabasePath, "cashier1", "Cashier Jane", cashierHash, Guid.Parse(DefaultSeedData.CashierRoleId));
            string managerHash = SalrynPasswordHasher.HashPassword("Manager123!").Value!;
            Guid managerId = SeedUserWithPassword(paths.DatabasePath, "manager1", "Manager Mike", managerHash, Guid.Parse(DefaultSeedData.ManagerRoleId));
            string adminHash = SalrynPasswordHasher.HashPassword("Admin123!").Value!;
            _ = SeedUserWithPassword(paths.DatabasePath, "admin-co", "Admin Auditor", adminHash, Guid.Parse(DefaultSeedData.AdminRoleId));

            Guid customerA = SeedCustomer(paths.DatabasePath, "Customer Alpha", "0917-111-2222", cashierId);
            Guid customerB = SeedCustomer(paths.DatabasePath, "Customer Beta", "0917-333-4444", cashierId);

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

            // CO-LINE-01/02: a persisted single child must survive list/detail
            // hydration and a new service instance, without making N detail queries.
            var oneLineDb = ReadPersistedOrderLines(paths.DatabasePath, order.Id);
            Assert(oneLineDb.Count == 1 && oneLineDb.TotalCents == 255_000,
                "CO-LINE-01 persisted single-line count / total mismatch.");
            var listedInitial = await service.GetOrdersAsync();
            Assert(listedInitial.IsSuccess, "CO-LINE-01 list GET failed.");
            CustomerOrder listedOne = listedInitial.Value.Single(o => o.Id == order.Id);
            Assert(listedOne.Lines.Count == 1 && listedOne.Lines[0].Id == order.Lines[0].Id &&
                   listedOne.TotalCents == oneLineDb.TotalCents, "CO-LINE-01 list returned an unhydrated line.");
            var detailInitial = await service.GetOrderByIdAsync(order.Id);
            Assert(detailInitial.IsSuccess && detailInitial.Value.Lines.SequenceEqual(listedOne.Lines),
                "CO-LINE-01 list/detail line identity differs.");
            var multiInput = createOrderInput with
            {
                CustomerName = "Two-Line Order",
                Lines = new List<CreateCustomerOrderLineRequest>
                {
                    new(productId, "Industrial Tool Set", "770001", "pcs", 2000, 85_000),
                    new(otherProductId, "Safety Goggles", "770002", "pcs", 500, 15_000)
                }
            };
            var multiCreated = await service.CreateOrderAsync(managerId, "Manager Mike", multiInput);
            Assert(multiCreated.IsSuccess && multiCreated.Value.TotalCents == 177_500,
                "CO-LINE-02 multi-line order creation or total failed.");
            var multiDb = ReadPersistedOrderLines(paths.DatabasePath, multiCreated.Value.Id);
            Assert(multiDb.Count == 2 && multiDb.TotalCents == 177_500,
                "CO-LINE-02 persisted multi-line count / total mismatch.");
            var freshService = new SqliteCustomerOrdersAndQuotesService(composition.ConnectionFactory);
            var filteredOrders = await freshService.GetOrdersAsync("Confirmed");
            Assert(filteredOrders.IsSuccess, "CO-LINE-02 filtered/reopened list failed.");
            var multiListed = filteredOrders.Value.Single(o => o.Id == multiCreated.Value.Id);
            var multiDetail = await freshService.GetOrderByIdAsync(multiCreated.Value.Id);
            Assert(multiDetail.IsSuccess && multiListed.Lines.Count == 2 &&
                   multiListed.Lines.SequenceEqual(multiDetail.Value.Lines) &&
                   multiListed.Lines.Sum(line => line.LineTotalCents) == multiListed.TotalCents,
                "CO-LINE-02 list/detail/print lines differ after fresh service read.");

            // CO-LINE-03/04: rejected creates must be fully atomic and must not
            // add parent/child/audit, checkout, stock, payment, or drawer records.
            var beforeInvalid = ReadOrderMutationCounts(paths.DatabasePath);
            var noLine = await service.CreateOrderAsync(managerId, "Manager Mike",
                createOrderInput with { CustomerName = "Invalid Empty Order", Lines = Array.Empty<CreateCustomerOrderLineRequest>() });
            Assert(!noLine.IsSuccess && noLine.ErrorDetail.Code == "CustomerOrder.LinesRequired",
                "CO-LINE-03 empty order must be deterministically rejected.");
            var invalidQuantity = await service.CreateOrderAsync(managerId, "Manager Mike",
                createOrderInput with { Lines = new[] { new CreateCustomerOrderLineRequest(productId, "Tool", null, "pcs", 0, 85_000) } });
            Assert(!invalidQuantity.IsSuccess && invalidQuantity.ErrorDetail.Code == "CustomerOrder.InvalidQuantity",
                "CO-LINE-04 zero quantity must be rejected.");
            var invalidProduct = await service.CreateOrderAsync(managerId, "Manager Mike",
                createOrderInput with { Lines = new[] { new CreateCustomerOrderLineRequest(Guid.Empty, "Tool", null, "pcs", 1000, 85_000) } });
            Assert(!invalidProduct.IsSuccess && invalidProduct.ErrorDetail.Code == "CustomerOrder.InvalidLine",
                "CO-LINE-04 empty product ID must be rejected.");
            var invalidPrice = await service.CreateOrderAsync(managerId, "Manager Mike",
                createOrderInput with { Lines = new[] { new CreateCustomerOrderLineRequest(productId, "Tool", null, "pcs", 1000, -1) } });
            Assert(!invalidPrice.IsSuccess && invalidPrice.ErrorDetail.Code == "CustomerOrder.InvalidLine",
                "CO-LINE-04 negative price must be rejected.");
            var overflowPrice = await service.CreateOrderAsync(managerId, "Manager Mike",
                createOrderInput with { Lines = new[] { new CreateCustomerOrderLineRequest(productId, "Tool", null, "pcs", long.MaxValue, 85_000) } });
            Assert(!overflowPrice.IsSuccess && overflowPrice.ErrorDetail.Code == "CustomerOrder.AmountOverflow",
                "CO-LINE-04 overflowing amount must be rejected.");
            Assert(beforeInvalid == ReadOrderMutationCounts(paths.DatabasePath),
                "CO-LINE-03/04 invalid order created parent, child, audit, sale, payment, stock or drawer mutation.");

            // Fulfill Order Invariants & Explicit State Machine
            // 1. Negative test: direct Fulfilled status write bypass must fail
            var bypassRes = await service.UpdateOrderStatusAsync(order.Id, "Fulfilled", CancellationToken.None);
            Assert(!bypassRes.IsSuccess, "Expected direct Fulfilled status write to fail");
            Assert(bypassRes.ErrorDetail.Code == "CustomerOrder.CannotSetFulfilledDirectly", $"Expected CustomerOrder.CannotSetFulfilledDirectly, got {bypassRes.ErrorDetail.Code}");

            // 2. Negative test: invalid status transition must fail
            var bogusRes = await service.UpdateOrderStatusAsync(order.Id, "Completed", CancellationToken.None);
            Assert(!bogusRes.IsSuccess, "Expected invalid status transition to fail");
            Assert(bogusRes.ErrorDetail.Code == "CustomerOrder.InvalidStatusTransition", $"Expected CustomerOrder.InvalidStatusTransition, got {bogusRes.ErrorDetail.Code}");

            // 3. Negative test: non-existent random GUID sale must fail
            Guid randomSaleId = Guid.NewGuid();
            var notFoundRes = await service.FulfillOrderAsync(order.Id, randomSaleId, CancellationToken.None);
            Assert(!notFoundRes.IsSuccess, "Expected random saleId to fail");
            Assert(notFoundRes.ErrorDetail.Code == "CustomerOrder.SaleNotFound", $"Expected CustomerOrder.SaleNotFound, got {notFoundRes.ErrorDetail.Code}");

            // 4. Negative test: voided sale must fail
            Guid voidedSaleId = SeedRealSale(paths.DatabasePath, "SL-VOID-1", cashierId, productId, "Industrial Tool Set", 3000, 85_000, 255_000, status: 3, isVoided: true);
            var voidedRes = await service.FulfillOrderAsync(order.Id, voidedSaleId, CancellationToken.None);
            Assert(!voidedRes.IsSuccess, "Expected voided sale to fail");
            Assert(voidedRes.ErrorDetail.Code == "CustomerOrder.SaleNotCompleted", $"Expected CustomerOrder.SaleNotCompleted, got {voidedRes.ErrorDetail.Code}");

            // 5. Negative test: mismatched items must fail
            Guid mismatchSaleId = SeedRealSale(paths.DatabasePath, "SL-MISMATCH-1", cashierId, otherProductId, "Safety Goggles", 3000, 85_000, 255_000);
            var mismatchRes = await service.FulfillOrderAsync(order.Id, mismatchSaleId, CancellationToken.None);
            Assert(!mismatchRes.IsSuccess, "Expected mismatched items sale to fail");
            Assert(mismatchRes.ErrorDetail.Code == "CustomerOrder.SaleLineMismatch", $"Expected CustomerOrder.SaleLineMismatch, got {mismatchRes.ErrorDetail.Code}");

            // 6. Positive test: real matching sale succeeds
            Guid realSaleId = SeedRealSale(paths.DatabasePath, "SL-REAL-1", cashierId, productId, "Industrial Tool Set", 3000, 85_000, 255_000);
            var fulfilledRes = await service.FulfillOrderAsync(order.Id, realSaleId, CancellationToken.None);
            Assert(fulfilledRes.IsSuccess, "Fulfill order with real sale failed: " + fulfilledRes.Error);
            Assert(fulfilledRes.Value.Status == "Fulfilled", $"Expected status Fulfilled, got {fulfilledRes.Value.Status}");
            Assert(fulfilledRes.Value.SaleId == realSaleId, "Fulfill saleId mismatch");
            Assert(fulfilledRes.Value.FulfilledAt != null, "FulfilledAt timestamp must be set");

            // 7. Negative test: terminal state reversal (cannot cancel or revive fulfilled order)
            var cancelFulfilledRes = await service.UpdateOrderStatusAsync(order.Id, "Cancelled", CancellationToken.None);
            Assert(!cancelFulfilledRes.IsSuccess, "Expected cancelling fulfilled order to fail");
            Assert(cancelFulfilledRes.ErrorDetail.Code == "CustomerOrder.TerminalState", $"Expected CustomerOrder.TerminalState, got {cancelFulfilledRes.ErrorDetail.Code}");

            var alreadyFulfilledRes = await service.FulfillOrderAsync(order.Id, realSaleId, CancellationToken.None);
            Assert(!alreadyFulfilledRes.IsSuccess && alreadyFulfilledRes.ErrorDetail.Code == "CustomerOrder.AlreadyFulfilled", "Expected CustomerOrder.AlreadyFulfilled");

            // 8. Negative test: duplicate linkage (same sale cannot fulfill second order)
            var secondOrderRes = await service.CreateOrderAsync(cashierId, "Cashier Jane", createOrderInput, CancellationToken.None);
            Assert(secondOrderRes.IsSuccess, "Second order creation failed");
            var dupLinkRes = await service.FulfillOrderAsync(secondOrderRes.Value.Id, realSaleId, CancellationToken.None);
            Assert(!dupLinkRes.IsSuccess, "Expected duplicate sale linkage to fail");
            Assert(dupLinkRes.ErrorDetail.Code == "CustomerOrder.SaleAlreadyLinked", $"Expected CustomerOrder.SaleAlreadyLinked, got {dupLinkRes.ErrorDetail.Code}");

            // 9. Cancel Order Flow & terminal-state invariant checks
            var orderToCancelRes = await service.CreateOrderAsync(cashierId, "Cashier Jane", createOrderInput, CancellationToken.None);
            var cancelledRes = await service.UpdateOrderStatusAsync(orderToCancelRes.Value.Id, "Cancelled", CancellationToken.None);
            Assert(cancelledRes.IsSuccess && cancelledRes.Value.Status == "Cancelled", "Expected status Cancelled");

            var doubleCancelRes = await service.UpdateOrderStatusAsync(orderToCancelRes.Value.Id, "Cancelled", CancellationToken.None);
            Assert(!doubleCancelRes.IsSuccess, "Expected double cancel to fail");
            Assert(doubleCancelRes.ErrorDetail.Code == "CustomerOrder.TerminalState", $"Expected CustomerOrder.TerminalState, got {doubleCancelRes.ErrorDetail.Code}");

            Guid cancelTestSale = SeedRealSale(paths.DatabasePath, "SL-CANCEL-TEST", cashierId, productId, "Industrial Tool Set", 3000, 85_000, 255_000);
            var fulfillCancelledRes = await service.FulfillOrderAsync(orderToCancelRes.Value.Id, cancelTestSale, CancellationToken.None);
            Assert(!fulfillCancelledRes.IsSuccess && fulfillCancelledRes.ErrorDetail.Code == "CustomerOrder.OrderCancelled", "Expected CustomerOrder.OrderCancelled");

            // 10. Customer identity & financial matching rules
            var orderWithCustInput = new CreateCustomerOrderRequest(
                customerA,
                "Customer Alpha",
                "0917-111-2222",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                "Account customer order",
                orderLines);
            var orderWithCustRes = await service.CreateOrderAsync(cashierId, "Cashier Jane", orderWithCustInput, CancellationToken.None);
            Assert(orderWithCustRes.IsSuccess, "Customer order with customerId failed: " + orderWithCustRes.Error);

            // 10a. Negative test: sale registered to different customer must fail
            Guid saleForCustB = SeedRealSale(paths.DatabasePath, "SL-CUST-B", cashierId, productId, "Industrial Tool Set", 3000, 85_000, 255_000, customerId: customerB);
            var custMismatchRes = await service.FulfillOrderAsync(orderWithCustRes.Value.Id, saleForCustB, CancellationToken.None);
            Assert(!custMismatchRes.IsSuccess, "Expected customer mismatch to fail");
            Assert(custMismatchRes.ErrorDetail.Code == "CustomerOrder.CustomerMismatch", $"Expected CustomerOrder.CustomerMismatch, got {custMismatchRes.ErrorDetail.Code}");

            // 10b. Negative test: sale total insufficient (underpayment) must fail
            Guid underpaidSale = SeedRealSale(paths.DatabasePath, "SL-UNDERPAID-1", cashierId, productId, "Industrial Tool Set", 3000, 85_000, 200_000, customerId: customerA);
            var underpaidRes = await service.FulfillOrderAsync(orderWithCustRes.Value.Id, underpaidSale, CancellationToken.None);
            Assert(!underpaidRes.IsSuccess, "Expected underpaid sale to fail");
            Assert(underpaidRes.ErrorDetail.Code == "CustomerOrder.SaleTotalMismatch", $"Expected CustomerOrder.SaleTotalMismatch, got {underpaidRes.ErrorDetail.Code}");

            // 10c. Positive test: matching customer + matching lines + covering total succeeds
            Guid validCustSale = SeedRealSale(paths.DatabasePath, "SL-CUST-A", cashierId, productId, "Industrial Tool Set", 3000, 85_000, 255_000, customerId: customerA);
            var validCustFulfillRes = await service.FulfillOrderAsync(orderWithCustRes.Value.Id, validCustSale, CancellationToken.None);
            Assert(validCustFulfillRes.IsSuccess, "Expected customer order fulfillment with matching customer to succeed: " + validCustFulfillRes.Error);
            Assert(validCustFulfillRes.Value.Status == "Fulfilled", "Status must be Fulfilled");
            Assert(validCustFulfillRes.Value.SaleId == validCustSale, "SaleId must match");

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

                // Manager owns Customer Orders; Cashier retains quotation access only.
                using HttpClient managerClient = new(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(boundUrl)
                };
                HttpResponseMessage managerLoginResp = await managerClient.PostAsJsonAsync("/api/auth/login", new { username = "manager1", password = "Manager123!" });
                Assert(managerLoginResp.IsSuccessStatusCode, "Manager login failed");
                string? mgrCookieHeader = managerLoginResp.Headers.TryGetValues("Set-Cookie", out var mcv) ? mcv.FirstOrDefault() : null;
                Assert(!string.IsNullOrEmpty(mgrCookieHeader), "Manager session cookie missing");
                managerClient.DefaultRequestHeaders.Add("Cookie", mgrCookieHeader!.Split(';')[0]);

                using HttpClient adminClient = new(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(boundUrl) };
                HttpResponseMessage adminLogin = await adminClient.PostAsJsonAsync("/api/auth/login",
                    new { username = "admin-co", password = "Admin123!" });
                Assert(adminLogin.IsSuccessStatusCode, "CO-LINE-05 admin login failed.");
                Assert(adminLogin.Headers.TryGetValues("Set-Cookie", out var adminCookies),
                    "CO-LINE-05 admin cookie missing.");
                adminClient.DefaultRequestHeaders.Add("Cookie", adminCookies!.First().Split(';')[0]);

                // Cashier must not browse, create, or fulfill manager-owned orders.
                Assert((await client.GetAsync("/api/customer-orders")).StatusCode == HttpStatusCode.Forbidden,
                    "Cashier GET /api/customer-orders must return 403.");
                HttpResponseMessage ordersResp = await managerClient.GetAsync("/api/customer-orders");
                Assert(ordersResp.IsSuccessStatusCode, $"Manager GET /api/customer-orders failed with {ordersResp.StatusCode}");
                var ordersList = await ordersResp.Content.ReadFromJsonAsync<List<CustomerOrder>>();
                Assert(ordersList != null && ordersList.Count >= 2, "Failed to retrieve orders list via HTTP");
                // CO-LINE-01/02: compare real database, list JSON, and detail JSON.
                foreach (var persistedOrder in new[] { order, multiCreated.Value })
                {
                    CustomerOrder fromList = ordersList.Single(o => o.Id == persistedOrder.Id);
                    var actual = ReadPersistedOrderLines(paths.DatabasePath, persistedOrder.Id);
                    Assert(actual.Count == fromList.Lines.Count && actual.TotalCents == fromList.TotalCents,
                        $"CO-LINE-01/02 list API diverges from DB for {persistedOrder.OrderNumber}.");
                    var detailResponse = await managerClient.GetAsync($"/api/customer-orders/{persistedOrder.Id}");
                    Assert(detailResponse.StatusCode == HttpStatusCode.OK, "CO-LINE-01/02 manager detail GET failed.");
                    var fromDetail = await detailResponse.Content.ReadFromJsonAsync<CustomerOrder>();
                    Assert(fromDetail != null && fromList.Lines.SequenceEqual(fromDetail.Lines),
                        $"CO-LINE-01/02 list API diverges from detail for {persistedOrder.OrderNumber}.");
                }
                Assert((await adminClient.GetAsync("/api/customer-orders")).StatusCode == HttpStatusCode.OK,
                    "CO-LINE-05 admin list read must succeed.");
                Assert((await adminClient.GetAsync($"/api/customer-orders/{order.Id}")).StatusCode == HttpStatusCode.OK,
                    "CO-LINE-05 admin detail read must succeed.");
                Assert((await client.GetAsync($"/api/customer-orders/{order.Id}")).StatusCode == HttpStatusCode.Forbidden,
                    "CO-LINE-05 cashier detail read must fail with 403.");

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

                HttpResponseMessage createOrderResp = await managerClient.PostAsJsonAsync("/api/customer-orders", httpOrderPayload);
                Assert(createOrderResp.StatusCode == HttpStatusCode.Created, $"POST /api/customer-orders failed with {createOrderResp.StatusCode}");
                var createdOrder = await createOrderResp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(createdOrder != null, "Created order payload is null");
                Assert(ReadPersistedOrderLines(paths.DatabasePath, createdOrder.Id).Count == 1,
                    "CO-LINE-01 manager API order must persist one child.");
                var postListResponse = await managerClient.GetAsync("/api/customer-orders");
                var postList = await postListResponse.Content.ReadFromJsonAsync<List<CustomerOrder>>();
                Assert(postListResponse.StatusCode == HttpStatusCode.OK &&
                       postList!.Single(o => o.Id == createdOrder.Id).Lines.SequenceEqual(createdOrder.Lines),
                    "CO-LINE-01 manager API list must hydrate newly inserted line.");

                // CO-LINE-02 HTTP multi-line payload and filtered list must retain exact children.
                var httpMultiPayload = new
                {
                    customerName = "HTTP Multi-line Buyer",
                    lines = new object[]
                    {
                        new { productId = productId.ToString("D"), productNameSnapshot = "Industrial Tool Set",
                              unitNameSnapshot = "pcs", quantityX1000 = 1000L, unitPriceCents = 85_000L },
                        new { productId = otherProductId.ToString("D"), productNameSnapshot = "Safety Goggles",
                              unitNameSnapshot = "pcs", quantityX1000 = 500L, unitPriceCents = 15_000L }
                    }
                };
                var httpMultiResponse = await managerClient.PostAsJsonAsync("/api/customer-orders", httpMultiPayload);
                Assert(httpMultiResponse.StatusCode == HttpStatusCode.Created,
                    $"CO-LINE-02 HTTP multi-line POST failed: {httpMultiResponse.StatusCode}");
                var httpMulti = await httpMultiResponse.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(httpMulti != null && httpMulti.TotalCents == 92_500 && httpMulti.Lines.Count == 2,
                    "CO-LINE-02 HTTP multi-line total/child count wrong.");
                var httpMultiDb = ReadPersistedOrderLines(paths.DatabasePath, httpMulti.Id);
                Assert(httpMultiDb.Count == 2 && httpMultiDb.TotalCents == httpMulti.TotalCents,
                    "CO-LINE-02 HTTP multi-line persistence mismatch.");
                var confirmedResponse = await managerClient.GetAsync("/api/customer-orders?status=Confirmed");
                var confirmedList = await confirmedResponse.Content.ReadFromJsonAsync<List<CustomerOrder>>();
                Assert(confirmedResponse.StatusCode == HttpStatusCode.OK &&
                    confirmedList!.Single(o => o.Id == httpMulti.Id).Lines.SequenceEqual(httpMulti.Lines),
                    "CO-LINE-02 filtered HTTP list must contain exact child lines.");

                // CO-LINE-03/04: API validation failures are atomic (parent, children,
                // creation audits, sales, payments, stock and drawer all unchanged).
                var beforeInvalidHttp = ReadOrderMutationCounts(paths.DatabasePath);
                var emptyResponse = await managerClient.PostAsJsonAsync("/api/customer-orders",
                    new { customerName = "Empty HTTP Order", lines = Array.Empty<object>() });
                Assert(emptyResponse.StatusCode == HttpStatusCode.BadRequest, "CO-LINE-03 [] must return 400.");
                var absentResponse = await managerClient.PostAsJsonAsync("/api/customer-orders",
                    new { customerName = "Absent HTTP Order" });
                Assert(absentResponse.StatusCode == HttpStatusCode.BadRequest, "CO-LINE-03 missing lines must return 400.");
                var invalidQuantityResponse = await managerClient.PostAsJsonAsync("/api/customer-orders",
                    new { customerName = "Invalid Qty HTTP",
                          lines = new[] { new { productId, productNameSnapshot = "Tool",
                            unitNameSnapshot = "pcs", quantityX1000 = 0L, unitPriceCents = 85_000L } } });
                Assert(invalidQuantityResponse.StatusCode == HttpStatusCode.BadRequest,
                    "CO-LINE-04 invalid quantity must return 400.");
                var invalidPriceResponse = await managerClient.PostAsJsonAsync("/api/customer-orders",
                    new { customerName = "Invalid Price HTTP",
                          lines = new[] { new { productId, productNameSnapshot = "Tool",
                            unitNameSnapshot = "pcs", quantityX1000 = 1000L, unitPriceCents = -100L } } });
                Assert(invalidPriceResponse.StatusCode == HttpStatusCode.BadRequest,
                    "CO-LINE-04 negative price must return 400.");
                Assert(beforeInvalidHttp == ReadOrderMutationCounts(paths.DatabasePath),
                    "CO-LINE-03/04 rejected HTTP create caused a database mutation.");
                Assert((await client.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/fulfill",
                    new { saleId = Guid.NewGuid().ToString("D") })).StatusCode == HttpStatusCode.Forbidden,
                    "CO-LINE-05 cashier fulfillment must return 403.");

                // 1. Negative test via HTTP: cashier cannot create or mutate customer orders
                Assert((await client.PostAsJsonAsync("/api/customer-orders", httpOrderPayload)).StatusCode == HttpStatusCode.Forbidden,
                    "Cashier POST /api/customer-orders must return 403.");
                // Unauthorized cashier cannot mutate order status
                HttpResponseMessage cashierStatusResp = await client.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "Cancelled" });
                Assert(cashierStatusResp.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden for cashier order cancellation, got {cashierStatusResp.StatusCode}");

                // 3. Negative test via HTTP: manager cannot directly write Fulfilled status
                HttpResponseMessage mgrDirectFulfill = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "Fulfilled" });
                Assert(mgrDirectFulfill.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict for direct Fulfilled status write, got {mgrDirectFulfill.StatusCode}");

                // 4. Negative test via HTTP: invalid status transition rejected with 400
                HttpResponseMessage mgrInvalidStatus = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "BogusStatus" });
                Assert(mgrInvalidStatus.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 BadRequest for invalid status, got {mgrInvalidStatus.StatusCode}");

                // Manager advance-state path is auditable and does not create a checkout sale.
                var preparingResponse = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "Preparing" });
                Assert(preparingResponse.StatusCode == HttpStatusCode.OK, "Confirmed -> Preparing must succeed.");
                var readyResponse = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "Ready" });
                Assert(readyResponse.StatusCode == HttpStatusCode.OK, "Preparing -> Ready must succeed.");
                var invalidReadyReplay = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "Ready" });
                Assert(invalidReadyReplay.StatusCode == HttpStatusCode.Conflict, "Repeated Ready must fail closed.");
                using (SqliteConnection auditConn = new($"Data Source={paths.DatabasePath}"))
                {
                    auditConn.Open();
                    using SqliteCommand auditCmd = auditConn.CreateCommand();
                    auditCmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_id=$orderId AND user_id=$actor AND action='CUSTOMER_ORDER_STATUS_CHANGED';";
                    auditCmd.Parameters.AddWithValue("$orderId", createdOrder.Id.ToString("D"));
                    auditCmd.Parameters.AddWithValue("$actor", managerId.ToString("D"));
                    Assert(Convert.ToInt64(auditCmd.ExecuteScalar()) == 2,
                        "Manager order state changes must have attributed and atomic audit entries.");
                }

                // 5. Negative test via HTTP: random sale ID returns 404
                HttpResponseMessage unlinkedHttpFulfill = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/fulfill", new { saleId = Guid.NewGuid().ToString("D") });
                Assert(unlinkedHttpFulfill.StatusCode == HttpStatusCode.NotFound, $"Expected 404 for random saleId fulfillment, got {unlinkedHttpFulfill.StatusCode}");

                // 6. Positive test via HTTP: fulfill via receipt number reference
                Guid httpSaleId = SeedRealSale(paths.DatabasePath, "SL-HTTP-0001", cashierId, productId, "Industrial Tool Set", 2000, 85_000, 170_000);
                HttpResponseMessage fulfillResp = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/fulfill", new { saleReference = "SL-HTTP-0001" });
                Assert(fulfillResp.IsSuccessStatusCode, $"Fulfill order via HTTP failed: {fulfillResp.StatusCode}");
                var fulfilledOrder = await fulfillResp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(fulfilledOrder != null && fulfilledOrder.Status == "Fulfilled", "Fulfilled order status check failed");
                Assert(fulfilledOrder.SaleId == httpSaleId, "Linked saleId mismatch via HTTP fulfill");

                // 7. Negative test via HTTP: cannot cancel an already fulfilled order
                HttpResponseMessage mgrCancelFulfilled = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder.Id}/status", new { status = "Cancelled" });
                Assert(mgrCancelFulfilled.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict when cancelling fulfilled order, got {mgrCancelFulfilled.StatusCode}");

                // 8. Positive test via HTTP: manager cancels a confirmed order
                HttpResponseMessage createOrder2Resp = await managerClient.PostAsJsonAsync("/api/customer-orders", httpOrderPayload);
                Assert(createOrder2Resp.StatusCode == HttpStatusCode.Created, "Failed to create second order");
                var createdOrder2 = await createOrder2Resp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(createdOrder2 != null, "Second order is null");

                HttpResponseMessage mgrCancelConfirmed = await managerClient.PostAsJsonAsync($"/api/customer-orders/{createdOrder2.Id}/status", new { status = "Cancelled" });
                Assert(mgrCancelConfirmed.IsSuccessStatusCode, $"Expected 200 OK when cancelling confirmed order, got {mgrCancelConfirmed.StatusCode}");
                var cancelledOrder = await mgrCancelConfirmed.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(cancelledOrder != null && cancelledOrder.Status == "Cancelled", "Order status must be Cancelled");

                // 9. Scenario B: Customer account-linked order fulfilled via AR sale
                var httpOrderWithAccountPayload = new
                {
                    customerId = customerA.ToString("D"),
                    customerName = "Customer Alpha",
                    customerPhone = "0917-111-2222",
                    notes = "AR fulfillment order",
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

                HttpResponseMessage createOrderAccountResp = await managerClient.PostAsJsonAsync("/api/customer-orders", httpOrderWithAccountPayload);
                Assert(createOrderAccountResp.StatusCode == HttpStatusCode.Created, "Failed to create account-linked order");
                var orderWithAccount = await createOrderAccountResp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(orderWithAccount != null && orderWithAccount.CustomerId == customerA, "Order customerId mismatch");

                // Scenario C: Negative test - fulfilling customerA's order with customerB's sale fails with 409 Conflict
                Guid saleForCustBHttp = SeedRealSale(paths.DatabasePath, "SL-AR-CUST-B", cashierId, productId, "Industrial Tool Set", 2000, 85_000, 170_000, customerId: customerB);
                HttpResponseMessage mismatchCustResp = await managerClient.PostAsJsonAsync($"/api/customer-orders/{orderWithAccount.Id}/fulfill", new { saleReference = "SL-AR-CUST-B" });
                Assert(mismatchCustResp.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict for customer mismatch, got {mismatchCustResp.StatusCode}");

                // Scenario B: Positive test - fulfilling customerA's order with customerA's AR sale succeeds
                Guid saleForCustAHttp = SeedRealSale(paths.DatabasePath, "SL-AR-CUST-A", cashierId, productId, "Industrial Tool Set", 2000, 85_000, 170_000, customerId: customerA);
                HttpResponseMessage matchCustResp = await managerClient.PostAsJsonAsync($"/api/customer-orders/{orderWithAccount.Id}/fulfill", new { saleReference = "SL-AR-CUST-A" });
                Assert(matchCustResp.IsSuccessStatusCode, $"Fulfill account-linked order failed: {matchCustResp.StatusCode}");
                var fulfilledAccountOrder = await matchCustResp.Content.ReadFromJsonAsync<CustomerOrder>();
                Assert(fulfilledAccountOrder != null && fulfilledAccountOrder.Status == "Fulfilled", "Fulfilled account order status check failed");
                Assert(fulfilledAccountOrder.SaleId == saleForCustAHttp, "SaleId mismatch for fulfilled account order");
            }
            finally
            {
                await app.StopAsync().ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }

            // 6. Test genuine upgrade path from migration v30 to v31
            await VerifyMigrationV30ToV31UpgradeAsync().ConfigureAwait(false);
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

    private static (long Count, long TotalCents) ReadPersistedOrderLines(string databasePath, Guid orderId)
    {
        using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(line_total_cents), 0)
            FROM customer_order_lines WHERE order_id = $id;
            """;
        command.Parameters.AddWithValue("$id", orderId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Unable to inspect persisted order lines.");
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static (long Orders, long Lines, long Audit, long Sales, long Payments, long Stock, long Cash)
        ReadOrderMutationCounts(string databasePath)
    {
        using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM customer_orders),
                   (SELECT COUNT(*) FROM customer_order_lines),
                   (SELECT COUNT(*) FROM audit_logs WHERE action = 'CUSTOMER_ORDER_CREATED'),
                   (SELECT COUNT(*) FROM sales),
                   (SELECT COUNT(*) FROM payments),
                   (SELECT COUNT(*) FROM stock_movements),
                   (SELECT COUNT(*) FROM cash_drawer_events);
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Unable to inspect order mutation counts.");
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6));
    }

    private static Guid SeedRealSale(
        string dbPath,
        string receiptNo,
        Guid cashierId,
        Guid productId,
        string productName,
        long qtyX1000,
        long unitPriceCents,
        long totalCents,
        int status = 2,
        bool isVoided = false,
        Guid? customerId = null)
    {
        Guid saleId = Guid.NewGuid();
        string now = DateTimeOffset.UtcNow.ToString("O");
        using SqliteConnection conn = new($"Data Source={dbPath}");
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO sales (
                id, receipt_no, sale_date, cashier_user_id, customer_id,
                subtotal_cents, discount_cents, tax_cents, total_cents,
                amount_paid_cents, change_cents, status, created_at, voided_at
            ) VALUES (
                $id, $receiptNo, $saleDate, $cashierUserId, $customerId,
                $subtotalCents, 0, 0, $totalCents,
                $totalCents, 0, $status, $createdAt, $voidedAt
            );

            INSERT INTO sale_items (
                id, sale_id, product_id, product_name_snapshot, barcode_snapshot,
                unit_name_snapshot, qty_x1000, unit_price_cents, cost_price_cents,
                discount_cents, line_total_cents
            ) VALUES (
                $itemId, $id, $productId, $productName, NULL,
                'pcs', $qtyX1000, $unitPriceCents, 0,
                0, $totalCents
            );
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString("D"));
        cmd.Parameters.AddWithValue("$receiptNo", receiptNo);
        cmd.Parameters.AddWithValue("$saleDate", now);
        cmd.Parameters.AddWithValue("$cashierUserId", cashierId.ToString("D"));
        cmd.Parameters.AddWithValue("$customerId", customerId.HasValue ? customerId.Value.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$subtotalCents", totalCents);
        cmd.Parameters.AddWithValue("$totalCents", totalCents);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$createdAt", now);
        cmd.Parameters.AddWithValue("$voidedAt", isVoided ? now : DBNull.Value);

        cmd.Parameters.AddWithValue("$itemId", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$productId", productId.ToString("D"));
        cmd.Parameters.AddWithValue("$productName", productName);
        cmd.Parameters.AddWithValue("$qtyX1000", qtyX1000);
        cmd.Parameters.AddWithValue("$unitPriceCents", unitPriceCents);

        cmd.ExecuteNonQuery();
        return saleId;
    }

    private static Guid SeedCustomer(string dbPath, string fullName, string contactNumber, Guid createdByUserId)
    {
        Guid id = Guid.NewGuid();
        string now = DateTimeOffset.UtcNow.ToString("O");
        using SqliteConnection conn = new($"Data Source={dbPath}");
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO customers (id, full_name, phone, is_active, created_by_user_id, created_at, updated_at)
            VALUES ($id, $fullName, $phone, 1, $createdByUserId, $now, $now);
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.Parameters.AddWithValue("$fullName", fullName);
        cmd.Parameters.AddWithValue("$phone", contactNumber);
        cmd.Parameters.AddWithValue("$createdByUserId", createdByUserId.ToString("D"));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static Task VerifyMigrationV30ToV31UpgradeAsync()
    {
        string upgradeRoot = Path.Combine(Path.GetTempPath(), "salryn-migration-v30-v31-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(upgradeRoot);
            paths.EnsureDirectories();

            // 1. Run migrations 1 through 30 only (simulating an existing v30 database)
            SqliteConnectionFactory connectionFactory = new(paths);
            IDatabaseMigration[] v30Migrations = new IDatabaseMigration[]
            {
                new InitialSchemaMigration(),
                new BirMachineProfileSchemaMigration(),
                new BirOfficialDocumentSequenceSchemaMigration(),
                new BirElectronicJournalSchemaMigration(),
                new BirElectronicJournalSchemaHardeningMigration(),
                new BirZReadingSchemaMigration(),
                new BirElectronicJournalCorrectionEventSchemaMigration(),
                new ProductTaxClassificationSchemaMigration(),
                new DeploymentIdentitySchemaMigration(),
                new TerminalRegistrySchemaMigration(),
                new LanRemoteTransportSchemaMigration(),
                new TerminalCredentialHandshakeSchemaMigration(),
                new OfficialReceiptProfileSchemaMigration(),
                new OfficialDryRunPackageSchemaMigration(),
                new VatRegistrationModeSchemaMigration(),
                new OfficialDocumentLifecycleCompletionSchemaMigration(),
                new OfficialDocumentLifecycleAccountingSchemaMigration(),
                new StorePrinterModeSchemaMigration(),
                new WindowsPrinterNameSchemaMigration(),
                new StoreLogoBrandingSchemaMigration(),
                new RemotePosCheckoutRequestSchemaMigration(),
                new AccountsReceivableSchemaMigration(),
                new HostCheckoutRequestSchemaMigration(),
                new ReceivableCollectionRequestSchemaMigration(),
                new CashDrawerReconciliationSchemaMigration(),
                new CashDrawerCloseReplaySchemaMigration(),
                new SaleVoidAuditReconciliationMigration(),
                new ProcureToPaySchemaMigration(),
                new ProcureToPayR2HardeningMigration(),
                new CustomerOrdersAndQuotationsSchemaMigration()
            };

            MigrationRunner runnerV30 = new(connectionFactory, v30Migrations);
            runnerV30.RunPendingMigrations();

            // Verify database is exactly at version 30 and index uq_customer_orders_sale_id does not exist yet
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
                long maxV = Convert.ToInt64(cmd.ExecuteScalar());
                Assert(maxV == 30, $"Expected max migration version 30 before upgrade, got {maxV}");

                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='uq_customer_orders_sale_id';";
                long idxCount = Convert.ToInt64(cmd.ExecuteScalar());
                Assert(idxCount == 0, $"Expected uq_customer_orders_sale_id index to NOT exist at v30, got {idxCount}");
            }

            // 2. Perform official upgrade using the full factory (advances from v30 to v31)
            var factory = DefaultDatabaseInitializationFactory.Create(paths);
            var initRes = factory.InitializeDatabase();
            Assert(initRes.IsSuccess, "Upgrade from v30 to v31 failed: " + (initRes.IsFailure ? initRes.ErrorDetail.Message : ""));

            // 3. Verify upgraded database
            using (SqliteConnection conn = new($"Data Source={paths.DatabasePath}"))
            {
                conn.Open();
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
                long maxV = Convert.ToInt64(cmd.ExecuteScalar());
                Assert(maxV == 31, $"Expected max migration version 31 after upgrade, got {maxV}");

                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='uq_customer_orders_sale_id';";
                long idxCount = Convert.ToInt64(cmd.ExecuteScalar());
                Assert(idxCount == 1, $"Expected uq_customer_orders_sale_id index to exist at v31, got {idxCount}");

                cmd.CommandText = "PRAGMA integrity_check;";
                string? integrity = cmd.ExecuteScalar()?.ToString();
                Assert(integrity == "ok", $"Integrity check failed: {integrity}");

                cmd.CommandText = "PRAGMA foreign_key_check;";
                using SqliteDataReader fkReader = cmd.ExecuteReader();
                Assert(!fkReader.HasRows, "Foreign key check failed: broken references found.");
            }

            return Task.CompletedTask;
        }
        finally
        {
            try
            {
                if (Directory.Exists(upgradeRoot)) Directory.Delete(upgradeRoot, true);
            }
            catch { }
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

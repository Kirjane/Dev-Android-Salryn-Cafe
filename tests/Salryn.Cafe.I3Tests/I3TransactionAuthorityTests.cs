using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Services;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class I3TransactionAuthorityTests
{
    [Fact]
    public async Task LiveHostComposition_SharesMutationGuardAcrossRequestScopes()
    {
        using I3TestDatabaseFixture fx = new();
        await using var app = await LocalApiApplicationFactory.CreateAsync(Array.Empty<string>(), new LocalApiApplicationOptions
        {
            Composition = fx.Composition,
            InitializeDatabase = false,
            EnableStaticWebAssets = false,
            WriteDevelopmentDatabasePath = false
        });
        using var firstRequest = app.Services.CreateScope();
        using var secondRequest = app.Services.CreateScope();
        Assert.Same(firstRequest.ServiceProvider.GetRequiredService<CafeMutationIdempotencyGuard>(),
            secondRequest.ServiceProvider.GetRequiredService<CafeMutationIdempotencyGuard>());
    }

    [Fact]
    public void PaymentMethodEnum_ContainsOnlyCashAndAccountsReceivable()
    {
        string[] names = Enum.GetNames<PaymentMethod>();
        Assert.Equal(2, names.Length);
        Assert.Contains(nameof(PaymentMethod.Cash), names);
        Assert.Contains(nameof(PaymentMethod.AccountsReceivable), names);
        Assert.DoesNotContain("GCash", names);
        Assert.DoesNotContain("Maya", names);
        Assert.DoesNotContain("Debit", names);
    }

    [Theory]
    [InlineData("GCash")]
    [InlineData("Maya")]
    [InlineData("Debit")]
    [InlineData("CreditCard")]
    public async Task HostCheckout_RejectsNonCashNonArPaymentMethods(string rejectedMethod)
    {
        using I3TestDatabaseFixture fx = new();
        HostOperatorApiServices hostOps = HostOperatorApiServices.Create(fx.Composition);

        HostOperatorCheckoutOperationResult result = await hostOps.CheckoutService.SubmitAsync(
            fx.AdminUserId,
            "Cashier One",
            new HostOperatorCheckoutRequest(
                Lines: new[] { new HostOperatorCheckoutLineRequest(fx.ProductId, 1000) },
                Payments: new[] { new HostOperatorCheckoutPaymentRequest(rejectedMethod, 15000) },
                ClientRequestId: Guid.NewGuid().ToString("D")));

        Assert.False(result.Ok);
        Assert.Equal("HostOperator.Checkout.UnsupportedPaymentMethod", result.Code);
        Assert.Equal(0, fx.CountSales());
    }

    [Theory]
    [InlineData("GCash")]
    [InlineData("Maya")]
    [InlineData("Debit")]
    public async Task AccountsReceivableCollect_RejectsNonCashMethods(string rejectedMethod)
    {
        using I3TestDatabaseFixture fx = new();
        SqliteAccountsReceivableService ar = new(fx.ConnectionFactory);

        Result<AccountsReceivableCollection> result = await ar.CollectAsync(
            new RecordReceivableCollectionRequest(
                CustomerId: fx.CustomerId,
                AmountCents: 5000,
                Method: rejectedMethod,
                Note: "Should fail",
                CollectedByUserId: fx.AdminUserId,
                ClientRequestId: Guid.NewGuid().ToString("D"),
                ReferenceNumber: "REF-123",
                NonCashConfirmed: true));

        Assert.True(result.IsFailure);
        Assert.Equal("Receivables.InvalidPaymentMethod", result.ErrorDetail.Code);
    }

    [Fact]
    public async Task HostCheckout_MissingClientRequestId_FailsClosed()
    {
        using I3TestDatabaseFixture fx = new();
        HostOperatorApiServices hostOps = HostOperatorApiServices.Create(fx.Composition);

        HostOperatorCheckoutOperationResult result = await hostOps.CheckoutService.SubmitAsync(
            fx.AdminUserId,
            "Cashier One",
            new HostOperatorCheckoutRequest(
                Lines: new[] { new HostOperatorCheckoutLineRequest(fx.ProductId, 1000) },
                Payments: new[] { new HostOperatorCheckoutPaymentRequest("Cash", 15000) },
                ClientRequestId: null));

        Assert.False(result.Ok);
        Assert.Equal("HostOperator.Checkout.ClientRequestIdRequired", result.Code);
        Assert.Equal(0, fx.CountSales());
    }

    [Fact]
    public async Task HostCheckout_ConcurrentDuplicateClientRequestId_CommitsSingleSaleAndReplays()
    {
        using I3TestDatabaseFixture fx = new();
        HostOperatorApiServices hostOps = HostOperatorApiServices.Create(fx.Composition);
        string requestId = Guid.NewGuid().ToString("D");

        HostOperatorCheckoutRequest request = new(
            Lines: new[] { new HostOperatorCheckoutLineRequest(fx.ProductId, 1000) },
            Payments: new[] { new HostOperatorCheckoutPaymentRequest("Cash", 20000) },
            ClientRequestId: requestId);

        Task<HostOperatorCheckoutOperationResult> t1 = hostOps.CheckoutService.SubmitAsync(fx.AdminUserId, "Cashier One", request);
        Task<HostOperatorCheckoutOperationResult> t2 = hostOps.CheckoutService.SubmitAsync(fx.AdminUserId, "Cashier One", request);
        HostOperatorCheckoutOperationResult[] results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.True(r.Ok));
        Assert.Equal(1, fx.CountSales());
        Assert.Equal(results[0].Checkout!.SaleId, results[1].Checkout!.SaleId);
        Assert.Equal(results[0].Checkout!.ReceiptNumber, results[1].Checkout!.ReceiptNumber);
        Assert.Equal(1, results.Count(r => r.Checkout!.Duplicate));
    }

    [Fact]
    public async Task HostCheckout_ClientRequestIdReusedWithDifferentPayload_FailsClosed()
    {
        using I3TestDatabaseFixture fx = new();
        HostOperatorApiServices hostOps = HostOperatorApiServices.Create(fx.Composition);
        string requestId = Guid.NewGuid().ToString("D");

        HostOperatorCheckoutOperationResult first = await hostOps.CheckoutService.SubmitAsync(
            fx.AdminUserId,
            "Cashier One",
            new HostOperatorCheckoutRequest(
                Lines: new[] { new HostOperatorCheckoutLineRequest(fx.ProductId, 1000) },
                Payments: new[] { new HostOperatorCheckoutPaymentRequest("Cash", 15000) },
                ClientRequestId: requestId));
        Assert.True(first.Ok);

        HostOperatorCheckoutOperationResult conflict = await hostOps.CheckoutService.SubmitAsync(
            fx.AdminUserId,
            "Cashier One",
            new HostOperatorCheckoutRequest(
                Lines: new[] { new HostOperatorCheckoutLineRequest(fx.ProductId, 2000) },
                Payments: new[] { new HostOperatorCheckoutPaymentRequest("Cash", 30000) },
                ClientRequestId: requestId));

        Assert.False(conflict.Ok);
        Assert.Equal("HostOperator.Checkout.ClientRequestIdReused", conflict.Code);
        Assert.Equal(1, fx.CountSales());
    }

    [Fact]
    public async Task HostCheckout_PartialCashPlusAccountsReceivable_SucceedsAndRecordsCashArCollection()
    {
        using I3TestDatabaseFixture fx = new();
        HostOperatorApiServices hostOps = HostOperatorApiServices.Create(fx.Composition);

        HostOperatorCheckoutOperationResult sale = await hostOps.CheckoutService.SubmitAsync(
            fx.AdminUserId,
            "Cashier One",
            new HostOperatorCheckoutRequest(
                Lines: new[] { new HostOperatorCheckoutLineRequest(fx.ProductId, 1000) },
                Payments: new[]
                {
                    new HostOperatorCheckoutPaymentRequest("Cash", 5000),
                    new HostOperatorCheckoutPaymentRequest("AccountsReceivable", 10000)
                },
                CustomerId: fx.CustomerId,
                ClientRequestId: Guid.NewGuid().ToString("D")));

        Assert.True(sale.Ok, sale.Message);
        Assert.Equal(15000, sale.Checkout!.TotalCents);
        Assert.Equal(0, sale.Checkout!.ChangeCents);
        Assert.Equal(1, fx.CountSales());

        // Collect AR balance in Cash
        SqliteAccountsReceivableService ar = new(fx.ConnectionFactory);
        Result<AccountsReceivableCollection> collection = await ar.CollectAsync(
            new RecordReceivableCollectionRequest(
                CustomerId: fx.CustomerId,
                AmountCents: 10000,
                Method: "Cash",
                Note: "Paid in cash",
                CollectedByUserId: fx.AdminUserId,
                ClientRequestId: Guid.NewGuid().ToString("D")));

        Assert.True(collection.IsSuccess, collection.IsFailure ? collection.ErrorDetail.Message : string.Empty);
        Assert.Equal("Cash", collection.Value.Method);
    }

    [Fact]
    public async Task CafeMutationIdempotencyGuard_MissingOperationId_Returns400()
    {
        using I3TestDatabaseFixture fx = new();
        CafeMutationIdempotencyGuard guard = new(fx.ConnectionFactory);
        DefaultHttpContext http = new();

        int calls = 0;
        IResult result = await guard.ExecuteSyncAsync(
            http,
            "POST /api/menu/categories",
            fx.AdminUserId,
            bodyOperationId: null,
            canonicalPayload: new { name = "Coffee" },
            _ =>
            {
                calls++;
                return new IdempotentExecutionOutcome(200, new { ok = true });
            });

        Assert.Equal(0, calls);
        IStatusCodeHttpResult statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }

    [Fact]
    public async Task CafeMutationIdempotencyGuard_SameOperationIdSamePayload_ReplaysWithoutReexecuting()
    {
        using I3TestDatabaseFixture fx = new();
        CafeMutationIdempotencyGuard guard = new(fx.ConnectionFactory);
        Guid opId = Guid.NewGuid();
        int calls = 0;

        DefaultHttpContext http1 = new();
        IResult r1 = await guard.ExecuteSyncAsync(
            http1,
            "POST /api/menu/categories",
            fx.AdminUserId,
            opId,
            new { name = "Espresso" },
            _ =>
            {
                calls++;
                return new IdempotentExecutionOutcome(200, new { id = "cat-1", name = "Espresso" });
            });

        DefaultHttpContext http2 = new();
        IResult r2 = await guard.ExecuteSyncAsync(
            http2,
            "POST /api/menu/categories",
            fx.AdminUserId,
            opId,
            new { name = "Espresso" },
            _ =>
            {
                calls++;
                return new IdempotentExecutionOutcome(200, new { id = "cat-2", name = "Espresso" });
            });

        Assert.Equal(1, calls);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r1).StatusCode);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r2).StatusCode);
        Assert.Equal("true", http2.Response.Headers["X-Idempotency-Replayed"].ToString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task CafeMutationIdempotencyGuard_DoesNotCacheAuthorizationDenials(int deniedStatus)
    {
        using I3TestDatabaseFixture fx = new();
        Guid operationId = Guid.NewGuid();
        int calls = 0;
        CafeMutationIdempotencyGuard guard = new(fx.ConnectionFactory);
        var denied = await guard.ExecuteSyncAsync(new DefaultHttpContext(), "POST /api/menu/categories",
            fx.AdminUserId, operationId, new { name = "Coffee" }, _ =>
            {
                calls++;
                return new IdempotentExecutionOutcome(deniedStatus, new { error = "denied" });
            });
        Assert.Equal(deniedStatus, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        // Simulate a new request/service instance after permission/session correction.
        guard = new CafeMutationIdempotencyGuard(fx.ConnectionFactory);
        var allowed = await guard.ExecuteSyncAsync(new DefaultHttpContext(), "POST /api/menu/categories",
            fx.AdminUserId, operationId, new { name = "Coffee" }, _ =>
            {
                calls++;
                return new IdempotentExecutionOutcome(200, new { id = "coffee" });
            });
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(allowed).StatusCode);
        Assert.Equal(2, calls);
        var replayContext = new DefaultHttpContext();
        await new CafeMutationIdempotencyGuard(fx.ConnectionFactory).ExecuteSyncAsync(replayContext,
            "POST /api/menu/categories", fx.AdminUserId, operationId, new { name = "Coffee" },
            _ => throw new InvalidOperationException("Successful outcome must replay durably."));
        Assert.Equal("true", replayContext.Response.Headers["X-Idempotency-Replayed"].ToString());
    }

    [Fact]
    public async Task CafeMutationIdempotencyGuard_SameOperationIdDivergentPayload_Returns409Conflict()
    {
        using I3TestDatabaseFixture fx = new();
        CafeMutationIdempotencyGuard guard = new(fx.ConnectionFactory);
        Guid opId = Guid.NewGuid();

        DefaultHttpContext http1 = new();
        await guard.ExecuteSyncAsync(
            http1,
            "POST /api/cafe/ingredients",
            fx.AdminUserId,
            opId,
            new { name = "Whole Milk", unit = "ml" },
            _ => new IdempotentExecutionOutcome(200, new { ok = true }));

        DefaultHttpContext http2 = new();
        IResult conflict = await guard.ExecuteSyncAsync(
            http2,
            "POST /api/cafe/ingredients",
            fx.AdminUserId,
            opId,
            new { name = "Oat Milk", unit = "ml" },
            _ => new IdempotentExecutionOutcome(200, new { ok = true }));

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(conflict).StatusCode);
    }

    [Fact]
    public void CafeSaleFinancialCalculator_EnforcesCashAndArInvariantsDeterministically()
    {
        Guid saleItemId = Guid.NewGuid();
        CafeSaleFinancialDecisionRequest request = new(
            OperationId: Guid.NewGuid(),
            FinancialDecisionId: Guid.NewGuid(),
            StoreId: Guid.NewGuid(),
            OrderId: null,
            OrderRevisionId: null,
            ExpectedOrderVersion: null,
            SaleId: Guid.NewGuid(),
            CashierSessionId: Guid.NewGuid(),
            ActorUserId: Guid.NewGuid(),
            Lines: new[]
            {
                new CafeSaleFinancialLineInput(saleItemId, GrossLineCents: 20000, LineDiscountCents: 2000, DisplayOrder: 1)
            },
            OrderDiscountCents: 1000,
            DiscountReason: "Regular customer discount",
            Fees: Array.Empty<CafeSaleFeeMutation>(),
            TaxCents: 0,
            CashTenderCents: 10000,
            AccountsReceivableTenderCents: 7000,
            OccurredAtUtc: DateTimeOffset.UtcNow);

        Result<CafeSaleFinancialDecision> decision = CafeSaleFinancialCalculator.Calculate(request);
        Assert.True(decision.IsSuccess, decision.IsFailure ? decision.ErrorDetail.Message : string.Empty);
        Assert.Equal(17000, decision.Value.NetSaleTotalCents);
        Assert.Equal(0, decision.Value.ChangeCents);
        Assert.False(string.IsNullOrWhiteSpace(decision.Value.ContentHash));
    }

    [Fact]
    public void PermissionsAndComposition_ExposeAllCafeAndReceivablesCapabilities()
    {
        using I3TestDatabaseFixture fx = new();

        Assert.NotNull(fx.Composition.MenuCatalog);
        Assert.NotNull(fx.Composition.IngredientRecipes);
        Assert.NotNull(fx.Composition.SupplierDirectory);
        Assert.NotNull(fx.Composition.CafeInventory);
        Assert.NotNull(fx.Composition.CafeOrders);
        Assert.NotNull(fx.Composition.CafeOrderCompletion);
        Assert.NotNull(fx.Composition.CafeCash);
        Assert.NotNull(fx.Composition.CafePrint);
        Assert.NotNull(fx.Composition.CafeDelivery);
        Assert.NotNull(fx.Composition.CafeBooking);
        Assert.NotNull(fx.Composition.CafeAttendanceIncident);
        Assert.NotNull(fx.Composition.CafeManagementReports);

        Assert.Equal("Orders.Complete", SalrynPermissions.OrdersComplete);
        Assert.Equal("Booking.Fulfill", SalrynPermissions.BookingFulfill);
        Assert.Equal("Receivables.Collect", SalrynPermissions.ReceivablesCollect);
        Assert.Equal("Orders.Complete", LocalApiPermissions.OrdersComplete);
        Assert.Equal("Receivables.Collect", LocalApiPermissions.ReceivablesCollect);
    }
}

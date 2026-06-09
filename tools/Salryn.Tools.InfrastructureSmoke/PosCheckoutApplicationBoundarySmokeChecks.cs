using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

internal static class PosCheckoutApplicationBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== POS CHECKOUT APPLICATION BOUNDARY SMOKE CHECKS =====");

        FakePosCheckoutService fakeCheckout = new();
        PosCheckoutApplicationService service = new(fakeCheckout);

        ExpectFailure(
            service.CompleteSaleAsync(CreateRequest(
                cashierUserId: Guid.Empty,
                trackStock: true)).GetAwaiter().GetResult(),
            "PosCheckoutBoundary.InvalidCashierUserId",
            ErrorType.Validation,
            "CheckoutBoundary.InvalidCashierUserId");

        if (fakeCheckout.CompleteCallCount != 0)
        {
            throw new InvalidOperationException("Boundary invalid cashier should not call checkout service.");
        }

        ExpectFailure(
            service.CompleteSaleAsync(new PosCheckoutApplicationRequest(
                Guid.NewGuid(),
                "APP-EMPTY-LINES",
                Array.Empty<PosCheckoutLineInput>(),
                new[] { new PosCheckoutPaymentInput(PaymentMethod.Cash, 1000) },
                0,
                0,
                "empty lines")).GetAwaiter().GetResult(),
            "PosCheckoutBoundary.EmptyLines",
            ErrorType.Validation,
            "CheckoutBoundary.LinesRequired");

        if (fakeCheckout.CompleteCallCount != 0)
        {
            throw new InvalidOperationException("Boundary empty lines should not call checkout service.");
        }

        Result trackedResult = service.CompleteSaleAsync(CreateRequest(
            cashierUserId: Guid.NewGuid(),
            trackStock: true)).GetAwaiter().GetResult();

        RequireSuccess(trackedResult, "PosCheckoutBoundary.TrackedLine");

        if (fakeCheckout.CompleteCallCount != 1 ||
            fakeCheckout.LastRequest is null)
        {
            throw new InvalidOperationException("Boundary did not call checkout service for valid tracked request.");
        }

        VerifyTrackedRequest(fakeCheckout.LastRequest);

        Result untrackedResult = service.CompleteSaleAsync(CreateRequest(
            cashierUserId: Guid.NewGuid(),
            trackStock: false)).GetAwaiter().GetResult();

        RequireSuccess(untrackedResult, "PosCheckoutBoundary.UntrackedLine");

        if (fakeCheckout.CompleteCallCount != 2 ||
            fakeCheckout.LastRequest is null ||
            fakeCheckout.LastRequest.StockMovements.Count != 0)
        {
            throw new InvalidOperationException("Boundary untracked product should not create stock movements.");
        }

        fakeCheckout.NextResult = Result.Fail(Error.Conflict(
            "CheckoutCore.SimulatedFailure",
            "Simulated checkout core failure."));

        ExpectFailure(
            service.CompleteSaleAsync(CreateRequest(
                cashierUserId: Guid.NewGuid(),
                trackStock: true)).GetAwaiter().GetResult(),
            "PosCheckoutBoundary.CoreFailurePropagation",
            ErrorType.Conflict,
            "CheckoutCore.SimulatedFailure");

        Console.WriteLine("POS checkout application boundary smoke checks succeeded.");
        Console.WriteLine();
    }

    private static PosCheckoutApplicationRequest CreateRequest(
        Guid cashierUserId,
        bool trackStock)
    {
        Guid productId = Guid.NewGuid();

        return new PosCheckoutApplicationRequest(
            cashierUserId,
            "APP-" + Guid.NewGuid().ToString("N"),
            new[]
            {
                new PosCheckoutLineInput(
                    productId,
                    "Boundary Smoke Product",
                    "1234567890123",
                    "pc",
                    QuantityX1000: 2000,
                    UnitPriceCents: 1500,
                    CostPriceCents: 900,
                    DiscountCents: 100,
                    TrackStock: trackStock)
            },
            new[]
            {
                new PosCheckoutPaymentInput(
                    PaymentMethod.Cash,
                    AmountCents: 3000)
            },
            SaleDiscountCents: 200,
            TaxCents: 50,
            ReceiptSnapshot: "Boundary smoke receipt snapshot");
    }

    private static void VerifyTrackedRequest(CompleteCheckoutSaleRequest request)
    {
        if (request.Sale.Id == Guid.Empty ||
            request.Sale.CashierUserId == Guid.Empty ||
            request.Sale.CashierSessionId is not null ||
            request.Sale.Status != SaleStatus.Completed ||
            request.Sale.Subtotal.Cents != 2900 ||
            request.Sale.Discount.Cents != 200 ||
            request.Sale.Tax.Cents != 50 ||
            request.Sale.Total.Cents != 2750 ||
            request.Sale.AmountPaid.Cents != 3000 ||
            request.Sale.Change.Cents != 250)
        {
            throw new InvalidOperationException("Boundary sale mapping verification failed.");
        }

        if (request.Items.Count != 1 ||
            request.Items[0].SaleId != request.Sale.Id ||
            request.Items[0].Quantity.ValueX1000 != 2000 ||
            request.Items[0].UnitPrice.Cents != 1500 ||
            request.Items[0].Discount.Cents != 100 ||
            request.Items[0].LineTotal.Cents != 2900)
        {
            throw new InvalidOperationException("Boundary sale item mapping verification failed.");
        }

        if (request.Payments.Count != 1 ||
            request.Payments[0].SaleId != request.Sale.Id ||
            request.Payments[0].Method != PaymentMethod.Cash ||
            request.Payments[0].Amount.Cents != 3000)
        {
            throw new InvalidOperationException("Boundary payment mapping verification failed.");
        }

        if (request.StockMovements.Count != 1 ||
            request.StockMovements[0].ProductId != request.Items[0].ProductId ||
            request.StockMovements[0].MovementType != StockMovementType.Sale ||
            request.StockMovements[0].QuantityChange.ValueX1000 != 2000 ||
            request.StockMovements[0].ReferenceType != "SALE" ||
            request.StockMovements[0].ReferenceId != request.Sale.Id ||
            request.StockMovements[0].UserId != request.Sale.CashierUserId)
        {
            throw new InvalidOperationException("Boundary stock movement mapping verification failed.");
        }
    }

    private static void ExpectFailure(
        Result result,
        string area,
        ErrorType expectedType,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (result.ErrorDetail.Type != expectedType ||
            !string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedType}/{expectedCode}, got {result.ErrorDetail.Type}/{result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private sealed class FakePosCheckoutService : IPosCheckoutService
    {
        public int CompleteCallCount { get; private set; }

        public CompleteCheckoutSaleRequest? LastRequest { get; private set; }

        public Result NextResult { get; set; } = Result.Success();

        public Task<Result> CompleteSaleAsync(
            CompleteCheckoutSaleRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CompleteCallCount++;
            LastRequest = request;

            Result result = NextResult;
            NextResult = Result.Success();

            return Task.FromResult(result);
        }
    }
}

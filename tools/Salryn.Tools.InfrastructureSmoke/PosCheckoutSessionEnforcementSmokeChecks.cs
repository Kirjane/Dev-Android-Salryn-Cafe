using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

internal static class PosCheckoutSessionEnforcementSmokeChecks
{
    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== POS CHECKOUT SESSION ENFORCEMENT SMOKE CHECKS =====");

        Guid cashierId = Guid.NewGuid();
        Guid openSessionId = Guid.NewGuid();
        Guid wrongSessionId = Guid.NewGuid();

        FakeSaleWriteRepository saleWriteRepository = new();
        FakeCashierSessionService cashierSessionService = new();

        PosCheckoutService service = new(
            saleWriteRepository,
            cashierSessionService);

        ExpectFailure(
            service.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                CreateSale(Guid.Empty, null),
                Array.Empty<SaleItem>(),
                Array.Empty<Payment>(),
                Array.Empty<StockMovement>())).GetAwaiter().GetResult(),
            "PosCheckout.InvalidCashierUserId",
            ErrorType.Validation,
            "Checkout.InvalidCashierUserId");

        if (cashierSessionService.FindOpenCallCount != 0 ||
            saleWriteRepository.AddCompletedSaleCallCount != 0)
        {
            throw new InvalidOperationException("Checkout invalid cashier should not query session or write sale.");
        }

        cashierSessionService.OpenSession = null;

        ExpectFailure(
            service.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                CreateSale(cashierId, null),
                Array.Empty<SaleItem>(),
                Array.Empty<Payment>(),
                Array.Empty<StockMovement>())).GetAwaiter().GetResult(),
            "PosCheckout.OpenSessionRequired",
            ErrorType.Conflict,
            "Checkout.OpenCashierSessionRequired");

        if (saleWriteRepository.AddCompletedSaleCallCount != 0)
        {
            throw new InvalidOperationException("Checkout without open cashier session should not write sale.");
        }

        cashierSessionService.OpenSession = CreateOpenSession(openSessionId, cashierId);

        ExpectFailure(
            service.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                CreateSale(cashierId, wrongSessionId),
                Array.Empty<SaleItem>(),
                Array.Empty<Payment>(),
                Array.Empty<StockMovement>())).GetAwaiter().GetResult(),
            "PosCheckout.SessionMismatch",
            ErrorType.Validation,
            "Checkout.CashierSessionMismatch");

        if (saleWriteRepository.AddCompletedSaleCallCount != 0)
        {
            throw new InvalidOperationException("Checkout session mismatch should not write sale.");
        }

        Result enforcedResult = service.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
            CreateSale(cashierId, null),
            Array.Empty<SaleItem>(),
            Array.Empty<Payment>(),
            Array.Empty<StockMovement>())).GetAwaiter().GetResult();

        RequireSuccess(enforcedResult, "PosCheckout.EnforceOpenSession");

        if (saleWriteRepository.AddCompletedSaleCallCount != 1 ||
            saleWriteRepository.LastSale is null ||
            saleWriteRepository.LastSale.CashierSessionId != openSessionId)
        {
            throw new InvalidOperationException("Checkout did not enforce the currently open cashier session ID.");
        }

        Result matchingResult = service.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
            CreateSale(cashierId, openSessionId),
            Array.Empty<SaleItem>(),
            Array.Empty<Payment>(),
            Array.Empty<StockMovement>())).GetAwaiter().GetResult();

        RequireSuccess(matchingResult, "PosCheckout.ExistingMatchingSession");

        if (saleWriteRepository.AddCompletedSaleCallCount != 2 ||
            saleWriteRepository.LastSale is null ||
            saleWriteRepository.LastSale.CashierSessionId != openSessionId)
        {
            throw new InvalidOperationException("Checkout failed with already-matching cashier session ID.");
        }

        saleWriteRepository.NextResult = Result.Fail(Error.Conflict(
            "Sales.RepositoryFailureSmoke",
            "Simulated sale repository failure."));

        ExpectFailure(
            service.CompleteSaleAsync(new CompleteCheckoutSaleRequest(
                CreateSale(cashierId, null),
                Array.Empty<SaleItem>(),
                Array.Empty<Payment>(),
                Array.Empty<StockMovement>())).GetAwaiter().GetResult(),
            "PosCheckout.RepositoryFailurePropagation",
            ErrorType.Conflict,
            "Sales.RepositoryFailureSmoke");

        Console.WriteLine("POS checkout session enforcement smoke checks succeeded.");
        Console.WriteLine();
    }

    private static Sale CreateSale(Guid cashierUserId, Guid? cashierSessionId)
    {
        return new Sale
        {
            Id = Guid.NewGuid(),
            ReceiptNumber = $"CHK-{Guid.NewGuid():N}",
            SaleDate = DateTimeOffset.UtcNow,
            CashierUserId = cashierUserId,
            CashierSessionId = cashierSessionId,
            Subtotal = Money.FromCents(1000),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(1000),
            AmountPaid = Money.FromCents(1000),
            Change = Money.Zero,
            Status = SaleStatus.Completed,
            ReceiptSnapshot = "Checkout session enforcement smoke receipt",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static CashierSessionReadModel CreateOpenSession(Guid sessionId, Guid cashierId)
    {
        return new CashierSessionReadModel(
            sessionId,
            cashierId,
            "Smoke Cashier",
            DateTimeOffset.UtcNow,
            null,
            Money.Zero,
            Money.Zero,
            null,
            Money.Zero,
            CashierSessionStatus.Open,
            null);
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

    private sealed class FakeSaleWriteRepository : ISaleWriteRepository
    {
        public int AddCompletedSaleCallCount { get; private set; }

        public Sale? LastSale { get; private set; }

        public Result NextResult { get; set; } = Result.Success();

        public Task<Result> AddCompletedSaleAsync(
            Sale sale,
            IReadOnlyList<SaleItem> items,
            IReadOnlyList<Payment> payments,
            IReadOnlyList<StockMovement> stockMovements,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddCompletedSaleCallCount++;
            LastSale = sale;

            Result result = NextResult;
            NextResult = Result.Success();

            return Task.FromResult(result);
        }

        public Task<Result> MarkVoidedAsync(
            Guid saleId,
            Guid voidedByUserId,
            string reason,
            IReadOnlyList<StockMovement> reversalMovements,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result.Fail(Error.Unexpected(
                "Smoke.NotUsed",
                "Void is not used by checkout session enforcement smoke.")));
        }
    }

    private sealed class FakeCashierSessionService : ICashierSessionService
    {
        public CashierSessionReadModel? OpenSession { get; set; }

        public int FindOpenCallCount { get; private set; }

        public Task<Result<CashierSessionReadModel?>> FindOpenSessionByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FindOpenCallCount++;

            return Task.FromResult(Result<CashierSessionReadModel?>.Success(OpenSession));
        }

        public Task<Result<CashierSessionReadModel?>> FindByIdAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CashierSessionReadModel? result = OpenSession?.Id == sessionId
                ? OpenSession
                : null;

            return Task.FromResult(Result<CashierSessionReadModel?>.Success(result));
        }

        public Task<Result<CashierSessionReadModel>> OpenAsync(
            OpenCashierSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result<CashierSessionReadModel>.Fail(Error.Unexpected(
                "Smoke.NotUsed",
                "Open is not used by checkout session enforcement smoke.")));
        }

        public Task<Result<CashierSessionReadModel>> CloseAsync(
            CloseCashierSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result<CashierSessionReadModel>.Fail(Error.Unexpected(
                "Smoke.NotUsed",
                "Close is not used by checkout session enforcement smoke.")));
        }

        public Task<Result<CashierSessionReadModel>> ForceCloseAsync(
            ForceCloseCashierSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Result<CashierSessionReadModel>.Fail(Error.Unexpected(
                "Smoke.NotUsed",
                "Force close is not used by checkout session enforcement smoke.")));
        }
    }
}

using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

// LAN-M16-QA-A remote-pos-post-commit-audit-safety smoke. Proves a best-effort audit failure AFTER
// a committed sale can never turn a Completed checkout into a failed/unknown result or create retry
// ambiguity. It drives a REAL host-owned sale with a deliberately failing audit sink (both a thrown
// exception and a failed Result) and proves: the first response is Completed (not RetryableUnknown),
// no raw audit detail leaks, and a retry replays the original committed result without double-posting
// the sale or double-decrementing inventory.
internal static class RemotePosPostCommitAuditSafetySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16-QA-A REMOTE POS POST-COMMIT AUDIT SAFETY SMOKE CHECKS =====");

        RemotePosSmokeSupport.EnsureDatabaseReady(projectRoot);
        SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(projectRoot);

        UserReadModel cashier = RemotePosSmokeSupport.GetCashier(composition);

        // Case 1: the audit sink THROWS after commit -> checkout still Completed; retry replays.
        RequireCommitSurvivesAuditFailure(
            projectRoot, composition, cashier, new ThrowingAuditWriteRepository(), "throwing audit sink");

        // Case 2: the audit sink returns a FAILED Result after commit -> same guarantees.
        RequireCommitSurvivesAuditFailure(
            projectRoot, composition, cashier, new FailingResultAuditWriteRepository(), "failed-result audit sink");

        Console.WriteLine("LAN-M16-QA-A remote POS post-commit audit safety smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireCommitSurvivesAuditFailure(
        string projectRoot,
        SalrynLocalComposition composition,
        UserReadModel cashier,
        IAuditWriteRepository failingAudit,
        string label)
    {
        RemotePosSmokeSupport.ForceCloseOpenShift(RemotePosSmokeSupport.CashierSessions(composition), cashier.Id);

        RemotePosCheckoutService checkout = BuildCheckoutServiceWithAudit(composition, failingAudit);

        Product product = RemotePosSmokeSupport.SeedTrackedProduct(
            composition, "LAN-M16 Audit Safety Product", stockX1000: 4000, sellingPriceCents: 1200);

        RemotePosOperatorSession session = RemotePosSmokeSupport.CreateSession(
            cashier.Id, "Cashier", SalrynPermissions.SalesCreate, SalrynPermissions.ShiftOpenClose);

        RemotePosSmokeSupport.Require(
            checkout.OpenShiftAsync(session, 10000).GetAwaiter().GetResult().Ok,
            $"Cashier shift must open on host ({label}).");

        checkout.StartCart(session);
        RemotePosCartOperationResult add = checkout.AddLineAsync(session, product.Id, 2000).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(add.Ok, $"Add line must succeed ({label}).");

        string clientRequestId = "rpos-audit-" + Guid.NewGuid().ToString("N");
        RemotePosCheckoutSubmitRequest request = new(
            clientRequestId, new[] { new RemotePosCheckoutPaymentRequest("Cash", 5000) });

        // The sale commits BEFORE the (failing) best-effort audit. The response must be Completed,
        // never RetryableUnknown, and must not leak any audit-failure detail.
        RemotePosCheckoutResultResponse first = checkout.SubmitAsync(session, request).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(first.Status == RemotePosCheckoutStatus.Completed,
            $"Checkout must remain Completed despite the {label}, got {first.Status}: {first.Message}");
        RemotePosSmokeSupport.Require(!first.Duplicate, $"First submit must not be a duplicate ({label}).");
        RemotePosSmokeSupport.Require(!string.IsNullOrWhiteSpace(first.ReceiptNumber), $"Receipt must be allocated ({label}).");
        RemotePosSmokeSupport.Require(!string.IsNullOrWhiteSpace(first.SaleId), $"Sale id must be returned ({label}).");
        RequireNoAuditLeak(first.Message, label);

        string receiptNumber = first.ReceiptNumber!;
        Guid saleId = Guid.Parse(first.SaleId!);

        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountSalesByReceipt(projectRoot, receiptNumber) == 1,
            $"Exactly one sale row must exist after commit ({label}).");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountStockMovementsForSale(projectRoot, saleId) == 1,
            $"Exactly one stock movement must exist after commit ({label}).");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 2000,
            $"Stock must decrement exactly once 4000 -> 2000 ({label}).");

        // Retry with the SAME idempotency key replays the original committed result (no double sale).
        RemotePosCheckoutResultResponse retry = checkout.SubmitAsync(session, request).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(retry.Status == RemotePosCheckoutStatus.DuplicateRequest,
            $"Retry after commit must be DuplicateRequest, got {retry.Status} ({label}).");
        RemotePosSmokeSupport.Require(retry.Duplicate, $"Retry must be flagged duplicate ({label}).");
        RemotePosSmokeSupport.Require(retry.ReceiptNumber == receiptNumber && retry.SaleId == first.SaleId,
            $"Retry must replay the original committed result ({label}).");
        RequireNoAuditLeak(retry.Message, label);

        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountSalesByReceipt(projectRoot, receiptNumber) == 1,
            $"Retry must not create a second sale row ({label}).");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountStockMovementsForSale(projectRoot, saleId) == 1,
            $"Retry must not create a second stock movement ({label}).");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 2000,
            $"Retry must not decrement stock again ({label}).");

        RemotePosSmokeSupport.ForceCloseOpenShift(RemotePosSmokeSupport.CashierSessions(composition), cashier.Id);
    }

    private static void RequireNoAuditLeak(string? message, string label)
    {
        string safe = message ?? string.Empty;
        RemotePosSmokeSupport.Require(
            !safe.Contains("Simulated", StringComparison.OrdinalIgnoreCase),
            $"Checkout response must not leak audit-failure detail ({label}).");
        RemotePosSmokeSupport.Require(
            !safe.Contains("Exception", StringComparison.OrdinalIgnoreCase),
            $"Checkout response must not leak exception detail ({label}).");
    }

    // Builds a real host-side checkout runtime identical to the production composition EXCEPT the
    // audit sink, which is replaced with a deliberately failing double to exercise the fail-safe path.
    private static RemotePosCheckoutService BuildCheckoutServiceWithAudit(
        SalrynLocalComposition composition,
        IAuditWriteRepository auditWriteRepository)
    {
        SqliteRepositoryFactory repositories = new(composition.ConnectionFactory);

        CashierSessionService cashierSessionService = new(
            repositories.CreateCashierSessionWriteRepository(),
            repositories.CreateCashierSessionReadRepository());

        PosCheckoutService posCheckoutService = new(
            repositories.CreateSaleWriteRepository(),
            cashierSessionService);

        PosCheckoutApplicationService checkoutApplicationService = new(
            posCheckoutService,
            composition.ProductReadRepository);

        return new RemotePosCheckoutService(
            composition.ProductReadRepository,
            checkoutApplicationService,
            cashierSessionService,
            composition.SaleReadRepository,
            auditWriteRepository,
            new RemotePosReceiptNumberAllocator());
    }

    private sealed class ThrowingAuditWriteRepository : IAuditWriteRepository
    {
        public Task<Result> AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit sink failure after commit.");
    }

    private sealed class FailingResultAuditWriteRepository : IAuditWriteRepository
    {
        public Task<Result> AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Fail(Error.Failure("Audit.SimulatedFailure", "Simulated audit write failure after commit.")));
    }
}

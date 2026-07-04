using Salryn.Api.Local.Composition;
using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;

// LAN-M16 remote-pos-checkout-idempotency smoke: drives a REAL host-owned sale through the remote
// checkout runtime against the development database, then proves a duplicate submit (LAN drop +
// retry) returns the ORIGINAL result and never double-posts the sale or double-decrements stock.
internal static class RemotePosCheckoutIdempotencySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16 REMOTE POS CHECKOUT IDEMPOTENCY SMOKE CHECKS =====");

        RemotePosSmokeSupport.EnsureDatabaseReady(projectRoot);

        RemotePosApiServices services = RemotePosSmokeSupport.CreateServices(
            projectRoot, out SalrynLocalComposition composition, out RemotePosRuntimeGate runtimeGate);
        runtimeGate.Enable(Guid.NewGuid());

        RemotePosCheckoutService checkout = services.CheckoutService;
        ICashierSessionService cashierSessions = RemotePosSmokeSupport.CashierSessions(composition);

        UserReadModel cashier = RemotePosSmokeSupport.GetCashier(composition);
        RemotePosSmokeSupport.ForceCloseOpenShift(cashierSessions, cashier.Id);

        Product product = RemotePosSmokeSupport.SeedTrackedProduct(
            composition, "LAN-M16 Idempotency Product", stockX1000: 5000, sellingPriceCents: 1500);

        RemotePosSmokeSupport.Require(
            RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 5000,
            "Initial stock must be 5000 (5.000 units).");

        RemotePosOperatorSession session = RemotePosSmokeSupport.CreateSession(
            cashier.Id, "Cashier", SalrynPermissions.SalesCreate, SalrynPermissions.ShiftOpenClose);

        // Open shift + build a cart on the host.
        RemotePosShiftOperationResult shift = checkout.OpenShiftAsync(session, 10000).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(shift.Ok, "Cashier shift must open on host.");

        checkout.StartCart(session);
        RemotePosCartOperationResult add = checkout.AddLineAsync(session, product.Id, 2000).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(add.Ok && add.Cart is not null, "Add line must succeed.");
        RemotePosSmokeSupport.Require(add.Cart!.SubtotalCents == 3000, "Host-priced subtotal must be 3000 (2 * 1500).");

        string clientRequestId = "rpos-idem-" + Guid.NewGuid().ToString("N");
        RemotePosCheckoutSubmitRequest request = new(
            clientRequestId,
            new[] { new RemotePosCheckoutPaymentRequest("Cash", 5000) });

        // First submit -> completed on host.
        RemotePosCheckoutResultResponse first = checkout.SubmitAsync(session, request).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(first.Status == RemotePosCheckoutStatus.Completed,
            $"First submit must complete, got {first.Status}: {first.Message}");
        RemotePosSmokeSupport.Require(!first.Duplicate, "First submit must not be flagged duplicate.");
        RemotePosSmokeSupport.Require(!string.IsNullOrWhiteSpace(first.ReceiptNumber) && first.ReceiptNumber!.StartsWith("RPOS-", StringComparison.Ordinal),
            "Receipt number must be host-allocated with the RPOS- prefix.");
        RemotePosSmokeSupport.Require(first.TotalCents == 3000, "Sale total must be host-computed 3000.");
        RemotePosSmokeSupport.Require(first.ChangeCents == 2000, "Change must be 5000 - 3000 = 2000.");
        RemotePosSmokeSupport.Require(!string.IsNullOrWhiteSpace(first.SaleId), "Sale id must be returned.");

        string receiptNumber = first.ReceiptNumber!;
        Guid saleId = Guid.Parse(first.SaleId!);

        // Exactly one persisted sale + one stock movement; stock decremented once.
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountSalesByReceipt(projectRoot, receiptNumber) == 1,
            "Exactly one sale row must exist after first submit.");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountStockMovementsForSale(projectRoot, saleId) == 1,
            "Exactly one stock movement must exist after first submit.");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 3000,
            "Stock must decrement by 2000 exactly once (5000 -> 3000).");

        // Second submit with the SAME client request id -> idempotent replay of the original result.
        RemotePosCheckoutResultResponse second = checkout.SubmitAsync(session, request).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(second.Status == RemotePosCheckoutStatus.DuplicateRequest,
            $"Duplicate submit must return DuplicateRequest, got {second.Status}.");
        RemotePosSmokeSupport.Require(second.Duplicate, "Duplicate submit must be flagged duplicate.");
        RemotePosSmokeSupport.Require(second.ReceiptNumber == receiptNumber, "Duplicate submit must return the original receipt number.");
        RemotePosSmokeSupport.Require(second.SaleId == first.SaleId, "Duplicate submit must return the original sale id.");

        // No second sale row, no second stock movement, stock unchanged after duplicate.
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountSalesByReceipt(projectRoot, receiptNumber) == 1,
            "Duplicate submit must not create a second sale row.");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountStockMovementsForSale(projectRoot, saleId) == 1,
            "Duplicate submit must not create a second stock movement.");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 3000,
            "Duplicate submit must not decrement stock again.");

        // Status lookup by client request id returns the memoized result.
        RemotePosCheckoutResultResponse? found = checkout.FindCheckoutResult(session, clientRequestId);
        RemotePosSmokeSupport.Require(found is not null && found.ReceiptNumber == receiptNumber,
            "Checkout status lookup must return the original result for the client request id.");

        // A different idempotency key with an (already cleared) empty cart fails safely without writing.
        RemotePosCheckoutResultResponse emptyCart = checkout
            .SubmitAsync(session, new RemotePosCheckoutSubmitRequest("rpos-empty-" + Guid.NewGuid().ToString("N"),
                new[] { new RemotePosCheckoutPaymentRequest("Cash", 5000) }))
            .GetAwaiter()
            .GetResult();
        RemotePosSmokeSupport.Require(emptyCart.Status == RemotePosCheckoutStatus.ValidationFailed,
            "Submitting an empty cart under a new key must fail validation, not post a sale.");

        RemotePosSmokeSupport.ForceCloseOpenShift(cashierSessions, cashier.Id);
        runtimeGate.Disable();

        Console.WriteLine("LAN-M16 remote POS checkout idempotency smoke checks succeeded.");
        Console.WriteLine();
    }
}

using Salryn.Api.Local.Composition;
using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;

// LAN-M16 remote-pos-host-write-boundary smoke: proves the official write boundary is host-owned.
// The remote client sends intent only; the host allocates the document number, computes totals,
// decrements inventory, and records an audit trail. The client cannot supply a receipt/sequence
// or authoritative price (the submit contract has no such fields) and the host re-prices the sale.
internal static class RemotePosHostWriteBoundarySmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16 REMOTE POS HOST WRITE BOUNDARY SMOKE CHECKS =====");

        RemotePosSmokeSupport.EnsureDatabaseReady(projectRoot);

        RemotePosApiServices services = RemotePosSmokeSupport.CreateServices(
            projectRoot, out SalrynLocalComposition composition, out RemotePosRuntimeGate runtimeGate);
        runtimeGate.Enable(Guid.NewGuid());

        RemotePosCheckoutService checkout = services.CheckoutService;
        ICashierSessionService cashierSessions = RemotePosSmokeSupport.CashierSessions(composition);
        ISaleReadRepository saleReads = RemotePosSmokeSupport.RepositoryFactory(composition).CreateSaleReadRepository();

        UserReadModel cashier = RemotePosSmokeSupport.GetCashier(composition);
        RemotePosSmokeSupport.ForceCloseOpenShift(cashierSessions, cashier.Id);

        Product product = RemotePosSmokeSupport.SeedTrackedProduct(
            composition, "LAN-M16 Host Write Product", stockX1000: 4000, sellingPriceCents: 1250);

        RemotePosOperatorSession session = RemotePosSmokeSupport.CreateSession(
            cashier.Id, "Cashier", SalrynPermissions.SalesCreate, SalrynPermissions.ShiftOpenClose);

        // Checkout submit before any shift is open -> host fails closed (OpenShiftRequired), no write.
        checkout.StartCart(session);
        RemotePosSmokeSupport.Require(
            checkout.AddLineAsync(session, product.Id, 1000).GetAwaiter().GetResult().Ok,
            "Add line must succeed.");

        RemotePosCheckoutResultResponse noShift = checkout
            .SubmitAsync(session, new RemotePosCheckoutSubmitRequest("rpos-noshift-" + Guid.NewGuid().ToString("N"),
                new[] { new RemotePosCheckoutPaymentRequest("Cash", 2000) }))
            .GetAwaiter()
            .GetResult();
        RemotePosSmokeSupport.Require(noShift.Status == RemotePosCheckoutStatus.OpenShiftRequired,
            $"Checkout without an open host shift must be blocked, got {noShift.Status}.");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 4000,
            "Stock must be unchanged after a blocked checkout.");

        // Open shift, then complete a real sale on the host.
        RemotePosSmokeSupport.Require(checkout.OpenShiftAsync(session, 5000).GetAwaiter().GetResult().Ok, "Shift must open.");
        checkout.StartCart(session);
        RemotePosSmokeSupport.Require(checkout.AddLineAsync(session, product.Id, 1000).GetAwaiter().GetResult().Ok, "Add line must succeed.");

        RemotePosCheckoutResultResponse result = checkout
            .SubmitAsync(session, new RemotePosCheckoutSubmitRequest("rpos-hostwrite-" + Guid.NewGuid().ToString("N"),
                new[] { new RemotePosCheckoutPaymentRequest("Cash", 2000) }))
            .GetAwaiter()
            .GetResult();

        RemotePosSmokeSupport.Require(result.Status == RemotePosCheckoutStatus.Completed,
            $"Host sale must complete, got {result.Status}: {result.Message}");

        string receipt = result.ReceiptNumber!;
        Guid saleId = Guid.Parse(result.SaleId!);

        // Host owns the document number.
        RemotePosSmokeSupport.Require(receipt.StartsWith("RPOS-", StringComparison.Ordinal),
            "Document number must be host-allocated (RPOS- prefix).");

        // Sale persisted on host with host-computed totals; client sent no price/receipt/sequence.
        SaleDetailReadModel? sale = RemotePosSmokeSupport.RequireSuccess(
            saleReads.FindSaleByReceiptNoAsync(receipt).GetAwaiter().GetResult(),
            "RemotePos.FindSale");
        RemotePosSmokeSupport.Require(sale is not null, "Sale must be persisted on the host.");
        RemotePosSmokeSupport.Require(sale!.Total.Cents == 1250, "Host must compute the sale total (1 * 1250).");
        RemotePosSmokeSupport.Require(sale.Status == SaleStatus.Completed, "Persisted sale must be Completed.");

        // Inventory decrement is host-owned and recorded once.
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.GetProductStockX1000(composition, product.Id) == 3000,
            "Host must decrement stock by 1000 (4000 -> 3000).");
        RemotePosSmokeSupport.Require(RemotePosSmokeSupport.CountStockMovementsForSale(projectRoot, saleId) == 1,
            "Exactly one host stock movement must be recorded.");

        // Audit trail records the host-owned remote sale.
        RemotePosSmokeSupport.Require(HasRemotePosAudit(projectRoot, saleId),
            "A RemotePos.CheckoutSubmit audit row must be recorded on the host.");

        RemotePosSmokeSupport.ForceCloseOpenShift(cashierSessions, cashier.Id);
        runtimeGate.Disable();

        Console.WriteLine("LAN-M16 remote POS host write boundary smoke checks succeeded.");
        Console.WriteLine();
    }

    private static bool HasRemotePosAudit(string projectRoot, Guid saleId)
    {
        using Microsoft.Data.Sqlite.SqliteConnection connection = RemotePosSmokeSupport.OpenConnection(projectRoot);
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM audit_logs WHERE action = 'RemotePos.CheckoutSubmit' AND entity_id = $saleId;";
        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) >= 1;
    }
}

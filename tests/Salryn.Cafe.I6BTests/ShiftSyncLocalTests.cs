using Salryn.Core.Models;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Sync.Contracts;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class ShiftSyncLocalTests
{
    [Fact]
    public async Task OpenAndBlindClose_RetryRestartAndPayloadConflictPreserveOneDrawerAndOutbox()
    {
        using var fx=new CanonicalCashierFixture();await fx.LoginAsync();
        var opening=new MobileShiftOpenIntent(Guid.NewGuid(),10000,fx.Now);
        var opened=CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(opening));
        Assert.Equal(CafeShiftSyncContract.SessionIdFor(opening.OperationId),opened.Id);
        Assert.True((await fx.Controller.OpenShiftAsync(opening with{OpeningCashCents=11000})).IsFailure);
        var restarted=fx.NewController();await fx.LoginAsync(restarted);
        Assert.Equal(opened.Id,CanonicalCashierFixture.Value(await restarted.OpenShiftAsync(opening)).Id);
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM sync_outbox WHERE operation_type='CafeShiftOpenedV1';"));
        var closing=new MobileShiftCloseIntent(Guid.NewGuid(),opened.Id,10000,fx.Now.AddMinutes(1));
        var closed=CanonicalCashierFixture.Value(await restarted.CloseShiftAsync(closing));
        Assert.Equal(CashierSessionStatus.Closed,closed.Status);
        Assert.True((await restarted.CloseShiftAsync(closing with{CountedCashCents=9000})).IsFailure);
        Assert.Equal(closed.Id,CanonicalCashierFixture.Value(await restarted.CloseShiftAsync(closing)).Id);
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM cashier_sessions;"));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM cash_drawer_reconciliations;"));
        Assert.Equal(2L,fx.Scalar("SELECT COUNT(*) FROM sync_outbox;"));
        Assert.Equal(0L,fx.Scalar("SELECT COUNT(*) FROM cash_movements;"));
        Assert.Equal(0L,fx.Scalar("SELECT COUNT(*) FROM cash_drawer_events;"));
    }

    [Fact]
    public async Task CashSaleClose_UsesExactSourceCoverageAndCreatesNoCashMovementForCount()
    {
        using var fx=new CanonicalCashierFixture();await fx.LoginAsync();
        var opened=CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(new(Guid.NewGuid(),10000,fx.Now)));
        var order=await fx.ReadyOrderAsync();
        var checkout=CanonicalCashierFixture.Value(await fx.Controller.CreateCheckoutIntentAsync(Guid.NewGuid(),order.Order.OrderId,order.Order.OrderVersion,fx.Now,15000,0,null));
        CanonicalCashierFixture.Value(await fx.Controller.CompleteAsync(checkout));
        var close=new MobileShiftCloseIntent(Guid.NewGuid(),opened.Id,22500,fx.Now.AddMinutes(1));
        CanonicalCashierFixture.Value(await fx.Controller.CloseShiftAsync(close));
        CanonicalCashierFixture.Value(await fx.Controller.CloseShiftAsync(close));
        Assert.Equal(22500L,fx.Scalar("SELECT expected_cash_cents FROM cash_drawer_reconciliations;"));
        Assert.Equal(0L,fx.Scalar("SELECT variance_cents FROM cash_drawer_reconciliations;"));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM cash_movements;"));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM cash_drawer_events;"));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM sync_outbox WHERE operation_type='CafeShiftClosedV1';"));
    }

    [Fact]
    public async Task RevokedShiftPermission_BlocksCloseAndDoesNotEnqueue()
    {
        using var fx=new CanonicalCashierFixture();await fx.LoginAsync();
        var opened=CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(new(Guid.NewGuid(),10000,fx.Now)));
        fx.Execute("DELETE FROM role_permissions WHERE permission='Shift.OpenClose';");
        Assert.True((await fx.Controller.CloseShiftAsync(new(Guid.NewGuid(),opened.Id,10000,fx.Now.AddMinutes(1)))).IsFailure);
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM cashier_sessions WHERE status=1;"));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM sync_outbox;"));
    }

    [Fact]
    public async Task ConcurrentIndependentOpenIntents_KeepOneDrawerAndOneEnvelope()
    {
        using var fx=new CanonicalCashierFixture();
        var attempts=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>fx.Runtime.CafeShifts.Open(Guid.NewGuid(),fx.DeviceId,fx.StoreId,fx.ActorId,10000,fx.Now))));
        Assert.Single(attempts.Where(x=>x.IsSuccess));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM cashier_sessions WHERE status=1;"));
        Assert.Equal(1L,fx.Scalar("SELECT COUNT(*) FROM sync_outbox;"));
    }
}

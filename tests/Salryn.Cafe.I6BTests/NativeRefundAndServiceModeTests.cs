using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Mobile.Foundation.Cashier;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class NativeRefundAndServiceModeTests
{
    [Fact]
    public async Task SaveOrder_SupportsDineInToggleToTakeoutWithServiceReference_ThroughCompletion()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();

        var opened = await fx.Controller.OpenShiftAsync(new(Guid.NewGuid(), 100_000, fx.Now));
        Assert.True(opened.IsSuccess, opened.IsFailure ? opened.ErrorDetail.Message : null);

        // Save Dine-In with table reference, then toggle existing Open order to Takeout
        Guid orderId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();
        var dineIn = await fx.Controller.SaveOrderAsync(new(
            Guid.NewGuid(),
            orderId,
            0,
            fx.Now,
            CafeOrderServiceModes.DineIn,
            "Table 7",
            [new(lineId, fx.ProductId, null, [], 1000, "Extra hot", 0)]));
        Assert.True(dineIn.IsSuccess, dineIn.IsFailure ? dineIn.ErrorDetail.Message : null);

        var loaded = await fx.Controller.FindOrderAsync(orderId);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(CafeOrderServiceModes.DineIn, loaded.Value.Order.ServiceMode);
        Assert.Equal("Table 7", loaded.Value.Order.ServiceReference);

        var switched = await fx.Controller.SaveOrderAsync(new(
            Guid.NewGuid(),
            orderId,
            loaded.Value.Order.OrderVersion,
            fx.Now,
            CafeOrderServiceModes.Takeout,
            "Pickup #4",
            [new(lineId, fx.ProductId, null, [], 1000, "Extra hot", 0)]));
        Assert.True(switched.IsSuccess, switched.IsFailure ? switched.ErrorDetail.Message : null);

        loaded = await fx.Controller.FindOrderAsync(orderId);
        Assert.Equal(CafeOrderServiceModes.Takeout, loaded.Value.Order.ServiceMode);
        Assert.Equal("Pickup #4", loaded.Value.Order.ServiceReference);

        Assert.True((await fx.Controller.SubmitPreparationAsync(new(Guid.NewGuid(), orderId, loaded.Value.Order.OrderVersion, fx.Now))).IsSuccess);
        loaded = await fx.Controller.FindOrderAsync(orderId);
        Assert.True((await fx.Controller.SetPreparationStatusAsync(new(Guid.NewGuid(), orderId, loaded.Value.Order.OrderVersion, fx.Now), CafeOrderStates.Preparing)).IsSuccess);
        loaded = await fx.Controller.FindOrderAsync(orderId);
        Assert.True((await fx.Controller.SetPreparationStatusAsync(new(Guid.NewGuid(), orderId, loaded.Value.Order.OrderVersion, fx.Now), CafeOrderStates.Ready)).IsSuccess);
        loaded = await fx.Controller.FindOrderAsync(orderId);

        var intent = await fx.Controller.CreateCheckoutIntentAsync(Guid.NewGuid(), orderId, loaded.Value.Order.OrderVersion,
            fx.Now, 12_500, 0, null);
        Assert.True(intent.IsSuccess);
        var sale = await fx.Controller.CompleteAsync(intent.Value);
        Assert.True(sale.IsSuccess, sale.IsFailure ? sale.ErrorDetail.Message : null);
        Assert.Equal(CafeOrderStates.Completed, (await fx.Controller.FindOrderAsync(orderId)).Value.Order.State);
    }

    [Fact]
    public async Task RefundLifecycle_SupportsRestockServiceNoStockApproverOnlyAccessStoreScopeAndDurableIntents()
    {
        using CanonicalCashierFixture fx = new();
        // Add a stock-tracked cookie item so Restock is also exercised alongside ServiceNoStock
        Guid unitId = Guid.Parse(fx.Scalar("SELECT id FROM units LIMIT 1;").ToString()!);
        Guid cookieId = fx.Runtime.MenuCatalog.SaveItem(new MenuItemMutation(
            null, null, "Butter Cookie", null, unitId, "COOKIE-1", null, 8_500, 3_000, true, false)).Value.ProductId;
        fx.Execute($"UPDATE products SET track_stock=1, stock_on_hand_x1000=10000 WHERE id='{cookieId:D}';");
        fx.Runtime.MenuCatalog.PublishCurrent();

        await fx.LoginAsync();
        var shift = await fx.Controller.OpenShiftAsync(new(Guid.NewGuid(), 100_000, fx.Now));
        Assert.True(shift.IsSuccess);

        // Complete an order with 1x espresso (trackStock: false) + 1x cookie (trackStock: true)
        Guid orderId = Guid.NewGuid();
        Assert.True((await fx.Controller.SaveOrderAsync(new(
            Guid.NewGuid(), orderId, 0, fx.Now, CafeOrderServiceModes.Takeout, null,
            [
                new(Guid.NewGuid(), fx.ProductId, null, [], 1000, null, 0),
                new(Guid.NewGuid(), cookieId, null, [], 1000, null, 1),
            ]))).IsSuccess);
        var loaded = (await fx.Controller.FindOrderAsync(orderId)).Value;
        Assert.True((await fx.Controller.SubmitPreparationAsync(new(Guid.NewGuid(), orderId, loaded.Order.OrderVersion, fx.Now))).IsSuccess);
        loaded = (await fx.Controller.FindOrderAsync(orderId)).Value;
        Assert.True((await fx.Controller.SetPreparationStatusAsync(new(Guid.NewGuid(), orderId, loaded.Order.OrderVersion, fx.Now), CafeOrderStates.Preparing)).IsSuccess);
        loaded = (await fx.Controller.FindOrderAsync(orderId)).Value;
        Assert.True((await fx.Controller.SetPreparationStatusAsync(new(Guid.NewGuid(), orderId, loaded.Order.OrderVersion, fx.Now), CafeOrderStates.Ready)).IsSuccess);
        loaded = (await fx.Controller.FindOrderAsync(orderId)).Value;

        var checkout = await fx.Controller.CreateCheckoutIntentAsync(Guid.NewGuid(), orderId, loaded.Order.OrderVersion,
            fx.Now, 21_000, 0, null);
        Assert.True(checkout.IsSuccess);
        var sale = await fx.Controller.CompleteAsync(checkout.Value);
        Assert.True(sale.IsSuccess);
        Assert.Equal(9_000L, Convert.ToInt64(fx.Scalar($"SELECT stock_on_hand_x1000 FROM products WHERE id='{cookieId:D}';")));

        var refundableSales = await fx.Controller.ListRefundableSalesAsync();
        Assert.True(refundableSales.IsSuccess);
        MobileRefundableSaleSummary saleRow = Assert.Single(refundableSales.Value, s => s.SaleId == sale.Value.SaleId);
        MobileRefundableSaleItem espressoLine = Assert.Single(saleRow.Items, i => i.ProductName == "Synthetic espresso");
        MobileRefundableSaleItem cookieLine = Assert.Single(saleRow.Items, i => i.ProductName == "Butter Cookie");
        Assert.False(espressoLine.TrackStock);
        Assert.True(cookieLine.TrackStock);

        // Durable MobileCashierIntentStore retains exact OperationId across RefundRequest failure/reload
        MobileCashierIntentStore intentStore = new(fx.Secrets);
        Guid reqOpId = Guid.NewGuid();
        Guid refundId = Guid.NewGuid();
        var reqIntent = new MobileRefundRequestIntent(reqOpId, refundId, saleRow.SaleId, 0, fx.Now, "Spilled & returned cookie",
        [
            new(Guid.NewGuid(), espressoLine.SaleItemId, 1000, CafeRefundStockDispositions.ServiceNoStock, null),
            new(Guid.NewGuid(), cookieLine.SaleItemId, 1000, CafeRefundStockDispositions.Restock, null),
        ]);
        await intentStore.SaveBeforeDispatchAsync(new("RefundRequest", RefundRequest: reqIntent, ActorUserId: fx.ActorId));
        var reloadedIntent = await intentStore.ReadAsync();
        Assert.NotNull(reloadedIntent);
        Assert.Equal(reqOpId, reloadedIntent!.OperationId);

        var requested = await fx.Controller.RequestRefundAsync(reloadedIntent.RefundRequest!);
        Assert.True(requested.IsSuccess, requested.IsFailure ? requested.ErrorDetail.Message : null);
        await intentStore.ClearConfirmedAsync(reloadedIntent.OperationId);

        // Remove Sales.RefundRequest while retaining Sales.RefundApprove -> approver-only still views refunds & refundable sales!
        fx.Execute($"DELETE FROM role_permissions WHERE permission='{SalrynPermissions.SalesRefundRequest}';");
        var approverSales = await fx.Controller.ListRefundableSalesAsync();
        Assert.True(approverSales.IsSuccess);
        var approverList = await fx.Controller.ListRefundsAsync();
        Assert.True(approverList.IsSuccess);
        MobileRefundSummary requestedSummary = Assert.Single(approverList.Value, r => r.RefundId == refundId);
        Assert.Equal("Requested", requestedSummary.State);
        Assert.Equal(21_000L, requestedSummary.TotalRefundCents);

        // Remove Sales.RefundApprove -> BOTH ResolveRefundAsync AND CommitRefundAsync must deny fail-closed
        fx.Execute($"DELETE FROM role_permissions WHERE permission='{SalrynPermissions.SalesRefundApprove}';");
        Assert.True((await fx.Controller.ResolveRefundAsync(new(Guid.NewGuid(), refundId, saleRow.SaleId, 1, fx.Now, true, "Denied"))).IsFailure);
        Assert.True((await fx.Controller.CommitRefundAsync(new(Guid.NewGuid(), refundId, saleRow.SaleId, 1, shift.Value.Id, fx.Now,
            [new(Guid.NewGuid(), CafeRefundSettlementMethods.Cash, 21_000)]))).IsFailure);

        // Restore Sales.RefundApprove -> Approve & durably Commit Cash reversal + Restock
        Guid roleId = Guid.Parse(fx.Scalar("SELECT role_id FROM users LIMIT 1;").ToString()!);
        fx.Execute($"INSERT INTO role_permissions(role_id, permission, created_at) VALUES('{roleId:D}', '{SalrynPermissions.SalesRefundApprove}', '{fx.Now:O}');");

        var approved = await fx.Controller.ResolveRefundAsync(new(Guid.NewGuid(), refundId, saleRow.SaleId, 1, fx.Now, true, "Manager approved"));
        Assert.True(approved.IsSuccess);

        Guid commitOpId = Guid.NewGuid();
        var commitIntent = new MobileRefundCommitIntent(commitOpId, refundId, saleRow.SaleId, approved.Value.Version, shift.Value.Id, fx.Now,
            [new(Guid.NewGuid(), CafeRefundSettlementMethods.Cash, 21_000)]);
        await intentStore.SaveBeforeDispatchAsync(new("RefundCommit", RefundCommit: commitIntent, ActorUserId: fx.ActorId));
        var committed = await fx.Controller.CommitRefundAsync((await intentStore.ReadAsync())!.RefundCommit!);
        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.ErrorDetail.Message : null);
        await intentStore.ClearConfirmedAsync(commitOpId);
        Assert.Equal(21_000L, committed.Value.TotalRefundCents);

        // Cookie stock restocked back to 10.000
        Assert.Equal(10_000L, Convert.ToInt64(fx.Scalar($"SELECT stock_on_hand_x1000 FROM products WHERE id='{cookieId:D}';")));

        // Fully refunded sale line is excluded from ListRefundableSalesAsync
        Assert.Empty((await fx.Controller.ListRefundableSalesAsync()).Value);
    }
}

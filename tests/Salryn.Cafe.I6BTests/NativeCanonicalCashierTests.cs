using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Mobile.Foundation.Cashier;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class NativeCanonicalCashierTests
{
    [Fact]
    public async Task OperatorLogin_IsSeparateFromDeviceApproval_AndMissingApprovalCannotWrite()
    {
        using CanonicalCashierFixture fx = new();
        await fx.Secrets.WriteAsync(ProtectedMobileEnrollmentReader.SecretKey, []);
        await fx.LoginAsync();
        Assert.NotNull(fx.Controller.CurrentOperator);
        var denied = await fx.Controller.OpenShiftAsync(0);
        Assert.True(denied.IsFailure);
        Assert.Equal("Mobile.DeviceApprovalRequired", denied.ErrorDetail.Code);
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM cashier_sessions;")));
    }

    [Fact]
    public async Task PublishedMenu_ReadyOrder_CashSaleAndRetry_UseCanonicalLedgerAndOutbox()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        var menu = CanonicalCashierFixture.Value(await fx.Controller.MenuAsync());
        Assert.Single(menu.Items);
        var shift = CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(10000));
        Assert.Equal(shift.Id, CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(10000)).Id);
        var order = await fx.ReadyOrderAsync();
        var intent = CanonicalCashierFixture.Value(await fx.Controller.CreateCheckoutIntentAsync(Guid.NewGuid(),
            order.Order.OrderId, order.Order.OrderVersion, fx.Now, 15000, 0, null));
        var committed = CanonicalCashierFixture.Value(await fx.Controller.CompleteAsync(intent));
        // New controller has no retained operator principal. Authenticate again before replay.
        var restarted = fx.NewController();
        await fx.LoginAsync(restarted);
        var replay = CanonicalCashierFixture.Value(await restarted.CompleteAsync(intent));
        Assert.Equal(committed.SaleId, replay.SaleId);
        Assert.True(replay.Idempotent);
        Assert.Equal(1L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM sales;")));
        Assert.Equal(12500L, Convert.ToInt64(fx.Scalar("SELECT total_cents FROM sales LIMIT 1;")));
        Assert.Equal(15000L, Convert.ToInt64(fx.Scalar("SELECT amount_cents FROM payments LIMIT 1;")));
        Assert.True(Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM sync_outbox;")) >= 6);
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'mobile_%' OR name='p0_business';")));
    }

    [Fact]
    public async Task ExpiredLease_BlocksBeforeCanonicalMutation()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        fx.Now = fx.Now.AddHours(2);
        var expired = await fx.Controller.OpenShiftAsync(1000);
        Assert.True(expired.IsFailure);
        Assert.Equal("LEASE_EXPIRED", expired.ErrorDetail.Code);
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM cashier_sessions;")));
    }

    [Fact]
    public async Task CurrentPermissionRevocation_BlocksEvenAfterSuccessfulLogin()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        fx.Execute("DELETE FROM role_permissions WHERE permission='Shift.OpenClose';");
        var denied = await fx.Controller.OpenShiftAsync(10000);
        Assert.True(denied.IsFailure);
        Assert.Equal("Mobile.PermissionDenied", denied.ErrorDetail.Code);
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM cashier_sessions;")));
    }

    [Fact]
    public async Task OperatorDeactivation_RevokesControllerPrincipalAndCannotOpenDrawer()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        fx.Execute("UPDATE users SET status=0;");
        var denied = await fx.Controller.OpenShiftAsync(10000);
        Assert.True(denied.IsFailure);
        Assert.Equal("Mobile.OperatorRevoked", denied.ErrorDetail.Code);
        Assert.Null(fx.Controller.CurrentOperator);
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM cashier_sessions;")));
    }

    [Fact]
    public async Task WrongOperatorAndCreditPermission_DenyBeforePosting()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(0));
        var order = await fx.ReadyOrderAsync();
        var intent = CanonicalCashierFixture.Value(await fx.Controller.CreateCheckoutIntentAsync(Guid.NewGuid(),
            order.Order.OrderId, order.Order.OrderVersion, fx.Now, 12500, 0, null));
        var foreign = await fx.Controller.CompleteAsync(intent with { ActorUserId = Guid.NewGuid() });
        Assert.True(foreign.IsFailure);
        Assert.Equal("Mobile.CheckoutOwnerMismatch", foreign.ErrorDetail.Code);
        fx.Execute("DELETE FROM role_permissions WHERE permission='Receivables.CreateCreditSale';");
        var credit = await fx.Controller.CompleteAsync(intent with { CashCents = 0, AccountsReceivableCents = 12500 });
        Assert.True(credit.IsFailure);
        Assert.Equal("Mobile.CreditSalePermissionRequired", credit.ErrorDetail.Code);
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM sales;")));
    }

    [Fact]
    public async Task SavedCheckoutIntent_SurvivesRestartAndCannotBeOverwrittenOrClearedByAnotherOperation()
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        var intent = new MobilePendingCashierIntent("Checkout", Checkout: new(Guid.NewGuid(), Guid.NewGuid(), 4,
            fx.Now, fx.ActorId, fx.DeviceId, fx.StoreId, 12500, 0, null), ActorUserId: fx.ActorId);
        MobileCashierIntentStore first = new(fx.Secrets);
        await first.SaveBeforeDispatchAsync(intent);
        MobileCashierIntentStore restarted = new(fx.Secrets);
        Assert.Equal(intent, await restarted.ReadAsync());
        await restarted.SaveBeforeDispatchAsync(intent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SaveBeforeDispatchAsync(
            intent with { Checkout = intent.Checkout! with { CashCents = 20000 } }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ClearConfirmedAsync(Guid.NewGuid()));
        Assert.Equal(intent, await restarted.ReadAsync());
        await restarted.ClearConfirmedAsync(intent.OperationId);
        Assert.Null(await restarted.ReadAsync());
    }

    [Fact]
    public async Task VariantAndRequiredModifier_PricesComeFromCanonicalPublishedMenu()
    {
        using CanonicalCashierFixture fx = new();
        var variant = CanonicalCashierFixture.Value(fx.Runtime.MenuCatalog.SaveVariant(
            new(null, fx.ProductId, "Large", null, null, 15000, 5000, true, false, 0)));
        var group = CanonicalCashierFixture.Value(fx.Runtime.MenuCatalog.SaveModifierGroup(
            new(null, "Milk", true, 1, 1, true, 0)));
        var modifier = CanonicalCashierFixture.Value(fx.Runtime.MenuCatalog.SaveModifierOption(
            new(null, group.Id, "Oat milk", 2500, true, 0)));
        Assert.True(fx.Runtime.MenuCatalog.SetVariantModifierGroups(new(variant.Id, [group.Id])).IsSuccess);
        CanonicalCashierFixture.Value(fx.Runtime.MenuCatalog.PublishCurrent());
        await fx.LoginAsync();
        var draft = new MobileOrderDraft(Guid.NewGuid(), Guid.NewGuid(), 0, fx.Now, CafeOrderServiceModes.Takeout, null,
            [new(Guid.NewGuid(), fx.ProductId, variant.Id, [modifier.Id], 2000, "Synthetic note", 0)]);
        var saved = CanonicalCashierFixture.Value(await fx.Controller.SaveOrderAsync(draft));
        var actual = CanonicalCashierFixture.Value(await fx.Controller.FindOrderAsync(saved.OrderId));
        Assert.Equal(17500, actual.CurrentRevision.Lines[0].CalculatedUnitPriceCents);
        Assert.Equal(35000, actual.CurrentRevision.Lines[0].LineTotalCents);
        Assert.Equal("Oat milk", actual.CurrentRevision.Lines[0].ModifierSelections[0].OptionNameSnapshot);
    }

    [Theory]
    [InlineData(0, 12500)]
    [InlineData(5000, 7500)]
    public async Task CashAndAr_AuthorityPostsOneCanonicalReceivable(long cash, long ar)
    {
        using CanonicalCashierFixture fx = new();
        await fx.LoginAsync();
        CanonicalCashierFixture.Value(await fx.Controller.OpenShiftAsync(0));
        var customer = CanonicalCashierFixture.Value(await fx.Runtime.AccountsReceivable.CreateCustomerAsync(
            new CreateReceivableCustomerRequest("Synthetic customer", null, null, null, null, fx.ActorId)));
        var order = await fx.ReadyOrderAsync();
        var intent = CanonicalCashierFixture.Value(await fx.Controller.CreateCheckoutIntentAsync(Guid.NewGuid(),
            order.Order.OrderId, order.Order.OrderVersion, fx.Now, cash, ar, customer.Id));
        CanonicalCashierFixture.Value(await fx.Controller.CompleteAsync(intent));
        Assert.Equal(1L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM sales;")));
        Assert.Equal(ar, Convert.ToInt64(fx.Scalar("SELECT SUM(debit_cents-credit_cents) FROM accounts_receivable_entries;")));
        Assert.Equal(0L, Convert.ToInt64(fx.Scalar("SELECT COUNT(*) FROM payments WHERE method NOT IN (1,2);")));
    }
}


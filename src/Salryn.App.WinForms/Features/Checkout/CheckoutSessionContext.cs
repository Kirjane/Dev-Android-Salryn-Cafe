using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed record CheckoutSessionContext(
    Guid CashierUserId,
    string CashierDisplayName,
    string CashierRoleName,
    Guid CashierSessionId,
    DateTimeOffset ShiftOpenedAt,
    string OpeningCashText)
{
    public string ShortCashierSessionId => CashierSessionId.ToString("N")[..8];

    public string CashierLine => $"{CashierDisplayName} · {CashierRoleName}";

    public string ShiftLine =>
        $"Shift {ShortCashierSessionId} · opened {ShiftOpenedAt:yyyy-MM-dd HH:mm} UTC · opening cash {OpeningCashText}";

    public string ModeLine =>
        $"Shift {ShortCashierSessionId} bound · Sale completion locked";

    // Kirch: Creates the checkout context from the authenticated app session and open cashier shift.
    public static CheckoutSessionContext From(
        CurrentAppSession appSession,
        CashierSessionReadModel cashierSession)
    {
        ArgumentNullException.ThrowIfNull(appSession);
        ArgumentNullException.ThrowIfNull(cashierSession);

        return new CheckoutSessionContext(
            appSession.UserId,
            appSession.DisplayName,
            appSession.RoleName,
            cashierSession.Id,
            cashierSession.OpenedAt,
            cashierSession.OpeningCash.ToString());
    }
}

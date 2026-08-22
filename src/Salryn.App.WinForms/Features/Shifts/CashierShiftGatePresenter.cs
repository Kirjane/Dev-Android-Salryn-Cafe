using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Shifts;

// Kirch file guide: presenter for cashier shift gate logic.
// UI location: runs before POS Checkout opens.
// Frontend note: visual form can change, but open-shift validation must stay here.

public sealed record CashierShiftGateViewModel(
    string UserLine,
    string StatusLine,
    string DetailLine,
    bool CanOpenShift,
    bool CanEnterCheckout,
    bool CanCloseShift,
    CashierSessionReadModel? OpenSession,
    CashierSessionReadModel? ClosedSession);

public sealed class CashierShiftGatePresenter
{
    private readonly CurrentAppSession _session;
    private readonly ICashierSessionService _cashierSessions;

    public CashierShiftGatePresenter(
        CurrentAppSession session,
        ICashierSessionService cashierSessions)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cashierSessions = cashierSessions ?? throw new ArgumentNullException(nameof(cashierSessions));
    }

    // Kirch: Checks whether the current user has an open cashier shift before entering checkout.
    public async Task<Result<CashierShiftGateViewModel>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        Result permissionResult = ValidatePermissions();

        if (permissionResult.IsFailure)
        {
            return Result<CashierShiftGateViewModel>.Success(
                BuildLockedState(permissionResult.Error));
        }

        Result<CashierSessionReadModel?> openSessionResult =
            await _cashierSessions.FindOpenSessionByUserIdAsync(
                _session.UserId,
                cancellationToken);

        if (openSessionResult.IsFailure)
        {
            return Result<CashierShiftGateViewModel>.Fail(openSessionResult.ErrorDetail);
        }

        CashierSessionReadModel? openSession = openSessionResult.Value;

        if (openSession is null)
        {
            return Result<CashierShiftGateViewModel>.Success(new CashierShiftGateViewModel(
                BuildUserLine(),
                "No open cashier shift.",
                "Open a shift before entering the POS checkout workspace.",
                CanOpenShift: true,
                CanEnterCheckout: false,
                CanCloseShift: false,
                OpenSession: null,
                ClosedSession: null));
        }

        return Result<CashierShiftGateViewModel>.Success(BuildOpenState(openSession));
    }

    // Kirch: Opens a cashier shift for the current user using the supplied opening cash amount.
    public async Task<Result<CashierShiftGateViewModel>> OpenShiftAsync(
        decimal openingCash,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        Result permissionResult = ValidatePermissions();

        if (permissionResult.IsFailure)
        {
            return Result<CashierShiftGateViewModel>.Fail(permissionResult.ErrorDetail);
        }

        if (openingCash < 0)
        {
            return Result<CashierShiftGateViewModel>.Fail(Error.Validation(
                "CashierShiftGate.InvalidOpeningCash",
                "Opening cash cannot be negative."));
        }

        Result<CashierSessionReadModel> openResult =
            await _cashierSessions.OpenAsync(
                new OpenCashierSessionRequest(
                    _session.UserId,
                    Money.FromPesos(openingCash),
                    string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()),
                cancellationToken);

        if (openResult.IsFailure)
        {
            return Result<CashierShiftGateViewModel>.Fail(openResult.ErrorDetail);
        }

        return Result<CashierShiftGateViewModel>.Success(
            BuildOpenState(openResult.Value));
    }

    // Kirch: Closes the current cashier shift through the cashier session service.
    public async Task<Result<CashierShiftGateViewModel>> CloseShiftAsync(
        Guid sessionId,
        decimal countedCash,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        Result permissionResult = ValidatePermissions();

        if (permissionResult.IsFailure)
        {
            return Result<CashierShiftGateViewModel>.Fail(permissionResult.ErrorDetail);
        }

        if (sessionId == Guid.Empty)
        {
            return Result<CashierShiftGateViewModel>.Fail(Error.Validation(
                "CashierShiftGate.InvalidSessionId",
                "Open cashier shift session ID is required before closing."));
        }

        if (countedCash < 0)
        {
            return Result<CashierShiftGateViewModel>.Fail(Error.Validation(
                "CashierShiftGate.InvalidCountedCash",
                "Counted cash cannot be negative."));
        }

        Result<CashierSessionReadModel> closeResult =
            await _cashierSessions.CloseAsync(
                new CloseCashierSessionRequest(
                    sessionId,
                    _session.UserId,
                    Money.FromPesos(countedCash),
                    string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()),
                cancellationToken);

        if (closeResult.IsFailure)
        {
            return Result<CashierShiftGateViewModel>.Fail(closeResult.ErrorDetail);
        }

        return Result<CashierShiftGateViewModel>.Success(
            BuildClosedState(closeResult.Value));
    }

    private Result ValidatePermissions()
    {
        if (!_session.IsActive)
        {
            return Result.Fail(Error.Forbidden(
                "CashierShiftGate.UserInactive",
                "Current user is not active."));
        }

        if (!_session.Can(SalrynPermissions.SalesCreate))
        {
            return Result.Fail(Error.Forbidden(
                "CashierShiftGate.SalesCreateRequired",
                "Sales.Create permission is required to enter POS checkout."));
        }

        if (!_session.Can(SalrynPermissions.ShiftOpenClose))
        {
            return Result.Fail(Error.Forbidden(
                "CashierShiftGate.ShiftPermissionRequired",
                "Shift.OpenClose permission is required to manage cashier shifts."));
        }

        return Result.Success();
    }

    private CashierShiftGateViewModel BuildOpenState(
        CashierSessionReadModel openSession)
    {
        return new CashierShiftGateViewModel(
            BuildUserLine(),
            $"Open shift: {openSession.OpenedAt:yyyy-MM-dd HH:mm} UTC",
            $"Opening cash: {openSession.OpeningCash} · Expected cash: {openSession.ExpectedCash}",
            CanOpenShift: false,
            CanEnterCheckout: true,
            CanCloseShift: true,
            OpenSession: openSession,
            ClosedSession: null);
    }

    private CashierShiftGateViewModel BuildClosedState(
        CashierSessionReadModel closedSession)
    {
        return new CashierShiftGateViewModel(
            BuildUserLine(),
            $"Closed shift: {closedSession.ClosedAt:yyyy-MM-dd HH:mm} UTC",
            $"Opening: {closedSession.OpeningCash} · Expected: {closedSession.ExpectedCash} · Counted: {closedSession.CountedCash?.ToString() ?? "--"} · Variance: {closedSession.Variance}",
            CanOpenShift: false,
            CanEnterCheckout: false,
            CanCloseShift: false,
            OpenSession: null,
            ClosedSession: closedSession);
    }

    private CashierShiftGateViewModel BuildLockedState(
        string reason)
    {
        return new CashierShiftGateViewModel(
            BuildUserLine(),
            "POS checkout is locked.",
            reason,
            CanOpenShift: false,
            CanEnterCheckout: false,
            CanCloseShift: false,
            OpenSession: null,
            ClosedSession: null);
    }

    private string BuildUserLine()
    {
        return $"{_session.DisplayName} · {_session.RoleName}";
    }
}

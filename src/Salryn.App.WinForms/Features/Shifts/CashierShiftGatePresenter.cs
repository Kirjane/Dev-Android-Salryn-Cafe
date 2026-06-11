using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.App.WinForms.Features.Shifts;

public sealed record CashierShiftGateViewModel(
    string UserLine,
    string StatusLine,
    string DetailLine,
    bool CanOpenShift,
    bool CanEnterCheckout,
    CashierSessionReadModel? OpenSession);

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
                OpenSession: null));
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
            $"Opening cash: {openSession.OpeningCash}",
            CanOpenShift: false,
            CanEnterCheckout: true,
            OpenSession: openSession);
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
            OpenSession: null);
    }

    private string BuildUserLine()
    {
        return $"{_session.DisplayName} · {_session.RoleName}";
    }
}

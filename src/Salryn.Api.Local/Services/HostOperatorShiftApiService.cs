using Salryn.Api.Local.Contracts;
using Salryn.Core.Common;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

public sealed record HostOperatorShiftOperationResult(
    bool Ok,
    string Code,
    string Message,
    HostOperatorShiftResponse? Shift);

public sealed class HostOperatorShiftApiService
{
    private readonly ICashierSessionService _cashierSessionService;

    public HostOperatorShiftApiService(ICashierSessionService cashierSessionService)
    {
        _cashierSessionService = cashierSessionService
            ?? throw new ArgumentNullException(nameof(cashierSessionService));
    }

    public async Task<HostOperatorShiftOperationResult> OpenShiftAsync(
        Guid userId,
        long openingCashCents,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new HostOperatorShiftOperationResult(
                false,
                "HostOperator.Shift.InvalidUser",
                "Operator user id is required.",
                null);
        }

        if (openingCashCents < 0)
        {
            return new HostOperatorShiftOperationResult(
                false,
                "HostOperator.Shift.InvalidCash",
                "Opening cash cannot be negative.",
                null);
        }

        Result<CashierSessionReadModel?> openResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        if (openResult.IsSuccess && openResult.Value is not null)
        {
            return new HostOperatorShiftOperationResult(
                true,
                "HostOperator.Shift.AlreadyOpen",
                "A cashier shift is already open.",
                BuildShiftResponse(openResult.Value));
        }

        Result<CashierSessionReadModel> opened = await _cashierSessionService
            .OpenAsync(
                new OpenCashierSessionRequest(
                    userId,
                    Money.FromCents(openingCashCents),
                    "Host Console shift"),
                cancellationToken)
            .ConfigureAwait(false);

        if (opened.IsFailure)
        {
            return new HostOperatorShiftOperationResult(
                false,
                opened.ErrorDetail.Code,
                opened.ErrorDetail.Message,
                null);
        }

        return new HostOperatorShiftOperationResult(
            true,
            "HostOperator.Shift.Opened",
            "Cashier shift opened.",
            BuildShiftResponse(opened.Value));
    }

    public async Task<HostOperatorShiftOperationResult> ReadShiftAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new HostOperatorShiftOperationResult(
                false,
                "HostOperator.Shift.InvalidUser",
                "Operator user id is required.",
                null);
        }

        Result<CashierSessionReadModel?> openResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        if (openResult.IsFailure)
        {
            return new HostOperatorShiftOperationResult(
                false,
                openResult.ErrorDetail.Code,
                openResult.ErrorDetail.Message,
                null);
        }

        if (openResult.Value is null)
        {
            return new HostOperatorShiftOperationResult(
                true,
                "HostOperator.Shift.None",
                "No open cashier shift.",
                new HostOperatorShiftResponse(
                    "None",
                    null,
                    null,
                    null,
                    "No open cashier shift for this operator."));
        }

        return new HostOperatorShiftOperationResult(
            true,
            "HostOperator.Shift.Open",
            "Open cashier shift.",
            BuildShiftResponse(openResult.Value));
    }

    public async Task<HostOperatorShiftOperationResult> CloseShiftAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new HostOperatorShiftOperationResult(
                false,
                "HostOperator.Shift.InvalidUser",
                "Operator user id is required.",
                null);
        }

        Result<CashierSessionReadModel?> openResult = await _cashierSessionService
            .FindOpenSessionByUserIdAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        if (openResult.IsFailure)
        {
            return new HostOperatorShiftOperationResult(
                false,
                openResult.ErrorDetail.Code,
                openResult.ErrorDetail.Message,
                null);
        }

        if (openResult.Value is null)
        {
            return new HostOperatorShiftOperationResult(
                false,
                "HostOperator.Shift.NoneToClose",
                "No open cashier shift to close.",
                null);
        }

        CashierSessionReadModel open = openResult.Value;

        Result<CashierSessionReadModel> closed = await _cashierSessionService
            .CloseAsync(
                new CloseCashierSessionRequest(
                    open.Id,
                    userId,
                    open.ExpectedCash,
                    "Host Console shift close"),
                cancellationToken)
            .ConfigureAwait(false);

        if (closed.IsFailure)
        {
            return new HostOperatorShiftOperationResult(
                false,
                closed.ErrorDetail.Code,
                closed.ErrorDetail.Message,
                null);
        }

        return new HostOperatorShiftOperationResult(
            true,
            "HostOperator.Shift.Closed",
            "Cashier shift closed.",
            new HostOperatorShiftResponse(
                "Closed",
                open.Id.ToString(),
                null,
                null,
                "Cashier shift closed."));
    }

    private static HostOperatorShiftResponse BuildShiftResponse(CashierSessionReadModel session) =>
        new(
            "Open",
            session.Id.ToString(),
            session.OpenedAt.ToString("O"),
            session.OpeningCash.Cents,
            "Open cashier shift.");
}
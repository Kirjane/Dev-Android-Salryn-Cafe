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
    private readonly CashDrawerReconciliationService _drawer;

    public HostOperatorShiftApiService(ICashierSessionService cashierSessionService,CashDrawerReconciliationService drawer)
    {
        _cashierSessionService = cashierSessionService
            ?? throw new ArgumentNullException(nameof(cashierSessionService));
        _drawer=drawer;
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

    public Task<HostOperatorShiftOperationResult> CloseShiftAsync(
        Guid userId,
        HostOperatorShiftCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Task.FromResult(new HostOperatorShiftOperationResult(
                false,
                "HostOperator.Shift.InvalidUser",
                "Operator user id is required.",
                null));
        }

        // Delegate directly to CashDrawerReconciliationService.Close so idempotent
        // post-commit retries by close_request_id return 200 even after status becomes Closed.
        _ = cancellationToken;
        Result<CashDrawerSnapshot> closed = _drawer.Close(
            userId,
            new(request.CountedCashCents, request.PhysicalCountConfirmed, request.ClientRequestId, request.Note));

        if (closed.IsFailure)
        {
            string code = string.Equals(closed.ErrorDetail.Code, "CashDrawer.NoOpenShift", StringComparison.Ordinal)
                ? "HostOperator.Shift.NoneToClose"
                : closed.ErrorDetail.Code;
            return Task.FromResult(new HostOperatorShiftOperationResult(
                false,
                code,
                closed.ErrorDetail.Message,
                null));
        }

        return Task.FromResult(new HostOperatorShiftOperationResult(
            true,
            "HostOperator.Shift.Closed",
            "Cashier shift closed.",
            new HostOperatorShiftResponse(
                "Closed",
                closed.Value.SessionId,
                null,
                null,
                "Cashier shift closed.",
                closed.Value.ExpectedCashCents,
                closed.Value.CountedCashCents,
                closed.Value.VarianceCents,
                closed.Value.CoverageStatus,
                closed.Value.ReviewStatus)));
    }

    private static HostOperatorShiftResponse BuildShiftResponse(CashierSessionReadModel session) =>
        new(
            "Open",
            session.Id.ToString(),
            session.OpenedAt.ToString("O"),
            session.OpeningCash.Cents,
            "Open cashier shift.");
}

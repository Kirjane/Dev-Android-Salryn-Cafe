using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.Services;

public sealed record OpenCashierSessionRequest(
    Guid UserId,
    Money OpeningCash,
    string? Notes);

public sealed record CloseCashierSessionRequest(
    Guid SessionId,
    Guid UserId,
    Money CountedCash,
    string? Notes);

public sealed record ForceCloseCashierSessionRequest(
    Guid SessionId,
    Guid UserId,
    Money? CountedCash,
    string Reason);

public interface ICashierSessionService
{
    Task<Result<CashierSessionReadModel?>> FindOpenSessionByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<CashierSessionReadModel?>> FindByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<Result<CashierSessionReadModel>> OpenAsync(
        OpenCashierSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<CashierSessionReadModel>> CloseAsync(
        CloseCashierSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<CashierSessionReadModel>> ForceCloseAsync(
        ForceCloseCashierSessionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CashierSessionService : ICashierSessionService
{
    private readonly ICashierSessionWriteRepository _writeRepository;
    private readonly ICashierSessionReadRepository _readRepository;

    public CashierSessionService(
        ICashierSessionWriteRepository writeRepository,
        ICashierSessionReadRepository readRepository)
    {
        _writeRepository = writeRepository ?? throw new ArgumentNullException(nameof(writeRepository));
        _readRepository = readRepository ?? throw new ArgumentNullException(nameof(readRepository));
    }

    public Task<Result<CashierSessionReadModel?>> FindOpenSessionByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Task.FromResult(Result<CashierSessionReadModel?>.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required.")));
        }

        return _readRepository.FindOpenSessionByUserIdAsync(userId, cancellationToken);
    }

    public Task<Result<CashierSessionReadModel?>> FindByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Task.FromResult(Result<CashierSessionReadModel?>.Fail(Error.Validation(
                "CashierSessions.InvalidSessionId",
                "Cashier session ID is required.")));
        }

        return _readRepository.FindByIdAsync(sessionId, cancellationToken);
    }

    public async Task<Result<CashierSessionReadModel>> OpenAsync(
        OpenCashierSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateOpenRequest(request);

        if (validation.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(validation.ErrorDetail);
        }

        Money openingCash = request.OpeningCash;

        CashierSession session = new()
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            OpenedAt = DateTimeOffset.UtcNow,
            ClosedAt = null,
            OpeningCash = openingCash,
            ExpectedCash = openingCash,
            CountedCash = null,
            Variance = Money.Zero,
            Status = CashierSessionStatus.Open,
            Notes = request.Notes
        };

        Result openResult = await _writeRepository.OpenAsync(session, cancellationToken);

        if (openResult.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(openResult.ErrorDetail);
        }

        return await ReadRequiredAfterWriteAsync(
            session.Id,
            "CashierSessionService.OpenReadBackMissing",
            "Cashier session was opened but could not be read back.",
            cancellationToken);
    }

    public async Task<Result<CashierSessionReadModel>> CloseAsync(
        CloseCashierSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateCloseRequest(request);

        if (validation.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(validation.ErrorDetail);
        }

        Result closeResult = await _writeRepository.CloseAsync(
            request.SessionId,
            request.UserId,
            request.CountedCash,
            request.Notes,
            cancellationToken);

        if (closeResult.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(closeResult.ErrorDetail);
        }

        return await ReadRequiredAfterWriteAsync(
            request.SessionId,
            "CashierSessionService.CloseReadBackMissing",
            "Cashier session was closed but could not be read back.",
            cancellationToken);
    }

    public async Task<Result<CashierSessionReadModel>> ForceCloseAsync(
        ForceCloseCashierSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateForceCloseRequest(request);

        if (validation.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(validation.ErrorDetail);
        }

        Result closeResult = await _writeRepository.ForceCloseAsync(
            request.SessionId,
            request.UserId,
            request.CountedCash,
            request.Reason,
            cancellationToken);

        if (closeResult.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(closeResult.ErrorDetail);
        }

        return await ReadRequiredAfterWriteAsync(
            request.SessionId,
            "CashierSessionService.ForceCloseReadBackMissing",
            "Cashier session was force closed but could not be read back.",
            cancellationToken);
    }

    private async Task<Result<CashierSessionReadModel>> ReadRequiredAfterWriteAsync(
        Guid sessionId,
        string missingCode,
        string missingMessage,
        CancellationToken cancellationToken)
    {
        Result<CashierSessionReadModel?> readResult =
            await _readRepository.FindByIdAsync(sessionId, cancellationToken);

        if (readResult.IsFailure)
        {
            return Result<CashierSessionReadModel>.Fail(readResult.ErrorDetail);
        }

        if (readResult.Value is null)
        {
            return Result<CashierSessionReadModel>.Fail(Error.Unexpected(
                missingCode,
                missingMessage));
        }

        return Result<CashierSessionReadModel>.Success(readResult.Value);
    }

    private static Result ValidateOpenRequest(OpenCashierSessionRequest request)
    {
        if (request.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required."));
        }

        if (request.OpeningCash.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidMoneyAmount",
                "Opening cash cannot be negative."));
        }

        return Result.Success();
    }

    private static Result ValidateCloseRequest(CloseCashierSessionRequest request)
    {
        if (request.SessionId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidSessionId",
                "Cashier session ID is required."));
        }

        if (request.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required."));
        }

        if (request.CountedCash.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidMoneyAmount",
                "Counted cash cannot be negative."));
        }

        return Result.Success();
    }

    private static Result ValidateForceCloseRequest(ForceCloseCashierSessionRequest request)
    {
        if (request.SessionId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidSessionId",
                "Cashier session ID is required."));
        }

        if (request.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required."));
        }

        if (request.CountedCash is Money countedCash && countedCash.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidMoneyAmount",
                "Counted cash cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.ForceCloseReasonRequired",
                "A reason is required when force closing a cashier session."));
        }

        return Result.Success();
    }
}

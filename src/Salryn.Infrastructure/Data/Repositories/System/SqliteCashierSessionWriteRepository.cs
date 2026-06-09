using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteCashierSessionWriteRepository :
    SqliteRepositoryBase,
    ICashierSessionWriteRepository
{
    public SqliteCashierSessionWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> OpenAsync(
        CashierSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateOpenSession(session);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            if (HasOpenSession(connection, transaction, session.UserId))
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.Conflict(
                    "CashierSessions.OpenSessionExists",
                    "User already has an open cashier session.")));
            }

            InsertSession(connection, transaction, session);

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "CashierSessions.DuplicateSessionId",
                "Cashier session ID already exists.")));
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "CashierSessions.InvalidReference",
                "Cashier session contains an invalid user reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("CashierSessions.OpenFailed", ex));
        }
    }

    public Task<Result> CloseAsync(
        Guid sessionId,
        Guid userId,
        Money countedCash,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateCloseRequest(sessionId, userId, countedCash);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            int affectedRows = CloseSession(
                connection,
                transaction,
                sessionId,
                userId,
                countedCash,
                notes,
                CashierSessionStatus.Closed);

            if (affectedRows == 0)
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.NotFound(
                    "CashierSessions.NotFoundOrNotOpen",
                    "Cashier session was not found or is not open.")));
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "CashierSessions.InvalidReference",
                "Cashier session close contains an invalid user reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("CashierSessions.CloseFailed", ex));
        }
    }

    public Task<Result> ForceCloseAsync(
        Guid sessionId,
        Guid userId,
        Money? countedCash,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateForceCloseRequest(sessionId, userId, countedCash, reason);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            int affectedRows = ForceCloseSession(
                connection,
                transaction,
                sessionId,
                userId,
                countedCash,
                reason);

            if (affectedRows == 0)
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.NotFound(
                    "CashierSessions.NotFoundOrNotOpen",
                    "Cashier session was not found or is not open.")));
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "CashierSessions.InvalidReference",
                "Cashier session force close contains an invalid user reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected("CashierSessions.ForceCloseFailed", ex));
        }
    }

    private static bool HasOpenSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cashier_sessions
            WHERE user_id = $userId
              AND status = $openStatus;
            """;

        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$openStatus", Convert.ToInt32(CashierSessionStatus.Open));

        return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
    }

    private static void InsertSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CashierSession session)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cashier_sessions (
                id,
                user_id,
                opened_at,
                closed_at,
                opening_cash_cents,
                expected_cash_cents,
                counted_cash_cents,
                variance_cents,
                status,
                notes
            )
            VALUES (
                $id,
                $userId,
                $openedAt,
                $closedAt,
                $openingCashCents,
                $expectedCashCents,
                $countedCashCents,
                $varianceCents,
                $status,
                $notes
            );
            """;

        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$userId", session.UserId.ToString("D"));
        command.Parameters.AddWithValue("$openedAt", session.OpenedAt.ToString("O"));
        command.Parameters.AddWithValue("$closedAt", DBNull.Value);
        command.Parameters.AddWithValue("$openingCashCents", session.OpeningCash.Cents);
        command.Parameters.AddWithValue("$expectedCashCents", session.ExpectedCash.Cents);
        command.Parameters.AddWithValue("$countedCashCents", DBNull.Value);
        command.Parameters.AddWithValue("$varianceCents", session.Variance.Cents);
        command.Parameters.AddWithValue("$status", Convert.ToInt32(CashierSessionStatus.Open));
        command.Parameters.AddWithValue("$notes", DbNullableText(session.Notes));

        command.ExecuteNonQuery();
    }

    private static int CloseSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        Guid userId,
        Money countedCash,
        string? notes,
        CashierSessionStatus status)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cashier_sessions
            SET
                closed_at = $closedAt,
                counted_cash_cents = $countedCashCents,
                variance_cents = $countedCashCents - expected_cash_cents,
                status = $status,
                notes = $notes
            WHERE id = $id
              AND user_id = $userId
              AND status = $openStatus;
            """;

        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$closedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$countedCashCents", countedCash.Cents);
        command.Parameters.AddWithValue("$status", Convert.ToInt32(status));
        command.Parameters.AddWithValue("$notes", DbNullableText(notes));
        command.Parameters.AddWithValue("$openStatus", Convert.ToInt32(CashierSessionStatus.Open));

        return command.ExecuteNonQuery();
    }

    private static int ForceCloseSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        Guid userId,
        Money? countedCash,
        string reason)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cashier_sessions
            SET
                closed_at = $closedAt,
                counted_cash_cents = $countedCashCents,
                variance_cents = CASE
                    WHEN $countedCashCents IS NULL THEN variance_cents
                    ELSE $countedCashCents - expected_cash_cents
                END,
                status = $status,
                notes = $notes
            WHERE id = $id
              AND user_id = $userId
              AND status = $openStatus;
            """;

        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$closedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$countedCashCents", DbNullableMoney(countedCash));
        command.Parameters.AddWithValue("$status", Convert.ToInt32(CashierSessionStatus.ForceClosed));
        command.Parameters.AddWithValue("$notes", reason.Trim());
        command.Parameters.AddWithValue("$openStatus", Convert.ToInt32(CashierSessionStatus.Open));

        return command.ExecuteNonQuery();
    }

    private static Result ValidateOpenSession(CashierSession session)
    {
        if (session.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidSessionId",
                "Cashier session ID is required."));
        }

        if (session.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required."));
        }

        if (session.Status != CashierSessionStatus.Open)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.OpenStatusRequired",
                "Opening a cashier session requires Open status."));
        }

        if (session.ClosedAt.HasValue || session.CountedCash.HasValue)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.OpenSessionCannotHaveCloseData",
                "Opening a cashier session cannot include close data."));
        }

        if (session.OpeningCash.Cents < 0 ||
            session.ExpectedCash.Cents < 0 ||
            session.Variance.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidMoneyAmount",
                "Cashier session money amounts cannot be negative when opening."));
        }

        if (session.ExpectedCash.Cents != session.OpeningCash.Cents)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.ExpectedCashMustMatchOpeningCash",
                "Expected cash must match opening cash when opening a cashier session."));
        }

        if (session.Variance.Cents != 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.OpenVarianceMustBeZero",
                "Variance must be zero when opening a cashier session."));
        }

        return Result.Success();
    }

    private static Result ValidateCloseRequest(
        Guid sessionId,
        Guid userId,
        Money countedCash)
    {
        if (sessionId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidSessionId",
                "Cashier session ID is required."));
        }

        if (userId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required."));
        }

        if (countedCash.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidMoneyAmount",
                "Counted cash cannot be negative."));
        }

        return Result.Success();
    }

    private static Result ValidateForceCloseRequest(
        Guid sessionId,
        Guid userId,
        Money? countedCash,
        string reason)
    {
        if (sessionId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidSessionId",
                "Cashier session ID is required."));
        }

        if (userId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidUserId",
                "Cashier session user ID is required."));
        }

        if (countedCash is Money money && money.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.InvalidMoneyAmount",
                "Counted cash cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail(Error.Validation(
                "CashierSessions.ForceCloseReasonRequired",
                "A reason is required when force closing a cashier session."));
        }

        return Result.Success();
    }

    private static object DbNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static object DbNullableMoney(Money? value)
    {
        return value is Money money
            ? money.Cents
            : DBNull.Value;
    }

    private static bool IsUniqueConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode is 1555 or 2067 ||
            exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForeignKeyConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode == 787 ||
            exception.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}

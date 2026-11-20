using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Repositories.Sales;

/// <summary>
/// Low-level validation/write support for explicit cash-ledger operations.
/// Sale receipts and AR cash collections are source-owned by migration-26 triggers,
/// so this helper does not provide alternate posting paths for those source events.
/// </summary>
internal static class SqliteCashMovementPersistence
{
    internal static Result<Guid> RequireOpenCashierSessionForUser(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId,
        string errorCode,
        string errorMessage)
    {
        if (userId == Guid.Empty)
        {
            return Result<Guid>.Fail(Error.Validation(errorCode, errorMessage));
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM cashier_sessions
            WHERE user_id = $userId
              AND status = $openStatus
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$openStatus", Convert.ToInt32(CashierSessionStatus.Open));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Result<Guid>.Fail(Error.Conflict(errorCode, errorMessage));
        }

        Guid sessionId = Guid.Parse(reader.GetString(0));
        if (reader.Read())
        {
            return Result<Guid>.Fail(Error.Conflict(
                "CashMovements.AmbiguousOpenCashierSession",
                "Cash drawer assignment is ambiguous because more than one open cashier session exists for the user."));
        }

        return Result<Guid>.Success(sessionId);
    }

    internal static bool IsCashMethod(string? method) =>
        string.Equals(
            NormalizeCollectionMethod(method),
            "Cash",
            StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeCollectionMethod(string? method) =>
        string.IsNullOrWhiteSpace(method) ? "Cash" : method.Trim();

    internal static Result Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CashMovement movement)
    {
        Result validation = Validate(movement);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (!IsSessionOpen(connection, transaction, movement.CashierSessionId))
        {
            return Result.Fail(Error.Conflict(
                "CashMovements.OpenCashierSessionRequired",
                "A new live cash movement requires an open cashier session."));
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cash_movements (
                id,
                cashier_session_id,
                actor_user_id,
                movement_type,
                delta_cents,
                reference_type,
                reference_id,
                reversal_of_movement_id,
                reason,
                created_at_utc
            )
            VALUES (
                $id,
                $cashierSessionId,
                $actorUserId,
                $movementType,
                $deltaCents,
                $referenceType,
                $referenceId,
                $reversalOfMovementId,
                $reason,
                $createdAtUtc
            );
            """;

        command.Parameters.AddWithValue("$id", movement.Id.ToString("D"));
        command.Parameters.AddWithValue("$cashierSessionId", movement.CashierSessionId.ToString("D"));
        command.Parameters.AddWithValue("$actorUserId", movement.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$movementType", Convert.ToInt32(movement.MovementType));
        command.Parameters.AddWithValue("$deltaCents", movement.Delta.Cents);
        command.Parameters.AddWithValue("$referenceType", movement.ReferenceType.Trim());
        command.Parameters.AddWithValue("$referenceId", movement.ReferenceId.ToString("D"));
        command.Parameters.AddWithValue(
            "$reversalOfMovementId",
            movement.ReversalOfMovementId.HasValue
                ? movement.ReversalOfMovementId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$reason",
            string.IsNullOrWhiteSpace(movement.Reason)
                ? DBNull.Value
                : movement.Reason.Trim());
        command.Parameters.AddWithValue("$createdAtUtc", movement.CreatedAt.ToString("O"));

        command.ExecuteNonQuery();
        return Result.Success();
    }

    private static bool IsSessionOpen(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM cashier_sessions
            WHERE id = $sessionId
              AND status = $openStatus
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$openStatus", Convert.ToInt32(CashierSessionStatus.Open));
        return command.ExecuteScalar() is not null;
    }

    private static Result Validate(CashMovement movement)
    {
        if (movement.Id == Guid.Empty)
            return Result.Fail(Error.Validation("CashMovements.InvalidMovementId", "Cash movement ID is required."));
        if (movement.CashierSessionId == Guid.Empty)
            return Result.Fail(Error.Validation("CashMovements.InvalidCashierSessionId", "Cash movement cashier session ID is required."));
        if (movement.ActorUserId == Guid.Empty)
            return Result.Fail(Error.Validation("CashMovements.InvalidActorUserId", "Cash movement actor user ID is required."));
        if (!Enum.IsDefined(typeof(CashMovementType), movement.MovementType))
            return Result.Fail(Error.Validation("CashMovements.InvalidMovementType", "Cash movement type is invalid."));
        if (movement.Delta.Cents == 0)
            return Result.Fail(Error.Validation("CashMovements.ZeroDeltaNotAllowed", "Cash movement delta cannot be zero."));
        if (movement.MovementType is CashMovementType.SaleCashReceipt or CashMovementType.ReceivableCashCollection &&
            movement.Delta.Cents <= 0)
            return Result.Fail(Error.Validation("CashMovements.PositiveReceiptRequired", "Cash receipt movements must have a positive delta."));
        if (movement.MovementType == CashMovementType.CashIn && movement.Delta.Cents <= 0)
            return Result.Fail(Error.Validation("CashMovements.PositiveCashInRequired", "Cash-in movements must have a positive delta."));
        if (movement.MovementType is CashMovementType.InventoryPurchase or CashMovementType.OperationalExpense or CashMovementType.CashRefund or CashMovementType.CashOut &&
            movement.Delta.Cents >= 0)
            return Result.Fail(Error.Validation("CashMovements.NegativeCashOutRequired", "Cash-out movements must have a negative delta."));
        if (string.IsNullOrWhiteSpace(movement.ReferenceType))
            return Result.Fail(Error.Validation("CashMovements.ReferenceTypeRequired", "Cash movement reference type is required."));
        if (movement.ReferenceId == Guid.Empty)
            return Result.Fail(Error.Validation("CashMovements.ReferenceIdRequired", "Cash movement reference ID is required."));
        if (movement.MovementType == CashMovementType.Reversal && !movement.ReversalOfMovementId.HasValue)
            return Result.Fail(Error.Validation("CashMovements.ReversalSourceRequired", "Cash reversal must reference the original cash movement."));
        if (movement.MovementType != CashMovementType.Reversal && movement.ReversalOfMovementId.HasValue)
            return Result.Fail(Error.Validation("CashMovements.UnexpectedReversalSource", "Only reversal cash movements may reference an original cash movement."));

        return Result.Success();
    }
}

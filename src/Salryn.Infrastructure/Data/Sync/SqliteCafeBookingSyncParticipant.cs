using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class SqliteCafeBookingSyncParticipant
{
    public static bool IsSupportedSourceOperation(string? operationType) =>
        CafeBookingOperationContract.IsSupported(operationType) ||
        string.Equals(
            operationType,
            BookingFulfillmentOrderBoundV1Contract.OperationType,
            StringComparison.Ordinal);

    public static string AppliedChangeTypeForSource(string operationType) =>
        operationType switch
        {
            CafeBookingOperationContract.BookingRevisionCommittedV1 =>
                AppliedBusinessChangeContract.BookingRevisionCommittedV1AppliedV1,
            CafeBookingOperationContract.BookingStatusAdvancedV1 =>
                AppliedBusinessChangeContract.BookingStatusAdvancedV1AppliedV1,
            CafeBookingOperationContract.BookingCancelledV1 =>
                AppliedBusinessChangeContract.BookingCancelledV1AppliedV1,
            CafeBookingOperationContract.BookingDepositReceivedV1 =>
                AppliedBusinessChangeContract.BookingDepositReceivedV1AppliedV1,
            CafeBookingOperationContract.BookingDepositRefundedV1 =>
                AppliedBusinessChangeContract.BookingDepositRefundedV1AppliedV1,
            BookingFulfillmentOrderBoundV1Contract.OperationType =>
                AppliedBusinessChangeContract.BookingFulfillmentOrderBoundV1AppliedV1,
            _ => throw new InvalidOperationException(
                "Unsupported C9 Booking source operation.")
        };

    public static string? SourceOperationTypeForApplied(string changeType) =>
        changeType switch
        {
            AppliedBusinessChangeContract.BookingRevisionCommittedV1AppliedV1 =>
                CafeBookingOperationContract.BookingRevisionCommittedV1,
            AppliedBusinessChangeContract.BookingStatusAdvancedV1AppliedV1 =>
                CafeBookingOperationContract.BookingStatusAdvancedV1,
            AppliedBusinessChangeContract.BookingCancelledV1AppliedV1 =>
                CafeBookingOperationContract.BookingCancelledV1,
            AppliedBusinessChangeContract.BookingDepositReceivedV1AppliedV1 =>
                CafeBookingOperationContract.BookingDepositReceivedV1,
            AppliedBusinessChangeContract.BookingDepositRefundedV1AppliedV1 =>
                CafeBookingOperationContract.BookingDepositRefundedV1,
            AppliedBusinessChangeContract.BookingFulfillmentOrderBoundV1AppliedV1 =>
                BookingFulfillmentOrderBoundV1Contract.OperationType,
            _ => null
        };

    public static Result ApplyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(envelope);

        if (!IsSupportedSourceOperation(envelope.OperationType) ||
            envelope.SchemaVersion != CafeBookingOperationContract.SchemaVersion)
        {
            return Result.Fail(
                Error.Validation(
                    "CafeBookingSync.ContractUnsupported",
                    "C9 Booking sync requires one of the six schema-version-1 pre-sale contracts."));
        }

        if (!StoreMatches(connection, transaction, envelope.StoreId))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeBookingSync.StoreAuthorityMismatch",
                    "C9 Booking StoreId does not match deployment authority."));
        }

        if (!ActorExists(connection, transaction, envelope.ActorUserId))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeBookingSync.ActorPrerequisiteMissing",
                    "C9 Booking actor prerequisite is missing."));
        }

        try
        {
            if (string.Equals(
                    envelope.OperationType,
                    CafeBookingOperationContract.BookingRevisionCommittedV1,
                    StringComparison.Ordinal))
            {
                CafeBookingCanonicalParseResult<BookingRevisionCommittedV1> parsed =
                    CafeBookingCanonicalJson.ParseRevision(envelope.PayloadJson);
                if (!parsed.IsValid || parsed.Payload is null)
                    return InvalidPayload(parsed.ErrorCode, parsed.ErrorMessage);
                Result envelopeCheck = ValidateEnvelope(
                    envelope,
                    parsed.Payload.OperationId,
                    parsed.Payload.BookingId,
                    parsed.Payload.StoreId,
                    parsed.Payload.ActorUserId,
                    parsed.Payload.ExpectedVersion);
                if (envelopeCheck.IsFailure) return envelopeCheck;
                if (BookingOperationExists(connection, transaction, envelope.OperationId))
                    return VerifyCanonical(connection, transaction, envelope)
                        ? Result.Success()
                        : Conflict("CafeBookingSync.OperationCollision",
                            "C9 Booking OperationId already owns different canonical evidence.");
                return ApplyRevision(connection, transaction, envelope, parsed.Payload);
            }

            if (string.Equals(
                    envelope.OperationType,
                    CafeBookingOperationContract.BookingStatusAdvancedV1,
                    StringComparison.Ordinal))
            {
                CafeBookingCanonicalParseResult<BookingStatusAdvancedV1> parsed =
                    CafeBookingCanonicalJson.ParseStatus(envelope.PayloadJson);
                if (!parsed.IsValid || parsed.Payload is null)
                    return InvalidPayload(parsed.ErrorCode, parsed.ErrorMessage);
                Result envelopeCheck = ValidateEnvelope(
                    envelope,
                    parsed.Payload.OperationId,
                    parsed.Payload.BookingId,
                    parsed.Payload.StoreId,
                    parsed.Payload.ActorUserId,
                    parsed.Payload.ExpectedVersion);
                if (envelopeCheck.IsFailure) return envelopeCheck;
                if (BookingOperationExists(connection, transaction, envelope.OperationId))
                    return VerifyCanonical(connection, transaction, envelope)
                        ? Result.Success()
                        : Conflict("CafeBookingSync.OperationCollision",
                            "C9 Booking OperationId already owns different canonical evidence.");
                return ApplyStatus(connection, transaction, envelope, parsed.Payload);
            }

            if (string.Equals(
                    envelope.OperationType,
                    CafeBookingOperationContract.BookingCancelledV1,
                    StringComparison.Ordinal))
            {
                CafeBookingCanonicalParseResult<BookingCancelledV1> parsed =
                    CafeBookingCanonicalJson.ParseCancellation(envelope.PayloadJson);
                if (!parsed.IsValid || parsed.Payload is null)
                    return InvalidPayload(parsed.ErrorCode, parsed.ErrorMessage);
                Result envelopeCheck = ValidateEnvelope(
                    envelope,
                    parsed.Payload.OperationId,
                    parsed.Payload.BookingId,
                    parsed.Payload.StoreId,
                    parsed.Payload.ActorUserId,
                    parsed.Payload.ExpectedVersion);
                if (envelopeCheck.IsFailure) return envelopeCheck;
                if (BookingOperationExists(connection, transaction, envelope.OperationId))
                    return VerifyCanonical(connection, transaction, envelope)
                        ? Result.Success()
                        : Conflict("CafeBookingSync.OperationCollision",
                            "C9 Booking OperationId already owns different canonical evidence.");
                return ApplyCancellation(connection, transaction, envelope, parsed.Payload);
            }

            if (string.Equals(
                    envelope.OperationType,
                    CafeBookingOperationContract.BookingDepositReceivedV1,
                    StringComparison.Ordinal))
            {
                CafeBookingCanonicalParseResult<BookingDepositReceivedV1> parsed =
                    CafeBookingCanonicalJson.ParseDepositReceive(envelope.PayloadJson);
                if (!parsed.IsValid || parsed.Payload is null)
                    return InvalidPayload(parsed.ErrorCode, parsed.ErrorMessage);
                Result envelopeCheck = ValidateEnvelopeWithoutCanonicalVersion(
                    envelope,
                    parsed.Payload.OperationId,
                    parsed.Payload.BookingId,
                    parsed.Payload.StoreId,
                    parsed.Payload.ActorUserId);
                if (envelopeCheck.IsFailure) return envelopeCheck;
                if (BookingOperationExists(connection, transaction, envelope.OperationId))
                    return VerifyCanonical(connection, transaction, envelope)
                        ? Result.Success()
                        : Conflict("CafeBookingSync.OperationCollision",
                            "C9 Booking OperationId already owns different canonical evidence.");
                return ApplyDepositReceive(connection, transaction, envelope, parsed.Payload);
            }

            if (string.Equals(
                    envelope.OperationType,
                    CafeBookingOperationContract.BookingDepositRefundedV1,
                    StringComparison.Ordinal))
            {
                CafeBookingCanonicalParseResult<BookingDepositRefundedV1> parsed =
                    CafeBookingCanonicalJson.ParseDepositRefund(envelope.PayloadJson);
                if (!parsed.IsValid || parsed.Payload is null)
                    return InvalidPayload(parsed.ErrorCode, parsed.ErrorMessage);
                Result envelopeCheck = ValidateEnvelopeWithoutCanonicalVersion(
                    envelope,
                    parsed.Payload.OperationId,
                    parsed.Payload.BookingId,
                    parsed.Payload.StoreId,
                    parsed.Payload.ActorUserId);
                if (envelopeCheck.IsFailure) return envelopeCheck;
                if (BookingOperationExists(connection, transaction, envelope.OperationId))
                    return VerifyCanonical(connection, transaction, envelope)
                        ? Result.Success()
                        : Conflict("CafeBookingSync.OperationCollision",
                            "C9 Booking OperationId already owns different canonical evidence.");
                return ApplyDepositRefund(connection, transaction, envelope, parsed.Payload);
            }

            BookingFulfillmentOrderBoundParseResult orderBound =
                BookingFulfillmentOrderBoundV1CanonicalJson.Parse(envelope.PayloadJson);
            if (!orderBound.IsValid || orderBound.Payload is null)
                return InvalidPayload(orderBound.ErrorCode, orderBound.ErrorMessage);

            Result bindEnvelope = ValidateEnvelope(
                envelope,
                orderBound.Payload.OperationId,
                orderBound.Payload.BookingId,
                orderBound.Payload.StoreId,
                orderBound.Payload.ActorUserId,
                orderBound.Payload.ExpectedBookingVersion);
            if (bindEnvelope.IsFailure) return bindEnvelope;
            if (BookingOperationExists(connection, transaction, envelope.OperationId))
                return VerifyCanonical(connection, transaction, envelope)
                    ? Result.Success()
                    : Conflict("CafeBookingSync.OperationCollision",
                        "C9 Booking OperationId already owns different canonical evidence.");

            return ApplyOrderBound(
                connection,
                transaction,
                envelope,
                orderBound.Payload);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Conflict(
                "CafeBookingSync.ConstraintConflict",
                ex.Message);
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or
            ArgumentException or FormatException or OverflowException)
        {
            return Result.Fail(
                Error.Unexpected(
                    "CafeBookingSync.ApplyFailed",
                    ex.Message));
        }
    }

    public static bool VerifyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        if (!IsSupportedSourceOperation(envelope.OperationType) ||
            envelope.SchemaVersion != CafeBookingOperationContract.SchemaVersion)
            return false;

        string payloadHash = HashForEnvelope(envelope);
        OperationRow? operation = ReadOperation(
            connection,
            transaction,
            envelope.OperationId);
        if (operation is null ||
            operation.DeviceId != envelope.DeviceId ||
            operation.StoreId != envelope.StoreId ||
            !string.Equals(operation.OperationType, envelope.OperationType, StringComparison.Ordinal) ||
            !string.Equals(operation.PayloadHash, payloadHash, StringComparison.Ordinal))
            return false;

        if (string.Equals(
                envelope.OperationType,
                CafeBookingOperationContract.BookingRevisionCommittedV1,
                StringComparison.Ordinal))
        {
            var parsed = CafeBookingCanonicalJson.ParseRevision(envelope.PayloadJson);
            return parsed.IsValid && parsed.Payload is not null &&
                VerifyRevision(connection, transaction, operation, parsed.Payload);
        }

        if (string.Equals(
                envelope.OperationType,
                CafeBookingOperationContract.BookingStatusAdvancedV1,
                StringComparison.Ordinal))
        {
            var parsed = CafeBookingCanonicalJson.ParseStatus(envelope.PayloadJson);
            return parsed.IsValid && parsed.Payload is not null &&
                VerifyStatus(connection, transaction, operation, parsed.Payload);
        }

        if (string.Equals(
                envelope.OperationType,
                CafeBookingOperationContract.BookingCancelledV1,
                StringComparison.Ordinal))
        {
            var parsed = CafeBookingCanonicalJson.ParseCancellation(envelope.PayloadJson);
            return parsed.IsValid && parsed.Payload is not null &&
                VerifyCancellation(connection, transaction, operation, parsed.Payload);
        }

        if (string.Equals(
                envelope.OperationType,
                CafeBookingOperationContract.BookingDepositReceivedV1,
                StringComparison.Ordinal))
        {
            var parsed = CafeBookingCanonicalJson.ParseDepositReceive(envelope.PayloadJson);
            return parsed.IsValid && parsed.Payload is not null &&
                VerifyDepositReceive(connection, transaction, operation, parsed.Payload);
        }

        if (string.Equals(
                envelope.OperationType,
                CafeBookingOperationContract.BookingDepositRefundedV1,
                StringComparison.Ordinal))
        {
            var parsed = CafeBookingCanonicalJson.ParseDepositRefund(envelope.PayloadJson);
            return parsed.IsValid && parsed.Payload is not null &&
                VerifyDepositRefund(connection, transaction, operation, parsed.Payload);
        }

        BookingFulfillmentOrderBoundParseResult orderBound =
            BookingFulfillmentOrderBoundV1CanonicalJson.Parse(envelope.PayloadJson);
        return orderBound.IsValid && orderBound.Payload is not null &&
            VerifyOrderBound(connection, transaction, operation, orderBound.Payload);
    }

    private static Result ApplyRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        BookingRevisionCommittedV1 payload)
    {
        Result prerequisites = ValidateRevisionPrerequisites(
            connection,
            transaction,
            payload.Revision,
            payload.StoreId);
        if (prerequisites.IsFailure) return prerequisites;

        if (payload.Revision.BookingVersion != payload.ResultingVersion)
            return Conflict(
                "CafeBookingSync.RevisionVersionMismatch",
                "Canonical Booking revision version differs from operation resulting version.");

        string payloadHash = HashForEnvelope(envelope);
        if (string.Equals(
                payload.ConfigurationAction,
                CafeBookingOperationContract.Created,
                StringComparison.Ordinal))
        {
            if (BookingExists(connection, transaction, payload.BookingId))
                return Conflict(
                    "CafeBookingSync.BookingIdConflict",
                    "Canonical create requires an unused BookingId.");
            if (payload.Revision.RevisionNumber != 1 ||
                payload.Revision.PreviousRevisionId.HasValue)
                return Conflict(
                    "CafeBookingSync.CreateRevisionSequenceConflict",
                    "Canonical create must own initial Booking revision 1.");

            InsertBooking(
                connection,
                transaction,
                payload.BookingId,
                payload.StoreId,
                payload.ResultingVersion,
                payload.Revision.RevisionId,
                payload.ToStatus,
                payload.ToFulfillmentState,
                payload.ActorUserId,
                payload.OccurredAtUtc);
            InsertRevision(connection, transaction, payload.Revision);
        }
        else
        {
            BookingRow? booking = ReadBooking(
                connection,
                transaction,
                payload.BookingId);
            if (booking is null ||
                booking.StoreId != payload.StoreId ||
                booking.Version != payload.ExpectedVersion ||
                booking.CurrentRevisionId != payload.Revision.PreviousRevisionId ||
                !string.Equals(booking.Status, payload.FromStatus, StringComparison.Ordinal) ||
                !string.Equals(
                    booking.FulfillmentState,
                    payload.FromFulfillmentState,
                    StringComparison.Ordinal))
                return Conflict(
                    "CafeBookingSync.BookingVersionConflict",
                    "Canonical Booking revision does not extend exact retained Booking state.");

            if (RevisionExists(connection, transaction, payload.Revision.RevisionId))
                return Conflict(
                    "CafeBookingSync.RevisionIdConflict",
                    "Canonical revision identity is already retained.");

            int expectedRevisionNumber =
                ReadRevisionNumber(
                    connection,
                    transaction,
                    booking.CurrentRevisionId) + 1;
            if (payload.Revision.RevisionNumber != expectedRevisionNumber)
                return Conflict(
                    "CafeBookingSync.RevisionSequenceConflict",
                    "Canonical Booking revision number is not the next immutable revision.");

            long retainedDeposit = ScalarLong(
                connection,
                transaction,
                """
                SELECT
                    COALESCE(SUM(CASE WHEN fact_type='Received' THEN amount_cents ELSE 0 END),0)
                    - COALESCE(SUM(CASE WHEN fact_type='Refunded' THEN amount_cents ELSE 0 END),0)
                    - COALESCE(SUM(CASE WHEN fact_type='Applied' THEN amount_cents ELSE 0 END),0)
                FROM cafe_booking_deposit_facts
                WHERE booking_id=$booking;
                """,
                ("$booking", payload.BookingId.ToString("D")));
            if (retainedDeposit < 0 ||
                payload.Revision.QuotedTotalCents < retainedDeposit)
                return Conflict(
                    "CafeBookingSync.RevisionBelowRetainedDeposit",
                    "Canonical revision cannot fall below retained Booking deposit liability.");

            InsertRevision(connection, transaction, payload.Revision);
            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_bookings
                SET current_version=$version,
                    current_revision_id=$revision,
                    status=$status,
                    fulfillment_state=$fulfillment,
                    updated_by_user_id=$actor,
                    updated_at_utc=$at
                WHERE booking_id=$booking
                  AND store_id=$store
                  AND current_version=$expected
                  AND current_revision_id=$previous;
                """;
            update.Parameters.AddWithValue("$version", payload.ResultingVersion);
            update.Parameters.AddWithValue("$revision", payload.Revision.RevisionId.ToString("D"));
            update.Parameters.AddWithValue("$status", payload.ToStatus);
            update.Parameters.AddWithValue("$fulfillment", payload.ToFulfillmentState);
            update.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            update.Parameters.AddWithValue("$at", Utc(payload.OccurredAtUtc));
            update.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            update.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
            update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
            update.Parameters.AddWithValue(
                "$previous",
                payload.Revision.PreviousRevisionId!.Value.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
                return Conflict(
                    "CafeBookingSync.BookingVersionConflict",
                    "Canonical Booking revision lost exact-version ownership.");
        }

        InsertOperation(
            connection,
            transaction,
            envelope,
            payload.BookingId,
            payload.ExpectedVersion,
            payload.ResultingVersion,
            payload.Revision.RevisionId,
            payload.ToStatus,
            payload.ToFulfillmentState,
            payload.ActorUserId,
            payload.OccurredAtUtc,
            payloadHash);
        InsertEvent(
            connection,
            transaction,
            payload.OperationId,
            payload.BookingId,
            payload.ExpectedVersion,
            payload.ResultingVersion,
            payload.ConfigurationAction,
            payload.FromStatus,
            payload.ToStatus,
            payload.FromFulfillmentState,
            payload.ToFulfillmentState,
            payload.Revision.RevisionId,
            null,
            null,
            payload.ActorUserId,
            payload.OccurredAtUtc,
            null,
            payloadHash);
        return Result.Success();
    }

    private static Result ApplyStatus(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        BookingStatusAdvancedV1 payload)
    {
        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        if (booking is null ||
            booking.StoreId != payload.StoreId ||
            booking.Version != payload.ExpectedVersion ||
            booking.CurrentRevisionId != payload.BookingRevisionId ||
            !string.Equals(booking.Status, payload.FromStatus, StringComparison.Ordinal) ||
            !string.Equals(
                booking.FulfillmentState,
                payload.FromFulfillmentState,
                StringComparison.Ordinal))
            return Conflict(
                "CafeBookingSync.BookingVersionConflict",
                "Canonical status operation does not extend exact retained Booking state.");

        if (string.Equals(
                payload.ToStatus,
                CafeBookingStatuses.ReadyForFulfillment,
                StringComparison.Ordinal))
        {
            long requirement = ScalarLong(
                connection,
                transaction,
                """
                SELECT deposit_requirement_cents
                FROM cafe_booking_revisions
                WHERE revision_id=$revision;
                """,
                ("$revision", payload.BookingRevisionId.ToString("D")));
            long retained = ScalarLong(
                connection,
                transaction,
                """
                SELECT
                    COALESCE(SUM(CASE WHEN fact_type='Received' THEN amount_cents ELSE 0 END),0)
                    - COALESCE(SUM(CASE WHEN fact_type='Refunded' THEN amount_cents ELSE 0 END),0)
                    - COALESCE(SUM(CASE WHEN fact_type='Applied' THEN amount_cents ELSE 0 END),0)
                FROM cafe_booking_deposit_facts
                WHERE booking_id=$booking;
                """,
                ("$booking", payload.BookingId.ToString("D")));
            if (retained < requirement)
                return Conflict(
                    "CafeBookingSync.DepositRequirementNotMet",
                    "Canonical ReadyForFulfillment transition lacks retained deposit prerequisite.");
        }

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_bookings
                SET current_version=$version,
                    status=$status,
                    fulfillment_state=$fulfillment,
                    updated_by_user_id=$actor,
                    updated_at_utc=$at
                WHERE booking_id=$booking
                  AND current_version=$expected
                  AND current_revision_id=$revision;
                """;
            update.Parameters.AddWithValue("$version", payload.ResultingVersion);
            update.Parameters.AddWithValue("$status", payload.ToStatus);
            update.Parameters.AddWithValue("$fulfillment", payload.ToFulfillmentState);
            update.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            update.Parameters.AddWithValue("$at", Utc(payload.OccurredAtUtc));
            update.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
            update.Parameters.AddWithValue("$revision", payload.BookingRevisionId.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
                return Conflict(
                    "CafeBookingSync.BookingVersionConflict",
                    "Canonical status operation lost exact-version ownership.");
        }

        string hash = HashForEnvelope(envelope);
        InsertOperation(
            connection, transaction, envelope, payload.BookingId,
            payload.ExpectedVersion, payload.ResultingVersion,
            payload.BookingRevisionId, payload.ToStatus,
            payload.ToFulfillmentState, payload.ActorUserId,
            payload.OccurredAtUtc, hash);
        InsertEvent(
            connection, transaction, payload.OperationId, payload.BookingId,
            payload.ExpectedVersion, payload.ResultingVersion,
            payload.ToStatus, payload.FromStatus, payload.ToStatus,
            payload.FromFulfillmentState, payload.ToFulfillmentState,
            payload.BookingRevisionId, null, null, payload.ActorUserId,
            payload.OccurredAtUtc, null, hash);
        return Result.Success();
    }

    private static Result ApplyCancellation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        BookingCancelledV1 payload)
    {
        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        if (booking is null ||
            booking.StoreId != payload.StoreId ||
            booking.Version != payload.ExpectedVersion ||
            booking.CurrentRevisionId != payload.BookingRevisionId ||
            !string.Equals(booking.Status, payload.FromStatus, StringComparison.Ordinal) ||
            !string.Equals(
                booking.FulfillmentState,
                payload.FromFulfillmentState,
                StringComparison.Ordinal))
            return Conflict(
                "CafeBookingSync.BookingVersionConflict",
                "Canonical cancellation does not extend exact retained Booking state.");

        if (string.Equals(
                payload.FromFulfillmentState,
                CafeBookingFulfillmentStates.OrderBound,
                StringComparison.Ordinal))
        {
            using SqliteCommand order = connection.CreateCommand();
            order.Transaction = transaction;
            order.CommandText = """
                SELECT o.state
                FROM cafe_booking_order_links l
                INNER JOIN cafe_orders o ON o.order_id=l.order_id
                WHERE l.booking_id=$booking
                  AND l.booking_revision_id=$revision
                LIMIT 1;
                """;
            order.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            order.Parameters.AddWithValue("$revision", payload.BookingRevisionId.ToString("D"));
            object? state = order.ExecuteScalar();
            if (!string.Equals(
                    Convert.ToString(state, CultureInfo.InvariantCulture),
                    CafeOrderStates.Cancelled,
                    StringComparison.Ordinal))
                return Conflict(
                    "CafeBookingSync.OrderBoundCancellationPrerequisiteMissing",
                    "OrderBound Booking cancellation requires its bound C4 Order to already be Cancelled.");
        }

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_bookings
                SET current_version=$version,
                    status='Cancelled',
                    fulfillment_state=$fulfillment,
                    updated_by_user_id=$actor,
                    updated_at_utc=$at,
                    cancelled_by_user_id=$actor,
                    cancelled_at_utc=$at,
                    cancellation_reason=$reason
                WHERE booking_id=$booking
                  AND current_version=$expected
                  AND current_revision_id=$revision;
                """;
            update.Parameters.AddWithValue("$version", payload.ResultingVersion);
            update.Parameters.AddWithValue("$fulfillment", payload.ToFulfillmentState);
            update.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            update.Parameters.AddWithValue("$at", Utc(payload.OccurredAtUtc));
            update.Parameters.AddWithValue("$reason", payload.Reason);
            update.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
            update.Parameters.AddWithValue("$revision", payload.BookingRevisionId.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
                return Conflict(
                    "CafeBookingSync.BookingVersionConflict",
                    "Canonical cancellation lost exact-version ownership.");
        }

        string hash = HashForEnvelope(envelope);
        InsertOperation(
            connection, transaction, envelope, payload.BookingId,
            payload.ExpectedVersion, payload.ResultingVersion,
            payload.BookingRevisionId, payload.ToStatus,
            payload.ToFulfillmentState, payload.ActorUserId,
            payload.OccurredAtUtc, hash);
        InsertEvent(
            connection, transaction, payload.OperationId, payload.BookingId,
            payload.ExpectedVersion, payload.ResultingVersion,
            CafeBookingActions.Cancelled, payload.FromStatus, payload.ToStatus,
            payload.FromFulfillmentState, payload.ToFulfillmentState,
            payload.BookingRevisionId, null, null, payload.ActorUserId,
            payload.OccurredAtUtc, payload.Reason, hash);
        return Result.Success();
    }

    private static Result ApplyDepositReceive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        BookingDepositReceivedV1 payload)
    {
        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        if (booking is null ||
            booking.StoreId != payload.StoreId ||
            booking.CurrentRevisionId != payload.BookingRevisionId ||
            booking.Version != envelope.BaseVersion ||
            booking.Status is not (
                CafeBookingStatuses.Confirmed or
                CafeBookingStatuses.ReadyForFulfillment) ||
            booking.FulfillmentState != CafeBookingFulfillmentStates.NotStarted)
            return Conflict(
                "CafeBookingSync.DepositReceivePrerequisiteConflict",
                "Deposit receipt requires exact Confirmed/Ready NotStarted Booking revision/version.");

        Result session = ValidateSession(
            connection,
            transaction,
            payload.CashierSessionId,
            payload.ActorUserId);
        if (session.IsFailure) return session;

        if (DepositFactExists(connection, transaction, payload.DepositFactId) ||
            CashMovementExists(connection, transaction, payload.CashMovementId))
            return Conflict(
                "CafeBookingSync.DepositCashIdentityConflict",
                "Deposit receipt fact or cash movement identity is already retained.");

        Result cash = SqliteCashMovementPersistence.Insert(
            connection,
            transaction,
            new CashMovement
            {
                Id = payload.CashMovementId,
                CashierSessionId = payload.CashierSessionId,
                ActorUserId = payload.ActorUserId,
                MovementType = CashMovementType.CashIn,
                Delta = Money.FromCents(payload.AmountCents),
                ReferenceType = CafeBookingCashReferenceTypes.DepositReceipt,
                ReferenceId = payload.DepositFactId,
                Reason = payload.Reference,
                CreatedAt = payload.OccurredAtUtc.ToUniversalTime()
            });
        if (cash.IsFailure) return cash;

        string hash = HashForEnvelope(envelope);
        InsertDepositFact(
            connection, transaction, payload.DepositFactId,
            payload.OperationId, payload.BookingId, payload.StoreId,
            payload.BookingRevisionId, "Received", payload.ActorUserId,
            payload.AmountCents, payload.OccurredAtUtc, payload.Reference,
            payload.CashierSessionId, payload.CashMovementId, null, hash);

        InsertOperation(
            connection, transaction, envelope, payload.BookingId,
            booking.Version, booking.Version, booking.CurrentRevisionId,
            booking.Status, booking.FulfillmentState, payload.ActorUserId,
            payload.OccurredAtUtc, hash);

        return Result.Success();
    }

    private static Result ApplyDepositRefund(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        BookingDepositRefundedV1 payload)
    {
        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        if (booking is null ||
            booking.StoreId != payload.StoreId ||
            booking.CurrentRevisionId != payload.BookingRevisionId ||
            booking.Version != envelope.BaseVersion ||
            booking.Status is not (
                CafeBookingStatuses.Draft or
                CafeBookingStatuses.Confirmed or
                CafeBookingStatuses.ReadyForFulfillment or
                CafeBookingStatuses.Cancelled) ||
            booking.FulfillmentState is not (
                CafeBookingFulfillmentStates.NotStarted or
                CafeBookingFulfillmentStates.OrderBound))
            return Conflict(
                "CafeBookingSync.DepositRefundPrerequisiteConflict",
                "Deposit refund requires exact pre-SaleBound Booking revision/version.");

        if (ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_deposit_facts
                WHERE booking_id=$booking AND fact_type='Applied';
                """,
                ("$booking", payload.BookingId.ToString("D"))) != 0)
            return Conflict(
                "CafeBookingSync.DepositAlreadyApplied",
                "Applied Booking deposit cannot be refunded through C9 deposit authority.");

        Result session = ValidateSession(
            connection,
            transaction,
            payload.CashierSessionId,
            payload.ActorUserId);
        if (session.IsFailure) return session;

        if (DepositFactExists(connection, transaction, payload.DepositFactId) ||
            CashMovementExists(connection, transaction, payload.CashMovementId))
            return Conflict(
                "CafeBookingSync.DepositCashIdentityConflict",
                "Deposit refund fact or cash movement identity is already retained.");

        foreach (BookingDepositAllocationV1 allocation in payload.Allocations)
        {
            ReceivedDeposit? received = ReadReceivedDeposit(
                connection,
                transaction,
                allocation.ReceivedDepositFactId);
            if (received is null ||
                received.BookingId != payload.BookingId ||
                received.StoreId != payload.StoreId)
                return Conflict(
                    "CafeBookingSync.DepositAllocationSourceMissing",
                    "Refund allocation requires a retained same-Booking Received deposit fact.");

            long consumed = ReadAllocatedTotal(
                connection,
                transaction,
                allocation.ReceivedDepositFactId);
            if (checked(consumed + allocation.AmountCents) > received.AmountCents)
                return Conflict(
                    "CafeBookingSync.DepositAllocationConflict",
                    "Refund allocation exceeds retained unconsumed Received authority.");
        }

        Result cash = SqliteCashMovementPersistence.Insert(
            connection,
            transaction,
            new CashMovement
            {
                Id = payload.CashMovementId,
                CashierSessionId = payload.CashierSessionId,
                ActorUserId = payload.ActorUserId,
                MovementType = CashMovementType.CashOut,
                Delta = Money.FromCents(-payload.AmountCents),
                ReferenceType = CafeBookingCashReferenceTypes.DepositRefund,
                ReferenceId = payload.DepositFactId,
                Reason = payload.Reason,
                CreatedAt = payload.OccurredAtUtc.ToUniversalTime()
            });
        if (cash.IsFailure) return cash;

        string hash = HashForEnvelope(envelope);
        InsertDepositFact(
            connection, transaction, payload.DepositFactId,
            payload.OperationId, payload.BookingId, payload.StoreId,
            payload.BookingRevisionId, "Refunded", payload.ActorUserId,
            payload.AmountCents, payload.OccurredAtUtc, payload.Reference,
            payload.CashierSessionId, payload.CashMovementId, null, hash);

        foreach (BookingDepositAllocationV1 allocation in payload.Allocations)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO cafe_booking_deposit_allocations(
                    action_fact_id,received_fact_id,amount_cents)
                VALUES($action,$received,$amount);
                """;
            insert.Parameters.AddWithValue("$action", payload.DepositFactId.ToString("D"));
            insert.Parameters.AddWithValue("$received", allocation.ReceivedDepositFactId.ToString("D"));
            insert.Parameters.AddWithValue("$amount", allocation.AmountCents);
            insert.ExecuteNonQuery();
        }

        InsertOperation(
            connection, transaction, envelope, payload.BookingId,
            booking.Version, booking.Version, booking.CurrentRevisionId,
            booking.Status, booking.FulfillmentState, payload.ActorUserId,
            payload.OccurredAtUtc, hash);
        return Result.Success();
    }

    private static Result ApplyOrderBound(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        BookingFulfillmentOrderBoundV1 payload)
    {
        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        if (booking is null ||
            booking.StoreId != payload.StoreId ||
            booking.Version != payload.ExpectedBookingVersion ||
            booking.CurrentRevisionId != payload.BookingRevisionId ||
            booking.Status != CafeBookingStatuses.ReadyForFulfillment ||
            booking.FulfillmentState != CafeBookingFulfillmentStates.NotStarted)
            return Conflict(
                "CafeBookingSync.OrderBoundPrerequisiteConflict",
                "OrderBound requires exact ReadyForFulfillment / NotStarted Booking authority.");

        if (ScalarLong(
                connection, transaction,
                "SELECT COUNT(*) FROM cafe_booking_order_links WHERE booking_id=$booking;",
                ("$booking", payload.BookingId.ToString("D"))) != 0 ||
            ScalarLong(
                connection, transaction,
                "SELECT COUNT(*) FROM cafe_booking_sale_links WHERE booking_id=$booking;",
                ("$booking", payload.BookingId.ToString("D"))) != 0)
            return Conflict(
                "CafeBookingSync.OrderBoundLinkConflict",
                "Booking already owns Order/Sale linkage.");

        Result bookingOrderMatch = ValidateOrderMatchesBookingRevision(
            connection,
            transaction,
            payload);
        if (bookingOrderMatch.IsFailure) return bookingOrderMatch;

        Result order = SeedCanonicalOrder(
            connection,
            transaction,
            payload,
            envelope.DeviceId);
        if (order.IsFailure) return order;

        string hash =
            BookingFulfillmentOrderBoundV1CanonicalJson.ComputeHash(
                envelope.PayloadJson);

        InsertOperation(
            connection, transaction, envelope, payload.BookingId,
            payload.ExpectedBookingVersion, payload.ResultingBookingVersion,
            payload.BookingRevisionId, payload.ResultBookingStatus,
            payload.ResultFulfillmentState, payload.ActorUserId,
            payload.OccurredAtUtc, hash);

        using (SqliteCommand link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO cafe_booking_order_links(
                    booking_order_link_id,booking_id,booking_revision_id,
                    order_id,operation_id,actor_user_id,occurred_at_utc,content_hash)
                VALUES($link,$booking,$revision,$order,$operation,$actor,$at,$hash);
                """;
            link.Parameters.AddWithValue("$link", payload.BookingOrderLinkId.ToString("D"));
            link.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            link.Parameters.AddWithValue("$revision", payload.BookingRevisionId.ToString("D"));
            link.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
            link.Parameters.AddWithValue("$operation", payload.OperationId.ToString("D"));
            link.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            link.Parameters.AddWithValue("$at", Utc(payload.OccurredAtUtc));
            link.Parameters.AddWithValue("$hash", hash);
            link.ExecuteNonQuery();
        }

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_bookings
                SET current_version=$version,
                    fulfillment_state='OrderBound',
                    updated_by_user_id=$actor,
                    updated_at_utc=$at
                WHERE booking_id=$booking
                  AND current_version=$expected
                  AND current_revision_id=$revision
                  AND status='ReadyForFulfillment'
                  AND fulfillment_state='NotStarted';
                """;
            update.Parameters.AddWithValue("$version", payload.ResultingBookingVersion);
            update.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            update.Parameters.AddWithValue("$at", Utc(payload.OccurredAtUtc));
            update.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            update.Parameters.AddWithValue("$expected", payload.ExpectedBookingVersion);
            update.Parameters.AddWithValue("$revision", payload.BookingRevisionId.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
                return Conflict(
                    "CafeBookingSync.OrderBoundConcurrentConflict",
                    "OrderBound lost exact Booking version ownership.");
        }

        InsertEvent(
            connection, transaction, payload.OperationId, payload.BookingId,
            payload.ExpectedBookingVersion, payload.ResultingBookingVersion,
            CafeBookingActions.OrderBound,
            CafeBookingStatuses.ReadyForFulfillment,
            payload.ResultBookingStatus,
            CafeBookingFulfillmentStates.NotStarted,
            payload.ResultFulfillmentState,
            payload.BookingRevisionId,
            payload.OrderId,
            null,
            payload.ActorUserId,
            payload.OccurredAtUtc,
            null,
            hash);
        return Result.Success();
    }

    private static Result SeedCanonicalOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingFulfillmentOrderBoundV1 payload,
        Guid deviceId)
    {
        if (ScalarLong(
                connection,
                transaction,
                "SELECT COUNT(*) FROM cafe_orders WHERE order_id=$order;",
                ("$order", payload.OrderId.ToString("D"))) != 0)
            return Conflict(
                "CafeBookingSync.OrderIdCollision",
                "Canonical Booking OrderId already exists.");

        Result publication = ValidatePublication(
            connection,
            transaction,
            payload.MenuPublicationRevisionId,
            payload.StoreId);
        if (publication.IsFailure) return publication;

        Result orderSnapshot = ValidateCanonicalOrderRevision(
            connection,
            transaction,
            payload);
        if (orderSnapshot.IsFailure) return orderSnapshot;

        CafeOrderRevisionSnapshot revision =
            ToLocalOrderRevision(payload.OrderId, payload.OrderRevision);

        using (SqliteCommand order = connection.CreateCommand())
        {
            order.Transaction = transaction;
            order.CommandText = """
                INSERT INTO cafe_orders(
                    order_id,store_id,current_version,current_revision_id,
                    menu_publication_revision_id,state,service_mode,service_reference,
                    created_by_user_id,created_at_utc,updated_at_utc,
                    submitted_revision_id,submitted_recipe_book_revision_id,
                    completed_sale_id,cancellation_reason)
                VALUES(
                    $order,$store,1,$revision,
                    $menu,'Open',$mode,$reference,
                    $actor,$at,$at,NULL,NULL,NULL,NULL);
                """;
            order.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
            order.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
            order.Parameters.AddWithValue("$revision", revision.RevisionId.ToString("D"));
            order.Parameters.AddWithValue("$menu", payload.MenuPublicationRevisionId);
            order.Parameters.AddWithValue("$mode", payload.IntendedServiceMode);
            order.Parameters.AddWithValue(
                "$reference",
                string.IsNullOrWhiteSpace(revision.ServiceReference)
                    ? DBNull.Value
                    : revision.ServiceReference);
            order.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            order.Parameters.AddWithValue("$at", Utc(payload.OccurredAtUtc));
            order.ExecuteNonQuery();
        }

        SqliteCafeOrderStore.InsertRevision(
            connection,
            transaction,
            revision);

        CafeOrderOperationV1 c4Payload = new(
            CafeOrderOperationContract.OrderRevisionCommittedV1,
            payload.OrderId,
            payload.StoreId,
            0,
            1,
            CafeOrderLifecycleActions.Created,
            null,
            CafeOrderStates.Open,
            payload.ActorUserId,
            payload.OccurredAtUtc.ToUniversalTime(),
            revision.RevisionId,
            payload.MenuPublicationRevisionId,
            payload.IntendedServiceMode,
            revision.ServiceReference,
            payload.OrderRevision,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<CafeOrderSaleItemLinkV1>());

        string c4Json = CafeOrderOperationCanonicalJson.Serialize(c4Payload);
        CafeOrderCanonicalParseResult c4Parsed =
            CafeOrderOperationCanonicalJson.Parse(
                CafeOrderOperationContract.OrderRevisionCommittedV1,
                c4Json);
        if (!c4Parsed.IsValid)
            return Conflict(
                c4Parsed.ErrorCode ?? "CafeBookingSync.C4CanonicalInvalid",
                c4Parsed.ErrorMessage ?? "Embedded canonical C4 seed is invalid.");

        string c4Hash = CafeOrderOperationCanonicalJson.ComputeHash(c4Json);
        SqliteCafeOrderStore.InsertOperationAndLifecycle(
            connection,
            transaction,
            payload.OperationId,
            payload.OrderId,
            CafeOrderOperationContract.OrderRevisionCommittedV1,
            c4Hash,
            0,
            1,
            revision.RevisionId,
            CafeOrderStates.Open,
            null,
            payload.ActorUserId,
            payload.OccurredAtUtc,
            CafeOrderLifecycleActions.Created,
            null,
            CafeOrderStates.Open,
            null,
            revision.RevisionId,
            null,
            null);

        return Result.Success();
    }

    private static Result ValidateRevisionPrerequisites(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingRevisionSnapshotV1 revision,
        Guid storeId)
    {
        if (ScalarLong(
                connection, transaction,
                "SELECT COUNT(*) FROM customers WHERE id=$customer;",
                ("$customer", revision.CustomerId.ToString("D"))) != 1)
            return Conflict(
                "CafeBookingSync.CustomerPrerequisiteMissing",
                "Canonical Booking Customer prerequisite is missing.");

        Result publication = ValidatePublication(
            connection,
            transaction,
            revision.MenuPublicationRevisionId,
            storeId);
        if (publication.IsFailure) return publication;

        foreach (BookingLineSnapshotV1 line in revision.Lines)
        {
            Result lineResult = ValidateBookingLine(
                connection,
                transaction,
                revision.MenuPublicationRevisionId,
                line);
            if (lineResult.IsFailure) return lineResult;
        }

        return Result.Success();
    }

    private static Result ValidatePublication(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId)
    {
        return ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM menu_revisions
            WHERE revision_id=$revision
              AND store_id=$store
              AND status IN ('Published','Superseded');
            """,
            ("$revision", revisionId),
            ("$store", storeId.ToString("D"))) == 1
            ? Result.Success()
            : Conflict(
                "CafeBookingSync.MenuPublicationPrerequisiteMissing",
                "Frozen C1 MenuPublication prerequisite is missing.");
    }

    private static Result ValidateBookingLine(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string menuRevision,
        BookingLineSnapshotV1 line)
    {
        using (SqliteCommand item = connection.CreateCommand())
        {
            item.Transaction = transaction;
            item.CommandText = """
                SELECT name,COALESCE(unit_name,'Unit'),selling_price_cents,
                       is_temporarily_unavailable
                FROM menu_revision_items
                WHERE revision_id=$revision AND product_id=$product
                LIMIT 1;
                """;
            item.Parameters.AddWithValue("$revision", menuRevision);
            item.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            using SqliteDataReader reader = item.ExecuteReader();
            if (!reader.Read() ||
                !string.Equals(reader.GetString(0), line.ProductNameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(1), line.UnitNameSnapshot, StringComparison.Ordinal) ||
                reader.GetInt64(2) != line.BaseUnitPriceCents ||
                reader.GetInt32(3) != 0)
                return Conflict(
                    "CafeBookingSync.ProductSnapshotConflict",
                    "Canonical Booking Product snapshot differs from frozen MenuPublication.");
        }

        long selectedBase = line.BaseUnitPriceCents;
        if (line.VariantId is not null)
        {
            using SqliteCommand variant = connection.CreateCommand();
            variant.Transaction = transaction;
            variant.CommandText = """
                SELECT product_id,name,selling_price_cents,is_temporarily_unavailable
                FROM menu_revision_variants
                WHERE revision_id=$revision AND variant_id=$variant
                LIMIT 1;
                """;
            variant.Parameters.AddWithValue("$revision", menuRevision);
            variant.Parameters.AddWithValue("$variant", line.VariantId);
            using SqliteDataReader reader = variant.ExecuteReader();
            if (!reader.Read() ||
                Guid.Parse(reader.GetString(0)) != line.ProductId ||
                !string.Equals(reader.GetString(1), line.VariantNameSnapshot, StringComparison.Ordinal) ||
                !line.VariantUnitPriceCents.HasValue ||
                reader.GetInt64(2) != line.VariantUnitPriceCents.Value ||
                reader.GetInt32(3) != 0)
                return Conflict(
                    "CafeBookingSync.VariantSnapshotConflict",
                    "Canonical Booking Variant snapshot differs from frozen MenuPublication.");
            selectedBase = line.VariantUnitPriceCents.Value;
        }
        else if (line.VariantNameSnapshot is not null ||
                 line.VariantUnitPriceCents.HasValue)
            return Conflict(
                "CafeBookingSync.VariantSnapshotConflict",
                "Canonical Booking Variant evidence is inconsistent.");

        long modifierDelta = 0;
        foreach (BookingModifierSelectionV1 modifier in line.ModifierSelections)
        {
            using SqliteCommand option = connection.CreateCommand();
            option.Transaction = transaction;
            option.CommandText = """
                SELECT g.name,o.name,o.price_delta_cents,o.display_order,
                       CASE
                         WHEN EXISTS(
                           SELECT 1 FROM menu_revision_item_modifier_groups l
                           WHERE l.revision_id=$revision
                             AND l.product_id=$product
                             AND l.group_id=o.group_id)
                         OR EXISTS(
                           SELECT 1 FROM menu_revision_variant_modifier_groups l
                           WHERE l.revision_id=$revision
                             AND l.variant_id=$variant
                             AND l.group_id=o.group_id)
                         THEN 1 ELSE 0
                       END
                FROM menu_revision_modifier_options o
                INNER JOIN menu_revision_modifier_groups g
                  ON g.revision_id=o.revision_id
                 AND g.group_id=o.group_id
                WHERE o.revision_id=$revision
                  AND o.option_id=$option
                  AND o.group_id=$group
                LIMIT 1;
                """;
            option.Parameters.AddWithValue("$revision", menuRevision);
            option.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            option.Parameters.AddWithValue("$variant", line.VariantId ?? string.Empty);
            option.Parameters.AddWithValue("$option", modifier.OptionId);
            option.Parameters.AddWithValue("$group", modifier.GroupId);
            using SqliteDataReader reader = option.ExecuteReader();
            if (!reader.Read() ||
                !string.Equals(reader.GetString(0), modifier.GroupNameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(1), modifier.OptionNameSnapshot, StringComparison.Ordinal) ||
                reader.GetInt64(2) != modifier.PriceDeltaCents ||
                reader.GetInt32(3) != modifier.DisplayOrder ||
                reader.GetInt32(4) != 1)
                return Conflict(
                    "CafeBookingSync.ModifierSnapshotConflict",
                    "Canonical Booking Modifier snapshot differs from frozen MenuPublication.");
            modifierDelta = checked(modifierDelta + modifier.PriceDeltaCents);
        }

        long expectedUnit = checked(selectedBase + modifierDelta);
        long expectedLine = checked((long)Math.Round(
            expectedUnit * (line.QuantityX1000 / 1000m),
            0,
            MidpointRounding.AwayFromZero));
        return expectedUnit == line.QuotedUnitPriceCents &&
               expectedLine == line.LineTotalCents
            ? Result.Success()
            : Conflict(
                "CafeBookingSync.PriceSnapshotConflict",
                "Canonical Booking quote does not re-derive from frozen MenuPublication.");
    }

    private static Result ValidateOrderMatchesBookingRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingFulfillmentOrderBoundV1 payload)
    {
        long bookingLineCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_booking_revision_lines
            WHERE revision_id=$revision;
            """,
            ("$revision", payload.BookingRevisionId.ToString("D")));
        if (bookingLineCount != payload.OrderRevision.Lines.Count)
            return Conflict(
                "CafeBookingSync.OrderSnapshotMismatch",
                "Canonical C4 seed line count differs from frozen Booking revision.");

        foreach (CafeOrderLineSnapshotV1 line in payload.OrderRevision.Lines)
        {
            if (ScalarLong(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM cafe_booking_revision_lines
                    WHERE revision_id=$revision
                      AND booking_line_id=$line
                      AND product_id=$product
                      AND product_name_snapshot=$name
                      AND unit_name_snapshot=$unit
                      AND quantity_x1000=$quantity
                      AND base_unit_price_cents=$base
                      AND COALESCE(variant_id,'')=COALESCE($variant,'')
                      AND COALESCE(variant_name_snapshot,'')=COALESCE($variantName,'')
                      AND COALESCE(variant_unit_price_cents,-1)=COALESCE($variantPrice,-1)
                      AND quoted_unit_price_cents=$quoted
                      AND line_total_cents=$total
                      AND display_order=$display
                      AND COALESCE(line_note,'')=COALESCE($note,'');
                    """,
                    ("$revision", payload.BookingRevisionId.ToString("D")),
                    ("$line", line.OrderLineId.ToString("D")),
                    ("$product", line.ProductId.ToString("D")),
                    ("$name", line.ProductNameSnapshot),
                    ("$unit", line.UnitNameSnapshot),
                    ("$quantity", line.QuantityX1000),
                    ("$base", line.BaseUnitPriceCents),
                    ("$variant", line.VariantId ?? (object)DBNull.Value),
                    ("$variantName", line.VariantNameSnapshot ?? (object)DBNull.Value),
                    ("$variantPrice", line.VariantUnitPriceCents ?? (object)DBNull.Value),
                    ("$quoted", line.CalculatedUnitPriceCents),
                    ("$total", line.LineTotalCents),
                    ("$display", line.DisplayOrder),
                    ("$note", line.Note ?? (object)DBNull.Value)) != 1)
                return Conflict(
                    "CafeBookingSync.OrderSnapshotMismatch",
                    "Canonical C4 seed line differs from frozen Booking revision.");

            long bookingModifierCount = ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_revision_modifier_selections
                WHERE revision_id=$revision AND booking_line_id=$line;
                """,
                ("$revision", payload.BookingRevisionId.ToString("D")),
                ("$line", line.OrderLineId.ToString("D")));
            if (bookingModifierCount != line.ModifierSelections.Count)
                return Conflict(
                    "CafeBookingSync.OrderModifierSnapshotMismatch",
                    "Canonical C4 seed modifiers differ from frozen Booking revision.");

            foreach (CafeOrderModifierSnapshotV1 modifier in line.ModifierSelections)
            {
                if (ScalarLong(
                        connection,
                        transaction,
                        """
                        SELECT COUNT(*)
                        FROM cafe_booking_revision_modifier_selections
                        WHERE revision_id=$revision
                          AND booking_line_id=$line
                          AND group_id=$group
                          AND group_name_snapshot=$groupName
                          AND option_id=$option
                          AND option_name_snapshot=$optionName
                          AND price_delta_cents=$price
                          AND display_order=$display;
                        """,
                        ("$revision", payload.BookingRevisionId.ToString("D")),
                        ("$line", line.OrderLineId.ToString("D")),
                        ("$group", modifier.GroupId),
                        ("$groupName", modifier.GroupNameSnapshot),
                        ("$option", modifier.OptionId),
                        ("$optionName", modifier.OptionNameSnapshot),
                        ("$price", modifier.PriceDeltaCents),
                        ("$display", modifier.DisplayOrder)) != 1)
                    return Conflict(
                        "CafeBookingSync.OrderModifierSnapshotMismatch",
                        "Canonical C4 seed modifier differs from frozen Booking revision.");
            }
        }

        return Result.Success();
    }

    private static Result ValidateCanonicalOrderRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingFulfillmentOrderBoundV1 payload)
    {
        foreach (CafeOrderLineSnapshotV1 line in payload.OrderRevision.Lines)
        {
            BookingLineSnapshotV1 bookingLine = new(
                line.OrderLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.UnitNameSnapshot,
                line.QuantityX1000,
                line.BaseUnitPriceCents,
                line.VariantId,
                line.VariantNameSnapshot,
                line.VariantUnitPriceCents,
                line.CalculatedUnitPriceCents,
                line.LineTotalCents,
                line.DisplayOrder,
                line.Note,
                line.ModifierSelections.Select(m =>
                    new BookingModifierSelectionV1(
                        m.GroupId,
                        m.GroupNameSnapshot,
                        m.OptionId,
                        m.OptionNameSnapshot,
                        m.PriceDeltaCents,
                        m.DisplayOrder)).ToArray());

            Result result = ValidateBookingLine(
                connection,
                transaction,
                payload.MenuPublicationRevisionId,
                bookingLine);
            if (result.IsFailure) return result;
        }

        return Result.Success();
    }

    private static CafeOrderRevisionSnapshot ToLocalOrderRevision(
        Guid orderId,
        CafeOrderRevisionSnapshotV1 r) =>
        new(
            r.RevisionId,
            orderId,
            r.RevisionNumber,
            r.OrderVersion,
            r.PreviousRevisionId,
            r.MenuPublicationRevisionId,
            r.ServiceMode,
            r.ServiceReference,
            r.ContentHash,
            r.ActorUserId,
            r.CreatedAtUtc,
            r.Lines.Select(line =>
                new CafeOrderLineSnapshot(
                    line.OrderLineId,
                    line.ProductId,
                    line.ProductNameSnapshot,
                    line.UnitNameSnapshot,
                    line.QuantityX1000,
                    line.BaseUnitPriceCents,
                    line.VariantId,
                    line.VariantNameSnapshot,
                    line.VariantUnitPriceCents,
                    line.ModifierSelections.Select(m =>
                        new CafeOrderModifierSelectionSnapshot(
                            m.GroupId,
                            m.GroupNameSnapshot,
                            m.OptionId,
                            m.OptionNameSnapshot,
                            m.PriceDeltaCents,
                            m.DisplayOrder)).ToArray(),
                    line.Note,
                    line.CalculatedUnitPriceCents,
                    line.LineTotalCents,
                    line.DisplayOrder)).ToArray());

    private static void InsertBooking(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId,
        Guid storeId,
        int version,
        Guid revisionId,
        string status,
        string fulfillment,
        Guid actor,
        DateTimeOffset occurredAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_bookings(
                booking_id,store_id,current_version,current_revision_id,
                status,fulfillment_state,created_by_user_id,created_at_utc,
                updated_by_user_id,updated_at_utc,
                cancelled_by_user_id,cancelled_at_utc,cancellation_reason)
            VALUES(
                $booking,$store,$version,$revision,
                $status,$fulfillment,$actor,$at,
                $actor,$at,NULL,NULL,NULL);
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$fulfillment", fulfillment);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$at", Utc(occurredAt));
        command.ExecuteNonQuery();
    }

    private static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingRevisionSnapshotV1 r)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cafe_booking_revisions(
                    revision_id,booking_id,revision_number,booking_version,previous_revision_id,
                    customer_id,customer_name_snapshot,customer_phone_snapshot,customer_address_snapshot,
                    booking_mode,event_name,event_reference,event_start_utc,event_end_utc,location,
                    setup_at_utc,service_window_start_utc,service_window_end_utc,headcount,package_label,
                    menu_publication_revision_id,quoted_total_cents,deposit_requirement_cents,
                    special_instructions,notes,fulfillment_service_mode,actor_user_id,created_at_utc,content_hash)
                VALUES(
                    $revision,$booking,$number,$version,$previous,
                    $customer,$name,$phone,$address,
                    $mode,$event,$eventRef,$start,$end,$location,
                    $setup,$serviceStart,$serviceEnd,$headcount,$package,
                    $menu,$quoted,$deposit,$instructions,$notes,$serviceMode,$actor,$at,$hash);
                """;
            command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
            command.Parameters.AddWithValue("$booking", r.BookingId.ToString("D"));
            command.Parameters.AddWithValue("$number", r.RevisionNumber);
            command.Parameters.AddWithValue("$version", r.BookingVersion);
            command.Parameters.AddWithValue("$previous", Db(r.PreviousRevisionId?.ToString("D")));
            command.Parameters.AddWithValue("$customer", r.CustomerId.ToString("D"));
            command.Parameters.AddWithValue("$name", r.CustomerNameSnapshot);
            command.Parameters.AddWithValue("$phone", Db(r.CustomerPhoneSnapshot));
            command.Parameters.AddWithValue("$address", Db(r.CustomerAddressSnapshot));
            command.Parameters.AddWithValue("$mode", r.BookingMode);
            command.Parameters.AddWithValue("$event", r.EventName);
            command.Parameters.AddWithValue("$eventRef", Db(r.EventReference));
            command.Parameters.AddWithValue("$start", Utc(r.EventStartUtc));
            command.Parameters.AddWithValue("$end", r.EventEndUtc.HasValue ? Utc(r.EventEndUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$location", Db(r.Location));
            command.Parameters.AddWithValue("$setup", r.SetupAtUtc.HasValue ? Utc(r.SetupAtUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$serviceStart", r.ServiceWindowStartUtc.HasValue ? Utc(r.ServiceWindowStartUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$serviceEnd", r.ServiceWindowEndUtc.HasValue ? Utc(r.ServiceWindowEndUtc.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$headcount", r.Headcount ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$package", Db(r.PackageLabel));
            command.Parameters.AddWithValue("$menu", r.MenuPublicationRevisionId);
            command.Parameters.AddWithValue("$quoted", r.QuotedTotalCents);
            command.Parameters.AddWithValue("$deposit", r.DepositRequirementCents);
            command.Parameters.AddWithValue("$instructions", Db(r.SpecialInstructions));
            command.Parameters.AddWithValue("$notes", Db(r.Notes));
            command.Parameters.AddWithValue("$serviceMode", r.IntendedServiceMode);
            command.Parameters.AddWithValue("$actor", r.ActorUserId.ToString("D"));
            command.Parameters.AddWithValue("$at", Utc(r.CreatedAtUtc));
            command.Parameters.AddWithValue("$hash", r.ContentHash);
            command.ExecuteNonQuery();
        }

        foreach (BookingLineSnapshotV1 line in r.Lines)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_booking_revision_lines(
                        revision_id,booking_line_id,product_id,product_name_snapshot,unit_name_snapshot,
                        quantity_x1000,base_unit_price_cents,variant_id,variant_name_snapshot,
                        variant_unit_price_cents,quoted_unit_price_cents,line_total_cents,display_order,line_note)
                    VALUES(
                        $revision,$line,$product,$name,$unit,
                        $quantity,$base,$variant,$variantName,
                        $variantPrice,$quoted,$total,$display,$note);
                    """;
                command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
                command.Parameters.AddWithValue("$line", line.BookingLineId.ToString("D"));
                command.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
                command.Parameters.AddWithValue("$name", line.ProductNameSnapshot);
                command.Parameters.AddWithValue("$unit", line.UnitNameSnapshot);
                command.Parameters.AddWithValue("$quantity", line.QuantityX1000);
                command.Parameters.AddWithValue("$base", line.BaseUnitPriceCents);
                command.Parameters.AddWithValue("$variant", Db(line.VariantId));
                command.Parameters.AddWithValue("$variantName", Db(line.VariantNameSnapshot));
                command.Parameters.AddWithValue("$variantPrice", line.VariantUnitPriceCents ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("$quoted", line.QuotedUnitPriceCents);
                command.Parameters.AddWithValue("$total", line.LineTotalCents);
                command.Parameters.AddWithValue("$display", line.DisplayOrder);
                command.Parameters.AddWithValue("$note", Db(line.LineNote));
                command.ExecuteNonQuery();
            }

            foreach (BookingModifierSelectionV1 modifier in line.ModifierSelections)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_booking_revision_modifier_selections(
                        revision_id,booking_line_id,group_id,group_name_snapshot,
                        option_id,option_name_snapshot,price_delta_cents,display_order)
                    VALUES($revision,$line,$group,$groupName,$option,$optionName,$price,$display);
                    """;
                command.Parameters.AddWithValue("$revision", r.RevisionId.ToString("D"));
                command.Parameters.AddWithValue("$line", line.BookingLineId.ToString("D"));
                command.Parameters.AddWithValue("$group", modifier.GroupId);
                command.Parameters.AddWithValue("$groupName", modifier.GroupNameSnapshot);
                command.Parameters.AddWithValue("$option", modifier.OptionId);
                command.Parameters.AddWithValue("$optionName", modifier.OptionNameSnapshot);
                command.Parameters.AddWithValue("$price", modifier.PriceDeltaCents);
                command.Parameters.AddWithValue("$display", modifier.DisplayOrder);
                command.ExecuteNonQuery();
            }
        }
    }

    private static void InsertOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        Guid bookingId,
        int expectedVersion,
        int resultingVersion,
        Guid? resultRevisionId,
        string resultStatus,
        string resultFulfillmentState,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_operations(
                operation_id,booking_id,device_id,store_id,operation_type,payload_hash,
                expected_version,resulting_version,result_revision_id,result_status,
                result_fulfillment_state,actor_user_id,occurred_at_utc)
            VALUES(
                $operation,$booking,$device,$store,$type,$hash,
                $expected,$resulting,$revision,$status,
                $fulfillment,$actor,$at);
            """;
        command.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$device", envelope.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$store", envelope.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$type", envelope.OperationType);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$revision", resultRevisionId.HasValue ? resultRevisionId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$status", resultStatus);
        command.Parameters.AddWithValue("$fulfillment", resultFulfillmentState);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", Utc(occurredAtUtc));
        command.ExecuteNonQuery();
    }

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid bookingId,
        int expectedVersion,
        int resultingVersion,
        string action,
        string? fromStatus,
        string toStatus,
        string? fromFulfillment,
        string toFulfillment,
        Guid? revisionId,
        Guid? orderId,
        Guid? saleId,
        Guid actor,
        DateTimeOffset at,
        string? reason,
        string hash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_events(
                event_id,operation_id,booking_id,expected_version,resulting_version,
                action,from_status,to_status,from_fulfillment_state,to_fulfillment_state,
                booking_revision_id,order_id,sale_id,actor_user_id,occurred_at_utc,reason,content_hash)
            VALUES(
                $event,$operation,$booking,$expected,$resulting,
                $action,$from,$to,$fromFulfillment,$toFulfillment,
                $revision,$order,$sale,$actor,$at,$reason,$hash);
            """;
        command.Parameters.AddWithValue(
            "$event",
            DeterministicGuid(
                "salryn-c9-booking-event-v1",
                operationId.ToString("D")).ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$from", Db(fromStatus));
        command.Parameters.AddWithValue("$to", toStatus);
        command.Parameters.AddWithValue("$fromFulfillment", Db(fromFulfillment));
        command.Parameters.AddWithValue("$toFulfillment", toFulfillment);
        command.Parameters.AddWithValue("$revision", revisionId.HasValue ? revisionId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$order", orderId.HasValue ? orderId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$sale", saleId.HasValue ? saleId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$at", Utc(at));
        command.Parameters.AddWithValue("$reason", Db(reason));
        command.Parameters.AddWithValue("$hash", hash);
        command.ExecuteNonQuery();
    }

    private static void InsertDepositFact(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid factId,
        Guid operationId,
        Guid bookingId,
        Guid storeId,
        Guid revisionId,
        string factType,
        Guid actor,
        long amount,
        DateTimeOffset at,
        string reference,
        Guid? cashierSessionId,
        Guid? cashMovementId,
        Guid? saleId,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_booking_deposit_facts(
                deposit_fact_id,operation_id,booking_id,store_id,booking_revision_id,
                fact_type,actor_user_id,amount_cents,occurred_at_utc,reference,
                cashier_session_id,cash_movement_id,sale_id,payload_hash)
            VALUES(
                $fact,$operation,$booking,$store,$revision,
                $type,$actor,$amount,$at,$reference,
                $session,$movement,$sale,$hash);
            """;
        command.Parameters.AddWithValue("$fact", factId.ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$type", factType);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$at", Utc(at));
        command.Parameters.AddWithValue("$reference", reference);
        command.Parameters.AddWithValue("$session", cashierSessionId.HasValue ? cashierSessionId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$movement", cashMovementId.HasValue ? cashMovementId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$sale", saleId.HasValue ? saleId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.ExecuteNonQuery();
    }

    private static bool VerifyRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationRow operation,
        BookingRevisionCommittedV1 payload)
    {
        if (!OperationMatches(
                operation, payload.BookingId, payload.ExpectedVersion,
                payload.ResultingVersion, payload.Revision.RevisionId,
                payload.ToStatus, payload.ToFulfillmentState,
                payload.ActorUserId, payload.OccurredAtUtc))
            return false;

        if (!RevisionMatches(connection, transaction, payload.Revision))
            return false;

        if (!EventMatches(
                connection, transaction, payload.OperationId, payload.BookingId,
                payload.ExpectedVersion, payload.ResultingVersion,
                payload.ConfigurationAction, payload.FromStatus, payload.ToStatus,
                payload.FromFulfillmentState, payload.ToFulfillmentState,
                payload.Revision.RevisionId, null, null,
                payload.ActorUserId, payload.OccurredAtUtc, null,
                operation.PayloadHash))
            return false;

        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        return booking is not null &&
            booking.StoreId == payload.StoreId &&
            booking.Version >= payload.ResultingVersion;
    }

    private static bool VerifyStatus(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationRow operation,
        BookingStatusAdvancedV1 payload)
    {
        if (!OperationMatches(
                operation, payload.BookingId, payload.ExpectedVersion,
                payload.ResultingVersion, payload.BookingRevisionId,
                payload.ToStatus, payload.ToFulfillmentState,
                payload.ActorUserId, payload.OccurredAtUtc))
            return false;

        if (!EventMatches(
                connection, transaction, payload.OperationId, payload.BookingId,
                payload.ExpectedVersion, payload.ResultingVersion, payload.ToStatus,
                payload.FromStatus, payload.ToStatus,
                payload.FromFulfillmentState, payload.ToFulfillmentState,
                payload.BookingRevisionId, null, null,
                payload.ActorUserId, payload.OccurredAtUtc, null,
                operation.PayloadHash))
            return false;

        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        return booking is not null && booking.Version >= payload.ResultingVersion;
    }

    private static bool VerifyCancellation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationRow operation,
        BookingCancelledV1 payload)
    {
        return OperationMatches(
                operation, payload.BookingId, payload.ExpectedVersion,
                payload.ResultingVersion, payload.BookingRevisionId,
                payload.ToStatus, payload.ToFulfillmentState,
                payload.ActorUserId, payload.OccurredAtUtc) &&
            EventMatches(
                connection, transaction, payload.OperationId, payload.BookingId,
                payload.ExpectedVersion, payload.ResultingVersion,
                CafeBookingActions.Cancelled, payload.FromStatus, payload.ToStatus,
                payload.FromFulfillmentState, payload.ToFulfillmentState,
                payload.BookingRevisionId, null, null,
                payload.ActorUserId, payload.OccurredAtUtc, payload.Reason,
                operation.PayloadHash) &&
            ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_bookings
                WHERE booking_id=$booking
                  AND status='Cancelled'
                  AND current_version=$version
                  AND current_revision_id=$revision
                  AND fulfillment_state=$fulfillment
                  AND cancellation_reason=$reason;
                """,
                ("$booking", payload.BookingId.ToString("D")),
                ("$version", payload.ResultingVersion),
                ("$revision", payload.BookingRevisionId.ToString("D")),
                ("$fulfillment", payload.ToFulfillmentState),
                ("$reason", payload.Reason)) == 1;
    }

    private static bool VerifyDepositReceive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationRow operation,
        BookingDepositReceivedV1 payload)
    {
        return DepositOperationMatches(
                operation, payload.BookingId, payload.BookingRevisionId,
                payload.ActorUserId, payload.OccurredAtUtc) &&
            ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_deposit_facts
                WHERE deposit_fact_id=$fact
                  AND operation_id=$operation
                  AND booking_id=$booking
                  AND store_id=$store
                  AND booking_revision_id=$revision
                  AND fact_type='Received'
                  AND actor_user_id=$actor
                  AND amount_cents=$amount
                  AND occurred_at_utc=$at
                  AND reference=$reference
                  AND cashier_session_id=$session
                  AND cash_movement_id=$movement
                  AND sale_id IS NULL
                  AND payload_hash=$hash;
                """,
                ("$fact", payload.DepositFactId.ToString("D")),
                ("$operation", payload.OperationId.ToString("D")),
                ("$booking", payload.BookingId.ToString("D")),
                ("$store", payload.StoreId.ToString("D")),
                ("$revision", payload.BookingRevisionId.ToString("D")),
                ("$actor", payload.ActorUserId.ToString("D")),
                ("$amount", payload.AmountCents),
                ("$at", Utc(payload.OccurredAtUtc)),
                ("$reference", payload.Reference),
                ("$session", payload.CashierSessionId.ToString("D")),
                ("$movement", payload.CashMovementId.ToString("D")),
                ("$hash", operation.PayloadHash)) == 1 &&
            VerifyReservedCash(
                connection, transaction, payload.CashMovementId,
                payload.CashierSessionId, payload.ActorUserId,
                CashMovementType.CashIn, payload.AmountCents,
                CafeBookingCashReferenceTypes.DepositReceipt,
                payload.DepositFactId, payload.OccurredAtUtc) &&
            NoCafeCashOperation(connection, transaction, payload.CashMovementId);
    }

    private static bool VerifyDepositRefund(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationRow operation,
        BookingDepositRefundedV1 payload)
    {
        if (!DepositOperationMatches(
                operation, payload.BookingId, payload.BookingRevisionId,
                payload.ActorUserId, payload.OccurredAtUtc) ||
            ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_deposit_facts
                WHERE deposit_fact_id=$fact
                  AND operation_id=$operation
                  AND booking_id=$booking
                  AND store_id=$store
                  AND booking_revision_id=$revision
                  AND fact_type='Refunded'
                  AND actor_user_id=$actor
                  AND amount_cents=$amount
                  AND occurred_at_utc=$at
                  AND reference=$reference
                  AND cashier_session_id=$session
                  AND cash_movement_id=$movement
                  AND sale_id IS NULL
                  AND payload_hash=$hash;
                """,
                ("$fact", payload.DepositFactId.ToString("D")),
                ("$operation", payload.OperationId.ToString("D")),
                ("$booking", payload.BookingId.ToString("D")),
                ("$store", payload.StoreId.ToString("D")),
                ("$revision", payload.BookingRevisionId.ToString("D")),
                ("$actor", payload.ActorUserId.ToString("D")),
                ("$amount", payload.AmountCents),
                ("$at", Utc(payload.OccurredAtUtc)),
                ("$reference", payload.Reference),
                ("$session", payload.CashierSessionId.ToString("D")),
                ("$movement", payload.CashMovementId.ToString("D")),
                ("$hash", operation.PayloadHash)) != 1 ||
            !VerifyReservedCash(
                connection, transaction, payload.CashMovementId,
                payload.CashierSessionId, payload.ActorUserId,
                CashMovementType.CashOut, -payload.AmountCents,
                CafeBookingCashReferenceTypes.DepositRefund,
                payload.DepositFactId, payload.OccurredAtUtc) ||
            !NoCafeCashOperation(connection, transaction, payload.CashMovementId))
            return false;

        Dictionary<Guid,long> retained = ReadAllocations(
            connection, transaction, payload.DepositFactId);
        return retained.Count == payload.Allocations.Count &&
            payload.Allocations.All(x =>
                retained.TryGetValue(x.ReceivedDepositFactId, out long amount) &&
                amount == x.AmountCents);
    }

    private static bool VerifyOrderBound(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OperationRow operation,
        BookingFulfillmentOrderBoundV1 payload)
    {
        if (!OperationMatches(
                operation, payload.BookingId, payload.ExpectedBookingVersion,
                payload.ResultingBookingVersion, payload.BookingRevisionId,
                payload.ResultBookingStatus, payload.ResultFulfillmentState,
                payload.ActorUserId, payload.OccurredAtUtc))
            return false;

        if (ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_order_links
                WHERE booking_order_link_id=$link
                  AND booking_id=$booking
                  AND booking_revision_id=$revision
                  AND order_id=$order
                  AND operation_id=$operation
                  AND actor_user_id=$actor
                  AND occurred_at_utc=$at
                  AND content_hash=$hash;
                """,
                ("$link", payload.BookingOrderLinkId.ToString("D")),
                ("$booking", payload.BookingId.ToString("D")),
                ("$revision", payload.BookingRevisionId.ToString("D")),
                ("$order", payload.OrderId.ToString("D")),
                ("$operation", payload.OperationId.ToString("D")),
                ("$actor", payload.ActorUserId.ToString("D")),
                ("$at", Utc(payload.OccurredAtUtc)),
                ("$hash", operation.PayloadHash)) != 1 ||
            !EventMatches(
                connection, transaction, payload.OperationId, payload.BookingId,
                payload.ExpectedBookingVersion, payload.ResultingBookingVersion,
                CafeBookingActions.OrderBound,
                CafeBookingStatuses.ReadyForFulfillment,
                payload.ResultBookingStatus,
                CafeBookingFulfillmentStates.NotStarted,
                payload.ResultFulfillmentState,
                payload.BookingRevisionId, payload.OrderId, null,
                payload.ActorUserId, payload.OccurredAtUtc, null,
                operation.PayloadHash) ||
            !OrderSeedMatches(connection, transaction, payload))
            return false;

        BookingRow? booking = ReadBooking(connection, transaction, payload.BookingId);
        return booking is not null &&
            booking.Version >= payload.ResultingBookingVersion;
    }

    private static bool OrderSeedMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingFulfillmentOrderBoundV1 payload)
    {
        CafeOrderRevisionSnapshotV1 r = payload.OrderRevision;
        if (ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_orders
                WHERE order_id=$order
                  AND store_id=$store
                  AND menu_publication_revision_id=$menu
                  AND service_mode=$mode
                  AND current_version>=1;
                """,
                ("$order", payload.OrderId.ToString("D")),
                ("$store", payload.StoreId.ToString("D")),
                ("$menu", payload.MenuPublicationRevisionId),
                ("$mode", payload.IntendedServiceMode)) != 1)
            return false;

        if (ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_order_revisions
                WHERE revision_id=$revision
                  AND order_id=$order
                  AND revision_number=1
                  AND order_version=1
                  AND menu_publication_revision_id=$menu
                  AND service_mode=$mode
                  AND COALESCE(service_reference,'')=COALESCE($reference,'')
                  AND content_hash=$hash
                  AND actor_user_id=$actor
                  AND created_at_utc=$at;
                """,
                ("$revision", r.RevisionId.ToString("D")),
                ("$order", payload.OrderId.ToString("D")),
                ("$menu", r.MenuPublicationRevisionId),
                ("$mode", r.ServiceMode),
                ("$reference", r.ServiceReference ?? (object)DBNull.Value),
                ("$hash", r.ContentHash),
                ("$actor", r.ActorUserId.ToString("D")),
                ("$at", Utc(r.CreatedAtUtc))) != 1)
            return false;

        foreach (CafeOrderLineSnapshotV1 line in r.Lines)
        {
            if (ScalarLong(
                    connection, transaction,
                    """
                    SELECT COUNT(*)
                    FROM cafe_order_revision_lines
                    WHERE revision_id=$revision
                      AND order_line_id=$line
                      AND product_id=$product
                      AND product_name_snapshot=$name
                      AND unit_name_snapshot=$unit
                      AND quantity_x1000=$quantity
                      AND base_unit_price_cents=$base
                      AND COALESCE(variant_id,'')=COALESCE($variant,'')
                      AND COALESCE(variant_name_snapshot,'')=COALESCE($variantName,'')
                      AND COALESCE(variant_unit_price_cents,-1)=COALESCE($variantPrice,-1)
                      AND calculated_unit_price_cents=$quoted
                      AND line_total_cents=$total
                      AND display_order=$display
                      AND COALESCE(note,'')=COALESCE($note,'');
                    """,
                    ("$revision", r.RevisionId.ToString("D")),
                    ("$line", line.OrderLineId.ToString("D")),
                    ("$product", line.ProductId.ToString("D")),
                    ("$name", line.ProductNameSnapshot),
                    ("$unit", line.UnitNameSnapshot),
                    ("$quantity", line.QuantityX1000),
                    ("$base", line.BaseUnitPriceCents),
                    ("$variant", line.VariantId ?? (object)DBNull.Value),
                    ("$variantName", line.VariantNameSnapshot ?? (object)DBNull.Value),
                    ("$variantPrice", line.VariantUnitPriceCents ?? (object)DBNull.Value),
                    ("$quoted", line.CalculatedUnitPriceCents),
                    ("$total", line.LineTotalCents),
                    ("$display", line.DisplayOrder),
                    ("$note", line.Note ?? (object)DBNull.Value)) != 1)
                return false;

            foreach (CafeOrderModifierSnapshotV1 modifier in line.ModifierSelections)
            {
                if (ScalarLong(
                        connection, transaction,
                        """
                        SELECT COUNT(*)
                        FROM cafe_order_revision_modifiers
                        WHERE revision_id=$revision
                          AND order_line_id=$line
                          AND group_id=$group
                          AND group_name_snapshot=$groupName
                          AND option_id=$option
                          AND option_name_snapshot=$optionName
                          AND price_delta_cents=$price
                          AND display_order=$display;
                        """,
                        ("$revision", r.RevisionId.ToString("D")),
                        ("$line", line.OrderLineId.ToString("D")),
                        ("$group", modifier.GroupId),
                        ("$groupName", modifier.GroupNameSnapshot),
                        ("$option", modifier.OptionId),
                        ("$optionName", modifier.OptionNameSnapshot),
                        ("$price", modifier.PriceDeltaCents),
                        ("$display", modifier.DisplayOrder)) != 1)
                    return false;
            }
        }

        return true;
    }

    private static bool RevisionMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingRevisionSnapshotV1 r)
    {
        if (ScalarLong(
                connection, transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_revisions
                WHERE revision_id=$revision
                  AND booking_id=$booking
                  AND revision_number=$number
                  AND booking_version=$version
                  AND COALESCE(previous_revision_id,'')=COALESCE($previous,'')
                  AND customer_id=$customer
                  AND customer_name_snapshot=$name
                  AND COALESCE(customer_phone_snapshot,'')=COALESCE($phone,'')
                  AND COALESCE(customer_address_snapshot,'')=COALESCE($address,'')
                  AND booking_mode=$mode
                  AND event_name=$event
                  AND COALESCE(event_reference,'')=COALESCE($eventRef,'')
                  AND event_start_utc=$start
                  AND COALESCE(event_end_utc,'')=COALESCE($end,'')
                  AND COALESCE(location,'')=COALESCE($location,'')
                  AND COALESCE(setup_at_utc,'')=COALESCE($setup,'')
                  AND COALESCE(service_window_start_utc,'')=COALESCE($serviceStart,'')
                  AND COALESCE(service_window_end_utc,'')=COALESCE($serviceEnd,'')
                  AND COALESCE(headcount,-1)=COALESCE($headcount,-1)
                  AND COALESCE(package_label,'')=COALESCE($package,'')
                  AND menu_publication_revision_id=$menu
                  AND quoted_total_cents=$quoted
                  AND deposit_requirement_cents=$deposit
                  AND COALESCE(special_instructions,'')=COALESCE($instructions,'')
                  AND COALESCE(notes,'')=COALESCE($notes,'')
                  AND fulfillment_service_mode=$serviceMode
                  AND actor_user_id=$actor
                  AND created_at_utc=$at
                  AND content_hash=$hash;
                """,
                ("$revision", r.RevisionId.ToString("D")),
                ("$booking", r.BookingId.ToString("D")),
                ("$number", r.RevisionNumber),
                ("$version", r.BookingVersion),
                ("$previous", r.PreviousRevisionId?.ToString("D") ?? (object)DBNull.Value),
                ("$customer", r.CustomerId.ToString("D")),
                ("$name", r.CustomerNameSnapshot),
                ("$phone", r.CustomerPhoneSnapshot ?? (object)DBNull.Value),
                ("$address", r.CustomerAddressSnapshot ?? (object)DBNull.Value),
                ("$mode", r.BookingMode),
                ("$event", r.EventName),
                ("$eventRef", r.EventReference ?? (object)DBNull.Value),
                ("$start", Utc(r.EventStartUtc)),
                ("$end", r.EventEndUtc.HasValue ? Utc(r.EventEndUtc.Value) : DBNull.Value),
                ("$location", r.Location ?? (object)DBNull.Value),
                ("$setup", r.SetupAtUtc.HasValue ? Utc(r.SetupAtUtc.Value) : DBNull.Value),
                ("$serviceStart", r.ServiceWindowStartUtc.HasValue ? Utc(r.ServiceWindowStartUtc.Value) : DBNull.Value),
                ("$serviceEnd", r.ServiceWindowEndUtc.HasValue ? Utc(r.ServiceWindowEndUtc.Value) : DBNull.Value),
                ("$headcount", r.Headcount ?? (object)DBNull.Value),
                ("$package", r.PackageLabel ?? (object)DBNull.Value),
                ("$menu", r.MenuPublicationRevisionId),
                ("$quoted", r.QuotedTotalCents),
                ("$deposit", r.DepositRequirementCents),
                ("$instructions", r.SpecialInstructions ?? (object)DBNull.Value),
                ("$notes", r.Notes ?? (object)DBNull.Value),
                ("$serviceMode", r.IntendedServiceMode),
                ("$actor", r.ActorUserId.ToString("D")),
                ("$at", Utc(r.CreatedAtUtc)),
                ("$hash", r.ContentHash)) != 1)
            return false;

        foreach (BookingLineSnapshotV1 line in r.Lines)
        {
            if (ScalarLong(
                    connection, transaction,
                    """
                    SELECT COUNT(*)
                    FROM cafe_booking_revision_lines
                    WHERE revision_id=$revision
                      AND booking_line_id=$line
                      AND product_id=$product
                      AND product_name_snapshot=$name
                      AND unit_name_snapshot=$unit
                      AND quantity_x1000=$quantity
                      AND base_unit_price_cents=$base
                      AND COALESCE(variant_id,'')=COALESCE($variant,'')
                      AND COALESCE(variant_name_snapshot,'')=COALESCE($variantName,'')
                      AND COALESCE(variant_unit_price_cents,-1)=COALESCE($variantPrice,-1)
                      AND quoted_unit_price_cents=$quoted
                      AND line_total_cents=$total
                      AND display_order=$display
                      AND COALESCE(line_note,'')=COALESCE($note,'');
                    """,
                    ("$revision", r.RevisionId.ToString("D")),
                    ("$line", line.BookingLineId.ToString("D")),
                    ("$product", line.ProductId.ToString("D")),
                    ("$name", line.ProductNameSnapshot),
                    ("$unit", line.UnitNameSnapshot),
                    ("$quantity", line.QuantityX1000),
                    ("$base", line.BaseUnitPriceCents),
                    ("$variant", line.VariantId ?? (object)DBNull.Value),
                    ("$variantName", line.VariantNameSnapshot ?? (object)DBNull.Value),
                    ("$variantPrice", line.VariantUnitPriceCents ?? (object)DBNull.Value),
                    ("$quoted", line.QuotedUnitPriceCents),
                    ("$total", line.LineTotalCents),
                    ("$display", line.DisplayOrder),
                    ("$note", line.LineNote ?? (object)DBNull.Value)) != 1)
                return false;

            foreach (BookingModifierSelectionV1 modifier in line.ModifierSelections)
            {
                if (ScalarLong(
                        connection, transaction,
                        """
                        SELECT COUNT(*)
                        FROM cafe_booking_revision_modifier_selections
                        WHERE revision_id=$revision
                          AND booking_line_id=$line
                          AND group_id=$group
                          AND group_name_snapshot=$groupName
                          AND option_id=$option
                          AND option_name_snapshot=$optionName
                          AND price_delta_cents=$price
                          AND display_order=$display;
                        """,
                        ("$revision", r.RevisionId.ToString("D")),
                        ("$line", line.BookingLineId.ToString("D")),
                        ("$group", modifier.GroupId),
                        ("$groupName", modifier.GroupNameSnapshot),
                        ("$option", modifier.OptionId),
                        ("$optionName", modifier.OptionNameSnapshot),
                        ("$price", modifier.PriceDeltaCents),
                        ("$display", modifier.DisplayOrder)) != 1)
                    return false;
            }
        }

        return true;
    }

    private static bool OperationMatches(
        OperationRow operation,
        Guid bookingId,
        int expected,
        int resulting,
        Guid revisionId,
        string status,
        string fulfillment,
        Guid actor,
        DateTimeOffset at) =>
        operation.BookingId == bookingId &&
        operation.ExpectedVersion == expected &&
        operation.ResultingVersion == resulting &&
        operation.ResultRevisionId == revisionId &&
        operation.ResultStatus == status &&
        operation.ResultFulfillmentState == fulfillment &&
        operation.ActorUserId == actor &&
        operation.OccurredAtUtc.ToUniversalTime() == at.ToUniversalTime();

    private static bool DepositOperationMatches(
        OperationRow operation,
        Guid bookingId,
        Guid revisionId,
        Guid actor,
        DateTimeOffset at) =>
        operation.BookingId == bookingId &&
        operation.ResultRevisionId == revisionId &&
        operation.ExpectedVersion == operation.ResultingVersion &&
        operation.ActorUserId == actor &&
        operation.OccurredAtUtc.ToUniversalTime() == at.ToUniversalTime();

    private static bool EventMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid bookingId,
        int expectedVersion,
        int resultingVersion,
        string action,
        string? fromStatus,
        string toStatus,
        string? fromFulfillment,
        string toFulfillment,
        Guid? revisionId,
        Guid? orderId,
        Guid? saleId,
        Guid actor,
        DateTimeOffset at,
        string? reason,
        string hash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_id,expected_version,resulting_version,action,
                   from_status,to_status,from_fulfillment_state,to_fulfillment_state,
                   booking_revision_id,order_id,sale_id,actor_user_id,
                   occurred_at_utc,reason,content_hash
            FROM cafe_booking_events
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return false;
        return Guid.Parse(reader.GetString(0)) == bookingId &&
            reader.GetInt32(1) == expectedVersion &&
            reader.GetInt32(2) == resultingVersion &&
            string.Equals(reader.GetString(3), action, StringComparison.Ordinal) &&
            NullableStringEquals(reader, 4, fromStatus) &&
            string.Equals(reader.GetString(5), toStatus, StringComparison.Ordinal) &&
            NullableStringEquals(reader, 6, fromFulfillment) &&
            string.Equals(reader.GetString(7), toFulfillment, StringComparison.Ordinal) &&
            NullableGuidEquals(reader, 8, revisionId) &&
            NullableGuidEquals(reader, 9, orderId) &&
            NullableGuidEquals(reader, 10, saleId) &&
            Guid.Parse(reader.GetString(11)) == actor &&
            DateTimeOffset.Parse(reader.GetString(12)).ToUniversalTime() == at.ToUniversalTime() &&
            NullableStringEquals(reader, 13, reason) &&
            string.Equals(reader.GetString(14), hash, StringComparison.Ordinal);
    }

    private static bool VerifyReservedCash(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid movementId,
        Guid sessionId,
        Guid actorId,
        CashMovementType type,
        long delta,
        string referenceType,
        Guid referenceId,
        DateTimeOffset at) =>
        ScalarLong(
            connection, transaction,
            """
            SELECT COUNT(*)
            FROM cash_movements
            WHERE id=$id
              AND cashier_session_id=$session
              AND actor_user_id=$actor
              AND movement_type=$type
              AND delta_cents=$delta
              AND reference_type=$referenceType
              AND reference_id=$referenceId
              AND created_at_utc=$at;
            """,
            ("$id", movementId.ToString("D")),
            ("$session", sessionId.ToString("D")),
            ("$actor", actorId.ToString("D")),
            ("$type", Convert.ToInt32(type)),
            ("$delta", delta),
            ("$referenceType", referenceType),
            ("$referenceId", referenceId.ToString("D")),
            ("$at", Utc(at))) == 1;

    private static bool NoCafeCashOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid movementId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM cafe_cash_operations WHERE movement_id=$movement;",
            ("$movement", movementId.ToString("D"))) == 0;

    private static Dictionary<Guid,long> ReadAllocations(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actionFactId)
    {
        Dictionary<Guid,long> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT received_fact_id,amount_cents
            FROM cafe_booking_deposit_allocations
            WHERE action_fact_id=$fact
            ORDER BY received_fact_id;
            """;
        command.Parameters.AddWithValue("$fact", actionFactId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            result[Guid.Parse(reader.GetString(0))] = reader.GetInt64(1);
        return result;
    }

    private static Result ValidateEnvelope(
        SyncOperationEnvelopeV1 envelope,
        Guid operationId,
        Guid bookingId,
        Guid storeId,
        Guid actorUserId,
        long expectedVersion)
    {
        if (envelope.OperationId != operationId ||
            envelope.AggregateId != bookingId ||
            envelope.StoreId != storeId ||
            envelope.ActorUserId != actorUserId ||
            envelope.BaseVersion != expectedVersion)
            return Conflict(
                "CafeBookingSync.EnvelopeAuthorityMismatch",
                "C9 envelope operation/aggregate/store/actor/base-version differs from canonical payload.");
        return Result.Success();
    }

    private static Result ValidateEnvelopeWithoutCanonicalVersion(
        SyncOperationEnvelopeV1 envelope,
        Guid operationId,
        Guid bookingId,
        Guid storeId,
        Guid actorUserId)
    {
        if (envelope.OperationId != operationId ||
            envelope.AggregateId != bookingId ||
            envelope.StoreId != storeId ||
            envelope.ActorUserId != actorUserId ||
            !envelope.BaseVersion.HasValue ||
            envelope.BaseVersion.Value <= 0)
            return Conflict(
                "CafeBookingSync.EnvelopeAuthorityMismatch",
                "C9 deposit envelope operation/aggregate/store/actor/base-version is invalid.");
        return Result.Success();
    }

    private static Result ValidateSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        Guid actorId)
    {
        return ScalarLong(
            connection, transaction,
            """
            SELECT COUNT(*)
            FROM cashier_sessions
            WHERE id=$session AND user_id=$actor AND status=$open;
            """,
            ("$session", sessionId.ToString("D")),
            ("$actor", actorId.ToString("D")),
            ("$open", Convert.ToInt32(CashierSessionStatus.Open))) == 1
            ? Result.Success()
            : Conflict(
                "CafeBookingSync.CashierSessionPrerequisiteMissing",
                "C9 deposit apply requires the exact retained open cashier session.");
    }

    private static BookingRow? ReadBooking(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id,current_version,current_revision_id,status,fulfillment_state
            FROM cafe_bookings
            WHERE booking_id=$booking
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new BookingRow(
            Guid.Parse(reader.GetString(0)),
            reader.GetInt32(1),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4));
    }

    private static OperationRow? ReadOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_id,device_id,store_id,operation_type,payload_hash,
                   expected_version,resulting_version,result_revision_id,
                   result_status,result_fulfillment_state,actor_user_id,occurred_at_utc
            FROM cafe_booking_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new OperationRow(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
            reader.GetString(8),
            reader.GetString(9),
            Guid.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11)));
    }

    private static ReceivedDeposit? ReadReceivedDeposit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid factId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_id,store_id,amount_cents
            FROM cafe_booking_deposit_facts
            WHERE deposit_fact_id=$fact AND fact_type='Received'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$fact", factId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ReceivedDeposit(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt64(2))
            : null;
    }

    private static long ReadAllocatedTotal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid receivedFactId) =>
        ScalarLong(
            connection,
            transaction,
            """
            SELECT COALESCE(SUM(amount_cents),0)
            FROM cafe_booking_deposit_allocations
            WHERE received_fact_id=$received;
            """,
            ("$received", receivedFactId.ToString("D")));

    private static int ReadRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT revision_number FROM cafe_booking_revisions WHERE revision_id=$revision;";
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? throw new InvalidOperationException("Retained Booking revision disappeared.")
            : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool StoreMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM deployment_identity WHERE store_id=$store;",
            ("$store", storeId.ToString("D"))) == 1;

    private static bool ActorExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM users WHERE id=$actor;",
            ("$actor", actorId.ToString("D"))) == 1;

    private static bool BookingExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM cafe_bookings WHERE booking_id=$booking;",
            ("$booking", bookingId.ToString("D"))) != 0;

    private static bool RevisionExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM cafe_booking_revisions WHERE revision_id=$revision;",
            ("$revision", revisionId.ToString("D"))) != 0;

    private static bool BookingOperationExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM cafe_booking_operations WHERE operation_id=$operation;",
            ("$operation", operationId.ToString("D"))) != 0;

    private static bool DepositFactExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid factId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM cafe_booking_deposit_facts WHERE deposit_fact_id=$fact;",
            ("$fact", factId.ToString("D"))) != 0;

    private static bool CashMovementExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid movementId) =>
        ScalarLong(
            connection, transaction,
            "SELECT COUNT(*) FROM cash_movements WHERE id=$movement;",
            ("$movement", movementId.ToString("D"))) != 0;

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static string HashForEnvelope(SyncOperationEnvelopeV1 envelope) =>
        string.Equals(
            envelope.OperationType,
            BookingFulfillmentOrderBoundV1Contract.OperationType,
            StringComparison.Ordinal)
        ? BookingFulfillmentOrderBoundV1CanonicalJson.ComputeHash(envelope.PayloadJson)
        : CafeBookingCanonicalJson.ComputeHash(envelope.PayloadJson);

    private static Result InvalidPayload(string? code, string? message) =>
        Result.Fail(
            Error.Validation(
                code ?? "CafeBookingSync.PayloadInvalid",
                message ?? "Canonical C9 Booking payload is invalid."));

    private static Result Conflict(string code, string message) =>
        Result.Fail(Error.Conflict(code, message));

    private static object Db(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private static Guid DeterministicGuid(string scope, string identity)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(scope + "|" + identity));
        byte[] bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static bool NullableStringEquals(
        SqliteDataReader reader,
        int ordinal,
        string? expected) =>
        reader.IsDBNull(ordinal)
            ? expected is null
            : expected is not null &&
              string.Equals(reader.GetString(ordinal), expected, StringComparison.Ordinal);

    private static bool NullableGuidEquals(
        SqliteDataReader reader,
        int ordinal,
        Guid? expected) =>
        reader.IsDBNull(ordinal)
            ? !expected.HasValue
            : expected.HasValue &&
              Guid.Parse(reader.GetString(ordinal)) == expected.Value;

    private sealed record BookingRow(
        Guid StoreId,
        int Version,
        Guid CurrentRevisionId,
        string Status,
        string FulfillmentState);

    private sealed record OperationRow(
        Guid BookingId,
        Guid DeviceId,
        Guid StoreId,
        string OperationType,
        string PayloadHash,
        int ExpectedVersion,
        int ResultingVersion,
        Guid? ResultRevisionId,
        string ResultStatus,
        string ResultFulfillmentState,
        Guid ActorUserId,
        DateTimeOffset OccurredAtUtc);

    private sealed record ReceivedDeposit(
        Guid BookingId,
        Guid StoreId,
        long AmountCents);
}

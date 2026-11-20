using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeBookingService
{
    public Result<CafeBookingOrderBindResult> BindOrder(
        CafeBookingOrderBindMutation mutation)
    {
        if (mutation is null ||
            mutation.OperationId == Guid.Empty ||
            mutation.BookingId == Guid.Empty ||
            mutation.OrderId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.ExpectedBookingVersion <= 0)
        {
            return Fail<CafeBookingOrderBindResult>(
                Error.Validation(
                    "CafeBooking.OrderBindInvalid",
                    "Order binding requires stable identities and a positive ExpectedBookingVersion."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.BookingFulfill,
                SalrynPermissions.OrdersCreate);
            if (authority.IsFailure)
                return Rollback<CafeBookingOrderBindResult>(
                    transaction,
                    authority.ErrorDetail);

            Result<CafeBookingOrderBindResult>? replay =
                TryReplayOrderBind(connection, transaction, mutation);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (OutboxOperationExists(connection, transaction, mutation.OperationId))
                return Rollback<CafeBookingOrderBindResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.OperationIdConflict",
                        "Order-binding OperationId already exists without matching retained C9 operation evidence."));

            BookingRow booking = RequireBooking(
                connection,
                transaction,
                mutation.BookingId,
                mutation.StoreId,
                mutation.ExpectedBookingVersion);

            if (!string.Equals(
                    booking.Status,
                    CafeBookingStatuses.ReadyForFulfillment,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    booking.FulfillmentState,
                    CafeBookingFulfillmentStates.NotStarted,
                    StringComparison.Ordinal))
                return Rollback<CafeBookingOrderBindResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.OrderBindStateInvalid",
                        "Booking must be ReadyForFulfillment / NotStarted before Order binding."));

            if (BookingOrderLinkExists(connection, transaction, mutation.BookingId) ||
                BookingSaleLinkExists(connection, transaction, mutation.BookingId))
                return Rollback<CafeBookingOrderBindResult>(
                    transaction,
                    Error.Conflict(
                        "CafeBooking.OrderAlreadyBound",
                        "Booking already owns an Order or final Sale link."));

            CafeBookingRevisionSnapshot revision =
                ReadRevision(
                    connection,
                    transaction,
                    booking.CurrentRevisionId);

            Result<CafeBookingHistoricalOrderSeedResult> seeded =
                SqliteCafeBookingHistoricalOrderParticipant.Seed(
                    connection,
                    transaction,
                    mutation,
                    revision);
            if (seeded.IsFailure)
                return Rollback<CafeBookingOrderBindResult>(
                    transaction,
                    seeded.ErrorDetail);

            int resultingVersion =
                checked(mutation.ExpectedBookingVersion + 1);
            Guid bookingOrderLinkId =
                SqliteCafeOrderStore.DeterministicGuid(
                    "salryn-c9-booking-order-link-v1",
                    mutation.OperationId.ToString("D"));

            BookingFulfillmentOrderBoundV1 payload = new(
                BookingFulfillmentOrderBoundV1Contract.OperationType,
                mutation.OperationId,
                mutation.BookingId,
                revision.RevisionId,
                bookingOrderLinkId,
                mutation.ExpectedBookingVersion,
                resultingVersion,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                mutation.OrderId,
                revision.MenuPublicationRevisionId,
                revision.IntendedServiceMode,
                seeded.Value.ContractRevision,
                CafeBookingStatuses.ReadyForFulfillment,
                CafeBookingFulfillmentStates.OrderBound);

            string json =
                BookingFulfillmentOrderBoundV1CanonicalJson.Serialize(payload);
            BookingFulfillmentOrderBoundParseResult parsed =
                BookingFulfillmentOrderBoundV1CanonicalJson.Parse(json);
            if (!parsed.IsValid)
                throw new BookingValidationException(
                    parsed.ErrorCode ?? "CafeBooking.OrderBindPayloadInvalid",
                    parsed.ErrorMessage ?? "Booking order-binding payload is invalid.");

            string payloadHash =
                BookingFulfillmentOrderBoundV1CanonicalJson.ComputeHash(json);

            InsertOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.BookingId,
                mutation.DeviceId,
                mutation.StoreId,
                BookingFulfillmentOrderBoundV1Contract.OperationType,
                payloadHash,
                mutation.ExpectedBookingVersion,
                resultingVersion,
                revision.RevisionId,
                CafeBookingStatuses.ReadyForFulfillment,
                CafeBookingFulfillmentStates.OrderBound,
                mutation.ActorUserId,
                mutation.OccurredAtUtc);

            using (SqliteCommand link = connection.CreateCommand())
            {
                link.Transaction = transaction;
                link.CommandText = """
                    INSERT INTO cafe_booking_order_links(
                        booking_order_link_id,booking_id,booking_revision_id,
                        order_id,operation_id,actor_user_id,occurred_at_utc,content_hash)
                    VALUES(
                        $link,$booking,$revision,$order,$operation,$actor,$at,$hash);
                    """;
                link.Parameters.AddWithValue("$link", bookingOrderLinkId.ToString("D"));
                link.Parameters.AddWithValue("$booking", mutation.BookingId.ToString("D"));
                link.Parameters.AddWithValue("$revision", revision.RevisionId.ToString("D"));
                link.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
                link.Parameters.AddWithValue("$operation", mutation.OperationId.ToString("D"));
                link.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                link.Parameters.AddWithValue("$at", Utc(mutation.OccurredAtUtc));
                link.Parameters.AddWithValue("$hash", payloadHash);
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
                      AND store_id=$store
                      AND current_version=$expected
                      AND current_revision_id=$revision
                      AND status='ReadyForFulfillment'
                      AND fulfillment_state='NotStarted';
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                update.Parameters.AddWithValue("$at", Utc(mutation.OccurredAtUtc));
                update.Parameters.AddWithValue("$booking", mutation.BookingId.ToString("D"));
                update.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedBookingVersion);
                update.Parameters.AddWithValue("$revision", revision.RevisionId.ToString("D"));
                if (update.ExecuteNonQuery() != 1)
                    throw new BookingValidationException(
                        "CafeBooking.OrderBindConcurrentConflict",
                        "Booking lost order-binding ownership.");
            }

            InsertEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.BookingId,
                mutation.ExpectedBookingVersion,
                resultingVersion,
                CafeBookingActions.OrderBound,
                CafeBookingStatuses.ReadyForFulfillment,
                CafeBookingStatuses.ReadyForFulfillment,
                CafeBookingFulfillmentStates.NotStarted,
                CafeBookingFulfillmentStates.OrderBound,
                revision.RevisionId,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                null,
                payloadHash,
                orderId: mutation.OrderId);

            Enqueue(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.OperationId,
                mutation.BookingId,
                mutation.ExpectedBookingVersion,
                BookingFulfillmentOrderBoundV1Contract.OperationType,
                mutation.OccurredAtUtc,
                json);

            transaction.Commit();

            return Result<CafeBookingOrderBindResult>.Success(
                new CafeBookingOrderBindResult(
                    mutation.BookingId,
                    revision.RevisionId,
                    bookingOrderLinkId,
                    mutation.OrderId,
                    seeded.Value.Revision.RevisionId,
                    resultingVersion,
                    CafeBookingStatuses.ReadyForFulfillment,
                    CafeBookingFulfillmentStates.OrderBound,
                    false));
        }
        catch (BookingValidationException ex)
        {
            return Fail<CafeBookingOrderBindResult>(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Fail<CafeBookingOrderBindResult>(
                Error.Conflict(
                    "CafeBooking.OrderBindConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (ExpectedException(ex))
        {
            return Fail<CafeBookingOrderBindResult>(
                Error.Unexpected(
                    "CafeBooking.OrderBindFailed",
                    ex.Message));
        }
    }

    private Result<CafeBookingOrderBindResult>? TryReplayOrderBind(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingOrderBindMutation mutation)
    {
        OperationRow? operation =
            ReadOperation(connection, transaction, mutation.OperationId);
        if (operation is null)
            return null;

        SyncOutboxItem? outbox =
            _outbox.Find(connection, transaction, mutation.OperationId);
        if (outbox is null)
            return Fail<CafeBookingOrderBindResult>(
                Error.Unexpected(
                    "CafeBooking.ReplayOutboxMissing",
                    "Retained Booking order-binding operation is missing its semantic outbox evidence."));

        BookingFulfillmentOrderBoundParseResult parsed =
            BookingFulfillmentOrderBoundV1CanonicalJson.Parse(
                outbox.Envelope.PayloadJson);
        BookingFulfillmentOrderBoundV1? payload = parsed.Payload;

        if (!parsed.IsValid ||
            payload is null ||
            operation.DeviceId != mutation.DeviceId ||
            operation.BookingId != mutation.BookingId ||
            operation.StoreId != mutation.StoreId ||
            operation.ExpectedVersion != mutation.ExpectedBookingVersion ||
            !string.Equals(
                operation.OperationType,
                BookingFulfillmentOrderBoundV1Contract.OperationType,
                StringComparison.Ordinal) ||
            payload.OperationId != mutation.OperationId ||
            payload.OrderId != mutation.OrderId ||
            payload.ActorUserId != mutation.ActorUserId ||
            payload.OccurredAtUtc != mutation.OccurredAtUtc.ToUniversalTime() ||
            !string.Equals(
                operation.PayloadHash,
                outbox.Envelope.PayloadHash,
                StringComparison.Ordinal))
            return Fail<CafeBookingOrderBindResult>(
                Error.Conflict(
                    "CafeBooking.OperationPayloadConflict",
                    "Order-binding OperationId already exists with different canonical evidence."));

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT booking_revision_id,booking_order_link_id,order_id,content_hash
            FROM cafe_booking_order_links
            WHERE operation_id=$operation
              AND booking_id=$booking
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", mutation.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$booking", mutation.BookingId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return Fail<CafeBookingOrderBindResult>(
                Error.Unexpected(
                    "CafeBooking.OrderBindEvidenceMissing",
                    "Order-binding replay is missing the immutable Booking to Order link."));

        Guid bookingRevisionId = Guid.Parse(reader.GetString(0));
        Guid linkId = Guid.Parse(reader.GetString(1));
        Guid orderId = Guid.Parse(reader.GetString(2));
        string linkHash = reader.GetString(3);
        reader.Close();

        if (bookingRevisionId != payload.BookingRevisionId ||
            linkId != payload.BookingOrderLinkId ||
            orderId != payload.OrderId ||
            !string.Equals(linkHash, operation.PayloadHash, StringComparison.Ordinal) ||
            !OrderSeedEvidenceMatches(
                connection,
                transaction,
                payload))
            return Fail<CafeBookingOrderBindResult>(
                Error.Conflict(
                    "CafeBooking.OrderBindEvidenceConflict",
                    "Retained Booking/C4 order evidence differs from the canonical binding payload."));

        return Result<CafeBookingOrderBindResult>.Success(
            new CafeBookingOrderBindResult(
                payload.BookingId,
                payload.BookingRevisionId,
                payload.BookingOrderLinkId,
                payload.OrderId,
                payload.OrderRevision.RevisionId,
                payload.ResultingBookingVersion,
                payload.ResultBookingStatus,
                payload.ResultFulfillmentState,
                true));
    }

    private static bool OrderSeedEvidenceMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookingFulfillmentOrderBoundV1 payload)
    {
        SqliteCafeOrderStore.OrderRow? order =
            SqliteCafeOrderStore.ReadOrder(
                connection,
                transaction,
                payload.OrderId);
        if (order is null ||
            order.StoreId != payload.StoreId ||
            order.CurrentVersion != 1 ||
            order.CurrentRevisionId != payload.OrderRevision.RevisionId ||
            !string.Equals(order.MenuPublicationRevisionId, payload.MenuPublicationRevisionId, StringComparison.Ordinal) ||
            !string.Equals(order.ServiceMode, payload.IntendedServiceMode, StringComparison.Ordinal))
            return false;

        CafeOrderRevisionSnapshot revision =
            SqliteCafeOrderStore.ReadRevision(
                connection,
                transaction,
                order.CurrentRevisionId);
        CafeOrderRevisionSnapshotV1 retained =
            SqliteCafeOrderStore.ToContractRevision(revision);

        return string.Equals(
            System.Text.Json.JsonSerializer.Serialize(retained),
            System.Text.Json.JsonSerializer.Serialize(payload.OrderRevision),
            StringComparison.Ordinal);
    }


    private static bool TryReadCancelledBoundOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId,
        out Guid? orderId)
    {
        orderId = null;

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT l.order_id,o.state,o.completed_sale_id,
                   CASE WHEN s.booking_id IS NULL THEN 0 ELSE 1 END
            FROM cafe_booking_order_links l
            INNER JOIN cafe_orders o
              ON o.order_id=l.order_id
            LEFT JOIN cafe_booking_sale_links s
              ON s.booking_id=l.booking_id
            WHERE l.booking_id=$booking
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        Guid retainedOrderId = Guid.Parse(reader.GetString(0));
        string state = reader.GetString(1);
        bool completedSale = !reader.IsDBNull(2);
        bool finalBookingSale = reader.GetInt32(3) != 0;
        reader.Close();

        if (!string.Equals(state, CafeOrderStates.Cancelled, StringComparison.Ordinal) ||
            completedSale ||
            finalBookingSale)
            return false;

        orderId = retainedOrderId;
        return true;
    }

    private static bool BookingOrderLinkExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM cafe_booking_order_links WHERE booking_id=$booking;";
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static bool BookingSaleLinkExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid bookingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM cafe_booking_sale_links WHERE booking_id=$booking;";
        command.Parameters.AddWithValue("$booking", bookingId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }
}

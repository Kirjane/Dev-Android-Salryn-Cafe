using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

internal sealed record CafeBookingFinalizationAllocation(
    Guid ReceivedDepositFactId,
    long AmountCents);

internal sealed record CafeBookingFinalizationContext(
    Guid BookingId,
    Guid BookingRevisionId,
    Guid BookingOrderLinkId,
    Guid OrderId,
    int ExpectedBookingVersion,
    int ResultingBookingVersion,
    Guid BookingSaleLinkId,
    long BookingDepositAppliedCents,
    Guid? DepositApplicationFactId,
    IReadOnlyList<CafeBookingFinalizationAllocation> Allocations);

internal static class SqliteCafeBookingFinalizationParticipant
{
    internal static Result<CafeBookingFinalizationContext> Prepare(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingFinalizationBinding binding,
        Guid orderId,
        Guid storeId,
        Guid saleCommittedOperationId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.BookingId == Guid.Empty ||
            binding.BookingRevisionId == Guid.Empty ||
            binding.BookingOrderLinkId == Guid.Empty ||
            binding.ExpectedBookingVersion <= 0 ||
            orderId == Guid.Empty ||
            storeId == Guid.Empty ||
            saleCommittedOperationId == Guid.Empty)
            return Result<CafeBookingFinalizationContext>.Fail(
                Error.Validation(
                    "CafeBooking.FinalizationBindingInvalid",
                    "C9 finalization requires stable Booking/link/order identities and positive ExpectedBookingVersion."));

        using (SqliteCommand booking = connection.CreateCommand())
        {
            booking.Transaction = transaction;
            booking.CommandText = """
                SELECT store_id,current_version,current_revision_id,status,fulfillment_state
                FROM cafe_bookings
                WHERE booking_id=$booking
                LIMIT 1;
                """;
            booking.Parameters.AddWithValue("$booking", binding.BookingId.ToString("D"));
            using SqliteDataReader reader = booking.ExecuteReader();
            if (!reader.Read())
                return Result<CafeBookingFinalizationContext>.Fail(
                    Error.NotFound(
                        "CafeBooking.BookingMissing",
                        "C9 finalization Booking was not found."));

            Guid retainedStore = Guid.Parse(reader.GetString(0));
            int version = reader.GetInt32(1);
            Guid revision = Guid.Parse(reader.GetString(2));
            string status = reader.GetString(3);
            string fulfillment = reader.GetString(4);
            reader.Close();

            if (retainedStore != storeId ||
                version != binding.ExpectedBookingVersion ||
                revision != binding.BookingRevisionId ||
                status != CafeBookingStatuses.ReadyForFulfillment ||
                fulfillment != CafeBookingFulfillmentStates.OrderBound)
                return Result<CafeBookingFinalizationContext>.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationStateConflict",
                        "C9 finalization requires exact ReadyForFulfillment / OrderBound Booking version and frozen revision."));
        }

        using (SqliteCommand link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                SELECT order_id
                FROM cafe_booking_order_links
                WHERE booking_order_link_id=$link
                  AND booking_id=$booking
                  AND booking_revision_id=$revision
                LIMIT 1;
                """;
            link.Parameters.AddWithValue("$link", binding.BookingOrderLinkId.ToString("D"));
            link.Parameters.AddWithValue("$booking", binding.BookingId.ToString("D"));
            link.Parameters.AddWithValue("$revision", binding.BookingRevisionId.ToString("D"));
            object? retained = link.ExecuteScalar();
            if (retained is not string text ||
                !Guid.TryParse(text, out Guid linkedOrder) ||
                linkedOrder != orderId)
                return Result<CafeBookingFinalizationContext>.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationOrderLinkConflict",
                        "C9 finalization Order does not match the immutable Booking to Order link."));
        }

        if (ScalarLong(
                connection,
                transaction,
                "SELECT COUNT(*) FROM cafe_booking_sale_links WHERE booking_id=$booking;",
                ("$booking", binding.BookingId.ToString("D"))) != 0)
            return Result<CafeBookingFinalizationContext>.Fail(
                Error.Conflict(
                    "CafeBooking.FinalSaleAlreadyBound",
                    "Booking already owns a final Sale link."));

        if (ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM cafe_booking_deposit_facts
                WHERE booking_id=$booking AND fact_type='Applied';
                """,
                ("$booking", binding.BookingId.ToString("D"))) != 0)
            return Result<CafeBookingFinalizationContext>.Fail(
                Error.Conflict(
                    "CafeBooking.DepositAlreadyApplied",
                    "Booking already owns a deposit application fact."));

        List<CafeBookingFinalizationAllocation> allocations = new();
        long available = 0;

        using (SqliteCommand deposits = connection.CreateCommand())
        {
            deposits.Transaction = transaction;
            deposits.CommandText = """
                SELECT f.deposit_fact_id,f.amount_cents,f.occurred_at_utc,
                       COALESCE(SUM(a.amount_cents),0)
                FROM cafe_booking_deposit_facts f
                LEFT JOIN cafe_booking_deposit_allocations a
                  ON a.received_fact_id=f.deposit_fact_id
                WHERE f.booking_id=$booking
                  AND f.store_id=$store
                  AND f.fact_type='Received'
                GROUP BY f.deposit_fact_id,f.amount_cents,f.occurred_at_utc
                ORDER BY f.occurred_at_utc,f.deposit_fact_id;
                """;
            deposits.Parameters.AddWithValue("$booking", binding.BookingId.ToString("D"));
            deposits.Parameters.AddWithValue("$store", storeId.ToString("D"));
            using SqliteDataReader reader = deposits.ExecuteReader();
            while (reader.Read())
            {
                Guid factId = Guid.Parse(reader.GetString(0));
                long received = reader.GetInt64(1);
                long consumed = reader.GetInt64(3);
                long remaining = checked(received - consumed);
                if (remaining < 0)
                    return Result<CafeBookingFinalizationContext>.Fail(
                        Error.Conflict(
                            "CafeBooking.DepositAllocationCorrupt",
                            "Retained deposit allocations exceed their Received fact."));
                if (remaining == 0)
                    continue;

                allocations.Add(new CafeBookingFinalizationAllocation(
                    factId,
                    remaining));
                available = checked(available + remaining);
            }
        }

        Guid saleLinkId = SqliteCafeOrderStore.DeterministicGuid(
            "salryn-c9-booking-sale-link-v1",
            saleCommittedOperationId.ToString("D"));
        Guid? applicationFactId = available > 0
            ? SqliteCafeOrderStore.DeterministicGuid(
                "salryn-c9-booking-deposit-application-v1",
                saleCommittedOperationId.ToString("D"))
            : null;

        return Result<CafeBookingFinalizationContext>.Success(
            new CafeBookingFinalizationContext(
                binding.BookingId,
                binding.BookingRevisionId,
                binding.BookingOrderLinkId,
                orderId,
                binding.ExpectedBookingVersion,
                checked(binding.ExpectedBookingVersion + 1),
                saleLinkId,
                available,
                applicationFactId,
                allocations));
    }

    internal static SaleCommittedV3 BuildPayload(
        CafeBookingFinalizationContext context,
        CompleteCheckoutSaleRequest salePackage,
        CafeSaleFinancialDecision decision)
    {
        SaleCommittedV1 saleEvidence =
            SaleCommittedV1Mapper.FromCore(salePackage);

        return new SaleCommittedV3(
            SaleCommittedV3Contract.OperationType,
            saleEvidence,
            decision.OperationId,
            decision.FinancialDecisionId,
            context.OrderId,
            decision.OrderRevisionId ??
                throw new InvalidOperationException(
                    "C9 SaleCommittedV3 requires immutable C4 OrderRevisionId."),
            decision.ExpectedOrderVersion ??
                throw new InvalidOperationException(
                    "C9 SaleCommittedV3 requires ExpectedOrderVersion."),
            decision.CreatedAtUtc,
            decision.ContentHash,
            decision.DiscountReason,
            decision.FeeTotalCents,
            decision.LineDecisions.Select(x =>
                new SaleCommittedV2LineDiscount(
                    x.SaleItemId,
                    x.LineDiscountCents,
                    x.OrderDiscountAllocatedCents,
                    x.TotalDiscountCents)).ToArray(),
            decision.Fees.Select(x =>
                new SaleCommittedV2Fee(
                    x.FeeId,
                    x.FeeType,
                    x.DisplayLabelSnapshot,
                    x.AmountCents,
                    x.ActorUserId,
                    x.Reason,
                    x.CreatedAtUtc)).ToArray(),
            context.BookingId,
            context.BookingRevisionId,
            context.BookingOrderLinkId,
            context.ExpectedBookingVersion,
            context.ResultingBookingVersion,
            context.BookingSaleLinkId,
            context.BookingDepositAppliedCents,
            context.DepositApplicationFactId,
            context.Allocations.Select(x =>
                new SaleCommittedV3DepositAllocation(
                    x.ReceivedDepositFactId,
                    x.AmountCents)).ToArray(),
            CafeBookingStatuses.Fulfilled,
            CafeBookingFulfillmentStates.SaleBound);
    }

    internal static Result Persist(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingFinalizationContext context,
        SaleCommittedV3 payload,
        Guid saleCommittedOperationId,
        Guid storeId,
        Guid deviceId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string payloadHash)
    {
        if (payload.BookingId != context.BookingId ||
            payload.BookingRevisionId != context.BookingRevisionId ||
            payload.BookingOrderLinkId != context.BookingOrderLinkId ||
            payload.BookingSaleLinkId != context.BookingSaleLinkId ||
            payload.BookingDepositAppliedCents != context.BookingDepositAppliedCents ||
            payload.DepositApplicationFactId != context.DepositApplicationFactId)
            return Result.Fail(
                Error.Conflict(
                    "CafeBooking.FinalizationPayloadConflict",
                    "SaleCommittedV3 payload does not match prepared C9 finalization authority."));

        if (context.BookingDepositAppliedCents > 0)
        {
            using SqliteCommand fact = connection.CreateCommand();
            fact.Transaction = transaction;
            fact.CommandText = """
                INSERT INTO cafe_booking_deposit_facts(
                    deposit_fact_id,operation_id,booking_id,store_id,booking_revision_id,
                    fact_type,actor_user_id,amount_cents,occurred_at_utc,reference,
                    cashier_session_id,cash_movement_id,sale_id,payload_hash)
                VALUES(
                    $fact,$operation,$booking,$store,$revision,
                    'Applied',$actor,$amount,$at,$reference,
                    NULL,NULL,$sale,$hash);
                """;
            fact.Parameters.AddWithValue("$fact", context.DepositApplicationFactId!.Value.ToString("D"));
            fact.Parameters.AddWithValue("$operation", saleCommittedOperationId.ToString("D"));
            fact.Parameters.AddWithValue("$booking", context.BookingId.ToString("D"));
            fact.Parameters.AddWithValue("$store", storeId.ToString("D"));
            fact.Parameters.AddWithValue("$revision", context.BookingRevisionId.ToString("D"));
            fact.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            fact.Parameters.AddWithValue("$amount", context.BookingDepositAppliedCents);
            fact.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            fact.Parameters.AddWithValue("$reference", "SALE:" + payload.SaleEvidence.Sale.Id.ToString("D"));
            fact.Parameters.AddWithValue("$sale", payload.SaleEvidence.Sale.Id.ToString("D"));
            fact.Parameters.AddWithValue("$hash", payloadHash);
            fact.ExecuteNonQuery();

            foreach (CafeBookingFinalizationAllocation allocation in context.Allocations)
            {
                using SqliteCommand row = connection.CreateCommand();
                row.Transaction = transaction;
                row.CommandText = """
                    INSERT INTO cafe_booking_deposit_allocations(
                        action_fact_id,received_fact_id,amount_cents)
                    VALUES($action,$received,$amount);
                    """;
                row.Parameters.AddWithValue("$action", context.DepositApplicationFactId.Value.ToString("D"));
                row.Parameters.AddWithValue("$received", allocation.ReceivedDepositFactId.ToString("D"));
                row.Parameters.AddWithValue("$amount", allocation.AmountCents);
                row.ExecuteNonQuery();
            }
        }

        using (SqliteCommand operation = connection.CreateCommand())
        {
            operation.Transaction = transaction;
            operation.CommandText = """
                INSERT INTO cafe_booking_operations(
                    operation_id,booking_id,device_id,store_id,operation_type,payload_hash,
                    expected_version,resulting_version,result_revision_id,result_status,
                    result_fulfillment_state,actor_user_id,occurred_at_utc)
                VALUES(
                    $operation,$booking,$device,$store,'SaleCommittedV3',$hash,
                    $expected,$resulting,$revision,'Fulfilled',
                    'SaleBound',$actor,$at);
                """;
            operation.Parameters.AddWithValue("$operation", saleCommittedOperationId.ToString("D"));
            operation.Parameters.AddWithValue("$booking", context.BookingId.ToString("D"));
            operation.Parameters.AddWithValue("$device", deviceId.ToString("D"));
            operation.Parameters.AddWithValue("$store", storeId.ToString("D"));
            operation.Parameters.AddWithValue("$hash", payloadHash);
            operation.Parameters.AddWithValue("$expected", context.ExpectedBookingVersion);
            operation.Parameters.AddWithValue("$resulting", context.ResultingBookingVersion);
            operation.Parameters.AddWithValue("$revision", context.BookingRevisionId.ToString("D"));
            operation.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            operation.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            operation.ExecuteNonQuery();
        }

        using (SqliteCommand link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO cafe_booking_sale_links(
                    booking_sale_link_id,booking_id,booking_revision_id,
                    booking_order_link_id,order_id,sale_id,deposit_application_fact_id,
                    operation_id,actor_user_id,occurred_at_utc,content_hash)
                VALUES(
                    $link,$booking,$revision,
                    $orderLink,$order,$sale,$deposit,
                    $operation,$actor,$at,$hash);
                """;
            link.Parameters.AddWithValue("$link", context.BookingSaleLinkId.ToString("D"));
            link.Parameters.AddWithValue("$booking", context.BookingId.ToString("D"));
            link.Parameters.AddWithValue("$revision", context.BookingRevisionId.ToString("D"));
            link.Parameters.AddWithValue("$orderLink", context.BookingOrderLinkId.ToString("D"));
            link.Parameters.AddWithValue("$order", context.OrderId.ToString("D"));
            link.Parameters.AddWithValue("$sale", payload.SaleEvidence.Sale.Id.ToString("D"));
            link.Parameters.AddWithValue(
                "$deposit",
                context.DepositApplicationFactId.HasValue
                    ? context.DepositApplicationFactId.Value.ToString("D")
                    : DBNull.Value);
            link.Parameters.AddWithValue("$operation", saleCommittedOperationId.ToString("D"));
            link.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            link.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            link.Parameters.AddWithValue("$hash", payloadHash);
            link.ExecuteNonQuery();
        }

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_bookings
                SET current_version=$resulting,
                    status='Fulfilled',
                    fulfillment_state='SaleBound',
                    updated_by_user_id=$actor,
                    updated_at_utc=$at
                WHERE booking_id=$booking
                  AND current_version=$expected
                  AND current_revision_id=$revision
                  AND status='ReadyForFulfillment'
                  AND fulfillment_state='OrderBound';
                """;
            update.Parameters.AddWithValue("$resulting", context.ResultingBookingVersion);
            update.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            update.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$booking", context.BookingId.ToString("D"));
            update.Parameters.AddWithValue("$expected", context.ExpectedBookingVersion);
            update.Parameters.AddWithValue("$revision", context.BookingRevisionId.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationConcurrentConflict",
                        "Booking lost finalization ownership."));
        }

        using (SqliteCommand evt = connection.CreateCommand())
        {
            evt.Transaction = transaction;
            evt.CommandText = """
                INSERT INTO cafe_booking_events(
                    event_id,operation_id,booking_id,expected_version,resulting_version,
                    action,from_status,to_status,from_fulfillment_state,to_fulfillment_state,
                    booking_revision_id,order_id,sale_id,actor_user_id,occurred_at_utc,
                    reason,content_hash)
                VALUES(
                    $event,$operation,$booking,$expected,$resulting,
                    'Fulfilled','ReadyForFulfillment','Fulfilled','OrderBound','SaleBound',
                    $revision,$order,$sale,$actor,$at,NULL,$hash);
                """;
            evt.Parameters.AddWithValue(
                "$event",
                SqliteCafeOrderStore.DeterministicGuid(
                    "salryn-c9-booking-event-v1",
                    saleCommittedOperationId.ToString("D")).ToString("D"));
            evt.Parameters.AddWithValue("$operation", saleCommittedOperationId.ToString("D"));
            evt.Parameters.AddWithValue("$booking", context.BookingId.ToString("D"));
            evt.Parameters.AddWithValue("$expected", context.ExpectedBookingVersion);
            evt.Parameters.AddWithValue("$resulting", context.ResultingBookingVersion);
            evt.Parameters.AddWithValue("$revision", context.BookingRevisionId.ToString("D"));
            evt.Parameters.AddWithValue("$order", context.OrderId.ToString("D"));
            evt.Parameters.AddWithValue("$sale", payload.SaleEvidence.Sale.Id.ToString("D"));
            evt.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            evt.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            evt.Parameters.AddWithValue("$hash", payloadHash);
            evt.ExecuteNonQuery();
        }

        return Result.Success();
    }

    internal static Result VerifyReplay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV3 payload,
        string payloadHash)
    {
        using (SqliteCommand booking = connection.CreateCommand())
        {
            booking.Transaction = transaction;
            booking.CommandText = """
                SELECT current_version,current_revision_id,status,fulfillment_state
                FROM cafe_bookings
                WHERE booking_id=$booking
                LIMIT 1;
                """;
            booking.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            using SqliteDataReader reader = booking.ExecuteReader();
            if (!reader.Read() ||
                reader.GetInt32(0) != payload.ResultingBookingVersion ||
                Guid.Parse(reader.GetString(1)) != payload.BookingRevisionId ||
                reader.GetString(2) != CafeBookingStatuses.Fulfilled ||
                reader.GetString(3) != CafeBookingFulfillmentStates.SaleBound)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationReplayStateMissing",
                        "Retained V3 Booking terminal projection is missing or changed."));
        }

        using (SqliteCommand link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                SELECT booking_revision_id,booking_order_link_id,order_id,sale_id,
                       deposit_application_fact_id,content_hash
                FROM cafe_booking_sale_links
                WHERE booking_sale_link_id=$link
                  AND booking_id=$booking
                LIMIT 1;
                """;
            link.Parameters.AddWithValue("$link", payload.BookingSaleLinkId.ToString("D"));
            link.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            using SqliteDataReader reader = link.ExecuteReader();
            if (!reader.Read() ||
                Guid.Parse(reader.GetString(0)) != payload.BookingRevisionId ||
                Guid.Parse(reader.GetString(1)) != payload.BookingOrderLinkId ||
                Guid.Parse(reader.GetString(2)) != payload.OrderId ||
                Guid.Parse(reader.GetString(3)) != payload.SaleEvidence.Sale.Id ||
                (reader.IsDBNull(4)
                    ? payload.DepositApplicationFactId.HasValue
                    : !payload.DepositApplicationFactId.HasValue ||
                      Guid.Parse(reader.GetString(4)) != payload.DepositApplicationFactId.Value) ||
                !string.Equals(reader.GetString(5), payloadHash, StringComparison.Ordinal))
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationReplayLinkMissing",
                        "Retained V3 Booking to Sale link is missing or changed."));
        }

        long operationCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_booking_operations
            WHERE operation_id=$operation
              AND booking_id=$booking
              AND operation_type='SaleCommittedV3'
              AND payload_hash=$hash
              AND expected_version=$expected
              AND resulting_version=$resulting
              AND result_status='Fulfilled'
              AND result_fulfillment_state='SaleBound';
            """,
            ("$operation", payload.SaleEvidence.Sale.Id == Guid.Empty
                ? string.Empty
                : ReadSaleCommittedOperationId(connection, transaction, payload.BookingSaleLinkId).ToString("D")),
            ("$booking", payload.BookingId.ToString("D")),
            ("$hash", payloadHash),
            ("$expected", payload.ExpectedBookingVersion),
            ("$resulting", payload.ResultingBookingVersion));

        if (operationCount != 1)
            return Result.Fail(
                Error.Conflict(
                    "CafeBooking.FinalizationReplayOperationMissing",
                    "Retained V3 C9 operation evidence is missing."));

        if (payload.BookingDepositAppliedCents == 0)
        {
            if (ScalarLong(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM cafe_booking_deposit_facts
                    WHERE booking_id=$booking AND fact_type='Applied';
                    """,
                    ("$booking", payload.BookingId.ToString("D"))) != 0)
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationReplayDepositConflict",
                        "Zero-deposit V3 replay unexpectedly retained an Applied deposit fact."));
            return Result.Success();
        }

        if (!payload.DepositApplicationFactId.HasValue)
            return Result.Fail(
                Error.Conflict(
                    "CafeBooking.FinalizationReplayDepositMissing",
                    "Positive V3 deposit application is missing its fact identity."));

        using (SqliteCommand fact = connection.CreateCommand())
        {
            fact.Transaction = transaction;
            fact.CommandText = """
                SELECT amount_cents,sale_id,payload_hash
                FROM cafe_booking_deposit_facts
                WHERE deposit_fact_id=$fact
                  AND booking_id=$booking
                  AND booking_revision_id=$revision
                  AND fact_type='Applied'
                LIMIT 1;
                """;
            fact.Parameters.AddWithValue("$fact", payload.DepositApplicationFactId.Value.ToString("D"));
            fact.Parameters.AddWithValue("$booking", payload.BookingId.ToString("D"));
            fact.Parameters.AddWithValue("$revision", payload.BookingRevisionId.ToString("D"));
            using SqliteDataReader reader = fact.ExecuteReader();
            if (!reader.Read() ||
                reader.GetInt64(0) != payload.BookingDepositAppliedCents ||
                Guid.Parse(reader.GetString(1)) != payload.SaleEvidence.Sale.Id ||
                !string.Equals(reader.GetString(2), payloadHash, StringComparison.Ordinal))
                return Result.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationReplayDepositMissing",
                        "Retained V3 Applied deposit fact is missing or changed."));
        }

        Dictionary<Guid,long> retainedAllocations = new();
        using (SqliteCommand allocations = connection.CreateCommand())
        {
            allocations.Transaction = transaction;
            allocations.CommandText = """
                SELECT received_fact_id,amount_cents
                FROM cafe_booking_deposit_allocations
                WHERE action_fact_id=$fact
                ORDER BY received_fact_id;
                """;
            allocations.Parameters.AddWithValue("$fact", payload.DepositApplicationFactId.Value.ToString("D"));
            using SqliteDataReader reader = allocations.ExecuteReader();
            while (reader.Read())
                retainedAllocations[Guid.Parse(reader.GetString(0))] = reader.GetInt64(1);
        }

        Dictionary<Guid,long> canonicalAllocations =
            payload.DepositAllocations.ToDictionary(
                x => x.ReceivedDepositFactId,
                x => x.AmountCents);

        return retainedAllocations.Count == canonicalAllocations.Count &&
               retainedAllocations.All(x =>
                   canonicalAllocations.TryGetValue(x.Key, out long amount) &&
                   amount == x.Value)
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeBooking.FinalizationReplayAllocationConflict",
                    "Retained V3 deposit allocations differ from canonical evidence."));
    }

    private static Guid ReadSaleCommittedOperationId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleLinkId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT operation_id FROM cafe_booking_sale_links WHERE booking_sale_link_id=$link;";
        command.Parameters.AddWithValue("$link", saleLinkId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out Guid id)
            ? id
            : Guid.Empty;
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }
}

public interface ICafeOrderCompletionFailureInjector
{
    void ThrowAfterC9FinalizationBeforeOutbox();
}

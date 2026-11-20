using Microsoft.Data.Sqlite;
using Salryn.Core.Models;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteCafeBookingBootstrapStore
{
    private static string? ValidatePrerequisites(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingBootstrapV1 snapshot)
    {
        CafeBookingBootstrapAuthorityV1 a = snapshot.Authority;

        Dictionary<Guid, CafeBookingBootstrapBookingV1> bookings =
            a.Bookings.ToDictionary(x => x.BookingId);
        Dictionary<Guid, CafeBookingBootstrapRevisionV1> revisions =
            a.Revisions.ToDictionary(x => x.RevisionId);
        Dictionary<Guid, CafeBookingBootstrapOperationV1> operations =
            a.Operations.ToDictionary(x => x.OperationId);
        Dictionary<Guid, CafeBookingBootstrapDepositFactV1> facts =
            a.DepositFacts.ToDictionary(x => x.DepositFactId);
        Dictionary<Guid, CafeBookingBootstrapOrderLinkV1> orderLinks =
            a.OrderLinks.ToDictionary(x => x.BookingOrderLinkId);

        HashSet<(Guid RevisionId, Guid LineId)> lines =
            a.Lines.Select(x => (x.RevisionId, x.BookingLineId)).ToHashSet();

        foreach (CafeBookingBootstrapBookingV1 booking in a.Bookings)
        {
            if (!revisions.TryGetValue(
                    booking.CurrentRevisionId,
                    out CafeBookingBootstrapRevisionV1? current) ||
                current.BookingId != booking.BookingId)
            {
                return "Booking " + booking.BookingId.ToString("D") +
                    " does not retain its current revision.";
            }

            foreach (Guid user in new[]
            {
                booking.CreatedByUserId,
                booking.UpdatedByUserId
            })
            {
                if (!Exists(connection, transaction, "users", "id", user.ToString("D")))
                    return "Booking user prerequisite is missing: " + user.ToString("D") + ".";
            }

            if (booking.CancelledByUserId.HasValue &&
                !Exists(connection, transaction, "users", "id",
                    booking.CancelledByUserId.Value.ToString("D")))
            {
                return "Booking cancellation actor prerequisite is missing.";
            }
        }

        foreach (CafeBookingBootstrapRevisionV1 revision in a.Revisions)
        {
            if (!bookings.ContainsKey(revision.BookingId))
                return "Booking revision references a missing Booking aggregate.";

            if (revision.PreviousRevisionId.HasValue &&
                (!revisions.TryGetValue(revision.PreviousRevisionId.Value, out var previous) ||
                 previous.BookingId != revision.BookingId))
            {
                return "Booking revision previous-revision authority is missing.";
            }

            if (!Exists(connection, transaction, "customers", "id",
                    revision.CustomerId.ToString("D")))
            {
                return "Customer prerequisite is missing for Booking " +
                    revision.BookingId.ToString("D") + ".";
            }

            if (!ExistsPair(
                    connection,
                    transaction,
                    "menu_revisions",
                    "revision_id",
                    revision.MenuPublicationRevisionId,
                    "store_id",
                    snapshot.StoreId.ToString("D")))
            {
                return "MenuPublication prerequisite is missing for Booking " +
                    revision.BookingId.ToString("D") + ".";
            }

            if (!Exists(connection, transaction, "users", "id",
                    revision.ActorUserId.ToString("D")))
            {
                return "Booking revision actor prerequisite is missing.";
            }
        }

        foreach (CafeBookingBootstrapLineV1 line in a.Lines)
        {
            if (!revisions.ContainsKey(line.RevisionId))
                return "Booking line references a missing immutable Booking revision.";

            if (!Exists(connection, transaction, "products", "id",
                    line.ProductId.ToString("D")))
            {
                return "Product prerequisite is missing for Booking line " +
                    line.BookingLineId.ToString("D") + ".";
            }
        }

        foreach (CafeBookingBootstrapModifierV1 modifier in a.ModifierSelections)
        {
            if (!lines.Contains((modifier.RevisionId, modifier.BookingLineId)))
                return "Booking modifier references a missing Booking line snapshot.";
        }

        foreach (CafeBookingBootstrapOperationV1 operation in a.Operations)
        {
            if (!bookings.ContainsKey(operation.BookingId))
                return "Booking operation references a missing Booking aggregate.";
            if (operation.ResultRevisionId.HasValue &&
                !revisions.ContainsKey(operation.ResultRevisionId.Value))
                return "Booking operation result revision is missing.";
            if (!Exists(connection, transaction, "users", "id",
                    operation.ActorUserId.ToString("D")))
                return "Booking operation actor prerequisite is missing.";
        }

        foreach (CafeBookingBootstrapEventV1 e in a.Events)
        {
            if (!bookings.ContainsKey(e.BookingId) ||
                !operations.ContainsKey(e.OperationId))
                return "Booking lifecycle event references missing retained Booking authority.";
            if (e.BookingRevisionId.HasValue &&
                !revisions.ContainsKey(e.BookingRevisionId.Value))
                return "Booking lifecycle event references a missing revision.";
            if (!Exists(connection, transaction, "users", "id",
                    e.ActorUserId.ToString("D")))
                return "Booking lifecycle event actor prerequisite is missing.";
            if (e.OrderId.HasValue &&
                !ExistsPair(connection, transaction, "cafe_orders", "order_id",
                    e.OrderId.Value.ToString("D"), "store_id", snapshot.StoreId.ToString("D")))
                return "C4 Order prerequisite is missing for Booking lifecycle history.";
            if (e.SaleId.HasValue &&
                !Exists(connection, transaction, "sales", "id", e.SaleId.Value.ToString("D")))
                return "C5 Sale prerequisite is missing for Booking lifecycle history.";
        }

        foreach (CafeBookingBootstrapDepositFactV1 fact in a.DepositFacts)
        {
            if (!bookings.ContainsKey(fact.BookingId) ||
                !revisions.ContainsKey(fact.BookingRevisionId) ||
                !operations.ContainsKey(fact.OperationId))
            {
                return "Booking deposit fact references missing retained Booking authority.";
            }

            if (!Exists(connection, transaction, "users", "id",
                    fact.ActorUserId.ToString("D")))
                return "Booking deposit actor prerequisite is missing.";

            if (fact.FactType is "Received" or "Refunded")
            {
                if (!fact.CashierSessionId.HasValue ||
                    !fact.CashMovementId.HasValue)
                    return "Cash-backed Booking deposit fact is missing cashier/cash identity.";

                string referenceType =
                    fact.FactType == "Received"
                        ? CafeBookingCashReferenceTypes.DepositReceipt
                        : CafeBookingCashReferenceTypes.DepositRefund;
                long expectedDelta =
                    fact.FactType == "Received"
                        ? fact.AmountCents
                        : -fact.AmountCents;

                if (!CashMovementMatches(
                        connection,
                        transaction,
                        fact,
                        referenceType,
                        expectedDelta))
                {
                    return "C6 cash prerequisite is missing or incompatible for Booking deposit fact " +
                        fact.DepositFactId.ToString("D") + ".";
                }
            }
            else if (fact.FactType == "Applied")
            {
                if (!fact.SaleId.HasValue ||
                    !Exists(connection, transaction, "sales", "id",
                        fact.SaleId.Value.ToString("D")))
                    return "Applied Booking deposit fact requires retained C5 Sale authority.";
            }
            else
            {
                return "Unsupported Booking deposit fact type in bootstrap authority.";
            }
        }

        foreach (CafeBookingBootstrapDepositAllocationV1 allocation in a.DepositAllocations)
        {
            if (!facts.TryGetValue(allocation.ActionFactId, out var action) ||
                !facts.TryGetValue(allocation.ReceivedFactId, out var received) ||
                received.FactType != "Received" ||
                action.FactType is not ("Refunded" or "Applied") ||
                action.BookingId != received.BookingId)
            {
                return "Booking deposit allocation references incompatible retained facts.";
            }
        }

        foreach (IGrouping<Guid, CafeBookingBootstrapDepositAllocationV1> group in
            a.DepositAllocations.GroupBy(x => x.ReceivedFactId))
        {
            if (!facts.TryGetValue(group.Key, out var received) ||
                group.Sum(x => x.AmountCents) > received.AmountCents)
                return "Booking deposit allocations exceed retained Received authority.";
        }

        foreach (IGrouping<Guid, CafeBookingBootstrapDepositAllocationV1> group in
            a.DepositAllocations.GroupBy(x => x.ActionFactId))
        {
            if (!facts.TryGetValue(group.Key, out var action) ||
                group.Sum(x => x.AmountCents) != action.AmountCents)
                return "Booking deposit action allocation total does not match retained fact amount.";
        }

        foreach (CafeBookingBootstrapOrderLinkV1 link in a.OrderLinks)
        {
            if (!bookings.ContainsKey(link.BookingId) ||
                !revisions.ContainsKey(link.BookingRevisionId) ||
                !operations.ContainsKey(link.OperationId))
                return "Booking->Order link references missing retained C9 authority.";

            if (!ExistsPair(connection, transaction, "cafe_orders", "order_id",
                    link.OrderId.ToString("D"), "store_id", snapshot.StoreId.ToString("D")))
                return "C4 Order prerequisite is missing for Booking->Order link.";

            if (!Exists(connection, transaction, "users", "id",
                    link.ActorUserId.ToString("D")))
                return "Booking->Order actor prerequisite is missing.";
        }

        foreach (CafeBookingBootstrapSaleLinkV1 link in a.SaleLinks)
        {
            if (!bookings.ContainsKey(link.BookingId) ||
                !revisions.ContainsKey(link.BookingRevisionId) ||
                !orderLinks.TryGetValue(link.BookingOrderLinkId, out var orderLink) ||
                orderLink.OrderId != link.OrderId ||
                !operations.ContainsKey(link.OperationId))
            {
                return "Booking->Sale link references missing retained C9 fulfillment authority.";
            }

            if (!ExistsPair(connection, transaction, "cafe_orders", "order_id",
                    link.OrderId.ToString("D"), "store_id", snapshot.StoreId.ToString("D")))
                return "C4 Order prerequisite is missing for Booking->Sale link.";

            if (!Exists(connection, transaction, "sales", "id",
                    link.SaleId.ToString("D")))
                return "C5 Sale prerequisite is missing for Booking->Sale link.";

            long applied = 0;
            if (link.DepositApplicationFactId.HasValue)
            {
                if (!facts.TryGetValue(link.DepositApplicationFactId.Value, out var application) ||
                    application.FactType != "Applied" ||
                    application.SaleId != link.SaleId)
                {
                    return "Booking->Sale link deposit application authority is missing.";
                }

                applied = application.AmountCents;
            }

            if (!FinancialDecisionMatches(
                    connection,
                    transaction,
                    link.SaleId,
                    link.OrderId,
                    applied))
            {
                return "C5 Sale/financial prerequisite is missing or incompatible for Booking->Sale link.";
            }

            if (!Exists(connection, transaction, "users", "id",
                    link.ActorUserId.ToString("D")))
                return "Booking->Sale actor prerequisite is missing.";
        }

        return null;
    }

    private static bool CashMovementMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingBootstrapDepositFactV1 fact,
        string referenceType,
        long delta)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cash_movements
            WHERE id=$cash
              AND cashier_session_id=$session
              AND actor_user_id=$actor
              AND UPPER(TRIM(reference_type))=$type
              AND reference_id=$fact
              AND delta_cents=$delta;
            """;
        command.Parameters.AddWithValue("$cash", fact.CashMovementId!.Value.ToString("D"));
        command.Parameters.AddWithValue("$session", fact.CashierSessionId!.Value.ToString("D"));
        command.Parameters.AddWithValue("$actor", fact.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$type", referenceType);
        command.Parameters.AddWithValue("$fact", fact.DepositFactId.ToString("D"));
        command.Parameters.AddWithValue("$delta", delta);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool FinancialDecisionMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        Guid orderId,
        long bookingDepositAppliedCents)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cafe_sale_financial_decisions
            WHERE sale_id=$sale
              AND order_id=$order
              AND booking_deposit_applied_cents=$applied;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        command.Parameters.AddWithValue("$applied", bookingDepositAppliedCents);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM " + table +
            " WHERE " + column + "=$value;";
        command.Parameters.AddWithValue("$value", value);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool ExistsPair(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string firstColumn,
        string firstValue,
        string secondColumn,
        string secondValue)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM " + table +
            " WHERE " + firstColumn + "=$first AND " +
            secondColumn + "=$second;";
        command.Parameters.AddWithValue("$first", firstValue);
        command.Parameters.AddWithValue("$second", secondValue);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static void InsertAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeBookingBootstrapAuthorityV1 authority)
    {
        foreach (CafeBookingBootstrapBookingV1 x in authority.Bookings)
            Exec(connection, transaction, """
                INSERT INTO cafe_bookings(
                    booking_id,store_id,current_version,current_revision_id,status,fulfillment_state,
                    created_by_user_id,created_at_utc,updated_by_user_id,updated_at_utc,
                    cancelled_by_user_id,cancelled_at_utc,cancellation_reason)
                VALUES(
                    $booking,$store,$version,$revision,$status,$fulfillment,
                    $createdBy,$createdAt,$updatedBy,$updatedAt,$cancelledBy,$cancelledAt,$reason);
                """,
                ("$booking", x.BookingId.ToString("D")), ("$store", x.StoreId.ToString("D")),
                ("$version", x.CurrentVersion), ("$revision", x.CurrentRevisionId.ToString("D")),
                ("$status", x.Status), ("$fulfillment", x.FulfillmentState),
                ("$createdBy", x.CreatedByUserId.ToString("D")), ("$createdAt", x.CreatedAtUtc),
                ("$updatedBy", x.UpdatedByUserId.ToString("D")), ("$updatedAt", x.UpdatedAtUtc),
                ("$cancelledBy", DbGuid(x.CancelledByUserId)), ("$cancelledAt", Db(x.CancelledAtUtc)),
                ("$reason", Db(x.CancellationReason)));

        foreach (CafeBookingBootstrapRevisionV1 x in authority.Revisions)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_revisions(
                    revision_id,booking_id,revision_number,booking_version,previous_revision_id,
                    customer_id,customer_name_snapshot,customer_phone_snapshot,customer_address_snapshot,
                    booking_mode,event_name,event_reference,event_start_utc,event_end_utc,location,
                    setup_at_utc,service_window_start_utc,service_window_end_utc,headcount,package_label,
                    menu_publication_revision_id,quoted_total_cents,deposit_requirement_cents,
                    special_instructions,notes,fulfillment_service_mode,actor_user_id,created_at_utc,content_hash)
                VALUES(
                    $revision,$booking,$revisionNumber,$bookingVersion,$previous,
                    $customer,$customerName,$customerPhone,$customerAddress,
                    $mode,$eventName,$eventReference,$eventStart,$eventEnd,$location,
                    $setup,$windowStart,$windowEnd,$headcount,$package,
                    $menu,$quoted,$deposit,$instructions,$notes,$serviceMode,$actor,$created,$hash);
                """,
                ("$revision", x.RevisionId.ToString("D")), ("$booking", x.BookingId.ToString("D")),
                ("$revisionNumber", x.RevisionNumber), ("$bookingVersion", x.BookingVersion),
                ("$previous", DbGuid(x.PreviousRevisionId)), ("$customer", x.CustomerId.ToString("D")),
                ("$customerName", x.CustomerNameSnapshot), ("$customerPhone", Db(x.CustomerPhoneSnapshot)),
                ("$customerAddress", Db(x.CustomerAddressSnapshot)), ("$mode", x.BookingMode),
                ("$eventName", x.EventName), ("$eventReference", Db(x.EventReference)),
                ("$eventStart", x.EventStartUtc), ("$eventEnd", Db(x.EventEndUtc)),
                ("$location", Db(x.Location)), ("$setup", Db(x.SetupAtUtc)),
                ("$windowStart", Db(x.ServiceWindowStartUtc)), ("$windowEnd", Db(x.ServiceWindowEndUtc)),
                ("$headcount", (object?)x.Headcount ?? DBNull.Value), ("$package", Db(x.PackageLabel)),
                ("$menu", x.MenuPublicationRevisionId), ("$quoted", x.QuotedTotalCents),
                ("$deposit", x.DepositRequirementCents), ("$instructions", Db(x.SpecialInstructions)),
                ("$notes", Db(x.Notes)), ("$serviceMode", x.FulfillmentServiceMode),
                ("$actor", x.ActorUserId.ToString("D")), ("$created", x.CreatedAtUtc), ("$hash", x.ContentHash));

        foreach (CafeBookingBootstrapLineV1 x in authority.Lines)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_revision_lines(
                    revision_id,booking_line_id,product_id,product_name_snapshot,unit_name_snapshot,
                    quantity_x1000,base_unit_price_cents,variant_id,variant_name_snapshot,
                    variant_unit_price_cents,quoted_unit_price_cents,line_total_cents,display_order,line_note)
                VALUES(
                    $revision,$line,$product,$name,$unit,$quantity,$base,$variant,$variantName,
                    $variantPrice,$quoted,$total,$display,$note);
                """,
                ("$revision", x.RevisionId.ToString("D")), ("$line", x.BookingLineId.ToString("D")),
                ("$product", x.ProductId.ToString("D")), ("$name", x.ProductNameSnapshot),
                ("$unit", x.UnitNameSnapshot), ("$quantity", x.QuantityX1000),
                ("$base", x.BaseUnitPriceCents), ("$variant", Db(x.VariantId)),
                ("$variantName", Db(x.VariantNameSnapshot)),
                ("$variantPrice", (object?)x.VariantUnitPriceCents ?? DBNull.Value),
                ("$quoted", x.QuotedUnitPriceCents), ("$total", x.LineTotalCents),
                ("$display", x.DisplayOrder), ("$note", Db(x.LineNote)));

        foreach (CafeBookingBootstrapModifierV1 x in authority.ModifierSelections)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_revision_modifier_selections(
                    revision_id,booking_line_id,group_id,group_name_snapshot,
                    option_id,option_name_snapshot,price_delta_cents,display_order)
                VALUES($revision,$line,$group,$groupName,$option,$optionName,$price,$display);
                """,
                ("$revision", x.RevisionId.ToString("D")), ("$line", x.BookingLineId.ToString("D")),
                ("$group", x.GroupId), ("$groupName", x.GroupNameSnapshot),
                ("$option", x.OptionId), ("$optionName", x.OptionNameSnapshot),
                ("$price", x.PriceDeltaCents), ("$display", x.DisplayOrder));

        foreach (CafeBookingBootstrapOperationV1 x in authority.Operations)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_operations(
                    operation_id,booking_id,device_id,store_id,operation_type,payload_hash,
                    expected_version,resulting_version,result_revision_id,result_status,
                    result_fulfillment_state,actor_user_id,occurred_at_utc)
                VALUES(
                    $operation,$booking,$device,$store,$type,$hash,$expected,$resulting,
                    $revision,$status,$fulfillment,$actor,$occurred);
                """,
                ("$operation", x.OperationId.ToString("D")), ("$booking", x.BookingId.ToString("D")),
                ("$device", x.DeviceId.ToString("D")), ("$store", x.StoreId.ToString("D")),
                ("$type", x.OperationType), ("$hash", x.PayloadHash), ("$expected", x.ExpectedVersion),
                ("$resulting", x.ResultingVersion), ("$revision", DbGuid(x.ResultRevisionId)),
                ("$status", x.ResultStatus), ("$fulfillment", x.ResultFulfillmentState),
                ("$actor", x.ActorUserId.ToString("D")), ("$occurred", x.OccurredAtUtc));

        foreach (CafeBookingBootstrapEventV1 x in authority.Events)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_events(
                    event_id,operation_id,booking_id,expected_version,resulting_version,action,
                    from_status,to_status,from_fulfillment_state,to_fulfillment_state,
                    booking_revision_id,order_id,sale_id,actor_user_id,occurred_at_utc,reason,content_hash)
                VALUES(
                    $event,$operation,$booking,$expected,$resulting,$action,$fromStatus,$toStatus,
                    $fromFulfillment,$toFulfillment,$revision,$order,$sale,$actor,$occurred,$reason,$hash);
                """,
                ("$event", x.EventId.ToString("D")), ("$operation", x.OperationId.ToString("D")),
                ("$booking", x.BookingId.ToString("D")), ("$expected", x.ExpectedVersion),
                ("$resulting", x.ResultingVersion), ("$action", x.Action),
                ("$fromStatus", Db(x.FromStatus)), ("$toStatus", x.ToStatus),
                ("$fromFulfillment", Db(x.FromFulfillmentState)), ("$toFulfillment", x.ToFulfillmentState),
                ("$revision", DbGuid(x.BookingRevisionId)), ("$order", DbGuid(x.OrderId)),
                ("$sale", DbGuid(x.SaleId)), ("$actor", x.ActorUserId.ToString("D")),
                ("$occurred", x.OccurredAtUtc), ("$reason", Db(x.Reason)), ("$hash", x.ContentHash));

        foreach (CafeBookingBootstrapDepositFactV1 x in authority.DepositFacts)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_deposit_facts(
                    deposit_fact_id,operation_id,booking_id,store_id,booking_revision_id,fact_type,
                    actor_user_id,amount_cents,occurred_at_utc,reference,cashier_session_id,
                    cash_movement_id,sale_id,payload_hash)
                VALUES(
                    $fact,$operation,$booking,$store,$revision,$type,$actor,$amount,$occurred,
                    $reference,$session,$cash,$sale,$hash);
                """,
                ("$fact", x.DepositFactId.ToString("D")), ("$operation", x.OperationId.ToString("D")),
                ("$booking", x.BookingId.ToString("D")), ("$store", x.StoreId.ToString("D")),
                ("$revision", x.BookingRevisionId.ToString("D")), ("$type", x.FactType),
                ("$actor", x.ActorUserId.ToString("D")), ("$amount", x.AmountCents),
                ("$occurred", x.OccurredAtUtc), ("$reference", x.Reference),
                ("$session", DbGuid(x.CashierSessionId)), ("$cash", DbGuid(x.CashMovementId)),
                ("$sale", DbGuid(x.SaleId)), ("$hash", x.PayloadHash));

        foreach (CafeBookingBootstrapDepositAllocationV1 x in authority.DepositAllocations)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_deposit_allocations(
                    action_fact_id,received_fact_id,amount_cents)
                VALUES($action,$received,$amount);
                """,
                ("$action", x.ActionFactId.ToString("D")),
                ("$received", x.ReceivedFactId.ToString("D")),
                ("$amount", x.AmountCents));

        foreach (CafeBookingBootstrapOrderLinkV1 x in authority.OrderLinks)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_order_links(
                    booking_order_link_id,booking_id,booking_revision_id,order_id,
                    operation_id,actor_user_id,occurred_at_utc,content_hash)
                VALUES($link,$booking,$revision,$order,$operation,$actor,$occurred,$hash);
                """,
                ("$link", x.BookingOrderLinkId.ToString("D")), ("$booking", x.BookingId.ToString("D")),
                ("$revision", x.BookingRevisionId.ToString("D")), ("$order", x.OrderId.ToString("D")),
                ("$operation", x.OperationId.ToString("D")), ("$actor", x.ActorUserId.ToString("D")),
                ("$occurred", x.OccurredAtUtc), ("$hash", x.ContentHash));

        foreach (CafeBookingBootstrapSaleLinkV1 x in authority.SaleLinks)
            Exec(connection, transaction, """
                INSERT INTO cafe_booking_sale_links(
                    booking_sale_link_id,booking_id,booking_revision_id,booking_order_link_id,
                    order_id,sale_id,deposit_application_fact_id,operation_id,
                    actor_user_id,occurred_at_utc,content_hash)
                VALUES(
                    $link,$booking,$revision,$orderLink,$order,$sale,$deposit,$operation,
                    $actor,$occurred,$hash);
                """,
                ("$link", x.BookingSaleLinkId.ToString("D")), ("$booking", x.BookingId.ToString("D")),
                ("$revision", x.BookingRevisionId.ToString("D")),
                ("$orderLink", x.BookingOrderLinkId.ToString("D")), ("$order", x.OrderId.ToString("D")),
                ("$sale", x.SaleId.ToString("D")), ("$deposit", DbGuid(x.DepositApplicationFactId)),
                ("$operation", x.OperationId.ToString("D")), ("$actor", x.ActorUserId.ToString("D")),
                ("$occurred", x.OccurredAtUtc), ("$hash", x.ContentHash));
    }

    private static void Exec(
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
        command.ExecuteNonQuery();
    }

    private static object Db(string? value) =>
        value is null ? DBNull.Value : value;

    private static object DbGuid(Guid? value) =>
        value.HasValue ? value.Value.ToString("D") : DBNull.Value;
}

using Microsoft.Data.Sqlite;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class CafeBookingBootstrapAuthorityReader
{
    public static CafeBookingBootstrapAuthorityV1 Read(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        if (storeId == Guid.Empty)
            throw new ArgumentException("C9 Booking bootstrap authority requires StoreId.", nameof(storeId));

        string store = storeId.ToString("D");

        return new CafeBookingBootstrapAuthorityV1(
            ReadBookings(connection, transaction, store),
            ReadRevisions(connection, transaction, store),
            ReadLines(connection, transaction, store),
            ReadModifiers(connection, transaction, store),
            ReadOperations(connection, transaction, store),
            ReadEvents(connection, transaction, store),
            ReadDepositFacts(connection, transaction, store),
            ReadDepositAllocations(connection, transaction, store),
            ReadOrderLinks(connection, transaction, store),
            ReadSaleLinks(connection, transaction, store));
    }

    private static IReadOnlyList<CafeBookingBootstrapBookingV1> ReadBookings(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapBookingV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT booking_id,store_id,current_version,current_revision_id,status,fulfillment_state,
                   created_by_user_id,created_at_utc,updated_by_user_id,updated_at_utc,
                   cancelled_by_user_id,cancelled_at_utc,cancellation_reason
            FROM cafe_bookings
            WHERE store_id=$store
            ORDER BY booking_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetInt32(2),
                Guid.Parse(r.GetString(3)), r.GetString(4), r.GetString(5),
                Guid.Parse(r.GetString(6)), r.GetString(7), Guid.Parse(r.GetString(8)),
                r.GetString(9), GuidOrNull(r, 10), StringOrNull(r, 11), StringOrNull(r, 12)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapRevisionV1> ReadRevisions(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapRevisionV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT r.revision_id,r.booking_id,r.revision_number,r.booking_version,r.previous_revision_id,
                   r.customer_id,r.customer_name_snapshot,r.customer_phone_snapshot,r.customer_address_snapshot,
                   r.booking_mode,r.event_name,r.event_reference,r.event_start_utc,r.event_end_utc,r.location,
                   r.setup_at_utc,r.service_window_start_utc,r.service_window_end_utc,r.headcount,r.package_label,
                   r.menu_publication_revision_id,r.quoted_total_cents,r.deposit_requirement_cents,
                   r.special_instructions,r.notes,r.fulfillment_service_mode,r.actor_user_id,r.created_at_utc,r.content_hash
            FROM cafe_booking_revisions r
            INNER JOIN cafe_bookings b ON b.booking_id=r.booking_id
            WHERE b.store_id=$store
            ORDER BY r.booking_id,r.booking_version,r.revision_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetInt32(2), r.GetInt32(3),
                GuidOrNull(r, 4), Guid.Parse(r.GetString(5)), r.GetString(6), StringOrNull(r, 7),
                StringOrNull(r, 8), r.GetString(9), r.GetString(10), StringOrNull(r, 11),
                r.GetString(12), StringOrNull(r, 13), StringOrNull(r, 14), StringOrNull(r, 15),
                StringOrNull(r, 16), StringOrNull(r, 17), IntOrNull(r, 18), StringOrNull(r, 19),
                r.GetString(20), r.GetInt64(21), r.GetInt64(22), StringOrNull(r, 23),
                StringOrNull(r, 24), r.GetString(25), Guid.Parse(r.GetString(26)), r.GetString(27), r.GetString(28)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapLineV1> ReadLines(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapLineV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT l.revision_id,l.booking_line_id,l.product_id,l.product_name_snapshot,l.unit_name_snapshot,
                   l.quantity_x1000,l.base_unit_price_cents,l.variant_id,l.variant_name_snapshot,
                   l.variant_unit_price_cents,l.quoted_unit_price_cents,l.line_total_cents,l.display_order,l.line_note
            FROM cafe_booking_revision_lines l
            INNER JOIN cafe_booking_revisions r ON r.revision_id=l.revision_id
            INNER JOIN cafe_bookings b ON b.booking_id=r.booking_id
            WHERE b.store_id=$store
            ORDER BY l.revision_id,l.display_order,l.booking_line_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
                r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetInt64(6),
                StringOrNull(r, 7), StringOrNull(r, 8), LongOrNull(r, 9), r.GetInt64(10),
                r.GetInt64(11), r.GetInt32(12), StringOrNull(r, 13)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapModifierV1> ReadModifiers(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapModifierV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT m.revision_id,m.booking_line_id,m.group_id,m.group_name_snapshot,
                   m.option_id,m.option_name_snapshot,m.price_delta_cents,m.display_order
            FROM cafe_booking_revision_modifier_selections m
            INNER JOIN cafe_booking_revisions r ON r.revision_id=m.revision_id
            INNER JOIN cafe_bookings b ON b.booking_id=r.booking_id
            WHERE b.store_id=$store
            ORDER BY m.revision_id,m.booking_line_id,m.display_order,m.group_id,m.option_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetInt32(7)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapOperationV1> ReadOperations(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapOperationV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT operation_id,booking_id,device_id,store_id,operation_type,payload_hash,
                   expected_version,resulting_version,result_revision_id,result_status,
                   result_fulfillment_state,actor_user_id,occurred_at_utc
            FROM cafe_booking_operations
            WHERE store_id=$store
            ORDER BY booking_id,resulting_version,operation_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)), r.GetString(4), r.GetString(5), r.GetInt32(6),
                r.GetInt32(7), GuidOrNull(r, 8), r.GetString(9), r.GetString(10),
                Guid.Parse(r.GetString(11)), r.GetString(12)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapEventV1> ReadEvents(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapEventV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT e.event_id,e.operation_id,e.booking_id,e.expected_version,e.resulting_version,
                   e.action,e.from_status,e.to_status,e.from_fulfillment_state,e.to_fulfillment_state,
                   e.booking_revision_id,e.order_id,e.sale_id,e.actor_user_id,e.occurred_at_utc,e.reason,e.content_hash
            FROM cafe_booking_events e
            INNER JOIN cafe_bookings b ON b.booking_id=e.booking_id
            WHERE b.store_id=$store
            ORDER BY e.booking_id,e.resulting_version,e.occurred_at_utc,e.event_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
                r.GetInt32(3), r.GetInt32(4), r.GetString(5), StringOrNull(r, 6), r.GetString(7),
                StringOrNull(r, 8), r.GetString(9), GuidOrNull(r, 10), GuidOrNull(r, 11),
                GuidOrNull(r, 12), Guid.Parse(r.GetString(13)), r.GetString(14),
                StringOrNull(r, 15), r.GetString(16)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapDepositFactV1> ReadDepositFacts(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapDepositFactV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT deposit_fact_id,operation_id,booking_id,store_id,booking_revision_id,
                   fact_type,actor_user_id,amount_cents,occurred_at_utc,reference,
                   cashier_session_id,cash_movement_id,sale_id,payload_hash
            FROM cafe_booking_deposit_facts
            WHERE store_id=$store
            ORDER BY booking_id,occurred_at_utc,deposit_fact_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)), Guid.Parse(r.GetString(4)), r.GetString(5),
                Guid.Parse(r.GetString(6)), r.GetInt64(7), r.GetString(8), r.GetString(9),
                GuidOrNull(r, 10), GuidOrNull(r, 11), GuidOrNull(r, 12), r.GetString(13)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapDepositAllocationV1> ReadDepositAllocations(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapDepositAllocationV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT a.action_fact_id,a.received_fact_id,a.amount_cents
            FROM cafe_booking_deposit_allocations a
            INNER JOIN cafe_booking_deposit_facts f ON f.deposit_fact_id=a.action_fact_id
            WHERE f.store_id=$store
            ORDER BY a.action_fact_id,a.received_fact_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetInt64(2)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapOrderLinkV1> ReadOrderLinks(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapOrderLinkV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT l.booking_order_link_id,l.booking_id,l.booking_revision_id,l.order_id,
                   l.operation_id,l.actor_user_id,l.occurred_at_utc,l.content_hash
            FROM cafe_booking_order_links l
            INNER JOIN cafe_bookings b ON b.booking_id=l.booking_id
            WHERE b.store_id=$store
            ORDER BY l.booking_id,l.booking_order_link_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)), Guid.Parse(r.GetString(4)), Guid.Parse(r.GetString(5)),
                r.GetString(6), r.GetString(7)));
        return result;
    }

    private static IReadOnlyList<CafeBookingBootstrapSaleLinkV1> ReadSaleLinks(
        SqliteConnection connection, SqliteTransaction transaction, string store)
    {
        List<CafeBookingBootstrapSaleLinkV1> result = new();
        using SqliteCommand command = Create(connection, transaction, """
            SELECT l.booking_sale_link_id,l.booking_id,l.booking_revision_id,l.booking_order_link_id,
                   l.order_id,l.sale_id,l.deposit_application_fact_id,l.operation_id,
                   l.actor_user_id,l.occurred_at_utc,l.content_hash
            FROM cafe_booking_sale_links l
            INNER JOIN cafe_bookings b ON b.booking_id=l.booking_id
            WHERE b.store_id=$store
            ORDER BY l.booking_id,l.booking_sale_link_id;
            """, store);
        using SqliteDataReader r = command.ExecuteReader();
        while (r.Read())
            result.Add(new(
                Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
                Guid.Parse(r.GetString(3)), Guid.Parse(r.GetString(4)), Guid.Parse(r.GetString(5)),
                GuidOrNull(r, 6), Guid.Parse(r.GetString(7)), Guid.Parse(r.GetString(8)),
                r.GetString(9), r.GetString(10)));
        return result;
    }

    private static SqliteCommand Create(
        SqliteConnection connection, SqliteTransaction transaction, string sql, string store)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$store", store);
        return command;
    }

    private static string? StringOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static Guid? GuidOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    private static long? LongOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static int? IntOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}

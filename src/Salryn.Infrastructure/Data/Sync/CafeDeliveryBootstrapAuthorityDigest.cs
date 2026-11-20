using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeDeliveryAuthorityDigest(
    long DeliveryCount,
    long NonTerminalDeliveryCount,
    long RevisionCount,
    long OperationCount,
    long EventCount,
    string Digest);

public static class CafeDeliveryBootstrapAuthorityDigest
{
    public static CafeDeliveryAuthorityDigest Read(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        if (storeId == Guid.Empty)
            throw new ArgumentException(
                "C8 delivery authority requires StoreId.",
                nameof(storeId));

        string store = storeId.ToString("D");

        long deliveryCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_delivery_fulfillments
            WHERE store_id=$store;
            """,
            ("$store", store));

        long nonTerminalDeliveryCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_delivery_fulfillments
            WHERE store_id=$store
              AND status IN ('Pending','Dispatched');
            """,
            ("$store", store));

        long revisionCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_delivery_revisions r
            INNER JOIN cafe_delivery_fulfillments f
              ON f.delivery_id=r.delivery_id
            WHERE f.store_id=$store;
            """,
            ("$store", store));

        long operationCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_delivery_operations
            WHERE store_id=$store;
            """,
            ("$store", store));

        long eventCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_delivery_events e
            INNER JOIN cafe_delivery_fulfillments f
              ON f.delivery_id=e.delivery_id
            WHERE f.store_id=$store;
            """,
            ("$store", store));

        StringBuilder canonical = new();

        AppendQuery(
            canonical,
            connection,
            transaction,
            "fulfillments",
            """
            SELECT
                delivery_id,order_id,store_id,current_version,
                current_revision_id,status,created_by_user_id,
                created_at_utc,updated_at_utc,terminal_reason
            FROM cafe_delivery_fulfillments
            WHERE store_id=$store
            ORDER BY delivery_id;
            """,
            ("$store", store));

        AppendQuery(
            canonical,
            connection,
            transaction,
            "revisions",
            """
            SELECT
                r.revision_id,r.delivery_id,r.order_id,
                r.revision_number,r.delivery_version,
                r.recipient_name,r.contact_phone,
                r.delivery_address,r.landmark,r.instructions,
                r.delivery_fee_cents,r.courier_reference,
                r.content_hash,r.actor_user_id,r.created_at_utc
            FROM cafe_delivery_revisions r
            INNER JOIN cafe_delivery_fulfillments f
              ON f.delivery_id=r.delivery_id
            WHERE f.store_id=$store
            ORDER BY
                r.delivery_id,
                r.delivery_version,
                r.revision_number,
                r.revision_id;
            """,
            ("$store", store));

        AppendQuery(
            canonical,
            connection,
            transaction,
            "operations",
            """
            SELECT
                operation_id,delivery_id,device_id,store_id,
                operation_type,payload_hash,expected_version,
                resulting_version,result_revision_id,
                result_status,actor_user_id,occurred_at_utc
            FROM cafe_delivery_operations
            WHERE store_id=$store
            ORDER BY
                delivery_id,
                resulting_version,
                operation_id;
            """,
            ("$store", store));

        AppendQuery(
            canonical,
            connection,
            transaction,
            "events",
            """
            SELECT
                e.event_id,e.operation_id,e.delivery_id,e.order_id,
                e.expected_version,e.resulting_version,e.action,
                e.from_status,e.to_status,e.actor_user_id,
                e.occurred_at_utc,e.reason,e.delivery_revision_id,
                e.sale_id,e.content_hash
            FROM cafe_delivery_events e
            INNER JOIN cafe_delivery_fulfillments f
              ON f.delivery_id=e.delivery_id
            WHERE f.store_id=$store
            ORDER BY
                e.delivery_id,
                e.resulting_version,
                e.occurred_at_utc,
                e.event_id;
            """,
            ("$store", store));

        string digest =
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            canonical.ToString())))
                .ToLowerInvariant();

        return new CafeDeliveryAuthorityDigest(
            deliveryCount,
            nonTerminalDeliveryCount,
            revisionCount,
            operationCount,
            eventCount,
            digest);
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);

        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L);
    }

    private static void AppendQuery(
        StringBuilder canonical,
        SqliteConnection connection,
        SqliteTransaction transaction,
        string section,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        canonical
            .Append('[')
            .Append(section.Length)
            .Append(':')
            .Append(section)
            .Append(']');

        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            canonical.Append('{');

            for (int i = 0; i < reader.FieldCount; i++)
            {
                object value = reader.GetValue(i);

                if (value is DBNull)
                {
                    canonical.Append("-1:");
                    continue;
                }

                string text =
                    Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture) ??
                    string.Empty;

                canonical
                    .Append(text.Length)
                    .Append(':')
                    .Append(text);
            }

            canonical.Append('}');
        }
    }
}

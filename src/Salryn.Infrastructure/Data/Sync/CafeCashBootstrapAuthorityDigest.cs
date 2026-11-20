using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeCashAuthorityDigest(
    long ManualMovementCount,
    long ReversalCount,
    long NetDeltaCents,
    string Digest);

public static class CafeCashBootstrapAuthorityDigest
{
    public static CafeCashAuthorityDigest Read(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        long movementCount = ScalarLong(
            connection,
            transaction,
            "SELECT COUNT(*) FROM cafe_cash_operations;");

        long reversalCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_cash_operations
            WHERE movement_type=$reversal;
            """,
            ("$reversal", Convert.ToInt32(CashMovementType.Reversal)));

        long netDeltaCents = ScalarLong(
            connection,
            transaction,
            "SELECT COALESCE(SUM(delta_cents),0) FROM cafe_cash_operations;");

        StringBuilder canonical = new();

        AppendQuery(
            canonical,
            connection,
            transaction,
            "operations",
            """
            SELECT operation_id,movement_id,device_id,store_id,
                   actor_user_id,cashier_session_id,movement_type,
                   requested_amount_cents,delta_cents,
                   reference_type,reference_id,reversal_of_movement_id,
                   reason,payload_hash,occurred_at_utc
            FROM cafe_cash_operations
            ORDER BY operation_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "ledger",
            """
            SELECT m.id,m.cashier_session_id,m.actor_user_id,
                   m.movement_type,m.delta_cents,m.reference_type,
                   m.reference_id,m.reversal_of_movement_id,
                   m.reason,m.created_at_utc
            FROM cash_movements m
            INNER JOIN cafe_cash_operations o
              ON o.movement_id=m.id
            ORDER BY m.id;
            """);

        string digest = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        return new CafeCashAuthorityDigest(
            movementCount,
            reversalCount,
            netDeltaCents,
            digest);
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

    private static void AppendQuery(
        StringBuilder canonical,
        SqliteConnection connection,
        SqliteTransaction transaction,
        string section,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        canonical.Append('[').Append(section).Append(']');

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            canonical.Append('{');
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (i > 0)
                    canonical.Append('|');

                object value = reader.GetValue(i);
                canonical.Append(
                    value is DBNull
                        ? "<null>"
                        : Convert.ToString(
                            value,
                            CultureInfo.InvariantCulture));
            }

            canonical.Append('}');
        }
    }
}

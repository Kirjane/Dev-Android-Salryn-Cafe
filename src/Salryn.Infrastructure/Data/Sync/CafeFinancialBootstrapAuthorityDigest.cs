using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record CafeFinancialAuthorityDigest(
    long FinancialDecisionCount,
    long RefundCount,
    long NonTerminalRefundCount,
    string Digest);

public static class CafeFinancialBootstrapAuthorityDigest
{
    public static CafeFinancialAuthorityDigest Read(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        long decisionCount = ScalarLong(
            connection,
            transaction,
            "SELECT COUNT(*) FROM cafe_sale_financial_decisions;");
        long refundCount = ScalarLong(
            connection,
            transaction,
            "SELECT COUNT(*) FROM cafe_sale_refunds;");
        long nonTerminalRefundCount = ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_sale_refunds
            WHERE state IN ('Requested','Approved');
            """);

        StringBuilder canonical = new();
        AppendQuery(
            canonical,
            connection,
            transaction,
            "financial_decisions",
            """
            SELECT financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                   expected_order_version,sale_id,cashier_session_id,actor_user_id,
                   gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                   fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                   accounts_receivable_tender_cents,amount_paid_cents,balance_due_cents,
                   change_cents,discount_reason,content_hash,created_at_utc
            FROM cafe_sale_financial_decisions
            ORDER BY financial_decision_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "financial_line_discounts",
            """
            SELECT financial_decision_id,sale_item_id,line_discount_cents,
                   order_discount_allocated_cents,total_discount_cents
            FROM cafe_sale_financial_line_discounts
            ORDER BY financial_decision_id,sale_item_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "fees",
            """
            SELECT fee_id,financial_decision_id,sale_id,fee_type,
                   display_label_snapshot,amount_cents,actor_user_id,
                   reason,created_at_utc
            FROM cafe_sale_fees
            ORDER BY fee_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refunds",
            """
            SELECT refund_id,sale_id,store_id,current_version,state,reason,
                   requested_by_user_id,requested_at_utc,request_hash,
                   resolved_by_user_id,resolved_at_utc,resolution_reason,
                   committed_by_user_id,committed_at_utc,total_refund_cents
            FROM cafe_sale_refunds
            ORDER BY refund_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refund_lines",
            """
            SELECT refund_line_id,refund_id,sale_item_id,product_id,
                   product_name_snapshot,original_quantity_x1000,
                   refund_quantity_x1000,frozen_unit_price_cents,
                   frozen_discount_cents,refund_amount_cents,
                   stock_disposition,operator_note
            FROM cafe_sale_refund_lines
            ORDER BY refund_line_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refund_operations",
            """
            SELECT operation_id,device_id,store_id,refund_id,sale_id,
                   operation_type,payload_hash,expected_version,resulting_version,
                   result_state,actor_user_id,occurred_at_utc
            FROM cafe_sale_refund_operations
            ORDER BY operation_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refund_events",
            """
            SELECT event_id,operation_id,refund_id,sale_id,expected_version,
                   resulting_version,action,from_state,to_state,actor_user_id,
                   occurred_at_utc,reason,content_hash
            FROM cafe_sale_refund_events
            ORDER BY event_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refund_settlements",
            """
            SELECT settlement_part_id,refund_id,method,amount_cents,
                   cash_movement_id,receivable_entry_id,created_at_utc
            FROM cafe_sale_refund_settlements
            ORDER BY settlement_part_id;
            """);

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refund_cash",
            """
            SELECT id,cashier_session_id,actor_user_id,movement_type,delta_cents,
                   reference_type,reference_id,reversal_of_movement_id,reason,created_at_utc
            FROM cash_movements
            WHERE movement_type=$refundType
              AND UPPER(TRIM(reference_type))='REFUND'
            ORDER BY id;
            """,
            ("$refundType", Convert.ToInt32(CashMovementType.CashRefund)));

        AppendQuery(
            canonical,
            connection,
            transaction,
            "refund_ar",
            """
            SELECT id,customer_id,sale_id,collection_id,entry_type,
                   debit_cents,credit_cents,balance_after_cents,due_date,
                   note,created_by_user_id,created_at,refund_id
            FROM accounts_receivable_entries
            WHERE refund_id IS NOT NULL
            ORDER BY id;
            """);

        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();

        return new CafeFinancialAuthorityDigest(
            decisionCount,
            refundCount,
            nonTerminalRefundCount,
            digest);
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
                if (reader.IsDBNull(i))
                {
                    canonical.Append("N;");
                    continue;
                }

                object value = reader.GetValue(i);
                string text = value switch
                {
                    byte[] bytes => Convert.ToHexString(bytes).ToLowerInvariant(),
                    IFormattable formattable =>
                        formattable.ToString(null, CultureInfo.InvariantCulture) ??
                        string.Empty,
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ??
                         string.Empty
                };

                canonical
                    .Append(text.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(text)
                    .Append(';');
            }

            canonical.Append('}');
        }
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L,
            CultureInfo.InvariantCulture);
    }
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

internal static class LocalSaleCommittedV2FactVerifier
{
    public static LocalSaleFactVerification VerifyOriginEcho(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SaleCommittedV2 payload)
    {
        LocalSaleFactVerification saleFacts =
            LocalSaleCommittedV1FactVerifier.VerifyOriginEcho(
                connection,
                transaction,
                change,
                payload.SaleEvidence,
                SaleCommittedV2Contract.OperationType,
                SaleCommittedV2Contract.SchemaVersion);

        if (!saleFacts.Matches)
            return saleFacts;

        Result<CafeSaleFinancialDecision> reconstructed =
            SaleCommittedV2Mapper.ToFinancialDecision(
                payload,
                change.StoreId);
        if (reconstructed.IsFailure)
        {
            return LocalSaleFactVerification.Conflict(
                reconstructed.ErrorDetail.Code,
                reconstructed.ErrorDetail.Message);
        }

        CafeSaleFinancialDecision decision =
            reconstructed.Value;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT operation_id,store_id,order_id,order_revision_id,
                       expected_order_version,sale_id,cashier_session_id,
                       actor_user_id,gross_subtotal_cents,
                       line_discount_total_cents,order_discount_total_cents,
                       fee_total_cents,tax_cents,net_sale_total_cents,
                       cash_tender_cents,accounts_receivable_tender_cents,
                       amount_paid_cents,balance_due_cents,change_cents,
                       COALESCE(discount_reason,''),content_hash,created_at_utc
                FROM cafe_sale_financial_decisions
                WHERE financial_decision_id=$decision
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$decision",
                decision.FinancialDecisionId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read() ||
                Guid.Parse(reader.GetString(0)) != decision.OperationId ||
                Guid.Parse(reader.GetString(1)) != decision.StoreId ||
                !NullableGuidEquals(reader, 2, decision.OrderId) ||
                !NullableGuidEquals(reader, 3, decision.OrderRevisionId) ||
                !NullableIntEquals(reader, 4, decision.ExpectedOrderVersion) ||
                Guid.Parse(reader.GetString(5)) != decision.SaleId ||
                Guid.Parse(reader.GetString(6)) != decision.CashierSessionId ||
                Guid.Parse(reader.GetString(7)) != decision.ActorUserId ||
                reader.GetInt64(8) != decision.GrossSubtotalCents ||
                reader.GetInt64(9) != decision.LineDiscountTotalCents ||
                reader.GetInt64(10) != decision.OrderDiscountTotalCents ||
                reader.GetInt64(11) != decision.FeeTotalCents ||
                reader.GetInt64(12) != decision.TaxCents ||
                reader.GetInt64(13) != decision.NetSaleTotalCents ||
                reader.GetInt64(14) != decision.CashTenderCents ||
                reader.GetInt64(15) != decision.AccountsReceivableTenderCents ||
                reader.GetInt64(16) != decision.AmountPaidCents ||
                reader.GetInt64(17) != decision.BalanceDueCents ||
                reader.GetInt64(18) != decision.ChangeCents ||
                !string.Equals(
                    reader.GetString(19),
                    decision.DiscountReason ?? string.Empty,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    reader.GetString(20),
                    decision.ContentHash,
                    StringComparison.Ordinal) ||
                DateTimeOffset.Parse(reader.GetString(21)).ToUniversalTime() !=
                    decision.CreatedAtUtc.ToUniversalTime())
            {
                return LocalSaleFactVerification.Conflict(
                    "Sync.OriginEchoC5DecisionMismatch",
                    "Origin-device V2 echo conflicts with durable local C5 financial decision evidence.");
            }
        }

        if (!LineDiscountsMatch(
                connection,
                transaction,
                decision) ||
            !FeesMatch(
                connection,
                transaction,
                decision))
        {
            return LocalSaleFactVerification.Conflict(
                "Sync.OriginEchoC5DetailMismatch",
                "Origin-device V2 echo conflicts with durable line-discount or fee evidence.");
        }

        return LocalSaleFactVerification.Match();
    }

    private static bool LineDiscountsMatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeSaleFinancialDecision decision)
    {
        if (Count(
                connection,
                transaction,
                "cafe_sale_financial_line_discounts",
                decision.FinancialDecisionId) !=
            decision.LineDecisions.Count)
            return false;

        foreach (CafeSaleFinancialLineDecision line in decision.LineDecisions)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM cafe_sale_financial_line_discounts
                WHERE financial_decision_id=$decision
                  AND sale_item_id=$item
                  AND line_discount_cents=$line
                  AND order_discount_allocated_cents=$order
                  AND total_discount_cents=$total;
                """;
            command.Parameters.AddWithValue("$decision", decision.FinancialDecisionId.ToString("D"));
            command.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
            command.Parameters.AddWithValue("$line", line.LineDiscountCents);
            command.Parameters.AddWithValue("$order", line.OrderDiscountAllocatedCents);
            command.Parameters.AddWithValue("$total", line.TotalDiscountCents);
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
                return false;
        }

        return true;
    }

    private static bool FeesMatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeSaleFinancialDecision decision)
    {
        using (SqliteCommand count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT COUNT(*)
                FROM cafe_sale_fees
                WHERE financial_decision_id=$decision;
                """;
            count.Parameters.AddWithValue("$decision", decision.FinancialDecisionId.ToString("D"));
            if (Convert.ToInt64(count.ExecuteScalar() ?? 0L) != decision.Fees.Count)
                return false;
        }

        foreach (CafeSaleFeeDecision fee in decision.Fees)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM cafe_sale_fees
                WHERE fee_id=$fee
                  AND financial_decision_id=$decision
                  AND sale_id=$sale
                  AND fee_type=$type
                  AND display_label_snapshot=$label
                  AND amount_cents=$amount
                  AND actor_user_id=$actor
                  AND COALESCE(reason,'')=COALESCE($reason,'')
                  AND created_at_utc=$at;
                """;
            command.Parameters.AddWithValue("$fee", fee.FeeId.ToString("D"));
            command.Parameters.AddWithValue("$decision", decision.FinancialDecisionId.ToString("D"));
            command.Parameters.AddWithValue("$sale", decision.SaleId.ToString("D"));
            command.Parameters.AddWithValue("$type", fee.FeeType);
            command.Parameters.AddWithValue("$label", fee.DisplayLabelSnapshot);
            command.Parameters.AddWithValue("$amount", fee.AmountCents);
            command.Parameters.AddWithValue("$actor", fee.ActorUserId.ToString("D"));
            command.Parameters.AddWithValue("$reason", fee.Reason ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$at", fee.CreatedAtUtc.ToUniversalTime().ToString("O"));
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
                return false;
        }

        return true;
    }

    private static long Count(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        Guid decisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM " + table +
            " WHERE financial_decision_id=$decision;";
        command.Parameters.AddWithValue(
            "$decision",
            decisionId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static bool NullableGuidEquals(
        SqliteDataReader reader,
        int ordinal,
        Guid? expected)
    {
        if (reader.IsDBNull(ordinal))
            return !expected.HasValue;
        return expected.HasValue &&
               Guid.Parse(reader.GetString(ordinal)) == expected.Value;
    }

    private static bool NullableIntEquals(
        SqliteDataReader reader,
        int ordinal,
        int? expected)
    {
        if (reader.IsDBNull(ordinal))
            return !expected.HasValue;
        return expected.HasValue &&
               reader.GetInt32(ordinal) == expected.Value;
    }
}

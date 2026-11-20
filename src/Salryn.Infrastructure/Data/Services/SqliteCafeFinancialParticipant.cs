using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Services;

internal static class SqliteCafeFinancialParticipant
{
    internal static Result<bool> Persist(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeSaleFinancialDecision decision)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.OperationId == Guid.Empty ||
            decision.FinancialDecisionId == Guid.Empty ||
            decision.SaleId == Guid.Empty ||
            string.IsNullOrWhiteSpace(decision.ContentHash))
        {
            return Result<bool>.Fail(Error.Validation(
                "CafeFinancial.InvalidDecision",
                "Financial decision requires stable operation, decision, sale and content hash authority."));
        }

        ExistingDecision? existingByOperation =
            ReadByOperation(connection, transaction, decision.OperationId);

        if (existingByOperation is not null)
        {
            bool exactReplay =
                existingByOperation.FinancialDecisionId == decision.FinancialDecisionId &&
                existingByOperation.SaleId == decision.SaleId &&
                string.Equals(
                    existingByOperation.ContentHash,
                    decision.ContentHash,
                    StringComparison.Ordinal);

            return exactReplay
                ? Result<bool>.Success(true)
                : Result<bool>.Fail(Error.Conflict(
                    "CafeFinancial.OperationIdPayloadConflict",
                    "Financial operation ID already exists with different immutable content."));
        }

        ExistingDecision? existingBySale =
            ReadBySale(connection, transaction, decision.SaleId);

        if (existingBySale is not null)
        {
            return Result<bool>.Fail(Error.Conflict(
                "CafeFinancial.SaleDecisionConflict",
                "Sale is already bound to a different financial decision."));
        }

        Result saleValidation =
            ValidateSaleEvidence(connection, transaction, decision);
        if (saleValidation.IsFailure)
        {
            return Result<bool>.Fail(saleValidation.ErrorDetail);
        }

        try
        {
            InsertDecision(connection, transaction, decision);

            foreach (CafeSaleFinancialLineDecision line in decision.LineDecisions)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_sale_financial_line_discounts(
                        financial_decision_id,sale_item_id,line_discount_cents,
                        order_discount_allocated_cents,total_discount_cents)
                    VALUES($decision,$item,$line,$order,$total);
                    """;
                command.Parameters.AddWithValue(
                    "$decision",
                    decision.FinancialDecisionId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$item",
                    line.SaleItemId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$line",
                    line.LineDiscountCents);
                command.Parameters.AddWithValue(
                    "$order",
                    line.OrderDiscountAllocatedCents);
                command.Parameters.AddWithValue(
                    "$total",
                    line.TotalDiscountCents);
                command.ExecuteNonQuery();
            }

            foreach (CafeSaleFeeDecision fee in decision.Fees)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_sale_fees(
                        fee_id,financial_decision_id,sale_id,fee_type,
                        display_label_snapshot,amount_cents,actor_user_id,
                        reason,created_at_utc)
                    VALUES(
                        $id,$decision,$sale,$type,$label,$amount,$actor,$reason,$at);
                    """;
                command.Parameters.AddWithValue("$id", fee.FeeId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$decision",
                    decision.FinancialDecisionId.ToString("D"));
                command.Parameters.AddWithValue("$sale", decision.SaleId.ToString("D"));
                command.Parameters.AddWithValue("$type", fee.FeeType);
                command.Parameters.AddWithValue("$label", fee.DisplayLabelSnapshot);
                command.Parameters.AddWithValue("$amount", fee.AmountCents);
                command.Parameters.AddWithValue("$actor", fee.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$reason",
                    string.IsNullOrWhiteSpace(fee.Reason)
                        ? DBNull.Value
                        : fee.Reason.Trim());
                command.Parameters.AddWithValue(
                    "$at",
                    fee.CreatedAtUtc.ToUniversalTime().ToString("O"));
                command.ExecuteNonQuery();
            }

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE sales
                    SET fee_total_cents=$fee
                    WHERE id=$sale
                      AND status=$completed;
                    """;
                update.Parameters.AddWithValue("$fee", decision.FeeTotalCents);
                update.Parameters.AddWithValue("$sale", decision.SaleId.ToString("D"));
                update.Parameters.AddWithValue(
                    "$completed",
                    Convert.ToInt32(SaleStatus.Completed));

                if (update.ExecuteNonQuery() != 1)
                {
                    return Result<bool>.Fail(Error.Conflict(
                        "CafeFinancial.SaleUpdateConflict",
                        "Committed sale was not available for financial decision binding."));
                }
            }

            return Result<bool>.Success(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<bool>.Fail(Error.Conflict(
                "CafeFinancial.PersistenceConflict",
                ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result<bool>.Fail(Error.Unexpected(
                "CafeFinancial.PersistenceFailed",
                ex.Message));
        }
    }

    private static Result ValidateSaleEvidence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeSaleFinancialDecision decision)
    {
        using SqliteCommand sale = connection.CreateCommand();
        sale.Transaction = transaction;
        sale.CommandText = """
            SELECT subtotal_cents,discount_cents,tax_cents,total_cents,
                   amount_paid_cents,change_cents,balance_due_cents,status
            FROM sales
            WHERE id=$sale
            LIMIT 1;
            """;
        sale.Parameters.AddWithValue("$sale", decision.SaleId.ToString("D"));

        using SqliteDataReader reader = sale.ExecuteReader();
        if (!reader.Read())
        {
            return Result.Fail(Error.NotFound(
                "CafeFinancial.SaleMissing",
                "Sale must exist before the financial decision is persisted."));
        }

        long subtotal = reader.GetInt64(0);
        long discount = reader.GetInt64(1);
        long tax = reader.GetInt64(2);
        long total = reader.GetInt64(3);
        long amountPaid = reader.GetInt64(4);
        long change = reader.GetInt64(5);
        long balanceDue = reader.GetInt64(6);
        SaleStatus status = (SaleStatus)reader.GetInt32(7);
        reader.Close();

        long expectedDiscount = checked(
            decision.LineDiscountTotalCents +
            decision.OrderDiscountTotalCents);

        if (status != SaleStatus.Completed ||
            subtotal != decision.GrossSubtotalCents ||
            discount != expectedDiscount ||
            tax != decision.TaxCents ||
            total != decision.NetSaleTotalCents ||
            amountPaid != decision.AmountPaidCents ||
            change != decision.ChangeCents ||
            balanceDue != decision.BalanceDueCents)
        {
            return Result.Fail(Error.Conflict(
                "CafeFinancial.SaleEvidenceMismatch",
                "Persisted Sale totals do not match the immutable financial decision."));
        }

        foreach (CafeSaleFinancialLineDecision line in decision.LineDecisions)
        {
            using SqliteCommand item = connection.CreateCommand();
            item.Transaction = transaction;
            item.CommandText = """
                SELECT discount_cents
                FROM sale_items
                WHERE id=$item AND sale_id=$sale
                LIMIT 1;
                """;
            item.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
            item.Parameters.AddWithValue("$sale", decision.SaleId.ToString("D"));
            object? value = item.ExecuteScalar();

            if (value is null or DBNull ||
                Convert.ToInt64(value) != line.TotalDiscountCents)
            {
                return Result.Fail(Error.Conflict(
                    "CafeFinancial.LineEvidenceMismatch",
                    "Persisted SaleItem discount evidence does not match the financial decision."));
            }
        }

        return Result.Success();
    }

    private static void InsertDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeSaleFinancialDecision decision)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_sale_financial_decisions(
                financial_decision_id,operation_id,store_id,order_id,order_revision_id,
                expected_order_version,sale_id,cashier_session_id,actor_user_id,
                gross_subtotal_cents,line_discount_total_cents,order_discount_total_cents,
                fee_total_cents,tax_cents,net_sale_total_cents,cash_tender_cents,
                accounts_receivable_tender_cents,booking_deposit_applied_cents,
                amount_paid_cents,balance_due_cents,
                change_cents,discount_reason,content_hash,created_at_utc)
            VALUES(
                $id,$operation,$store,$order,$revision,$expected,$sale,$session,$actor,
                $gross,$lineDiscount,$orderDiscount,$fee,$tax,$net,$cash,$ar,$bookingDeposit,
                $paid,$balance,$change,$reason,$hash,$at);
            """;

        command.Parameters.AddWithValue("$id", decision.FinancialDecisionId.ToString("D"));
        command.Parameters.AddWithValue("$operation", decision.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$store", decision.StoreId.ToString("D"));
        command.Parameters.AddWithValue(
            "$order",
            decision.OrderId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$revision",
            decision.OrderRevisionId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$expected",
            decision.ExpectedOrderVersion ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sale", decision.SaleId.ToString("D"));
        command.Parameters.AddWithValue("$session", decision.CashierSessionId.ToString("D"));
        command.Parameters.AddWithValue("$actor", decision.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$gross", decision.GrossSubtotalCents);
        command.Parameters.AddWithValue("$lineDiscount", decision.LineDiscountTotalCents);
        command.Parameters.AddWithValue("$orderDiscount", decision.OrderDiscountTotalCents);
        command.Parameters.AddWithValue("$fee", decision.FeeTotalCents);
        command.Parameters.AddWithValue("$tax", decision.TaxCents);
        command.Parameters.AddWithValue("$net", decision.NetSaleTotalCents);
        command.Parameters.AddWithValue("$cash", decision.CashTenderCents);
        command.Parameters.AddWithValue("$ar", decision.AccountsReceivableTenderCents);
        command.Parameters.AddWithValue(
            "$bookingDeposit",
            decision.BookingDepositAppliedCents);
        command.Parameters.AddWithValue("$paid", decision.AmountPaidCents);
        command.Parameters.AddWithValue("$balance", decision.BalanceDueCents);
        command.Parameters.AddWithValue("$change", decision.ChangeCents);
        command.Parameters.AddWithValue(
            "$reason",
            string.IsNullOrWhiteSpace(decision.DiscountReason)
                ? DBNull.Value
                : decision.DiscountReason.Trim());
        command.Parameters.AddWithValue("$hash", decision.ContentHash);
        command.Parameters.AddWithValue(
            "$at",
            decision.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static ExistingDecision? ReadByOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT financial_decision_id,sale_id,content_hash
            FROM cafe_sale_financial_decisions
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ExistingDecision(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2))
            : null;
    }

    private static ExistingDecision? ReadBySale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT financial_decision_id,sale_id,content_hash
            FROM cafe_sale_financial_decisions
            WHERE sale_id=$sale
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new ExistingDecision(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2))
            : null;
    }

    private sealed record ExistingDecision(
        Guid FinancialDecisionId,
        Guid SaleId,
        string ContentHash);
}

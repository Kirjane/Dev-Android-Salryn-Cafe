using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class SharedSqliteCafeBookingV3Participant
{
    public static async Task<Result> ApplyCanonicalAsync(
        SqliteSharedTransactionContext context,
        SyncOperationEnvelopeV1 envelope,
        SaleCommittedV3 payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        string? validation = SaleCommittedV3Validator.Validate(payload);
        if (validation is not null)
            return Result.Fail(
                Error.Validation(
                    "SaleCommittedV3.PayloadInvalid",
                    validation));

        SaleCommittedV1Sale sale = payload.SaleEvidence.Sale;
        if (!string.Equals(
                envelope.OperationType,
                SaleCommittedV3Contract.OperationType,
                StringComparison.Ordinal) ||
            envelope.SchemaVersion != SaleCommittedV3Contract.SchemaVersion ||
            envelope.OperationId == Guid.Empty ||
            envelope.AggregateId != sale.Id ||
            envelope.StoreId == Guid.Empty ||
            envelope.ActorUserId != sale.CashierUserId ||
            envelope.BaseVersion.HasValue)
            return Result.Fail(
                Error.Validation(
                    "SaleCommittedV3.EnvelopeAuthorityMismatch",
                    "V3 envelope operation/schema/sale/store/actor/base-version differs from canonical authority."));

        Guid? localStore = ReadDeploymentStore(
            context.Connection,
            context.Transaction);
        if (!localStore.HasValue || localStore.Value != envelope.StoreId)
            return Result.Fail(
                Error.Conflict(
                    "SaleCommittedV3.StorePrerequisiteConflict",
                    "V3 Store authority does not match local deployment."));

        if (SaleOrReceiptExists(
                context.Connection,
                context.Transaction,
                sale.Id,
                sale.ReceiptNumber))
            return Result.Fail(
                Error.Conflict(
                    "SaleCommittedV3.SaleCollision",
                    "V3 SaleId or receipt already exists before new canonical apply."));

        if (!OrderReady(
                context.Connection,
                context.Transaction,
                payload))
            return Result.Fail(
                Error.Conflict(
                    "SaleCommittedV3.OrderPrerequisiteConflict",
                    "V3 requires exact linked C4 Ready Order version/revision."));

        CafeBookingFinalizationBinding binding = new(
            payload.BookingId,
            payload.BookingRevisionId,
            payload.BookingOrderLinkId,
            payload.ExpectedBookingVersion);

        Result<CafeBookingFinalizationContext> prepared =
            SqliteCafeBookingFinalizationParticipant.Prepare(
                context.Connection,
                context.Transaction,
                binding,
                payload.OrderId,
                envelope.StoreId,
                envelope.OperationId);
        if (prepared.IsFailure)
            return Result.Fail(prepared.ErrorDetail);

        if (!ContextMatches(prepared.Value, payload))
            return Result.Fail(
                Error.Conflict(
                    "SaleCommittedV3.DepositAuthorityConflict",
                    "V3 canonical final IDs/deposit allocations differ from retained Booking authority."));

        Result<CompleteCheckoutSaleRequest> core =
            SaleCommittedV3Mapper.ToCore(payload);
        if (core.IsFailure)
            return Result.Fail(core.ErrorDetail);

        Result<CafeSaleFinancialDecision> financial =
            SaleCommittedV3Mapper.ToFinancialDecision(
                payload,
                envelope.StoreId);
        if (financial.IsFailure)
            return Result.Fail(financial.ErrorDetail);

        Result saleWrite = SqliteSaleWriteRepository.WriteCompletedSale(
            context.Connection,
            context.Transaction,
            core.Value.Sale,
            core.Value.Items,
            core.Value.Payments,
            core.Value.StockMovements,
            cancellationToken,
            grossSubtotalBeforeAllocatedDiscounts: true,
            bookingDepositAppliedCents:
                payload.BookingDepositAppliedCents);
        if (saleWrite.IsFailure)
            return saleWrite;

        Result<bool> financialWrite =
            SqliteCafeFinancialParticipant.Persist(
                context.Connection,
                context.Transaction,
                financial.Value);
        if (financialWrite.IsFailure)
            return Result.Fail(financialWrite.ErrorDetail);

        string hash =
            SaleCommittedV3CanonicalJson.ComputeHash(
                envelope.PayloadJson);

        Result c9 = SqliteCafeBookingFinalizationParticipant.Persist(
            context.Connection,
            context.Transaction,
            prepared.Value,
            payload,
            envelope.OperationId,
            envelope.StoreId,
            envelope.DeviceId,
            envelope.ActorUserId,
            envelope.OccurredAtLocal,
            hash);
        return c9;
    }

    internal static LocalSaleFactVerification VerifyOriginEcho(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SaleCommittedV3 payload)
    {
        LocalSaleFactVerification sale =
            LocalSaleCommittedV1FactVerifier.VerifyOriginEcho(
                connection,
                transaction,
                change,
                payload.SaleEvidence,
                SaleCommittedV3Contract.OperationType,
                SaleCommittedV3Contract.SchemaVersion);
        if (!sale.Matches)
            return sale;

        Result<CafeSaleFinancialDecision> financial =
            SaleCommittedV3Mapper.ToFinancialDecision(
                payload,
                change.StoreId);
        if (financial.IsFailure)
            return LocalSaleFactVerification.Conflict(
                financial.ErrorDetail.Code,
                financial.ErrorDetail.Message);

        if (!FinancialFactsMatch(
                connection,
                transaction,
                financial.Value))
            return LocalSaleFactVerification.Conflict(
                "Sync.OriginEchoV3FinancialMismatch",
                "Origin V3 echo conflicts with durable C5 financial evidence.");

        string hash =
            SaleCommittedV3CanonicalJson.ComputeHash(
                change.PayloadJson);
        Result replay =
            SqliteCafeBookingFinalizationParticipant.VerifyReplay(
                connection,
                transaction,
                payload,
                hash);
        if (replay.IsFailure)
            return LocalSaleFactVerification.Conflict(
                replay.ErrorDetail.Code,
                replay.ErrorDetail.Message);

        if (payload.DepositApplicationFactId.HasValue &&
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM cash_movements
                WHERE reference_id=$fact;
                """,
                ("$fact",
                    payload.DepositApplicationFactId.Value.ToString("D"))) != 0)
            return LocalSaleFactVerification.Conflict(
                "Sync.OriginEchoV3DepositCashConflict",
                "Booking deposit application unexpectedly owns physical cash movement.");

        return LocalSaleFactVerification.Match();
    }

    private static bool ContextMatches(
        CafeBookingFinalizationContext context,
        SaleCommittedV3 payload)
    {
        if (context.BookingId != payload.BookingId ||
            context.BookingRevisionId != payload.BookingRevisionId ||
            context.BookingOrderLinkId != payload.BookingOrderLinkId ||
            context.OrderId != payload.OrderId ||
            context.ExpectedBookingVersion !=
                payload.ExpectedBookingVersion ||
            context.ResultingBookingVersion !=
                payload.ResultingBookingVersion ||
            context.BookingSaleLinkId != payload.BookingSaleLinkId ||
            context.BookingDepositAppliedCents !=
                payload.BookingDepositAppliedCents ||
            context.DepositApplicationFactId !=
                payload.DepositApplicationFactId)
            return false;

        Dictionary<Guid,long> expected =
            payload.DepositAllocations.ToDictionary(
                x => x.ReceivedDepositFactId,
                x => x.AmountCents);
        Dictionary<Guid,long> actual =
            context.Allocations.ToDictionary(
                x => x.ReceivedDepositFactId,
                x => x.AmountCents);
        return actual.Count == expected.Count &&
            actual.All(x =>
                expected.TryGetValue(x.Key, out long amount) &&
                amount == x.Value);
    }

    private static bool OrderReady(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV3 payload) =>
        ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM cafe_orders o
            INNER JOIN cafe_booking_order_links l
              ON l.order_id=o.order_id
            WHERE o.order_id=$order
              AND o.current_version=$version
              AND o.current_revision_id=$revision
              AND o.state='Ready'
              AND o.completed_sale_id IS NULL
              AND l.booking_order_link_id=$link
              AND l.booking_id=$booking
              AND l.booking_revision_id=$bookingRevision;
            """,
            ("$order", payload.OrderId.ToString("D")),
            ("$version", payload.ExpectedOrderVersion),
            ("$revision", payload.OrderRevisionId.ToString("D")),
            ("$link", payload.BookingOrderLinkId.ToString("D")),
            ("$booking", payload.BookingId.ToString("D")),
            ("$bookingRevision",
                payload.BookingRevisionId.ToString("D"))) == 1;

    private static bool FinancialFactsMatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeSaleFinancialDecision d)
    {
        if (ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM cafe_sale_financial_decisions
                WHERE financial_decision_id=$id
                  AND operation_id=$operation
                  AND store_id=$store
                  AND order_id=$order
                  AND order_revision_id=$revision
                  AND expected_order_version=$expected
                  AND sale_id=$sale
                  AND cashier_session_id=$session
                  AND actor_user_id=$actor
                  AND gross_subtotal_cents=$gross
                  AND line_discount_total_cents=$line
                  AND order_discount_total_cents=$orderDiscount
                  AND fee_total_cents=$fee
                  AND tax_cents=$tax
                  AND net_sale_total_cents=$net
                  AND cash_tender_cents=$cash
                  AND accounts_receivable_tender_cents=$ar
                  AND booking_deposit_applied_cents=$deposit
                  AND amount_paid_cents=$paid
                  AND balance_due_cents=$balance
                  AND change_cents=$change
                  AND COALESCE(discount_reason,'')=COALESCE($reason,'')
                  AND content_hash=$hash
                  AND created_at_utc=$at;
                """,
                ("$id", d.FinancialDecisionId.ToString("D")),
                ("$operation", d.OperationId.ToString("D")),
                ("$store", d.StoreId.ToString("D")),
                ("$order", d.OrderId!.Value.ToString("D")),
                ("$revision", d.OrderRevisionId!.Value.ToString("D")),
                ("$expected", d.ExpectedOrderVersion!.Value),
                ("$sale", d.SaleId.ToString("D")),
                ("$session", d.CashierSessionId.ToString("D")),
                ("$actor", d.ActorUserId.ToString("D")),
                ("$gross", d.GrossSubtotalCents),
                ("$line", d.LineDiscountTotalCents),
                ("$orderDiscount", d.OrderDiscountTotalCents),
                ("$fee", d.FeeTotalCents),
                ("$tax", d.TaxCents),
                ("$net", d.NetSaleTotalCents),
                ("$cash", d.CashTenderCents),
                ("$ar", d.AccountsReceivableTenderCents),
                ("$deposit", d.BookingDepositAppliedCents),
                ("$paid", d.AmountPaidCents),
                ("$balance", d.BalanceDueCents),
                ("$change", d.ChangeCents),
                ("$reason", d.DiscountReason ?? (object)DBNull.Value),
                ("$hash", d.ContentHash),
                ("$at", d.CreatedAtUtc.ToUniversalTime().ToString("O"))) != 1)
            return false;

        foreach (CafeSaleFinancialLineDecision line in d.LineDecisions)
        {
            if (ScalarLong(
                    connection,
                    transaction,
                    """
                    SELECT COUNT(*)
                    FROM cafe_sale_financial_line_discounts
                    WHERE financial_decision_id=$decision
                      AND sale_item_id=$item
                      AND line_discount_cents=$line
                      AND order_discount_allocated_cents=$orderDiscount
                      AND total_discount_cents=$total;
                    """,
                    ("$decision",
                        d.FinancialDecisionId.ToString("D")),
                    ("$item", line.SaleItemId.ToString("D")),
                    ("$line", line.LineDiscountCents),
                    ("$orderDiscount",
                        line.OrderDiscountAllocatedCents),
                    ("$total", line.TotalDiscountCents)) != 1)
                return false;
        }

        foreach (CafeSaleFeeDecision fee in d.Fees)
        {
            if (ScalarLong(
                    connection,
                    transaction,
                    """
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
                    """,
                    ("$fee", fee.FeeId.ToString("D")),
                    ("$decision",
                        d.FinancialDecisionId.ToString("D")),
                    ("$sale", d.SaleId.ToString("D")),
                    ("$type", fee.FeeType),
                    ("$label", fee.DisplayLabelSnapshot),
                    ("$amount", fee.AmountCents),
                    ("$actor", fee.ActorUserId.ToString("D")),
                    ("$reason", fee.Reason ?? (object)DBNull.Value),
                    ("$at", fee.CreatedAtUtc
                        .ToUniversalTime().ToString("O"))) != 1)
                return false;
        }

        return true;
    }

    private static Guid? ReadDeploymentStore(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT store_id FROM deployment_identity LIMIT 1;";
        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out Guid id)
            ? id
            : null;
    }

    private static bool SaleOrReceiptExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        string receipt) =>
        ScalarLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM sales
            WHERE id=$sale OR receipt_no=$receipt;
            """,
            ("$sale", saleId.ToString("D")),
            ("$receipt", receipt)) != 0;

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
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Models;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

internal sealed record LocalSaleFactVerification(
    bool Matches,
    string? Code,
    string? Message)
{
    public static LocalSaleFactVerification Match() => new(true, null, null);

    public static LocalSaleFactVerification Conflict(string code, string message) =>
        new(false, code, message);
}

internal static class LocalSaleCommittedV1FactVerifier
{
    public static LocalSaleFactVerification VerifyOriginEcho(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SaleCommittedV1 payload) =>
        VerifyOriginEcho(
            connection,
            transaction,
            change,
            payload,
            SaleCommittedV1Contract.OperationType,
            SaleCommittedV1Contract.SchemaVersion);

    internal static LocalSaleFactVerification VerifyOriginEcho(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AppliedBusinessChangeV1 change,
        SaleCommittedV1 payload,
        string expectedOperationType,
        int expectedSchemaVersion)
    {
        using (SqliteCommand outbox = connection.CreateCommand())
        {
            outbox.Transaction = transaction;
            outbox.CommandText = """
                SELECT device_id, store_id, local_sequence, operation_type, schema_version,
                       aggregate_id, payload_json, payload_hash
                FROM sync_outbox
                WHERE operation_id=$operation
                LIMIT 1;
                """;
            outbox.Parameters.AddWithValue("$operation", change.SourceOperationId.ToString("D"));

            using SqliteDataReader reader = outbox.ExecuteReader();
            if (!reader.Read())
            {
                return LocalSaleFactVerification.Conflict(
                    "Sync.OriginEchoOutboxMissing",
                    "Origin-device echo has no matching durable local outbox evidence.");
            }

            bool envelopeMatches =
                Guid.Parse(reader.GetString(0)) == change.OriginDeviceId &&
                Guid.Parse(reader.GetString(1)) == change.StoreId &&
                reader.GetInt64(2) == change.OriginLocalSequence &&
                string.Equals(reader.GetString(3), expectedOperationType, StringComparison.Ordinal) &&
                reader.GetInt32(4) == expectedSchemaVersion &&
                Guid.Parse(reader.GetString(5)) == change.AggregateId &&
                string.Equals(reader.GetString(6), change.PayloadJson, StringComparison.Ordinal) &&
                string.Equals(reader.GetString(7), change.PayloadHash, StringComparison.Ordinal);

            if (!envelopeMatches)
            {
                return LocalSaleFactVerification.Conflict(
                    "Sync.OriginEchoOutboxMismatch",
                    "Origin-device echo conflicts with retained local outbox immutable evidence.");
            }
        }

        if (!SaleHeaderMatches(connection, transaction, payload.Sale))
        {
            return LocalSaleFactVerification.Conflict(
                "Sync.OriginEchoSaleMismatch",
                "Origin-device echo conflicts with the committed local sale header.");
        }

        if (!ItemsMatch(connection, transaction, payload) ||
            !PaymentsMatch(connection, transaction, payload) ||
            !StockMovementsMatch(connection, transaction, payload))
        {
            return LocalSaleFactVerification.Conflict(
                "Sync.OriginEchoDetailMismatch",
                "Origin-device echo conflicts with local sale item, payment, or stock evidence.");
        }

        if (!CashEvidenceMatches(connection, transaction, payload) ||
            !ReceivableEvidenceMatches(connection, transaction, payload))
        {
            return LocalSaleFactVerification.Conflict(
                "Sync.OriginEchoFinancialMismatch",
                "Origin-device echo conflicts with local cash or receivable evidence.");
        }

        return LocalSaleFactVerification.Match();
    }

    private static bool SaleHeaderMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1Sale sale)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT receipt_no, sale_date, cashier_user_id, cashier_session_id,
                   subtotal_cents, discount_cents, tax_cents, total_cents,
                   amount_paid_cents, change_cents, status,
                   customer_id, payment_status, balance_due_cents,
                   receivable_due_date, receivable_note, receipt_snapshot, created_at
            FROM sales
            WHERE id=$id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", sale.Id.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        return
            string.Equals(reader.GetString(0), sale.ReceiptNumber, StringComparison.Ordinal) &&
            SameTime(reader.GetString(1), sale.SaleDate) &&
            Guid.Parse(reader.GetString(2)) == sale.CashierUserId &&
            !reader.IsDBNull(3) && Guid.Parse(reader.GetString(3)) == sale.CashierSessionId &&
            reader.GetInt64(4) == sale.SubtotalCents &&
            reader.GetInt64(5) == sale.DiscountCents &&
            reader.GetInt64(6) == sale.TaxCents &&
            reader.GetInt64(7) == sale.TotalCents &&
            reader.GetInt64(8) == sale.AmountPaidCents &&
            reader.GetInt64(9) == sale.ChangeCents &&
            reader.GetInt32(10) == Convert.ToInt32(SaleStatus.Completed) &&
            NullableGuidEquals(reader, 11, sale.CustomerId) &&
            string.Equals(reader.GetString(12), sale.PaymentStatus, StringComparison.Ordinal) &&
            reader.GetInt64(13) == sale.BalanceDueCents &&
            NullableTextEquals(reader, 14, sale.ReceivableDueDate?.ToString("yyyy-MM-dd")) &&
            NullableTextEquals(reader, 15, sale.ReceivableNote) &&
            NullableTextEquals(reader, 16, sale.ReceiptSnapshot) &&
            SameTime(reader.GetString(17), sale.CreatedAt);
    }

    private static bool ItemsMatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1 payload)
    {
        if (Count(connection, transaction, "sale_items", "sale_id", payload.Sale.Id) != payload.Items.Count)
        {
            return false;
        }

        foreach (SaleCommittedV1Item item in payload.Items)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM sale_items
                WHERE id=$id AND sale_id=$sale AND product_id=$product
                  AND product_name_snapshot=$name
                  AND COALESCE(barcode_snapshot,'')=COALESCE($barcode,'')
                  AND unit_name_snapshot=$unit
                  AND qty_x1000=$qty
                  AND unit_price_cents=$price
                  AND COALESCE(cost_price_cents,-1)=COALESCE($cost,-1)
                  AND discount_cents=$discount
                  AND line_total_cents=$total;
                """;
            command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            command.Parameters.AddWithValue("$sale", item.SaleId.ToString("D"));
            command.Parameters.AddWithValue("$product", item.ProductId.ToString("D"));
            command.Parameters.AddWithValue("$name", item.ProductNameSnapshot);
            command.Parameters.AddWithValue("$barcode", item.BarcodeSnapshot ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$unit", item.UnitNameSnapshot);
            command.Parameters.AddWithValue("$qty", item.QuantityX1000);
            command.Parameters.AddWithValue("$price", item.UnitPriceCents);
            command.Parameters.AddWithValue("$cost", item.CostPriceCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$discount", item.DiscountCents);
            command.Parameters.AddWithValue("$total", item.LineTotalCents);
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PaymentsMatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1 payload)
    {
        if (Count(connection, transaction, "payments", "sale_id", payload.Sale.Id) != payload.Payments.Count)
        {
            return false;
        }

        foreach (SaleCommittedV1Payment payment in payload.Payments)
        {
            int method = payment.Method switch
            {
                SaleCommittedV1Contract.PaymentCash => Convert.ToInt32(PaymentMethod.Cash),
                SaleCommittedV1Contract.PaymentAccountsReceivable =>
                    Convert.ToInt32(PaymentMethod.AccountsReceivable),
                _ => int.MinValue
            };

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT method, amount_cents, paid_at
                FROM payments
                WHERE id=$id AND sale_id=$sale
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", payment.Id.ToString("D"));
            command.Parameters.AddWithValue("$sale", payment.SaleId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read() ||
                reader.GetInt32(0) != method ||
                reader.GetInt64(1) != payment.AmountCents ||
                !SameTime(reader.GetString(2), payment.PaidAt))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StockMovementsMatch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1 payload)
    {
        foreach (SaleCommittedV1StockMovement movement in payload.StockMovements)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*)
                FROM stock_movements
                WHERE id=$id AND product_id=$product
                  AND movement_type=$type
                  AND qty_change_x1000=$qty
                  AND COALESCE(cost_price_cents,-1)=COALESCE($cost,-1)
                  AND COALESCE(reference_type,'')=$referenceType
                  AND reference_id=$referenceId
                  AND user_id=$user;
                """;
            command.Parameters.AddWithValue("$id", movement.Id.ToString("D"));
            command.Parameters.AddWithValue("$product", movement.ProductId.ToString("D"));
            command.Parameters.AddWithValue("$type", Convert.ToInt32(StockMovementType.Sale));
            command.Parameters.AddWithValue("$qty", checked(-movement.QuantityChangeX1000));
            command.Parameters.AddWithValue("$cost", movement.CostPriceCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$referenceType", movement.ReferenceType);
            command.Parameters.AddWithValue("$referenceId", movement.ReferenceId.ToString("D"));
            command.Parameters.AddWithValue("$user", movement.UserId.ToString("D"));
            if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CashEvidenceMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1 payload)
    {
        long retainedCash =
            payload.Payments
                .Where(x => string.Equals(
                    x.Method,
                    SaleCommittedV1Contract.PaymentCash,
                    StringComparison.Ordinal))
                .Sum(x => x.AmountCents) -
            payload.Sale.ChangeCents;

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(delta_cents),0)
            FROM cash_movements
            WHERE reference_type='SALE'
              AND reference_id=$sale;
            """;
        command.Parameters.AddWithValue("$sale", payload.Sale.Id.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        long expectedCount = retainedCash > 0 ? 1 : 0;
        return reader.GetInt64(0) == expectedCount && reader.GetInt64(1) == retainedCash;
    }

    private static bool ReceivableEvidenceMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1 payload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(debit_cents),0)
            FROM accounts_receivable_entries
            WHERE sale_id=$sale
              AND entry_type='CreditSale';
            """;
        command.Parameters.AddWithValue("$sale", payload.Sale.Id.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        long expectedCount = payload.Sale.BalanceDueCents > 0 ? 1 : 0;
        return reader.GetInt64(0) == expectedCount &&
               reader.GetInt64(1) == payload.Sale.BalanceDueCents;
    }

    private static long Count(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        Guid id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM " + table + " WHERE " + column + "=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static bool SameTime(string persisted, DateTimeOffset expected) =>
        DateTimeOffset.Parse(persisted).ToUniversalTime() == expected.ToUniversalTime();

    private static bool NullableGuidEquals(
        SqliteDataReader reader,
        int ordinal,
        Guid? expected)
    {
        if (reader.IsDBNull(ordinal))
        {
            return !expected.HasValue;
        }

        return expected.HasValue && Guid.Parse(reader.GetString(ordinal)) == expected.Value;
    }

    private static bool NullableTextEquals(
        SqliteDataReader reader,
        int ordinal,
        string? expected)
    {
        string? actual = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }
}

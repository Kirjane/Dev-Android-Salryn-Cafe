// Recovered canonical prerequisite rules from Cafe Yza 4241ea1e; no parallel sale writer.
using Microsoft.Data.Sqlite;
using Salryn.Core.Models;
using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

public sealed record CentralSaleCommittedPrerequisiteResult(
    bool CanApply,
    string? ConflictCode,
    string? ConflictMessage)
{
    public static CentralSaleCommittedPrerequisiteResult Ready() =>
        new(true, null, null);

    public static CentralSaleCommittedPrerequisiteResult Conflict(
        string code,
        string message) =>
        new(false, code, message);
}

public static class CentralSaleCommittedPrerequisites
{
    public static Guid? ReadDeploymentStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id
            FROM deployment_identity
            ORDER BY created_at ASC
            LIMIT 1;
            """;

        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out Guid storeId)
            ? storeId
            : null;
    }

    public static CentralSaleCommittedPrerequisiteResult Validate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleCommittedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(payload);

        if (!Exists(
                connection,
                transaction,
                "SELECT 1 FROM users WHERE id=$id AND status=$status LIMIT 1;",
                payload.Sale.CashierUserId,
                ("$status", Convert.ToInt32(UserStatus.Active))))
        {
            return CentralSaleCommittedPrerequisiteResult.Conflict(
                "SaleCommittedV1.CentralCashierMissing",
                "Central cashier user is missing or inactive.");
        }

        if (!Exists(
                connection,
                transaction,
                """
                SELECT 1
                FROM cashier_sessions
                WHERE id=$id
                  AND user_id=$user
                  AND status=$status
                LIMIT 1;
                """,
                payload.Sale.CashierSessionId,
                ("$user", payload.Sale.CashierUserId.ToString("D")),
                ("$status", Convert.ToInt32(CashierSessionStatus.Open))))
        {
            return CentralSaleCommittedPrerequisiteResult.Conflict(
                "SaleCommittedV1.CentralSessionConflict",
                "Central cashier session is missing, closed, or owned by a different cashier.");
        }

        if (SaleOrReceiptExists(connection, transaction, payload.Sale.Id, payload.Sale.ReceiptNumber))
        {
            return CentralSaleCommittedPrerequisiteResult.Conflict(
                "SaleCommittedV1.CentralSaleConflict",
                "Central sale ID or receipt number already exists.");
        }

        foreach (Guid productId in payload.Items.Select(item => item.ProductId).Distinct())
        {
            if (!Exists(
                    connection,
                    transaction,
                    "SELECT 1 FROM products WHERE id=$id AND is_active=1 LIMIT 1;",
                    productId))
            {
                return CentralSaleCommittedPrerequisiteResult.Conflict(
                    "SaleCommittedV1.CentralProductMissing",
                    "A committed sale product is missing or inactive in central state.");
            }
        }

        foreach (IGrouping<Guid, SaleCommittedV1StockMovement> group in
                 payload.StockMovements.GroupBy(movement => movement.ProductId))
        {
            long required;
            try
            {
                required = group.Aggregate(
                    0L,
                    (sum, movement) => checked(sum + movement.QuantityChangeX1000));
            }
            catch (OverflowException)
            {
                return CentralSaleCommittedPrerequisiteResult.Conflict(
                    "SaleCommittedV1.CentralStockArithmeticOverflow",
                    "Central stock prerequisite arithmetic overflowed.");
            }

            using SqliteCommand stock = connection.CreateCommand();
            stock.Transaction = transaction;
            stock.CommandText = """
                SELECT stock_on_hand_x1000, track_stock, is_active
                FROM products
                WHERE id=$id
                LIMIT 1;
                """;
            stock.Parameters.AddWithValue("$id", group.Key.ToString("D"));

            using SqliteDataReader reader = stock.ExecuteReader();
            if (!reader.Read() ||
                reader.GetInt32(1) == 0 ||
                reader.GetInt32(2) == 0)
            {
                return CentralSaleCommittedPrerequisiteResult.Conflict(
                    "SaleCommittedV1.CentralTrackedProductMissing",
                    "Central tracked product prerequisite is missing or inactive.");
            }

            if (reader.GetInt64(0) < required)
            {
                return CentralSaleCommittedPrerequisiteResult.Conflict(
                    "SaleCommittedV1.CentralStockInsufficient",
                    "Central stock is insufficient for the locally committed sale.");
            }
        }

        if (payload.Sale.BalanceDueCents > 0)
        {
            if (!payload.Sale.CustomerId.HasValue ||
                !Exists(
                    connection,
                    transaction,
                    "SELECT 1 FROM customers WHERE id=$id AND is_active=1 LIMIT 1;",
                    payload.Sale.CustomerId.Value))
            {
                return CentralSaleCommittedPrerequisiteResult.Conflict(
                    "SaleCommittedV1.CentralCustomerMissing",
                    "Central AR customer is missing or inactive.");
            }
        }

        return CentralSaleCommittedPrerequisiteResult.Ready();
    }

    private static bool SaleOrReceiptExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        string receiptNumber)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM sales
            WHERE id=$id OR receipt_no=$receipt
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", saleId.ToString("D"));
        command.Parameters.AddWithValue("$receipt", receiptNumber);
        return command.ExecuteScalar() is not null;
    }

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Guid id,
        params (string Name, object Value)[] extra)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        foreach ((string name, object value) in extra)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar() is not null;
    }
}

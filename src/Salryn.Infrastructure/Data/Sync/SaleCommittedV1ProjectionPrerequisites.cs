using Microsoft.Data.Sqlite;
using Salryn.Core.Models;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

internal sealed record SaleCommittedV1ProjectionPrerequisiteResult(
    bool CanApply,
    string? Code,
    string? Message)
{
    public static SaleCommittedV1ProjectionPrerequisiteResult Ready() =>
        new(true, null, null);

    public static SaleCommittedV1ProjectionPrerequisiteResult Conflict(
        string code,
        string message) =>
        new(false, code, message);
}

internal static class SaleCommittedV1ProjectionPrerequisites
{
    public static SaleCommittedV1ProjectionPrerequisiteResult Validate(
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
            return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                "Sync.ProjectionCashierMissing",
                "Projection requires the canonical cashier prerequisite.");
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
            return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                "Sync.ProjectionSessionMissing",
                "Projection requires the matching open cashier session prerequisite.");
        }

        foreach (Guid productId in payload.Items.Select(x => x.ProductId).Distinct())
        {
            if (!Exists(
                    connection,
                    transaction,
                    "SELECT 1 FROM products WHERE id=$id AND is_active=1 LIMIT 1;",
                    productId))
            {
                return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                    "Sync.ProjectionProductMissing",
                    "Projection requires every canonical sale product to exist and be active.");
            }
        }

        foreach (IGrouping<Guid, SaleCommittedV1StockMovement> group in
                 payload.StockMovements.GroupBy(x => x.ProductId))
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
                return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                    "Sync.ProjectionStockArithmeticOverflow",
                    "Projection stock prerequisite arithmetic overflowed.");
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
            if (!reader.Read() || reader.GetInt32(1) == 0 || reader.GetInt32(2) == 0)
            {
                return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                    "Sync.ProjectionTrackedProductMissing",
                    "Projection requires an active stock-tracked product prerequisite.");
            }

            if (reader.GetInt64(0) < required)
            {
                return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                    "Sync.ProjectionStockInsufficient",
                    "Projection would make canonical local stock negative.");
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
                return SaleCommittedV1ProjectionPrerequisiteResult.Conflict(
                    "Sync.ProjectionCustomerMissing",
                    "Projection requires the canonical active AR customer prerequisite.");
            }
        }

        return SaleCommittedV1ProjectionPrerequisiteResult.Ready();
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

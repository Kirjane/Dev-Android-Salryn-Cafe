using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeManagementReportingService
{
    private static CafeReportCashSummary ReadCash(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        Dictionary<CashMovementType, long> sums =
            Enum.GetValues<CashMovementType>()
                .ToDictionary(x => x, _ => 0L);

        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT movement_type,COALESCE(SUM(delta_cents),0)
            FROM cash_movements
            WHERE created_at_utc >= $start
              AND created_at_utc < $end
            GROUP BY movement_type;
            """;
        command.Parameters.AddWithValue("$start", Iso(start));
        command.Parameters.AddWithValue("$end", Iso(end));

        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                CashMovementType type =
                    (CashMovementType)reader.GetInt32(0);
                sums[type] = reader.GetInt64(1);
            }
        }

        long net =
            sums.Values.Sum();

        return new(
            sums[CashMovementType.SaleCashReceipt],
            sums[CashMovementType.ReceivableCashCollection],
            sums[CashMovementType.CashIn],
            checked(-sums[CashMovementType.InventoryPurchase]),
            checked(-sums[CashMovementType.OperationalExpense]),
            checked(-sums[CashMovementType.CashRefund]),
            checked(-sums[CashMovementType.CashOut]),
            sums[CashMovementType.Reversal],
            net);
    }

    private static long ReadOperationalExpenseCents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(
                CASE
                    WHEN m.movement_type=$expense
                        THEN m.delta_cents
                    WHEN m.movement_type=$reversal
                         AND original.movement_type=$expense
                        THEN m.delta_cents
                    ELSE 0
                END),0)
            FROM cash_movements m
            LEFT JOIN cash_movements original
                ON original.id=m.reversal_of_movement_id
            WHERE m.created_at_utc >= $start
              AND m.created_at_utc < $end;
            """;
        command.Parameters.AddWithValue(
            "$expense",
            Convert.ToInt32(CashMovementType.OperationalExpense));
        command.Parameters.AddWithValue(
            "$reversal",
            Convert.ToInt32(CashMovementType.Reversal));
        command.Parameters.AddWithValue("$start", Iso(start));
        command.Parameters.AddWithValue("$end", Iso(end));

        long netDelta =
            Convert.ToInt64(
                command.ExecuteScalar() ?? 0L);

        return checked(-netDelta);
    }

    private static CafeReportArSummary ReadAccountsReceivable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        long debit =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(debit_cents),0)
                FROM accounts_receivable_entries
                WHERE created_at >= $start
                  AND created_at < $end;
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end)));

        long credit =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(credit_cents),0)
                FROM accounts_receivable_entries
                WHERE created_at >= $start
                  AND created_at < $end;
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end)));

        long cashCollections =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(amount_cents),0)
                FROM accounts_receivable_collections
                WHERE collected_at >= $start
                  AND collected_at < $end
                  AND voided_at IS NULL
                  AND (
                    COALESCE(TRIM(method),'')=''
                    OR UPPER(TRIM(method))='CASH');
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end)));

        long outstanding =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(
                    SUM(debit_cents) - SUM(credit_cents),
                    0)
                FROM accounts_receivable_entries;
                """);

        return new(
            debit,
            credit,
            cashCollections,
            outstanding);
    }

    private static CafeReportPurchaseSummary ReadPurchases(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COUNT(DISTINCT r.purchase_receipt_id),
                COUNT(DISTINCT CASE
                    WHEN r.correction_of_purchase_receipt_id IS NOT NULL
                    THEN r.purchase_receipt_id END),
                COALESCE(SUM(DISTINCT r.total_acquisition_cost_cents),0),
                COALESCE(SUM(CASE
                    WHEN l.stock_target_type='Ingredient'
                    THEN l.quantity_received_x1000 ELSE 0 END),0),
                COALESCE(SUM(CASE
                    WHEN l.stock_target_type='Product'
                    THEN l.quantity_received_x1000 ELSE 0 END),0)
            FROM purchase_receipts r
            LEFT JOIN purchase_receipt_lines l
                ON l.purchase_receipt_id=r.purchase_receipt_id
            WHERE r.received_at_utc >= $start
              AND r.received_at_utc < $end;
            """;
        command.Parameters.AddWithValue("$start", Iso(start));
        command.Parameters.AddWithValue("$end", Iso(end));

        using SqliteDataReader reader =
            command.ExecuteReader();
        reader.Read();

        // Net acquisition cost must not rely on DISTINCT when receipts share totals.
        long netCost =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(total_acquisition_cost_cents),0)
                FROM purchase_receipts
                WHERE received_at_utc >= $start
                  AND received_at_utc < $end;
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end)));

        return new(
            reader.GetInt32(0),
            reader.GetInt32(1),
            netCost,
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private static CafeReportWasteSummary ReadWaste(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        int eventCount =
            checked((int)ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM inventory_waste_events
                WHERE occurred_at_utc >= $start
                  AND occurred_at_utc < $end;
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end))));

        int lineCount =
            checked((int)ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM inventory_waste_lines l
                INNER JOIN inventory_waste_events e
                    ON e.waste_event_id=l.waste_event_id
                WHERE e.occurred_at_utc >= $start
                  AND e.occurred_at_utc < $end;
                """,
                ("$start", Iso(start)),
                ("$end", Iso(end))));

        List<WasteMovementFact> movements =
            ReadWasteMovements(
                connection,
                transaction,
                start,
                end);

        long ingredientWaste = 0;
        long productWaste = 0;
        long knownCost = 0;
        int valued = 0;
        int missing = 0;
        List<string> missingLabels = new();

        foreach (WasteMovementFact movement in movements)
        {
            long recognizedWasteX1000 =
                checked(-movement.QuantityDeltaX1000);

            if (movement.TargetType == "Ingredient")
                ingredientWaste =
                    checked(
                        ingredientWaste +
                        recognizedWasteX1000);
            else
                productWaste =
                    checked(
                        productWaste +
                        recognizedWasteX1000);

            AcquisitionBasis basis =
                ReadAcquisitionBasis(
                    connection,
                    transaction,
                    movement.TargetType,
                    movement.TargetId,
                    movement.OccurredAtUtc);

            if (!basis.IsValid)
            {
                missing++;
                missingLabels.Add(
                    movement.TargetType + ": " +
                    movement.NameSnapshot);
                continue;
            }

            long cost =
                RoundSignedWeightedCost(
                    recognizedWasteX1000,
                    basis);

            knownCost =
                checked(knownCost + cost);
            valued++;
        }

        CafeReportValuationCoverage coverage = new(
            missing == 0
                ? CafeReportCostCoverageStates.Complete
                : CafeReportCostCoverageStates.Incomplete,
            valued,
            missing,
            knownCost,
            missingLabels
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray());

        return new(
            eventCount,
            lineCount,
            ingredientWaste,
            productWaste,
            coverage);
    }

    private static List<WasteMovementFact> ReadWasteMovements(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        List<WasteMovementFact> rows = new();

        using (SqliteCommand ingredient = connection.CreateCommand())
        {
            ingredient.Transaction = transaction;
            ingredient.CommandText = """
                SELECT
                    m.ingredient_id,
                    i.name,
                    m.quantity_delta_x1000,
                    m.created_at_utc
                FROM ingredient_stock_movements m
                INNER JOIN ingredients i
                    ON i.id=m.ingredient_id
                WHERE m.movement_kind IN ('Waste','WasteCorrection')
                  AND m.created_at_utc >= $start
                  AND m.created_at_utc < $end
                ORDER BY m.created_at_utc,m.id;
                """;
            ingredient.Parameters.AddWithValue("$start", Iso(start));
            ingredient.Parameters.AddWithValue("$end", Iso(end));

            using SqliteDataReader reader =
                ingredient.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(
                    new(
                        "Ingredient",
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetInt64(2),
                        ParseDate(reader.GetString(3))));
            }
        }

        using (SqliteCommand product = connection.CreateCommand())
        {
            product.Transaction = transaction;
            product.CommandText = """
                SELECT
                    m.product_id,
                    p.name,
                    m.qty_change_x1000,
                    m.created_at
                FROM stock_movements m
                INNER JOIN products p
                    ON p.id=m.product_id
                WHERE UPPER(TRIM(COALESCE(m.reference_type,'')))='INVENTORYWASTE'
                  AND m.created_at >= $start
                  AND m.created_at < $end
                ORDER BY m.created_at,m.id;
                """;
            product.Parameters.AddWithValue("$start", Iso(start));
            product.Parameters.AddWithValue("$end", Iso(end));

            using SqliteDataReader reader =
                product.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(
                    new(
                        "Product",
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetInt64(2),
                        ParseDate(reader.GetString(3))));
            }
        }

        return rows;
    }

    private static CafeReportInventorySummary ReadInventory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset atUtc)
    {
        long ingredientQuantity =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(stock_on_hand_x1000),0)
                FROM ingredients
                WHERE is_active=1;
                """);

        long productQuantity =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(stock_on_hand_x1000),0)
                FROM products
                WHERE is_active=1
                  AND track_stock=1;
                """);

        List<InventoryPosition> positions = new();

        using (SqliteCommand ingredient = connection.CreateCommand())
        {
            ingredient.Transaction = transaction;
            ingredient.CommandText = """
                SELECT id,name,stock_on_hand_x1000
                FROM ingredients
                WHERE is_active=1
                  AND stock_on_hand_x1000 <> 0
                ORDER BY name,id;
                """;

            using SqliteDataReader reader =
                ingredient.ExecuteReader();

            while (reader.Read())
            {
                positions.Add(
                    new(
                        "Ingredient",
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetInt64(2)));
            }
        }

        using (SqliteCommand product = connection.CreateCommand())
        {
            product.Transaction = transaction;
            product.CommandText = """
                SELECT id,name,stock_on_hand_x1000
                FROM products
                WHERE is_active=1
                  AND track_stock=1
                  AND stock_on_hand_x1000 <> 0
                ORDER BY name,id;
                """;

            using SqliteDataReader reader =
                product.ExecuteReader();

            while (reader.Read())
            {
                positions.Add(
                    new(
                        "Product",
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetInt64(2)));
            }
        }

        long knownValue = 0;
        int valued = 0;
        int missing = 0;
        List<string> missingLabels = new();

        foreach (InventoryPosition position in positions)
        {
            AcquisitionBasis basis =
                ReadAcquisitionBasis(
                    connection,
                    transaction,
                    position.TargetType,
                    position.TargetId,
                    atUtc);

            if (!basis.IsValid)
            {
                missing++;
                missingLabels.Add(
                    position.TargetType + ": " +
                    position.Name);
                continue;
            }

            knownValue =
                checked(
                    knownValue +
                    RoundSignedWeightedCost(
                        position.QuantityX1000,
                        basis));
            valued++;
        }

        return new(
            ingredientQuantity,
            productQuantity,
            new(
                missing == 0
                    ? CafeReportCostCoverageStates.Complete
                    : CafeReportCostCoverageStates.Incomplete,
                valued,
                missing,
                knownValue,
                missingLabels
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
    }

    private static IReadOnlyList<CafeReportRecipeCostRow>
        ReadRecipeCosts(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Guid storeId,
            DateTimeOffset atUtc)
    {
        string? revision =
            ScalarString(
                connection,
                transaction,
                """
                SELECT revision_id
                FROM recipe_book_current_publication
                WHERE store_id=$store
                LIMIT 1;
                """,
                ("$store", storeId.ToString("D")));

        if (!Guid.TryParse(
                revision,
                out Guid revisionId))
            return Array.Empty<CafeReportRecipeCostRow>();

        List<RecipeHeader> recipes = new();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT recipe_id,name,yield_x1000
                FROM recipe_book_revision_recipes
                WHERE revision_id=$revision
                ORDER BY display_order,name,recipe_id;
                """;
            command.Parameters.AddWithValue(
                "$revision",
                revisionId.ToString("D"));

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                recipes.Add(
                    new(
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetInt64(2)));
            }
        }

        List<CafeReportRecipeCostRow> result = new();

        foreach (RecipeHeader recipe in recipes)
        {
            List<RecipeIngredientFact> lines = new();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT ingredient_id,normalized_quantity_x1000
                    FROM recipe_book_revision_lines
                    WHERE revision_id=$revision
                      AND recipe_id=$recipe
                    ORDER BY display_order,ingredient_id;
                    """;
                command.Parameters.AddWithValue(
                    "$revision",
                    revisionId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$recipe",
                    recipe.RecipeId.ToString("D"));

                using SqliteDataReader reader =
                    command.ExecuteReader();

                while (reader.Read())
                {
                    lines.Add(
                        new(
                            Guid.Parse(reader.GetString(0)),
                            reader.GetInt64(1)));
                }
            }

            long known = 0;
            int missing = 0;

            foreach (RecipeIngredientFact line in lines)
            {
                AcquisitionBasis basis =
                    ReadAcquisitionBasis(
                        connection,
                        transaction,
                        "Ingredient",
                        line.IngredientId,
                        atUtc);

                if (!basis.IsValid)
                {
                    missing++;
                    continue;
                }

                known =
                    checked(
                        known +
                        RoundWeightedCost(
                            line.QuantityX1000,
                            basis));
            }

            result.Add(
                new(
                    recipe.RecipeId,
                    recipe.Name,
                    revisionId,
                    recipe.YieldX1000,
                    known,
                    missing == 0
                        ? CafeReportCostCoverageStates.Complete
                        : CafeReportCostCoverageStates.Incomplete,
                    lines.Count,
                    missing));
        }

        return result;
    }

    private static IReadOnlyList<CafeReportSessionRow>
        ReadCashierSessions(
            SqliteConnection connection,
            SqliteTransaction transaction,
            DateTimeOffset start,
            DateTimeOffset end)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                r.session_id,
                r.cashier_user_id,
                u.display_name,
                r.opened_at_utc,
                r.closed_at_utc,
                r.gross_sales_cents,
                r.net_sales_cents,
                r.cash_sales_cents,
                r.noncash_sales_cents,
                r.expected_closing_cash_cents,
                r.declared_closing_cash_cents,
                r.variance_cents,
                r.reconciliation_basis,
                r.ledger_net_delta_cents
            FROM cashier_session_reports r
            INNER JOIN users u
                ON u.id=r.cashier_user_id
            WHERE r.closed_at_utc >= $start
              AND r.closed_at_utc < $end
            ORDER BY r.closed_at_utc DESC,r.session_id;
            """;
        command.Parameters.AddWithValue("$start", Iso(start));
        command.Parameters.AddWithValue("$end", Iso(end));

        List<CafeReportSessionRow> rows = new();

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(
                new(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    ParseDate(reader.GetString(3)),
                    ParseDate(reader.GetString(4)),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.IsDBNull(10)
                        ? null
                        : reader.GetInt64(10),
                    reader.IsDBNull(11)
                        ? null
                        : reader.GetInt64(11),
                    reader.GetString(12),
                    reader.IsDBNull(13)
                        ? null
                        : reader.GetInt64(13)));
        }

        return rows;
    }

    private static CafeReportBookingSummary ReadBookings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(CASE WHEN r.booking_mode='Event' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN r.booking_mode='CoffeeCart' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN b.status='Draft' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN b.status='Confirmed' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN b.status='ReadyForFulfillment' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN b.status='Fulfilled' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN b.status='Cancelled' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(r.quoted_total_cents),0)
            FROM cafe_bookings b
            INNER JOIN cafe_booking_revisions r
                ON r.revision_id=b.current_revision_id
            WHERE b.store_id=$store
              AND b.created_at_utc >= $start
              AND b.created_at_utc < $end;
            """;
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        command.Parameters.AddWithValue("$start", Iso(start));
        command.Parameters.AddWithValue("$end", Iso(end));

        using SqliteDataReader reader =
            command.ExecuteReader();
        reader.Read();

        long received =
            ReadBookingDepositTotal(
                connection,
                transaction,
                storeId,
                start,
                end,
                "Received");
        long refunded =
            ReadBookingDepositTotal(
                connection,
                transaction,
                storeId,
                start,
                end,
                "Refunded");
        long applied =
            ReadBookingDepositTotal(
                connection,
                transaction,
                storeId,
                start,
                end,
                "Applied");

        return new(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt64(8),
            received,
            refunded,
            applied);
    }

    private static long ReadBookingDepositTotal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        DateTimeOffset start,
        DateTimeOffset end,
        string factType) =>
        ScalarLong(
            connection,
            transaction,
            """
            SELECT COALESCE(SUM(amount_cents),0)
            FROM cafe_booking_deposit_facts
            WHERE store_id=$store
              AND fact_type=$type
              AND occurred_at_utc >= $start
              AND occurred_at_utc < $end;
            """,
            ("$store", storeId.ToString("D")),
            ("$type", factType),
            ("$start", Iso(start)),
            ("$end", Iso(end)));

    private static CafeReportDeliverySummary ReadDelivery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        using SqliteCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(CASE WHEN status='Pending' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN status='Dispatched' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN status='Delivered' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN status='Failed' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN status='Cancelled' THEN 1 ELSE 0 END),0)
            FROM cafe_delivery_fulfillments
            WHERE store_id=$store
              AND created_at_utc >= $start
              AND created_at_utc < $end;
            """;
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        command.Parameters.AddWithValue("$start", Iso(start));
        command.Parameters.AddWithValue("$end", Iso(end));

        using SqliteDataReader reader =
            command.ExecuteReader();
        reader.Read();

        long fee =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COALESCE(SUM(f.amount_cents),0)
                FROM cafe_sale_fees f
                INNER JOIN sales s
                    ON s.id=f.sale_id
                WHERE f.fee_type='Delivery'
                  AND s.status=$completed
                  AND s.sale_date >= $start
                  AND s.sale_date < $end;
                """,
                ("$completed", Convert.ToInt32(SaleStatus.Completed)),
                ("$start", Iso(start)),
                ("$end", Iso(end)));

        return new(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            fee);
    }

    private static long RoundSignedWeightedCost(
        long quantityX1000,
        AcquisitionBasis basis)
    {
        if (!basis.IsValid)
            throw new InvalidOperationException(
                "Historical acquisition basis is incomplete.");

        decimal exact =
            decimal.Multiply(
                quantityX1000,
                basis.NetCostCents) /
            basis.NetQuantityX1000;

        return checked(
            (long)Math.Round(
                exact,
                0,
                MidpointRounding.AwayFromZero));
    }

    private static string? ScalarString(
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

        object? scalarValue =
            command.ExecuteScalar();

        return scalarValue is null or DBNull
            ? null
            : Convert.ToString(scalarValue);
    }

    private sealed record WasteMovementFact(
        string TargetType,
        Guid TargetId,
        string NameSnapshot,
        long QuantityDeltaX1000,
        DateTimeOffset OccurredAtUtc);

    private sealed record InventoryPosition(
        string TargetType,
        Guid TargetId,
        string Name,
        long QuantityX1000);

    private sealed record RecipeHeader(
        Guid RecipeId,
        string Name,
        long YieldX1000);

    private sealed record RecipeIngredientFact(
        Guid IngredientId,
        long QuantityX1000);
}

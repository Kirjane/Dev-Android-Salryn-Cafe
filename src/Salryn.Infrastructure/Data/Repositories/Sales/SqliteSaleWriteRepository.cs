using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Repositories.Sales;

public sealed class SqliteSaleWriteRepository :
    SqliteRepositoryBase,
    ISaleWriteRepository
{
    public SqliteSaleWriteRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result> AddCompletedSaleAsync(
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<StockMovement> stockMovements,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            Result writeResult = WriteCompletedSale(
                connection,
                transaction,
                sale,
                items,
                payments,
                stockMovements,
                cancellationToken);

            if (writeResult.IsFailure)
            {
                transaction.Rollback();
                return Task.FromResult(writeResult);
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Task.FromResult(Unexpected("Sales.AddCompletedSaleFailed", ex));
        }
    }

    internal static Result WriteCompletedSale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<StockMovement> stockMovements,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateCompletedSale(
            sale,
            items,
            payments,
            stockMovements);

        if (validation.IsFailure)
        {
            return validation;
        }

        try
        {
            // Kirch: shared official checkout uses this same write path so sale rows and journal evidence cannot drift.
            InsertSale(connection, transaction, sale);

            foreach (SaleItem item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InsertSaleItem(connection, transaction, item);
            }

            foreach (Payment payment in payments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InsertPayment(connection, transaction, payment);
            }

            foreach (StockMovement movement in stockMovements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Result stockResult = ApplyStockMovement(
                    connection,
                    transaction,
                    movement);

                if (stockResult.IsFailure)
                {
                    return stockResult;
                }
            }

            if (sale.BalanceDue.Cents > 0)
            {
                InsertReceivableDebit(connection, transaction, sale);
            }

            return Result.Success();
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ToAddSaleUniqueConflict(ex);
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidReference",
                "Sale contains an invalid cashier, session, product, payment, stock, or user reference."));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Unexpected("Sales.AddCompletedSaleFailed", ex);
        }
    }

    public Task<Result> MarkVoidedAsync(
        Guid saleId,
        Guid voidedByUserId,
        string reason,
        IReadOnlyList<StockMovement> reversalMovements,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateVoidRequest(
            saleId,
            voidedByUserId,
            reason,
            reversalMovements);

        if (validation.IsFailure)
        {
            return Task.FromResult(validation);
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = """
                UPDATE sales
                SET
                    status = $status,
                    voided_by_user_id = $voidedByUserId,
                    void_reason = $voidReason,
                    voided_at = $voidedAt
                WHERE id = $id
                  AND status = $completedStatus;
                """;

            command.Parameters.AddWithValue("$id", saleId.ToString("D"));
            command.Parameters.AddWithValue("$status", Convert.ToInt32(SaleStatus.Voided));
            command.Parameters.AddWithValue("$voidedByUserId", voidedByUserId.ToString("D"));
            command.Parameters.AddWithValue("$voidReason", reason.Trim());
            command.Parameters.AddWithValue("$voidedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$completedStatus", Convert.ToInt32(SaleStatus.Completed));

            int affectedRows = command.ExecuteNonQuery();

            if (affectedRows == 0)
            {
                transaction.Rollback();

                return Task.FromResult(Result.Fail(Error.NotFound(
                    "Sales.NotFoundOrNotVoidable",
                    "Sale was not found or is not voidable.")));
            }

            foreach (StockMovement movement in reversalMovements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Result stockResult = ApplyStockMovement(
                    connection,
                    transaction,
                    movement);

                if (stockResult.IsFailure)
                {
                    transaction.Rollback();
                    return Task.FromResult(stockResult);
                }
            }

            transaction.Commit();

            return Task.FromResult(Result.Success());
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Conflict(
                "Sales.DuplicateStockMovement",
                "One or more stock movement IDs already exist.")));
        }
        catch (SqliteException ex) when (IsForeignKeyConstraintViolation(ex))
        {
            return Task.FromResult(Result.Fail(Error.Validation(
                "Sales.InvalidReference",
                "Sale void contains an invalid sale, user, product, or stock movement reference.")));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Task.FromResult(Unexpected("Sales.MarkVoidedFailed", ex));
        }
    }

    private static void InsertSale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Sale sale)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sales (
                id,
                receipt_no,
                sale_date,
                cashier_user_id,
                cashier_session_id,
                subtotal_cents,
                discount_cents,
                tax_cents,
                total_cents,
                amount_paid_cents,
                change_cents,
                status,
                voided_by_user_id,
                void_reason,
                voided_at,
                receipt_snapshot,
                created_at,
                customer_id,
                payment_status,
                balance_due_cents,
                receivable_due_date,
                receivable_note
            )
            VALUES (
                $id,
                $receiptNo,
                $saleDate,
                $cashierUserId,
                $cashierSessionId,
                $subtotalCents,
                $discountCents,
                $taxCents,
                $totalCents,
                $amountPaidCents,
                $changeCents,
                $status,
                $voidedByUserId,
                $voidReason,
                $voidedAt,
                $receiptSnapshot,
                $createdAt,
                $customerId,
                $paymentStatus,
                $balanceDueCents,
                $receivableDueDate,
                $receivableNote
            );
            """;

        command.Parameters.AddWithValue("$id", sale.Id.ToString("D"));
        command.Parameters.AddWithValue("$receiptNo", sale.ReceiptNumber.Trim());
        command.Parameters.AddWithValue("$saleDate", sale.SaleDate.ToString("O"));
        command.Parameters.AddWithValue("$cashierUserId", sale.CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$cashierSessionId", DbNullableGuid(sale.CashierSessionId));
        command.Parameters.AddWithValue("$subtotalCents", sale.Subtotal.Cents);
        command.Parameters.AddWithValue("$discountCents", sale.Discount.Cents);
        command.Parameters.AddWithValue("$taxCents", sale.Tax.Cents);
        command.Parameters.AddWithValue("$totalCents", sale.Total.Cents);
        command.Parameters.AddWithValue("$amountPaidCents", sale.AmountPaid.Cents);
        command.Parameters.AddWithValue("$changeCents", sale.Change.Cents);
        command.Parameters.AddWithValue("$status", Convert.ToInt32(sale.Status));
        command.Parameters.AddWithValue("$voidedByUserId", DbNullableGuid(sale.VoidedByUserId));
        command.Parameters.AddWithValue("$voidReason", DbNullableText(sale.VoidReason));
        command.Parameters.AddWithValue("$voidedAt", sale.VoidedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$receiptSnapshot", DbNullableText(sale.ReceiptSnapshot));
        command.Parameters.AddWithValue("$createdAt", sale.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$customerId", DbNullableGuid(sale.CustomerId));
        command.Parameters.AddWithValue("$paymentStatus", sale.PaymentStatus.ToString());
        command.Parameters.AddWithValue("$balanceDueCents", sale.BalanceDue.Cents);
        command.Parameters.AddWithValue("$receivableDueDate", sale.ReceivableDueDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$receivableNote", DbNullableText(sale.ReceivableNote));

        command.ExecuteNonQuery();
    }

    private static void InsertReceivableDebit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Sale sale)
    {
        if (!sale.CustomerId.HasValue)
        {
            throw new InvalidOperationException("A customer is required for an accounts receivable debit.");
        }

        using SqliteCommand balance = connection.CreateCommand();
        balance.Transaction = transaction;
        balance.CommandText =
            "SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$customer ORDER BY created_at DESC, id DESC LIMIT 1;";
        balance.Parameters.AddWithValue("$customer", sale.CustomerId.Value.ToString("D"));
        object? value = balance.ExecuteScalar();
        long current = value is null or DBNull ? 0 : Convert.ToInt64(value);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO accounts_receivable_entries
            (id,customer_id,sale_id,collection_id,entry_type,debit_cents,credit_cents,
             balance_after_cents,due_date,note,created_by_user_id,created_at)
            VALUES ($id,$customer,$sale,NULL,'CreditSale',$debit,0,$balance,$due,$note,$user,$at);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$customer", sale.CustomerId.Value.ToString("D"));
        command.Parameters.AddWithValue("$sale", sale.Id.ToString("D"));
        command.Parameters.AddWithValue("$debit", sale.BalanceDue.Cents);
        command.Parameters.AddWithValue("$balance", checked(current + sale.BalanceDue.Cents));
        command.Parameters.AddWithValue("$due", sale.ReceivableDueDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$note", DbNullableText(sale.ReceivableNote));
        command.Parameters.AddWithValue("$user", sale.CashierUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", sale.CreatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertSaleItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SaleItem item)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sale_items (
                id,
                sale_id,
                product_id,
                product_name_snapshot,
                barcode_snapshot,
                unit_name_snapshot,
                qty_x1000,
                unit_price_cents,
                cost_price_cents,
                discount_cents,
                line_total_cents
            )
            VALUES (
                $id,
                $saleId,
                $productId,
                $productNameSnapshot,
                $barcodeSnapshot,
                $unitNameSnapshot,
                $quantityX1000,
                $unitPriceCents,
                $costPriceCents,
                $discountCents,
                $lineTotalCents
            );
            """;

        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$saleId", item.SaleId.ToString("D"));
        command.Parameters.AddWithValue("$productId", item.ProductId.ToString("D"));
        command.Parameters.AddWithValue("$productNameSnapshot", item.ProductNameSnapshot.Trim());
        command.Parameters.AddWithValue("$barcodeSnapshot", DbNullableText(item.BarcodeSnapshot));
        command.Parameters.AddWithValue("$unitNameSnapshot", item.UnitNameSnapshot.Trim());
        command.Parameters.AddWithValue("$quantityX1000", item.Quantity.ValueX1000);
        command.Parameters.AddWithValue("$unitPriceCents", item.UnitPrice.Cents);
        command.Parameters.AddWithValue("$costPriceCents", DbNullableMoney(item.CostPrice));
        command.Parameters.AddWithValue("$discountCents", item.Discount.Cents);
        command.Parameters.AddWithValue("$lineTotalCents", item.LineTotal.Cents);

        command.ExecuteNonQuery();
    }

    private static void InsertPayment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Payment payment)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO payments (
                id,
                sale_id,
                method,
                amount_cents,
                paid_at
            )
            VALUES (
                $id,
                $saleId,
                $method,
                $amountCents,
                $paidAt
            );
            """;

        command.Parameters.AddWithValue("$id", payment.Id.ToString("D"));
        command.Parameters.AddWithValue("$saleId", payment.SaleId.ToString("D"));
        command.Parameters.AddWithValue("$method", Convert.ToInt32(payment.Method));
        command.Parameters.AddWithValue("$amountCents", payment.Amount.Cents);
        command.Parameters.AddWithValue("$paidAt", payment.PaidAt.ToString("O"));

        command.ExecuteNonQuery();
    }

    private static Result ApplyStockMovement(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StockMovement movement)
    {
        ProductStockState? productState = GetProductStockState(
            connection,
            transaction,
            movement.ProductId);

        if (productState is null || !productState.IsActive)
        {
            return Result.Fail(Error.NotFound(
                "Sales.ProductNotFoundOrInactive",
                "Product was not found or is inactive."));
        }

        if (!productState.TrackStock)
        {
            return Result.Fail(Error.Validation(
                "Sales.ProductNotStockTracked",
                "Product is not configured for stock tracking."));
        }

        long signedQuantityChange = GetSignedQuantityChangeX1000(movement);
        long newStockOnHand = checked(productState.StockOnHandX1000 + signedQuantityChange);

        if (newStockOnHand < 0)
        {
            return Result.Fail(Error.Conflict(
                "Sales.NegativeStockNotAllowed",
                "Sale stock movement would make product stock negative."));
        }

        UpdateProductStock(
            connection,
            transaction,
            movement.ProductId,
            newStockOnHand);

        InsertStockMovement(
            connection,
            transaction,
            movement,
            signedQuantityChange);

        return Result.Success();
    }

    private static ProductStockState? GetProductStockState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                stock_on_hand_x1000,
                track_stock,
                is_active
            FROM products
            WHERE id = $id
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$id", productId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new ProductStockState(
            reader.GetInt64(0),
            reader.GetInt32(1) != 0,
            reader.GetInt32(2) != 0);
    }

    private static void UpdateProductStock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId,
        long newStockOnHandX1000)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            UPDATE products
            SET
                stock_on_hand_x1000 = $stockOnHandX1000,
                updated_at = $updatedAt
            WHERE id = $id
              AND is_active = 1
              AND track_stock = 1;
            """;

        command.Parameters.AddWithValue("$id", productId.ToString("D"));
        command.Parameters.AddWithValue("$stockOnHandX1000", newStockOnHandX1000);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        int affectedRows = command.ExecuteNonQuery();

        if (affectedRows == 0)
        {
            throw new InvalidOperationException("Product stock update failed.");
        }
    }

    private static void InsertStockMovement(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StockMovement movement,
        long signedQuantityChangeX1000)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO stock_movements (
                id,
                product_id,
                movement_type,
                qty_change_x1000,
                cost_price_cents,
                reference_type,
                reference_id,
                reason,
                user_id,
                created_at
            )
            VALUES (
                $id,
                $productId,
                $movementType,
                $quantityChangeX1000,
                $costPriceCents,
                $referenceType,
                $referenceId,
                $reason,
                $userId,
                $createdAt
            );
            """;

        command.Parameters.AddWithValue("$id", movement.Id.ToString("D"));
        command.Parameters.AddWithValue("$productId", movement.ProductId.ToString("D"));
        command.Parameters.AddWithValue("$movementType", Convert.ToInt32(movement.MovementType));
        command.Parameters.AddWithValue("$quantityChangeX1000", signedQuantityChangeX1000);
        command.Parameters.AddWithValue("$costPriceCents", DbNullableMoney(movement.CostPrice));
        command.Parameters.AddWithValue("$referenceType", DbNullableText(movement.ReferenceType));
        command.Parameters.AddWithValue("$referenceId", DbNullableGuid(movement.ReferenceId));
        command.Parameters.AddWithValue("$reason", DbNullableText(movement.Reason));
        command.Parameters.AddWithValue("$userId", movement.UserId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", movement.CreatedAt.ToString("O"));

        command.ExecuteNonQuery();
    }

    private static Result ValidateCompletedSale(
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<StockMovement> stockMovements)
    {
        if (sale is null)
        {
            return Result.Fail(Error.Validation(
                "Sales.SaleRequired",
                "Sale is required."));
        }

        if (items is null)
        {
            return Result.Fail(Error.Validation(
                "Sales.SaleItemsRequired",
                "Sale items are required."));
        }

        if (payments is null)
        {
            return Result.Fail(Error.Validation(
                "Sales.PaymentsRequired",
                "Sale payments are required."));
        }

        if (stockMovements is null)
        {
            return Result.Fail(Error.Validation(
                "Sales.StockMovementsRequired",
                "Sale stock movements are required."));
        }

        if (sale.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidSaleId",
                "Sale ID is required."));
        }

        if (string.IsNullOrWhiteSpace(sale.ReceiptNumber))
        {
            return Result.Fail(Error.Validation(
                "Sales.ReceiptNumberRequired",
                "Receipt number is required."));
        }

        if (sale.CashierUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidCashierUserId",
                "Cashier user ID is required."));
        }

        if (sale.CashierSessionId.HasValue && sale.CashierSessionId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidCashierSessionId",
                "Cashier session ID cannot be empty."));
        }

        if (sale.Status != SaleStatus.Completed)
        {
            return Result.Fail(Error.Validation(
                "Sales.CompletedStatusRequired",
                "AddCompletedSaleAsync only accepts completed sales."));
        }

        if (sale.VoidedByUserId.HasValue ||
            !string.IsNullOrWhiteSpace(sale.VoidReason) ||
            sale.VoidedAt.HasValue)
        {
            return Result.Fail(Error.Validation(
                "Sales.CompletedSaleCannotHaveVoidMetadata",
                "Completed sale writes cannot already contain void metadata."));
        }

        if (items.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.EmptySaleItems",
                "At least one sale item is required."));
        }

        if (payments.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.EmptyPayments",
                "At least one payment is required."));
        }

        if (sale.Subtotal.Cents < 0 ||
            sale.Discount.Cents < 0 ||
            sale.Tax.Cents < 0 ||
            sale.Total.Cents < 0 ||
            sale.AmountPaid.Cents < 0 ||
            sale.Change.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidMoneyAmount",
                "Sale money amounts cannot be negative."));
        }

        long expectedTotal = checked(sale.Subtotal.Cents - sale.Discount.Cents + sale.Tax.Cents);

        if (expectedTotal < 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidMoneyAmount",
                "Sale total cannot be negative after discounts and tax."));
        }

        if (sale.Total.Cents != expectedTotal)
        {
            return Result.Fail(Error.Validation(
                "Sales.TotalMismatch",
                "Sale total must equal subtotal minus discount plus tax."));
        }

        if (sale.AmountPaid.Cents < sale.Total.Cents)
        {
            return Result.Fail(Error.Validation(
                "Sales.InsufficientPayment",
                "Amount paid cannot be less than sale total."));
        }

        if (sale.Change.Cents != sale.AmountPaid.Cents - sale.Total.Cents)
        {
            return Result.Fail(Error.Validation(
                "Sales.ChangeMismatch",
                "Sale change must equal amount paid minus total."));
        }

        HashSet<Guid> saleItemIds = new();
        long itemTotal = 0;

        foreach (SaleItem item in items)
        {
            Result itemValidation = ValidateSaleItem(item, sale.Id);

            if (itemValidation.IsFailure)
            {
                return itemValidation;
            }

            if (!saleItemIds.Add(item.Id))
            {
                return Result.Fail(Error.Validation(
                    "Sales.DuplicateSaleItemId",
                    "Sale item IDs must be unique within the sale write request."));
            }

            itemTotal = checked(itemTotal + item.LineTotal.Cents);
        }

        if (itemTotal != sale.Subtotal.Cents)
        {
            return Result.Fail(Error.Validation(
                "Sales.SubtotalMismatch",
                "Sale subtotal must equal the sum of sale item line totals."));
        }

        HashSet<Guid> paymentIds = new();
        long paymentTotal = 0;

        foreach (Payment payment in payments)
        {
            Result paymentValidation = ValidatePayment(payment, sale.Id);

            if (paymentValidation.IsFailure)
            {
                return paymentValidation;
            }

            if (!paymentIds.Add(payment.Id))
            {
                return Result.Fail(Error.Validation(
                    "Sales.DuplicatePaymentId",
                    "Payment IDs must be unique within the sale write request."));
            }

            paymentTotal = checked(paymentTotal + payment.Amount.Cents);
        }

        if (paymentTotal != sale.AmountPaid.Cents)
        {
            return Result.Fail(Error.Validation(
                "Sales.PaymentTotalMismatch",
                "Payment total must equal sale amount paid."));
        }

        return ValidateStockMovementsForSale(
            sale.Id,
            sale.CashierUserId,
            stockMovements,
            StockMovementType.Sale,
            "Sales.InvalidSaleStockMovementType",
            "Sales.InvalidSaleStockMovementReference");
    }

    private static Result ValidateVoidRequest(
        Guid saleId,
        Guid voidedByUserId,
        string reason,
        IReadOnlyList<StockMovement> reversalMovements)
    {
        if (saleId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidSaleId",
                "Sale ID is required."));
        }

        if (voidedByUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidUserId",
                "Void user ID is required."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail(Error.Validation(
                "Sales.VoidReasonRequired",
                "A reason is required when voiding a sale."));
        }

        return ValidateStockMovementsForSale(
            saleId,
            voidedByUserId,
            reversalMovements,
            StockMovementType.VoidSale,
            "Sales.InvalidVoidStockMovementType",
            "Sales.InvalidVoidStockMovementReference");
    }

    private static Result ValidateSaleItem(SaleItem item, Guid saleId)
    {
        if (item.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidSaleItemId",
                "Sale item ID is required."));
        }

        if (item.SaleId != saleId)
        {
            return Result.Fail(Error.Validation(
                "Sales.SaleItemSaleIdMismatch",
                "Sale item must belong to the sale being written."));
        }

        if (item.ProductId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidProductId",
                "Sale item product ID is required."));
        }

        if (string.IsNullOrWhiteSpace(item.ProductNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "Sales.ProductNameSnapshotRequired",
                "Sale item product name snapshot is required."));
        }

        if (string.IsNullOrWhiteSpace(item.UnitNameSnapshot))
        {
            return Result.Fail(Error.Validation(
                "Sales.UnitNameSnapshotRequired",
                "Sale item unit name snapshot is required."));
        }

        if (item.Quantity.ValueX1000 <= 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.ItemQuantityRequired",
                "Sale item quantity must be greater than zero."));
        }

        if (item.UnitPrice.Cents < 0 ||
            item.Discount.Cents < 0 ||
            item.LineTotal.Cents < 0 ||
            item.CostPrice is Money costPrice && costPrice.Cents < 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidSaleItemMoneyAmount",
                "Sale item money amounts cannot be negative."));
        }

        long grossLineTotal = CalculateLineGrossCents(
            item.UnitPrice,
            item.Quantity);

        if (item.Discount.Cents > grossLineTotal)
        {
            return Result.Fail(Error.Validation(
                "Sales.ItemDiscountExceedsLineGross",
                "Sale item discount cannot exceed line gross amount."));
        }

        long expectedLineTotal = checked(grossLineTotal - item.Discount.Cents);

        if (item.LineTotal.Cents != expectedLineTotal)
        {
            return Result.Fail(Error.Validation(
                "Sales.LineTotalMismatch",
                "Sale item line total must equal unit price times quantity minus item discount."));
        }

        return Result.Success();
    }

    private static Result ValidatePayment(Payment payment, Guid saleId)
    {
        if (payment.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidPaymentId",
                "Payment ID is required."));
        }

        if (payment.SaleId != saleId)
        {
            return Result.Fail(Error.Validation(
                "Sales.PaymentSaleIdMismatch",
                "Payment must belong to the sale being written."));
        }

        if (!Enum.IsDefined(typeof(PaymentMethod), payment.Method))
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidPaymentMethod",
                "Payment method is invalid."));
        }

        if (payment.Amount.Cents <= 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.PaymentAmountRequired",
                "Payment amount must be greater than zero."));
        }

        return Result.Success();
    }

    private static Result ValidateStockMovementsForSale(
        Guid saleId,
        Guid expectedUserId,
        IReadOnlyList<StockMovement> stockMovements,
        StockMovementType expectedMovementType,
        string invalidMovementTypeCode,
        string invalidReferenceCode)
    {
        HashSet<Guid> movementIds = new();

        foreach (StockMovement movement in stockMovements)
        {
            Result movementValidation = ValidateStockMovement(movement);

            if (movementValidation.IsFailure)
            {
                return movementValidation;
            }

            if (!movementIds.Add(movement.Id))
            {
                return Result.Fail(Error.Validation(
                    "Sales.DuplicateStockMovementId",
                    "Stock movement IDs must be unique within the sale write request."));
            }

            if (movement.MovementType != expectedMovementType)
            {
                return Result.Fail(Error.Validation(
                    invalidMovementTypeCode,
                    "Stock movement type is not valid for this sale operation."));
            }

            if (!string.Equals(movement.ReferenceType?.Trim(), "SALE", StringComparison.OrdinalIgnoreCase) ||
                movement.ReferenceId != saleId)
            {
                return Result.Fail(Error.Validation(
                    invalidReferenceCode,
                    "Stock movement reference must point to the sale being written."));
            }

            if (movement.UserId != expectedUserId)
            {
                return Result.Fail(Error.Validation(
                    "Sales.StockMovementUserMismatch",
                    "Stock movement user must match the sale operation user."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateStockMovement(StockMovement movement)
    {
        if (movement.Id == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidStockMovementId",
                "Stock movement ID is required."));
        }

        if (movement.ProductId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidStockMovementProductId",
                "Stock movement product ID is required."));
        }

        if (movement.UserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidStockMovementUserId",
                "Stock movement user ID is required."));
        }

        if (!Enum.IsDefined(typeof(StockMovementType), movement.MovementType))
        {
            return Result.Fail(Error.Validation(
                "Sales.InvalidStockMovementType",
                "Stock movement type is invalid."));
        }

        if (movement.QuantityChange.ValueX1000 <= 0)
        {
            return Result.Fail(Error.Validation(
                "Sales.StockMovementQuantityRequired",
                "Stock movement quantity must be greater than zero."));
        }

        return Result.Success();
    }

    private static long CalculateLineGrossCents(
        Money unitPrice,
        Quantity quantity)
    {
        decimal gross = unitPrice.Cents * (quantity.ValueX1000 / 1000m);

        return checked((long)Math.Round(
            gross,
            0,
            MidpointRounding.AwayFromZero));
    }

    private static long GetSignedQuantityChangeX1000(StockMovement movement)
    {
        long quantity = movement.QuantityChange.ValueX1000;

        return movement.MovementType switch
        {
            StockMovementType.Sale => checked(-quantity),
            StockMovementType.VoidSale => quantity,
            StockMovementType.StockIn => quantity,
            StockMovementType.AdjustmentAdd => quantity,
            StockMovementType.AdjustmentRemove => checked(-quantity),
            StockMovementType.Damage => checked(-quantity),
            StockMovementType.Expired => checked(-quantity),
            StockMovementType.CountCorrection => quantity,
            _ => throw new InvalidOperationException("Unsupported stock movement type.")
        };
    }

    private static object DbNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static object DbNullableGuid(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty
            ? value.Value.ToString("D")
            : DBNull.Value;
    }

    private static object DbNullableMoney(Money? value)
    {
        return value is Money money
            ? money.Cents
            : DBNull.Value;
    }

    private static Result ToAddSaleUniqueConflict(SqliteException exception)
    {
        string message = exception.Message;

        if (message.Contains("stock_movements", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(Error.Conflict(
                "Sales.DuplicateStockMovement",
                "One or more stock movement IDs already exist."));
        }

        if (message.Contains("sale_items", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(Error.Conflict(
                "Sales.DuplicateSaleItem",
                "One or more sale item IDs already exist."));
        }

        if (message.Contains("payments", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail(Error.Conflict(
                "Sales.DuplicatePayment",
                "One or more payment IDs already exist."));
        }

        return Result.Fail(Error.Conflict(
            "Sales.DuplicateSaleOrReceipt",
            "Sale ID or receipt number already exists."));
    }

    private static bool IsUniqueConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode is 1555 or 2067 ||
            exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForeignKeyConstraintViolation(SqliteException exception)
    {
        return exception.SqliteExtendedErrorCode == 787 ||
            exception.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProductStockState(
        long StockOnHandX1000,
        bool TrackStock,
        bool IsActive);
}

using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCustomerOrdersAndQuotesService : ICustomerOrdersAndQuotesService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCustomerOrdersAndQuotesService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Result<IReadOnlyList<CustomerQuotation>>> GetQuotationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            List<CustomerQuotation> list = new();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, quote_number, customer_name, customer_contact, notes,
                       subtotal_cents, discount_cents, total_cents, status,
                       created_by_user_id, created_by_user_name, created_at, valid_until
                FROM customer_quotations
                ORDER BY created_at DESC;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid id = Guid.Parse(reader.GetString(0));
                string quoteNumber = reader.GetString(1);
                string customerName = reader.GetString(2);
                string? customerContact = reader.IsDBNull(3) ? null : reader.GetString(3);
                string? notes = reader.IsDBNull(4) ? null : reader.GetString(4);
                long subtotal = reader.GetInt64(5);
                long discount = reader.GetInt64(6);
                long total = reader.GetInt64(7);
                string status = reader.GetString(8);
                Guid userId = Guid.Parse(reader.GetString(9));
                string userName = reader.GetString(10);
                DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                DateOnly? validUntil = reader.IsDBNull(12) ? null : DateOnly.Parse(reader.GetString(12), CultureInfo.InvariantCulture);

                list.Add(new CustomerQuotation(
                    id, quoteNumber, customerName, customerContact, notes,
                    subtotal, discount, total, status, userId, userName, createdAt, validUntil,
                    Array.Empty<CustomerQuotationLine>()));
            }

            return Result<IReadOnlyList<CustomerQuotation>>.Success(list);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<CustomerQuotation>>.Fail(Error.Unexpected("Quotation.GetFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerQuotation>> GetQuotationByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, quote_number, customer_name, customer_contact, notes,
                       subtotal_cents, discount_cents, total_cents, status,
                       created_by_user_id, created_by_user_name, created_at, valid_until
                FROM customer_quotations
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return Result<CustomerQuotation>.Fail(Error.NotFound("Quotation.NotFound", "Quotation not found."));
            }

            string quoteNumber = reader.GetString(1);
            string customerName = reader.GetString(2);
            string? customerContact = reader.IsDBNull(3) ? null : reader.GetString(3);
            string? notes = reader.IsDBNull(4) ? null : reader.GetString(4);
            long subtotal = reader.GetInt64(5);
            long discount = reader.GetInt64(6);
            long total = reader.GetInt64(7);
            string status = reader.GetString(8);
            Guid userId = Guid.Parse(reader.GetString(9));
            string userName = reader.GetString(10);
            DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateOnly? validUntil = reader.IsDBNull(12) ? null : DateOnly.Parse(reader.GetString(12), CultureInfo.InvariantCulture);
            await reader.CloseAsync().ConfigureAwait(false);

            List<CustomerQuotationLine> lines = new();
            await using var lineCmd = connection.CreateCommand();
            lineCmd.CommandText = """
                SELECT id, product_id, product_name_snapshot, barcode_snapshot,
                       unit_name_snapshot, quantity_x1000, unit_price_cents,
                       discount_cents, line_total_cents
                FROM customer_quotation_lines
                WHERE quotation_id = $quoteId
                ORDER BY rowid;
                """;
            lineCmd.Parameters.AddWithValue("$quoteId", id.ToString("D"));

            await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await lineReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(new CustomerQuotationLine(
                    Guid.Parse(lineReader.GetString(0)),
                    id,
                    Guid.Parse(lineReader.GetString(1)),
                    lineReader.GetString(2),
                    lineReader.IsDBNull(3) ? null : lineReader.GetString(3),
                    lineReader.GetString(4),
                    lineReader.GetInt64(5),
                    lineReader.GetInt64(6),
                    lineReader.GetInt64(7),
                    lineReader.GetInt64(8)));
            }

            return Result<CustomerQuotation>.Success(new CustomerQuotation(
                id, quoteNumber, customerName, customerContact, notes,
                subtotal, discount, total, status, userId, userName, createdAt, validUntil, lines));
        }
        catch (Exception ex)
        {
            return Result<CustomerQuotation>.Fail(Error.Unexpected("Quotation.GetByIdFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerQuotation>> CreateQuotationAsync(
        Guid userId,
        string userName,
        CreateCustomerQuotationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return Result<CustomerQuotation>.Fail(Error.Validation("Quotation.CustomerNameRequired", "Customer name is required."));
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<CustomerQuotation>.Fail(Error.Validation("Quotation.LinesRequired", "At least one item is required for quotation."));
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();

            Guid quoteId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string prefix = $"PQ-{now:yyyyMM}-";
            string quoteNumber = await AllocateNextSequenceAsync(connection, transaction, prefix, cancellationToken).ConfigureAwait(false);

            long subtotalCents = 0;
            List<CustomerQuotationLine> lines = new();

            foreach (var line in request.Lines)
            {
                if (line.QuantityX1000 <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return Result<CustomerQuotation>.Fail(Error.Validation("Quotation.InvalidQuantity", "Item quantity must be greater than zero."));
                }
                long grossLineCents = (line.QuantityX1000 * line.UnitPriceCents + 500) / 1000;
                long netLineCents = Math.Max(0L, grossLineCents - line.DiscountCents);
                subtotalCents += netLineCents;

                Guid lineId = Guid.NewGuid();
                CustomerQuotationLine quotationLine = new(
                    lineId, quoteId, line.ProductId, line.ProductNameSnapshot.Trim(),
                    line.BarcodeSnapshot?.Trim(), line.UnitNameSnapshot.Trim(),
                    line.QuantityX1000, line.UnitPriceCents, line.DiscountCents, netLineCents);
                lines.Add(quotationLine);
            }

            long totalCents = Math.Max(0L, subtotalCents - request.DiscountCents);

            // 1. Insert parent quotation first to satisfy foreign key constraints
            await using var insertQuote = connection.CreateCommand();
            insertQuote.Transaction = transaction;
            insertQuote.CommandText = """
                INSERT INTO customer_quotations (
                    id, quote_number, customer_name, customer_contact, notes,
                    subtotal_cents, discount_cents, total_cents, status,
                    created_by_user_id, created_by_user_name, created_at, valid_until
                ) VALUES (
                    $id, $quoteNumber, $customerName, $customerContact, $notes,
                    $subtotal, $discount, $total, 'Issued',
                    $userId, $userName, $createdAt, $validUntil
                );
                """;
            insertQuote.Parameters.AddWithValue("$id", quoteId.ToString("D"));
            insertQuote.Parameters.AddWithValue("$quoteNumber", quoteNumber);
            insertQuote.Parameters.AddWithValue("$customerName", request.CustomerName.Trim());
            insertQuote.Parameters.AddWithValue("$customerContact", (object?)request.CustomerContact?.Trim() ?? DBNull.Value);
            insertQuote.Parameters.AddWithValue("$notes", (object?)request.Notes?.Trim() ?? DBNull.Value);
            insertQuote.Parameters.AddWithValue("$subtotal", subtotalCents);
            insertQuote.Parameters.AddWithValue("$discount", request.DiscountCents);
            insertQuote.Parameters.AddWithValue("$total", totalCents);
            insertQuote.Parameters.AddWithValue("$userId", userId.ToString("D"));
            insertQuote.Parameters.AddWithValue("$userName", userName);
            insertQuote.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            insertQuote.Parameters.AddWithValue("$validUntil", request.ValidUntil.HasValue ? request.ValidUntil.Value.ToString("yyyy-MM-dd") : DBNull.Value);
            await insertQuote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // 2. Insert quotation lines
            foreach (var qLine in lines)
            {
                await using var insertLine = connection.CreateCommand();
                insertLine.Transaction = transaction;
                insertLine.CommandText = """
                    INSERT INTO customer_quotation_lines (
                        id, quotation_id, product_id, product_name_snapshot, barcode_snapshot,
                        unit_name_snapshot, quantity_x1000, unit_price_cents, discount_cents, line_total_cents
                    ) VALUES (
                        $id, $quoteId, $productId, $productName, $barcode,
                        $unitName, $quantity, $unitPrice, $discount, $lineTotal
                    );
                    """;
                insertLine.Parameters.AddWithValue("$id", qLine.Id.ToString("D"));
                insertLine.Parameters.AddWithValue("$quoteId", quoteId.ToString("D"));
                insertLine.Parameters.AddWithValue("$productId", qLine.ProductId.ToString("D"));
                insertLine.Parameters.AddWithValue("$productName", qLine.ProductNameSnapshot.Trim());
                insertLine.Parameters.AddWithValue("$barcode", (object?)qLine.BarcodeSnapshot?.Trim() ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("$unitName", qLine.UnitNameSnapshot.Trim());
                insertLine.Parameters.AddWithValue("$quantity", qLine.QuantityX1000);
                insertLine.Parameters.AddWithValue("$unitPrice", qLine.UnitPriceCents);
                insertLine.Parameters.AddWithValue("$discount", qLine.DiscountCents);
                insertLine.Parameters.AddWithValue("$lineTotal", qLine.LineTotalCents);
                await insertLine.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<CustomerQuotation>.Success(new CustomerQuotation(
                quoteId, quoteNumber, request.CustomerName.Trim(), request.CustomerContact?.Trim(),
                request.Notes?.Trim(), subtotalCents, request.DiscountCents, totalCents,
                "Issued", userId, userName, now, request.ValidUntil, lines));
        }
        catch (Exception ex)
        {
            return Result<CustomerQuotation>.Fail(Error.Unexpected("Quotation.CreateFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerQuotation>> UpdateQuotationStatusAsync(Guid id, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE customer_quotations SET status = $status WHERE id = $id;";
            cmd.Parameters.AddWithValue("$status", status.Trim());
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return Result<CustomerQuotation>.Fail(Error.NotFound("Quotation.NotFound", "Quotation not found."));
            }

            return await GetQuotationByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<CustomerQuotation>.Fail(Error.Unexpected("Quotation.UpdateStatusFailed", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyList<CustomerOrder>>> GetOrdersAsync(string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            List<CustomerOrder> list = new();
            await using var cmd = connection.CreateCommand();
            if (string.IsNullOrWhiteSpace(status))
            {
                cmd.CommandText = """
                    SELECT id, order_number, customer_id, customer_name, customer_phone,
                           promised_date, notes, total_cents, status, sale_id,
                           created_by_user_id, created_by_user_name, created_at, fulfilled_at
                    FROM customer_orders
                    ORDER BY created_at DESC;
                    """;
            }
            else
            {
                cmd.CommandText = """
                    SELECT id, order_number, customer_id, customer_name, customer_phone,
                           promised_date, notes, total_cents, status, sale_id,
                           created_by_user_id, created_by_user_name, created_at, fulfilled_at
                    FROM customer_orders
                    WHERE status = $status
                    ORDER BY created_at DESC;
                    """;
                cmd.Parameters.AddWithValue("$status", status.Trim());
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid id = Guid.Parse(reader.GetString(0));
                string orderNumber = reader.GetString(1);
                Guid? customerId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2));
                string customerName = reader.GetString(3);
                string? customerPhone = reader.IsDBNull(4) ? null : reader.GetString(4);
                DateOnly? promisedDate = reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
                string? notes = reader.IsDBNull(6) ? null : reader.GetString(6);
                long total = reader.GetInt64(7);
                string st = reader.GetString(8);
                Guid? saleId = reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9));
                Guid userId = Guid.Parse(reader.GetString(10));
                string userName = reader.GetString(11);
                DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                DateTimeOffset? fulfilledAt = reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                list.Add(new CustomerOrder(
                    id, orderNumber, customerId, customerName, customerPhone,
                    promisedDate, notes, total, st, saleId, userId, userName, createdAt, fulfilledAt,
                    Array.Empty<CustomerOrderLine>()));
            }

            return Result<IReadOnlyList<CustomerOrder>>.Success(list);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<CustomerOrder>>.Fail(Error.Unexpected("CustomerOrders.GetFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerOrder>> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, order_number, customer_id, customer_name, customer_phone,
                       promised_date, notes, total_cents, status, sale_id,
                       created_by_user_id, created_by_user_name, created_at, fulfilled_at
                FROM customer_orders
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return Result<CustomerOrder>.Fail(Error.NotFound("CustomerOrder.NotFound", "Order not found."));
            }

            string orderNumber = reader.GetString(1);
            Guid? customerId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2));
            string customerName = reader.GetString(3);
            string? customerPhone = reader.IsDBNull(4) ? null : reader.GetString(4);
            DateOnly? promisedDate = reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
            string? notes = reader.IsDBNull(6) ? null : reader.GetString(6);
            long total = reader.GetInt64(7);
            string status = reader.GetString(8);
            Guid? saleId = reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9));
            Guid userId = Guid.Parse(reader.GetString(10));
            string userName = reader.GetString(11);
            DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            DateTimeOffset? fulfilledAt = reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            await reader.CloseAsync().ConfigureAwait(false);

            List<CustomerOrderLine> lines = new();
            await using var lineCmd = connection.CreateCommand();
            lineCmd.CommandText = """
                SELECT id, product_id, product_name_snapshot, barcode_snapshot,
                       unit_name_snapshot, quantity_x1000, unit_price_cents, line_total_cents
                FROM customer_order_lines
                WHERE order_id = $orderId
                ORDER BY rowid;
                """;
            lineCmd.Parameters.AddWithValue("$orderId", id.ToString("D"));

            await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await lineReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(new CustomerOrderLine(
                    Guid.Parse(lineReader.GetString(0)),
                    id,
                    Guid.Parse(lineReader.GetString(1)),
                    lineReader.GetString(2),
                    lineReader.IsDBNull(3) ? null : lineReader.GetString(3),
                    lineReader.GetString(4),
                    lineReader.GetInt64(5),
                    lineReader.GetInt64(6),
                    lineReader.GetInt64(7)));
            }

            return Result<CustomerOrder>.Success(new CustomerOrder(
                id, orderNumber, customerId, customerName, customerPhone,
                promisedDate, notes, total, status, saleId, userId, userName, createdAt, fulfilledAt, lines));
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.GetByIdFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerOrder>> CreateOrderAsync(
        Guid userId,
        string userName,
        CreateCustomerOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return Result<CustomerOrder>.Fail(Error.Validation("CustomerOrder.CustomerNameRequired", "Customer name is required."));
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<CustomerOrder>.Fail(Error.Validation("CustomerOrder.LinesRequired", "At least one item is required for order."));
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();

            Guid orderId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string prefix = $"CO-{now:yyyyMM}-";
            string orderNumber = await AllocateNextSequenceAsync(connection, transaction, prefix, cancellationToken).ConfigureAwait(false);

            long totalCents = 0;
            List<CustomerOrderLine> lines = new();

            foreach (var line in request.Lines)
            {
                if (line.QuantityX1000 <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return Result<CustomerOrder>.Fail(Error.Validation("CustomerOrder.InvalidQuantity", "Item quantity must be greater than zero."));
                }
                long lineTotalCents = (line.QuantityX1000 * line.UnitPriceCents + 500) / 1000;
                totalCents += lineTotalCents;

                Guid lineId = Guid.NewGuid();
                CustomerOrderLine orderLine = new(
                    lineId, orderId, line.ProductId, line.ProductNameSnapshot.Trim(),
                    line.BarcodeSnapshot?.Trim(), line.UnitNameSnapshot.Trim(),
                    line.QuantityX1000, line.UnitPriceCents, lineTotalCents);
                lines.Add(orderLine);
            }

            // 1. Insert parent customer order first to satisfy foreign key constraints
            await using var insertOrder = connection.CreateCommand();
            insertOrder.Transaction = transaction;
            insertOrder.CommandText = """
                INSERT INTO customer_orders (
                    id, order_number, customer_id, customer_name, customer_phone,
                    promised_date, notes, total_cents, status,
                    created_by_user_id, created_by_user_name, created_at
                ) VALUES (
                    $id, $orderNumber, $customerId, $customerName, $customerPhone,
                    $promisedDate, $notes, $total, 'Confirmed',
                    $userId, $userName, $createdAt
                );
                """;
            insertOrder.Parameters.AddWithValue("$id", orderId.ToString("D"));
            insertOrder.Parameters.AddWithValue("$orderNumber", orderNumber);
            insertOrder.Parameters.AddWithValue("$customerId", (object?)request.CustomerId?.ToString("D") ?? DBNull.Value);
            insertOrder.Parameters.AddWithValue("$customerName", request.CustomerName.Trim());
            insertOrder.Parameters.AddWithValue("$customerPhone", (object?)request.CustomerPhone?.Trim() ?? DBNull.Value);
            insertOrder.Parameters.AddWithValue("$promisedDate", request.PromisedDate.HasValue ? request.PromisedDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
            insertOrder.Parameters.AddWithValue("$notes", (object?)request.Notes?.Trim() ?? DBNull.Value);
            insertOrder.Parameters.AddWithValue("$total", totalCents);
            insertOrder.Parameters.AddWithValue("$userId", userId.ToString("D"));
            insertOrder.Parameters.AddWithValue("$userName", userName);
            insertOrder.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            await insertOrder.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // 2. Insert child order lines
            foreach (var oLine in lines)
            {
                await using var insertLine = connection.CreateCommand();
                insertLine.Transaction = transaction;
                insertLine.CommandText = """
                    INSERT INTO customer_order_lines (
                        id, order_id, product_id, product_name_snapshot, barcode_snapshot,
                        unit_name_snapshot, quantity_x1000, unit_price_cents, line_total_cents
                    ) VALUES (
                        $id, $orderId, $productId, $productName, $barcode,
                        $unitName, $quantity, $unitPrice, $lineTotal
                    );
                    """;
                insertLine.Parameters.AddWithValue("$id", oLine.Id.ToString("D"));
                insertLine.Parameters.AddWithValue("$orderId", orderId.ToString("D"));
                insertLine.Parameters.AddWithValue("$productId", oLine.ProductId.ToString("D"));
                insertLine.Parameters.AddWithValue("$productName", oLine.ProductNameSnapshot.Trim());
                insertLine.Parameters.AddWithValue("$barcode", (object?)oLine.BarcodeSnapshot?.Trim() ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("$unitName", oLine.UnitNameSnapshot.Trim());
                insertLine.Parameters.AddWithValue("$quantity", oLine.QuantityX1000);
                insertLine.Parameters.AddWithValue("$unitPrice", oLine.UnitPriceCents);
                insertLine.Parameters.AddWithValue("$lineTotal", oLine.LineTotalCents);
                await insertLine.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<CustomerOrder>.Success(new CustomerOrder(
                orderId, orderNumber, request.CustomerId, request.CustomerName.Trim(),
                request.CustomerPhone?.Trim(), request.PromisedDate, request.Notes?.Trim(),
                totalCents, "Confirmed", null, userId, userName, now, null, lines));
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.CreateFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerOrder>> UpdateOrderStatusAsync(Guid id, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE customer_orders SET status = $status WHERE id = $id;";
            cmd.Parameters.AddWithValue("$status", status.Trim());
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return Result<CustomerOrder>.Fail(Error.NotFound("CustomerOrder.NotFound", "Order not found."));
            }

            return await GetOrderByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.UpdateStatusFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerOrder>> FulfillOrderAsync(Guid orderId, Guid saleId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE customer_orders
                SET status = 'Fulfilled', sale_id = $saleId, fulfilled_at = $now
                WHERE id = $id AND status != 'Fulfilled';
                """;
            cmd.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", orderId.ToString("D"));

            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.AlreadyFulfilledOrNotFound", "Order is already fulfilled or was not found."));
            }

            return await GetOrderByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.FulfillFailed", ex.Message));
        }
    }

    private static async Task<string> AllocateNextSequenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string prefix,
        CancellationToken ct)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO lpi_document_sequences (prefix, current_number, updated_at)
            VALUES ($prefix, 0, $now);
            UPDATE lpi_document_sequences
            SET current_number = current_number + 1, updated_at = $now
            WHERE prefix = $prefix;
            SELECT current_number FROM lpi_document_sequences WHERE prefix = $prefix;
            """;
        cmd.Parameters.AddWithValue("$prefix", prefix);
        cmd.Parameters.AddWithValue("$now", now);

        object? result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        long seq = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        return $"{prefix}{seq:D4}";
    }
}

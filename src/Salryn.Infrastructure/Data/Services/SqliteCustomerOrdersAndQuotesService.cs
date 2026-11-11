using System.Globalization;
using System.Text.Json;
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

            await WriteAuditAsync(connection, transaction, userId, "CUSTOMER_QUOTATION_CREATED",
                "customer_quotations", quoteId, null,
                JsonSerializer.Serialize(new { quoteNumber, status = "Issued", totalCents, lineCount = lines.Count }),
                "Quotation created; no sale, payment, or stock movement", now, cancellationToken);
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

    public async Task<Result<CustomerQuotation>> UpdateQuotationStatusAsync(Guid id, string status, CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        try
        {
            if (status is null || (status.Trim() != "Expired" && status.Trim() != "Converted"))
                return Result<CustomerQuotation>.Fail(Error.Validation("Quotation.InvalidStatus", "Only Issued quotations can be expired or marked converted."));
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE customer_quotations SET status = $status WHERE id = $id AND status = 'Issued';";
            cmd.Parameters.AddWithValue("$status", status.Trim());
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return Result<CustomerQuotation>.Fail(Error.Conflict("Quotation.InvalidTransition", "Quotation was not Issued or no longer exists."));
            }
            await WriteAuditAsync(connection, transaction, actorUserId, "CUSTOMER_QUOTATION_STATUS_CHANGED",
                "customer_quotations", id, "{\"status\":\"Issued\"}",
                JsonSerializer.Serialize(new { status = status.Trim() }), null, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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

            await WriteAuditAsync(connection, transaction, userId, "CUSTOMER_ORDER_CREATED",
                "customer_orders", orderId, null,
                JsonSerializer.Serialize(new { orderNumber, status = "Confirmed", totalCents, lineCount = lines.Count }),
                "Manager order recorded; no stock or sale posted", now, cancellationToken);
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

    public async Task<Result<CustomerOrder>> UpdateOrderStatusAsync(Guid id, string status, CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return Result<CustomerOrder>.Fail(Error.Validation("CustomerOrder.InvalidStatus", "Status is required."));
        }

        string targetStatus = status.Trim();

        // 1. Direct fulfillment write is strictly prohibited
        if (string.Equals(targetStatus, "Fulfilled", StringComparison.OrdinalIgnoreCase))
        {
            return Result<CustomerOrder>.Fail(Error.Conflict(
                "CustomerOrder.CannotSetFulfilledDirectly",
                "Customer orders cannot be fulfilled via status update. Fulfillment requires verified sale completion via the /fulfill endpoint."));
        }

        // 2. Only permitted manual status change is Cancelled
        if (!string.Equals(targetStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return Result<CustomerOrder>.Fail(Error.Validation(
                "CustomerOrder.InvalidStatusTransition",
                $"Invalid order status transition to '{targetStatus}'. The only permitted manual status change is 'Cancelled'."));
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();

            // 3. Fetch existing status
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT status FROM customer_orders WHERE id = $id;";
            checkCmd.Parameters.AddWithValue("$id", id.ToString("D"));
            object? currentStatusObj = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (currentStatusObj is null || currentStatusObj is DBNull)
            {
                return Result<CustomerOrder>.Fail(Error.NotFound("CustomerOrder.NotFound", "Customer order not found."));
            }

            string currentStatus = currentStatusObj.ToString()!;

            // 4. Disallow modifying or reviving terminal states
            if (string.Equals(currentStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentStatus, "Fulfilled", StringComparison.OrdinalIgnoreCase))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict(
                    "CustomerOrder.TerminalState",
                    $"Cannot modify customer order in terminal status '{currentStatus}'. Reverting or modifying terminal states is disallowed."));
            }

            if (!string.Equals(currentStatus, "Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict(
                    "CustomerOrder.InvalidState",
                    $"Customer order in status '{currentStatus}' cannot be cancelled."));
            }

            // 5. Update to Cancelled
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE customer_orders SET status = 'Cancelled' WHERE id = $id AND status = 'Confirmed';";
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.UpdateConflict", "Order status could not be updated."));
            }

            // 6. Audit log
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var auditCmd = connection.CreateCommand();
            auditCmd.Transaction = transaction;
            auditCmd.CommandText = """
                INSERT INTO audit_logs (id, user_id, action, entity_type, entity_id, old_value_json, new_value_json, reason, created_at)
                VALUES ($id, $actor, 'CUSTOMER_ORDER_CANCELLED', 'customer_orders', $entityId, $oldVal, $newVal, $reason, $now);
                """;
            auditCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            auditCmd.Parameters.AddWithValue("$actor", (object?)actorUserId?.ToString("D") ?? DBNull.Value);
            auditCmd.Parameters.AddWithValue("$entityId", id.ToString("D"));
            auditCmd.Parameters.AddWithValue("$oldVal", $"{{\"status\":\"{currentStatus}\"}}");
            auditCmd.Parameters.AddWithValue("$newVal", "{\"status\":\"Cancelled\"}");
            auditCmd.Parameters.AddWithValue("$reason", "Customer order cancelled by authorized operator.");
            auditCmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await auditCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return await GetOrderByIdAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.UpdateStatusFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerOrder>> FulfillOrderByReferenceAsync(Guid orderId, string saleReference, CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        if (string.IsNullOrWhiteSpace(saleReference))
        {
            return Result<CustomerOrder>.Fail(Error.Validation("CustomerOrder.SaleRequired", "Valid completed sale ID or receipt number is required."));
        }

        if (Guid.TryParse(saleReference.Trim(), out Guid saleId))
        {
            return await FulfillOrderAsync(orderId, saleId, cancellationToken, actorUserId).ConfigureAwait(false);
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM sales WHERE receipt_no = $rec LIMIT 1;";
            cmd.Parameters.AddWithValue("$rec", saleReference.Trim());
            object? found = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found is null || found is DBNull || !Guid.TryParse(found.ToString(), out Guid resolvedId))
            {
                return Result<CustomerOrder>.Fail(Error.NotFound("CustomerOrder.SaleNotFound", $"Referenced receipt '{saleReference}' does not exist."));
            }

            return await FulfillOrderAsync(orderId, resolvedId, cancellationToken, actorUserId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.ResolveSaleFailed", ex.Message));
        }
    }

    public async Task<Result<CustomerOrder>> FulfillOrderAsync(Guid orderId, Guid saleId, CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();

            // 1. Fetch order details
            await using var orderCmd = connection.CreateCommand();
            orderCmd.Transaction = transaction;
            orderCmd.CommandText = """
                SELECT status, customer_id, total_cents
                FROM customer_orders
                WHERE id = $orderId;
                """;
            orderCmd.Parameters.AddWithValue("$orderId", orderId.ToString("D"));
            string? orderStatus = null;
            Guid? orderCustomerId = null;
            long orderTotalCents = 0;
            await using (var orderReader = await orderCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await orderReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    orderStatus = orderReader.GetString(0);
                    orderCustomerId = orderReader.IsDBNull(1) ? null : Guid.Parse(orderReader.GetString(1));
                    orderTotalCents = orderReader.GetInt64(2);
                }
            }

            if (orderStatus is null)
            {
                return Result<CustomerOrder>.Fail(Error.NotFound("CustomerOrder.NotFound", "Customer order not found."));
            }
            if (string.Equals(orderStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.OrderCancelled", "Cannot fulfill a cancelled customer order."));
            }
            if (string.Equals(orderStatus, "Fulfilled", StringComparison.OrdinalIgnoreCase))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.AlreadyFulfilled", "Customer order is already fulfilled."));
            }
            if (!string.Equals(orderStatus, "Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.InvalidStateForFulfillment", $"Customer order in status '{orderStatus}' cannot be fulfilled."));
            }

            // 2. Validate sale exists, is completed, not voided
            await using var saleCmd = connection.CreateCommand();
            saleCmd.Transaction = transaction;
            saleCmd.CommandText = """
                SELECT status, voided_at, customer_id, total_cents
                FROM sales
                WHERE id = $saleId;
                """;
            saleCmd.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
            int? saleStatus = null;
            bool isVoided = false;
            Guid? saleCustomerId = null;
            long saleTotalCents = 0;
            await using (var saleReader = await saleCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await saleReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    saleStatus = saleReader.GetInt32(0);
                    isVoided = !saleReader.IsDBNull(1) || saleStatus == (int)SaleStatus.Voided;
                    saleCustomerId = saleReader.IsDBNull(2) ? null : Guid.Parse(saleReader.GetString(2));
                    saleTotalCents = saleReader.GetInt64(3);
                }
            }

            if (!saleStatus.HasValue)
            {
                return Result<CustomerOrder>.Fail(Error.NotFound("CustomerOrder.SaleNotFound", "Referenced sale does not exist."));
            }
            if (saleStatus.Value != (int)SaleStatus.Completed || isVoided)
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.SaleNotCompleted", "Referenced sale is voided or not in completed status."));
            }

            // 3. Customer identity consistency check
            if (orderCustomerId.HasValue && (!saleCustomerId.HasValue || orderCustomerId.Value != saleCustomerId.Value))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.CustomerMismatch", "Referenced sale customer account does not match the customer order account."));
            }
            if (saleCustomerId.HasValue && (!orderCustomerId.HasValue || saleCustomerId.Value != orderCustomerId.Value))
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.CustomerMismatch", "Referenced sale is assigned to a customer account, but customer order is unassigned or mismatched."));
            }

            // 4. Financial amount verification (sale must cover full order total)
            if (saleTotalCents < orderTotalCents)
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.SaleTotalMismatch", $"Referenced sale total ({saleTotalCents} cents) does not cover order total ({orderTotalCents} cents)."));
            }

            // 5. One-to-one uniqueness check (sale must not already be linked to another order)
            await using var checkLinkCmd = connection.CreateCommand();
            checkLinkCmd.Transaction = transaction;
            checkLinkCmd.CommandText = """
                SELECT id FROM customer_orders
                WHERE sale_id = $saleId AND id != $orderId;
                """;
            checkLinkCmd.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
            checkLinkCmd.Parameters.AddWithValue("$orderId", orderId.ToString("D"));
            var existingLink = await checkLinkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existingLink is not null && existingLink is not DBNull)
            {
                return Result<CustomerOrder>.Fail(Error.Conflict("CustomerOrder.SaleAlreadyLinked", "Referenced sale is already linked to another customer order."));
            }

            // 5. Line items match check
            await using var orderLinesCmd = connection.CreateCommand();
            orderLinesCmd.Transaction = transaction;
            orderLinesCmd.CommandText = """
                SELECT product_id, quantity_x1000
                FROM customer_order_lines
                WHERE order_id = $orderId;
                """;
            orderLinesCmd.Parameters.AddWithValue("$orderId", orderId.ToString("D"));
            List<(Guid ProductId, long QtyX1000)> requiredLines = new();
            await using (var r = await orderLinesCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    requiredLines.Add((Guid.Parse(r.GetString(0)), r.GetInt64(1)));
                }
            }

            await using var saleItemsCmd = connection.CreateCommand();
            saleItemsCmd.Transaction = transaction;
            saleItemsCmd.CommandText = """
                SELECT product_id, qty_x1000
                FROM sale_items
                WHERE sale_id = $saleId;
                """;
            saleItemsCmd.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
            Dictionary<Guid, long> saleItems = new();
            await using (var r = await saleItemsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    Guid pid = Guid.Parse(r.GetString(0));
                    long qty = r.GetInt64(1);
                    saleItems[pid] = saleItems.TryGetValue(pid, out long existingQty) ? existingQty + qty : qty;
                }
            }

            foreach (var (pid, reqQty) in requiredLines)
            {
                if (!saleItems.TryGetValue(pid, out long actualQty) || actualQty < reqQty)
                {
                    return Result<CustomerOrder>.Fail(Error.Conflict(
                        "CustomerOrder.SaleLineMismatch",
                        $"Referenced sale does not contain required order items or sufficient quantity for product '{pid}'."));
                }
            }

            // 6. Update order status and link sale
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = """
                UPDATE customer_orders
                SET status = 'Fulfilled', sale_id = $saleId, fulfilled_at = $now
                WHERE id = $orderId;
                """;
            updateCmd.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
            updateCmd.Parameters.AddWithValue("$now", now.ToString("O"));
            updateCmd.Parameters.AddWithValue("$orderId", orderId.ToString("D"));
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // 7. Audit log
            await using var auditCmd = connection.CreateCommand();
            auditCmd.Transaction = transaction;
            auditCmd.CommandText = """
                INSERT INTO audit_logs (id, user_id, action, entity_type, entity_id, new_value_json, reason, created_at)
                VALUES ($id, $actor, 'CUSTOMER_ORDER_FULFILLED', 'customer_orders', $entityId, $newVal, $reason, $now);
                """;
            auditCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            auditCmd.Parameters.AddWithValue("$actor", (object?)actorUserId?.ToString("D") ?? DBNull.Value);
            auditCmd.Parameters.AddWithValue("$entityId", orderId.ToString("D"));
            auditCmd.Parameters.AddWithValue("$newVal", $"{{\"saleId\":\"{saleId:D}\",\"orderTotalCents\":{orderTotalCents},\"saleTotalCents\":{saleTotalCents}}}");
            auditCmd.Parameters.AddWithValue("$reason", "Customer order fulfilled with matching completed POS sale.");
            auditCmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await auditCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return await GetOrderByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<CustomerOrder>.Fail(Error.Unexpected("CustomerOrder.FulfillFailed", ex.Message));
        }
    }

    private static async Task WriteAuditAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid? actorUserId, string action, string entityType, Guid entityId,
        string? oldJson, string newJson, string? reason, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO audit_logs
              (id,user_id,action,entity_type,entity_id,old_value_json,new_value_json,reason,created_at)
            VALUES ($id,$actor,$action,$type,$entity,$old,$new,$reason,$at);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$actor", (object?)actorUserId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$type", entityType);
        cmd.Parameters.AddWithValue("$entity", entityId.ToString("D"));
        cmd.Parameters.AddWithValue("$old", (object?)oldJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$new", newJson);
        cmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", at.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

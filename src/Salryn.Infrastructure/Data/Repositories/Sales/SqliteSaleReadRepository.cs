using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;

namespace Salryn.Infrastructure.Data.Repositories.Sales;

public sealed class SqliteSaleReadRepository :
    SqliteRepositoryBase,
    ISaleReadRepository
{
    public SqliteSaleReadRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<SaleDetailReadModel?>> FindSaleByIdAsync(
        Guid saleId,
        CancellationToken cancellationToken = default)
    {
        return FindSaleDetailAsync(
            "Sales.FindByIdFailed",
            "s.id = $value",
            saleId.ToString("D"),
            cancellationToken);
    }

    public Task<Result<SaleDetailReadModel?>> FindSaleByReceiptNoAsync(
        string receiptNo,
        CancellationToken cancellationToken = default)
    {
        return FindSaleDetailAsync(
            "Sales.FindByReceiptNoFailed",
            "s.receipt_no = $value",
            receiptNo.Trim(),
            cancellationToken);
    }

    public Task<Result<PagedResult<SaleHistoryItemReadModel>>> SearchSalesAsync(
        SaleHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            int totalCount = CountSales(connection, filter);
            IReadOnlyList<SaleHistoryItemReadModel> items = SearchSales(
                connection,
                filter,
                cancellationToken);

            PagedResult<SaleHistoryItemReadModel> page = new(
                items,
                totalCount,
                filter.Page.PageNumber,
                filter.Page.PageSize);

            return Task.FromResult(Result<PagedResult<SaleHistoryItemReadModel>>.Success(page));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<PagedResult<SaleHistoryItemReadModel>>(
                "Sales.SearchFailed",
                ex));
        }
    }

    private Task<Result<SaleDetailReadModel?>> FindSaleDetailAsync(
        string errorCode,
        string whereClause,
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();

            SaleHeader? header = ReadSaleHeader(connection, whereClause, value);

            if (header is null)
            {
                return Task.FromResult(Result<SaleDetailReadModel?>.Success(null));
            }

            IReadOnlyList<SaleItemDetailReadModel> items = ReadSaleItems(
                connection,
                header.Id,
                cancellationToken);

            IReadOnlyList<PaymentReadModel> payments = ReadPayments(
                connection,
                header.Id,
                cancellationToken);

            SaleDetailReadModel detail = new(
                header.Id,
                header.ReceiptNo,
                header.SaleDate,
                header.CashierDisplayName,
                header.Subtotal,
                header.Discount,
                header.Tax,
                header.Total,
                header.AmountPaid,
                header.Change,
                header.Status,
                header.VoidReason,
                header.VoidedAt,
                header.ReceiptSnapshot,
                items,
                payments);

            return Task.FromResult(Result<SaleDetailReadModel?>.Success(detail));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<SaleDetailReadModel?>(errorCode, ex));
        }
    }

    private static SaleHeader? ReadSaleHeader(
        SqliteConnection connection,
        string whereClause,
        string value)
    {
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT
                s.id,
                s.receipt_no,
                s.sale_date,
                u.display_name AS cashier_display_name,
                s.subtotal_cents,
                s.discount_cents,
                s.tax_cents,
                s.total_cents,
                s.amount_paid_cents,
                s.change_cents,
                s.status,
                s.void_reason,
                s.voided_at,
                s.receipt_snapshot
            FROM sales s
            INNER JOIN users u ON u.id = s.cashier_user_id
            WHERE {whereClause}
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$value", value);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new SaleHeader(
            reader.GetGuidValue("id"),
            reader.GetStringValue("receipt_no"),
            reader.GetDateTimeOffsetValue("sale_date"),
            reader.GetStringValue("cashier_display_name"),
            reader.GetMoneyValue("subtotal_cents"),
            reader.GetMoneyValue("discount_cents"),
            reader.GetMoneyValue("tax_cents"),
            reader.GetMoneyValue("total_cents"),
            reader.GetMoneyValue("amount_paid_cents"),
            reader.GetMoneyValue("change_cents"),
            reader.GetEnumValue<SaleStatus>("status"),
            reader.GetNullableString("void_reason"),
            reader.GetNullableDateTimeOffsetValue("voided_at"),
            reader.GetNullableString("receipt_snapshot"));
    }

    private static IReadOnlyList<SaleItemDetailReadModel> ReadSaleItems(
        SqliteConnection connection,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                id,
                product_id,
                product_name_snapshot,
                barcode_snapshot,
                unit_name_snapshot,
                qty_x1000,
                unit_price_cents,
                cost_price_cents,
                discount_cents,
                line_total_cents
            FROM sale_items
            WHERE sale_id = $saleId
            ORDER BY rowid;
            """;

        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        using var reader = command.ExecuteReader();

        List<SaleItemDetailReadModel> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            items.Add(new SaleItemDetailReadModel(
                reader.GetGuidValue("id"),
                reader.GetGuidValue("product_id"),
                reader.GetStringValue("product_name_snapshot"),
                reader.GetNullableString("barcode_snapshot"),
                reader.GetStringValue("unit_name_snapshot"),
                reader.GetQuantityValue("qty_x1000"),
                reader.GetMoneyValue("unit_price_cents"),
                reader.GetNullableMoneyValue("cost_price_cents"),
                reader.GetMoneyValue("discount_cents"),
                reader.GetMoneyValue("line_total_cents")));
        }

        return items;
    }

    private static IReadOnlyList<PaymentReadModel> ReadPayments(
        SqliteConnection connection,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT id, method, amount_cents, paid_at
            FROM payments
            WHERE sale_id = $saleId
            ORDER BY paid_at;
            """;

        command.Parameters.AddWithValue("$saleId", saleId.ToString("D"));

        using var reader = command.ExecuteReader();

        List<PaymentReadModel> payments = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            payments.Add(new PaymentReadModel(
                reader.GetGuidValue("id"),
                reader.GetEnumValue<PaymentMethod>("method"),
                reader.GetMoneyValue("amount_cents"),
                reader.GetDateTimeOffsetValue("paid_at")));
        }

        return payments;
    }

    private static int CountSales(
        SqliteConnection connection,
        SaleHistoryFilter filter)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT COUNT(*)
            FROM sales s
            WHERE ($from IS NULL OR s.sale_date >= $from)
              AND ($to IS NULL OR s.sale_date <= $to)
              AND ($cashierUserId IS NULL OR s.cashier_user_id = $cashierUserId)
              AND ($status IS NULL OR s.status = $status)
              AND ($receiptNo IS NULL OR s.receipt_no LIKE $receiptNoLike);
            """;

        AddSaleFilterParameters(command, filter);

        object? value = command.ExecuteScalar();

        return Convert.ToInt32(value ?? 0);
    }

    private static IReadOnlyList<SaleHistoryItemReadModel> SearchSales(
        SqliteConnection connection,
        SaleHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT
                s.id,
                s.receipt_no,
                s.sale_date,
                u.display_name AS cashier_display_name,
                s.total_cents,
                s.amount_paid_cents,
                s.change_cents,
                s.status,
                s.void_reason
            FROM sales s
            INNER JOIN users u ON u.id = s.cashier_user_id
            WHERE ($from IS NULL OR s.sale_date >= $from)
              AND ($to IS NULL OR s.sale_date <= $to)
              AND ($cashierUserId IS NULL OR s.cashier_user_id = $cashierUserId)
              AND ($status IS NULL OR s.status = $status)
              AND ($receiptNo IS NULL OR s.receipt_no LIKE $receiptNoLike)
            ORDER BY s.sale_date DESC
            LIMIT $limit OFFSET $offset;
            """;

        AddSaleFilterParameters(command, filter);
        command.Parameters.AddWithValue("$limit", Math.Max(1, filter.Page.PageSize));
        command.Parameters.AddWithValue("$offset", Math.Max(0, filter.Page.Offset));

        using var reader = command.ExecuteReader();

        List<SaleHistoryItemReadModel> items = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            items.Add(new SaleHistoryItemReadModel(
                reader.GetGuidValue("id"),
                reader.GetStringValue("receipt_no"),
                reader.GetDateTimeOffsetValue("sale_date"),
                reader.GetStringValue("cashier_display_name"),
                reader.GetMoneyValue("total_cents"),
                reader.GetMoneyValue("amount_paid_cents"),
                reader.GetMoneyValue("change_cents"),
                reader.GetEnumValue<SaleStatus>("status"),
                reader.GetNullableString("void_reason")));
        }

        return items;
    }

    private static void AddSaleFilterParameters(
        SqliteCommand command,
        SaleHistoryFilter filter)
    {
        string? receiptNo = string.IsNullOrWhiteSpace(filter.ReceiptNo)
            ? null
            : filter.ReceiptNo.Trim();

        command.Parameters.AddWithValue("$from", filter.From?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$to", filter.To?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$cashierUserId", filter.CashierUserId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", filter.Status.HasValue ? Convert.ToInt32(filter.Status.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$receiptNo", receiptNo ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$receiptNoLike", receiptNo is null ? DBNull.Value : $"%{receiptNo}%");
    }

    private sealed record SaleHeader(
        Guid Id,
        string ReceiptNo,
        DateTimeOffset SaleDate,
        string CashierDisplayName,
        Core.ValueObjects.Money Subtotal,
        Core.ValueObjects.Money Discount,
        Core.ValueObjects.Money Tax,
        Core.ValueObjects.Money Total,
        Core.ValueObjects.Money AmountPaid,
        Core.ValueObjects.Money Change,
        SaleStatus Status,
        string? VoidReason,
        DateTimeOffset? VoidedAt,
        string? ReceiptSnapshot);
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public sealed record SaleCommittedV1CanonicalParseResult(
    bool IsValid,
    SaleCommittedV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SaleCommittedV1CanonicalParseResult Success(SaleCommittedV1 payload) =>
        new(true, payload, null, null);

    public static SaleCommittedV1CanonicalParseResult Fail(string code, string message) =>
        new(false, null, code, message);
}

public static class SaleCommittedV1CanonicalJson
{
    public static string Serialize(SaleCommittedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("sale");
            WriteSale(writer, payload.Sale);

            writer.WritePropertyName("items");
            writer.WriteStartArray();
            foreach (SaleCommittedV1Item item in payload.Items)
            {
                WriteItem(writer, item);
            }
            writer.WriteEndArray();

            writer.WritePropertyName("payments");
            writer.WriteStartArray();
            foreach (SaleCommittedV1Payment payment in payload.Payments)
            {
                WritePayment(writer, payment);
            }
            writer.WriteEndArray();

            writer.WritePropertyName("stock_movements");
            writer.WriteStartArray();
            foreach (SaleCommittedV1StockMovement movement in payload.StockMovements)
            {
                WriteStockMovement(writer, movement);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static SaleCommittedV1CanonicalParseResult Parse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return SaleCommittedV1CanonicalParseResult.Fail(
                "SaleCommittedV1.PayloadRequired",
                "SaleCommittedV1 payload JSON is required.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;

            SaleCommittedV1 payload = new(
                ReadSale(root.GetProperty("sale")),
                ReadItems(root.GetProperty("items")),
                ReadPayments(root.GetProperty("payments")),
                ReadStockMovements(root.GetProperty("stock_movements")));

            string canonical = Serialize(payload);
            if (!string.Equals(canonical, payloadJson, StringComparison.Ordinal))
            {
                return SaleCommittedV1CanonicalParseResult.Fail(
                    "SaleCommittedV1.PayloadNotCanonical",
                    "SaleCommittedV1 payload must use the exact canonical JSON representation.");
            }

            return SaleCommittedV1CanonicalParseResult.Success(payload);
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or FormatException or
            KeyNotFoundException or OverflowException or ArgumentException)
        {
            return SaleCommittedV1CanonicalParseResult.Fail(
                "SaleCommittedV1.PayloadInvalid",
                "SaleCommittedV1 payload could not be parsed: " + ex.Message);
        }
    }

    private static void WriteSale(Utf8JsonWriter writer, SaleCommittedV1Sale sale)
    {
        writer.WriteStartObject();
        writer.WriteString("id", GuidText(sale.Id));
        writer.WriteString("receipt_number", sale.ReceiptNumber);
        writer.WriteString("sale_date", TimestampText(sale.SaleDate));
        writer.WriteString("cashier_user_id", GuidText(sale.CashierUserId));
        writer.WriteString("cashier_session_id", GuidText(sale.CashierSessionId));
        writer.WriteNumber("subtotal_cents", sale.SubtotalCents);
        writer.WriteNumber("discount_cents", sale.DiscountCents);
        writer.WriteNumber("tax_cents", sale.TaxCents);
        writer.WriteNumber("total_cents", sale.TotalCents);
        writer.WriteNumber("amount_paid_cents", sale.AmountPaidCents);
        writer.WriteNumber("change_cents", sale.ChangeCents);
        WriteNullableGuid(writer, "customer_id", sale.CustomerId);
        writer.WriteString("payment_status", sale.PaymentStatus);
        writer.WriteNumber("balance_due_cents", sale.BalanceDueCents);
        WriteNullableDate(writer, "receivable_due_date", sale.ReceivableDueDate);
        WriteNullableString(writer, "receivable_note", sale.ReceivableNote);
        WriteNullableString(writer, "receipt_snapshot", sale.ReceiptSnapshot);
        writer.WriteString("created_at", TimestampText(sale.CreatedAt));
        writer.WriteEndObject();
    }

    private static void WriteItem(Utf8JsonWriter writer, SaleCommittedV1Item item)
    {
        writer.WriteStartObject();
        writer.WriteString("id", GuidText(item.Id));
        writer.WriteString("sale_id", GuidText(item.SaleId));
        writer.WriteString("product_id", GuidText(item.ProductId));
        writer.WriteString("product_name_snapshot", item.ProductNameSnapshot);
        WriteNullableString(writer, "barcode_snapshot", item.BarcodeSnapshot);
        writer.WriteString("unit_name_snapshot", item.UnitNameSnapshot);
        writer.WriteNumber("quantity_x1000", item.QuantityX1000);
        writer.WriteNumber("unit_price_cents", item.UnitPriceCents);
        WriteNullableLong(writer, "cost_price_cents", item.CostPriceCents);
        writer.WriteNumber("discount_cents", item.DiscountCents);
        writer.WriteNumber("line_total_cents", item.LineTotalCents);
        writer.WriteEndObject();
    }

    private static void WritePayment(Utf8JsonWriter writer, SaleCommittedV1Payment payment)
    {
        writer.WriteStartObject();
        writer.WriteString("id", GuidText(payment.Id));
        writer.WriteString("sale_id", GuidText(payment.SaleId));
        writer.WriteString("method", payment.Method);
        writer.WriteNumber("amount_cents", payment.AmountCents);
        writer.WriteString("paid_at", TimestampText(payment.PaidAt));
        writer.WriteEndObject();
    }

    private static void WriteStockMovement(
        Utf8JsonWriter writer,
        SaleCommittedV1StockMovement movement)
    {
        writer.WriteStartObject();
        writer.WriteString("id", GuidText(movement.Id));
        writer.WriteString("product_id", GuidText(movement.ProductId));
        writer.WriteString("movement_type", movement.MovementType);
        writer.WriteNumber("quantity_change_x1000", movement.QuantityChangeX1000);
        WriteNullableLong(writer, "cost_price_cents", movement.CostPriceCents);
        writer.WriteString("reference_type", movement.ReferenceType);
        writer.WriteString("reference_id", GuidText(movement.ReferenceId));
        WriteNullableString(writer, "reason", movement.Reason);
        writer.WriteString("user_id", GuidText(movement.UserId));
        writer.WriteString("created_at", TimestampText(movement.CreatedAt));
        writer.WriteEndObject();
    }

    private static SaleCommittedV1Sale ReadSale(JsonElement sale) =>
        new(
            ReadGuid(sale, "id"),
            sale.GetProperty("receipt_number").GetString() ?? string.Empty,
            ReadTimestamp(sale, "sale_date"),
            ReadGuid(sale, "cashier_user_id"),
            ReadGuid(sale, "cashier_session_id"),
            sale.GetProperty("subtotal_cents").GetInt64(),
            sale.GetProperty("discount_cents").GetInt64(),
            sale.GetProperty("tax_cents").GetInt64(),
            sale.GetProperty("total_cents").GetInt64(),
            sale.GetProperty("amount_paid_cents").GetInt64(),
            sale.GetProperty("change_cents").GetInt64(),
            ReadNullableGuid(sale, "customer_id"),
            sale.GetProperty("payment_status").GetString() ?? string.Empty,
            sale.GetProperty("balance_due_cents").GetInt64(),
            ReadNullableDate(sale, "receivable_due_date"),
            ReadNullableString(sale, "receivable_note"),
            ReadNullableString(sale, "receipt_snapshot"),
            ReadTimestamp(sale, "created_at"));

    private static IReadOnlyList<SaleCommittedV1Item> ReadItems(JsonElement array)
    {
        List<SaleCommittedV1Item> items = new();
        foreach (JsonElement item in array.EnumerateArray())
        {
            items.Add(new SaleCommittedV1Item(
                ReadGuid(item, "id"),
                ReadGuid(item, "sale_id"),
                ReadGuid(item, "product_id"),
                item.GetProperty("product_name_snapshot").GetString() ?? string.Empty,
                ReadNullableString(item, "barcode_snapshot"),
                item.GetProperty("unit_name_snapshot").GetString() ?? string.Empty,
                item.GetProperty("quantity_x1000").GetInt64(),
                item.GetProperty("unit_price_cents").GetInt64(),
                ReadNullableLong(item, "cost_price_cents"),
                item.GetProperty("discount_cents").GetInt64(),
                item.GetProperty("line_total_cents").GetInt64()));
        }
        return items;
    }

    private static IReadOnlyList<SaleCommittedV1Payment> ReadPayments(JsonElement array)
    {
        List<SaleCommittedV1Payment> payments = new();
        foreach (JsonElement payment in array.EnumerateArray())
        {
            payments.Add(new SaleCommittedV1Payment(
                ReadGuid(payment, "id"),
                ReadGuid(payment, "sale_id"),
                payment.GetProperty("method").GetString() ?? string.Empty,
                payment.GetProperty("amount_cents").GetInt64(),
                ReadTimestamp(payment, "paid_at")));
        }
        return payments;
    }

    private static IReadOnlyList<SaleCommittedV1StockMovement> ReadStockMovements(JsonElement array)
    {
        List<SaleCommittedV1StockMovement> movements = new();
        foreach (JsonElement movement in array.EnumerateArray())
        {
            movements.Add(new SaleCommittedV1StockMovement(
                ReadGuid(movement, "id"),
                ReadGuid(movement, "product_id"),
                movement.GetProperty("movement_type").GetString() ?? string.Empty,
                movement.GetProperty("quantity_change_x1000").GetInt64(),
                ReadNullableLong(movement, "cost_price_cents"),
                movement.GetProperty("reference_type").GetString() ?? string.Empty,
                ReadGuid(movement, "reference_id"),
                ReadNullableString(movement, "reason"),
                ReadGuid(movement, "user_id"),
                ReadTimestamp(movement, "created_at")));
        }
        return movements;
    }

    private static Guid ReadGuid(JsonElement element, string name) =>
        Guid.ParseExact(
            element.GetProperty(name).GetString()
            ?? throw new FormatException(name + " is required."),
            "D");

    private static Guid? ReadNullableGuid(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : Guid.ParseExact(value.GetString() ?? string.Empty, "D");
    }

    private static DateTimeOffset ReadTimestamp(JsonElement element, string name)
    {
        string text = element.GetProperty(name).GetString()
            ?? throw new FormatException(name + " is required.");
        return DateTimeOffset.ParseExact(
            text,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static DateOnly? ReadNullableDate(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return DateOnly.ParseExact(
            value.GetString() ?? string.Empty,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
    }

    private static string? ReadNullableString(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static long? ReadNullableLong(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();
    }

    private static string GuidText(Guid value) => value.ToString("D");

    private static string TimestampText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void WriteNullableGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(name, GuidText(value.Value));
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteNullableDate(Utf8JsonWriter writer, string name, DateOnly? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(name, value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNullableLong(Utf8JsonWriter writer, string name, long? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(name, value.Value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}

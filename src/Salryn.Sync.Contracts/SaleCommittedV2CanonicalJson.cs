using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public sealed record SaleCommittedV2CanonicalParseResult(
    bool IsValid,
    SaleCommittedV2? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SaleCommittedV2CanonicalParseResult Success(SaleCommittedV2 payload) =>
        new(true, payload, null, null);

    public static SaleCommittedV2CanonicalParseResult Fail(string code, string message) =>
        new(false, null, code, message);
}

public static class SaleCommittedV2CanonicalJson
{
    public static string Serialize(SaleCommittedV2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payload.SaleEvidence);

        string v1 = SaleCommittedV1CanonicalJson.Serialize(payload.SaleEvidence);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("sale_committed_v1");
            writer.WriteRawValue(v1, skipInputValidation: false);
            writer.WriteString("financial_operation_id", payload.FinancialOperationId.ToString("D"));
            writer.WriteString("financial_decision_id", payload.FinancialDecisionId.ToString("D"));
            WriteNullableGuid(writer, "order_id", payload.OrderId);
            WriteNullableGuid(writer, "order_revision_id", payload.OrderRevisionId);
            WriteNullableInt(writer, "expected_order_version", payload.ExpectedOrderVersion);
            writer.WriteString(
                "financial_decision_created_at_utc",
                payload.FinancialDecisionCreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("financial_decision_hash", payload.FinancialDecisionHash);
            WriteNullableString(writer, "discount_reason", payload.DiscountReason);
            writer.WriteNumber("fee_total_cents", payload.FeeTotalCents);

            writer.WritePropertyName("line_discounts");
            writer.WriteStartArray();
            foreach (SaleCommittedV2LineDiscount line in payload.LineDiscounts.OrderBy(x => x.SaleItemId))
            {
                writer.WriteStartObject();
                writer.WriteString("sale_item_id", line.SaleItemId.ToString("D"));
                writer.WriteNumber("line_discount_cents", line.LineDiscountCents);
                writer.WriteNumber("order_discount_allocated_cents", line.OrderDiscountAllocatedCents);
                writer.WriteNumber("total_discount_cents", line.TotalDiscountCents);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("fees");
            writer.WriteStartArray();
            foreach (SaleCommittedV2Fee fee in payload.Fees.OrderBy(x => x.FeeId))
            {
                writer.WriteStartObject();
                writer.WriteString("fee_id", fee.FeeId.ToString("D"));
                writer.WriteString("fee_type", fee.FeeType);
                writer.WriteString("display_label_snapshot", fee.DisplayLabelSnapshot);
                writer.WriteNumber("amount_cents", fee.AmountCents);
                writer.WriteString("actor_user_id", fee.ActorUserId.ToString("D"));
                WriteNullableString(writer, "reason", fee.Reason);
                writer.WriteString("created_at_utc", fee.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static SaleCommittedV2CanonicalParseResult Parse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return SaleCommittedV2CanonicalParseResult.Fail("SaleCommittedV2.PayloadRequired", "SaleCommittedV2 payload JSON is required.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;
            SaleCommittedV1CanonicalParseResult v1 =
                SaleCommittedV1CanonicalJson.Parse(root.GetProperty("sale_committed_v1").GetRawText());

            if (!v1.IsValid || v1.Payload is null)
                return SaleCommittedV2CanonicalParseResult.Fail(v1.ErrorCode ?? "SaleCommittedV2.LegacyEvidenceInvalid", v1.ErrorMessage ?? "Embedded SaleCommittedV1 evidence is invalid.");

            List<SaleCommittedV2LineDiscount> lines = new();
            foreach (JsonElement line in root.GetProperty("line_discounts").EnumerateArray())
            {
                lines.Add(new SaleCommittedV2LineDiscount(
                    Guid.ParseExact(line.GetProperty("sale_item_id").GetString() ?? string.Empty, "D"),
                    line.GetProperty("line_discount_cents").GetInt64(),
                    line.GetProperty("order_discount_allocated_cents").GetInt64(),
                    line.GetProperty("total_discount_cents").GetInt64()));
            }

            List<SaleCommittedV2Fee> fees = new();
            foreach (JsonElement fee in root.GetProperty("fees").EnumerateArray())
            {
                fees.Add(new SaleCommittedV2Fee(
                    Guid.ParseExact(fee.GetProperty("fee_id").GetString() ?? string.Empty, "D"),
                    fee.GetProperty("fee_type").GetString() ?? string.Empty,
                    fee.GetProperty("display_label_snapshot").GetString() ?? string.Empty,
                    fee.GetProperty("amount_cents").GetInt64(),
                    Guid.ParseExact(fee.GetProperty("actor_user_id").GetString() ?? string.Empty, "D"),
                    ReadNullableString(fee, "reason"),
                    DateTimeOffset.ParseExact(
                        fee.GetProperty("created_at_utc").GetString() ?? string.Empty,
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
            }

            SaleCommittedV2 payload = new(
                v1.Payload,
                Guid.ParseExact(root.GetProperty("financial_operation_id").GetString() ?? string.Empty, "D"),
                Guid.ParseExact(root.GetProperty("financial_decision_id").GetString() ?? string.Empty, "D"),
                ReadNullableGuid(root, "order_id"),
                ReadNullableGuid(root, "order_revision_id"),
                ReadNullableInt(root, "expected_order_version"),
                DateTimeOffset.ParseExact(
                    root.GetProperty("financial_decision_created_at_utc").GetString() ?? string.Empty,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                root.GetProperty("financial_decision_hash").GetString() ?? string.Empty,
                ReadNullableString(root, "discount_reason"),
                root.GetProperty("fee_total_cents").GetInt64(),
                lines,
                fees);

            string canonical = Serialize(payload);
            if (!string.Equals(canonical, payloadJson, StringComparison.Ordinal))
                return SaleCommittedV2CanonicalParseResult.Fail("SaleCommittedV2.PayloadNotCanonical", "SaleCommittedV2 payload must use the exact canonical JSON representation.");

            return SaleCommittedV2CanonicalParseResult.Success(payload);
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or FormatException or
            KeyNotFoundException or OverflowException or ArgumentException)
        {
            return SaleCommittedV2CanonicalParseResult.Fail("SaleCommittedV2.PayloadInvalid", "SaleCommittedV2 payload could not be parsed: " + ex.Message);
        }
    }

    private static Guid? ReadNullableGuid(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : Guid.ParseExact(value.GetString() ?? string.Empty, "D");
    }

    private static int? ReadNullableInt(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : value.GetInt32();
    }

    private static string? ReadNullableString(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static void WriteNullableGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value.HasValue)
            writer.WriteString(name, value.Value.ToString("D"));
        else
            writer.WriteNull(name);
    }

    private static void WriteNullableInt(Utf8JsonWriter writer, string name, int? value)
    {
        if (value.HasValue)
            writer.WriteNumber(name, value.Value);
        else
            writer.WriteNull(name);
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteString(name, value);
    }
}

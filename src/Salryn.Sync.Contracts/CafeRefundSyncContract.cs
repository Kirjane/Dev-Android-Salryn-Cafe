using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class CafeRefundSyncContract
{
    public const int SchemaVersion = 1;

    public const string Requested = "SaleRefundRequestedV1";
    public const string Resolved = "SaleRefundResolvedV1";
    public const string Committed = "SaleRefundCommittedV1";

    public const string StateRequested = "Requested";
    public const string StateApproved = "Approved";
    public const string StateRejected = "Rejected";
    public const string StateCommitted = "Committed";

    public const string Restock = "Restock";
    public const string DamagedNonRestockable = "DamagedNonRestockable";
    public const string ServiceNoStock = "ServiceNoStock";

    public const string Cash = "Cash";
    public const string AccountsReceivable = "AccountsReceivable";

    public static bool IsSupported(string? operationType) =>
        string.Equals(operationType, Requested, StringComparison.Ordinal) ||
        string.Equals(operationType, Resolved, StringComparison.Ordinal) ||
        string.Equals(operationType, Committed, StringComparison.Ordinal);
}

public sealed record CafeRefundLineV1(
    Guid RefundLineId,
    Guid SaleItemId,
    Guid ProductId,
    string ProductNameSnapshot,
    long OriginalQuantityX1000,
    long RefundQuantityX1000,
    long FrozenUnitPriceCents,
    long FrozenDiscountCents,
    long RefundAmountCents,
    string StockDisposition,
    string? OperatorNote);

public sealed record CafeRefundSettlementV1(
    Guid SettlementPartId,
    string Method,
    long AmountCents,
    Guid? CashMovementId,
    Guid? ReceivableEntryId,
    DateTimeOffset CreatedAtUtc);

public sealed record CafeRefundOperationV1(
    string OperationType,
    Guid RefundId,
    Guid SaleId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    string? FromState,
    string ToState,
    string RequestReason,
    string? ResolutionReason,
    string RequestHash,
    Guid ActorUserId,
    Guid? CashierSessionId,
    DateTimeOffset OccurredAtUtc,
    long RefundTotalCents,
    IReadOnlyList<CafeRefundLineV1> Lines,
    IReadOnlyList<CafeRefundSettlementV1> Settlements);

public sealed record CafeRefundCanonicalParseResult(
    bool IsValid,
    CafeRefundOperationV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeRefundCanonicalParseResult Success(CafeRefundOperationV1 payload) =>
        new(true, payload, null, null);

    public static CafeRefundCanonicalParseResult Fail(string code, string message) =>
        new(false, null, code, message);
}

public sealed record CafeRefundValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeRefundValidationResult Success() => new(true, null, null);
    public static CafeRefundValidationResult Fail(string code, string message) =>
        new(false, code, message);
}

public static class CafeRefundOperationV1CanonicalJson
{
    public static string Serialize(CafeRefundOperationV1 payload)
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
            writer.WriteString("operation_type", payload.OperationType);
            writer.WriteString("refund_id", payload.RefundId.ToString("D"));
            writer.WriteString("sale_id", payload.SaleId.ToString("D"));
            writer.WriteString("store_id", payload.StoreId.ToString("D"));
            writer.WriteNumber("expected_version", payload.ExpectedVersion);
            writer.WriteNumber("resulting_version", payload.ResultingVersion);
            WriteNullableString(writer, "from_state", payload.FromState);
            writer.WriteString("to_state", payload.ToState);
            writer.WriteString("request_reason", payload.RequestReason);
            WriteNullableString(writer, "resolution_reason", payload.ResolutionReason);
            writer.WriteString("request_hash", payload.RequestHash);
            writer.WriteString("actor_user_id", payload.ActorUserId.ToString("D"));
            WriteNullableGuid(writer, "cashier_session_id", payload.CashierSessionId);
            writer.WriteString(
                "occurred_at_utc",
                payload.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteNumber("refund_total_cents", payload.RefundTotalCents);

            writer.WritePropertyName("lines");
            writer.WriteStartArray();
            foreach (CafeRefundLineV1 line in payload.Lines
                         .OrderBy(x => x.RefundLineId))
            {
                writer.WriteStartObject();
                writer.WriteString("refund_line_id", line.RefundLineId.ToString("D"));
                writer.WriteString("sale_item_id", line.SaleItemId.ToString("D"));
                writer.WriteString("product_id", line.ProductId.ToString("D"));
                writer.WriteString("product_name_snapshot", line.ProductNameSnapshot);
                writer.WriteNumber("original_quantity_x1000", line.OriginalQuantityX1000);
                writer.WriteNumber("refund_quantity_x1000", line.RefundQuantityX1000);
                writer.WriteNumber("frozen_unit_price_cents", line.FrozenUnitPriceCents);
                writer.WriteNumber("frozen_discount_cents", line.FrozenDiscountCents);
                writer.WriteNumber("refund_amount_cents", line.RefundAmountCents);
                writer.WriteString("stock_disposition", line.StockDisposition);
                WriteNullableString(writer, "operator_note", line.OperatorNote);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("settlements");
            writer.WriteStartArray();
            foreach (CafeRefundSettlementV1 settlement in payload.Settlements
                         .OrderBy(x => x.SettlementPartId))
            {
                writer.WriteStartObject();
                writer.WriteString("settlement_part_id", settlement.SettlementPartId.ToString("D"));
                writer.WriteString("method", settlement.Method);
                writer.WriteNumber("amount_cents", settlement.AmountCents);
                WriteNullableGuid(writer, "cash_movement_id", settlement.CashMovementId);
                WriteNullableGuid(writer, "receivable_entry_id", settlement.ReceivableEntryId);
                writer.WriteString(
                    "created_at_utc",
                    settlement.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static CafeRefundCanonicalParseResult Parse(
        string operationType,
        string payloadJson)
    {
        if (!CafeRefundSyncContract.IsSupported(operationType))
        {
            return CafeRefundCanonicalParseResult.Fail(
                "CafeRefundSync.OperationUnsupported",
                "Refund sync operation type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return CafeRefundCanonicalParseResult.Fail(
                "CafeRefundSync.PayloadRequired",
                "Refund sync payload is required.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;

            string embeddedType = root.GetProperty("operation_type").GetString() ?? string.Empty;
            if (!string.Equals(operationType, embeddedType, StringComparison.Ordinal))
            {
                return CafeRefundCanonicalParseResult.Fail(
                    "CafeRefundSync.OperationTypeMismatch",
                    "Envelope operation type differs from canonical refund payload.");
            }

            List<CafeRefundLineV1> lines = new();
            foreach (JsonElement line in root.GetProperty("lines").EnumerateArray())
            {
                lines.Add(new CafeRefundLineV1(
                    ParseGuid(line, "refund_line_id"),
                    ParseGuid(line, "sale_item_id"),
                    ParseGuid(line, "product_id"),
                    line.GetProperty("product_name_snapshot").GetString() ?? string.Empty,
                    line.GetProperty("original_quantity_x1000").GetInt64(),
                    line.GetProperty("refund_quantity_x1000").GetInt64(),
                    line.GetProperty("frozen_unit_price_cents").GetInt64(),
                    line.GetProperty("frozen_discount_cents").GetInt64(),
                    line.GetProperty("refund_amount_cents").GetInt64(),
                    line.GetProperty("stock_disposition").GetString() ?? string.Empty,
                    ReadNullableString(line, "operator_note")));
            }

            List<CafeRefundSettlementV1> settlements = new();
            foreach (JsonElement settlement in root.GetProperty("settlements").EnumerateArray())
            {
                settlements.Add(new CafeRefundSettlementV1(
                    ParseGuid(settlement, "settlement_part_id"),
                    settlement.GetProperty("method").GetString() ?? string.Empty,
                    settlement.GetProperty("amount_cents").GetInt64(),
                    ReadNullableGuid(settlement, "cash_movement_id"),
                    ReadNullableGuid(settlement, "receivable_entry_id"),
                    DateTimeOffset.ParseExact(
                        settlement.GetProperty("created_at_utc").GetString() ?? string.Empty,
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
            }

            CafeRefundOperationV1 payload = new(
                embeddedType,
                ParseGuid(root, "refund_id"),
                ParseGuid(root, "sale_id"),
                ParseGuid(root, "store_id"),
                root.GetProperty("expected_version").GetInt32(),
                root.GetProperty("resulting_version").GetInt32(),
                ReadNullableString(root, "from_state"),
                root.GetProperty("to_state").GetString() ?? string.Empty,
                root.GetProperty("request_reason").GetString() ?? string.Empty,
                ReadNullableString(root, "resolution_reason"),
                root.GetProperty("request_hash").GetString() ?? string.Empty,
                ParseGuid(root, "actor_user_id"),
                ReadNullableGuid(root, "cashier_session_id"),
                DateTimeOffset.ParseExact(
                    root.GetProperty("occurred_at_utc").GetString() ?? string.Empty,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                root.GetProperty("refund_total_cents").GetInt64(),
                lines,
                settlements);

            string canonical = Serialize(payload);
            if (!string.Equals(canonical, payloadJson, StringComparison.Ordinal))
            {
                return CafeRefundCanonicalParseResult.Fail(
                    "CafeRefundSync.PayloadNotCanonical",
                    "Refund payload must use the exact canonical JSON representation.");
            }

            CafeRefundValidationResult validation = CafeRefundOperationV1Validator.Validate(payload);
            return validation.IsValid
                ? CafeRefundCanonicalParseResult.Success(payload)
                : CafeRefundCanonicalParseResult.Fail(
                    validation.ErrorCode ?? "CafeRefundSync.PayloadInvalid",
                    validation.ErrorMessage ?? "Refund payload is invalid.");
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or FormatException or
            OverflowException or ArgumentException or KeyNotFoundException)
        {
            return CafeRefundCanonicalParseResult.Fail(
                "CafeRefundSync.PayloadInvalid",
                "Refund payload could not be parsed: " + ex.Message);
        }
    }

    private static Guid ParseGuid(JsonElement element, string name) =>
        Guid.ParseExact(element.GetProperty(name).GetString() ?? string.Empty, "D");

    private static Guid? ReadNullableGuid(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : Guid.ParseExact(value.GetString() ?? string.Empty, "D");
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

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}

public static class CafeRefundOperationV1Validator
{
    public static CafeRefundValidationResult Validate(CafeRefundOperationV1 payload)
    {
        if (payload is null ||
            !CafeRefundSyncContract.IsSupported(payload.OperationType) ||
            payload.RefundId == Guid.Empty ||
            payload.SaleId == Guid.Empty ||
            payload.StoreId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty)
        {
            return Fail(
                "CafeRefundSync.IdentityInvalid",
                "Refund sync requires supported operation and stable refund/sale/store/actor identities.");
        }

        if (payload.ExpectedVersion < 0 ||
            payload.ResultingVersion != payload.ExpectedVersion + 1)
        {
            return Fail(
                "CafeRefundSync.VersionInvalid",
                "Refund resulting version must be exactly expected version + 1.");
        }

        if (string.IsNullOrWhiteSpace(payload.RequestReason) ||
            payload.RequestHash.Length != 64 ||
            !payload.RequestHash.All(Uri.IsHexDigit) ||
            payload.RefundTotalCents <= 0 ||
            payload.Lines is null ||
            payload.Lines.Count == 0)
        {
            return Fail(
                "CafeRefundSync.RequestEvidenceInvalid",
                "Refund request reason/hash, positive total and line evidence are required.");
        }

        HashSet<Guid> lineIds = new();
        HashSet<Guid> itemIds = new();
        long lineTotal = 0;
        foreach (CafeRefundLineV1 line in payload.Lines)
        {
            if (line.RefundLineId == Guid.Empty ||
                line.SaleItemId == Guid.Empty ||
                line.ProductId == Guid.Empty ||
                !lineIds.Add(line.RefundLineId) ||
                !itemIds.Add(line.SaleItemId) ||
                string.IsNullOrWhiteSpace(line.ProductNameSnapshot) ||
                line.OriginalQuantityX1000 <= 0 ||
                line.RefundQuantityX1000 <= 0 ||
                line.RefundQuantityX1000 > line.OriginalQuantityX1000 ||
                line.FrozenUnitPriceCents < 0 ||
                line.FrozenDiscountCents < 0 ||
                line.RefundAmountCents < 0 ||
                !IsDisposition(line.StockDisposition))
            {
                return Fail(
                    "CafeRefundSync.LineInvalid",
                    "Refund line evidence is invalid or duplicated.");
            }

            try { lineTotal = checked(lineTotal + line.RefundAmountCents); }
            catch (OverflowException)
            {
                return Fail("CafeRefundSync.AmountOverflow", "Refund line total overflowed.");
            }
        }

        if (lineTotal != payload.RefundTotalCents)
        {
            return Fail(
                "CafeRefundSync.TotalMismatch",
                "Refund line values must equal refund total.");
        }

        if (string.Equals(payload.OperationType, CafeRefundSyncContract.Requested, StringComparison.Ordinal))
        {
            if (payload.ExpectedVersion != 0 ||
                payload.FromState is not null ||
                !string.Equals(payload.ToState, CafeRefundSyncContract.StateRequested, StringComparison.Ordinal) ||
                payload.CashierSessionId.HasValue ||
                payload.Settlements.Count != 0 ||
                payload.ResolutionReason is not null)
            {
                return Fail("CafeRefundSync.RequestTransitionInvalid", "Refund request transition evidence is invalid.");
            }
        }
        else if (string.Equals(payload.OperationType, CafeRefundSyncContract.Resolved, StringComparison.Ordinal))
        {
            bool approved = string.Equals(payload.ToState, CafeRefundSyncContract.StateApproved, StringComparison.Ordinal);
            bool rejected = string.Equals(payload.ToState, CafeRefundSyncContract.StateRejected, StringComparison.Ordinal);
            if (payload.ExpectedVersion <= 0 ||
                !string.Equals(payload.FromState, CafeRefundSyncContract.StateRequested, StringComparison.Ordinal) ||
                !(approved || rejected) ||
                payload.CashierSessionId.HasValue ||
                payload.Settlements.Count != 0 ||
                (rejected && string.IsNullOrWhiteSpace(payload.ResolutionReason)))
            {
                return Fail("CafeRefundSync.ResolveTransitionInvalid", "Refund resolution transition evidence is invalid.");
            }
        }
        else
        {
            if (payload.ExpectedVersion <= 0 ||
                !string.Equals(payload.FromState, CafeRefundSyncContract.StateApproved, StringComparison.Ordinal) ||
                !string.Equals(payload.ToState, CafeRefundSyncContract.StateCommitted, StringComparison.Ordinal) ||
                !payload.CashierSessionId.HasValue ||
                payload.CashierSessionId == Guid.Empty ||
                payload.Settlements is null ||
                payload.Settlements.Count == 0)
            {
                return Fail("CafeRefundSync.CommitTransitionInvalid", "Refund commit transition evidence is invalid.");
            }

            HashSet<Guid> settlementIds = new();
            HashSet<string> methods = new(StringComparer.Ordinal);
            long settlementTotal = 0;
            foreach (CafeRefundSettlementV1 settlement in payload.Settlements)
            {
                bool cash = string.Equals(settlement.Method, CafeRefundSyncContract.Cash, StringComparison.Ordinal);
                bool ar = string.Equals(settlement.Method, CafeRefundSyncContract.AccountsReceivable, StringComparison.Ordinal);
                if (settlement.SettlementPartId == Guid.Empty ||
                    !settlementIds.Add(settlement.SettlementPartId) ||
                    !methods.Add(settlement.Method) ||
                    !(cash || ar) ||
                    settlement.AmountCents <= 0 ||
                    (cash && (!settlement.CashMovementId.HasValue || settlement.ReceivableEntryId.HasValue)) ||
                    (ar && (!settlement.ReceivableEntryId.HasValue || settlement.CashMovementId.HasValue)))
                {
                    return Fail("CafeRefundSync.SettlementInvalid", "Refund settlement evidence is invalid.");
                }

                try { settlementTotal = checked(settlementTotal + settlement.AmountCents); }
                catch (OverflowException)
                {
                    return Fail("CafeRefundSync.AmountOverflow", "Refund settlement total overflowed.");
                }
            }

            if (settlementTotal != payload.RefundTotalCents)
                return Fail("CafeRefundSync.SettlementTotalMismatch", "Refund settlement parts must equal refund total.");
        }

        return CafeRefundValidationResult.Success();
    }

    public static CafeRefundValidationResult ValidateEnvelope(
        SyncOperationEnvelopeV1 envelope,
        CafeRefundOperationV1 payload,
        Guid expectedStoreId)
    {
        SyncEnvelopeValidationResult envelopeValidation = SyncProtocolV1.Validate(envelope);
        if (!envelopeValidation.IsValid)
        {
            return Fail(
                envelopeValidation.ErrorCode ?? "CafeRefundSync.EnvelopeInvalid",
                envelopeValidation.ErrorMessage ?? "Refund envelope is invalid.");
        }

        CafeRefundValidationResult payloadValidation = Validate(payload);
        if (!payloadValidation.IsValid)
            return payloadValidation;

        if (!string.Equals(envelope.OperationType, payload.OperationType, StringComparison.Ordinal) ||
            envelope.SchemaVersion != CafeRefundSyncContract.SchemaVersion ||
            envelope.AggregateId != payload.RefundId ||
            envelope.StoreId != payload.StoreId ||
            envelope.StoreId != expectedStoreId ||
            envelope.ActorUserId != payload.ActorUserId ||
            envelope.BaseVersion != payload.ExpectedVersion)
        {
            return Fail(
                "CafeRefundSync.EnvelopeMismatch",
                "Refund envelope authority does not match canonical payload.");
        }

        return CafeRefundValidationResult.Success();
    }

    private static bool IsDisposition(string value) =>
        string.Equals(value, CafeRefundSyncContract.Restock, StringComparison.Ordinal) ||
        string.Equals(value, CafeRefundSyncContract.DamagedNonRestockable, StringComparison.Ordinal) ||
        string.Equals(value, CafeRefundSyncContract.ServiceNoStock, StringComparison.Ordinal);

    private static CafeRefundValidationResult Fail(string code, string message) =>
        CafeRefundValidationResult.Fail(code, message);
}

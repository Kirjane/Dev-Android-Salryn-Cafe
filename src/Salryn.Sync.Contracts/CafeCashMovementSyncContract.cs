using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class CafeCashMovementSyncContract
{
    public const int SchemaVersion = 1;
    public const string OperationType = "CashMovementPostedV1";

    public const string CashIn = "CashIn";
    public const string CashOut = "CashOut";
    public const string OperationalExpense = "OperationalExpense";
    public const string InventoryPurchase = "InventoryPurchase";
    public const string Reversal = "Reversal";

    public static bool IsSupportedMovementType(string? value) =>
        string.Equals(value, CashIn, StringComparison.Ordinal) ||
        string.Equals(value, CashOut, StringComparison.Ordinal) ||
        string.Equals(value, OperationalExpense, StringComparison.Ordinal) ||
        string.Equals(value, InventoryPurchase, StringComparison.Ordinal) ||
        string.Equals(value, Reversal, StringComparison.Ordinal);
}

public sealed record CafeCashMovementPostedV1(
    Guid MovementId,
    Guid StoreId,
    Guid CashierSessionId,
    Guid ActorUserId,
    string MovementType,
    long RequestedAmountCents,
    long DeltaCents,
    string ReferenceType,
    Guid ReferenceId,
    Guid? ReversalOfMovementId,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeCashMovementValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeCashMovementValidationResult Success() =>
        new(true, null, null);

    public static CafeCashMovementValidationResult Fail(
        string code,
        string message) =>
        new(false, code, message);
}

public sealed record CafeCashMovementCanonicalParseResult(
    bool IsValid,
    CafeCashMovementPostedV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeCashMovementCanonicalParseResult Success(
        CafeCashMovementPostedV1 payload) =>
        new(true, payload, null, null);

    public static CafeCashMovementCanonicalParseResult Fail(
        string code,
        string message) =>
        new(false, null, code, message);
}

public static class CafeCashMovementPostedV1Validator
{
    public static CafeCashMovementValidationResult Validate(
        CafeCashMovementPostedV1 payload)
    {
        if (payload is null ||
            payload.MovementId == Guid.Empty ||
            payload.StoreId == Guid.Empty ||
            payload.CashierSessionId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            payload.ReferenceId == Guid.Empty ||
            !CafeCashMovementSyncContract.IsSupportedMovementType(
                payload.MovementType))
        {
            return Fail(
                "CafeCashSync.IdentityInvalid",
                "Cash sync requires stable movement/store/session/actor/reference identities and a supported movement type.");
        }

        if (payload.RequestedAmountCents <= 0 ||
            payload.DeltaCents == 0 ||
            string.IsNullOrWhiteSpace(payload.ReferenceType) ||
            string.IsNullOrWhiteSpace(payload.Reason))
        {
            return Fail(
                "CafeCashSync.EvidenceInvalid",
                "Cash sync requires positive requested amount, non-zero delta, reference and reason.");
        }

        bool validSign =
            (string.Equals(
                 payload.MovementType,
                 CafeCashMovementSyncContract.CashIn,
                 StringComparison.Ordinal) &&
             payload.DeltaCents == payload.RequestedAmountCents &&
             !payload.ReversalOfMovementId.HasValue)
            ||
            ((string.Equals(
                  payload.MovementType,
                  CafeCashMovementSyncContract.CashOut,
                  StringComparison.Ordinal) ||
              string.Equals(
                  payload.MovementType,
                  CafeCashMovementSyncContract.OperationalExpense,
                  StringComparison.Ordinal) ||
              string.Equals(
                  payload.MovementType,
                  CafeCashMovementSyncContract.InventoryPurchase,
                  StringComparison.Ordinal)) &&
             payload.DeltaCents == -payload.RequestedAmountCents &&
             !payload.ReversalOfMovementId.HasValue)
            ||
            (string.Equals(
                 payload.MovementType,
                 CafeCashMovementSyncContract.Reversal,
                 StringComparison.Ordinal) &&
             payload.ReversalOfMovementId.HasValue &&
             Math.Abs(payload.DeltaCents) == payload.RequestedAmountCents);

        if (!validSign)
        {
            return Fail(
                "CafeCashSync.SignInvalid",
                "Cash sync amount/sign/reversal evidence is inconsistent.");
        }

        return CafeCashMovementValidationResult.Success();
    }

    private static CafeCashMovementValidationResult Fail(
        string code,
        string message) =>
        CafeCashMovementValidationResult.Fail(code, message);
}

public static class CafeCashMovementPostedV1CanonicalJson
{
    public static string Serialize(CafeCashMovementPostedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = false,
                       SkipValidation = false
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "movement_id",
                payload.MovementId.ToString("D"));
            writer.WriteString(
                "store_id",
                payload.StoreId.ToString("D"));
            writer.WriteString(
                "cashier_session_id",
                payload.CashierSessionId.ToString("D"));
            writer.WriteString(
                "actor_user_id",
                payload.ActorUserId.ToString("D"));
            writer.WriteString(
                "movement_type",
                payload.MovementType);
            writer.WriteNumber(
                "requested_amount_cents",
                payload.RequestedAmountCents);
            writer.WriteNumber(
                "delta_cents",
                payload.DeltaCents);
            writer.WriteString(
                "reference_type",
                payload.ReferenceType);
            writer.WriteString(
                "reference_id",
                payload.ReferenceId.ToString("D"));

            if (payload.ReversalOfMovementId.HasValue)
            {
                writer.WriteString(
                    "reversal_of_movement_id",
                    payload.ReversalOfMovementId.Value.ToString("D"));
            }
            else
            {
                writer.WriteNull("reversal_of_movement_id");
            }

            writer.WriteString("reason", payload.Reason);
            writer.WriteString(
                "occurred_at_utc",
                payload.OccurredAtUtc
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static CafeCashMovementCanonicalParseResult Parse(
        string operationType,
        string payloadJson)
    {
        if (!string.Equals(
                operationType,
                CafeCashMovementSyncContract.OperationType,
                StringComparison.Ordinal))
        {
            return CafeCashMovementCanonicalParseResult.Fail(
                "CafeCashSync.OperationUnsupported",
                "Cash sync operation type is not supported.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;

            Guid? reversal = root
                .GetProperty("reversal_of_movement_id")
                .ValueKind == JsonValueKind.Null
                ? null
                : Guid.ParseExact(
                    root.GetProperty("reversal_of_movement_id")
                        .GetString() ?? string.Empty,
                    "D");

            CafeCashMovementPostedV1 payload = new(
                Guid.ParseExact(
                    root.GetProperty("movement_id").GetString() ?? string.Empty,
                    "D"),
                Guid.ParseExact(
                    root.GetProperty("store_id").GetString() ?? string.Empty,
                    "D"),
                Guid.ParseExact(
                    root.GetProperty("cashier_session_id").GetString() ??
                    string.Empty,
                    "D"),
                Guid.ParseExact(
                    root.GetProperty("actor_user_id").GetString() ??
                    string.Empty,
                    "D"),
                root.GetProperty("movement_type").GetString() ?? string.Empty,
                root.GetProperty("requested_amount_cents").GetInt64(),
                root.GetProperty("delta_cents").GetInt64(),
                root.GetProperty("reference_type").GetString() ?? string.Empty,
                Guid.ParseExact(
                    root.GetProperty("reference_id").GetString() ??
                    string.Empty,
                    "D"),
                reversal,
                root.GetProperty("reason").GetString() ?? string.Empty,
                DateTimeOffset.ParseExact(
                    root.GetProperty("occurred_at_utc").GetString() ??
                    string.Empty,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));

            string canonical = Serialize(payload);
            if (!string.Equals(canonical, payloadJson, StringComparison.Ordinal))
            {
                return CafeCashMovementCanonicalParseResult.Fail(
                    "CafeCashSync.PayloadNotCanonical",
                    "Cash sync payload must use exact canonical JSON.");
            }

            CafeCashMovementValidationResult validation =
                CafeCashMovementPostedV1Validator.Validate(payload);

            return validation.IsValid
                ? CafeCashMovementCanonicalParseResult.Success(payload)
                : CafeCashMovementCanonicalParseResult.Fail(
                    validation.ErrorCode ?? "CafeCashSync.PayloadInvalid",
                    validation.ErrorMessage ?? "Cash sync payload is invalid.");
        }
        catch (Exception ex) when (
            ex is JsonException or FormatException or
            InvalidOperationException or ArgumentException or
            OverflowException or KeyNotFoundException)
        {
            return CafeCashMovementCanonicalParseResult.Fail(
                "CafeCashSync.PayloadInvalid",
                "Cash sync payload could not be parsed: " + ex.Message);
        }
    }
}

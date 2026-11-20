using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class CafeDeliverySyncContract
{
    public const int SchemaVersion = 1;

    public const string DeliveryConfiguredV1 = "DeliveryConfiguredV1";
    public const string DeliveryDispatchedV1 = "DeliveryDispatchedV1";
    public const string DeliveryResolvedV1 = "DeliveryResolvedV1";
    public const string DeliveryCancelledV1 = "DeliveryCancelledV1";

    public const string DeliveryCreatedEvidenceV1 = "DeliveryCreatedV1";
    public const string DeliveryRevisedEvidenceV1 = "DeliveryRevisedV1";

    public static bool IsSupported(string? operationType) =>
        string.Equals(operationType, DeliveryConfiguredV1, StringComparison.Ordinal) ||
        string.Equals(operationType, DeliveryDispatchedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, DeliveryResolvedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, DeliveryCancelledV1, StringComparison.Ordinal);
}

public sealed record CafeDeliveryRevisionEvidenceV1(
    Guid RevisionId,
    int RevisionNumber,
    int DeliveryVersion,
    string RecipientName,
    string? ContactPhone,
    string DeliveryAddress,
    string? Landmark,
    string? Instructions,
    long DeliveryFeeCents,
    string? CourierReference,
    string ContentHash,
    Guid ActorUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record CafeDeliveryOperationV1(
    Guid OperationId,
    string EvidenceOperationType,
    string? ConfigurationAction,
    Guid DeliveryId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    string Action,
    string? FromStatus,
    string ToStatus,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    Guid CurrentRevisionId,
    CafeDeliveryRevisionEvidenceV1? Revision,
    Guid? SaleId,
    string? Reason,
    string EvidencePayloadHash);

public sealed record CafeDeliveryCanonicalParseResult(
    bool IsValid,
    CafeDeliveryOperationV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeDeliveryCanonicalParseResult Success(CafeDeliveryOperationV1 payload) =>
        new(true, payload, null, null);

    public static CafeDeliveryCanonicalParseResult Fail(string code, string message) =>
        new(false, null, code, message);
}

public static class CafeDeliveryOperationCanonicalJson
{
    public static string Serialize(
        string operationType,
        CafeDeliveryOperationV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string? validation = Validate(operationType, payload);
        if (validation is not null)
        {
            throw new ArgumentException(validation, nameof(payload));
        }

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
            writer.WriteString("operation_id", payload.OperationId.ToString("D"));
            writer.WriteString("evidence_operation_type", payload.EvidenceOperationType);
            WriteNullableString(writer, "configuration_action", payload.ConfigurationAction);
            writer.WriteString("delivery_id", payload.DeliveryId.ToString("D"));
            writer.WriteString("order_id", payload.OrderId.ToString("D"));
            writer.WriteString("store_id", payload.StoreId.ToString("D"));
            writer.WriteNumber("expected_version", payload.ExpectedVersion);
            writer.WriteNumber("resulting_version", payload.ResultingVersion);
            writer.WriteString("action", payload.Action);
            WriteNullableString(writer, "from_status", payload.FromStatus);
            writer.WriteString("resulting_status", payload.ToStatus);
            writer.WriteString("actor_user_id", payload.ActorUserId.ToString("D"));
            writer.WriteString(
                "occurred_at_utc",
                payload.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("current_revision_id", payload.CurrentRevisionId.ToString("D"));

            writer.WritePropertyName("revision");
            if (payload.Revision is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WriteRevision(writer, payload.Revision);
            }

            if (payload.SaleId.HasValue)
                writer.WriteString("sale_id", payload.SaleId.Value.ToString("D"));
            else
                writer.WriteNull("sale_id");

            WriteNullableString(writer, "reason", payload.Reason);
            writer.WriteString("evidence_payload_hash", payload.EvidencePayloadHash);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static CafeDeliveryCanonicalParseResult Parse(
        string operationType,
        string payloadJson)
    {
        if (!CafeDeliverySyncContract.IsSupported(operationType))
        {
            return CafeDeliveryCanonicalParseResult.Fail(
                "CafeDeliverySync.OperationUnsupported",
                "C8 delivery sync operation type is not supported.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;

            CafeDeliveryRevisionEvidenceV1? revision = null;
            JsonElement revisionElement = root.GetProperty("revision");
            if (revisionElement.ValueKind != JsonValueKind.Null)
            {
                revision = new CafeDeliveryRevisionEvidenceV1(
                    Guid.ParseExact(
                        revisionElement.GetProperty("revision_id").GetString() ?? string.Empty,
                        "D"),
                    revisionElement.GetProperty("revision_number").GetInt32(),
                    revisionElement.GetProperty("delivery_version").GetInt32(),
                    revisionElement.GetProperty("recipient_name").GetString() ?? string.Empty,
                    ReadNullableString(revisionElement, "contact_phone"),
                    revisionElement.GetProperty("delivery_address").GetString() ?? string.Empty,
                    ReadNullableString(revisionElement, "landmark"),
                    ReadNullableString(revisionElement, "instructions"),
                    revisionElement.GetProperty("delivery_fee_cents").GetInt64(),
                    ReadNullableString(revisionElement, "courier_reference"),
                    revisionElement.GetProperty("content_hash").GetString() ?? string.Empty,
                    Guid.ParseExact(
                        revisionElement.GetProperty("actor_user_id").GetString() ?? string.Empty,
                        "D"),
                    DateTimeOffset.ParseExact(
                        revisionElement.GetProperty("created_at_utc").GetString() ?? string.Empty,
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind));
            }

            Guid? saleId =
                root.GetProperty("sale_id").ValueKind == JsonValueKind.Null
                    ? null
                    : Guid.ParseExact(
                        root.GetProperty("sale_id").GetString() ?? string.Empty,
                        "D");

            CafeDeliveryOperationV1 payload = new(
                Guid.ParseExact(root.GetProperty("operation_id").GetString() ?? string.Empty, "D"),
                root.GetProperty("evidence_operation_type").GetString() ?? string.Empty,
                ReadNullableString(root, "configuration_action"),
                Guid.ParseExact(root.GetProperty("delivery_id").GetString() ?? string.Empty, "D"),
                Guid.ParseExact(root.GetProperty("order_id").GetString() ?? string.Empty, "D"),
                Guid.ParseExact(root.GetProperty("store_id").GetString() ?? string.Empty, "D"),
                root.GetProperty("expected_version").GetInt32(),
                root.GetProperty("resulting_version").GetInt32(),
                root.GetProperty("action").GetString() ?? string.Empty,
                ReadNullableString(root, "from_status"),
                root.GetProperty("resulting_status").GetString() ?? string.Empty,
                Guid.ParseExact(root.GetProperty("actor_user_id").GetString() ?? string.Empty, "D"),
                DateTimeOffset.ParseExact(
                    root.GetProperty("occurred_at_utc").GetString() ?? string.Empty,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                Guid.ParseExact(root.GetProperty("current_revision_id").GetString() ?? string.Empty, "D"),
                revision,
                saleId,
                ReadNullableString(root, "reason"),
                root.GetProperty("evidence_payload_hash").GetString() ?? string.Empty);

            string? validation = Validate(operationType, payload);
            if (validation is not null)
            {
                return CafeDeliveryCanonicalParseResult.Fail(
                    "CafeDeliverySync.PayloadInvalid",
                    validation);
            }

            string canonical = Serialize(operationType, payload);
            if (!string.Equals(canonical, payloadJson, StringComparison.Ordinal))
            {
                return CafeDeliveryCanonicalParseResult.Fail(
                    "CafeDeliverySync.PayloadNotCanonical",
                    "C8 delivery sync payload must use exact canonical JSON.");
            }

            return CafeDeliveryCanonicalParseResult.Success(payload);
        }
        catch (Exception ex) when (
            ex is JsonException or FormatException or
            InvalidOperationException or ArgumentException or
            OverflowException or KeyNotFoundException)
        {
            return CafeDeliveryCanonicalParseResult.Fail(
                "CafeDeliverySync.PayloadInvalid",
                "C8 delivery sync payload could not be parsed: " + ex.Message);
        }
    }

    public static string? Validate(
        string operationType,
        CafeDeliveryOperationV1 payload)
    {
        if (!CafeDeliverySyncContract.IsSupported(operationType))
            return "Unsupported delivery sync operation.";

        if (payload.OperationId == Guid.Empty ||
            payload.DeliveryId == Guid.Empty ||
            payload.OrderId == Guid.Empty ||
            payload.StoreId == Guid.Empty ||
            payload.ActorUserId == Guid.Empty ||
            payload.CurrentRevisionId == Guid.Empty ||
            payload.ExpectedVersion < 0 ||
            payload.ResultingVersion != payload.ExpectedVersion + 1 ||
            string.IsNullOrWhiteSpace(payload.EvidenceOperationType) ||
            string.IsNullOrWhiteSpace(payload.Action) ||
            string.IsNullOrWhiteSpace(payload.ToStatus) ||
            string.IsNullOrWhiteSpace(payload.EvidencePayloadHash) ||
            payload.EvidencePayloadHash.Length != 64)
        {
            return "Delivery sync requires stable identities, contiguous versions, action/status, and 64-character evidence hash.";
        }

        if (string.Equals(operationType, CafeDeliverySyncContract.DeliveryConfiguredV1, StringComparison.Ordinal))
        {
            bool create =
                string.Equals(payload.ConfigurationAction, "Created", StringComparison.Ordinal) &&
                string.Equals(payload.EvidenceOperationType, CafeDeliverySyncContract.DeliveryCreatedEvidenceV1, StringComparison.Ordinal) &&
                string.Equals(payload.Action, "Created", StringComparison.Ordinal) &&
                payload.ExpectedVersion == 0 &&
                payload.ResultingVersion == 1 &&
                payload.FromStatus is null;

            bool revise =
                string.Equals(payload.ConfigurationAction, "Revised", StringComparison.Ordinal) &&
                string.Equals(payload.EvidenceOperationType, CafeDeliverySyncContract.DeliveryRevisedEvidenceV1, StringComparison.Ordinal) &&
                string.Equals(payload.Action, "Revised", StringComparison.Ordinal) &&
                payload.ExpectedVersion > 0 &&
                string.Equals(payload.FromStatus, "Pending", StringComparison.Ordinal);

            if ((!create && !revise) ||
                !string.Equals(payload.ToStatus, "Pending", StringComparison.Ordinal) ||
                payload.Revision is null ||
                payload.Revision.RevisionId != payload.CurrentRevisionId ||
                payload.Revision.DeliveryVersion != payload.ResultingVersion ||
                payload.Revision.RevisionNumber <= 0 ||
                payload.Revision.ActorUserId != payload.ActorUserId ||
                payload.Revision.DeliveryFeeCents < 0 ||
                string.IsNullOrWhiteSpace(payload.Revision.RecipientName) ||
                string.IsNullOrWhiteSpace(payload.Revision.DeliveryAddress) ||
                string.IsNullOrWhiteSpace(payload.Revision.ContentHash) ||
                payload.Revision.ContentHash.Length != 64 ||
                payload.SaleId.HasValue ||
                payload.Reason is not null)
            {
                return "Configured delivery payload is inconsistent with create/revise immutable revision evidence.";
            }

            return null;
        }

        if (payload.Revision is not null)
            return "Lifecycle delivery sync operations must not carry a mutable revision body.";

        if (payload.ConfigurationAction is not null)
            return "Lifecycle delivery sync operations must not carry ConfigurationAction.";

        if (string.Equals(operationType, CafeDeliverySyncContract.DeliveryDispatchedV1, StringComparison.Ordinal))
        {
            return
                string.Equals(payload.EvidenceOperationType, "DeliveryDispatchedV1", StringComparison.Ordinal) &&
                string.Equals(payload.Action, "Dispatched", StringComparison.Ordinal) &&
                string.Equals(payload.FromStatus, "Pending", StringComparison.Ordinal) &&
                string.Equals(payload.ToStatus, "Dispatched", StringComparison.Ordinal) &&
                payload.SaleId.HasValue &&
                payload.Reason is null
                    ? null
                    : "Delivery dispatch payload is inconsistent.";
        }

        if (string.Equals(operationType, CafeDeliverySyncContract.DeliveryResolvedV1, StringComparison.Ordinal))
        {
            bool delivered =
                string.Equals(payload.Action, "Delivered", StringComparison.Ordinal) &&
                string.Equals(payload.ToStatus, "Delivered", StringComparison.Ordinal) &&
                payload.Reason is null;
            bool failed =
                string.Equals(payload.Action, "Failed", StringComparison.Ordinal) &&
                string.Equals(payload.ToStatus, "Failed", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(payload.Reason);

            return
                string.Equals(payload.EvidenceOperationType, "DeliveryResolvedV1", StringComparison.Ordinal) &&
                string.Equals(payload.FromStatus, "Dispatched", StringComparison.Ordinal) &&
                payload.SaleId.HasValue &&
                (delivered || failed)
                    ? null
                    : "Delivery resolution payload is inconsistent.";
        }

        return
            string.Equals(payload.EvidenceOperationType, "DeliveryCancelledV1", StringComparison.Ordinal) &&
            string.Equals(payload.Action, "Cancelled", StringComparison.Ordinal) &&
            string.Equals(payload.FromStatus, "Pending", StringComparison.Ordinal) &&
            string.Equals(payload.ToStatus, "Cancelled", StringComparison.Ordinal) &&
            !payload.SaleId.HasValue &&
            !string.IsNullOrWhiteSpace(payload.Reason)
                ? null
                : "Delivery cancellation payload is inconsistent.";
    }

    private static void WriteRevision(
        Utf8JsonWriter writer,
        CafeDeliveryRevisionEvidenceV1 revision)
    {
        writer.WriteStartObject();
        writer.WriteString("revision_id", revision.RevisionId.ToString("D"));
        writer.WriteNumber("revision_number", revision.RevisionNumber);
        writer.WriteNumber("delivery_version", revision.DeliveryVersion);
        writer.WriteString("recipient_name", revision.RecipientName);
        WriteNullableString(writer, "contact_phone", revision.ContactPhone);
        writer.WriteString("delivery_address", revision.DeliveryAddress);
        WriteNullableString(writer, "landmark", revision.Landmark);
        WriteNullableString(writer, "instructions", revision.Instructions);
        writer.WriteNumber("delivery_fee_cents", revision.DeliveryFeeCents);
        WriteNullableString(writer, "courier_reference", revision.CourierReference);
        writer.WriteString("content_hash", revision.ContentHash);
        writer.WriteString("actor_user_id", revision.ActorUserId.ToString("D"));
        writer.WriteString(
            "created_at_utc",
            revision.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static string? ReadNullableString(JsonElement element, string property)
    {
        JsonElement value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : value.GetString();
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string property,
        string? value)
    {
        if (value is null)
            writer.WriteNull(property);
        else
            writer.WriteString(property, value);
    }
}

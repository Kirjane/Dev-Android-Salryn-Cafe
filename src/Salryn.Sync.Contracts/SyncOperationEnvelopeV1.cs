using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record SyncOperationEnvelopeV1(
    int ProtocolVersion,
    Guid OperationId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    long LocalSequence,
    string OperationType,
    int SchemaVersion,
    Guid AggregateId,
    long? BaseVersion,
    DateTimeOffset OccurredAtLocal,
    string PayloadJson,
    string PayloadHash,
    string OperationHash);

public sealed record SyncEnvelopeValidationResult(bool IsValid, string? ErrorCode, string? ErrorMessage)
{
    public static SyncEnvelopeValidationResult Success() => new(true, null, null);

    public static SyncEnvelopeValidationResult Fail(string code, string message) =>
        new(false, code, message);
}

public static class SyncProtocolV1
{
    public const int ProtocolVersion = 1;
    public const string PendingUpload = "PendingUpload";
    public const string ReceivedByCentral = "ReceivedByCentral";
    public const string CentralReceived = "Received";

    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SyncOperationEnvelopeV1 CreateEnvelope(
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        long localSequence,
        string operationType,
        int schemaVersion,
        Guid aggregateId,
        long? baseVersion,
        DateTimeOffset occurredAtLocal,
        string payloadJson,
        Guid? operationId = null)
    {
        Guid resolvedOperationId = operationId is { } supplied && supplied != Guid.Empty
            ? supplied
            : Guid.CreateVersion7();

        string payloadHash = ComputePayloadHash(payloadJson);
        SyncOperationEnvelopeV1 unsigned = new(
            ProtocolVersion,
            resolvedOperationId,
            deviceId,
            storeId,
            actorUserId,
            localSequence,
            operationType,
            schemaVersion,
            aggregateId,
            baseVersion,
            occurredAtLocal,
            payloadJson,
            payloadHash,
            string.Empty);

        return unsigned with { OperationHash = ComputeOperationHash(unsigned) };
    }

    public static string ComputePayloadHash(string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)))
            .ToLowerInvariant();
    }

    public static string ComputeOperationHash(SyncOperationEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using MemoryStream buffer = new();
        WriteInt32(buffer, envelope.ProtocolVersion);
        WriteGuid(buffer, envelope.OperationId);
        WriteGuid(buffer, envelope.DeviceId);
        WriteGuid(buffer, envelope.StoreId);
        WriteGuid(buffer, envelope.ActorUserId);
        WriteInt64(buffer, envelope.LocalSequence);
        WriteString(buffer, envelope.OperationType);
        WriteInt32(buffer, envelope.SchemaVersion);
        WriteGuid(buffer, envelope.AggregateId);

        if (envelope.BaseVersion.HasValue)
        {
            buffer.WriteByte(1);
            WriteInt64(buffer, envelope.BaseVersion.Value);
        }
        else
        {
            buffer.WriteByte(0);
        }

        WriteString(buffer, envelope.OccurredAtLocal.ToUniversalTime().ToString("O"));
        WriteString(buffer, envelope.PayloadHash);

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    public static SyncEnvelopeValidationResult Validate(SyncOperationEnvelopeV1 envelope)
    {
        if (envelope.ProtocolVersion != ProtocolVersion)
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.ProtocolVersionUnsupported",
                "Only sync protocol version 1 is supported.");
        }

        if (envelope.OperationId == Guid.Empty ||
            envelope.DeviceId == Guid.Empty ||
            envelope.StoreId == Guid.Empty ||
            envelope.ActorUserId == Guid.Empty ||
            envelope.AggregateId == Guid.Empty)
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.RequiredIdentityMissing",
                "Operation, device, store, actor and aggregate IDs are required.");
        }

        if (envelope.LocalSequence <= 0)
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.LocalSequenceInvalid",
                "Local sequence must be positive.");
        }

        if (string.IsNullOrWhiteSpace(envelope.OperationType))
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.OperationTypeRequired",
                "Operation type is required.");
        }

        if (envelope.SchemaVersion <= 0)
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.SchemaVersionInvalid",
                "Operation schema version must be positive.");
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(envelope.PayloadJson);
        }
        catch (JsonException)
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.PayloadJsonInvalid",
                "Payload JSON is invalid.");
        }

        if (!LowerHexSha256.IsMatch(envelope.PayloadHash))
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.PayloadHashFormatInvalid",
                "Payload hash must be lowercase SHA-256 hex.");
        }

        string expectedPayloadHash = ComputePayloadHash(envelope.PayloadJson);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedPayloadHash),
                Encoding.ASCII.GetBytes(envelope.PayloadHash)))
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.PayloadHashMismatch",
                "Payload hash does not match the exact UTF-8 payload text.");
        }

        if (!LowerHexSha256.IsMatch(envelope.OperationHash))
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.OperationHashFormatInvalid",
                "Operation hash must be lowercase SHA-256 hex.");
        }

        string expectedOperationHash = ComputeOperationHash(envelope);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedOperationHash),
                Encoding.ASCII.GetBytes(envelope.OperationHash)))
        {
            return SyncEnvelopeValidationResult.Fail(
                "Sync.OperationHashMismatch",
                "Operation hash does not match canonical immutable envelope metadata.");
        }

        return SyncEnvelopeValidationResult.Success();
    }

    public static bool ImmutableContentEquals(
        SyncOperationEnvelopeV1 left,
        SyncOperationEnvelopeV1 right)
    {
        return left.ProtocolVersion == right.ProtocolVersion &&
               left.OperationId == right.OperationId &&
               left.DeviceId == right.DeviceId &&
               left.StoreId == right.StoreId &&
               left.ActorUserId == right.ActorUserId &&
               left.LocalSequence == right.LocalSequence &&
               string.Equals(left.OperationType, right.OperationType, StringComparison.Ordinal) &&
               left.SchemaVersion == right.SchemaVersion &&
               left.AggregateId == right.AggregateId &&
               left.BaseVersion == right.BaseVersion &&
               left.OccurredAtLocal.ToUniversalTime() == right.OccurredAtLocal.ToUniversalTime() &&
               string.Equals(left.PayloadJson, right.PayloadJson, StringComparison.Ordinal) &&
               string.Equals(left.PayloadHash, right.PayloadHash, StringComparison.Ordinal) &&
               string.Equals(left.OperationHash, right.OperationHash, StringComparison.Ordinal);
    }

    private static void WriteGuid(Stream stream, Guid value) =>
        WriteString(stream, value.ToString("D"));

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

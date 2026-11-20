using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record AppliedBusinessChangeValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static AppliedBusinessChangeValidationResult Success() => new(true, null, null);

    public static AppliedBusinessChangeValidationResult Fail(string code, string message) =>
        new(false, code, message);
}

public static class AppliedBusinessChangeProtocolV1
{
    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static AppliedBusinessChangeV1 Create(
        Guid centralInstanceId,
        long serverSequence,
        Guid sourceOperationId,
        Guid originDeviceId,
        long originLocalSequence,
        Guid storeId,
        string changeType,
        int schemaVersion,
        Guid aggregateId,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        Guid? changeId = null)
    {
        Guid resolvedChangeId = changeId is { } supplied && supplied != Guid.Empty
            ? supplied
            : Guid.CreateVersion7();

        string payloadHash = SyncProtocolV1.ComputePayloadHash(payloadJson);
        AppliedBusinessChangeV1 unsigned = new(
            centralInstanceId,
            serverSequence,
            resolvedChangeId,
            sourceOperationId,
            originDeviceId,
            originLocalSequence,
            storeId,
            changeType,
            schemaVersion,
            aggregateId,
            payloadJson,
            payloadHash,
            string.Empty,
            createdAtUtc.ToUniversalTime());

        return unsigned with { ChangeHash = ComputeChangeHash(unsigned) };
    }

    public static string ComputeChangeHash(AppliedBusinessChangeV1 change)
    {
        ArgumentNullException.ThrowIfNull(change);

        using MemoryStream buffer = new();
        WriteGuid(buffer, change.CentralInstanceId);
        WriteInt64(buffer, change.ServerSequence);
        WriteGuid(buffer, change.ChangeId);
        WriteGuid(buffer, change.SourceOperationId);
        WriteGuid(buffer, change.OriginDeviceId);
        WriteInt64(buffer, change.OriginLocalSequence);
        WriteGuid(buffer, change.StoreId);
        WriteString(buffer, change.ChangeType);
        WriteInt32(buffer, change.SchemaVersion);
        WriteGuid(buffer, change.AggregateId);
        WriteString(buffer, change.PayloadHash);
        WriteString(buffer, change.CreatedAtUtc.ToUniversalTime().ToString("O"));

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    public static AppliedBusinessChangeValidationResult Validate(AppliedBusinessChangeV1 change)
    {
        if (change.CentralInstanceId == Guid.Empty ||
            change.ChangeId == Guid.Empty ||
            change.SourceOperationId == Guid.Empty ||
            change.OriginDeviceId == Guid.Empty ||
            change.StoreId == Guid.Empty ||
            change.AggregateId == Guid.Empty)
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangeRequiredIdentityMissing",
                "Central instance, change, source operation, origin device, store and aggregate identities are required.");
        }

        if (change.ServerSequence <= 0 || change.OriginLocalSequence <= 0)
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangeSequenceInvalid",
                "Server and origin local sequences must be positive.");
        }

        if (string.IsNullOrWhiteSpace(change.ChangeType) || change.SchemaVersion <= 0)
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangeContractInvalid",
                "Change type and positive schema version are required.");
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(change.PayloadJson);
        }
        catch (JsonException)
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangePayloadJsonInvalid",
                "Applied change payload JSON is invalid.");
        }

        if (!LowerHexSha256.IsMatch(change.PayloadHash))
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangePayloadHashFormatInvalid",
                "Applied change payload hash must be lowercase SHA-256 hex.");
        }

        string expectedPayloadHash = SyncProtocolV1.ComputePayloadHash(change.PayloadJson);
        if (!FixedEquals(expectedPayloadHash, change.PayloadHash))
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangePayloadHashMismatch",
                "Applied change payload hash does not match the exact payload text.");
        }

        if (!LowerHexSha256.IsMatch(change.ChangeHash))
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangeHashFormatInvalid",
                "Applied change hash must be lowercase SHA-256 hex.");
        }

        string expectedChangeHash = ComputeChangeHash(change);
        if (!FixedEquals(expectedChangeHash, change.ChangeHash))
        {
            return AppliedBusinessChangeValidationResult.Fail(
                "Sync.ChangeHashMismatch",
                "Applied change hash does not match immutable change metadata.");
        }

        return AppliedBusinessChangeValidationResult.Success();
    }

    public static bool ImmutableContentEquals(
        AppliedBusinessChangeV1 left,
        AppliedBusinessChangeV1 right) =>
        left.CentralInstanceId == right.CentralInstanceId &&
        left.ServerSequence == right.ServerSequence &&
        left.ChangeId == right.ChangeId &&
        left.SourceOperationId == right.SourceOperationId &&
        left.OriginDeviceId == right.OriginDeviceId &&
        left.OriginLocalSequence == right.OriginLocalSequence &&
        left.StoreId == right.StoreId &&
        string.Equals(left.ChangeType, right.ChangeType, StringComparison.Ordinal) &&
        left.SchemaVersion == right.SchemaVersion &&
        left.AggregateId == right.AggregateId &&
        string.Equals(left.PayloadJson, right.PayloadJson, StringComparison.Ordinal) &&
        string.Equals(left.PayloadHash, right.PayloadHash, StringComparison.Ordinal) &&
        string.Equals(left.ChangeHash, right.ChangeHash, StringComparison.Ordinal) &&
        left.CreatedAtUtc.ToUniversalTime() == right.CreatedAtUtc.ToUniversalTime();

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));

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

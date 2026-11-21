using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Salryn.Sync.Contracts;

public sealed record CafeShiftOperationV1(Guid SessionId, Guid DeviceId, Guid StoreId, Guid ActorUserId,
    long CashAmountCents, DateTimeOffset OccurredAtUtc, bool PhysicalCountConfirmed,
    string CashSourceDigest, IReadOnlyList<Guid> PrerequisiteOperationIds);

public static class CafeShiftSyncContract
{
    public const string Opened = "CafeShiftOpenedV1";
    public const string Closed = "CafeShiftClosedV1";
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static bool IsSupported(string type) => type is Opened or Closed;
    public static Guid SessionIdFor(Guid operationId) => new(SHA256.HashData(
        Encoding.UTF8.GetBytes("Salryn.Cafe.Shift:" + operationId.ToString("D"))).AsSpan(0, 16));
    public static string Serialize(CafeShiftOperationV1 payload) => JsonSerializer.Serialize(payload, Json);
    public static CafeShiftOperationV1? Parse(string type, string json)
    {
        if (!IsSupported(type)) return null;
        try
        {
            var value = JsonSerializer.Deserialize<CafeShiftOperationV1>(json, Json);
            if (value is null || value.SessionId == Guid.Empty || value.DeviceId == Guid.Empty ||
                value.StoreId == Guid.Empty || value.ActorUserId == Guid.Empty || value.CashAmountCents < 0 ||
                value.OccurredAtUtc == default || value.PrerequisiteOperationIds is null || value.CashSourceDigest is null ||
                value.PrerequisiteOperationIds.Any(id => id == Guid.Empty) ||
                value.PrerequisiteOperationIds.Distinct().Count() != value.PrerequisiteOperationIds.Count ||
                (type == Opened && (value.PhysicalCountConfirmed || value.CashSourceDigest.Length != 0 || value.PrerequisiteOperationIds.Count != 0)) ||
                (type == Closed && (!value.PhysicalCountConfirmed || value.CashSourceDigest.Length != 64)) ||
                !string.Equals(Serialize(value), json, StringComparison.Ordinal)) return null;
            return value;
        }
        catch (JsonException) { return null; }
    }
}

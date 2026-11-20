using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Salryn.Sync.Contracts;

public sealed record SyncBootstrapRoleV1(
    Guid Id,
    string Name,
    bool IsSystemRole);

public sealed record SyncBootstrapUserV1(
    Guid Id,
    string Username,
    string DisplayName,
    Guid RoleId,
    int Status);

public sealed record SyncBootstrapUnitV1(
    Guid Id,
    string Name,
    string? Abbreviation,
    bool AllowsDecimalQuantity,
    bool IsActive);

public sealed record SyncBootstrapProductV1(
    Guid Id,
    string? Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid UnitId,
    long? CostPriceCents,
    long SellingPriceCents,
    long? WholesalePriceCents,
    long StockOnHandX1000,
    long ReorderLevelX1000,
    bool TrackStock,
    bool AllowDiscount,
    bool IsActive,
    int TaxClassification);

public sealed record SyncBootstrapCashierSessionV1(
    Guid Id,
    Guid UserId,
    DateTimeOffset OpenedAt,
    long OpeningCashCents,
    long ExpectedCashCents,
    int Status,
    string? Notes,
    long LedgerNetDeltaCents = 0);

public sealed record SyncBootstrapCustomerV1(
    Guid Id,
    string FullName,
    string? Phone,
    string? Address,
    string? Note,
    long? CreditLimitCents,
    bool IsActive,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long OutstandingReceivableCents = 0);

public sealed record SyncBootstrapSnapshotV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    IReadOnlyList<SyncBootstrapRoleV1> Roles,
    IReadOnlyList<SyncBootstrapUserV1> Users,
    IReadOnlyList<SyncBootstrapUnitV1> Units,
    IReadOnlyList<SyncBootstrapProductV1> Products,
    IReadOnlyList<SyncBootstrapCashierSessionV1> CashierSessions,
    IReadOnlyList<SyncBootstrapCustomerV1> Customers,
    string SnapshotId,
    string SnapshotHash);

public sealed record SyncBootstrapValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SyncBootstrapValidationResult Success() => new(true, null, null);

    public static SyncBootstrapValidationResult Fail(string code, string message) =>
        new(false, code, message);
}

public static class SyncBootstrapSnapshotProtocolV1
{
    public const int ProtocolVersion = 1;
    public const string SnapshotIdPrefix = "f1e-bootstrap-v1:";

    public static SyncBootstrapSnapshotV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        IEnumerable<SyncBootstrapRoleV1> roles,
        IEnumerable<SyncBootstrapUserV1> users,
        IEnumerable<SyncBootstrapUnitV1> units,
        IEnumerable<SyncBootstrapProductV1> products,
        IEnumerable<SyncBootstrapCashierSessionV1> cashierSessions,
        IEnumerable<SyncBootstrapCustomerV1> customers)
    {
        SyncBootstrapSnapshotV1 unsigned = new(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            roles.OrderBy(x => x.Id).ToArray(),
            users.OrderBy(x => x.Id).ToArray(),
            units.OrderBy(x => x.Id).ToArray(),
            products.OrderBy(x => x.Id).ToArray(),
            cashierSessions.OrderBy(x => x.Id).ToArray(),
            customers.OrderBy(x => x.Id).ToArray(),
            string.Empty,
            string.Empty);

        string hash = ComputeSnapshotHash(unsigned);
        return unsigned with
        {
            SnapshotHash = hash,
            SnapshotId = SnapshotIdPrefix + hash
        };
    }

    public static string ComputeSnapshotHash(SyncBootstrapSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using MemoryStream buffer = new();
        WriteInt32(buffer, ProtocolVersion);
        WriteGuid(buffer, snapshot.CentralInstanceId);
        WriteGuid(buffer, snapshot.StoreId);
        WriteInt64(buffer, snapshot.BaselineServerSequence);

        SyncBootstrapRoleV1[] roles = snapshot.Roles.OrderBy(x => x.Id).ToArray();
        WriteInt32(buffer, roles.Length);
        foreach (SyncBootstrapRoleV1 role in roles)
        {
            WriteGuid(buffer, role.Id);
            WriteString(buffer, role.Name);
            WriteBool(buffer, role.IsSystemRole);
        }

        SyncBootstrapUserV1[] users = snapshot.Users.OrderBy(x => x.Id).ToArray();
        WriteInt32(buffer, users.Length);
        foreach (SyncBootstrapUserV1 user in users)
        {
            WriteGuid(buffer, user.Id);
            WriteString(buffer, user.Username);
            WriteString(buffer, user.DisplayName);
            WriteGuid(buffer, user.RoleId);
            WriteInt32(buffer, user.Status);
        }

        SyncBootstrapUnitV1[] units = snapshot.Units.OrderBy(x => x.Id).ToArray();
        WriteInt32(buffer, units.Length);
        foreach (SyncBootstrapUnitV1 unit in units)
        {
            WriteGuid(buffer, unit.Id);
            WriteString(buffer, unit.Name);
            WriteNullableString(buffer, unit.Abbreviation);
            WriteBool(buffer, unit.AllowsDecimalQuantity);
            WriteBool(buffer, unit.IsActive);
        }

        SyncBootstrapProductV1[] products = snapshot.Products.OrderBy(x => x.Id).ToArray();
        WriteInt32(buffer, products.Length);
        foreach (SyncBootstrapProductV1 product in products)
        {
            WriteGuid(buffer, product.Id);
            WriteNullableString(buffer, product.Sku);
            WriteNullableString(buffer, product.Barcode);
            WriteString(buffer, product.Name);
            WriteNullableString(buffer, product.Description);
            WriteGuid(buffer, product.UnitId);
            WriteNullableInt64(buffer, product.CostPriceCents);
            WriteInt64(buffer, product.SellingPriceCents);
            WriteNullableInt64(buffer, product.WholesalePriceCents);
            WriteInt64(buffer, product.StockOnHandX1000);
            WriteInt64(buffer, product.ReorderLevelX1000);
            WriteBool(buffer, product.TrackStock);
            WriteBool(buffer, product.AllowDiscount);
            WriteBool(buffer, product.IsActive);
            WriteInt32(buffer, product.TaxClassification);
        }

        SyncBootstrapCashierSessionV1[] sessions =
            snapshot.CashierSessions.OrderBy(x => x.Id).ToArray();
        WriteInt32(buffer, sessions.Length);
        foreach (SyncBootstrapCashierSessionV1 session in sessions)
        {
            WriteGuid(buffer, session.Id);
            WriteGuid(buffer, session.UserId);
            WriteString(buffer, session.OpenedAt.ToUniversalTime().ToString("O"));
            WriteInt64(buffer, session.OpeningCashCents);
            WriteInt64(buffer, session.ExpectedCashCents);
            WriteInt32(buffer, session.Status);
            WriteNullableString(buffer, session.Notes);
            WriteInt64(buffer, session.LedgerNetDeltaCents);
        }

        SyncBootstrapCustomerV1[] customers = snapshot.Customers.OrderBy(x => x.Id).ToArray();
        WriteInt32(buffer, customers.Length);
        foreach (SyncBootstrapCustomerV1 customer in customers)
        {
            WriteGuid(buffer, customer.Id);
            WriteString(buffer, customer.FullName);
            WriteNullableString(buffer, customer.Phone);
            WriteNullableString(buffer, customer.Address);
            WriteNullableString(buffer, customer.Note);
            WriteNullableInt64(buffer, customer.CreditLimitCents);
            WriteBool(buffer, customer.IsActive);
            WriteGuid(buffer, customer.CreatedByUserId);
            WriteString(buffer, customer.CreatedAt.ToUniversalTime().ToString("O"));
            WriteString(buffer, customer.UpdatedAt.ToUniversalTime().ToString("O"));
            WriteInt64(buffer, customer.OutstandingReceivableCents);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    public static SyncBootstrapValidationResult Validate(SyncBootstrapSnapshotV1 snapshot)
    {
        if (snapshot.CentralInstanceId == Guid.Empty || snapshot.StoreId == Guid.Empty)
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapIdentityRequired",
                "Bootstrap central instance and store identities are required.");
        }

        if (snapshot.BaselineServerSequence < 0)
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapBaselineInvalid",
                "Bootstrap baseline server sequence cannot be negative.");
        }

        if (snapshot.Roles.Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name)) ||
            snapshot.Users.Any(x =>
                x.Id == Guid.Empty ||
                x.RoleId == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.Username) ||
                string.IsNullOrWhiteSpace(x.DisplayName)) ||
            snapshot.Units.Any(x => x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name)) ||
            snapshot.Products.Any(x =>
                x.Id == Guid.Empty ||
                x.UnitId == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.Name)) ||
            snapshot.CashierSessions.Any(x => x.Id == Guid.Empty || x.UserId == Guid.Empty) ||
            snapshot.Customers.Any(x =>
                x.Id == Guid.Empty ||
                x.CreatedByUserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.FullName) ||
                x.OutstandingReceivableCents < 0))
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapPrerequisiteIdentityInvalid",
                "Bootstrap prerequisite identities and required names must be non-empty.");
        }

        if (snapshot.Roles.Select(x => x.Id).Distinct().Count() != snapshot.Roles.Count ||
            snapshot.Users.Select(x => x.Id).Distinct().Count() != snapshot.Users.Count ||
            snapshot.Units.Select(x => x.Id).Distinct().Count() != snapshot.Units.Count ||
            snapshot.Products.Select(x => x.Id).Distinct().Count() != snapshot.Products.Count ||
            snapshot.CashierSessions.Select(x => x.Id).Distinct().Count() != snapshot.CashierSessions.Count ||
            snapshot.Customers.Select(x => x.Id).Distinct().Count() != snapshot.Customers.Count)
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapDuplicateIdentity",
                "Bootstrap prerequisite collections cannot contain duplicate identities.");
        }

        HashSet<Guid> roleIds = snapshot.Roles.Select(x => x.Id).ToHashSet();
        HashSet<Guid> userIds = snapshot.Users.Select(x => x.Id).ToHashSet();
        HashSet<Guid> unitIds = snapshot.Units.Select(x => x.Id).ToHashSet();

        if (snapshot.Users.Any(x => !roleIds.Contains(x.RoleId)) ||
            snapshot.Products.Any(x => !unitIds.Contains(x.UnitId)) ||
            snapshot.CashierSessions.Any(x => !userIds.Contains(x.UserId)) ||
            snapshot.Customers.Any(x => !userIds.Contains(x.CreatedByUserId)))
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapPrerequisiteReferenceMissing",
                "Bootstrap prerequisite references are incomplete.");
        }

        string expectedHash = ComputeSnapshotHash(snapshot);
        if (!FixedEquals(expectedHash, snapshot.SnapshotHash))
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapSnapshotHashMismatch",
                "Bootstrap snapshot hash does not match canonical snapshot content.");
        }

        string expectedId = SnapshotIdPrefix + expectedHash;
        if (!string.Equals(expectedId, snapshot.SnapshotId, StringComparison.Ordinal))
        {
            return SyncBootstrapValidationResult.Fail(
                "Sync.BootstrapSnapshotIdMismatch",
                "Bootstrap snapshot identity does not match canonical snapshot content.");
        }

        return SyncBootstrapValidationResult.Success();
    }

    private static bool FixedEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }

    private static void WriteBool(Stream stream, bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);

    private static void WriteGuid(Stream stream, Guid value) => WriteString(stream, value.ToString("D"));

    private static void WriteNullableString(Stream stream, string? value)
    {
        if (value is null)
        {
            stream.WriteByte(0);
            return;
        }

        stream.WriteByte(1);
        WriteString(stream, value);
    }

    private static void WriteNullableInt64(Stream stream, long? value)
    {
        if (!value.HasValue)
        {
            stream.WriteByte(0);
            return;
        }

        stream.WriteByte(1);
        WriteInt64(stream, value.Value);
    }

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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Runtime;

namespace Salryn.Cafe.I6BTests;

/// <summary>Synthetic approved binding and actual canonical SQLite/domain. No prototype ledger.</summary>
internal sealed class CanonicalCashierFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "salryn-native-canonical-" + Guid.NewGuid().ToString("N"));
    public MobileProductionComposition Runtime { get; }
    public MobileCashierController Controller { get; }
    public Guid ActorId { get; }
    public Guid StoreId { get; }
    public Guid DeviceId { get; } = Guid.NewGuid();
    public Guid ProductId { get; }
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public InMemorySecretStore Secrets { get; } = new();

    public CanonicalCashierFixture()
    {
        Runtime = MobileProductionComposition.Create(Root);
        Runtime.InitializeDatabaseAsync().GetAwaiter().GetResult();
        ActorId = Value(Runtime.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
            "Synthetic Cafe", null, null, null, null, 80, "synthetic", "Synthetic Operator", "SyntheticOnly123!"))).AdminUserId;
        StoreId = Guid.Parse(Scalar("SELECT store_id FROM deployment_identity LIMIT 1;").ToString()!);
        Guid unit = Guid.Parse(Scalar("SELECT id FROM units LIMIT 1;").ToString()!);
        ProductId = Value(Runtime.MenuCatalog.SaveItem(new MenuItemMutation(null, null, "Synthetic espresso", null,
            unit, "SYNTHETIC-ESPRESSO", null, 12500, 4000, true, false))).ProductId;
        Value(Runtime.MenuCatalog.PublishCurrent());
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo());
        Guid tenant = Guid.NewGuid();
        DateTimeOffset issued = Now.AddMinutes(-1), expires = Now.AddHours(1);
        string payload = $"{DeviceId:D}|{tenant:D}|{StoreId:D}|Salryn.Cafe|1|{issued:O}|{expires:O}";
        MobileSignedOfflineLease lease = new(DeviceId, tenant, StoreId, "Salryn.Cafe", 1, issued, expires,
            Convert.ToBase64String(signingKey.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256)), publicKey);
        MobileEnrollmentBinding binding = new(DeviceId, tenant, StoreId, "Salryn.Cafe", 1, publicKey, lease);
        Secrets.WriteAsync(ProtectedMobileEnrollmentReader.SecretKey, JsonSerializer.SerializeToUtf8Bytes(binding)).GetAwaiter().GetResult();
        Controller = NewController();
    }

    public MobileCashierController NewController() => new(Runtime, new(new ProtectedMobileEnrollmentReader(Secrets),
        Secrets, () => new(Now, 10000, 1)));

    public async Task LoginAsync(MobileCashierController? controller = null) =>
        Value(await (controller ?? Controller).LoginAsync("synthetic", "SyntheticOnly123!"));

    public async Task<CafeOrderDetail> ReadyOrderAsync()
    {
        Guid orderId = Guid.NewGuid();
        Value(await Controller.SaveOrderAsync(new(Guid.NewGuid(), orderId, 0, Now,
            CafeOrderServiceModes.Takeout, null, [new(Guid.NewGuid(), ProductId, null, [], 1000, null, 0)])));
        CafeOrderDetail order = Value(await Controller.FindOrderAsync(orderId));
        Value(await Controller.SubmitPreparationAsync(new(Guid.NewGuid(), orderId, order.Order.OrderVersion, Now)));
        order = Value(await Controller.FindOrderAsync(orderId));
        Value(await Controller.SetPreparationStatusAsync(new(Guid.NewGuid(), orderId, order.Order.OrderVersion, Now), CafeOrderStates.Preparing));
        order = Value(await Controller.FindOrderAsync(orderId));
        Value(await Controller.SetPreparationStatusAsync(new(Guid.NewGuid(), orderId, order.Order.OrderVersion, Now), CafeOrderStates.Ready));
        return Value(await Controller.FindOrderAsync(orderId));
    }

    public object Scalar(string sql)
    {
        using var connection = Runtime.ConnectionFactory.CreateConnection(); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    public void Execute(string sql)
    {
        using var connection = Runtime.ConnectionFactory.CreateConnection(); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static T Value<T>(Result<T> result) => result.IsSuccess ? result.Value
        : throw new InvalidOperationException(result.ErrorDetail.Code + ": " + result.ErrorDetail.Message);

    public void Dispose()
    {
        SqliteConnectionFactory.ClearAllPools();
        // Only this fixture-created, exact disposable root is eligible for cleanup.
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}

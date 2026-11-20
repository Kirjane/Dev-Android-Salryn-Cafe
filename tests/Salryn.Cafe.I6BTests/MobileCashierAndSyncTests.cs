using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Printing;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Foundation.Sync;
using Salryn.Mobile.Runtime;
using Salryn.Sync.Central.Server;
using Xunit;

namespace Salryn.Cafe.I6BTests;

internal sealed class InMemorySecretStore : IDeviceSecretStore
{
    private readonly ConcurrentDictionary<string, byte[]> _map = new(StringComparer.Ordinal);
    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_map.TryGetValue(key, out byte[]? v) ? v : null);
    public Task WriteAsync(string key, byte[] secret, CancellationToken cancellationToken = default)
    {
        _map[key] = secret.ToArray();
        return Task.CompletedTask;
    }
}

public sealed class MobileCashierAndSyncTests
{
    private static (MobileSignedOfflineLease Lease, string ServerPubSpki) CreateSignedLease(
        Guid deviceId, Guid tenantId, Guid storeId, long epoch, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        (string privPem, string pubSpki) = CentralCrypto.CreateEcdsaP256KeyPair();
        string payload = CentralCrypto.BuildLeaseSigningPayload(deviceId, tenantId, storeId, "Salryn.Cafe", epoch, issuedAt, expiresAt);
        string sig = CentralCrypto.SignEcdsaP256WithPem(privPem, payload);
        return (new MobileSignedOfflineLease(deviceId, tenantId, storeId, "Salryn.Cafe", epoch, issuedAt, expiresAt, sig, pubSpki), pubSpki);
    }

    private static string TempDbPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "salryn-cafe-i6b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "mobile-cafe.db");
    }

    [Fact]
    public async Task OfflineCashierFlows_CashArSplitOnly_IdempotentOperationId_AuditedRefund_BlindClose_AndSimulatedEscPos()
    {
        string dbPath = TempDbPath();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid deviceId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid storeId = Guid.CreateVersion7();

        CafeMobileDatabase db = new(dbPath);
        db.Initialize();

        InMemorySecretStore secrets = new();
        (MobileSignedOfflineLease lease, string serverKey) = CreateSignedLease(deviceId, tenantId, storeId, 1, now.AddMinutes(-5), now.AddHours(24));
        MobileOfflineLeaseGuard leaseGuard = new(secrets, serverKey);
        SimulatedEscPosPrinterTransport printer = new();
        MobileCashierEngine engine = new(db, leaseGuard, printer, utcNow: () => now, clockSource: () => (1000L, 1));

        engine.ConfigureDeviceBinding(deviceId, tenantId, storeId, 1, lease);
        engine.ConfigurePrinterStation("Receipt", "AA:BB:CC:11:22:01", "Salryn Receipt BT-58");
        engine.ConfigurePrinterStation("Kitchen", "AA:BB:CC:11:22:02", "Salryn Kitchen BT-80");

        // Operator PIN RBAC
        Guid cashierId = Guid.CreateVersion7();
        Guid managerId = Guid.CreateVersion7();
        engine.SeedOperator(cashierId, "cashier1", "Ana Cashier", "Cashier", "1234");
        engine.SeedOperator(managerId, "manager1", "Boss Manager", "Manager", "9999");

        Assert.Null(engine.AuthenticateOperator("cashier1", "0000"));
        MobileOperatorPrincipal cashier = engine.AuthenticateOperator("cashier1", "1234")!;
        MobileOperatorPrincipal manager = engine.AuthenticateOperator("manager1", "9999")!;

        // Open shift
        var shift = await engine.OpenShiftAsync(cashier, openingCashCents: 50000);
        Assert.True(shift.IsSuccess);

        // Create Dine-In order (queues Kitchen Ticket on Kitchen BT station)
        MobileOrderResult order = await engine.CreateOrderAsync(
            cashier,
            "DineIn",
            [new MobileOrderLineInput(Guid.CreateVersion7(), "Iced Spanish Latte", 2, 14500, ["Oat Milk", "Less Ice"])],
            discountCents: 1000,
            notes: "Table 4");
        Assert.True(order.IsSuccess);
        Assert.Equal(28000, order.TotalCents);
        Assert.NotNull(order.KitchenPrintJobId);

        // Reject unsupported tender (Card / GCash forbidden!)
        MobileCheckoutResult badTender = await engine.CompleteCheckoutAsync(
            cashier,
            new MobileCheckoutRequest(Guid.CreateVersion7(), shift.ShiftId, order.OrderId, "Card", 0, 0));
        Assert.False(badTender.IsSuccess);
        Assert.Equal("ERR_UNSUPPORTED_TENDER", badTender.ErrorCode);

        // Partial Cash + AR checkout with durable operationId
        Guid checkoutOpId = Guid.CreateVersion7();
        Guid customerId = Guid.CreateVersion7();
        MobileCheckoutRequest splitReq = new(checkoutOpId, shift.ShiftId, order.OrderId, MobileCashierEngine.TenderCashAndAr, CashCents: 18000, ArCents: 10000, CustomerId: customerId);
        MobileCheckoutResult sale = await engine.CompleteCheckoutAsync(cashier, splitReq);
        Assert.True(sale.IsSuccess);
        Assert.False(sale.IsIdempotentReplay);
        Assert.Equal(28000, sale.TotalCents);
        Assert.NotNull(sale.ReceiptPrintJobId);

        // Duplicate operationId replays cleanly without duplicate outbox
        MobileCheckoutResult replay = await engine.CompleteCheckoutAsync(cashier, splitReq);
        Assert.True(replay.IsSuccess);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(sale.SaleId, replay.SaleId);
        Assert.Equal(1, engine.GetPendingOutboxCount());

        // Flush simulated ESC/POS queue (Kitchen + Receipt on separate stations!)
        int printedCount = await engine.FlushPrintQueueAsync();
        Assert.Equal(2, printedCount);
        Assert.Equal(2, printer.CapturedPrints.Count);
        Assert.Contains(printer.CapturedPrints, p => p.TargetId == "AA:BB:CC:11:22:02" && p.AsciiPreview.Contains("KITCHEN TICKET") && p.AsciiPreview.Contains("Oat Milk"));
        Assert.Contains(printer.CapturedPrints, p => p.TargetId == "AA:BB:CC:11:22:01" && p.AsciiPreview.Contains("SALRYN CAFE") && p.AsciiPreview.Contains("CashAndAR"));

        // Audited Receipt Reprint includes *** REPRINT #1 *** banner
        engine.QueueAuditedReceiptReprint(cashier, sale.SaleId, "Customer requested extra copy");
        Assert.Equal(1, await engine.FlushPrintQueueAsync());
        Assert.Contains("*** REPRINT #1 ***", printer.CapturedPrints.Last().AsciiPreview);

        // Cashier CANNOT issue refund (RBAC), Manager CAN
        MobileRefundResult deniedRefund = await engine.IssueAuditedRefundAsync(cashier, Guid.CreateVersion7(), sale.SaleId, "Spilled");
        Assert.False(deniedRefund.IsSuccess);
        Assert.Equal("ERR_FORBIDDEN_ROLE", deniedRefund.ErrorCode);

        MobileRefundResult okRefund = await engine.IssueAuditedRefundAsync(manager, Guid.CreateVersion7(), sale.SaleId, "Spilled drink");
        Assert.True(okRefund.IsSuccess);
        Assert.Equal(2, engine.GetPendingOutboxCount());

        // Blind Shift Close (opening 50000 + 0 completed cash sales since refunded -> expected 50000; counted 49500 -> variance -500)
        MobileBlindShiftCloseResult closed = await engine.CloseShiftBlindAsync(cashier, Guid.CreateVersion7(), shift.ShiftId, countedCashCents: 49500, note: "Short PHP 5");
        Assert.True(closed.IsSuccess);
        Assert.Equal(-500, closed.VarianceCents);
        Assert.Equal(3, engine.GetPendingOutboxCount());

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task OfflineLeaseGuard_DetectsExpiry_AndClockRollbackAcrossReboots()
    {
        string dbPath = TempDbPath();
        DateTimeOffset t0 = DateTimeOffset.UtcNow;
        long elapsed = 5000;
        int bootCount = 1;

        Guid deviceId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid storeId = Guid.CreateVersion7();

        CafeMobileDatabase db = new(dbPath);
        db.Initialize();
        InMemorySecretStore secrets = new();
        (MobileSignedOfflineLease lease, string serverKey) = CreateSignedLease(deviceId, tenantId, storeId, 1, t0.AddMinutes(-1), t0.AddHours(1));
        MobileOfflineLeaseGuard guard = new(secrets, serverKey);
        SimulatedEscPosPrinterTransport printer = new();
        MobileCashierEngine engine = new(db, guard, printer, utcNow: () => t0, clockSource: () => (elapsed, bootCount));

        engine.ConfigureDeviceBinding(deviceId, tenantId, storeId, 1, lease);
        engine.SeedOperator(Guid.CreateVersion7(), "c1", "Cashier", "Cashier", "1234");
        MobileOperatorPrincipal cashier = engine.AuthenticateOperator("c1", "1234")!;

        // Valid at t0 -> advances high-water mark
        Assert.True((await engine.OpenShiftAsync(cashier, 1000)).IsSuccess);

        // Reboot (bootCount=2, elapsed=100) + roll back wall clock by 10 minutes -> LOCKED!
        t0 = t0.AddMinutes(-10);
        bootCount = 2;
        elapsed = 100;
        var rolledBack = await engine.OpenShiftAsync(cashier, 1000);
        Assert.False(rolledBack.IsSuccess);
        Assert.Equal("LEASE_CLOCK_TAMPER_LOCKED", rolledBack.ErrorCode);

        // Advance past lease expiry -> LEASE_EXPIRED!
        t0 = t0.AddHours(3);
        var expired = await engine.OpenShiftAsync(cashier, 1000);
        Assert.False(expired.IsSuccess);
        Assert.Equal("LEASE_EXPIRED", expired.ErrorCode);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task LiveE2E_MobileHttpSyncOutboxTransport_WithCentralServer_SyncsAndFencesRevokedDevice()
    {
        string centralDbPath = TempDbPath();
        string mobileDbPath = TempDbPath();
        Guid tenantId = Guid.CreateVersion7();
        Guid storeId = Guid.CreateVersion7();

        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CentralServerOptions>();
                services.RemoveAll<CentralSyncDatabase>();
                services.RemoveAll<CentralSyncService>();
                services.AddSingleton(new CentralServerOptions
                {
                    DatabasePath = centralDbPath,
                    RequireHttps = true,
                    TrustForwardedProto = true,
                    BootstrapAdmin = new BootstrapAdminOptions
                    {
                        TenantId = tenantId,
                        StoreId = storeId,
                        Username = "owner@mobile.test",
                        DisplayName = "Owner",
                        Role = "Owner",
                        Password = "Strong-Password!2026"
                    }
                });
                services.AddSingleton<CentralSyncDatabase>();
                services.AddSingleton<CentralSyncService>();
            });
        });

        using HttpClient adminHttp = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://central.salryn-cafe.test") });
        adminHttp.DefaultRequestHeaders.Add("X-Salryn-Product-Id", "Salryn.Cafe");

        // Admin login + invite
        AdminLoginResponse login = (await (await adminHttp.PostAsJsonAsync("/api/sync/v1/admin/auth/login", new AdminLoginRequest("owner@mobile.test", "Strong-Password!2026"))).Content.ReadFromJsonAsync<AdminLoginResponse>())!;
        using HttpRequestMessage invReq = new(HttpMethod.Post, "/api/sync/v1/admin/devices/invites");
        invReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        invReq.Content = JsonContent.Create(new CreateInviteRequest("OfflineAndroidCashier"));
        CreateInviteResponse invite = (await (await adminHttp.SendAsync(invReq)).Content.ReadFromJsonAsync<CreateInviteResponse>())!;

        // Device enroll + approve
        using ECDsa deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pubSpki = Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo());
        EnrollDeviceResponse enrolled = (await (await adminHttp.PostAsJsonAsync("/api/sync/v1/devices/enroll",
            new EnrollDeviceRequest(invite.JoinCode, tenantId, storeId, "Salryn.Cafe", "Android-Tab-1", pubSpki))).Content.ReadFromJsonAsync<EnrollDeviceResponse>())!;

        using HttpRequestMessage appReq = new(HttpMethod.Post, $"/api/sync/v1/admin/devices/{enrolled.DeviceId:D}/approve");
        appReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        (await adminHttp.SendAsync(appReq)).EnsureSuccessStatusCode();

        // Mobile HttpSyncOutboxTransport authenticates via ECDSA P-256 challenge-response
        string? activeToken = null;
        long activeEpoch = 1;
        using HttpClient mobileHttp = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://central.salryn-cafe.test") });
        HttpSyncOutboxTransport transport = new(
            mobileHttp,
            () => activeToken,
            () => (enrolled.DeviceId, tenantId, storeId, activeEpoch));

        MobileAuthTokenOutcome auth = await transport.AuthenticateWithChallengeAsync(enrolled.DeviceId, tenantId, storeId, deviceKey);
        Assert.True(auth.IsSuccess);
        activeToken = auth.AccessToken;
        activeEpoch = auth.ServerEpoch;

        // Configure MobileCashierEngine with Central's signed offline lease
        CafeMobileDatabase mobileDb = new(mobileDbPath);
        mobileDb.Initialize();
        InMemorySecretStore secrets = new();
        SimulatedEscPosPrinterTransport printer = new();
        // Provision trust from the synthetic server fixture, independently of token/lease data.
        CentralSyncDatabase centralDb = factory.Services.GetRequiredService<CentralSyncDatabase>();
        using SqliteConnection centralConnection = centralDb.OpenConnection();
        string trustedServerKey = centralDb.ReadServerState(centralConnection).LeaseSigningPublicKeySpki;
        MobileCashierEngine engine = new(mobileDb, new MobileOfflineLeaseGuard(secrets, trustedServerKey), printer,
            clockSource: () => (Environment.TickCount64, 1));
        engine.ConfigureDeviceBinding(enrolled.DeviceId, tenantId, storeId, activeEpoch, auth.OfflineLease);
        engine.SeedOperator(Guid.CreateVersion7(), "cashier", "Ana", "Cashier", "1234");
        MobileOperatorPrincipal cashier = engine.AuthenticateOperator("cashier", "1234")!;

        // Perform offline cash sale & sync to Central
        var shift = await engine.OpenShiftAsync(cashier, 10000);
        var ord1 = await engine.CreateOrderAsync(cashier, "Takeout", [new MobileOrderLineInput(Guid.CreateVersion7(), "Americano", 1, 11000)]);
        var sale1 = await engine.CompleteCheckoutAsync(cashier, new MobileCheckoutRequest(Guid.CreateVersion7(), shift.ShiftId, ord1.OrderId, "Cash", 11000, 0));
        Assert.True(sale1.IsSuccess);
        Assert.Equal(1, engine.GetPendingOutboxCount());

        MobileSyncCycleResult sync1 = await engine.SyncNowAsync(transport);
        Assert.True(sync1.IsSuccess);
        Assert.Equal(1, sync1.PushedCount);
        Assert.Equal(0, engine.GetPendingOutboxCount());

        // Perform a second offline sale, then Admin REVOKES the tablet -> Sync fences outbox & locks tablet!
        var ord2 = await engine.CreateOrderAsync(cashier, "Takeout", [new MobileOrderLineInput(Guid.CreateVersion7(), "Latte", 1, 14500)]);
        var sale2 = await engine.CompleteCheckoutAsync(cashier, new MobileCheckoutRequest(Guid.CreateVersion7(), shift.ShiftId, ord2.OrderId, "Cash", 14500, 0));
        Assert.True(sale2.IsSuccess);

        using HttpRequestMessage revReq = new(HttpMethod.Post, $"/api/sync/v1/admin/devices/{enrolled.DeviceId:D}/revoke");
        revReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        revReq.Content = JsonContent.Create(new RevokeDeviceRequest("Lost tablet"));
        (await adminHttp.SendAsync(revReq)).EnsureSuccessStatusCode();

        MobileSyncCycleResult fencedSync = await engine.SyncNowAsync(transport);
        Assert.False(fencedSync.IsSuccess);
        Assert.True(fencedSync.IsRevokedFenced);
        Assert.Equal(1, fencedSync.QuarantinedCount);
        Assert.Equal("DEVICE_REVOKED_FENCED", fencedSync.ErrorCode);

        // Local cashier is now LOCKED from further offline mutations!
        var blockedOrder = await engine.CreateOrderAsync(cashier, "Takeout", [new MobileOrderLineInput(Guid.CreateVersion7(), "Latte", 1, 14500)]);
        Assert.False(blockedOrder.IsSuccess);
        Assert.Equal("DEVICE_REVOKED_FENCED", blockedOrder.ErrorCode);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void ArchitectureBoundary_MobileAssembliesHaveZeroDesktopOrAspNetReferences()
    {
        string[] forbiddenPrefixes = ["Microsoft.AspNetCore", "Salryn.Api.Local", "System.Windows.Forms", "PdfSharp", "MigraDoc", "Microsoft.Web.WebView2"];

        foreach (var asm in new[] { typeof(MobileCashierEngine).Assembly, typeof(HttpSyncOutboxTransport).Assembly })
        {
            var refs = asm.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();
            foreach (string forbidden in forbiddenPrefixes)
            {
                Assert.DoesNotContain(refs, r => r.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}

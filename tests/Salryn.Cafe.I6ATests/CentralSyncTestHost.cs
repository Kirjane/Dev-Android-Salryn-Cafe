using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Salryn.Sync.Central.Server;

namespace Salryn.Cafe.I6ATests;

internal sealed class CentralSyncTestHost : IDisposable
{
    public string TempDirectory { get; }
    public string DatabasePath { get; }
    public Guid TenantId { get; } = Guid.CreateVersion7();
    public Guid StoreId { get; } = Guid.CreateVersion7();
    public string AdminUsername { get; } = "owner@cafeyza.local";
    public string AdminPassword { get; } = "CafeYza-Owner-Test!2026";
    public WebApplicationFactory<Program> Factory { get; }

    public CentralSyncTestHost(string? existingDbPath = null, bool canonical = false, Guid? canonicalStore = null)
    {
        if (canonicalStore.HasValue) StoreId=canonicalStore.Value;
        TempDirectory = Path.Combine(Path.GetTempPath(), "salryn-cafe-i6a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        DatabasePath = existingDbPath ?? Path.Combine(TempDirectory, "central-sync.db");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CentralServerOptions>();
                services.RemoveAll<CentralSyncDatabase>();
                services.RemoveAll<CentralSyncService>();

                CentralServerOptions options = new()
                {
                    DatabasePath = DatabasePath,
                    EnableCanonicalDomainProcessing = canonical,
                    CanonicalTenantId = canonical ? TenantId : Guid.Empty,
                    CanonicalStoreId = canonical ? StoreId : Guid.Empty,
                    RequireHttps = true,
                    TrustForwardedProto = true,
                    OfflineLeaseHours = 24,
                    BootstrapAdmin = new BootstrapAdminOptions
                    {
                        TenantId = TenantId,
                        StoreId = StoreId,
                        Username = AdminUsername,
                        DisplayName = "Cafe Yza Owner",
                        Role = "Owner",
                        Password = AdminPassword
                    }
                };

                services.AddSingleton(options);
                services.AddSingleton<CentralSyncDatabase>();
                services.AddSingleton<CentralSyncService>();
            });
        });
    }

    public HttpClient CreateHttpsClient(string productId = CentralServerOptions.CanonicalProductId)
    {
        HttpClient client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://central.salryn-cafe.test")
        });
        client.DefaultRequestHeaders.Add(CentralSyncEndpoints.ProductIdHeader, productId);
        return client;
    }

    public HttpClient CreatePlainHttpClient()
    {
        HttpClient client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://central.salryn-cafe.test")
        });
        client.DefaultRequestHeaders.Add(CentralSyncEndpoints.ProductIdHeader, CentralServerOptions.CanonicalProductId);
        return client;
    }

    public async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        HttpResponseMessage resp = await client.PostAsJsonAsync(
            "/api/sync/v1/admin/auth/login",
            new AdminLoginRequest(AdminUsername, AdminPassword));
        resp.EnsureSuccessStatusCode();
        AdminLoginResponse? body = await resp.Content.ReadFromJsonAsync<AdminLoginResponse>();
        return body!.AccessToken;
    }

    public async Task<EnrolledDeviceClient> EnrollApproveAndAuthenticateDeviceAsync(
        HttpClient adminClient,
        string adminToken,
        string deviceName,
        string deviceRole = "OfflineAndroidCashier")
    {
        using HttpRequestMessage inviteReq = new(HttpMethod.Post, "/api/sync/v1/admin/devices/invites");
        inviteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        inviteReq.Content = JsonContent.Create(new CreateInviteRequest(deviceRole));
        HttpResponseMessage inviteResp = await adminClient.SendAsync(inviteReq);
        inviteResp.EnsureSuccessStatusCode();
        CreateInviteResponse invite = (await inviteResp.Content.ReadFromJsonAsync<CreateInviteResponse>())!;

        ECDsa deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pubSpki = Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo());

        HttpResponseMessage enrollResp = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/devices/enroll",
            new EnrollDeviceRequest(
                invite.JoinCode,
                TenantId,
                StoreId,
                CentralServerOptions.CanonicalProductId,
                deviceName,
                pubSpki));
        enrollResp.EnsureSuccessStatusCode();
        EnrollDeviceResponse enrolled = (await enrollResp.Content.ReadFromJsonAsync<EnrollDeviceResponse>())!;

        using HttpRequestMessage approveReq = new(HttpMethod.Post, $"/api/sync/v1/admin/devices/{enrolled.DeviceId:D}/approve");
        approveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        HttpResponseMessage approveResp = await adminClient.SendAsync(approveReq);
        approveResp.EnsureSuccessStatusCode();

        // Challenge-response token exchange
        HttpResponseMessage chResp = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/devices/challenge",
            new DeviceChallengeRequest(enrolled.DeviceId, TenantId, StoreId, CentralServerOptions.CanonicalProductId));
        chResp.EnsureSuccessStatusCode();
        DeviceChallengeResponse challenge = (await chResp.Content.ReadFromJsonAsync<DeviceChallengeResponse>())!;

        string signingPayload = CentralCrypto.BuildChallengeSigningPayload(
            challenge.ChallengeId,
            challenge.NonceBase64,
            enrolled.DeviceId,
            CentralServerOptions.CanonicalProductId,
            TenantId,
            StoreId);
        string sigBase64 = Convert.ToBase64String(
            deviceKey.SignData(Encoding.UTF8.GetBytes(signingPayload), HashAlgorithmName.SHA256));

        HttpResponseMessage tokResp = await adminClient.PostAsJsonAsync(
            "/api/sync/v1/devices/token",
            new DeviceTokenRequest(
                challenge.ChallengeId,
                enrolled.DeviceId,
                TenantId,
                StoreId,
                CentralServerOptions.CanonicalProductId,
                sigBase64));
        tokResp.EnsureSuccessStatusCode();
        DeviceTokenResponse token = (await tokResp.Content.ReadFromJsonAsync<DeviceTokenResponse>())!;

        HttpClient deviceHttp = CreateHttpsClient();
        deviceHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        deviceHttp.DefaultRequestHeaders.Add(CentralSyncEndpoints.DeviceIdHeader, enrolled.DeviceId.ToString("D"));
        deviceHttp.DefaultRequestHeaders.Add(CentralSyncEndpoints.TenantIdHeader, TenantId.ToString("D"));
        deviceHttp.DefaultRequestHeaders.Add(CentralSyncEndpoints.StoreIdHeader, StoreId.ToString("D"));

        return new EnrolledDeviceClient(enrolled.DeviceId, deviceKey, token, deviceHttp, invite.JoinCode, challenge);
    }

    public void Dispose()
    {
        Factory.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup race on Windows
        }
    }
}

internal sealed record EnrolledDeviceClient(
    Guid DeviceId,
    ECDsa PrivateKey,
    DeviceTokenResponse TokenResponse,
    HttpClient Http,
    string UsedJoinCode,
    DeviceChallengeResponse UsedChallenge) : IDisposable
{
    public void Dispose()
    {
        Http.Dispose();
        PrivateKey.Dispose();
    }
}

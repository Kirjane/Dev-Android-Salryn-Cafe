using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Salryn.Core.Models;
using Salryn.Mobile.Foundation.Cashier;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Foundation.Sync;
using Salryn.Mobile.Runtime;
using Salryn.Sync.Contracts;

namespace Salryn.App.Android.Platform;

public sealed record AndroidCentralConfiguration(string HttpsUrl, Guid TenantId, Guid StoreId,
    string PinnedServerKey, Guid? DeviceId = null);

public sealed record AndroidDeviceStatusSummary(Guid? DeviceId, DateTimeOffset? LeaseExpiresAtUtc,
    bool IsRevokedFenced, int PendingOutboxCount);

/// <summary>Device setup is independent of operator authentication. TLS validation is never disabled.
/// Keys and approved configuration persist in Android Keystore protected storage; access tokens stay in memory.</summary>
public sealed class AndroidCentralConnection(IDeviceSecretStore secrets, Func<MobileClockSnapshot> clock)
{
    private const string ConfigurationKey = "salryn.cafe.central.configuration.v1";
    private const string SigningKey = "salryn.cafe.central.signing.pkcs8.v1";

    public async Task<AndroidCentralConfiguration?> ReadConfigurationAsync()
    {
        byte[]? bytes = await secrets.ReadAsync(ConfigurationKey);
        return bytes is null ? null : JsonSerializer.Deserialize<AndroidCentralConfiguration>(bytes);
    }

    public async Task<AndroidDeviceStatusSummary> ReadDeviceStatusAsync(MobileProductionComposition runtime)
    {
        AndroidCentralConfiguration? config = await ReadConfigurationAsync();
        MobileEnrollmentBinding? binding = await new ProtectedMobileEnrollmentReader(secrets).ReadAsync();
        bool revokedFenced = false;
        try
        {
            byte[]? wmBytes = await secrets.ReadAsync("salryn.cafe.offline_lease_watermark.v1");
            if (wmBytes is { Length: > 0 })
            {
                AntiRollbackWatermark? wm = JsonSerializer.Deserialize<AntiRollbackWatermark>(wmBytes);
                revokedFenced = wm?.IsRevokedFenced == true;
            }
        }
        catch { }
        int pendingCount = config?.DeviceId is { } deviceId && deviceId != Guid.Empty
            ? runtime.SyncOutbox.ReadPending(deviceId).Count
            : 0;
        return new AndroidDeviceStatusSummary(config?.DeviceId, binding?.Lease?.ExpiresAtUtc, revokedFenced, pendingCount);
    }

    public async Task<Guid> EnrollAsync(AndroidCentralConfiguration requested, string joinCode, string deviceName)
    {
        Validate(requested);
        AndroidCentralConfiguration? existing = await ReadConfigurationAsync();
        if (existing is not null && (existing.HttpsUrl != requested.HttpsUrl || existing.TenantId != requested.TenantId ||
            existing.StoreId != requested.StoreId || existing.PinnedServerKey != requested.PinnedServerKey))
            throw new InvalidOperationException("Existing device authority is preserved. Ask the administrator to reconcile setup.");
        if (existing?.DeviceId is { } existingDevice) return existingDevice;
        using ECDsa signing = await ReadOrCreateSigningKeyAsync();
        // Save key and authority before the one-time join request. A lost response must be reconciled by the administrator.
        await SaveAsync(requested);
        using HttpClient http = CreateClient(requested);
        using HttpResponseMessage response = await http.PostAsJsonAsync("/api/sync/v1/devices/enroll", new
        {
            joinCode, requested.TenantId, requested.StoreId, productId = "Salryn.Cafe", deviceName,
            publicKeySpkiBase64 = Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo())
        });
        await RequireSuccessAsync(response);
        EnrollResponse enrollment = (await response.Content.ReadFromJsonAsync<EnrollResponse>())
            ?? throw new InvalidOperationException("Enrollment response was empty; retain setup for administrator reconciliation.");
        if (enrollment.DeviceId == Guid.Empty) throw new InvalidOperationException("Enrollment returned no device identity.");
        await SaveAsync(requested with { DeviceId = enrollment.DeviceId });
        return enrollment.DeviceId;
    }

    public async Task<string> BootstrapAsync(MobileProductionComposition runtime)
    {
        AndroidCentralConfiguration config = await RequiredConfigurationAsync();
        using HttpClient http = CreateClient(config);
        string bearer = await AuthenticateAsync(config, http);
        const string bundleKey="salryn.cafe.bootstrap.pending_bundle.v1";
        BootstrapBundle? bundle=null;
        byte[]? retained=await secrets.ReadAsync(bundleKey);
        if(retained is not null) bundle=JsonSerializer.Deserialize<BootstrapBundle>(retained)
            ?? throw new InvalidOperationException("Retained bootstrap bundle is corrupt; preserve setup for reconciliation.");
        if(bundle is null)
        {
            var transport = new HttpSyncOutboxTransport(http, () => bearer,
            () => (config.DeviceId!.Value, config.TenantId, config.StoreId, 1));
        MobileBootstrapOutcome result = await transport.BootstrapAsync();
        if (IsRevoked(result.ErrorCode)) await FenceAsync(config);
        if (!result.IsSuccess || result.Snapshot is null) throw new InvalidOperationException(result.ErrorMessage ?? "Bootstrap failed.");
        if (result.Snapshot.StoreId != config.StoreId) throw new InvalidOperationException("Bootstrap store does not match approved device scope.");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        http.DefaultRequestHeaders.Add("X-Salryn-Tenant-Id", config.TenantId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Salryn-Store-Id", config.StoreId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Salryn-Device-Id", config.DeviceId!.Value.ToString("D"));
        using HttpResponseMessage ingredientResponse = await http.GetAsync(
            $"/api/sync/v1/cafe/ingredients/bootstrap?storeId={config.StoreId:D}&centralInstanceId={result.Snapshot.CentralInstanceId:D}&baselineServerSequence={result.Snapshot.BaselineServerSequence}");
        await RequireSuccessAsync(ingredientResponse, config);
        CafeIngredientRecipeBootstrapV1 ingredients = (await ingredientResponse.Content.ReadFromJsonAsync<CafeIngredientRecipeBootstrapV1>())
            ?? throw new InvalidOperationException("Central ingredient bootstrap was empty.");
        using HttpResponseMessage menuResponse = await http.GetAsync($"/api/sync/v1/cafe/menu/publication?storeId={config.StoreId:D}");
        await RequireSuccessAsync(menuResponse, config);
        MenuPublicationSnapshot menu = (await menuResponse.Content.ReadFromJsonAsync<MenuPublicationSnapshot>())
            ?? throw new InvalidOperationException("Central menu publication was empty.");
        if (menu.StoreId != config.StoreId) throw new InvalidOperationException("Menu publication store does not match approved scope.");
        using HttpResponseMessage recipeResponse = await http.GetAsync($"/api/sync/v1/cafe/recipes/publication?storeId={config.StoreId:D}");
        await RequireSuccessAsync(recipeResponse, config);
        RecipeBookPublicationSnapshot recipes = (await recipeResponse.Content.ReadFromJsonAsync<RecipeBookPublicationSnapshot>())
            ?? throw new InvalidOperationException("Central recipe publication was empty.");
        if (recipes.StoreId != config.StoreId) throw new InvalidOperationException("Recipe publication store does not match approved scope.");
            var approved=await new MobileCashierDeviceGuard(new ProtectedMobileEnrollmentReader(secrets),secrets,clock).AuthorizeAsync();
            if(approved.IsFailure) throw new InvalidOperationException(approved.ErrorDetail.Message);
            bundle=new(config.DeviceId!.Value,approved.Value.ServerEpoch,result.Snapshot,ingredients,menu,recipes);
            await secrets.WriteAsync(bundleKey,JsonSerializer.SerializeToUtf8Bytes(bundle));
        }
        var binding=await new MobileCashierDeviceGuard(new ProtectedMobileEnrollmentReader(secrets),secrets,clock).AuthorizeAsync();
        if(binding.IsFailure || bundle.DeviceId!=config.DeviceId || bundle.ServerEpoch!=binding.Value.ServerEpoch ||
            bundle.Base.StoreId!=config.StoreId || bundle.Ingredients.StoreId!=config.StoreId ||
            bundle.Ingredients.CentralInstanceId!=bundle.Base.CentralInstanceId ||
            bundle.Ingredients.BaselineServerSequence!=bundle.Base.BaselineServerSequence)
            throw new InvalidOperationException("Retained bootstrap bundle does not match current approved authority; preserve it for reconciliation.");
        var applied=runtime.SyncBootstrap.Apply(bundle.Base);
        if(!applied.IsSuccess) throw new InvalidOperationException(applied.ErrorMessage??"Base bootstrap refused; existing data preserved.");
        var ingredientsApplied=runtime.CafeIngredientBootstrap.Apply(bundle.Ingredients);
        if(ingredientsApplied.IsFailure) throw new InvalidOperationException(ingredientsApplied.ErrorDetail.Message);
        var menuApplied=runtime.MenuCatalog.ApplyPublication(bundle.Menu);
        if(menuApplied.IsFailure) throw new InvalidOperationException(menuApplied.ErrorDetail.Message);
        var recipesApplied=runtime.IngredientRecipes.ApplyPublication(bundle.Recipes);
        if(recipesApplied.IsFailure) throw new InvalidOperationException(recipesApplied.ErrorDetail.Message);
        // Keep the exact bundle for interruption-safe idempotent setup retry. It is never silently replaced by a newer baseline.
        return "Base data, published Cafe menu and recipes persisted. Enroll the operator through Central login before offline sign in.";
    }

    public async Task<string> EnrollOperatorAsync(MobileProductionComposition runtime, string username, string password)
    {
        AndroidCentralConfiguration config = await RequiredConfigurationAsync();
        using HttpClient http = CreateClient(config);
        string bearer = await AuthenticateAsync(config, http);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        http.DefaultRequestHeaders.Add("X-Salryn-Tenant-Id", config.TenantId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Salryn-Store-Id", config.StoreId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Salryn-Device-Id", config.DeviceId!.Value.ToString("D"));
        using HttpResponseMessage response = await http.PostAsJsonAsync("/api/sync/v1/operators/login", new { username, password });
        await RequireSuccessAsync(response, config);
        OperatorResponse login = (await response.Content.ReadFromJsonAsync<OperatorResponse>())
            ?? throw new InvalidOperationException("Operator response was empty.");
        try
        {
            await StoreVerifiedOperatorAsync(runtime, config, login, username, password);
            return "Operator enrolled for offline sign in on this approved device. Return to operator sign in.";
        }
        finally
        {
            using HttpRequestMessage logout = new(HttpMethod.Post, "/api/sync/v1/operators/logout");
            logout.Headers.Add("X-Salryn-Operator-Token", login.OperatorToken);
            using HttpResponseMessage logoutResponse = await http.SendAsync(logout);
            if (!logoutResponse.IsSuccessStatusCode)
            {
                ErrorResponse? error = await TryReadErrorAsync(logoutResponse);
                if (IsRevoked(error?.ErrorCode)) await FenceAsync(config);
            }
        }
    }

    public async Task<string> ReconnectAsync(MobileProductionComposition runtime, string username, string password, Guid expectedActor)
    {
        AndroidCentralConfiguration config = await RequiredConfigurationAsync();
        using HttpClient http = CreateClient(config);
        string bearer = await AuthenticateAsync(config, http);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        http.DefaultRequestHeaders.Add("X-Salryn-Tenant-Id", config.TenantId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Salryn-Store-Id", config.StoreId.ToString("D"));
        http.DefaultRequestHeaders.Add("X-Salryn-Device-Id", config.DeviceId!.Value.ToString("D"));
        using HttpResponseMessage loginResponse = await http.PostAsJsonAsync("/api/sync/v1/operators/login", new { username, password });
        await RequireSuccessAsync(loginResponse, config);
        OperatorResponse login = (await loginResponse.Content.ReadFromJsonAsync<OperatorResponse>())
            ?? throw new InvalidOperationException("Operator response was empty.");
        try
        {
            if (login.UserId != expectedActor) throw new InvalidOperationException("Central operator does not match the signed-in local actor.");
            await StoreVerifiedOperatorAsync(runtime, config, login, username, password);
            var transport = new HttpSyncOutboxTransport(http, () => bearer,
                () => (config.DeviceId.Value, config.TenantId, config.StoreId, login.ServerEpoch),
                operatorTokenProvider: () => login.OperatorToken);
            var pending = runtime.SyncOutbox.ReadPending(config.DeviceId.Value)
                .Where(x => x.Envelope.ActorUserId == expectedActor).Select(x => x.Envelope).ToArray();
            int acknowledged = 0;
            bool unresolved = false;
            string cursorKey = "salryn.cafe.pending_scan." + expectedActor.ToString("N");
            byte[]? cursorBytes = await secrets.ReadAsync(cursorKey);
            long cursor = cursorBytes is { Length: 8 } ? BitConverter.ToInt64(cursorBytes) : 0;
            var scan = pending.Where(x => x.LocalSequence > cursor).ToArray();
            if (scan.Length == 0) scan = pending;
            // Visit later operations even if an earlier immutable operation needs a prerequisite.
            // A protected continuation cursor prevents a long offline queue from starving later batches.
            var window = scan.Take(2000).ToArray();
            foreach (var batch in window.Chunk(100))
            {
                MobilePushOutcome outcome = await transport.PushBatchAsync(batch);
                if (outcome.IsRevokedFenced || IsRevoked(outcome.ErrorCode))
                {
                    await FenceAsync(config);
                    throw new InvalidOperationException(outcome.ErrorMessage ?? "Device revoked; pending evidence retained.");
                }
                foreach (SyncOperationFinalResolutionV1 resolution in outcome.Resolutions.Where(x => x.IsApplied))
                {
                    runtime.SyncOutbox.RecordCentralResult(resolution);
                    acknowledged++;
                }
                unresolved |= !outcome.IsSuccess;
                await secrets.WriteAsync(cursorKey, BitConverter.GetBytes(batch[^1].LocalSequence));
            }
            if (window.Length == scan.Length) await secrets.WriteAsync(cursorKey, BitConverter.GetBytes(0L));
            // Bound each interactive reconnect. Cursors are durable, so another reconnect safely continues a large feed.
            for (int pageNumber = 0; pageNumber < 20; pageNumber++)
            {
                var state = runtime.SyncPull.ReadRemoteState(config.StoreId)
                    ?? throw new InvalidOperationException("Bootstrap is required before receiving Central changes.");
                MobilePullOutcome pulled = await transport.PullPageAsync(state.LastReceivedServerSequence);
                if (pulled.IsRevoked || IsRevoked(pulled.ErrorCode)) await FenceAsync(config);
                if (!pulled.IsSuccess || pulled.Page is null)
                    throw new InvalidOperationException(pulled.ErrorMessage ?? "Central changes were not confirmed.");
                var accepted = runtime.SyncPull.AcceptPage(config.StoreId, pulled.Page);
                if (!accepted.IsSuccess) throw new InvalidOperationException(accepted.ErrorMessage ?? "Central feed was not accepted.");
                while (true)
                {
                    var projection = await runtime.SyncProjection.ProjectNextAsync(config.StoreId, config.DeviceId.Value);
                    if (!projection.IsSuccess) throw new InvalidOperationException(projection.Message ?? "Central change requires reconciliation; retained evidence is preserved.");
                    if (!projection.HasWork) break;
                }
                if (!pulled.Page.HasMore) break;
            }
            int remaining = runtime.SyncOutbox.ReadPending(config.DeviceId.Value).Count;
            return $"Connected and lease renewed. Central applied {acknowledged} operations with durable receipts. Pending operations: {remaining}." +
                (unresolved ? " Unresolved responses were retained for retry/reconciliation." : string.Empty);
        }
        finally
        {
            using HttpRequestMessage logout = new(HttpMethod.Post, "/api/sync/v1/operators/logout");
            logout.Headers.Add("X-Salryn-Operator-Token", login.OperatorToken);
            using HttpResponseMessage logoutResponse = await http.SendAsync(logout);
            if (!logoutResponse.IsSuccessStatusCode)
            {
                ErrorResponse? error = await TryReadErrorAsync(logoutResponse);
                if (IsRevoked(error?.ErrorCode)) await FenceAsync(config);
            }
        }
    }

    private async Task StoreVerifiedOperatorAsync(MobileProductionComposition runtime, AndroidCentralConfiguration config,
        OperatorResponse login, string username, string password)
    {
        var approval = await new MobileCashierDeviceGuard(new ProtectedMobileEnrollmentReader(secrets), secrets, clock).AuthorizeAsync();
        if (approval.IsFailure) throw new InvalidOperationException(approval.ErrorDetail.Message);
        MobileEnrollmentBinding binding = approval.Value;
        var result = runtime.OperatorCredentials.EnrollAfterVerifiedLogin(
            new(binding.DeviceId, binding.TenantId, binding.StoreId, binding.ServerEpoch, true),
            new(login.UserId, config.DeviceId!.Value, config.TenantId, config.StoreId, login.ServerEpoch, login.ExpiresAtUtc, true)
                { RoleId = login.RoleId, Permissions = login.Permissions },
            username, password, clock().UtcNow);
        if (result.IsFailure) throw new InvalidOperationException(result.ErrorDetail.Message);
    }

    private async Task<string> AuthenticateAsync(AndroidCentralConfiguration config, HttpClient http)
    {
        using ECDsa signing = await ReadSigningKeyAsync();
        var transport = new HttpSyncOutboxTransport(http, () => null,
            () => (config.DeviceId!.Value, config.TenantId, config.StoreId, 1));
        MobileAuthTokenOutcome auth = await transport.AuthenticateWithChallengeAsync(
            config.DeviceId!.Value, config.TenantId, config.StoreId, signing);
        if (auth.IsRevoked) await FenceAsync(config);
        if (!auth.IsSuccess || auth.OfflineLease is null || string.IsNullOrWhiteSpace(auth.AccessToken))
            throw new InvalidOperationException(auth.ErrorMessage ?? "Device approval is required before connecting.");
        var binding = new MobileEnrollmentBinding(config.DeviceId.Value, config.TenantId, config.StoreId,
            "Salryn.Cafe", auth.ServerEpoch, config.PinnedServerKey, auth.OfflineLease);
        var stored = await new ProtectedMobileEnrollmentWriter(secrets).WriteVerifiedAsync(binding, config.PinnedServerKey, clock());
        if (stored.IsFailure) throw new InvalidOperationException(stored.ErrorDetail.Message);
        return auth.AccessToken;
    }

    private async Task FenceAsync(AndroidCentralConfiguration config)
    {
        MobileClockSnapshot now = clock();
        await new MobileOfflineLeaseGuard(secrets, config.PinnedServerKey)
            .MarkRevokedFencedAsync(now.UtcNow, now.ElapsedRealtimeMs, now.BootCount);
    }

    private async Task<AndroidCentralConfiguration> RequiredConfigurationAsync()
    {
        var config = await ReadConfigurationAsync() ?? throw new InvalidOperationException("Set up this device first.");
        Validate(config);
        if (config.DeviceId is null || config.DeviceId == Guid.Empty) throw new InvalidOperationException("Complete enrollment and administrator approval first.");
        return config;
    }

    private static void Validate(AndroidCentralConfiguration config)
    {
        if (!Uri.TryCreate(config.HttpsUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Enter the administrator-approved HTTPS origin without a path or credentials.");
        if (config.TenantId == Guid.Empty || config.StoreId == Guid.Empty) throw new InvalidOperationException("Valid tenant and store IDs are required.");
        using ECDsa serverKey = ECDsa.Create();
        byte[] spki = Convert.FromBase64String(config.PinnedServerKey);
        serverKey.ImportSubjectPublicKeyInfo(spki, out int read);
        if (read != spki.Length || serverKey.KeySize != 256)
            throw new InvalidOperationException("The approved Central signing key must be ECDSA P-256 SPKI.");
    }

    private static HttpClient CreateClient(AndroidCentralConfiguration config)
    {
        HttpClient http = new() { BaseAddress = new Uri(config.HttpsUrl), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-Salryn-Product-Id", "Salryn.Cafe");
        return http;
    }
    private Task SaveAsync(AndroidCentralConfiguration config) => secrets.WriteAsync(ConfigurationKey, JsonSerializer.SerializeToUtf8Bytes(config));
    private async Task<ECDsa> ReadOrCreateSigningKeyAsync()
    {
        if (await secrets.ReadAsync(SigningKey) is not null) return await ReadSigningKeyAsync();
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateBytes = key.ExportPkcs8PrivateKey();
        try { await secrets.WriteAsync(SigningKey, privateBytes); return key; }
        catch { key.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(privateBytes); }
    }
    private async Task<ECDsa> ReadSigningKeyAsync()
    {
        byte[] bytes = await secrets.ReadAsync(SigningKey) ?? throw new InvalidOperationException("Device signing key is unavailable; preserve data for administrator recovery.");
        ECDsa key = ECDsa.Create();
        try { key.ImportPkcs8PrivateKey(bytes, out _); return key; }
        catch { key.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private async Task RequireSuccessAsync(HttpResponseMessage response, AndroidCentralConfiguration? config = null)
    {
        if (response.IsSuccessStatusCode) return;
        ErrorResponse? error = await TryReadErrorAsync(response);
        if (config is not null && IsRevoked(error?.ErrorCode)) await FenceAsync(config);
        throw new InvalidOperationException(error?.ErrorMessage ?? $"Central request failed (HTTP {(int)response.StatusCode}).");
    }
    private static async Task<ErrorResponse?> TryReadErrorAsync(HttpResponseMessage response)
    {
        try { return await response.Content.ReadFromJsonAsync<ErrorResponse>(); }
        catch { return null; }
    }
    private static bool IsRevoked(string? code) => code is "DEVICE_REVOKED" or "DEVICE_REVOKED_FENCED";
    private sealed record BootstrapBundle(Guid DeviceId,long ServerEpoch,SyncBootstrapSnapshotV1 Base,
        CafeIngredientRecipeBootstrapV1 Ingredients,MenuPublicationSnapshot Menu,RecipeBookPublicationSnapshot Recipes);
    private sealed record EnrollResponse(Guid DeviceId);
    private sealed record OperatorResponse(string OperatorToken, Guid UserId, long ServerEpoch, DateTimeOffset ExpiresAtUtc,
        Guid RoleId, IReadOnlyList<string>? Permissions);
    private sealed record ErrorResponse(string? ErrorCode, string? ErrorMessage);
}

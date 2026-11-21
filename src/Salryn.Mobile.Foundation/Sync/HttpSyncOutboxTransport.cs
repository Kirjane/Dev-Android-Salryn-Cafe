using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Security;
using Salryn.Sync.Contracts;

namespace Salryn.Mobile.Foundation.Sync;

public sealed record MobilePushOutcome(
    bool IsSuccess,
    bool IsRevokedFenced,
    bool RequiresRebootstrap,
    long? ServerEpoch,
    IReadOnlyList<Guid> AppliedOperationIds,
    IReadOnlyList<Guid> QuarantinedOperationIds,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobilePullOutcome(
    bool IsSuccess,
    bool IsRevoked,
    bool RequiresRebootstrap,
    long? ServerEpoch,
    SyncChangeFeedPageV1? Page,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobileBootstrapOutcome(
    bool IsSuccess,
    long ServerEpoch,
    long HeadSequence,
    SyncBootstrapSnapshotV1? Snapshot,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobileAuthTokenOutcome(
    bool IsSuccess,
    bool IsRevoked,
    string? AccessToken,
    long ServerEpoch,
    MobileSignedOfflineLease? OfflineLease,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Concrete HTTPS transport implementing IP0OutboxTransport and Central Sync v1 push/pull/bootstrap/auth.
/// Strictly enforces HTTPS and X-Salryn-Product-Id: Salryn.Cafe.
/// </summary>
public sealed class HttpSyncOutboxTransport : IP0OutboxTransport
{
    public const string ProductIdHeader = "X-Salryn-Product-Id";
    public const string TenantIdHeader = "X-Salryn-Tenant-Id";
    public const string StoreIdHeader = "X-Salryn-Store-Id";
    public const string DeviceIdHeader = "X-Salryn-Device-Id";
    public const string CanonicalProductId = "Salryn.Cafe";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly Func<Guid, SyncOperationEnvelopeV1?>? _durableEnvelopeProvider;
    private readonly Func<string?> _bearerTokenProvider;
    private readonly Func<(Guid DeviceId, Guid TenantId, Guid StoreId, long KnownEpoch)> _scopeProvider;

    public HttpSyncOutboxTransport(
        HttpClient httpClient,
        Func<string?> bearerTokenProvider,
        Func<(Guid DeviceId, Guid TenantId, Guid StoreId, long KnownEpoch)> scopeProvider,
        Func<Guid, SyncOperationEnvelopeV1?>? durableEnvelopeProvider = null)
    {
        _durableEnvelopeProvider = durableEnvelopeProvider;
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_http.BaseAddress is not null &&
            !string.Equals(_http.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ERR_HTTPS_REQUIRED: HttpSyncOutboxTransport requires an https:// BaseAddress.");
        }

        _bearerTokenProvider = bearerTokenProvider ?? throw new ArgumentNullException(nameof(bearerTokenProvider));
        _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
    }

    public async Task<MobileAuthTokenOutcome> AuthenticateWithChallengeAsync(
        Guid deviceId,
        Guid tenantId,
        Guid storeId,
        ECDsa devicePrivateKey,
        CancellationToken ct = default)
    {
        using HttpRequestMessage chReq = CreateBaseRequest(HttpMethod.Post, "/api/sync/v1/devices/challenge");
        chReq.Content = JsonContent.Create(new
        {
            deviceId,
            tenantId,
            storeId,
            productId = CanonicalProductId
        });

        using HttpResponseMessage chResp = await _http.SendAsync(chReq, ct).ConfigureAwait(false);
        if (!chResp.IsSuccessStatusCode)
        {
            (string code, string msg) = await ReadErrorAsync(chResp, ct).ConfigureAwait(false);
            return new MobileAuthTokenOutcome(false, code == "DEVICE_REVOKED", null, 0, null, code, msg);
        }

        ChallengeDto ch = (await chResp.Content.ReadFromJsonAsync<ChallengeDto>(JsonOpts, ct).ConfigureAwait(false))!;
        string payload = $"{ch.ChallengeId}|{ch.NonceBase64}|{deviceId:D}|{CanonicalProductId}|{tenantId:D}|{storeId:D}";
        string sig = Convert.ToBase64String(devicePrivateKey.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256));

        using HttpRequestMessage tokReq = CreateBaseRequest(HttpMethod.Post, "/api/sync/v1/devices/token");
        tokReq.Content = JsonContent.Create(new
        {
            challengeId = ch.ChallengeId,
            deviceId,
            tenantId,
            storeId,
            productId = CanonicalProductId,
            signatureBase64 = sig
        });

        using HttpResponseMessage tokResp = await _http.SendAsync(tokReq, ct).ConfigureAwait(false);
        if (!tokResp.IsSuccessStatusCode)
        {
            (string code, string msg) = await ReadErrorAsync(tokResp, ct).ConfigureAwait(false);
            return new MobileAuthTokenOutcome(false, code == "DEVICE_REVOKED", null, 0, null, code, msg);
        }

        TokenDto tok = (await tokResp.Content.ReadFromJsonAsync<TokenDto>(JsonOpts, ct).ConfigureAwait(false))!;
        return new MobileAuthTokenOutcome(true, false, tok.AccessToken, tok.ServerEpoch, tok.OfflineLease, null, null);
    }

    public async Task<MobileBootstrapOutcome> BootstrapAsync(CancellationToken ct = default)
    {
        (Guid deviceId, Guid tenantId, Guid storeId, _) = _scopeProvider();
        using HttpRequestMessage req = CreateAuthenticatedDeviceRequest(HttpMethod.Get, $"/api/sync/v1/bootstrap?storeId={storeId:D}", deviceId, tenantId, storeId);
        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            (string code, string msg) = await ReadErrorAsync(resp, ct).ConfigureAwait(false);
            return new MobileBootstrapOutcome(false, 0, 0, null, code, msg);
        }

        BootstrapDto dto = (await resp.Content.ReadFromJsonAsync<BootstrapDto>(JsonOpts, ct).ConfigureAwait(false))!;
        return new MobileBootstrapOutcome(true, dto.ServerEpoch, dto.HeadSequence, dto.Snapshot, null, null);
    }

    public async Task<MobilePushOutcome> PushBatchAsync(
        IReadOnlyList<SyncOperationEnvelopeV1> envelopes,
        CancellationToken ct = default)
    {
        (Guid deviceId, Guid tenantId, Guid storeId, long knownEpoch) = _scopeProvider();
        using HttpRequestMessage req = CreateAuthenticatedDeviceRequest(HttpMethod.Post, "/api/sync/v1/push", deviceId, tenantId, storeId);
        req.Content = JsonContent.Create(new { knownEpoch, operations = envelopes });

        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
        {
            PushRespDto? dto;
            try
            {
                dto = await resp.Content.ReadFromJsonAsync<PushRespDto>(JsonOpts, ct).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                dto = null;
            }

            // HTTP 2xx alone must never clear durable outbox rows. Require exactly one
            // central receipt per submitted operation; missing, foreign or duplicate IDs
            // leave the entire batch pending for safe retry/reconciliation. This is only
            // a transport receipt, NOT proof of canonical business projection.
            HashSet<Guid> outstanding = envelopes.Select(e => e.OperationId).ToHashSet();
            if (dto?.Results is null ||
                outstanding.Count != envelopes.Count ||
                outstanding.Contains(Guid.Empty) ||
                dto.Results.Count != envelopes.Count ||
                dto.Results.Any(r => r is null || !outstanding.Remove(r.OperationId) ||
                    (r.Resolution is not null && r.Resolution.OperationId != r.OperationId)) ||
                outstanding.Count != 0)
            {
                return new MobilePushOutcome(
                    false, false, false, dto?.ServerEpoch,
                    Array.Empty<Guid>(), Array.Empty<Guid>(),
                    "ERR_SYNC_ACK_MISMATCH",
                    "Central response did not acknowledge exactly the submitted operation IDs; local outbox remains pending.");
            }

            Guid[] applied = dto.Results
                .Where(r => r.Resolution?.IsApplied == true)
                .Select(r => r.OperationId).ToArray();
            bool allApplied = applied.Length == envelopes.Count;
            return new MobilePushOutcome(
                allApplied, false, false, dto.ServerEpoch,
                applied, Array.Empty<Guid>(),
                allApplied ? null : "ERR_SYNC_NOT_APPLIED",
                allApplied ? null : "Central received operations without applying all business effects; retain unapplied outbox evidence.");
        }

        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            FencedDto? fenced = TryDeserialize<FencedDto>(raw);
            if (fenced is not null && string.Equals(fenced.ErrorCode, "DEVICE_REVOKED_FENCED", StringComparison.Ordinal))
            {
                return new MobilePushOutcome(
                    false, true, false, fenced.ServerEpoch,
                    Array.Empty<Guid>(),
                    fenced.QuarantinedOperationIds ?? Array.Empty<Guid>(),
                    fenced.ErrorCode, fenced.ErrorMessage);
            }
        }

        (string errCode, string errMsg, long? epoch) = await ReadErrorWithEpochAsync(resp, ct).ConfigureAwait(false);
        return new MobilePushOutcome(
            false,
            errCode == "DEVICE_REVOKED",
            errCode == "EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED",
            epoch,
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            errCode,
            errMsg);
    }

    public async Task<MobilePullOutcome> PullPageAsync(long afterSequence, int limit = 50, CancellationToken ct = default)
    {
        (Guid deviceId, Guid tenantId, Guid storeId, long knownEpoch) = _scopeProvider();
        string path = $"/api/sync/v1/pull?storeId={storeId:D}&afterSequence={afterSequence}&limit={limit}&knownEpoch={knownEpoch}";
        using HttpRequestMessage req = CreateAuthenticatedDeviceRequest(HttpMethod.Get, path, deviceId, tenantId, storeId);
        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
        {
            PullRespDto dto = (await resp.Content.ReadFromJsonAsync<PullRespDto>(JsonOpts, ct).ConfigureAwait(false))!;
            return new MobilePullOutcome(true, false, false, dto.ServerEpoch, dto.Page, null, null);
        }

        (string errCode, string errMsg, long? epoch) = await ReadErrorWithEpochAsync(resp, ct).ConfigureAwait(false);
        return new MobilePullOutcome(
            false,
            errCode == "DEVICE_REVOKED",
            errCode == "EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED",
            epoch,
            null,
            errCode,
            errMsg);
    }

    // Implements legacy P0 interface so P0OutboxDispatcher can also use live HTTPS transport
    public async Task<P0TransportResult> SendAsync(P0OutboxItem item, CancellationToken cancellationToken = default)
    {
        (Guid deviceId, _, Guid storeId, _) = _scopeProvider();
        // Transport must replay the immutable envelope already committed by the canonical
        // SqliteSyncOutboxStore, including actor, timestamp, payload and allocated sequence.
        // P0 rows alone contain no authoritative actor or device-local operation identity.
        SyncOperationEnvelopeV1? env = _durableEnvelopeProvider?.Invoke(item.OperationId);
        if (env is null || env.OperationId != item.OperationId || env.DeviceId != deviceId ||
            env.StoreId != storeId || env.AggregateId != item.BusinessId ||
            !SyncProtocolV1.Validate(env).IsValid)
        {
            return P0TransportResult.Fail("ERR_SYNC_DURABLE_ENVELOPE_REQUIRED: A matching canonical persisted envelope is required before sending.");
        }

        MobilePushOutcome outcome = await PushBatchAsync([env], cancellationToken).ConfigureAwait(false);
        return outcome.IsSuccess
            ? P0TransportResult.Ack()
            : P0TransportResult.Fail($"{outcome.ErrorCode}: {outcome.ErrorMessage}");
    }

    private static HttpRequestMessage CreateBaseRequest(HttpMethod method, string relativePath)
    {
        HttpRequestMessage req = new(method, relativePath);
        req.Headers.Add(ProductIdHeader, CanonicalProductId);
        return req;
    }

    private HttpRequestMessage CreateAuthenticatedDeviceRequest(
        HttpMethod method,
        string relativePath,
        Guid deviceId,
        Guid tenantId,
        Guid storeId)
    {
        HttpRequestMessage req = CreateBaseRequest(method, relativePath);
        string? token = _bearerTokenProvider();
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        req.Headers.Add(DeviceIdHeader, deviceId.ToString("D"));
        req.Headers.Add(TenantIdHeader, tenantId.ToString("D"));
        req.Headers.Add(StoreIdHeader, storeId.ToString("D"));
        return req;
    }

    private static async Task<(string Code, string Message)> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        (string code, string msg, _) = await ReadErrorWithEpochAsync(resp, ct).ConfigureAwait(false);
        return (code, msg);
    }

    private static async Task<(string Code, string Message, long? Epoch)> ReadErrorWithEpochAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        ErrDto? err = TryDeserialize<ErrDto>(raw);
        return (err?.ErrorCode ?? $"HTTP_{(int)resp.StatusCode}", err?.ErrorMessage ?? raw, err?.ServerEpoch);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    private sealed record ErrDto(string? ErrorCode, string? ErrorMessage, long? ServerEpoch);
    private sealed record ChallengeDto(string ChallengeId, string NonceBase64, DateTimeOffset ExpiresAtUtc);
    private sealed record TokenDto(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, long ServerEpoch, MobileSignedOfflineLease OfflineLease);
    private sealed record BootstrapDto(string ProductId, long ServerEpoch, long HeadSequence, SyncBootstrapSnapshotV1 Snapshot);
    private sealed record PushResultItemDto(Guid OperationId, bool IsDuplicateReplay, SyncOperationFinalResolutionV1? Resolution);
    private sealed record PushRespDto(Guid CentralInstanceId, long ServerEpoch, long HeadSequence, IReadOnlyList<PushResultItemDto> Results);
    private sealed record FencedDto(string? ErrorCode, string? ErrorMessage, long ServerEpoch, IReadOnlyList<Guid>? QuarantinedOperationIds);
    private sealed record PullRespDto(string ProductId, long ServerEpoch, SyncChangeFeedPageV1 Page);
}

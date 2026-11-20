using System.Collections.Concurrent;
using System.Net;

namespace Salryn.Sync.Central.Server;

public static class CentralSyncEndpoints
{
    public const string ProductIdHeader = "X-Salryn-Product-Id";
    public const string TenantIdHeader = "X-Salryn-Tenant-Id";
    public const string StoreIdHeader = "X-Salryn-Store-Id";
    public const string DeviceIdHeader = "X-Salryn-Device-Id";

    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> DeviceRateBuckets = new(StringComparer.Ordinal);

    public static WebApplication MapCentralSyncEndpoints(this WebApplication app)
    {
        // Mandatory HTTPS middleware (fail-closed on plaintext HTTP when RequireHttps=true)
        app.Use(async (ctx, next) =>
        {
            CentralServerOptions options = ctx.RequestServices.GetRequiredService<CentralServerOptions>();
            if (options.RequireHttps && !IsHttpsRequest(ctx, options))
            {
                ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponseDto(
                    "ERR_HTTPS_REQUIRED",
                    "Salryn Cafe Central Sync requires HTTPS (TLS 1.2+)."));
                return;
            }

            if (options.RequireHttps)
            {
                ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            await next();
        });

        // Health endpoint (no product header required; exposes canonical ProductId & epoch)
        app.MapGet("/api/sync/v1/health", (CentralSyncService service) =>
            Results.Ok(service.GetHealth()));

        RouteGroupBuilder api = app.MapGroup("/api/sync/v1");
        api.AddEndpointFilter(async (ctx, next) =>
        {
            string productId = ctx.HttpContext.Request.Headers[ProductIdHeader].ToString();
            if (!string.Equals(productId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
            {
                return Results.Json(
                    new ErrorResponseDto(
                        "ERR_WRONG_PRODUCT",
                        $"Header '{ProductIdHeader}' must be '{CentralServerOptions.CanonicalProductId}'."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(ctx);
        });

        // --- Admin Auth ---
        api.MapPost("/admin/auth/login", (AdminLoginRequest req, CentralSyncService service) =>
            ToResult(service.AdminLogin(req)));

        api.MapPost("/admin/auth/logout", (HttpContext http, CentralSyncService service) =>
        {
            string? token = ExtractBearerToken(http.Request);
            AdminSessionDto? admin = service.ValidateAdminBearerToken(token);
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }

            service.AdminLogout(token!);
            return Results.Ok(new { loggedOut = true });
        });

        api.MapGet("/admin/auth/session", (HttpContext http, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            return admin is null
                ? Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401)
                : Results.Ok(admin);
        });

        // --- Admin Device & Quarantine Management ---
        api.MapPost("/admin/devices/invites", (HttpContext http, CreateInviteRequest req, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return ToResult(service.CreateInvite(admin, req));
        });

        api.MapGet("/admin/devices", (HttpContext http, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return Results.Ok(service.ListDevices(admin));
        });

        api.MapPost("/admin/devices/{deviceId:guid}/approve", (HttpContext http, Guid deviceId, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return ToResult(service.ApproveDevice(admin, deviceId));
        });

        api.MapPost("/admin/devices/{deviceId:guid}/revoke", (HttpContext http, Guid deviceId, RevokeDeviceRequest? req, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return ToResult(service.RevokeDevice(admin, deviceId, req?.Reason));
        });

        api.MapGet("/admin/quarantine", (HttpContext http, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return Results.Ok(service.ListQuarantine(admin));
        });

        api.MapPost("/admin/quarantine/{quarantineId:guid}/review", (HttpContext http, Guid quarantineId, ReviewQuarantineRequest req, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return ToResult(service.ReviewQuarantine(admin, quarantineId, req));
        });

        api.MapPost("/admin/epoch/bump", (HttpContext http, EpochBumpRequest req, CentralSyncService service) =>
        {
            AdminSessionDto? admin = service.ValidateAdminBearerToken(ExtractBearerToken(http.Request));
            if (admin is null)
            {
                return Results.Json(new ErrorResponseDto("ERR_ADMIN_AUTH_REQUIRED", "Valid admin bearer token required."), statusCode: 401);
            }
            return ToResult(service.BumpEpoch(admin, req));
        });

        // --- Device Enrollment, Challenge & Token (sliding-window rate-limited) ---
        api.MapPost("/devices/enroll", (HttpContext http, EnrollDeviceRequest req, CentralSyncService service, CentralServerOptions opts, TimeProvider time) =>
        {
            if (!TryConsumeDeviceRateLimit(http, "enroll", opts, time))
            {
                return Results.Json(new ErrorResponseDto("ERR_RATE_LIMITED", "Too many enrollment requests. Try again shortly."), statusCode: 429);
            }
            return ToResult(service.EnrollDevice(req));
        });

        api.MapPost("/devices/challenge", (HttpContext http, DeviceChallengeRequest req, CentralSyncService service, CentralServerOptions opts, TimeProvider time) =>
        {
            if (!TryConsumeDeviceRateLimit(http, "challenge", opts, time))
            {
                return Results.Json(new ErrorResponseDto("ERR_RATE_LIMITED", "Too many challenge requests. Try again shortly."), statusCode: 429);
            }
            return ToResult(service.CreateChallenge(req));
        });

        api.MapPost("/devices/token", (HttpContext http, DeviceTokenRequest req, CentralSyncService service, CentralServerOptions opts, TimeProvider time) =>
        {
            if (!TryConsumeDeviceRateLimit(http, "token", opts, time))
            {
                return Results.Json(new ErrorResponseDto("ERR_RATE_LIMITED", "Too many token requests. Try again shortly."), statusCode: 429);
            }
            return ToResult(service.ExchangeToken(req));
        });

        // --- Sync Bootstrap, Push & Pull ---
        api.MapGet("/bootstrap", (HttpContext http, Guid storeId, CentralSyncService service) =>
        {
            ServiceOutcome<AuthenticatedDeviceContext> auth = AuthenticateDevice(http, service, allowRevokedForQuarantinePush: false);
            if (auth.Value is null)
            {
                return ToResult(auth);
            }
            return ToResult(service.GetBootstrap(auth.Value, storeId));
        });

        api.MapPost("/push", (HttpContext http, PushOperationsRequest req, CentralSyncService service) =>
        {
            ServiceOutcome<AuthenticatedDeviceContext> auth = AuthenticateDevice(http, service, allowRevokedForQuarantinePush: true);
            if (auth.Value is null)
            {
                return ToResult(auth);
            }
            return ToResult(service.PushOperations(auth.Value, req));
        });

        api.MapGet("/pull", (HttpContext http, Guid storeId, long afterSequence, int? limit, long knownEpoch, CentralSyncService service) =>
        {
            ServiceOutcome<AuthenticatedDeviceContext> auth = AuthenticateDevice(http, service, allowRevokedForQuarantinePush: false);
            if (auth.Value is null)
            {
                return ToResult(auth);
            }
            return ToResult(service.PullChanges(auth.Value, storeId, afterSequence, limit ?? 50, knownEpoch));
        });

        return app;
    }

    private static ServiceOutcome<AuthenticatedDeviceContext> AuthenticateDevice(
        HttpContext http,
        CentralSyncService service,
        bool allowRevokedForQuarantinePush)
    {
        if (!Guid.TryParse(http.Request.Headers[DeviceIdHeader].ToString(), out Guid deviceId) ||
            !Guid.TryParse(http.Request.Headers[TenantIdHeader].ToString(), out Guid tenantId) ||
            !Guid.TryParse(http.Request.Headers[StoreIdHeader].ToString(), out Guid storeId))
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(
                400,
                "ERR_MISSING_DEVICE_HEADERS",
                $"Headers '{DeviceIdHeader}', '{TenantIdHeader}', and '{StoreIdHeader}' must be valid GUIDs.");
        }

        string productId = http.Request.Headers[ProductIdHeader].ToString();
        string? rawToken = ExtractBearerToken(http.Request);
        return service.AuthenticateDeviceToken(rawToken, deviceId, tenantId, storeId, productId, allowRevokedForQuarantinePush);
    }

    public static bool IsHttpsRequest(HttpContext ctx, CentralServerOptions options)
    {
        if (ctx.Request.IsHttps)
        {
            return true;
        }

        if (options.TrustForwardedProto)
        {
            string forwarded = ctx.Request.Headers["X-Forwarded-Proto"].ToString();
            if (string.Equals(forwarded, "https", StringComparison.OrdinalIgnoreCase))
            {
                IPAddress? remoteIp = ctx.Connection.RemoteIpAddress;
                // Only trust X-Forwarded-Proto from loopback or an explicitly configured TrustedProxyAddresses allowlist
                if (remoteIp is null ||
                    IPAddress.IsLoopback(remoteIp) ||
                    options.TrustedProxyAddresses.Contains(remoteIp.ToString()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryConsumeDeviceRateLimit(
        HttpContext http,
        string bucketName,
        CentralServerOptions options,
        TimeProvider time)
    {
        int limit = Math.Max(1, options.DeviceEndpointRateLimitPerMinute);
        string ip = http.Connection.RemoteIpAddress?.ToString() ?? "loopback";
        string key = $"{bucketName}:{ip}";
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset cutoff = now.AddMinutes(-1);

        Queue<DateTimeOffset> q = DeviceRateBuckets.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && q.Peek() <= cutoff)
            {
                q.Dequeue();
            }

            if (q.Count >= limit)
            {
                return false;
            }

            q.Enqueue(now);
            return true;
        }
    }

    private static string? ExtractBearerToken(HttpRequest request)
    {
        string authHeader = request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }
        return null;
    }

    private static IResult ToResult<T>(ServiceOutcome<T> outcome)
    {
        if (outcome.Fenced is not null)
        {
            return Results.Json(outcome.Fenced, statusCode: outcome.StatusCode);
        }

        if (outcome.Error is not null)
        {
            return Results.Json(outcome.Error, statusCode: outcome.StatusCode);
        }

        return Results.Json(outcome.Value, statusCode: outcome.StatusCode);
    }
}

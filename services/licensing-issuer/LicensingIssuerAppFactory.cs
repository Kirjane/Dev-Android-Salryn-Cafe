using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Salryn.Core.Services.Licensing;

namespace Salryn.LicensingIssuer.Server;

public sealed class LicensingIssuerHostOptions
{
    public string DatabasePath { get; init; } = Path.Combine(AppContext.BaseDirectory, "data", "salryn-licensing-issuer.db");
    public string? ListenUrl { get; init; } = "http://127.0.0.1:5410";
    public string? BootstrapAdminUsername { get; init; }
    public string? BootstrapAdminDisplayName { get; init; }
    public string? BootstrapAdminPassword { get; init; }
    public string? StaticWebRootPath { get; init; }
    public LicenseSignerOptions SignerOptions { get; init; } = new();
    public int MaxLoginAttemptsPerWindow { get; init; } = 8;
}

public static class LicensingIssuerAppFactory
{
    public static WebApplication Create(string[] args, LicensingIssuerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        if (!string.IsNullOrWhiteSpace(options.ListenUrl))
        {
            builder.WebHost.UseUrls(options.ListenUrl);
        }

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        var store = new IssuerSqliteStore(options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(options.BootstrapAdminUsername) &&
            !string.IsNullOrWhiteSpace(options.BootstrapAdminPassword))
        {
            store.EnsureBootstrapAdmin(
                options.BootstrapAdminUsername,
                options.BootstrapAdminDisplayName ?? "Kirjane Labs Administrator",
                options.BootstrapAdminPassword,
                IssuerConstants.RoleAdmin);
        }

        var signer = new ExternalLicenseSignerService(options.SignerOptions);
        var sessions = new IssuerSessionManager();
        var rateLimiter = new LoginRateLimiter(options.MaxLoginAttemptsPerWindow);

        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<ILicenseSignerService>(signer);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton(rateLimiter);

        WebApplication app = builder.Build();

        // Security headers on all responses (no open CORS; same-origin SPA hosting)
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            ctx.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                ctx.Response.Headers["Pragma"] = "no-cache";
            }
            await next();
        });

        static IssuerActiveSession? ResolveSession(HttpContext ctx, IssuerSessionManager mgr)
        {
            ctx.Request.Cookies.TryGetValue(IssuerConstants.SessionCookieName, out string? token);
            return mgr.TryGet(token);
        }

        static IResult? RequireAuth(HttpContext ctx, IssuerSessionManager mgr, bool requireCsrf, out IssuerActiveSession? session)
        {
            session = ResolveSession(ctx, mgr);
            if (session is null)
            {
                return Results.Json(new { code = "Issuer.Unauthorized", message = "Administrator authentication is required." }, statusCode: StatusCodes.Status401Unauthorized);
            }
            if (requireCsrf && !IssuerSessionManager.ValidateCsrf(session, ctx.Request))
            {
                return Results.Json(new { code = "Issuer.CsrfInvalid", message = $"Missing or invalid '{IssuerConstants.CsrfHeaderName}' header." }, statusCode: StatusCodes.Status403Forbidden);
            }
            return null;
        }

        // Public health endpoint (never reveals secrets or key material)
        app.MapGet("/api/issuer/health", (ILicenseSignerService s) =>
        {
            SignerStatusSnapshot signerStatus = s.GetStatus();
            return Results.Ok(new
            {
                service = "Salryn.LicensingIssuer",
                status = "ok",
                signerConfigured = signerStatus.IsConfigured,
                signerCanIssue = signerStatus.CanIssue,
                signerMode = signerStatus.SignerMode,
                trustedRuntimeSpkiSha256 = signerStatus.TrustedRuntimeSpkiSha256,
                utcTime = DateTimeOffset.UtcNow
            });
        });

        // Auth endpoints
        app.MapGet("/api/issuer/auth/session", (HttpContext ctx, IssuerSessionManager mgr) =>
        {
            IssuerActiveSession? session = ResolveSession(ctx, mgr);
            if (session is null)
            {
                return Results.Ok(new IssuerSessionInfo(false, null, null));
            }
            return Results.Ok(new IssuerSessionInfo(true, session.CsrfToken, session.User));
        });

        app.MapPost("/api/issuer/auth/login", (
            HttpContext ctx,
            LoginRequestDto? body,
            IssuerSqliteStore db,
            IssuerSessionManager mgr,
            LoginRateLimiter limiter) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            {
                return Results.BadRequest(new { code = "Issuer.CredentialsRequired", message = "Username and password are required." });
            }

            string remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string rateKey = $"{remoteIp}:{body.Username.Trim().ToLowerInvariant()}";
            if (limiter.IsRateLimited(rateKey))
            {
                return Results.Json(
                    new { code = "Issuer.LoginRateLimited", message = "Too many failed login attempts. Try again in a few minutes." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            IssuerAdminUser? user = db.Authenticate(body.Username, body.Password);
            if (user is null)
            {
                limiter.RecordFailure(rateKey);
                return Results.Json(
                    new { code = "Issuer.InvalidCredentials", message = "Invalid username or password." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            limiter.Reset(rateKey);
            IssuerActiveSession session = mgr.Create(user);
            db.RecordAudit(user, "Auth.Login", "Session", user.Id.ToString("D"), new { remoteIp });

            ctx.Response.Cookies.Append(IssuerConstants.SessionCookieName, session.SessionToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = ctx.Request.IsHttps,
                Path = "/",
                IsEssential = true,
                Expires = session.ExpiresAtUtc
            });

            return Results.Ok(new IssuerSessionInfo(true, session.CsrfToken, session.User));
        });

        app.MapPost("/api/issuer/auth/logout", (HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;

            mgr.Revoke(session!.SessionToken);
            db.RecordAudit(session.User, "Auth.Logout", "Session", session.User.Id.ToString("D"), new { });
            ctx.Response.Cookies.Delete(IssuerConstants.SessionCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new IssuerSessionInfo(false, null, null));
        });

        // Dashboard + metadata
        app.MapGet("/api/issuer/dashboard", (HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db, ILicenseSignerService s) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            return Results.Ok(db.GetDashboard(s.GetStatus()));
        });

        app.MapGet("/api/issuer/metadata", (HttpContext ctx, IssuerSessionManager mgr, ILicenseSignerService s) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            return Results.Ok(new
            {
                products = new[] { IssuerConstants.ProductCafe, IssuerConstants.ProductRetail },
                licenseTypes = new[] { nameof(LicenseType.Host), nameof(LicenseType.Terminal) },
                editions = new[] { nameof(LicenseEdition.Trial), nameof(LicenseEdition.Local), nameof(LicenseEdition.Professional), nameof(LicenseEdition.Enterprise) },
                allFeatures = LicenseFeatures.AllList,
                terminalAllowedFeatures = LicenseFeatures.TerminalAllowed.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                signer = s.GetStatus()
            });
        });

        // Customers
        app.MapGet("/api/issuer/customers", (HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            return Results.Ok(db.ListCustomers());
        });

        app.MapPost("/api/issuer/customers", (HttpContext ctx, CreateCustomerRequestDto? body, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;
            if (body is null || string.IsNullOrWhiteSpace(body.CustomerName) || body.CustomerName.Trim().Length > 160)
            {
                return Results.BadRequest(new { code = "Issuer.CustomerNameInvalid", message = "CustomerName is required (max 160 chars)." });
            }
            IssuerCustomerRecord created = db.CreateCustomer(body.CustomerName, body.ContactEmail, body.Notes, session!.User);
            return Results.Created($"/api/issuer/customers/{created.Id:D}", created);
        });

        // Stores
        app.MapGet("/api/issuer/stores", (HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            return Results.Ok(db.ListStores());
        });

        app.MapPost("/api/issuer/stores", (HttpContext ctx, CreateStoreRequestDto? body, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;
            if (body is null ||
                body.CustomerId == Guid.Empty ||
                string.IsNullOrWhiteSpace(body.StoreCode) ||
                string.IsNullOrWhiteSpace(body.StoreName))
            {
                return Results.BadRequest(new { code = "Issuer.StoreInvalid", message = "CustomerId, StoreCode, and StoreName are required." });
            }
            string productId = (body.ProductId ?? string.Empty).Trim();
            if (productId is not (IssuerConstants.ProductCafe or IssuerConstants.ProductRetail))
            {
                return Results.BadRequest(new { code = "Issuer.ProductUnsupported", message = $"ProductId must be '{IssuerConstants.ProductCafe}' or '{IssuerConstants.ProductRetail}'." });
            }
            try
            {
                IssuerStoreRecord created = db.CreateStore(body.CustomerId, body.StoreCode, body.StoreName, productId, session!.User);
                return Results.Created($"/api/issuer/stores/{created.Id:D}", created);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { code = "Issuer.NotFound", message = ex.Message });
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return Results.Conflict(new { code = "Issuer.StoreCodeDuplicate", message = $"StoreCode '{body.StoreCode.Trim()}' already exists." });
            }
        });

        // License requests (draft / review / approve / reject / issue / download / reissue)
        app.MapGet("/api/issuer/requests", (HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            return Results.Ok(db.ListRequests());
        });

        app.MapGet("/api/issuer/requests/{id:guid}", (Guid id, HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            IssuerLicenseRequestRecord? found = db.FindRequest(id);
            return found is null
                ? Results.NotFound(new { code = "Issuer.RequestNotFound", message = "License request was not found." })
                : Results.Ok(found);
        });

        app.MapPost("/api/issuer/requests", (HttpContext ctx, CreateLicenseDraftRequestDto? body, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;
            if (body is null)
            {
                return Results.BadRequest(new { code = "Issuer.BodyRequired", message = "Request body is required." });
            }
            try
            {
                (IssuerLicenseRequestRecord rec, IReadOnlyList<string> warnings, bool isReplay) = db.CreateDraft(body, session!.User);
                return Results.Json(new { request = rec, warnings, isReplay }, statusCode: isReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { code = "Issuer.NotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { code = "Issuer.DraftValidationFailed", message = ex.Message });
            }
        });

        app.MapPost("/api/issuer/requests/{id:guid}/approve", (Guid id, HttpContext ctx, ReviewDecisionRequestDto? body, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;
            try
            {
                IssuerLicenseRequestRecord updated = db.ApproveRequest(id, body?.Note, session!.User);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { code = "Issuer.RequestNotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { code = "Issuer.InvalidState", message = ex.Message });
            }
        });

        app.MapPost("/api/issuer/requests/{id:guid}/reject", (Guid id, HttpContext ctx, ReviewDecisionRequestDto? body, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;
            try
            {
                IssuerLicenseRequestRecord updated = db.RejectRequest(id, body?.Note, session!.User);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { code = "Issuer.RequestNotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { code = "Issuer.InvalidState", message = ex.Message });
            }
        });

        app.MapPost("/api/issuer/requests/{id:guid}/issue", (Guid id, HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db, ILicenseSignerService s) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;
            try
            {
                (IssuerLicenseRequestRecord? issued, LicenseSigningOutcome outcome, bool alreadyIssued) = db.IssueRequest(id, s, session!.User);
                if (!outcome.IsSuccess || issued is null)
                {
                    int statusCode = outcome.ErrorCode is "SIGNER_NOT_CONFIGURED" or "SIGNER_KEY_UNTRUSTED"
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status422UnprocessableEntity;
                    return Results.Json(new
                    {
                        code = outcome.ErrorCode ?? "ISSUER_SIGNING_FAILED",
                        message = outcome.ErrorMessage ?? "License signing failed.",
                        signer = s.GetStatus()
                    }, statusCode: statusCode);
                }
                return Results.Ok(new { request = issued, alreadyIssued });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { code = "Issuer.RequestNotFound", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { code = "Issuer.InvalidState", message = ex.Message });
            }
        });

        app.MapGet("/api/issuer/requests/{id:guid}/download", (Guid id, HttpContext ctx, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out IssuerActiveSession? session);
            if (authError is not null) return authError;

            IssuerLicenseRequestRecord? req = db.FindRequest(id);
            if (req is null)
            {
                return Results.NotFound(new { code = "Issuer.RequestNotFound", message = "License request was not found." });
            }
            if (req.Status is not (IssuerConstants.StatusIssued or IssuerConstants.StatusSuperseded) || string.IsNullOrWhiteSpace(req.SignedLicenseJson))
            {
                return Results.Conflict(new { code = "Issuer.NotIssued", message = "License has not been signed/issued yet." });
            }

            db.RecordAudit(session!.User, "LicenseRequest.Downloaded", "LicenseRequest", req.Id.ToString("D"), new
            {
                req.LicenseId,
                req.SignedLicenseSha256,
                req.SignerMode
            });

            string safeFileName = string.Concat(req.LicenseId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')) + ".license.json";
            byte[] bytes = Encoding.UTF8.GetBytes(req.SignedLicenseJson);
            ctx.Response.Headers["X-License-Sha256"] = req.SignedLicenseSha256 ?? string.Empty;
            return Results.File(bytes, "application/json; charset=utf-8", safeFileName);
        });

        app.MapPost("/api/issuer/requests/{id:guid}/reissue", (Guid id, HttpContext ctx, ReissueRequestDto? body, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: true, out IssuerActiveSession? session);
            if (authError is not null) return authError;

            IssuerLicenseRequestRecord? prior = db.FindRequest(id);
            if (prior is null)
            {
                return Results.NotFound(new { code = "Issuer.RequestNotFound", message = "Prior license request was not found." });
            }
            if (prior.Status is not (IssuerConstants.StatusIssued or IssuerConstants.StatusSuperseded))
            {
                return Results.Conflict(new { code = "Issuer.ReissueRequiresIssuedPrior", message = "Only an Issued or Superseded license can be reissued." });
            }

            var draftDto = new CreateLicenseDraftRequestDto(
                CustomerId: prior.CustomerId,
                StoreId: prior.StoreId,
                ApplicationId: prior.ApplicationId,
                LicenseType: prior.LicenseType.ToString(),
                Edition: prior.Edition.ToString(),
                MachineBinding: string.IsNullOrWhiteSpace(body?.MachineBinding) ? prior.MachineBinding : body.MachineBinding.Trim(),
                Features: body?.Features is { Count: > 0 } ? body.Features : prior.Features,
                AllowedLanTerminals: prior.LicenseType == LicenseType.Host ? (body?.AllowedLanTerminals ?? prior.AllowedLanTerminals) : null,
                HostLicenseId: prior.HostLicenseId,
                TerminalId: prior.TerminalId,
                ExpiresAtUtc: body?.ExpiresAtUtc ?? prior.ExpiresAtUtc,
                SupportExpiresAtUtc: prior.SupportExpiresAtUtc,
                UpdatesUntilUtc: prior.UpdatesUntilUtc,
                IdempotencyKey: body?.IdempotencyKey,
                SupersedesRequestId: prior.Id);

            try
            {
                (IssuerLicenseRequestRecord rec, IReadOnlyList<string> warnings, bool isReplay) = db.CreateDraft(draftDto, session!.User);
                return Results.Json(new { request = rec, warnings, isReplay }, statusCode: isReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { code = "Issuer.ReissueValidationFailed", message = ex.Message });
            }
        });

        app.MapGet("/api/issuer/audit", (HttpContext ctx, int? limit, IssuerSessionManager mgr, IssuerSqliteStore db) =>
        {
            IResult? authError = RequireAuth(ctx, mgr, requireCsrf: false, out _);
            if (authError is not null) return authError;
            return Results.Ok(db.ListAuditLog(limit ?? 100));
        });

        // Optional same-origin static SPA hosting (apps/licensing-web/dist)
        if (!string.IsNullOrWhiteSpace(options.StaticWebRootPath) && Directory.Exists(options.StaticWebRootPath))
        {
            string fullRoot = Path.GetFullPath(options.StaticWebRootPath);
            var fileProvider = new PhysicalFileProvider(fullRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

            string indexHtmlPath = Path.Combine(fullRoot, "index.html");
            app.MapFallback(async ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (File.Exists(indexHtmlPath))
                {
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.SendFileAsync(indexHtmlPath);
                }
                else
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            });
        }

        return app;
    }
}

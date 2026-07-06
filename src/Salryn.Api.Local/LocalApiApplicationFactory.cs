using Microsoft.Extensions.FileProviders;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.RemotePos;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Security;

namespace Salryn.Api.Local;

public sealed class LocalApiApplicationOptions
{
    public string Url { get; init; } = "http://127.0.0.1:5299";

    public SalrynLocalComposition? Composition { get; init; }

    public ILocalApiSessionStore? SessionStore { get; init; }

    public bool InitializeDatabase { get; init; } = true;

    public bool EnableStaticWebAssets { get; init; } = true;

    public bool WriteDevelopmentDatabasePath { get; init; } = true;
}

public static class LocalApiApplicationFactory
{
    public static async Task<WebApplication> CreateAsync(
        string[] args,
        LocalApiApplicationOptions? options = null)
    {
        options ??= new LocalApiApplicationOptions();

        SalrynLocalComposition composition =
            options.Composition ?? SalrynLocalLaunchModeResolver.CreateComposition(args);

        if (options.InitializeDatabase)
        {
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);
        }

        if (options.WriteDevelopmentDatabasePath &&
            SalrynLocalLaunchModeResolver.IsDevelopmentRequested(args))
        {
            Console.WriteLine(
                $"[Salryn Dev] SQLite database: {composition.DbPaths.DatabasePath}");
        }

        DashboardSummaryService dashboardSummaryService = new(composition);
        LanRemoteTransportApiServices lanRemoteTransportServices =
            LanRemoteTransportApiComposition.Create(composition);
        RemotePosRuntimeGate remotePosRuntimeGate = new();
        RemotePosApiServices remotePosServices =
            RemotePosApiServices.Create(composition, lanRemoteTransportServices, remotePosRuntimeGate);
        ILocalApiSessionStore sessionStore =
            options.SessionStore ?? new InMemoryLocalApiSessionStore();
        LocalApiSessionService sessionService = new(composition.Authentication, sessionStore);

        Result<LanRemoteTransportConfig> lanConfigResult = await lanRemoteTransportServices.Config
            .GetCurrentAsync()
            .ConfigureAwait(false);
        LanRemoteTransportConfig lanConfig = lanConfigResult.IsSuccess
            ? lanConfigResult.Value
            : LanRemoteTransportConfig.CreateDefault();
        IReadOnlyList<string> listenUrls = LocalApiLanUrlResolver.ResolveListenUrls(options.Url, lanConfig);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls(listenUrls.ToArray());

        builder.Services.AddSingleton(composition);
        builder.Services.AddSingleton(lanRemoteTransportServices);
        builder.Services.AddSingleton(remotePosRuntimeGate);
        builder.Services.AddSingleton(remotePosServices);
        builder.Services.AddSingleton(new LocalApiHostControlUrls(options.Url));
        builder.Services.AddSingleton(sessionStore);
        builder.Services.AddSingleton(sessionService);
        builder.Services.AddSingleton(dashboardSummaryService);
        builder.Services.AddSingleton<OperatorReadService>();
        builder.Services.AddSingleton<UserAdminApiService>();
        builder.Services.AddSingleton<RemoteReadOnlyApiService>();
        builder.Services.AddSingleton<LocalLanControlService>();

        builder.Services.AddCors(corsOptions =>
        {
            corsOptions.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        WebApplication app = builder.Build();

        app.UseCors();

        string? webDistPath = options.EnableStaticWebAssets
            ? ResolveWebDistPath()
            : null;

        WebDistSnapshot webDistSnapshot = WebDistDiagnostics.Inspect(webDistPath);
        WebDistDiagnostics.WriteStartupLog(webDistSnapshot);

        if (webDistPath is not null)
        {
            var distFileProvider = new PhysicalFileProvider(webDistPath);

            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = distFileProvider });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = distFileProvider,
                OnPrepareResponse = context =>
                {
                    if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
                    {
                        WebDistDiagnostics.ApplyIndexHtmlNoCacheHeaders(context.Context.Response);
                    }
                }
            });
        }

        app.MapGet("/api/system/web-dist", (HttpContext httpContext) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            return Results.Ok(WebDistDiagnostics.ToApiResponse(webDistSnapshot));
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "Salryn.Api.Local"
        }));

        app.MapPost("/api/auth/login", async (
            LoginApiRequest request,
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            Result<LocalApiSession> loginResult = await sessions.LoginAsync(
                new LoginRequest(request.Username, request.Password),
                cancellationToken).ConfigureAwait(false);

            if (loginResult.IsFailure)
            {
                return loginResult.ErrorDetail.Code.StartsWith("Auth.", StringComparison.Ordinal)
                    ? Results.Json(
                        new { error = loginResult.ErrorDetail.Message },
                        statusCode: StatusCodeForAuthError(loginResult.ErrorDetail))
                    : Results.BadRequest(new { error = loginResult.ErrorDetail.Message });
            }

            AppendSessionCookie(httpContext, loginResult.Value.SessionId);
            return Results.Ok(LocalApiSessionService.ToSessionResponse(loginResult.Value));
        });

        app.MapPost("/api/auth/logout", (
            HttpContext httpContext,
            LocalApiSessionService sessions) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            sessions.Logout(sessionId);
            httpContext.Response.Cookies.Delete(LocalApiSessionService.SessionCookieName);
            return Results.Ok(new { status = "logged_out" });
        });

        app.MapGet("/api/session", (
            HttpContext httpContext,
            LocalApiSessionService sessions) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            return Results.Ok(sessions.GetSession(sessionId));
        });

        app.MapGet("/api/dashboard/summary", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            DashboardSummaryService summaryService,
            CancellationToken cancellationToken) =>
        {
            if (ApiAuthExtensions.RequireAuthenticatedSession(httpContext, sessions) is IResult unauthorized)
            {
                return unauthorized;
            }

            var summary = await summaryService.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(summary);
        });

        app.MapReadOnlyOperatorEndpoints();
        app.MapLocalUserAdminEndpoints();
        app.MapRemoteReadOnlyEndpoints();
        app.MapLocalLanControlEndpoints();
        app.MapRemotePosEndpoints();

        if (webDistPath is not null)
        {
            string indexHtmlPath = Path.Combine(webDistPath, "index.html");

            app.MapFallback((HttpContext httpContext) =>
            {
                if (httpContext.Request.Path.StartsWithSegments("/api") ||
                    httpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return Results.NotFound();
                }

                WebDistDiagnostics.ApplyIndexHtmlNoCacheHeaders(httpContext.Response);
                return Results.File(indexHtmlPath, "text/html");
            });
        }

        return app;
    }

    private static void AppendSessionCookie(HttpContext httpContext, string sessionId)
    {
        httpContext.Response.Cookies.Append(
            LocalApiSessionService.SessionCookieName,
            sessionId,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = TimeSpan.FromHours(8)
            });
    }

    private static int StatusCodeForAuthError(Error error)
    {
        return error.Code switch
        {
            var code when code.StartsWith("Auth.InvalidCredentials", StringComparison.Ordinal) =>
                StatusCodes.Status401Unauthorized,
            var code when code.StartsWith("Auth.UserNotActive", StringComparison.Ordinal) =>
                StatusCodes.Status403Forbidden,
            var code when code.StartsWith("Session.", StringComparison.Ordinal) =>
                StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static string? ResolveWebDistPath()
    {
        foreach (string root in WebDistCandidateRoots())
        {
            string distPath = Path.Combine(root, "src", "Salryn.App.Web", "dist");

            if (File.Exists(Path.Combine(distPath, "index.html")))
            {
                return distPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> WebDistCandidateRoots()
    {
        string? fromBase = FindRepositoryRootFrom(AppContext.BaseDirectory);
        if (fromBase is not null)
        {
            yield return fromBase;
        }

        string? fromCurrent = FindRepositoryRootFrom(Directory.GetCurrentDirectory());
        if (fromCurrent is not null &&
            !string.Equals(fromCurrent, fromBase, StringComparison.OrdinalIgnoreCase))
        {
            yield return fromCurrent;
        }
    }

    private static string? FindRepositoryRootFrom(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}

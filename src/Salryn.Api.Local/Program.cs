using Microsoft.Extensions.FileProviders;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Security;

const string SmokeArgument = "--smoke";
const string SmokeIfSupportedArgument = "--smoke-if-supported";

if (IsSmokeRequested(args))
{
    await RunSmokeAsync(args).ConfigureAwait(false);
    return;
}

SalrynLocalComposition composition = SalrynLocalLaunchModeResolver.CreateComposition(args);
await composition.InitializeDatabaseAsync().ConfigureAwait(false);

DashboardSummaryService dashboardSummaryService = new(composition);
InMemoryLocalApiSessionStore sessionStore = new();
LocalApiSessionService sessionService = new(composition.Authentication, sessionStore);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:5299");

builder.Services.AddSingleton(composition);
builder.Services.AddSingleton<ILocalApiSessionStore>(sessionStore);
builder.Services.AddSingleton(sessionService);
builder.Services.AddSingleton(dashboardSummaryService);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://127.0.0.1:5173", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

WebApplication app = builder.Build();

app.UseCors();

// Optional static hosting of the built React app for the native WebView host.
// Only enabled when src/Salryn.App.Web/dist/index.html exists. No upload, no
// directory browsing, no broad file access, no LAN exposure, no new API surface.
string? webDistPath = ResolveWebDistPath();

if (webDistPath is not null)
{
    var distFileProvider = new PhysicalFileProvider(webDistPath);

    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = distFileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = distFileProvider });
}

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
    Result<LocalApiSession> loginResult = await sessions.LoginAsync(
        new LoginRequest(request.Username, request.Password),
        cancellationToken).ConfigureAwait(false);

    if (loginResult.IsFailure)
    {
        return loginResult.ErrorDetail.Code.StartsWith("Auth.", StringComparison.Ordinal)
            ? Results.Json(new { error = loginResult.ErrorDetail.Message }, statusCode: StatusCodeForAuthError(loginResult.ErrorDetail))
            : Results.BadRequest(new { error = loginResult.ErrorDetail.Message });
    }

    AppendSessionCookie(httpContext, loginResult.Value.SessionId);
    return Results.Ok(LocalApiSessionService.ToSessionResponse(loginResult.Value));
});

app.MapPost("/api/auth/logout", (
    HttpContext httpContext,
    LocalApiSessionService sessions) =>
{
    string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
    sessions.Logout(sessionId);
    httpContext.Response.Cookies.Delete(LocalApiSessionService.SessionCookieName);
    return Results.Ok(new { status = "logged_out" });
});

app.MapGet("/api/session", (
    HttpContext httpContext,
    LocalApiSessionService sessions) =>
{
    string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
    return Results.Ok(sessions.GetSession(sessionId));
});

app.MapGet("/api/dashboard/summary", async (
    HttpContext httpContext,
    LocalApiSessionService sessions,
    DashboardSummaryService summaryService,
    CancellationToken cancellationToken) =>
{
    string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];

    if (sessions.GetRequiredSession(sessionId) is null)
    {
        return Results.Json(
            new { error = "Authentication is required." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var summary = await summaryService.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(summary);
});

// SPA fallback: serve index.html for client-side routes only when static hosting is
// enabled. API and health routes are never shadowed by the fallback.
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

        return Results.File(indexHtmlPath, "text/html");
    });
}

app.Run();

static string? ResolveWebDistPath()
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

static IEnumerable<string> WebDistCandidateRoots()
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

static string? FindRepositoryRootFrom(string startPath)
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

static bool IsSmokeRequested(string[] args)
{
    return args.Any(item =>
        string.Equals(item, SmokeArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item, SmokeIfSupportedArgument, StringComparison.OrdinalIgnoreCase));
}

static void AppendSessionCookie(HttpContext httpContext, string sessionId)
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

static int StatusCodeForAuthError(Error error)
{
    return error.Code switch
    {
        var code when code.StartsWith("Auth.InvalidCredentials", StringComparison.Ordinal) => StatusCodes.Status401Unauthorized,
        var code when code.StartsWith("Auth.UserNotActive", StringComparison.Ordinal) => StatusCodes.Status403Forbidden,
        var code when code.StartsWith("Session.", StringComparison.Ordinal) => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest
    };
}

static async Task RunSmokeAsync(string[] args)
{
    SalrynLocalComposition composition = SalrynLocalLaunchModeResolver.CreateComposition(args);
    await composition.InitializeDatabaseAsync().ConfigureAwait(false);

    DashboardSummaryService summaryService = new(composition);
    InMemoryLocalApiSessionStore sessionStore = new();
    LocalApiSessionService sessionService = new(composition.Authentication, sessionStore);

    SessionResponse unauthenticated = sessionService.GetSession(null);
    if (unauthenticated.IsAuthenticated)
    {
        throw new InvalidOperationException("API smoke expected unauthenticated session by default.");
    }

    Result<LocalApiSession> invalidLogin = await sessionService.LoginAsync(
        new LoginRequest("web-a2-smoke-invalid", "not-a-real-password")).ConfigureAwait(false);

    if (invalidLogin.IsSuccess)
    {
        throw new InvalidOperationException("API smoke expected invalid login to fail.");
    }

    var summary = await summaryService.GetSummaryAsync().ConfigureAwait(false);

    Console.WriteLine(
        $"API smoke passed: session=unauthenticated, invalidLogin={invalidLogin.ErrorDetail.Code}, products={summary.ProductCount}, lowStock={summary.LowStockCount}");
}

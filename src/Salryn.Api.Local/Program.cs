using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
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

app.MapPost("/api/session/handoff/register", (
    NativeHandoffRegisterRequest request,
    LocalApiSessionService sessions) =>
{
    AuthenticatedUserReadModel user = new(
        request.UserId,
        request.Username,
        request.DisplayName,
        request.RoleName,
        Enum.Parse<UserStatus>(request.Status, ignoreCase: true),
        request.Permissions)
    {
        RoleId = request.RoleId
    };

    string handoffKey = sessions.RegisterNativeHandoff(user);
    return Results.Ok(new { handoffKey });
});

app.MapPost("/api/session/handoff/complete", (
    NativeHandoffCompleteRequest request,
    HttpContext httpContext,
    LocalApiSessionService sessions) =>
{
    Result<LocalApiSession> handoffResult = sessions.CompleteNativeHandoff(request.HandoffKey);

    if (handoffResult.IsFailure)
    {
        return Results.Json(
            new { error = handoffResult.ErrorDetail.Message },
            statusCode: StatusCodeForAuthError(handoffResult.ErrorDetail));
    }

    AppendSessionCookie(httpContext, handoffResult.Value.SessionId);
    return Results.Ok(LocalApiSessionService.ToSessionResponse(handoffResult.Value));
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

app.Run();

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

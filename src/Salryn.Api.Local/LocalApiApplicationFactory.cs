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
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;

namespace Salryn.Api.Local;

public sealed class LocalApiApplicationOptions
{
    public string Url { get; init; } = SalrynProductIdentity.ResolveLoopbackApiBaseUrl();

    public SalrynLocalComposition? Composition { get; init; }

    public ILocalApiSessionStore? SessionStore { get; init; }

    public ILocalLicenseService? LicenseService { get; init; }

    public bool InitializeDatabase { get; init; } = true;

    public bool EnableStaticWebAssets { get; init; } = true;

    public bool WriteDevelopmentDatabasePath { get; init; } = true;

    /// <summary>
    /// Explicitly enables the deferred LAN listener for internal QA only. The release
    /// default is fail-closed even when an older database persisted LAN as enabled.
    /// </summary>
    public bool EnableLanRemoteTransportForInternalQa { get; init; }

    /// <summary>
    /// Optional ephemeral test host identity nonce. When set, enables /api/test/host-identity
    /// to prove process and database identity to acceptance test runners.
    /// </summary>
    public string? TestHostNonce { get; init; }
}

public static class LocalApiApplicationFactory
{
    public static async Task<WebApplication> CreateAsync(
        string[] args,
        LocalApiApplicationOptions? options = null)
    {
        options ??= new LocalApiApplicationOptions();
        LocalApiLanBindStartupOptions startupBind = LocalApiLanBindStartupOptions.Parse(args);
        bool internalLanTransportEnabled = options.EnableLanRemoteTransportForInternalQa
            || args.Any(arg => string.Equals(arg, "--internal-lan-qa", StringComparison.OrdinalIgnoreCase));
        string localUrl = startupBind.Port is > 0
            ? LocalApiLanBindStartupOptions.BuildLocalUrl(startupBind.Port)
            : options.Url;

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

        if (startupBind.HasLanBind && !internalLanTransportEnabled)
        {
            throw new InvalidOperationException(
                "LAN transport is a future feature in this release. Explicit LAN binding requires --internal-lan-qa.");
        }

        if (startupBind.HasLanBind)
        {
            Result bindValidation = LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind(startupBind.LanBindIp);
            if (bindValidation.IsFailure)
            {
                throw new InvalidOperationException(bindValidation.Error);
            }

            LanRemoteTransportConfigUpdateRequest startupRequest = new(
                RemoteTransportEnabled: true,
                BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
                BindAddress: startupBind.LanBindIp!,
                Port: startupBind.Port ?? lanConfig.Port,
                RequirePairedTerminal: lanConfig.RequirePairedTerminal,
                RequireActiveTerminal: lanConfig.RequireActiveTerminal,
                RejectUnknownTerminals: lanConfig.RejectUnknownTerminals,
                AllowOfficialCommandsRemotely: lanConfig.AllowOfficialCommandsRemotely,
                AllowAdminCommandsRemotely: lanConfig.AllowAdminCommandsRemotely,
                UpdatedByUserId: null);

            Result<LanRemoteTransportConfig> savedStartup = await lanRemoteTransportServices.Config
                .SaveAsync(startupRequest)
                .ConfigureAwait(false);
            if (savedStartup.IsFailure)
            {
                throw new InvalidOperationException(savedStartup.Error);
            }

            lanConfig = savedStartup.Value;
            Console.WriteLine(
                $"[Salryn LAN] Explicit startup bind applied: {startupBind.LanBindIp}:{ExtractListenPort(localUrl)}");
        }

        LanRemoteTransportConfig effectiveListenConfig = internalLanTransportEnabled
            ? lanConfig
            : LanRemoteTransportConfig.CreateDefault();
        if (!internalLanTransportEnabled && lanConfig.RemoteTransportEnabled)
        {
            Console.WriteLine(
                "[Salryn LAN] Persisted LAN configuration retained but listener suppressed by the release boundary.");
        }

        IReadOnlyList<string> listenUrls = LocalApiLanUrlResolver.ResolveListenUrls(localUrl, effectiveListenConfig);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.WebHost.UseUrls(listenUrls.ToArray());
        builder.Services.AddSingleton(composition);
        builder.Services.AddLocalCafeDomainServices(composition);
        builder.Services.AddSingleton(lanRemoteTransportServices);
        builder.Services.AddSingleton(remotePosRuntimeGate);
        builder.Services.AddSingleton(remotePosServices);
        builder.Services.AddSingleton(new LocalApiHostControlUrls(localUrl));
        builder.Services.AddSingleton(sessionStore);
        builder.Services.AddSingleton(sessionService);
        ILocalLicenseService licenseService = options.LicenseService ?? new LocalLicenseService();
        builder.Services.AddSingleton<CafeCentralConnection>();
        builder.Services.AddSingleton(licenseService);
        builder.Services.AddSingleton<ILocalLicenseEnforcementService>(
            new LocalLicenseEnforcementService(licenseService));
        builder.Services.AddSingleton(dashboardSummaryService);
        builder.Services.AddSingleton<OperatorReadService>();
        builder.Services.AddSingleton<FastAddProductApiService>();
        builder.Services.AddSingleton<ProductManagementApiService>();
        builder.Services.AddSingleton<SaleVoidApiService>();
        builder.Services.AddSingleton<OwnerReportsApiService>();
        builder.Services.AddSingleton<OwnerAnalyticsApiService>();
        builder.Services.AddSingleton<CashDrawerReconciliationService>();
        builder.Services.AddSingleton<UserAdminApiService>();
        builder.Services.AddSingleton<IAccountsReceivableService>(
            new SqliteAccountsReceivableService(composition.ConnectionFactory));
        builder.Services.AddSingleton<IProcureToPayService>(
            new SqliteProcureToPayService(composition.ConnectionFactory));
        builder.Services.AddSingleton<ICustomerOrdersAndQuotesService>(
            new SqliteCustomerOrdersAndQuotesService(composition.ConnectionFactory));
        builder.Services.AddSingleton<RemoteReadOnlyApiService>();
        builder.Services.AddSingleton<LocalApiLanRuntimeStatusService>();
        builder.Services.AddSingleton<LocalLanControlService>();
        HostOperatorApiServices hostOperatorServices = HostOperatorApiServices.Create(composition);
        builder.Services.AddSingleton(hostOperatorServices);

        bool isDevCorsEnabled = SalrynLocalLaunchModeResolver.IsDevelopmentRequested(args)
            || builder.Environment.IsDevelopment();

        if (isDevCorsEnabled)
        {
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
        }

        WebApplication app = builder.Build();

        app.Use(async (httpContext, next) =>
        {
            httpContext.Response.Headers[SalrynProductIdentity.ProductHeaderName] = SalrynProductIdentity.ProductId;

            if (httpContext.Request.Headers.TryGetValue(SalrynProductIdentity.ProductHeaderName, out var incomingValues))
            {
                string? incomingProductId = incomingValues.FirstOrDefault();
                Result productCheck = SalrynProductGuard.ValidateProductId(incomingProductId);
                if (productCheck.IsFailure)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        errorCode = SalrynProductGuard.ErrorWrongProductIdentity,
                        error = productCheck.ErrorDetail.Message,
                        expectedProductId = SalrynProductIdentity.ProductId
                    }).ConfigureAwait(false);
                    return;
                }
            }

            await next().ConfigureAwait(false);
        });

        if (isDevCorsEnabled)
        {
            app.UseCors();
        }

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

        // The Cafe workstation is served from its own bundle under /cafe/.
        // Keep API requests on this Host origin and its existing authorization boundary.
        string? cafeDist = new[] { Path.Combine(AppContext.BaseDirectory, "cafe-dist") }
            .Concat(WebDistCandidateRoots().Select(root => Path.Combine(root, "apps", "cafe-web", "dist")))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "index.html")));
        if (options.EnableStaticWebAssets && cafeDist is not null)
        {
            var cafeProvider = new PhysicalFileProvider(cafeDist);
            app.Map("/cafe", cafeBranch =>
            {
                cafeBranch.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = cafeProvider,
                    OnPrepareResponse = context =>
                    {
                        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
                        {
                            WebDistDiagnostics.ApplyIndexHtmlNoCacheHeaders(context.Context.Response);
                        }
                    }
                });
                cafeBranch.Run(async context =>
                {
                    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                    {
                        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                        return;
                    }

                    string subpath = context.Request.Path.Value ?? string.Empty;
                    if (subpath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(subpath, "/assets", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    WebDistDiagnostics.ApplyIndexHtmlNoCacheHeaders(context.Response);
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.SendFileAsync(Path.Combine(cafeDist, "index.html"), context.RequestAborted)
                        .ConfigureAwait(false);
                });
            });
        }

        app.MapGet("/api/system/cafe-dist", (HttpContext httpContext) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            string? indexFile = cafeDist is null ? null : Path.Combine(cafeDist, "index.html");
            return Results.Ok(new
            {
                resolved = options.EnableStaticWebAssets && cafeDist is not null,
                staticAssetsEnabled = options.EnableStaticWebAssets,
                indexHtmlExists = indexFile is not null && File.Exists(indexFile),
                path = cafeDist,
                mountPath = "/cafe/"
            });
        });

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
            service = "Salryn.Api.Local",
            productId = SalrynProductIdentity.ProductId,
            productName = SalrynProductIdentity.ProductDisplayName,
            loopbackPort = SalrynProductIdentity.LoopbackPort,
            lanHttpsPort = SalrynProductIdentity.LanHttpsPort
        }));

        app.MapGet("/api/system/identity", (HttpContext httpContext) =>
        {
            bool isLoopback = LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is null;
            return Results.Ok(new
            {
                productId = SalrynProductIdentity.ProductId,
                productName = SalrynProductIdentity.ProductDisplayName,
                service = "Salryn.Api.Local",
                loopbackPort = SalrynProductIdentity.LoopbackPort,
                lanHttpsPort = SalrynProductIdentity.LanHttpsPort,
                desktopMutex = SalrynProductIdentity.DesktopMutexName,
                localApiMutex = SalrynProductIdentity.LocalApiMutexName,
                dataRoot = isLoopback ? composition.DbPaths.RootPath : null,
                databasePath = isLoopback ? composition.DbPaths.DatabasePath : null
            });
        });

        if (!string.IsNullOrWhiteSpace(options.TestHostNonce))
        {
            string testNonce = options.TestHostNonce;
            app.MapGet("/api/test/host-identity", (HttpContext httpContext) =>
            {
                if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
                {
                    return localOnly;
                }

                return Results.Ok(new
                {
                    status = "ok",
                    testHostNonce = testNonce,
                    processId = Environment.ProcessId,
                    databasePath = composition.DbPaths.DatabasePath
                });
            });
        }

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
            await Services.OperatorAuditTrail.TryWriteAsync(
                    composition.ConnectionFactory,
                    loginResult.Value.UserId,
                    Core.Services.OperatorAuditActions.UserLogin,
                    "User",
                    loginResult.Value.UserId.ToString("D"),
                    new
                    {
                        userId = loginResult.Value.UserId.ToString("D"),
                        username = loginResult.Value.Username,
                        roleName = loginResult.Value.RoleName
                    },
                    reason: "Host operator login succeeded.",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(LocalApiSessionService.ToSessionResponse(loginResult.Value));
        });

        app.MapPost("/api/auth/logout", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
            {
                return localOnly;
            }

            string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
            LocalApiSession? existing = sessions.GetRequiredSession(sessionId);
            sessions.Logout(sessionId);
            httpContext.Response.Cookies.Delete(LocalApiSessionService.SessionCookieName);
            if (existing is not null)
            {
                await Services.OperatorAuditTrail.TryWriteAsync(
                        composition.ConnectionFactory,
                        existing.UserId,
                        Core.Services.OperatorAuditActions.UserLogout,
                        "User",
                        existing.UserId.ToString("D"),
                        new
                        {
                            userId = existing.UserId.ToString("D"),
                            username = existing.Username
                        },
                        reason: "Host operator logout succeeded.",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

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

        app.MapLocalBootstrapEndpoints(composition, options.LicenseService);
        app.MapLocalLicenseEndpoints(options.LicenseService, composition: composition);

        app.MapGet("/api/dashboard/summary", async (
            HttpContext httpContext,
            LocalApiSessionService sessions,
            DashboardSummaryService summaryService,
            CancellationToken cancellationToken) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(
                    httpContext,
                    sessions,
                    LocalApiPermissions.ReportsView) is IResult unauthorized)
            {
                return unauthorized;
            }

            var summary = await summaryService.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(summary);
        });

        app.MapReadOnlyOperatorEndpoints();
        app.MapLocalFastAddProductEndpoints();
        app.MapLocalProductManagementEndpoints();
        app.MapLocalSaleVoidEndpoints();
        app.MapLocalOwnerReportsEndpoints();
        app.MapLocalOwnerAnalyticsEndpoints();
        app.MapLocalStoreSettingsEndpoints(composition);
        app.MapLocalStoreBrandingEndpoints(composition);
        app.MapLocalBackupPackageEndpoints(composition);
        app.MapLocalHostShiftEndpoints();
        app.MapLocalHostCheckoutEndpoints();
        app.MapLocalAccountsReceivableEndpoints();
        app.MapLocalCafeDomainEndpoints();
        app.MapLocalCafeCentralSyncEndpoints();
        app.MapLocalReceiptPrinterEndpoints();
        app.MapLocalUserAdminEndpoints();
        app.MapRemoteReadOnlyEndpoints();
        app.MapLocalLanControlEndpoints();
        app.MapRemotePosEndpoints();
        // RMO-BIR-M3/M4: host-local official readiness + setup (no live official posting).
        app.MapLocalOfficialReadinessEndpoints(composition.ConnectionFactory);
        app.MapLocalOfficialSetupEndpoints(composition.ConnectionFactory);
        app.MapLocalOfficialNumberingEndpoints(composition.ConnectionFactory);
        app.MapLocalOfficialIntegrityEndpoints(composition.ConnectionFactory);
        app.MapLocalOfficialSpineEndpoints(composition.ConnectionFactory);
        app.MapLocalOfficialDryRunPackageEndpoints(composition.ConnectionFactory);
        app.MapLocalOfficialLivePostingCandidateEndpoints(composition.ConnectionFactory);
        // TRAD-M3: host-local invoice output projection (immutable snapshot read; no live posting).
        app.MapLocalOfficialInvoiceOutputEndpoints(composition.ConnectionFactory);
        // TRAD-M4: host-local document lifecycle (reprint/void/refund append-only events).
        app.MapLocalOfficialDocumentLifecycleEndpoints(composition.ConnectionFactory);
        // TRAD-M5-RC2: lifecycle-aware traditional reporting (X/Z/EJ/eSales draft). BIR schema unconfirmed.
        app.MapLocalOfficialTraditionalReportingEndpoints(composition.ConnectionFactory);
        // PRE-CRYSTAL M0: host-only production report dataset spine. Crystal renderer unavailable.
        app.MapLocalProductionReportEndpoints(composition.ConnectionFactory);

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

        foreach (string listenUrl in listenUrls)
        {
            Console.WriteLine($"[Salryn LAN] Listening on: {listenUrl}");
        }

        return app;
    }

    private static int ExtractListenPort(string localUrl)
    {
        return Uri.TryCreate(localUrl, UriKind.Absolute, out Uri? uri) && uri.Port > 0
            ? uri.Port
            : SalrynProductIdentity.LoopbackPort;
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

    /// <summary>
    /// Resolves React static dist for host page and SPA fallback.
    /// Package/install layout: dist\ (or wwwroot\) beside Salryn.Api.Local.exe.
    /// Dev layout: repo root with Salryn.sln → src\Salryn.App.Web\dist.
    /// </summary>
    private static string? ResolveWebDistPath()
    {
        foreach (string candidate in WebDistCandidatePaths())
        {
            if (File.Exists(Path.Combine(candidate, "index.html")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> WebDistCandidatePaths()
    {
        // Package/publish layout: static assets next to the published executable.
        yield return Path.Combine(AppContext.BaseDirectory, "dist");
        yield return Path.Combine(AppContext.BaseDirectory, "wwwroot");

        // Staging layout that mirrors repo shape (optional package shape).
        yield return Path.Combine(AppContext.BaseDirectory, "src", "Salryn.App.Web", "dist");

        foreach (string root in WebDistCandidateRoots())
        {
            yield return Path.Combine(root, "src", "Salryn.App.Web", "dist");
            yield return Path.Combine(root, "dist");
        }
    }

    private static IEnumerable<string> WebDistCandidateRoots()
    {
        string? fromBase = FindRepositoryRootFrom(AppContext.BaseDirectory);
        if (fromBase is not null)
        {
            yield return fromBase;
        }

        string currentDirectory = Directory.GetCurrentDirectory();
        string? fromCurrent = FindRepositoryRootFrom(currentDirectory);
        if (fromCurrent is not null &&
            !string.Equals(fromCurrent, fromBase, StringComparison.OrdinalIgnoreCase))
        {
            yield return fromCurrent;
        }
    }

    private static string? FindRepositoryRootFrom(string startPath)
    {
        DirectoryInfo? directory = new(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

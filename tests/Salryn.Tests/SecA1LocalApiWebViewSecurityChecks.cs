using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class SecA1LocalApiWebViewSecurityChecks
{
    public static void Run()
    {
        int assertions = 0;
        void CountAssertion() => assertions++;

        VerifySourceAndContractGuards(CountAssertion);
        VerifyExecutableUnitSecurityGuards(CountAssertion);
        VerifyExecutableHttpSecurityAsync(CountAssertion).GetAwaiter().GetResult();

        Console.WriteLine(
            $"SEC-A1 local API & WebView security checks: PASS ({assertions} assertions; source + unit + live HTTP host).");
    }

    private static void VerifySourceAndContractGuards(Action countAssertion)
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string coreSmokeText = ReadRequiredFile(
            Path.Combine(repoRoot.FullName, "tools", "Salryn.Tools.CoreSmoke"), "Program.cs");

        RequireContains(coreSmokeText, "SecA1LocalApiWebViewSecurityChecks.Run();", "CoreSmoke must run SEC-A1 checks.", countAssertion);

        string apiProgram = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Program.cs")
            + "\n"
            + ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string productIdentitySource = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Infrastructure", "Paths", "SalrynProductIdentity.cs");
        string lanUrlResolverSource = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Services", "LocalApiLanUrlResolver.cs");
        string networkValidatorSource = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Services", "LocalApiNetworkInterfaceValidator.cs");
        string readOnlyEndpoints = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");
        string readOnlyContracts = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Contracts", "ReadOnlyContracts.cs");
        string authSessionContracts = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Contracts", "AuthSessionContracts.cs");
        string sessionService = ReadRequiredFile(repoRoot.FullName, "src", "Salryn.Api.Local", "Auth", "LocalApiSessionService.cs");

        string webViewDir = Path.Combine(repoRoot.FullName, "src", "Salryn.Desktop.WebView");
        string devHostConfig = ReadRequiredFile(webViewDir, "DevHostConfig.cs");
        string localWebOriginPolicy = ReadRequiredFile(webViewDir, "LocalWebOriginPolicy.cs");
        string mainForm = ReadRequiredFile(webViewDir, "MainForm.cs");
        string webViewProgram = ReadRequiredFile(webViewDir, "Program.cs");
        string webViewSource = string.Join("\n", devHostConfig, localWebOriginPolicy, mainForm, webViewProgram);

        string webSrc = Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web", "src");
        string sessionApi = ReadRequiredFile(webSrc, "api", "session.ts");
        string productsApi = ReadRequiredFile(webSrc, "api", "products.ts");
        string inventoryApi = ReadRequiredFile(webSrc, "api", "inventory.ts");
        string salesApi = ReadRequiredFile(webSrc, "api", "sales.ts");
        string usersApi = ReadRequiredFile(webSrc, "api", "users.ts");
        string auditApi = ReadRequiredFile(webSrc, "api", "audit.ts");
        string readOnlyModuleApi = string.Join("\n", productsApi, inventoryApi, salesApi, auditApi);

        string gitignore = ReadRequiredFile(repoRoot.FullName, ".gitignore");

        // R3A API localhost/LAN/CORS posture (Cafe loopback 5399, fail-closed LAN 5400, strict dev-only CORS).
        RequireContains(apiProgram, "SalrynProductIdentity.ResolveLoopbackApiBaseUrl()", "API options must default via SalrynProductIdentity.ResolveLoopbackApiBaseUrl().", countAssertion);
        RequireContains(apiProgram, "LocalApiLanUrlResolver.ResolveListenUrls(", "API startup must resolve listen URLs via LocalApiLanUrlResolver.ResolveListenUrls.", countAssertion);
        RequireContains(apiProgram, "--internal-lan-qa", "API must require explicit --internal-lan-qa opt-in for LAN binding.", countAssertion);
        RequireContains(apiProgram, "EnableLanRemoteTransportForInternalQa", "API options must keep explicit EnableLanRemoteTransportForInternalQa gate.", countAssertion);
        RequireContains(apiProgram, "LanRemoteTransportConfig.CreateDefault()", "API must fail closed to loopback-only when internal LAN QA is not enabled.", countAssertion);
        RequireContains(apiProgram, "LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind", "API must validate explicit LAN bind addresses against host NICs.", countAssertion);
        RequireContains(apiProgram, "bool isDevCorsEnabled = SalrynLocalLaunchModeResolver.IsDevelopmentRequested(args)", "API CORS must compute strict dev-only gate isDevCorsEnabled.", countAssertion);
        RequireContains(apiProgram, "if (isDevCorsEnabled)", "API CORS registration and middleware must be strictly gated by isDevCorsEnabled.", countAssertion);
        RequireContains(apiProgram, "WithOrigins(\"http://127.0.0.1:5173\", \"http://localhost:5173\")", "API dev CORS must stay restricted to local Vite origins.", countAssertion);
        RequireContains(apiProgram, "AllowCredentials", "API dev CORS must allow credentials for session cookies.", countAssertion);
        RequireNotContains(apiProgram, "AllowAnyOrigin", "API CORS must not use AllowAnyOrigin.", countAssertion);
        RequireNotContains(apiProgram, "ListenAnyIP", "API must not use ListenAnyIP.", countAssertion);
        RequireNotContains(apiProgram, "0.0.0.0", "API must not bind to 0.0.0.0.", countAssertion);
        RequireNotContains(apiProgram, "5299", "API host/factory must not reference protected Retail legacy port 5299.", countAssertion);
        RequireNotContains(apiProgram, "5387", "API host/factory must not reference protected Retail port 5387.", countAssertion);

        RequireContains(productIdentitySource, "public const string ProductId = \"Salryn.Cafe\";", "Product identity must pin Salryn.Cafe.", countAssertion);
        RequireContains(productIdentitySource, "public const string LoopbackBaseUrl = \"http://127.0.0.1:5399\";", "Product identity must pin Cafe loopback base URL to 127.0.0.1:5399.", countAssertion);
        RequireContains(productIdentitySource, "public const int LanHttpsPort = 5400;", "Product identity must pin Cafe LAN HTTPS port to 5400.", countAssertion);
        RequireContains(lanUrlResolverSource, "urls.Add($\"https://{bindAddress}:{lanPort}\");", "LAN URL resolver must emit HTTPS-only LAN URLs.", countAssertion);
        RequireNotContains(lanUrlResolverSource, "http://{bindAddress}", "LAN URL resolver must never emit cleartext HTTP LAN URLs.", countAssertion);
        RequireContains(networkValidatorSource, "WildcardForbidden", "Network interface validator must forbid wildcard binds.", countAssertion);

        // Dev DB path diagnostic stays console-only.
        RequireContains(apiProgram, "IsDevelopmentRequested", "Dev DB path diagnostic must remain dev-gated.", countAssertion);
        RequireNotContains(apiProgram, "MapGet(\"/api/database", "API must not expose database path through a public endpoint.", countAssertion);
        RequireNotContains(apiProgram, "Results.Ok(composition.DbPaths", "API must not return database paths in HTTP responses.", countAssertion);

        // Sensitive leakage guards.
        RequireNotContains(readOnlyContracts, "TechnicalDetail", "Error log DTOs must not expose technical detail.", countAssertion);
        RequireNotContains(authSessionContracts, "PasswordHash", "Session contracts must not expose password hashes.", countAssertion);
        RequireNotContains(sessionService, "PasswordHash", "Session service must not expose password hashes.", countAssertion);

        // Desktop WebView URL allowlist (Cafe 5173/5399 only) and navigation blocking.
        string[] expectedWebOrigins =
        [
            "http://127.0.0.1:5173",
            "http://localhost:5173",
            "http://127.0.0.1:5399",
            "http://localhost:5399",
        ];
        string allowedPrefixesBlock = ExtractBlock(localWebOriginPolicy, "AllowedWebOriginPrefixes =", "];");
        string[] actualAllowedLiterals = Regex.Matches(allowedPrefixesBlock, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Require(
            actualAllowedLiterals.Length == expectedWebOrigins.Length &&
            expectedWebOrigins.All(origin => actualAllowedLiterals.Contains(origin, StringComparer.Ordinal)),
            "LocalWebOriginPolicy.AllowedWebOriginPrefixes must allow ONLY Cafe loopback origins (5173 and 5399).",
            countAssertion);
        RequireNotContains(allowedPrefixesBlock, "5299", "WebView allowlist must not include protected Retail port 5299.", countAssertion);
        RequireNotContains(allowedPrefixesBlock, "5387", "WebView allowlist must not include protected Retail port 5387.", countAssertion);
        RequireNotContains(allowedPrefixesBlock, "https://", "WebView allowlist must not include HTTPS origins.", countAssertion);

        RequireContains(localWebOriginPolicy, "TryResolveConfiguredWebUrl", "WebView must validate SALRYN_CAFE_WEB_URL before use.", countAssertion);
        RequireContains(localWebOriginPolicy, "uri.Port is not (5173 or 5399)", "WebView origin policy must enforce Cafe ports 5173 or 5399 only.", countAssertion);
        RequireContains(localWebOriginPolicy, "Uri.UriSchemeHttp", "WebView origin policy must restrict allowed URLs to http scheme.", countAssertion);
        RequireContains(localWebOriginPolicy, "Uri.UriSchemeHttps", "WebView navigation policy must explicitly block https scheme.", countAssertion);
        RequireContains(localWebOriginPolicy, "Uri.UriSchemeFile", "WebView navigation policy must explicitly block file scheme.", countAssertion);
        RequireContains(localWebOriginPolicy, "\"data\"", "WebView navigation policy must explicitly block data scheme.", countAssertion);
        RequireContains(localWebOriginPolicy, "\"javascript\"", "WebView navigation policy must explicitly block javascript scheme.", countAssertion);
        RequireContains(localWebOriginPolicy, "!string.IsNullOrEmpty(uri.UserInfo)", "WebView origin policy must reject URLs containing userinfo.", countAssertion);

        RequireContains(devHostConfig, "public const string ApiBaseUrl = \"http://127.0.0.1:5399\";", "DevHostConfig must target Cafe loopback 127.0.0.1:5399.", countAssertion);
        RequireContains(devHostConfig, "public const int ProtectedRetailLegacyPort = 5299;", "DevHostConfig must explicitly track protected Retail legacy port 5299 for rejection.", countAssertion);
        RequireContains(devHostConfig, "public const int ProtectedRetailPort = 5387;", "DevHostConfig must explicitly track protected Retail port 5387 for rejection.", countAssertion);
        RequireContains(devHostConfig, "uri.Port == ProtectedRetailLegacyPort", "DevHostConfig must reject Retail port 5299 overrides.", countAssertion);
        RequireContains(devHostConfig, "uri.Port == ProtectedRetailPort", "DevHostConfig must reject Retail port 5387 overrides.", countAssertion);
        RequireContains(devHostConfig, "ResolveWebUrl(out string? configurationRejection)", "DevHostConfig must surface rejected SALRYN_CAFE_WEB_URL.", countAssertion);
        RequireContains(mainForm, "NavigationStarting", "WebView must block non-local navigation.", countAssertion);
        RequireContains(mainForm, "NewWindowRequested", "WebView must block external new-window navigation.", countAssertion);
        RequireContains(webViewProgram, "configurationRejection", "WebView host must warn when SALRYN_CAFE_WEB_URL is rejected.", countAssertion);

        RequireNotContains(webViewSource, "AllowAnyOrigin", "WebView host must not weaken CORS.", countAssertion);
        RequireNotContains(webViewSource, "0.0.0.0", "WebView host must not target 0.0.0.0.", countAssertion);

        foreach (string blockedScheme in new[] { "file://", "data:", "javascript:" })
        {
            RequireNotContains(webViewSource, blockedScheme, $"WebView host must not allow {blockedScheme} navigation.", countAssertion);
        }

        // Session client must send cookies.
        RequireContains(sessionApi, "credentials: \"include\"", "Web session client must send cookies.", countAssertion);

        // Read-only module clients remain GET-only. Users may POST only for USER-M1 host-local creation.
        RequireNotContains(readOnlyModuleApi, "method: \"POST\"", "Read-only module clients must not POST.", countAssertion);
        RequireNotContains(readOnlyModuleApi, "method: \"PUT\"", "Read-only module clients must not PUT.", countAssertion);
        RequireNotContains(readOnlyModuleApi, "method: \"PATCH\"", "Read-only module clients must not PATCH.", countAssertion);
        RequireNotContains(readOnlyModuleApi, "method: \"DELETE\"", "Read-only module clients must not DELETE.", countAssertion);
        RequireContains(usersApi, "method: \"POST\"", "Users client may POST only for USER-M1 host-local user creation.", countAssertion);
        RequireContains(usersApi, "/api/users", "USER-M1 user creation must stay under /api/users.", countAssertion);
        RequireNotContains(usersApi, "method: \"PUT\"", "USER-M1 must not add broad users PUT.", countAssertion);
        RequireNotContains(usersApi, "method: \"PATCH\"", "USER-M1 must not add broad users PATCH.", countAssertion);
        RequireNotContains(usersApi, "method: \"DELETE\"", "USER-M1 must not add broad users DELETE.", countAssertion);

        // No hardcoded diagnostic passwords in app source.
        foreach (string appRoot in new[]
        {
            Path.Combine(repoRoot.FullName, "src", "Salryn.Api.Local"),
            Path.Combine(repoRoot.FullName, "src", "Salryn.App.Web"),
            Path.Combine(repoRoot.FullName, "src", "Salryn.Desktop.WebView"),
            Path.Combine(repoRoot.FullName, "src", "Salryn.Composition.Local"),
        })
        {
            string combined = ReadAllSource(appRoot, new[] { ".cs", ".ts", ".tsx" }, SearchOption.AllDirectories);
            RequireNoForbiddenToken(combined, "StrongPass123!", "App source must not hardcode diagnostic passwords.", countAssertion);
            RequireNoForbiddenToken(combined, "StrongAdmin123!", "App source must not hardcode seed passwords.", countAssertion);
        }

        // Local diagnostic log hygiene.
        RequireContains(gitignore, ".salryn-*.log", "Gitignore must ignore local Salryn diagnostic logs.", countAssertion);
        RequireContains(gitignore, ".salryn-*.log.err", "Gitignore must ignore local Salryn diagnostic log errors.", countAssertion);

        // Read-only API surface still guarded.
        RequireNotContains(readOnlyEndpoints, "MapPost(\"/api/products", "Products must not expose POST.", countAssertion);
        RequireNotContains(readOnlyEndpoints, "MapPut(", "Read-only API must not expose PUT.", countAssertion);
        RequireNotContains(readOnlyEndpoints, "MapPatch(", "Read-only API must not expose PATCH.", countAssertion);
        RequireNotContains(readOnlyEndpoints, "MapDelete(", "Read-only API must not expose DELETE.", countAssertion);
    }

    private static void VerifyExecutableUnitSecurityGuards(Action countAssertion)
    {
        // 1. Cafe loopback URL resolution defaults and fail-closed fallback.
        string? priorApiUrl = Environment.GetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, null);
            Require(
                string.Equals(SalrynProductIdentity.ResolveLoopbackApiBaseUrl(), "http://127.0.0.1:5399", StringComparison.Ordinal),
                "ResolveLoopbackApiBaseUrl() must default to http://127.0.0.1:5399.",
                countAssertion);

            foreach (string rejectedOverride in new[]
            {
                "http://127.0.0.1:5299",
                "http://localhost:5299",
                "http://127.0.0.1:5387",
                "http://localhost:5387",
                "http://0.0.0.0:5399",
                "http://192.168.1.50:5399",
                "http://192.0.2.10:5399",
                "https://127.0.0.1:5399",
                "https://localhost:5399",
                "ftp://127.0.0.1:5399",
                "not-a-valid-url",
            })
            {
                Require(
                    string.Equals(SalrynProductIdentity.ResolveLoopbackApiBaseUrl(rejectedOverride), "http://127.0.0.1:5399", StringComparison.Ordinal),
                    $"ResolveLoopbackApiBaseUrl override must fall back to 127.0.0.1:5399 for '{rejectedOverride}'.",
                    countAssertion);

                Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, rejectedOverride);
                Require(
                    string.Equals(SalrynProductIdentity.ResolveLoopbackApiBaseUrl(), "http://127.0.0.1:5399", StringComparison.Ordinal),
                    $"SALRYN_CAFE_API_URL env var must fall back to 127.0.0.1:5399 for '{rejectedOverride}'.",
                    countAssertion);
            }

            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, "http://localhost:5499");
            Require(
                string.Equals(SalrynProductIdentity.ResolveLoopbackApiBaseUrl(), "http://localhost:5499", StringComparison.Ordinal),
                "Valid loopback non-retail SALRYN_CAFE_API_URL override must resolve cleanly.",
                countAssertion);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, priorApiUrl);
        }

        // 2. LocalApiLanUrlResolver fail-closed loopback, wildcard/invalid rejection, HTTPS-only LAN separation.
        const string defaultLoopback = "http://127.0.0.1:5399";
        LanRemoteTransportConfig defaultConfig = LanRemoteTransportConfig.CreateDefault();
        IReadOnlyList<string> defaultListenUrls = LocalApiLanUrlResolver.ResolveListenUrls(defaultLoopback, defaultConfig);
        Require(
            defaultListenUrls.Count == 1 && string.Equals(defaultListenUrls[0], defaultLoopback, StringComparison.Ordinal),
            "Default LocalApiLanUrlResolver must return a single loopback URL.",
            countAssertion);

        foreach (string invalidBind in new[] { "0.0.0.0", "::", "127.0.0.1", "192.0.2.99", "not-an-ip", "" })
        {
            LanRemoteTransportConfig badConfig = defaultConfig with
            {
                RemoteTransportEnabled = true,
                BindMode = LanRemoteTransportBindMode.ExplicitLanAddress,
                BindAddress = invalidBind,
                Port = SalrynProductIdentity.LanHttpsPort
            };
            IReadOnlyList<string> resolved = LocalApiLanUrlResolver.ResolveListenUrls(defaultLoopback, badConfig);
            Require(
                resolved.Count == 1 && string.Equals(resolved[0], defaultLoopback, StringComparison.Ordinal),
                $"LocalApiLanUrlResolver must reject invalid/wildcard/unassigned LAN bind '{invalidBind}' and keep loopback only.",
                countAssertion);
        }

        Require(
            LocalApiLanUrlResolver.ResolveLanHttpsPort(defaultConfig) == 5400 &&
            LocalApiLanUrlResolver.ResolveLanHttpsPort(defaultConfig with { Port = 5399 }) == 5400 &&
            LocalApiLanUrlResolver.ResolveLanHttpsPort(defaultConfig with { Port = 5299 }) == 5400 &&
            LocalApiLanUrlResolver.ResolveLanHttpsPort(defaultConfig with { Port = 5387 }) == 5400 &&
            LocalApiLanUrlResolver.ResolveLanHttpsPort(defaultConfig with { Port = 5443 }) == 5443,
            "ResolveLanHttpsPort must fall back to 5400 for default/loopback/retail ports and accept valid custom ports.",
            countAssertion);

        if (TryFindAssignedLanIpv4(out string? assignedLanIp))
        {
            LanRemoteTransportConfig defaultLanPortConfig = defaultConfig with
            {
                RemoteTransportEnabled = true,
                BindMode = LanRemoteTransportBindMode.ExplicitLanAddress,
                BindAddress = assignedLanIp!,
                Port = LanRemoteTransportConfig.DefaultPort
            };
            IReadOnlyList<string> lanUrlsDefaultPort = LocalApiLanUrlResolver.ResolveListenUrls(defaultLoopback, defaultLanPortConfig);
            Require(
                lanUrlsDefaultPort.Count == 2 &&
                string.Equals(lanUrlsDefaultPort[0], defaultLoopback, StringComparison.Ordinal) &&
                string.Equals(lanUrlsDefaultPort[1], $"https://{assignedLanIp}:5400", StringComparison.Ordinal),
                "Enabled LAN config must keep HTTP loopback and HTTPS 5400 LAN listeners strictly separate.",
                countAssertion);

            LanRemoteTransportConfig customLanPortConfig = defaultLanPortConfig with { Port = 5443 };
            IReadOnlyList<string> lanUrlsCustomPort = LocalApiLanUrlResolver.ResolveListenUrls(defaultLoopback, customLanPortConfig);
            Require(
                lanUrlsCustomPort.Count == 2 &&
                string.Equals(lanUrlsCustomPort[0], defaultLoopback, StringComparison.Ordinal) &&
                string.Equals(lanUrlsCustomPort[1], $"https://{assignedLanIp}:5443", StringComparison.Ordinal) &&
                lanUrlsCustomPort.Skip(1).All(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)),
                "Enabled LAN config with custom non-retail port must emit HTTPS-only LAN URL.",
                countAssertion);
        }

        // 3. LocalApiNetworkInterfaceValidator negative and positive checks.
        RequireValidationFailure(LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind(null), "LanBind.BindAddressRequired", "Null bind must fail.", countAssertion);
        RequireValidationFailure(LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind("0.0.0.0"), "LanBind.WildcardForbidden", "0.0.0.0 bind must fail.", countAssertion);
        RequireValidationFailure(LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind("127.0.0.1"), "LanBind.LoopbackForbidden", "Loopback bind must fail.", countAssertion);
        RequireValidationFailure(LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind("::1"), "LanBind.LoopbackForbidden", "IPv6 loopback bind must fail.", countAssertion);
        RequireValidationFailure(LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind("not-an-ip"), "LanBind.InvalidIpv4", "Malformed IP must fail.", countAssertion);
        RequireValidationFailure(LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind("192.0.2.1"), "LanBind.IpNotOnHost", "Unassigned IPv4 must fail.", countAssertion);

        if (TryFindAssignedLanIpv4(out string? validLanIp))
        {
            Require(
                LocalApiNetworkInterfaceValidator.ValidateExplicitLanBind(validLanIp).IsSuccess,
                "Assigned host LAN IPv4 must pass ValidateExplicitLanBind.",
                countAssertion);
        }

        // 4. LocalApiLocalAccessGuard IP and origin checks.
        DefaultHttpContext validLoopbackContext = new();
        validLoopbackContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        Require(LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(validLoopbackContext), "Loopback request without cross-origin headers must pass LocalAccessGuard.", countAssertion);

        DefaultHttpContext remoteIpContext = new();
        remoteIpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.55");
        Require(!LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(remoteIpContext), "Non-loopback IP must fail LocalAccessGuard.", countAssertion);

        DefaultHttpContext crossOriginContext = new();
        crossOriginContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        crossOriginContext.Request.Headers.Origin = "https://evil.invalid";
        Require(!LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(crossOriginContext), "Cross-origin Origin header must fail LocalAccessGuard.", countAssertion);

        DefaultHttpContext secFetchCrossSiteContext = new();
        secFetchCrossSiteContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        secFetchCrossSiteContext.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        Require(!LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(secFetchCrossSiteContext), "Sec-Fetch-Site: cross-site must fail LocalAccessGuard.", countAssertion);

        DefaultHttpContext remoteBridgeHeaderContext = new();
        remoteBridgeHeaderContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        remoteBridgeHeaderContext.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId] = Guid.NewGuid().ToString("D");
        Require(!LocalApiLocalAccessGuard.IsLoopbackOperatorRequest(remoteBridgeHeaderContext), "Remote bridge terminal header must fail LocalAccessGuard.", countAssertion);

        // 5. SalrynProductGuard wrong-product isolation checks.
        Result retailProductResult = SalrynProductGuard.ValidateProductId(SalrynProductIdentity.ProtectedRetailProductId);
        Require(
            retailProductResult.IsFailure && retailProductResult.ErrorDetail.Code == SalrynProductGuard.ErrorWrongProductIdentity,
            "SalrynProductGuard must reject protected Retail product identity 'Salryn.POS'.",
            countAssertion);
        Require(
            SalrynProductGuard.ValidateProductId(SalrynProductIdentity.ProductId).IsSuccess,
            "SalrynProductGuard must accept 'Salryn.Cafe'.",
            countAssertion);
        Require(
            SalrynProductGuard.ValidateLicenseProductId(SalrynProductIdentity.ProtectedRetailProductId).IsFailure,
            "SalrynProductGuard must reject 'Salryn.POS' license product id.",
            countAssertion);
        BackupProductGuardResult wrongBackup = SalrynProductGuard.ValidateBackupManifestForRestore(SalrynProductIdentity.ProtectedRetailProductId);
        Require(
            !wrongBackup.Allowed && wrongBackup.ErrorCode == SalrynProductGuard.ErrorWrongProductBackup,
            "SalrynProductGuard must reject 'Salryn.POS' backup manifests.",
            countAssertion);
        Require(
            SalrynProductGuard.ValidateNotProtectedRetailPath(SalrynProductIdentity.ProtectedRetailInstallDir).IsFailure,
            "SalrynProductGuard must reject protected Retail install directory.",
            countAssertion);
    }

    private static async Task VerifyExecutableHttpSecurityAsync(Action countAssertion)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-sec-a1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string? priorAppEnv = Environment.GetEnvironmentVariable("SALRYN_APP_ENV");
        string? priorAspNetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string? priorDotNetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        string? priorLanBindIp = Environment.GetEnvironmentVariable("SALRYN_LAN_BIND_IP");
        string? priorPort = Environment.GetEnvironmentVariable("SALRYN_PORT");
        string? priorApiUrl = Environment.GetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable);

        try
        {
            // Isolate ambient environment so Production vs --dev tests are deterministic.
            Environment.SetEnvironmentVariable("SALRYN_APP_ENV", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
            Environment.SetEnvironmentVariable("SALRYN_LAN_BIND_IP", null);
            Environment.SetEnvironmentVariable("SALRYN_PORT", null);
            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, null);

            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            await composition.InitializeDatabaseAsync().ConfigureAwait(false);

            // Host 1: Production mode (CORS disabled, fail-closed auth & product guard).
            await using (WebApplication prodApp = await LocalApiApplicationFactory.CreateAsync(
                Array.Empty<string>(),
                CreateEphemeralHostOptions(composition)).ConfigureAwait(false))
            {
                await prodApp.StartAsync().ConfigureAwait(false);
                try
                {
                    Uri baseAddress = ResolveStartedLoopbackAddress(prodApp);
                    using HttpClient client = new() { BaseAddress = baseAddress };

                    // (a) Production mode: unauthorized AND Vite dev origins must receive NO Access-Control-Allow-Origin.
                    foreach (string origin in new[] { "https://evil.invalid", "http://127.0.0.1:5299", "http://127.0.0.1:5173" })
                    {
                        await AssertNoCorsAllowOriginAsync(client, origin, "Production mode", countAssertion).ConfigureAwait(false);
                    }

                    // (b) Wrong product header X-Salryn-Product-Id: Salryn.POS -> HTTP 403 ERR_WRONG_PRODUCT_IDENTITY.
                    using (HttpRequestMessage wrongProductRequest = new(HttpMethod.Get, "/health"))
                    {
                        wrongProductRequest.Headers.Add(SalrynProductIdentity.ProductHeaderName, SalrynProductIdentity.ProtectedRetailProductId);
                        using HttpResponseMessage wrongProductResponse = await client.SendAsync(wrongProductRequest).ConfigureAwait(false);
                        string wrongProductBody = await wrongProductResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Require(
                            wrongProductResponse.StatusCode == HttpStatusCode.Forbidden &&
                            wrongProductBody.Contains(SalrynProductGuard.ErrorWrongProductIdentity, StringComparison.Ordinal) &&
                            wrongProductBody.Contains(SalrynProductIdentity.ProductId, StringComparison.Ordinal),
                            $"Wrong product header '{SalrynProductIdentity.ProtectedRetailProductId}' must return 403 {SalrynProductGuard.ErrorWrongProductIdentity}. Got {(int)wrongProductResponse.StatusCode}: {wrongProductBody}",
                            countAssertion);
                    }

                    // Valid product header returns 200 OK with Cafe product header echoed.
                    using (HttpRequestMessage validProductRequest = new(HttpMethod.Get, "/health"))
                    {
                        validProductRequest.Headers.Add(SalrynProductIdentity.ProductHeaderName, SalrynProductIdentity.ProductId);
                        using HttpResponseMessage validProductResponse = await client.SendAsync(validProductRequest).ConfigureAwait(false);
                        Require(
                            validProductResponse.StatusCode == HttpStatusCode.OK &&
                            validProductResponse.Headers.TryGetValues(SalrynProductIdentity.ProductHeaderName, out IEnumerable<string>? headerValues) &&
                            headerValues.Contains(SalrynProductIdentity.ProductId, StringComparer.Ordinal),
                            "Valid Cafe product header must succeed on /health and echo X-Salryn-Product-Id: Salryn.Cafe.",
                            countAssertion);
                    }

                    // (c) Unauthenticated protected endpoints fail closed (401 on loopback, 403 when cross-origin).
                    foreach (string protectedEndpoint in new[]
                    {
                        "/api/dashboard/summary",
                        "/api/users",
                        "/api/lan-control/status",
                        "/api/lan-control/runtime-status"
                    })
                    {
                        using HttpResponseMessage unauthResponse = await client.GetAsync(protectedEndpoint).ConfigureAwait(false);
                        Require(
                            unauthResponse.StatusCode == HttpStatusCode.Unauthorized ||
                            unauthResponse.StatusCode == HttpStatusCode.Forbidden,
                            $"Unauthenticated GET {protectedEndpoint} must fail closed (401/403). Got {(int)unauthResponse.StatusCode}.",
                            countAssertion);

                        using HttpRequestMessage crossOriginProtected = new(HttpMethod.Get, protectedEndpoint);
                        crossOriginProtected.Headers.Add("Origin", "https://evil.invalid");
                        using HttpResponseMessage crossOriginResponse = await client.SendAsync(crossOriginProtected).ConfigureAwait(false);
                        Require(
                            crossOriginResponse.StatusCode == HttpStatusCode.Forbidden,
                            $"Cross-origin GET {protectedEndpoint} must be rejected 403. Got {(int)crossOriginResponse.StatusCode}.",
                            countAssertion);
                    }
                }
                finally
                {
                    await prodApp.StopAsync().ConfigureAwait(false);
                }
            }

            // Host 2: --dev mode (strict Vite-only CORS with Vary/Credentials, still blocks evil and Retail 5299 origins).
            await using (WebApplication devApp = await LocalApiApplicationFactory.CreateAsync(
                new[] { "--dev" },
                CreateEphemeralHostOptions(composition)).ConfigureAwait(false))
            {
                await devApp.StartAsync().ConfigureAwait(false);
                try
                {
                    Uri baseAddress = ResolveStartedLoopbackAddress(devApp);
                    using HttpClient client = new() { BaseAddress = baseAddress };

                    foreach (string disallowedOrigin in new[] { "https://evil.invalid", "http://127.0.0.1:5299" })
                    {
                        await AssertNoCorsAllowOriginAsync(client, disallowedOrigin, "--dev mode", countAssertion).ConfigureAwait(false);
                    }

                    // Allowed Vite origin receives Access-Control-Allow-Origin, Access-Control-Allow-Credentials, and Vary: Origin.
                    const string viteOrigin = "http://127.0.0.1:5173";
                    using (HttpRequestMessage preflight = new(HttpMethod.Options, "/health"))
                    {
                        preflight.Headers.Add("Origin", viteOrigin);
                        preflight.Headers.Add("Access-Control-Request-Method", "GET");
                        using HttpResponseMessage preflightResponse = await client.SendAsync(preflight).ConfigureAwait(false);
                        AssertCorsAllowed(preflightResponse, viteOrigin, "--dev preflight OPTIONS", countAssertion);
                    }

                    using (HttpRequestMessage corsGet = new(HttpMethod.Get, "/health"))
                    {
                        corsGet.Headers.Add("Origin", viteOrigin);
                        using HttpResponseMessage corsGetResponse = await client.SendAsync(corsGet).ConfigureAwait(false);
                        Require(corsGetResponse.StatusCode == HttpStatusCode.OK, "--dev CORS GET /health must return 200 OK.", countAssertion);
                        AssertCorsAllowed(corsGetResponse, viteOrigin, "--dev GET", countAssertion);
                    }
                }
                finally
                {
                    await devApp.StopAsync().ConfigureAwait(false);
                }
            }

            // (c) Explicit --lan-bind without --internal-lan-qa must throw InvalidOperationException.
            string lanBindCandidate = TryFindAssignedLanIpv4(out string? hostLanIp) ? hostLanIp! : "192.0.2.50";
            bool threwExpected = false;
            try
            {
                await using WebApplication shouldNotStart = await LocalApiApplicationFactory.CreateAsync(
                    new[] { "--lan-bind", lanBindCandidate },
                    CreateEphemeralHostOptions(composition)).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("--internal-lan-qa", StringComparison.Ordinal))
            {
                threwExpected = true;
            }

            Require(
                threwExpected,
                "Explicit --lan-bind without --internal-lan-qa must fail closed by throwing InvalidOperationException.",
                countAssertion);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_APP_ENV", priorAppEnv);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", priorAspNetEnv);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", priorDotNetEnv);
            Environment.SetEnvironmentVariable("SALRYN_LAN_BIND_IP", priorLanBindIp);
            Environment.SetEnvironmentVariable("SALRYN_PORT", priorPort);
            Environment.SetEnvironmentVariable(SalrynProductIdentity.ApiUrlEnvironmentVariable, priorApiUrl);

            CleanupDirectory(tempRoot);
        }
    }

    private static LocalApiApplicationOptions CreateEphemeralHostOptions(SalrynLocalComposition composition) => new()
    {
        Url = "http://127.0.0.1:0",
        Composition = composition,
        InitializeDatabase = false,
        EnableStaticWebAssets = false,
        WriteDevelopmentDatabasePath = false,
        EnableLanRemoteTransportForInternalQa = false
    };

    private static Uri ResolveStartedLoopbackAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(url =>
            url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(address) || address.EndsWith(":0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SEC-A1 ephemeral host failed to bind an assigned loopback port.");
        }

        return new Uri(address);
    }

    private static async Task AssertNoCorsAllowOriginAsync(
        HttpClient client,
        string origin,
        string modeLabel,
        Action countAssertion)
    {
        using (HttpRequestMessage preflight = new(HttpMethod.Options, "/health"))
        {
            preflight.Headers.Add("Origin", origin);
            preflight.Headers.Add("Access-Control-Request-Method", "GET");
            using HttpResponseMessage response = await client.SendAsync(preflight).ConfigureAwait(false);
            Require(
                !response.Headers.Contains("Access-Control-Allow-Origin"),
                $"{modeLabel} preflight must NOT emit Access-Control-Allow-Origin for '{origin}'.",
                countAssertion);
        }

        using (HttpRequestMessage getRequest = new(HttpMethod.Get, "/health"))
        {
            getRequest.Headers.Add("Origin", origin);
            using HttpResponseMessage response = await client.SendAsync(getRequest).ConfigureAwait(false);
            Require(
                !response.Headers.Contains("Access-Control-Allow-Origin"),
                $"{modeLabel} GET must NOT emit Access-Control-Allow-Origin for '{origin}'.",
                countAssertion);
        }
    }

    private static void AssertCorsAllowed(
        HttpResponseMessage response,
        string expectedOrigin,
        string contextLabel,
        Action countAssertion)
    {
        Require(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? allowOriginValues) &&
            allowOriginValues.Contains(expectedOrigin, StringComparer.Ordinal),
            $"{contextLabel} must emit Access-Control-Allow-Origin: {expectedOrigin}.",
            countAssertion);

        Require(
            response.Headers.TryGetValues("Access-Control-Allow-Credentials", out IEnumerable<string>? allowCredValues) &&
            allowCredValues.Contains("true", StringComparer.OrdinalIgnoreCase),
            $"{contextLabel} must emit Access-Control-Allow-Credentials: true.",
            countAssertion);

        Require(
            response.Headers.TryGetValues("Vary", out IEnumerable<string>? varyValues) &&
            varyValues.Any(v => v.Contains("Origin", StringComparison.OrdinalIgnoreCase)),
            $"{contextLabel} must emit Vary: Origin.",
            countAssertion);
    }

    private static bool TryFindAssignedLanIpv4(out string? lanIp)
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))
                {
                    lanIp = address.Address.ToString();
                    return true;
                }
            }
        }

        lanIp = null;
        return false;
    }

    private static void CleanupDirectory(string root)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int attempt = 0; attempt < 5 && Directory.Exists(root); attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void RequireValidationFailure(Result result, string expectedCode, string message, Action countAssertion)
    {
        Require(
            result.IsFailure && string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal),
            $"{message} Expected '{expectedCode}', got '{result.ErrorDetail.Code}'.",
            countAssertion);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;
        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static string ReadAllSource(
        string root,
        string[] extensions,
        SearchOption searchOption)
    {
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        IEnumerable<string> files = Directory.EnumerateFiles(root, "*.*", searchOption)
            .Where(path => extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

        return string.Join("\n", files.Select(File.ReadAllText));
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Expected source block start missing: " + startMarker);
        }

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Expected source block end missing: " + endMarker);
        }

        return source[start..end];
    }

    private static void Require(bool condition, string message, Action countAssertion)
    {
        countAssertion();
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string text, string value, string message, Action countAssertion)
    {
        countAssertion();
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message, Action countAssertion)
    {
        countAssertion();
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNoForbiddenToken(string text, string token, string message, Action countAssertion)
    {
        countAssertion();
        string pattern = $@"\b{Regex.Escape(token)}\b";
        if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(message);
        }
    }
}

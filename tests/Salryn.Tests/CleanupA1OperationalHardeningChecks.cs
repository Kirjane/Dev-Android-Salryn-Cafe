using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Services;

namespace Salryn.Tests;

public static class CleanupA1OperationalHardeningChecks
{
    private const string NoCacheControl = "no-cache, no-store, must-revalidate";

    public static async Task RunAsync()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        RunSourceGuards(projectRoot);
        AssertSanitizationBehavior();
        await RunRuntimeChecksAsync(projectRoot).ConfigureAwait(false);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string factory = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs");
        string diagnostics = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "WebDistDiagnostics.cs");
        string remoteReadOnlyService = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "Services", "RemoteReadOnlyApiService.cs");
        string remotePosEndpoints = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "RemotePos", "RemotePosApiEndpoints.cs");
        string bridgeDiagnostics = ReadRequiredFile(projectRoot, "src", "Salryn.Api.Local", "Services", "RemoteReadOnlyBridgeDiagnostics.cs");
        string packageJson = ReadRequiredFile(projectRoot, "src", "Salryn.App.Web", "package.json");
        string buildStampScript = ReadRequiredFile(projectRoot, "src", "Salryn.App.Web", "scripts", "write-build-stamp.mjs");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(factory, "WebDistDiagnostics.Inspect(webDistPath)", "Startup must inspect resolved web dist.");
        RequireContains(factory, "WebDistDiagnostics.WriteStartupLog(webDistSnapshot)", "Startup must log web dist diagnostics.");
        RequireContains(factory, "WebDistDiagnostics.ApplyIndexHtmlNoCacheHeaders", "SPA fallback must apply index.html no-cache headers.");
        RequireContains(factory, "/api/system/web-dist", "Loopback-only web dist diagnostic endpoint must be mapped.");
        RequireContains(factory, "LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest", "Web dist diagnostic endpoint must require loopback.");

        RequireContains(diagnostics, "salryn-build-stamp.txt", "Diagnostics must read the frontend build stamp.");
        RequireContains(diagnostics, NoCacheControl, "Diagnostics must define index.html no-cache headers.");
        RequireContains(diagnostics, "Pragma", "Diagnostics must set Pragma for index.html.");
        RequireContains(diagnostics, "Expires", "Diagnostics must set Expires for index.html.");

        RequireContains(remoteReadOnlyService, "SanitizeRemoteClientMessage", "Remote read-only service must sanitize remote-facing errors.");
        RequireContains(remotePosEndpoints, "SanitizeRemoteClientMessage", "Remote POS endpoints must sanitize remote-facing errors.");
        RequireContains(bridgeDiagnostics, "SanitizeRemoteClientMessage", "Bridge diagnostics must expose remote client sanitization.");

        RequireContains(packageJson, "write-build-stamp.mjs", "Frontend build must write a dist build stamp.");
        RequireContains(buildStampScript, "salryn-build-stamp.txt", "Build stamp script must emit salryn-build-stamp.txt.");

        RequireContains(coreSmoke, "CleanupA1OperationalHardeningChecks.RunAsync()", "CoreSmoke must wire CLEANUP-A1 checks.");
    }

    private static void AssertSanitizationBehavior()
    {
        string fallback = "Remote request could not be completed.";
        string sanitized = RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
            "handler failed: System.IO.FileNotFoundException: C:\\secret\\path.db",
            fallback);
        RequireSafeRemoteMessage(
            sanitized,
            fallback,
            "Handler failure details must not leak to remote clients.",
            "handler failed",
            "FileNotFoundException",
            "path.db");

        sanitized = RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
            "InnerException: connection string Data Source=secret.db",
            fallback);
        RequireSafeRemoteMessage(
            sanitized,
            fallback,
            "Inner exception details must not leak to remote clients.",
            "InnerException",
            "Data Source=",
            "secret.db");

        sanitized = RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
            "Remote terminal is pending pairing and cannot read yet.",
            fallback);
        RequireEqual(
            "Remote terminal is pending pairing and cannot read yet.",
            sanitized,
            "Safe operator-facing messages must be preserved for remote clients.");

        string redacted = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText("sqlite connection at Data Source=secret.db")
            ?? string.Empty;
        RequireEqual("[redacted]", redacted, "Secret markers must be redacted from public text.");
    }

    private static async Task RunRuntimeChecksAsync(string projectRoot)
    {
        string distPath = Path.Combine(projectRoot, "src", "Salryn.App.Web", "dist");
        string indexPath = Path.Combine(distPath, "index.html");

        if (!File.Exists(indexPath))
        {
            return;
        }

        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    EnableStaticWebAssets = true,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = ResolveStartedBaseAddress(app);
            using HttpClient loopbackClient = new() { BaseAddress = baseAddress };

            using HttpResponseMessage webDistResponse = await loopbackClient
                .GetAsync("/api/system/web-dist")
                .ConfigureAwait(false);
            RequireEqual(HttpStatusCode.OK, webDistResponse.StatusCode, "Loopback web-dist diagnostic must return 200.");

            string webDistJson = await webDistResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument webDistDocument = JsonDocument.Parse(webDistJson);
            JsonElement root = webDistDocument.RootElement;
            RequireTrue(root.GetProperty("indexHtmlExists").GetBoolean(), "Web-dist diagnostic must report index.html exists.");
            RequireTrue(root.GetProperty("distDirectoryExists").GetBoolean(), "Web-dist diagnostic must report dist exists.");
            RequireNotEmpty(root.GetProperty("distPath").GetString(), "Web-dist diagnostic must include dist path.");

            using HttpRequestMessage remoteDistRequest = new(HttpMethod.Get, "/api/system/web-dist");
            remoteDistRequest.Headers.Add(RemoteReadOnlyBridgeHeaders.TerminalId, Guid.NewGuid().ToString());
            using HttpResponseMessage remoteDistResponse = await loopbackClient
                .SendAsync(remoteDistRequest)
                .ConfigureAwait(false);
            RequireEqual(
                HttpStatusCode.Forbidden,
                remoteDistResponse.StatusCode,
                "Web-dist diagnostic must reject remote bridge header classification.");

            using HttpResponseMessage indexResponse = await loopbackClient.GetAsync("/").ConfigureAwait(false);
            RequireEqual(HttpStatusCode.OK, indexResponse.StatusCode, "SPA root must serve index.html.");
            AssertIndexNoCacheHeaders(indexResponse, "SPA root");

            using HttpResponseMessage fallbackResponse = await loopbackClient
                .GetAsync("/remote-client")
                .ConfigureAwait(false);
            RequireEqual(HttpStatusCode.OK, fallbackResponse.StatusCode, "SPA fallback must serve index.html.");
            AssertIndexNoCacheHeaders(fallbackResponse, "SPA fallback");

            string indexHtml = await File.ReadAllTextAsync(indexPath).ConfigureAwait(false);
            string? assetPath = ExtractFirstAssetPath(indexHtml);
            if (assetPath is not null)
            {
                using HttpResponseMessage assetResponse = await loopbackClient.GetAsync(assetPath).ConfigureAwait(false);
                RequireEqual(HttpStatusCode.OK, assetResponse.StatusCode, "Hashed static asset must still be served.");
                if (assetResponse.Headers.CacheControl is not null)
                {
                    string cacheControl = assetResponse.Headers.CacheControl.ToString();
                    RequireFalse(
                        cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase),
                        "Hashed static assets must not inherit index.html no-store caching.");
                }
            }
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static void AssertIndexNoCacheHeaders(HttpResponseMessage response, string context)
    {
        if (!TryGetHeaderValues(response, "Cache-Control", out IEnumerable<string>? cacheValues))
        {
            throw new InvalidOperationException($"{context} must include Cache-Control header.");
        }

        string cacheControl = string.Join(", ", cacheValues);
        RequireContains(cacheControl, "no-cache", $"{context} Cache-Control must include no-cache.");
        RequireContains(cacheControl, "no-store", $"{context} Cache-Control must include no-store.");
        RequireContains(cacheControl, "must-revalidate", $"{context} Cache-Control must include must-revalidate.");

        RequireTrue(
            TryGetHeaderValues(response, "Pragma", out _),
            $"{context} must include Pragma header.");
        RequireTrue(
            TryGetHeaderValues(response, "Expires", out _),
            $"{context} must include Expires header.");
    }

    private static bool TryGetHeaderValues(
        HttpResponseMessage response,
        string name,
        out IEnumerable<string>? values)
    {
        if (response.Headers.TryGetValues(name, out values))
        {
            return true;
        }

        if (response.Content.Headers.TryGetValues(name, out values))
        {
            return true;
        }

        values = null;
        return false;
    }

    private static string? ExtractFirstAssetPath(string indexHtml)
    {
        const string marker = "src=\"/assets/";
        int start = indexHtml.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += "src=\"".Length;
        int end = indexHtml.IndexOf('"', start);
        return end < 0 ? null : indexHtml[start..end];
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(address) || address.EndsWith(":0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CLEANUP-A1 runtime harness could not resolve started loopback address.");
        }

        return new Uri(address);
    }

    private static string FindProjectRoot(string startPath)
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

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(string root, params string[] pathParts)
    {
        string path = root;
        foreach (string part in pathParts)
        {
            path = Path.Combine(path, part);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotEmpty(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSafeRemoteMessage(
        string sanitized,
        string fallback,
        string message,
        params string[] forbiddenFragments)
    {
        if (!string.Equals(sanitized, fallback, StringComparison.Ordinal)
            && !string.Equals(sanitized, "[redacted]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Expected safe fallback or [redacted], got '{sanitized}'.");
        }

        foreach (string fragment in forbiddenFragments)
        {
            if (sanitized.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{message} Leaked forbidden fragment '{fragment}'.");
            }
        }
    }
}
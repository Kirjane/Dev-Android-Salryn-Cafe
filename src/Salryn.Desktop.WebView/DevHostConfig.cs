namespace Salryn.Desktop.WebView;

/// <summary>
/// Static host configuration for Salryn Cafe Desktop.
/// Targets loopback 127.0.0.1:5399 exclusively (never retail ports 5299 or 5387).
/// </summary>
internal static class DevHostConfig
{
    public const string ProductId = "Salryn.Cafe";
    public const string ProductDisplayName = "Salryn Cafe";
    public const string ProductHeaderName = "X-Salryn-Product-Id";
    public const string DesktopMutexName = @"Global\Kirjane.SalrynCafe.Desktop";

    public const int LoopbackPort = 5399;
    public const int ProtectedRetailLegacyPort = 5299;
    public const int ProtectedRetailPort = 5387;

    public const string ApiBaseUrl = "http://127.0.0.1:5399";
    public const string ApiUrlEnvironmentVariable = "SALRYN_CAFE_API_URL";
    public const string WebUrlEnvironmentVariable = "SALRYN_CAFE_WEB_URL";
    public const string DefaultViteUrl = "http://127.0.0.1:5173";

    public static string ResolveApiBaseUrl(string? envOverride = null)
    {
        string? raw = envOverride ?? Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ApiBaseUrl;
        }

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) ||
            uri.Port <= 0 ||
            uri.Port == ProtectedRetailLegacyPort ||
            uri.Port == ProtectedRetailPort)
        {
            return ApiBaseUrl;
        }

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    public static string HealthUrl => ResolveApiBaseUrl() + "/health";

    public static string DefaultInstalledWebUrl => ResolveApiBaseUrl() + "/";

    public static string GetWebView2UserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SalrynCafe",
            "WebView2");
    }

    /// <summary>
    /// True when this process was installed beside Salryn.Api.Local.exe (installer layout).
    /// </summary>
    public static bool IsInstalledLayout()
    {
        string apiExe = Path.Combine(AppContext.BaseDirectory, "Salryn.Api.Local.exe");
        return File.Exists(apiExe);
    }

    /// <summary>
    /// Resolves the frontend URL to load.
    /// Priority: validated SALRYN_CAFE_WEB_URL override; otherwise API origin for installed layout,
    /// or the Vite dev server for repository/dev layout.
    /// </summary>
    public static string ResolveWebUrl(out string? configurationRejection)
    {
        string? configured = Environment.GetEnvironmentVariable(WebUrlEnvironmentVariable);
        string fallback = IsInstalledLayout() ? DefaultInstalledWebUrl : DefaultViteUrl;

        if (LocalWebOriginPolicy.TryResolveConfiguredWebUrl(configured, fallback, out string resolved, out string? rejection))
        {
            configurationRejection = null;
            return resolved;
        }

        configurationRejection = rejection;
        return resolved;
    }
}

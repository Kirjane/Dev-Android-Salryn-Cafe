namespace Salryn.Desktop.WebView;

/// <summary>
/// Static host configuration. The native host only ever targets localhost.
/// It never binds sockets itself and never exposes anything to the LAN.
/// </summary>
internal static class DevHostConfig
{
    public const string ApiBaseUrl = "http://127.0.0.1:5299";
    public const string HealthUrl = ApiBaseUrl + "/health";

    public const string WebUrlEnvironmentVariable = "SALRYN_WEB_URL";
    public const string DefaultViteUrl = "http://127.0.0.1:5173";
    public const string DefaultInstalledWebUrl = ApiBaseUrl + "/";

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
    /// Priority: validated SALRYN_WEB_URL override; otherwise API origin for installed layout,
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

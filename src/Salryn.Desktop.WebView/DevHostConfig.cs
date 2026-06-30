namespace Salryn.Desktop.WebView;

/// <summary>
/// Static dev-host configuration. The native host only ever targets localhost.
/// It never binds sockets itself and never exposes anything to the LAN.
/// </summary>
internal static class DevHostConfig
{
    public const string ApiBaseUrl = "http://127.0.0.1:5299";
    public const string HealthUrl = ApiBaseUrl + "/health";

    public const string WebUrlEnvironmentVariable = "SALRYN_WEB_URL";
    public const string DefaultViteUrl = "http://127.0.0.1:5173";

    /// <summary>
    /// Resolves the frontend URL to load.
    /// Priority: validated SALRYN_WEB_URL override, otherwise the Vite dev server.
    /// </summary>
    public static string ResolveWebUrl(out string? configurationRejection)
    {
        string? configured = Environment.GetEnvironmentVariable(WebUrlEnvironmentVariable);

        if (LocalWebOriginPolicy.TryResolveConfiguredWebUrl(configured, out string resolved, out string? rejection))
        {
            configurationRejection = null;
            return resolved;
        }

        configurationRejection = rejection;
        return resolved;
    }
}

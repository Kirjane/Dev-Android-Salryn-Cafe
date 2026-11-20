namespace Salryn.Desktop.WebView;

/// <summary>
/// Restricts the Salryn Cafe WebView host to Cafe loopback origins only (ports 5399 / dev 5173).
/// Explicitly blocks protected retail ports (5299, 5387), external hosts, and non-HTTP schemes.
/// </summary>
internal static class LocalWebOriginPolicy
{
    internal static readonly string[] AllowedWebOriginPrefixes =
    [
        "http://127.0.0.1:5173",
        "http://localhost:5173",
        "http://127.0.0.1:5399",
        "http://localhost:5399",
    ];

    public static bool TryResolveConfiguredWebUrl(
        string? configuredUrl,
        string fallbackUrl,
        out string resolvedUrl,
        out string? rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            resolvedUrl = fallbackUrl;
            rejectionReason = null;
            return true;
        }

        string trimmed = configuredUrl.Trim();

        if (IsAllowedLocalWebUrl(trimmed))
        {
            resolvedUrl = trimmed;
            rejectionReason = null;
            return true;
        }

        rejectionReason =
            "SALRYN_CAFE_WEB_URL must be a local http URL on 127.0.0.1 or localhost port 5173 or 5399. " +
            "Retail ports (5299/5387), external sites, file/data/javascript URLs, and LAN addresses are blocked.";

        resolvedUrl = fallbackUrl;
        return false;
    }

    public static bool TryResolveConfiguredWebUrl(
        string? configuredUrl,
        out string resolvedUrl,
        out string? rejectionReason) =>
        TryResolveConfiguredWebUrl(configuredUrl, DevHostConfig.DefaultViteUrl, out resolvedUrl, out rejectionReason);

    public static bool IsAllowedLocalWebUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return IsAllowedLocalUri(uri);
    }

    public static bool IsAllowedNavigationUri(string uriString)
    {
        if (string.Equals(uriString, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (IsBlockedScheme(uri.Scheme))
        {
            return false;
        }

        return IsAllowedLocalUri(uri);
    }

    private static bool IsAllowedLocalUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (uri.Port is not (5173 or 5399))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string origin = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return AllowedWebOriginPrefixes.Any(
            prefix => string.Equals(origin, prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlockedScheme(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, "data", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, "javascript", StringComparison.OrdinalIgnoreCase);
}

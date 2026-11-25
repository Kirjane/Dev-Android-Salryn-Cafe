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

    /// <summary>
    /// Installed Desktop WebView launches the unified production Salryn Cafe R3 shell by default.
    /// Legacy workstation (/cafe/), backoffice (/cafe/backoffice), and isolated central (/cafe/central)
    /// routes remain preserved on the same loopback origin for recovery and explicit navigation.
    /// </summary>
    public static string DefaultInstalledWebUrl => ResolveApiBaseUrl() + "/cafe/r3";

    public static string GetWebView2UserDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SalrynCafe",
            "WebView2");
    }

    /// <summary>
    /// True when running from inside a source checkout containing Salryn.sln.
    /// </summary>
    public static bool IsRepositoryLayout(string? baseDirectory = null)
    {
        return FindRepositoryRoot(baseDirectory ?? AppContext.BaseDirectory) is not null;
    }

    internal static string? FindRepositoryRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Directory.Exists(startPath))
        {
            return null;
        }

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

    /// <summary>
    /// True when this process was installed beside Salryn.Api.Local.exe (installer layout).
    /// </summary>
    public static bool IsInstalledLayout(string? baseDirectory = null)
    {
        string dir = baseDirectory ?? AppContext.BaseDirectory;
        string apiExe = Path.Combine(dir, "Salryn.Api.Local.exe");
        return File.Exists(apiExe);
    }

    /// <summary>
    /// True when executing in an installed package folder rather than a developer source checkout.
    /// An incomplete installation missing Salryn.Api.Local.exe or cafe-dist/index.html must fail closed
    /// instead of falling back to Vite dev mode (:5173).
    /// </summary>
    public static bool IsInstalledPackageContext(string? baseDirectory = null)
    {
        string dir = baseDirectory ?? AppContext.BaseDirectory;
        if (IsInstalledLayout(dir))
        {
            return true;
        }

        if (File.Exists(Path.Combine(dir, "unins000.exe")))
        {
            return true;
        }

        return !IsRepositoryLayout(dir);
    }

    public static bool TryValidateInstalledBundle(string? baseDirectory, out string? failureReason)
    {
        string dir = baseDirectory ?? AppContext.BaseDirectory;
        if (!IsInstalledPackageContext(dir))
        {
            failureReason = null;
            return true;
        }

        List<string> missing = [];
        if (!File.Exists(Path.Combine(dir, "Salryn.Api.Local.exe")))
        {
            missing.Add("Salryn.Api.Local.exe");
        }

        if (!File.Exists(Path.Combine(dir, "cafe-dist", "index.html")))
        {
            missing.Add(Path.Combine("cafe-dist", "index.html"));
        }

        if (missing.Count == 0)
        {
            failureReason = null;
            return true;
        }

        failureReason =
            "Salryn Cafe installation is incomplete and cannot start (missing: " +
            string.Join(", ", missing) +
            "). Re-run Salryn-Cafe-Setup.exe to repair the installation.";
        return false;
    }

    /// <summary>
    /// Resolves the frontend URL to load.
    /// Priority: validated SALRYN_CAFE_WEB_URL override; otherwise API origin /cafe/r3 for installed
    /// layout/package context, or the Vite dev server for repository/dev layout.
    /// </summary>
    public static string ResolveWebUrl(out string? configurationRejection, string? baseDirectory = null)
    {
        string? configured = Environment.GetEnvironmentVariable(WebUrlEnvironmentVariable);
        string fallback = IsInstalledPackageContext(baseDirectory) ? DefaultInstalledWebUrl : DefaultViteUrl;

        if (LocalWebOriginPolicy.TryResolveConfiguredWebUrl(configured, fallback, out string resolved, out string? rejection))
        {
            configurationRejection = null;
            return resolved;
        }

        configurationRejection = rejection;
        return resolved;
    }
}

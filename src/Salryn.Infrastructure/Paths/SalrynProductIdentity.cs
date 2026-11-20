namespace Salryn.Infrastructure.Paths;

/// <summary>
/// Canonical product identity and side-by-side isolation constants for Salryn Cafe.
/// General Merchandise / Retail (Salryn.POS) paths, ports, GUIDs, and mutexes are
/// strictly protected and must never be targeted by Salryn Cafe.
/// </summary>
public static class SalrynProductIdentity
{
    public const string ProductId = "Salryn.Cafe";
    public const string ProductDisplayName = "Salryn Cafe";
    public const string ProductHeaderName = "X-Salryn-Product-Id";

    // Protected Retail / General Merchandise boundaries (never write, bind, or adopt)
    public const string ProtectedRetailProductId = "Salryn.POS";
    public const string ProtectedRetailInstallDir = @"C:\Program Files\Kirjane Information Technology Services";
    public const string ProtectedRetailProgramDataFolderName = "Salryn";
    public const string ProtectedRetailLocalAppDataFolderName = "Salryn";
    public const int ProtectedRetailLegacyPort = 5299;
    public const int ProtectedRetailPort = 5387;

    // Independent Salryn Cafe boundaries (ADR-I1, #3, #74, #81, #82)
    public const string CafeInstallDir = @"C:\Program Files\Kirjane Information Technology Services - Cafe";
    public const string ProgramDataFolderName = "SalrynCafe";
    public const string LocalAppDataFolderName = "SalrynCafe";
    public const string DevelopmentRootFolderName = ".salryn-cafe-dev";
    public const string DatabaseFileName = "salryn-cafe.db";

    public const int LoopbackPort = 5399;
    public const int LanHttpsPort = 5400;
    public const string LoopbackBaseUrl = "http://127.0.0.1:5399";
    public const string ApiUrlEnvironmentVariable = "SALRYN_CAFE_API_URL";
    public const string WebUrlEnvironmentVariable = "SALRYN_CAFE_WEB_URL";

    // Distinct OS single-instance mutexes & installer identity
    public const string DesktopMutexName = @"Global\Kirjane.SalrynCafe.Desktop";
    public const string LocalApiMutexName = @"Global\Kirjane.SalrynCafe.LocalApi";
    public const string InstallerAppGuid = "8F4C2E19-6B3A-4D7E-9A51-3C8E7F2B4D60";
    public const string InstallerBaseFilename = "Salryn-Cafe-Setup";

    public static string GetProgramDataRoot()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, ProgramDataFolderName);
    }

    public static string GetLocalAppDataRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, LocalAppDataFolderName);
    }

    public static string GetLicenseRootDirectory() => Path.Combine(GetLocalAppDataRoot(), "Licensing");

    public static string GetNativePdfReportRootDirectory() => Path.Combine(GetLocalAppDataRoot(), "Reports", "NativePdf");

    public static string GetWebView2UserDataFolder() => Path.Combine(GetLocalAppDataRoot(), "WebView2");

    /// <summary>
    /// Resolves the Cafe loopback API URL from SALRYN_CAFE_API_URL when explicitly set and valid,
    /// otherwise returns <see cref="LoopbackBaseUrl"/> (127.0.0.1:5399). Never falls back to retail ports 5299/5387.
    /// </summary>
    public static string ResolveLoopbackApiBaseUrl(string? envOverride = null)
    {
        string? raw = envOverride ?? Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LoopbackBaseUrl;
        }

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return LoopbackBaseUrl;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return LoopbackBaseUrl;
        }

        if (!string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return LoopbackBaseUrl;
        }

        if (uri.Port <= 0 || uri.Port == ProtectedRetailLegacyPort || uri.Port == ProtectedRetailPort)
        {
            return LoopbackBaseUrl;
        }

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}

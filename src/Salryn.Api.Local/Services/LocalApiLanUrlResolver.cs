using Salryn.Core.Host;
using Salryn.Infrastructure.Paths;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Resolves Kestrel listen URLs for Salryn Cafe:
/// - Loopback (Desktop WebView2 only): http://127.0.0.1:5399 (or explicit loopback override).
/// - LAN listener (opt-in): https://&lt;lan-ip&gt;:5400 (TLS mandatory; no cleartext HTTP on LAN).
/// </summary>
public static class LocalApiLanUrlResolver
{
    public const int DefaultLoopbackPort = SalrynProductIdentity.LoopbackPort;
    public const int DefaultLanHttpsPort = SalrynProductIdentity.LanHttpsPort;

    public static IReadOnlyList<string> ResolveListenUrls(string defaultUrl, LanRemoteTransportConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultUrl);
        ArgumentNullException.ThrowIfNull(config);

        List<string> urls = [defaultUrl];

        if (UsesEphemeralPort(defaultUrl))
        {
            return urls;
        }

        if (!config.RemoteTransportEnabled
            || config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            return urls;
        }

        if (SalrynLanRemoteTransportGate.ValidateConfig(config).IsFailure
            || SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(config.BindAddress))
        {
            return urls;
        }

        string bindAddress = config.BindAddress.Trim();
        if (!LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(bindAddress))
        {
            return urls;
        }

        int lanPort = ResolveLanHttpsPort(config);
        urls.Add($"https://{bindAddress}:{lanPort}");

        return urls;
    }

    public static int ResolveLanHttpsPort(LanRemoteTransportConfig config)
    {
        if (config.Port > 0
            && config.Port != LanRemoteTransportConfig.DefaultPort
            && config.Port != SalrynProductIdentity.LoopbackPort
            && config.Port != SalrynProductIdentity.ProtectedRetailLegacyPort
            && config.Port != SalrynProductIdentity.ProtectedRetailPort)
        {
            return config.Port;
        }

        return DefaultLanHttpsPort;
    }

    private static bool UsesEphemeralPort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.IsDefaultPort is false
            && uri.Port == 0;
    }
}

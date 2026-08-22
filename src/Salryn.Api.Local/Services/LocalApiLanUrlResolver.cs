using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

public static class LocalApiLanUrlResolver
{
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

        int port = ExtractPort(defaultUrl) ?? 5299;
        urls.Add($"http://{bindAddress}:{port}");

        return urls;
    }

    private static int? ExtractPort(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return uri.Port > 0 ? uri.Port : null;
    }

    private static bool UsesEphemeralPort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.IsDefaultPort is false
            && uri.Port == 0;
    }
}

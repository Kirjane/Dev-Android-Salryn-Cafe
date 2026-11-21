using System.Net;
using Salryn.Core.Host;
using Salryn.Infrastructure.Paths;

namespace Salryn.Api.Local.Services;

/// <summary>
/// Separates the host loopback control URL (<c>http://127.0.0.1:5399</c> by default)
/// from advertised LAN HTTPS endpoints (<c>https://&lt;lan-ip&gt;:5400</c> or a valid
/// configured LAN HTTPS port override resolved via <see cref="LocalApiLanUrlResolver"/>).
/// </summary>
public sealed class LocalApiHostControlUrls
{
    public LocalApiHostControlUrls(
        string? localUrl = null,
        int defaultLanHttpsPort = LocalApiLanUrlResolver.DefaultLanHttpsPort)
    {
        LocalUrl = string.IsNullOrWhiteSpace(localUrl)
            ? SalrynProductIdentity.ResolveLoopbackApiBaseUrl()
            : localUrl.Trim();
        LoopbackPort = ExtractPort(LocalUrl) ?? LocalApiLanUrlResolver.DefaultLoopbackPort;
        DefaultLanHttpsPort = NormalizeLanHttpsPort(defaultLanHttpsPort);
    }

    /// <summary>
    /// Host loopback URL used strictly by the local desktop/operator shell.
    /// </summary>
    public string LocalUrl { get; }

    /// <summary>
    /// Loopback HTTP port extracted from <see cref="LocalUrl"/>. Never advertised as a LAN port.
    /// </summary>
    public int LoopbackPort { get; }

    /// <summary>
    /// Default LAN HTTPS port (<c>5400</c>) when no valid custom LAN HTTPS port override is configured.
    /// </summary>
    public int DefaultLanHttpsPort { get; }

    /// <summary>
    /// Default advertised LAN HTTPS port (<see cref="DefaultLanHttpsPort"/>).
    /// </summary>
    public int Port => DefaultLanHttpsPort;

    public int ResolveLanHttpsPort(LanRemoteTransportConfig? config = null)
    {
        return config is null
            ? DefaultLanHttpsPort
            : LocalApiLanUrlResolver.ResolveLanHttpsPort(config);
    }

    public string BuildLanUrl(string bindAddress, LanRemoteTransportConfig? config = null)
    {
        return BuildLanUrl(bindAddress, ResolveLanHttpsPort(config));
    }

    public string BuildLanUrl(string bindAddress, int lanHttpsPort)
    {
        // Always require a bare, non-loopback, non-wildcard IPv4 address.
        // Never prefix a value that already contains a scheme or path.
        string? ip = LocalApiLanBindNormalizer.TryNormalizeOrNull(bindAddress)
            ?? (LocalApiLanBindNormalizer.LooksLikeBareIpv4(bindAddress) ? bindAddress.Trim() : null);

        if (ip is null
            || SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(ip)
            || (IPAddress.TryParse(ip, out IPAddress? parsed) && IPAddress.IsLoopback(parsed)))
        {
            throw new ArgumentException(
                "LAN URL requires a bare non-loopback IPv4 bind address (example: 192.168.1.161).",
                nameof(bindAddress));
        }

        int resolvedPort = NormalizeLanHttpsPort(lanHttpsPort);
        return $"https://{ip}:{resolvedPort}";
    }

    public string BuildClientUrl(string bindAddress, LanRemoteTransportConfig? config = null)
        => $"{BuildLanUrl(bindAddress, config)}/remote-client";

    public string BuildClientUrl(string bindAddress, int lanHttpsPort)
        => $"{BuildLanUrl(bindAddress, lanHttpsPort)}/remote-client";

    private static int NormalizeLanHttpsPort(int candidatePort)
    {
        if (candidatePort > 0
            && candidatePort != LanRemoteTransportConfig.DefaultPort
            && candidatePort != SalrynProductIdentity.LoopbackPort
            && candidatePort != SalrynProductIdentity.ProtectedRetailLegacyPort
            && candidatePort != SalrynProductIdentity.ProtectedRetailPort)
        {
            return candidatePort;
        }

        return LocalApiLanUrlResolver.DefaultLanHttpsPort;
    }

    private static int? ExtractPort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Port > 0
            ? uri.Port
            : null;
    }
}

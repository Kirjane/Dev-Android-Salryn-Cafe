using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Salryn.Api.Local.Contracts;
using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

public sealed class LocalApiLanRuntimeStatusService
{
    private readonly IServiceProvider _services;
    private readonly LocalApiHostControlUrls _hostUrls;

    public LocalApiLanRuntimeStatusService(IServiceProvider services, LocalApiHostControlUrls hostUrls)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _hostUrls = hostUrls ?? throw new ArgumentNullException(nameof(hostUrls));
    }

    public LocalLanControlRuntimeStatusResponse Build(LanRemoteTransportConfig config)
    {
        IReadOnlyList<string> actualListenUrls = ReadActualListenUrls();
        IReadOnlyList<string> detectedLanListenIps = DetectLanListenIps(actualListenUrls);
        IReadOnlyList<string> detectedHostIpv4Addresses = DetectHostIpv4Addresses();
        int port = LocalApiLanUrlResolver.ResolveLanHttpsPort(config);
        string hostLocalUrl = _hostUrls.LocalUrl;
        bool lanTransportEnabled = config.RemoteTransportEnabled
            && config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress;

        string? rawConfigured = null;
        string? normalizedBind = null;
        bool configuredBindMalformed = false;
        string? repairSuggestion = null;

        if (lanTransportEnabled)
        {
            rawConfigured = string.IsNullOrWhiteSpace(config.BindAddress) ? null : config.BindAddress.Trim();
            LocalApiLanBindNormalizer.NormalizeResult normalize =
                LocalApiLanBindNormalizer.NormalizeConfiguredLanBindInput(rawConfigured);

            if (normalize.Ok && !string.IsNullOrWhiteSpace(normalize.Ip))
            {
                normalizedBind = normalize.Ip;
                if (normalize.WasRepaired
                    || !LocalApiLanBindNormalizer.LooksLikeBareIpv4(rawConfigured))
                {
                    configuredBindMalformed = true;
                    repairSuggestion = normalize.Ip;
                }
            }
            else if (!string.IsNullOrWhiteSpace(rawConfigured)
                && !SalrynHostCommandLoopbackRules.IsLoopbackOrigin(rawConfigured)
                && !SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(rawConfigured))
            {
                configuredBindMalformed = true;
                repairSuggestion = detectedLanListenIps.FirstOrDefault() ?? detectedHostIpv4Addresses.FirstOrDefault();
            }
        }

        // Prefer normalized bare IP for truth checks; never use raw URLs for listen matching.
        string? configuredBindAddress = normalizedBind;

        bool bindIpOnHost = configuredBindAddress is not null
            && LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(configuredBindAddress);

        bool listensOnConfiguredLan = configuredBindAddress is not null
            && actualListenUrls.Any(url => UrlMatchesHostAndPort(url, configuredBindAddress, port));

        bool loopbackOnly = lanTransportEnabled
            && configuredBindAddress is not null
            && bindIpOnHost
            && !listensOnConfiguredLan;

        bool restartRequired = lanTransportEnabled
            && configuredBindAddress is not null
            && bindIpOnHost
            && !listensOnConfiguredLan;

        string? clientUrl = null;
        bool clientUrlReady = false;
        string? problem = null;
        string? remediation = null;
        string readinessState = "Disabled";

        if (!lanTransportEnabled)
        {
            readinessState = "Disabled";
            problem = "LanTransportDisabled";
            remediation = "Enable LAN transport with an explicit bind address, then restart Salryn with that LAN bind.";
        }
        else if (configuredBindMalformed && configuredBindAddress is null)
        {
            readinessState = "Misconfigured";
            problem = "ConfiguredBindMalformed";
            string suggestion = repairSuggestion
                ?? detectedLanListenIps.FirstOrDefault()
                ?? detectedHostIpv4Addresses.FirstOrDefault()
                ?? "YOUR_LAN_IP";
            remediation =
                $"Configured LAN address is malformed ({rawConfigured}). Repair to a bare IPv4 such as {suggestion}, then restart if the API is not already listening on that IP.";
            if (string.IsNullOrWhiteSpace(repairSuggestion) && detectedHostIpv4Addresses.Count > 0)
            {
                repairSuggestion = detectedHostIpv4Addresses[0];
            }
        }
        else if (configuredBindAddress is null)
        {
            readinessState = "LoopbackOnly";
            problem = "ExplicitBindMissing";
            remediation = BuildRestartGuidance("the Host PC LAN address");
        }
        else if (configuredBindMalformed)
        {
            // Repairable: we can normalize to a real NIC IP, but stored value is still garbage for humans.
            // ClientUrlReady stays false until a bare IP is persisted — even if Kestrel already listens.
            readinessState = "Misconfigured";
            problem = "ConfiguredBindMalformed";
            remediation =
                $"Configured LAN address is malformed. Repair to {configuredBindAddress}."
                + (listensOnConfiguredLan
                    ? " The API is already listening on that IP; repair will fix host display and future restarts."
                    : " After repair, restart Salryn with the explicit LAN bind if clients still cannot connect.");
            repairSuggestion = configuredBindAddress;
            clientUrlReady = false;
            clientUrl = null;
        }
        else if (!bindIpOnHost)
        {
            readinessState = "BindIpNotOnHost";
            problem = "BindIpNotOnHost";
            remediation = $"Choose a LAN IPv4 address assigned to this host. Current configured bind: {configuredBindAddress}.";
            if (detectedHostIpv4Addresses.Count > 0)
            {
                repairSuggestion = detectedHostIpv4Addresses[0];
                remediation += $" Detected Host PC address: {detectedHostIpv4Addresses[0]}.";
            }
        }
        else if (loopbackOnly)
        {
            readinessState = "LoopbackOnly";
            problem = "ApiLoopbackOnly";
            remediation = BuildRestartGuidance(configuredBindAddress);
            restartRequired = true;
        }
        else
        {
            readinessState = "Ready";
            clientUrl = _hostUrls.BuildClientUrl(configuredBindAddress, config);
            clientUrlReady = true;
        }

        // Keep a real assigned Host PC address available for setup and repair without claiming it is already listening.
        if (repairSuggestion is null && detectedHostIpv4Addresses.Count > 0)
        {
            repairSuggestion = detectedHostIpv4Addresses[0];
        }

        return new LocalLanControlRuntimeStatusResponse(
            ConfiguredBindAddress: configuredBindAddress,
            Port: port,
            ActualListenUrls: actualListenUrls,
            LanTransportEnabled: lanTransportEnabled,
            ClientUrl: clientUrl,
            ClientUrlReady: clientUrlReady,
            ReadinessState: readinessState,
            RestartRequired: restartRequired,
            BindIpOnHost: bindIpOnHost,
            Problem: problem,
            Remediation: remediation,
            HostLocalUrl: hostLocalUrl,
            FirewallGuidance: BuildFirewallGuidance(port),
            ConfiguredBindAddressRaw: rawConfigured,
            ConfiguredBindAddressNormalized: normalizedBind,
            ConfiguredBindAddressRepairSuggestion: repairSuggestion,
            DetectedLanListenIps: detectedLanListenIps,
            ConfiguredBindMalformed: configuredBindMalformed,
            DetectedHostIpv4Addresses: detectedHostIpv4Addresses);
    }

    private IReadOnlyList<string> ReadActualListenUrls()
    {
        IServer server = _services.GetRequiredService<IServer>();
        IServerAddressesFeature? feature = server.Features.Get<IServerAddressesFeature>();
        if (feature?.Addresses is null || feature.Addresses.Count == 0)
        {
            return Array.Empty<string>();
        }

        return feature.Addresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> DetectLanListenIps(IReadOnlyList<string> actualListenUrls)
    {
        List<string> ips = new();
        foreach (string url in actualListenUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IPAddress.TryParse(uri.Host, out IPAddress? parsed)
                || parsed.AddressFamily != AddressFamily.InterNetwork
                || IPAddress.IsLoopback(parsed))
            {
                continue;
            }

            if (!ips.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                ips.Add(uri.Host);
            }
        }

        return ips;
    }

    private static IReadOnlyList<string> DetectHostIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .Where(LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool UrlMatchesHostAndPort(string listenUrl, string host, int port)
    {
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Port == port
            && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRestartGuidance(string bindAddress)
    {
        return $"Restart Salryn after saving {bindAddress}. After restart, wait for LAN status to show Ready before connecting terminal devices.";
    }

    private static string BuildFirewallGuidance(int port)
    {
        return $"Allow Salryn inbound access on TCP port {port} for Private networks. "
            + "Do not enable access on Public networks; ask your network administrator or Salryn support if assistance is needed.";
    }
}

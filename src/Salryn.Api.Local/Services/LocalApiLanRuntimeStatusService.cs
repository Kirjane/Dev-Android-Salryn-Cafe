using System.Net;
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
        int port = _hostUrls.Port;
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
                repairSuggestion = detectedLanListenIps.FirstOrDefault();
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
            string suggestion = repairSuggestion ?? detectedLanListenIps.FirstOrDefault() ?? "YOUR_LAN_IP";
            remediation =
                $"Configured LAN address is malformed ({rawConfigured}). Repair to a bare IPv4 such as {suggestion}, then restart if the API is not already listening on that IP.";
            if (string.IsNullOrWhiteSpace(repairSuggestion) && detectedLanListenIps.Count > 0)
            {
                repairSuggestion = detectedLanListenIps[0];
            }
        }
        else if (configuredBindAddress is null)
        {
            readinessState = "LoopbackOnly";
            problem = "ExplicitBindMissing";
            remediation = BuildRestartCommand("YOUR_LAN_IP", port);
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
            if (detectedLanListenIps.Count > 0)
            {
                repairSuggestion = detectedLanListenIps[0];
                remediation += $" Detected LAN listener IP: {detectedLanListenIps[0]}.";
            }
        }
        else if (loopbackOnly)
        {
            readinessState = "LoopbackOnly";
            problem = "ApiLoopbackOnly";
            remediation = BuildRestartCommand(configuredBindAddress, port);
            restartRequired = true;
        }
        else
        {
            readinessState = "Ready";
            clientUrl = _hostUrls.BuildClientUrl(configuredBindAddress);
            clientUrlReady = true;
        }

        // When listeners include LAN but config is wrong, prefer detected LAN as repair suggestion.
        if (repairSuggestion is null && detectedLanListenIps.Count > 0)
        {
            repairSuggestion = detectedLanListenIps[0];
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
            ConfiguredBindMalformed: configuredBindMalformed);
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
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
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

    private static bool UrlMatchesHostAndPort(string listenUrl, string host, int port)
    {
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Port == port
            && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRestartCommand(string bindAddress, int port)
    {
        return $"Restart with: .\\tools\\dev\\Start-SalrynLanDev.ps1 -BindIp {bindAddress} -Port {port} -StopStale";
    }

    private static string BuildFirewallGuidance(int port)
    {
        return $"If LAN clients still time out, allow inbound TCP {port} on Private profile: "
            + $"New-NetFirewallRule -DisplayName \"Salryn Dev LAN {port}\" -Direction Inbound -Action Allow "
            + $"-Protocol TCP -LocalPort {port} -Profile Private";
    }
}

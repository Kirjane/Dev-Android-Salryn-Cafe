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
        string? configuredBindAddress = ResolveConfiguredBindAddress(config);
        int port = _hostUrls.Port;
        string hostLocalUrl = _hostUrls.LocalUrl;
        bool lanTransportEnabled = config.RemoteTransportEnabled
            && config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress;

        bool bindIpOnHost = configuredBindAddress is not null
            && LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface(configuredBindAddress);

        bool listensOnConfiguredLan = configuredBindAddress is not null
            && actualListenUrls.Any(url => UrlMatchesHostAndPort(url, configuredBindAddress, port));

        bool loopbackOnly = lanTransportEnabled
            && configuredBindAddress is not null
            && !listensOnConfiguredLan;

        bool restartRequired = lanTransportEnabled
            && configuredBindAddress is not null
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
        else if (configuredBindAddress is null)
        {
            readinessState = "LoopbackOnly";
            problem = "ExplicitBindMissing";
            remediation = BuildRestartCommand("YOUR_LAN_IP", port);
        }
        else if (!bindIpOnHost)
        {
            readinessState = "BindIpNotOnHost";
            problem = "BindIpNotOnHost";
            remediation = $"Choose a LAN IPv4 address assigned to this host. Current configured bind: {configuredBindAddress}.";
        }
        else if (loopbackOnly)
        {
            readinessState = "LoopbackOnly";
            problem = "ApiLoopbackOnly";
            remediation = BuildRestartCommand(configuredBindAddress, port);
        }
        else
        {
            readinessState = "Ready";
            clientUrl = _hostUrls.BuildClientUrl(configuredBindAddress);
            clientUrlReady = true;
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
            FirewallGuidance: BuildFirewallGuidance(port));
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

    private static string? ResolveConfiguredBindAddress(LanRemoteTransportConfig config)
    {
        if (!config.RemoteTransportEnabled
            || config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            return null;
        }

        string bind = config.BindAddress.Trim();
        if (SalrynHostCommandLoopbackRules.IsLoopbackOrigin(bind)
            || SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(bind))
        {
            return null;
        }

        return bind;
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
        return $"Restart with: dotnet run --project .\\src\\Salryn.Api.Local\\Salryn.Api.Local.csproj -- --dev --lan-bind {bindAddress} --port {port}";
    }

    private static string BuildFirewallGuidance(int port)
    {
        return $"If LAN clients still time out, allow inbound TCP {port} on Private profile: "
            + $"New-NetFirewallRule -DisplayName \"Salryn Dev LAN {port}\" -Direction Inbound -Action Allow "
            + $"-Protocol TCP -LocalPort {port} -Profile Private";
    }
}
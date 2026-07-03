using Salryn.Api.Local.Contracts;
using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

public static class RemoteReadOnlyLanBindPolicyMapper
{
    public const string LocalhostOnlyMode = "LocalhostOnly";

    public const string LocalOnlyReadMode = "LocalOnlyReadMode";

    public const string ExplicitLanAddressMode = "ExplicitLanAddress";

    public const string HybridBridgeReadyMode = "HybridBridgeReady";

    public static RemoteReadOnlyBindPolicyResponse Map(LanRemoteTransportConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        bool remoteEnabled = config.RemoteTransportEnabled
            && config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress;

        string effectiveMode = remoteEnabled
            ? ExplicitLanAddressMode
            : LocalhostOnlyMode;

        string message = remoteEnabled
            ? $"Api.Local listens on 127.0.0.1 and explicit LAN address {config.BindAddress.Trim()} for hybrid bridge reads. Wildcard bind remains forbidden."
            : "Api.Local remains bound to 127.0.0.1. LAN host bind requires explicit future configuration and must never use wildcard bind.";

        return new RemoteReadOnlyBindPolicyResponse(
            DefaultMode: LocalhostOnlyMode,
            EffectiveMode: effectiveMode,
            DisabledByDefault: !config.RemoteTransportEnabled,
            LocalhostOnly: !remoteEnabled,
            ExplicitHostBindRequired: true,
            SpecificIpOnly: true,
            NoWildcardBind: true,
            NoDiscovery: true,
            Message: message);
    }

    public static string ResolveConnectionMode(LanRemoteTransportConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.RemoteTransportEnabled)
        {
            return LocalOnlyReadMode;
        }

        return config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress
            ? HybridBridgeReadyMode
            : LocalOnlyReadMode;
    }
}

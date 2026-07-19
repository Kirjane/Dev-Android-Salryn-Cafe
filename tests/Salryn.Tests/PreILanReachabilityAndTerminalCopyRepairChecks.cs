using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Salryn.Api.Local.Services;
using Salryn.Core.Host;

namespace Salryn.Tests;

/// <summary>
/// PRE-I proof that terminal links follow the running listener, not merely saved LAN configuration.
/// Combines runtime listener cases with source guards for copy, restart, and client-facing wording.
/// </summary>
public static class PreILanReachabilityAndTerminalCopyRepairChecks
{
    private const int Port = 5299;

    public static void Run()
    {
        RuntimeListenerTruth();
        SourceContract();
    }

    private static void RuntimeListenerTruth()
    {
        string hostIp = FindAssignedLanIpv4();
        LanRemoteTransportConfig enabled = CreateConfig(hostIp, enabled: true);

        LocalApiLanRuntimeStatusService loopbackOnly = CreateRuntime("http://127.0.0.1:5299");
        var restart = loopbackOnly.Build(enabled);
        RequireEqual("LoopbackOnly", restart.ReadinessState, "Saved LAN bind without a matching listener must not be ready.");
        RequireTrue(restart.RestartRequired, "Saved LAN bind without a matching listener must require restart.");
        RequireTrue(!restart.ClientUrlReady, "Restart-required state must not expose a ready terminal link.");
        RequireTrue(restart.ClientUrl is null, "Restart-required state must not expose a terminal URL.");
        RequireTrue(restart.DetectedHostIpv4Addresses?.Contains(hostIp, StringComparer.OrdinalIgnoreCase) == true,
            "Assigned Host PC address must be detected independently of active LAN listeners.");

        LocalApiLanRuntimeStatusService wrongPort = CreateRuntime(
            "http://127.0.0.1:5299",
            $"http://{hostIp}:5300");
        var portMismatch = wrongPort.Build(enabled);
        RequireTrue(portMismatch.RestartRequired && !portMismatch.ClientUrlReady,
            "Listener host and port must both match before terminal copy becomes ready.");

        LocalApiLanRuntimeStatusService reachable = CreateRuntime(
            "http://127.0.0.1:5299",
            $"http://{hostIp}:5299");
        var ready = reachable.Build(enabled);
        RequireEqual("Ready", ready.ReadinessState, "Matching running listener must report Ready.");
        RequireTrue(!ready.RestartRequired, "Matching running listener must not require restart.");
        RequireTrue(ready.ClientUrlReady, "Matching running listener must expose a copy-ready terminal link.");
        RequireEqual($"http://{hostIp}:5299/remote-client", ready.ClientUrl, "Ready terminal link must use the actual matching bind.");

        var disabled = reachable.Build(CreateConfig(hostIp, enabled: false));
        RequireEqual("Disabled", disabled.ReadinessState, "Disabled LAN transport must remain disabled even when a listener is present.");
        RequireTrue(!disabled.ClientUrlReady && disabled.ClientUrl is null,
            "Disabled LAN transport must not expose a terminal link.");
    }

    private static void SourceContract()
    {
        string root = FindRoot();
        string page = Read(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx");
        string runtime = Read(root, "src", "Salryn.Api.Local", "Services", "LocalApiLanRuntimeStatusService.cs");
        string coreSmoke = Read(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(page, "const terminalLink = runtime?.clientUrlReady && runtime.clientUrl ? runtime.clientUrl : \"\";",
            "Terminal link must come only from runtime-confirmed reachability.");
        RequireContains(page, "const canCopyTerminalLink = Boolean(runtime?.clientUrlReady && runtime.clientUrl);",
            "Copy action must use the same runtime-confirmed readiness.");
        RequireContains(page, "disabled={!canCopyTerminalLink || busy}", "Copy button must remain disabled until runtime readiness.");
        RequireNotContains(page, "`http://${terminalIp}:${runtime.port}/remote-client`",
            "UI must not invent a terminal link from saved configuration.");
        RequireNotContains(page, "readiness !== \"Ready\" && terminalLink",
            "UI must not show a terminal link before runtime readiness.");

        RequireContains(page, "const refreshed = await runtimeStatusQuery.refetch();",
            "Save success must refresh runtime truth before notifying the operator.");
        RequireContains(page, "Restart Salryn to make the saved LAN address reachable",
            "Save success must state restart when the listener is not ready.");
        RequireContains(page, "Restart Salryn before connecting terminals", "Restart-required page guidance must be explicit.");
        RequireContains(page, "case \"LoopbackOnly\":\n      return \"Action needed\";",
            "Summary badge must not duplicate the Restart required value.");
        RequireNotContains(page, "runtime?.restartRequired ? <StatusPill",
            "Restart-required state must not render a duplicate status pill.");

        RequireContains(page, "recommendedHostIp || hostPcAddress",
            "A detected or valid saved host IP must suppress the not-detected warning.");
        RequireContains(page, "runtime?.detectedHostIpv4Addresses ?? runtime?.detectedLanListenIps",
            "Host address selection must prefer assigned Host PC addresses over listener-only compatibility data.");
        RequireContains(page, "Windows Firewall help", "Firewall guidance must use client-facing wording.");
        RequireContains(page, "Advanced diagnostics", "Technical diagnostics must remain collapsed under Advanced.");
        RequireOrder(page, "Advanced diagnostics", "LAN server is actually listening on",
            "Actual listener diagnostics must remain under Advanced.");
        RequireOrder(page, "Advanced diagnostics", "Firewall detail:",
            "Technical firewall detail must remain under Advanced.");

        foreach (string forbidden in new[]
        {
            "phone is stuck", "new build", "DB reset", "Salryn Dev", "New-NetFirewallRule",
            "tools\\dev", "-BindIp", "Restart with:"
        })
        {
            RequireNotContains(page, forbidden, "Terminal Management must not expose development notes.");
            RequireNotContains(runtime, forbidden, "LAN runtime guidance must not expose development commands or labels.");
        }

        RequireContains(runtime, "UrlMatchesHostAndPort(url, configuredBindAddress, port)",
            "Runtime readiness must match the configured host and port against actual listeners.");
        RequireContains(runtime, "DetectHostIpv4Addresses()",
            "Runtime status must detect assigned Host PC addresses independently from active listeners.");
        RequireContains(coreSmoke, "PreILanReachabilityAndTerminalCopyRepairChecks.Run();",
            "Full CoreSmoke registration is required.");
        RequireContains(coreSmoke, "pre-i-lan-reachability-and-terminal-copy-repair",
            "Focused CoreSmoke registration is required.");
    }

    private static LocalApiLanRuntimeStatusService CreateRuntime(params string[] listenUrls)
    {
        FakeServer server = new(listenUrls);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IServer>(server)
            .BuildServiceProvider();
        return new LocalApiLanRuntimeStatusService(services, new LocalApiHostControlUrls($"http://127.0.0.1:{Port}"));
    }

    private static LanRemoteTransportConfig CreateConfig(string hostIp, bool enabled) => new(
        RemoteTransportEnabled: enabled,
        BindMode: enabled ? LanRemoteTransportBindMode.ExplicitLanAddress : LanRemoteTransportBindMode.LoopbackOnly,
        BindAddress: hostIp,
        Port: Port,
        RequirePairedTerminal: true,
        RequireActiveTerminal: true,
        RejectUnknownTerminals: true,
        AllowOfficialCommandsRemotely: false,
        AllowAdminCommandsRemotely: false,
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedByUserId: null);

    private static string FindAssignedLanIpv4()
    {
        string? ip = Dns.GetHostAddresses(Dns.GetHostName())
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .FirstOrDefault(LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface);
        return ip ?? throw new InvalidOperationException("LAN reachability runtime proof requires an assigned non-loopback IPv4 address.");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal)) throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireOrder(string source, string first, string second, string message)
    {
        int firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        if (firstIndex < 0 || secondIndex <= firstIndex) throw new InvalidOperationException(message);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}.");
    }

    private sealed class FakeServer : IServer
    {
        public FakeServer(IEnumerable<string> addresses)
        {
            ServerAddressesFeature feature = new();
            foreach (string address in addresses) feature.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}

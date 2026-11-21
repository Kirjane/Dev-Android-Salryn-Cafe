using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Salryn.Api.Local;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Host;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.Tests;

public sealed class LocalApiIdentityAndInstallerTests
{
    [Fact]
    public void LanUrlResolver_DefaultsToHttpsPort5400_AndNeverUsesRetailPorts()
    {
        var defaultConfig = LanRemoteTransportConfig.CreateDefault();
        Assert.Equal(5400, LocalApiLanUrlResolver.ResolveLanHttpsPort(defaultConfig));

        var retailPortConfig = defaultConfig with { Port = 5299 };
        Assert.Equal(5400, LocalApiLanUrlResolver.ResolveLanHttpsPort(retailPortConfig));

        var protectedPortConfig = defaultConfig with { Port = 5387 };
        Assert.Equal(5400, LocalApiLanUrlResolver.ResolveLanHttpsPort(protectedPortConfig));

        var loopbackPortConfig = defaultConfig with { Port = 5399 };
        Assert.Equal(5400, LocalApiLanUrlResolver.ResolveLanHttpsPort(loopbackPortConfig));

        var customPortConfig = defaultConfig with { Port = 5444 };
        Assert.Equal(5444, LocalApiLanUrlResolver.ResolveLanHttpsPort(customPortConfig));
    }

    [Fact]
    public void HostControlUrls_SeparatesLoopback5399FromAdvertisedLanHttps5400_AndHonorsValidPortOverrides()
    {
        var urls = new LocalApiHostControlUrls("http://127.0.0.1:5399");
        Assert.Equal("http://127.0.0.1:5399", urls.LocalUrl);
        Assert.Equal(5399, urls.LoopbackPort);
        Assert.Equal(5400, urls.DefaultLanHttpsPort);
        Assert.Equal(5400, urls.Port);

        var defaultUrls = new LocalApiHostControlUrls();
        Assert.Equal("http://127.0.0.1:5399", defaultUrls.LocalUrl);
        Assert.Equal(5399, defaultUrls.LoopbackPort);
        Assert.Equal(5400, defaultUrls.Port);

        Assert.Equal("https://192.168.1.25:5400", urls.BuildLanUrl("192.168.1.25"));
        Assert.Equal("https://192.168.1.25:5400/remote-client", urls.BuildClientUrl("192.168.1.25"));

        // Legacy retail ports (5299, 5387) and Cafe loopback port (5399) normalize to 5400
        var baseConfig = LanRemoteTransportConfig.CreateDefault();
        foreach (int rejectedPort in new[] { 5299, 5387, 5399, 0, -1 })
        {
            var cfg = baseConfig with { Port = rejectedPort };
            Assert.Equal("https://192.168.1.25:5400", urls.BuildLanUrl("192.168.1.25", cfg));
            Assert.Equal("https://192.168.1.25:5400/remote-client", urls.BuildClientUrl("192.168.1.25", cfg));
        }

        // Valid custom LAN HTTPS port override is preserved
        var overrideCfg = baseConfig with { Port = 5444 };
        Assert.Equal("https://192.168.1.25:5444", urls.BuildLanUrl("192.168.1.25", overrideCfg));
        Assert.Equal("https://192.168.1.25:5444/remote-client", urls.BuildClientUrl("192.168.1.25", overrideCfg));

        // Scheme-prefixed input is normalized to bare IPv4 + HTTPS LAN port (never double-prefixed)
        Assert.Equal("https://192.168.1.25:5400", urls.BuildLanUrl("http://192.168.1.25:5299/remote-client"));
        Assert.Equal("https://192.168.1.25:5400/remote-client", urls.BuildClientUrl("http://192.168.1.25:5299"));

        // Loopback, wildcard, and non-IPv4 bind strings are rejected
        Assert.Throws<ArgumentException>(() => urls.BuildLanUrl("127.0.0.1"));
        Assert.Throws<ArgumentException>(() => urls.BuildLanUrl("0.0.0.0"));
        Assert.Throws<ArgumentException>(() => urls.BuildLanUrl("not-an-ipv4-address"));
        Assert.Throws<ArgumentException>(() => urls.BuildClientUrl(""));
    }

    [Fact]
    public void LanRuntimeStatus_AdvertisesRemoteClientOnlyWhenMatchingHttpsListenerIsActive()
    {
        string? hostIp = FindAssignedLanIpv4OrNull();
        if (hostIp is null)
        {
            // No non-loopback NIC on runner; verify disabled and unassigned-IP cases deterministically.
            LocalApiLanRuntimeStatusService noNicSvc = CreateRuntimeStatusService("http://127.0.0.1:5399");
            var unassignedStatus = noNicSvc.Build(CreateLanConfig("198.51.100.42", enabled: true));
            Assert.Equal("BindIpNotOnHost", unassignedStatus.ReadinessState);
            Assert.False(unassignedStatus.ClientUrlReady);
            Assert.Null(unassignedStatus.ClientUrl);
            return;
        }

        LanRemoteTransportConfig enabledDefault = CreateLanConfig(hostIp, enabled: true, port: 5299);

        // 1. Loopback-only / Restart-required
        var loopbackOnly = CreateRuntimeStatusService("http://127.0.0.1:5399").Build(enabledDefault);
        Assert.Equal("LoopbackOnly", loopbackOnly.ReadinessState);
        Assert.Equal("ApiLoopbackOnly", loopbackOnly.Problem);
        Assert.True(loopbackOnly.RestartRequired);
        Assert.False(loopbackOnly.ClientUrlReady);
        Assert.Null(loopbackOnly.ClientUrl);
        Assert.Equal(5400, loopbackOnly.Port);
        Assert.Equal("http://127.0.0.1:5399", loopbackOnly.HostLocalUrl);

        // 2. Negative: Cleartext HTTP listener on configured LAN IP:5400 MUST NEVER report Ready
        var cleartextHttp = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"http://{hostIp}:5400").Build(enabledDefault);
        Assert.Equal("LoopbackOnly", cleartextHttp.ReadinessState);
        Assert.True(cleartextHttp.RestartRequired);
        Assert.False(cleartextHttp.ClientUrlReady);
        Assert.Null(cleartextHttp.ClientUrl);
        Assert.Empty(cleartextHttp.DetectedLanListenIps ?? []);

        // 3. Negative: Wrong-port HTTPS listeners (:5299, :5399, :5401) MUST NEVER report Ready
        foreach (int wrongPort in new[] { 5299, 5399, 5401 })
        {
            var wrongPortStatus = CreateRuntimeStatusService(
                "http://127.0.0.1:5399",
                $"https://{hostIp}:{wrongPort}").Build(enabledDefault);
            Assert.Equal("LoopbackOnly", wrongPortStatus.ReadinessState);
            Assert.True(wrongPortStatus.RestartRequired);
            Assert.False(wrongPortStatus.ClientUrlReady);
            Assert.Null(wrongPortStatus.ClientUrl);
        }

        // 4. Negative: Wrong-interface / unassigned IP (TEST-NET-2 198.51.100.42)
        var wrongInterface = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"https://{hostIp}:5400").Build(CreateLanConfig("198.51.100.42", enabled: true));
        Assert.Equal("BindIpNotOnHost", wrongInterface.ReadinessState);
        Assert.Equal("BindIpNotOnHost", wrongInterface.Problem);
        Assert.False(wrongInterface.BindIpOnHost);
        Assert.False(wrongInterface.ClientUrlReady);
        Assert.Null(wrongInterface.ClientUrl);

        // 5. Positive: Active HTTPS listener on default 5400
        var readyDefault = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"https://{hostIp}:5400").Build(enabledDefault);
        Assert.Equal("Ready", readyDefault.ReadinessState);
        Assert.False(readyDefault.RestartRequired);
        Assert.True(readyDefault.ClientUrlReady);
        Assert.Equal(5400, readyDefault.Port);
        Assert.Equal($"https://{hostIp}:5400/remote-client", readyDefault.ClientUrl);
        Assert.Equal("http://127.0.0.1:5399", readyDefault.HostLocalUrl);

        // 6. Positive & Negative: Custom LAN HTTPS port override (5444)
        LanRemoteTransportConfig customPortConfig = CreateLanConfig(hostIp, enabled: true, port: 5444);
        var overrideMismatch = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"https://{hostIp}:5400").Build(customPortConfig);
        Assert.Equal("LoopbackOnly", overrideMismatch.ReadinessState);
        Assert.True(overrideMismatch.RestartRequired);
        Assert.False(overrideMismatch.ClientUrlReady);
        Assert.Null(overrideMismatch.ClientUrl);
        Assert.Equal(5444, overrideMismatch.Port);

        var overrideReady = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"https://{hostIp}:5444").Build(customPortConfig);
        Assert.Equal("Ready", overrideReady.ReadinessState);
        Assert.False(overrideReady.RestartRequired);
        Assert.True(overrideReady.ClientUrlReady);
        Assert.Equal(5444, overrideReady.Port);
        Assert.Equal($"https://{hostIp}:5444/remote-client", overrideReady.ClientUrl);

        // 7. Disabled config even when HTTPS listener is present
        var disabledStatus = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"https://{hostIp}:5400").Build(CreateLanConfig(hostIp, enabled: false));
        Assert.Equal("Disabled", disabledStatus.ReadinessState);
        Assert.Equal("LanTransportDisabled", disabledStatus.Problem);
        Assert.False(disabledStatus.ClientUrlReady);
        Assert.Null(disabledStatus.ClientUrl);

        // 8. Misconfigured (malformed URL stored in BindAddress) even when HTTPS listener is active
        var malformedConfig = CreateLanConfig($"http://{hostIp}:5299/remote-client", enabled: true);
        var malformedStatus = CreateRuntimeStatusService(
            "http://127.0.0.1:5399",
            $"https://{hostIp}:5400").Build(malformedConfig);
        Assert.Equal("Misconfigured", malformedStatus.ReadinessState);
        Assert.Equal("ConfiguredBindMalformed", malformedStatus.Problem);
        Assert.True(malformedStatus.ConfiguredBindMalformed);
        Assert.Equal(hostIp, malformedStatus.ConfiguredBindAddressRepairSuggestion);
        Assert.False(malformedStatus.ClientUrlReady);
        Assert.Null(malformedStatus.ClientUrl);
    }

    [Fact]
    public async Task LanControlService_AdvertisesHostLanUrlOnlyWhenHttpsListenerIsActiveAndReady()
    {
        string? hostIp = FindAssignedLanIpv4OrNull();
        if (hostIp is null)
        {
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-cafe-r3a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            DbPaths dbPaths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(dbPaths);
            await composition.InitializeDatabaseAsync();

            var lanServices = Salryn.Api.Local.Composition.LanRemoteTransportApiComposition.Create(composition);
            var runtimeGate = new Salryn.Api.Local.RemotePos.RemotePosRuntimeGate();
            var remotePosServices = Salryn.Api.Local.RemotePos.RemotePosApiServices.Create(composition, lanServices, runtimeGate);
            var hostUrls = new LocalApiHostControlUrls("http://127.0.0.1:5399");
            var actor = new Salryn.Core.TerminalRegistry.TerminalRegistryActorContext(
                Guid.NewGuid(),
                ["StoreTerminal.View", "StoreTerminal.Manage", "Settings.Manage"]);

            // 1. Default disabled state
            var svcLoopback = new LocalLanControlService(
                lanServices, runtimeGate, remotePosServices, hostUrls,
                CreateRuntimeStatusService("http://127.0.0.1:5399"));
            var disabledStatus = await svcLoopback.GetStatusAsync(actor);
            Assert.Equal("http://127.0.0.1:5399", disabledStatus.HostLocalUrl);
            Assert.Equal("Not enabled", disabledStatus.HostLanUrl);

            // 2. Enable LAN in config while Kestrel is still loopback-only -> HostLanUrl must NOT advertise Ready URL
            var enableRes = await svcLoopback.SetRemoteTransportAsync(enable: true, bindAddress: hostIp, actor);
            Assert.True(enableRes.IsSuccess);
            Assert.Equal("Not active (restart required for HTTPS LAN listener)", enableRes.Value.HostLanUrl);

            // 3. Cleartext HTTP listener on LAN IP:5400 -> still not active
            var svcCleartext = new LocalLanControlService(
                lanServices, runtimeGate, remotePosServices, hostUrls,
                CreateRuntimeStatusService("http://127.0.0.1:5399", $"http://{hostIp}:5400"));
            var cleartextStatus = await svcCleartext.GetStatusAsync(actor);
            Assert.Equal("Not active (restart required for HTTPS LAN listener)", cleartextStatus.HostLanUrl);

            // 4. Active HTTPS listener on LAN IP:5400 -> advertises https://<hostIp>:5400
            var svcHttpsReady = new LocalLanControlService(
                lanServices, runtimeGate, remotePosServices, hostUrls,
                CreateRuntimeStatusService("http://127.0.0.1:5399", $"https://{hostIp}:5400"));
            var readyStatus = await svcHttpsReady.GetStatusAsync(actor);
            Assert.Equal($"https://{hostIp}:5400", readyStatus.HostLanUrl);
            var readyRuntime = await svcHttpsReady.GetRuntimeStatusAsync();
            Assert.Equal("Ready", readyRuntime.ReadinessState);
            Assert.Equal($"https://{hostIp}:5400/remote-client", readyRuntime.ClientUrl);

            // 5. Custom LAN HTTPS port override (5444) persisted -> requires https://<hostIp>:5444 listener
            await lanServices.Config.SaveAsync(new LanRemoteTransportConfigUpdateRequest(
                RemoteTransportEnabled: true,
                BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
                BindAddress: hostIp,
                Port: 5444,
                RequirePairedTerminal: true,
                RequireActiveTerminal: true,
                RejectUnknownTerminals: true,
                AllowOfficialCommandsRemotely: false,
                AllowAdminCommandsRemotely: false,
                UpdatedByUserId: actor.ActorUserId));
            var mismatch5400 = await svcHttpsReady.GetStatusAsync(actor);
            Assert.Equal("Not active (restart required for HTTPS LAN listener)", mismatch5400.HostLanUrl);

            var svcHttps5444 = new LocalLanControlService(
                lanServices, runtimeGate, remotePosServices, hostUrls,
                CreateRuntimeStatusService("http://127.0.0.1:5399", $"https://{hostIp}:5444"));
            var ready5444 = await svcHttps5444.GetStatusAsync(actor);
            Assert.Equal($"https://{hostIp}:5444", ready5444.HostLanUrl);
            var runtime5444 = await svcHttps5444.GetRuntimeStatusAsync();
            Assert.Equal("Ready", runtime5444.ReadinessState);
            Assert.Equal(5444, runtime5444.Port);
            Assert.Equal($"https://{hostIp}:5444/remote-client", runtime5444.ClientUrl);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task LocalApi_ExposesCafeIdentity_RejectsWrongProductHeader_AndDisablesDevCorsInProduction()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-cafe-api-i1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string? priorEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            DbPaths dbPaths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(dbPaths);

            WebApplication app = await LocalApiApplicationFactory.CreateAsync(
                args: [],
                options: new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                });

            await app.StartAsync();
            try
            {
                string baseUrl = app.Urls.First();
                using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

                // 1. /health returns ProductId=Salryn.Cafe + header
                using HttpResponseMessage healthResp = await client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
                Assert.Equal("Salryn.Cafe", healthResp.Headers.GetValues(SalrynProductIdentity.ProductHeaderName).Single());

                using JsonDocument healthJson = JsonDocument.Parse(await healthResp.Content.ReadAsStringAsync());
                Assert.Equal("Salryn.Cafe", healthJson.RootElement.GetProperty("productId").GetString());
                Assert.Equal(5399, healthJson.RootElement.GetProperty("loopbackPort").GetInt32());
                Assert.Equal(5400, healthJson.RootElement.GetProperty("lanHttpsPort").GetInt32());

                // 2. /api/system/identity returns isolated Cafe metadata
                using HttpResponseMessage idResp = await client.GetAsync("/api/system/identity");
                Assert.Equal(HttpStatusCode.OK, idResp.StatusCode);
                using JsonDocument idJson = JsonDocument.Parse(await idResp.Content.ReadAsStringAsync());
                Assert.Equal("Salryn.Cafe", idJson.RootElement.GetProperty("productId").GetString());
                Assert.Equal(@"Global\Kirjane.SalrynCafe.Desktop", idJson.RootElement.GetProperty("desktopMutex").GetString());

                // 3. Wrong X-Salryn-Product-Id header fails closed with 403
                using var wrongReq = new HttpRequestMessage(HttpMethod.Get, "/health");
                wrongReq.Headers.Add(SalrynProductIdentity.ProductHeaderName, "Salryn.POS");
                using HttpResponseMessage wrongResp = await client.SendAsync(wrongReq);
                Assert.Equal(HttpStatusCode.Forbidden, wrongResp.StatusCode);

                // 4. Production mode does NOT allow :5173 CORS origin
                using var corsReq = new HttpRequestMessage(HttpMethod.Get, "/health");
                corsReq.Headers.Add("Origin", "http://127.0.0.1:5173");
                using HttpResponseMessage corsResp = await client.SendAsync(corsReq);
                Assert.False(corsResp.Headers.Contains("Access-Control-Allow-Origin"));
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", priorEnv);
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // SQLite connection pool may briefly hold file handle on Windows; safe to ignore in temp.
            }
        }
    }

    [Fact]
    public void SalrynCafeSetupIss_EnforcesIndependentGuidInstallDirProgramDataAndRetailGuard()
    {
        string repoRoot = FindRepoRoot();
        string issPath = Path.Combine(repoRoot, "installer", "SalrynCafeSetup.iss");
        Assert.True(File.Exists(issPath), $"Missing {issPath}");

        string iss = File.ReadAllText(issPath);
        Assert.Contains("8F4C2E19-6B3A-4D7E-9A51-3C8E7F2B4D60", iss, StringComparison.Ordinal);
        Assert.DoesNotContain("A4E2B8C1-7D3F-4A91-9C2E-5F6A8B1C0D3E", iss, StringComparison.Ordinal);
        Assert.Contains(@"DefaultDirName={autopf}\Kirjane Information Technology Services - Cafe", iss, StringComparison.Ordinal);
        Assert.Contains("OutputBaseFilename=Salryn-Cafe-Setup", iss, StringComparison.Ordinal);
        Assert.Contains(@"{commonappdata}\SalrynCafe", iss, StringComparison.Ordinal);
        Assert.DoesNotContain(@"{commonappdata}\Salryn\", iss, StringComparison.Ordinal);
        Assert.Contains(@"AppMutex=Global\Kirjane.SalrynCafe.Desktop,Global\Kirjane.SalrynCafe.LocalApi", iss, StringComparison.Ordinal);
        Assert.Contains("IsProtectedRetailInstallPath", iss, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salryn.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root containing Salryn.sln.");
    }

    private static LocalApiLanRuntimeStatusService CreateRuntimeStatusService(params string[] listenUrls)
    {
        FakeServer server = new(listenUrls);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IServer>(server)
            .BuildServiceProvider();
        return new LocalApiLanRuntimeStatusService(services, new LocalApiHostControlUrls("http://127.0.0.1:5399"));
    }

    private static LanRemoteTransportConfig CreateLanConfig(string bindAddress, bool enabled, int port = 5299) => new(
        RemoteTransportEnabled: enabled,
        BindMode: enabled ? LanRemoteTransportBindMode.ExplicitLanAddress : LanRemoteTransportBindMode.LoopbackOnly,
        BindAddress: bindAddress,
        Port: port,
        RequirePairedTerminal: true,
        RequireActiveTerminal: true,
        RejectUnknownTerminals: true,
        AllowOfficialCommandsRemotely: false,
        AllowAdminCommandsRemotely: false,
        UpdatedAtUtc: DateTimeOffset.UtcNow,
        UpdatedByUserId: null);

    private static string? FindAssignedLanIpv4OrNull()
    {
        return Dns.GetHostAddresses(Dns.GetHostName())
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .FirstOrDefault(LocalApiNetworkInterfaceValidator.IsAssignedToLocalInterface);
    }

    private sealed class FakeServer : IServer
    {
        public FakeServer(IEnumerable<string> urls)
        {
            ServerAddressesFeature addresses = new();
            foreach (string url in urls)
            {
                addresses.Addresses.Add(url);
            }

            Features.Set<IServerAddressesFeature>(addresses);
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

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
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

        var customPortConfig = defaultConfig with { Port = 5444 };
        Assert.Equal(5444, LocalApiLanUrlResolver.ResolveLanHttpsPort(customPortConfig));
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
}

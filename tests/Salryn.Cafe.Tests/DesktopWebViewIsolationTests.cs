using Salryn.Desktop.WebView;
using Xunit;

namespace Salryn.Cafe.Tests;

public sealed class DesktopWebViewIsolationTests
{
    [Fact]
    public void DevHostConfig_UsesIsolatedCafePortMutexAndWebViewProfile()
    {
        Assert.Equal("Salryn.Cafe", DevHostConfig.ProductId);
        Assert.Equal("http://127.0.0.1:5399", DevHostConfig.ApiBaseUrl);
        Assert.Equal(@"Global\Kirjane.SalrynCafe.Desktop", DevHostConfig.DesktopMutexName);
        Assert.EndsWith(
            Path.Combine("SalrynCafe", "WebView2"),
            DevHostConfig.GetWebView2UserDataFolder(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5399/", true)]
    [InlineData("http://localhost:5399/pos", true)]
    [InlineData("http://127.0.0.1:5173/", true)]
    [InlineData("http://127.0.0.1:5299/", false)]
    [InlineData("http://127.0.0.1:5387/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    public void LocalWebOriginPolicy_AllowsCafePortsAndBlocksRetailAndExternal(string url, bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, LocalWebOriginPolicy.IsAllowedLocalWebUrl(url));
    }

    [Fact]
    public void LocalApiProcessManager_HealthVerification_RejectsRetailOrUnidentifiedServices()
    {
        Assert.True(LocalApiProcessManager.IsVerifiedCafeHealthResponse(
            "Salryn.Cafe",
            """{"status":"ok","service":"Salryn.Api.Local","productId":"Salryn.Cafe"}"""));

        // Legacy retail health payload without productId must be rejected
        Assert.False(LocalApiProcessManager.IsVerifiedCafeHealthResponse(
            null,
            """{"status":"ok","service":"Salryn.Api.Local"}"""));

        // Explicit retail productId must be rejected
        Assert.False(LocalApiProcessManager.IsVerifiedCafeHealthResponse(
            "Salryn.POS",
            """{"status":"ok","service":"Salryn.Api.Local","productId":"Salryn.POS"}"""));

        // Header/body mismatch must be rejected
        Assert.False(LocalApiProcessManager.IsVerifiedCafeHealthResponse(
            "Salryn.POS",
            """{"status":"ok","service":"Salryn.Api.Local","productId":"Salryn.Cafe"}"""));
    }
}

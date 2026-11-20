using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.Tests;

public sealed class ProductIsolationTests
{
    [Fact]
    public void ProductionPaths_UseIsolatedSalrynCafeRootAndDatabaseName()
    {
        DbPaths paths = DbPaths.CreateProduction();

        Assert.EndsWith(
            Path.Combine("SalrynCafe", "data", "salryn-cafe.db"),
            paths.DatabasePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.Combine("ProgramData", "Salryn", "data"),
            paths.DatabasePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DevelopmentPaths_UseIsolatedSalrynCafeDevRoot()
    {
        string fakeRoot = Path.Combine(Path.GetTempPath(), "salryn-cafe-dev-root-check");
        DbPaths paths = DbPaths.CreateDevelopment(fakeRoot);

        Assert.Equal(Path.Combine(fakeRoot, ".salryn-cafe-dev"), paths.RootPath);
        Assert.Equal(Path.Combine(fakeRoot, ".salryn-cafe-dev", "data", "salryn-cafe.db"), paths.DatabasePath);
    }

    [Fact]
    public void LocalDataContract_TargetsSalrynCafeOnly()
    {
        Assert.Equal("Salryn.Cafe", SalrynLocalDataContract.ProductId);
        Assert.Equal("ProgramData/SalrynCafe", SalrynLocalDataContract.ProductionRootOwner);
        Assert.Equal(".salryn-cafe-dev", SalrynLocalDataContract.DevelopmentRootOwner);
        Assert.Equal("LocalApplicationData/SalrynCafe/Licensing", SalrynLocalDataContract.LocalLicenseRootOwner);
        Assert.Equal("LocalApplicationData/SalrynCafe/WebView2", SalrynLocalDataContract.WebView2ProfileRootOwner);
        Assert.All(
            SalrynLocalDataContract.RequiredEntries,
            entry => Assert.Contains("SalrynCafe", entry.RelativePath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Kirjane Information Technology Services")]
    [InlineData(@"C:\Program Files\Kirjane Information Technology Services\Salryn POS")]
    public void ProtectedRetailInstallPaths_AreRejected(string retailPath)
    {
        var result = SalrynProductGuard.ValidateNotProtectedRetailPath(retailPath);

        Assert.True(result.IsFailure);
        Assert.Equal(SalrynProductGuard.ErrorProtectedRetailPath, result.ErrorDetail.Code);
    }

    [Fact]
    public void ProtectedRetailProgramDataAndLocalAppData_AreRejected()
    {
        string retailProgramData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Salryn",
            "data");
        string retailLocalAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Salryn",
            "Licensing");

        Assert.True(SalrynProductGuard.ValidateNotProtectedRetailPath(retailProgramData).IsFailure);
        Assert.True(SalrynProductGuard.ValidateNotProtectedRetailPath(retailLocalAppData).IsFailure);
    }

    [Fact]
    public void IndependentCafePaths_AreAccepted()
    {
        Assert.True(SalrynProductGuard.ValidateNotProtectedRetailPath(SalrynProductIdentity.CafeInstallDir).IsSuccess);
        Assert.True(SalrynProductGuard.ValidateNotProtectedRetailPath(SalrynProductIdentity.GetProgramDataRoot()).IsSuccess);
        Assert.True(SalrynProductGuard.ValidateNotProtectedRetailPath(SalrynProductIdentity.GetLocalAppDataRoot()).IsSuccess);
    }

    [Theory]
    [InlineData(null, "http://127.0.0.1:5399")]
    [InlineData("", "http://127.0.0.1:5399")]
    [InlineData("http://127.0.0.1:5299", "http://127.0.0.1:5399")]
    [InlineData("http://127.0.0.1:5387", "http://127.0.0.1:5399")]
    [InlineData("http://192.168.1.50:5399", "http://127.0.0.1:5399")]
    [InlineData("http://127.0.0.1:5499", "http://127.0.0.1:5499")]
    public void ResolveLoopbackApiBaseUrl_BlocksRetailAndNonLoopback(string? envOverride, string expected)
    {
        Assert.Equal(expected, SalrynProductIdentity.ResolveLoopbackApiBaseUrl(envOverride));
    }
}

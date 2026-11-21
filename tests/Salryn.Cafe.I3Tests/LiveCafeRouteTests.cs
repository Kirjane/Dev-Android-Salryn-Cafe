using System.Net;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.I3Tests;

public sealed class LiveCafeRouteTests
{
    [Fact]
    public async Task ExecutableHost_MapsCafeDomainsBehindAuthenticationAndExcludesRetailRoutes()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-cafe-route-" + Guid.NewGuid().ToString("N"));
        var composition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(root));
        await using var host = await LocalApiApplicationFactory.CreateAsync([], new()
        {
            Composition = composition, Url = "http://127.0.0.1:0", EnableStaticWebAssets = false,
            WriteDevelopmentDatabasePath = false
        });
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
        foreach (string path in new[] { "/api/menu/publication/current", "/api/cafe/orders", "/api/cafe/inventory/low-stock",
            "/api/cafe/cash/movements", "/api/cafe/print/stations", "/api/cafe/deliveries", "/api/cafe/bookings",
            "/api/cafe/attendance", "/api/cafe/incidents" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"{path} expected authentication guard; actual {(int)response.StatusCode}");
        }
        foreach (string path in new[] { "/api/lpi/foundation", "/api/customer-orders", "/api/customer-quotations" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        await host.StopAsync();
    }
}

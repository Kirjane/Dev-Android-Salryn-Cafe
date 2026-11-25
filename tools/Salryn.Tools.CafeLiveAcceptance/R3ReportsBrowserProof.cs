using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Licensing;

internal static class R3ReportsBrowserProof
{
    public static async Task RunAsync(DirectoryInfo repo, string playwrightPackageJson, string axePath)
    {
#if !DEBUG
        throw new InvalidOperationException("Debug disposable Host required.");
#else
        string? priorDevMode = Environment.GetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE");
        string? overrideRoot = Environment.GetEnvironmentVariable("CAFE_R3_REPORTS_ARTIFACT_ROOT");
        string root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? overrideRoot
            : Path.Combine(Path.GetTempPath(), "salryn-r3-reports-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string dbRoot = Path.Combine(root, "db-" + Guid.NewGuid().ToString("N"));
            var fixture = R3LiveProofFixture.Create(dbRoot);
            string nonce = Guid.NewGuid().ToString("N");

            // Phase A: Disposable Host WITHOUT dev license and with an isolated empty license directory -> proves fail-closed unlicensed gate in real browser.
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", null);
            string emptyLicenseDir = Path.Combine(root, "empty-licensing");
            Directory.CreateDirectory(emptyLicenseDir);
            var isolatedUnlicensedService = new LocalLicenseService(
                new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(emptyLicenseDir, LocalLicenseFileStore.DefaultFileName)));
            await using (var unlicensedHost = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(fixture.Db.Paths),
                LicenseService = isolatedUnlicensedService,
                Url = "http://127.0.0.1:0",
                TestHostNonce = nonce,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }))
            {
                await unlicensedHost.StartAsync();
                await RunWithViteProxyAsync(
                    repo,
                    unlicensedHost.Urls.Single(),
                    playwrightPackageJson,
                    axePath,
                    nonce,
                    root,
                    phase: "unlicensed",
                    fixture);
                await unlicensedHost.StopAsync();
            }

            // Phase B: Disposable Host WITH DEBUG_DEVELOPMENT_LICENSE + real seeded nonzero multi-period financial history.
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "1");
            await using (var licensedHost = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(fixture.Db.Paths),
                Url = "http://127.0.0.1:0",
                TestHostNonce = nonce,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }))
            {
                await licensedHost.StartAsync();
                using (var ownerHttp = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
                {
                    BaseAddress = new Uri(licensedHost.Urls.Single())
                })
                {
                    await R3LiveProofFixture.LoginAsync(ownerHttp, R3LiveProofFixture.OwnerUsername, R3LiveProofFixture.OwnerPassword);
                    await fixture.SeedLiveFinancialHistoryAsync(ownerHttp);
                }

                await RunWithViteProxyAsync(
                    repo,
                    licensedHost.Urls.Single(),
                    playwrightPackageJson,
                    axePath,
                    nonce,
                    root,
                    phase: "licensed",
                    fixture);
                await licensedHost.StopAsync();
            }

            Console.WriteLine($"PASS: R3 live reports browser proof (unlicensed fail-closed + live nonzero Host + redaction + CSV + stale-response guard + axe-core + responsive screenshots); DEBUG_DEVELOPMENT_LICENSE; not Release acceptance. Root: {root}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", priorDevMode);
        }
#endif
    }

    private static async Task RunWithViteProxyAsync(
        DirectoryInfo repo,
        string hostUrl,
        string playwrightPackageJson,
        string axePath,
        string nonce,
        string root,
        string phase,
        R3LiveProofFixture fixture)
    {
        int vitePort = ReserveFreeLoopbackPort();
        string viteUrl = $"http://127.0.0.1:{vitePort}";
        string cafeWebDir = Path.Combine(repo.FullName, "apps", "cafe-web");
        string viteBin = Path.Combine(cafeWebDir, "node_modules", "vite", "bin", "vite.js");
        if (!File.Exists(viteBin))
        {
            throw new InvalidOperationException("Vite binary not found at: " + viteBin);
        }

        var viteStart = new ProcessStartInfo("node")
        {
            WorkingDirectory = cafeWebDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        viteStart.ArgumentList.Add(viteBin);
        viteStart.ArgumentList.Add("--host");
        viteStart.ArgumentList.Add("127.0.0.1");
        viteStart.ArgumentList.Add("--port");
        viteStart.ArgumentList.Add(vitePort.ToString());
        viteStart.ArgumentList.Add("--strictPort");
        viteStart.Environment["VITE_ENABLE_R3_LIVE_REPORTS"] = "1";
        viteStart.Environment["VITE_HOST_PROXY_TARGET"] = hostUrl;

        using var viteProc = Process.Start(viteStart)
            ?? throw new InvalidOperationException("Failed to start Vite dev server for R3 reports browser proof.");
        var viteStdout = viteProc.StandardOutput.ReadToEndAsync();
        var viteStderr = viteProc.StandardError.ReadToEndAsync();

        try
        {
            await WaitForHttpReadyAsync($"{viteUrl}/cafe/", viteProc, TimeSpan.FromSeconds(30));

            var browserStart = new ProcessStartInfo("node") { UseShellExecute = false };
            browserStart.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "r3-reports-browser.mjs"));
            browserStart.Environment["CAFE_QA_PLAYWRIGHT_PACKAGE"] = playwrightPackageJson;
            browserStart.Environment["CAFE_QA_AXE_PATH"] = axePath;
            browserStart.Environment["CAFE_QA_URL"] = viteUrl;
            browserStart.Environment["CAFE_QA_HOST_URL"] = hostUrl;
            browserStart.Environment["CAFE_QA_NONCE"] = nonce;
            browserStart.Environment["CAFE_QA_ROOT"] = root;
            browserStart.Environment["CAFE_QA_PHASE"] = phase;
            browserStart.Environment["CAFE_QA_OWNER_USER"] = R3LiveProofFixture.OwnerUsername;
            browserStart.Environment["CAFE_QA_OWNER_PASS"] = R3LiveProofFixture.OwnerPassword;
            browserStart.Environment["CAFE_QA_REPORTS_ONLY_USER"] = R3LiveProofFixture.ReportsOnlyUsername;
            browserStart.Environment["CAFE_QA_REPORTS_ONLY_PASS"] = R3LiveProofFixture.ReportsOnlyPassword;
            browserStart.Environment["CAFE_QA_CASHIER_USER"] = R3LiveProofFixture.CashierUsername;
            browserStart.Environment["CAFE_QA_CASHIER_PASS"] = R3LiveProofFixture.CashierPassword;
            browserStart.Environment["CAFE_QA_EXPECTED_JSON"] = JsonSerializer.Serialize(new
            {
                dailyNetCents = fixture.ExpectedDailyNetRevenueCents,
                weeklyNetCents = fixture.ExpectedWeeklyNetRevenueCents,
                monthlyNetCents = fixture.ExpectedMonthlyNetRevenueCents,
                hasEarlierWeek = fixture.HasEarlierWeekSale,
                hasEarlierMonth = fixture.HasEarlierMonthSale,
            });

            using var browserProc = Process.Start(browserStart)
                ?? throw new InvalidOperationException("Failed to start R3 reports Playwright runner.");
            await browserProc.WaitForExitAsync();
            if (browserProc.ExitCode != 0)
            {
                throw new InvalidOperationException($"R3 reports browser proof ({phase}) failed with exit {browserProc.ExitCode}. Root: {root}");
            }
        }
        finally
        {
            try
            {
                if (!viteProc.HasExited)
                {
                    viteProc.Kill(entireProcessTree: true);
                    await viteProc.WaitForExitAsync();
                }
            }
            catch
            {
            }

            try
            {
                File.WriteAllText(
                    Path.Combine(root, $"vite-{phase}.log"),
                    await viteStdout + "\n--- STDERR ---\n" + await viteStderr);
            }
            catch
            {
            }
        }
    }

    private static int ReserveFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForHttpReadyAsync(string url, Process proc, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (proc.HasExited)
            {
                throw new InvalidOperationException($"Vite dev server exited prematurely with code {proc.ExitCode}.");
            }
            try
            {
                using var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode) return;
            }
            catch
            {
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Timed out waiting for Vite dev server at {url}.");
    }
}

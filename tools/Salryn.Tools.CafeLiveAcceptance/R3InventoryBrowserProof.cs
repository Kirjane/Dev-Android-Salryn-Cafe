using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Salryn.Api.Local;
using Salryn.Cafe.I6ATests;
using Salryn.Composition.Local;

internal static class R3InventoryBrowserProof
{
    public static async Task RunAsync(DirectoryInfo repo, string playwrightPackageJson, string axePath)
    {
#if !DEBUG
        throw new InvalidOperationException("Debug disposable Host required.");
#else
        string? priorDevMode = Environment.GetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE");
        string? overrideRoot = Environment.GetEnvironmentVariable("CAFE_R3_INVENTORY_ARTIFACT_ROOT");
        string root = !string.IsNullOrWhiteSpace(overrideRoot)
            ? overrideRoot
            : Path.Combine(Path.GetTempPath(), "salryn-r3-inventory-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string dbRoot = Path.Combine(root, "db-" + Guid.NewGuid().ToString("N"));
            var fixture = R3LiveProofFixture.Create(dbRoot);
            string nonce = Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", "true");

            await using (var host = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(fixture.Db.Paths),
                Url = "http://127.0.0.1:0",
                TestHostNonce = nonce,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            }))
            {
                await host.StartAsync();
                string hostUrl = host.Urls.Single();

                int vitePort = GetFreeTcpPort();
                string viteBaseUrl = $"http://127.0.0.1:{vitePort}";
                string cafeWebDir = Path.Combine(repo.FullName, "apps", "cafe-web");
                string viteBin = Path.Combine(cafeWebDir, "node_modules", "vite", "bin", "vite.js");
                if (!File.Exists(viteBin)) throw new InvalidOperationException("Vite CLI not found at: " + viteBin);

                var viteStart = new ProcessStartInfo("node", $"\"{viteBin}\" --host 127.0.0.1 --port {vitePort} --strictPort")
                {
                    WorkingDirectory = cafeWebDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                viteStart.Environment["VITE_ENABLE_R3_LIVE_INVENTORY"] = "1";
                viteStart.Environment["VITE_HOST_PROXY_TARGET"] = hostUrl;

                using var vite = Process.Start(viteStart) ?? throw new InvalidOperationException("Failed to start Vite dev server.");
                var viteOut = new List<string>();
                vite.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (viteOut) viteOut.Add(e.Data); };
                vite.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (viteOut) viteOut.Add(e.Data); };
                vite.BeginOutputReadLine();
                vite.BeginErrorReadLine();

                try
                {
                    await WaitForViteAsync(viteBaseUrl, vite, viteOut).ConfigureAwait(false);

                    string scriptPath = Path.Combine(AppContext.BaseDirectory, "r3-inventory-browser.mjs");
                    if (!File.Exists(scriptPath)) throw new InvalidOperationException("Missing browser script: " + scriptPath);

                    var nodeStart = new ProcessStartInfo("node", $"\"{scriptPath}\"")
                    {
                        WorkingDirectory = repo.FullName,
                        UseShellExecute = false
                    };
                    nodeStart.Environment["CAFE_LIVE_PLAYWRIGHT_PACKAGE_JSON"] = playwrightPackageJson;
                    nodeStart.Environment["CAFE_LIVE_AXE_PATH"] = axePath;
                    nodeStart.Environment["CAFE_R3_BASE_URL"] = viteBaseUrl;
                    nodeStart.Environment["CAFE_R3_NONCE"] = nonce;
                    nodeStart.Environment["CAFE_R3_ROOT"] = root;
                    nodeStart.Environment["CAFE_R3_OWNER_USER"] = R3LiveProofFixture.OwnerUsername;
                    nodeStart.Environment["CAFE_R3_OWNER_PASS"] = R3LiveProofFixture.OwnerPassword;
                    nodeStart.Environment["CAFE_R3_CASHIER_USER"] = R3LiveProofFixture.CashierUsername;
                    nodeStart.Environment["CAFE_R3_CASHIER_PASS"] = R3LiveProofFixture.CashierPassword;

                    using var browserProc = Process.Start(nodeStart) ?? throw new InvalidOperationException("Failed to launch browser proof.");
                    await browserProc.WaitForExitAsync().ConfigureAwait(false);
                    if (browserProc.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"R3 inventory browser proof failed with exit {browserProc.ExitCode}. Root: {root}");
                    }
                }
                finally
                {
                    try { if (!vite.HasExited) vite.Kill(entireProcessTree: true); } catch { }
                }

                await host.StopAsync();
            }

            // SQLite persistence assertions after Host shutdown.
            long adjCount = fixture.Db.ScalarLong("SELECT COUNT(*) FROM inventory_adjustment_events;");
            if (adjCount != 1) throw new InvalidOperationException($"Expected 1 inventory adjustment row, got {adjCount}.");

            long finalizedCounts = fixture.Db.ScalarLong("SELECT COUNT(*) FROM inventory_count_sessions WHERE state='Finalized';");
            if (finalizedCounts != 1) throw new InvalidOperationException($"Expected 1 finalized count session, got {finalizedCounts}.");

            long finalStock = fixture.Db.ScalarLong(
                "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;",
                ("$id", C4ProofIds.ModifierIngredientId.ToString("D")));
            if (finalStock != 95000) throw new InvalidOperationException($"Expected final counted ingredient stock 95000, got {finalStock}.");

            Console.WriteLine($"PASS: R3 live inventory browser proof (RBAC + INT-01 reload-retry + INT-02 draft/finalize/immutable + 3 SQLite assertions + axe-core + screenshot); DEBUG_DEVELOPMENT_LICENSE; not Release acceptance. Root: {root}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SALRYN_LICENSE_DEVELOPMENT_MODE", priorDevMode);
        }
#endif
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForViteAsync(string baseUrl, Process vite, List<string> viteOut)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (vite.HasExited)
            {
                string log;
                lock (viteOut) log = string.Join(Environment.NewLine, viteOut);
                throw new InvalidOperationException($"Vite dev server exited early ({vite.ExitCode}):\n{log}");
            }
            try
            {
                using var res = await http.GetAsync(baseUrl + "/cafe/integration/r3/inventory").ConfigureAwait(false);
                if ((int)res.StatusCode < 500) return;
            }
            catch { }
            await Task.Delay(250).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Timed out waiting for Vite dev server at " + baseUrl);
    }
}

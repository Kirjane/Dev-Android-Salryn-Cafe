using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Desktop.WebView;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.Tests;

public sealed class ProductTruthAndPackagingTests
{
    [Fact]
    public void InstalledBundleValidation_FailsClosedWhenLocalApiOrCafeDistIsMissing()
    {
        string tempInstall = Path.Combine(Path.GetTempPath(), "salryn-cafe-incomplete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        try
        {
            // Incomplete installed directory without Salryn.sln MUST NOT fall back to Vite :5173.
            Assert.True(DevHostConfig.IsInstalledPackageContext(tempInstall));
            Assert.False(DevHostConfig.TryValidateInstalledBundle(tempInstall, out string? missingBoth));
            Assert.Contains("Salryn.Api.Local.exe", missingBoth, StringComparison.Ordinal);
            Assert.Contains("cafe-dist", missingBoth, StringComparison.Ordinal);

            // Presence of Desktop/uninstall + wwwroot alone (the historical broken package shape) still fails closed.
            File.WriteAllText(Path.Combine(tempInstall, "unins000.exe"), "placeholder");
            Directory.CreateDirectory(Path.Combine(tempInstall, "wwwroot"));
            File.WriteAllText(Path.Combine(tempInstall, "wwwroot", "index.html"), "<html><title>Salryn POS</title></html>");
            Assert.False(DevHostConfig.TryValidateInstalledBundle(tempInstall, out string? missingBothWithWwwroot));
            Assert.Contains("Salryn.Api.Local.exe", missingBothWithWwwroot, StringComparison.Ordinal);
            Assert.Contains("cafe-dist", missingBothWithWwwroot, StringComparison.Ordinal);

            // Adding only Salryn.Api.Local.exe still rejects missing cafe-dist/index.html.
            File.WriteAllText(Path.Combine(tempInstall, "Salryn.Api.Local.exe"), "placeholder");
            Assert.False(DevHostConfig.TryValidateInstalledBundle(tempInstall, out string? missingCafeDist));
            Assert.DoesNotContain("Salryn.Api.Local.exe", missingCafeDist, StringComparison.Ordinal);
            Assert.Contains("cafe-dist", missingCafeDist, StringComparison.Ordinal);

            // Complete installed package succeeds and resolves to unified R3 (/cafe/r3).
            Directory.CreateDirectory(Path.Combine(tempInstall, "cafe-dist"));
            File.WriteAllText(Path.Combine(tempInstall, "cafe-dist", "index.html"), "<html><title>Salryn Cafe</title></html>");
            Assert.True(DevHostConfig.TryValidateInstalledBundle(tempInstall, out string? okReason));
            Assert.Null(okReason);
            Assert.EndsWith("/cafe/r3", DevHostConfig.ResolveWebUrl(out _, tempInstall), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(tempInstall, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task LocalHost_ServesRealCafeDistAtCafeRootAndSpaFallback_SeparateFromLegacyRoot()
    {
        string repoRoot = FindRepoRoot();
        string cafeDistDir = Path.Combine(repoRoot, "apps", "cafe-web", "dist");
        string cafeIndex = Path.Combine(cafeDistDir, "index.html");
        Assert.True(File.Exists(cafeIndex), $"Expected built Cafe bundle at {cafeIndex}");

        string indexHtml = await File.ReadAllTextAsync(cafeIndex);
        Assert.Contains("Salryn Cafe", indexHtml, StringComparison.Ordinal);
        Assert.Contains("/cafe/assets/", indexHtml, StringComparison.Ordinal);

        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-cafe-r0-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using WebApplication app = await LocalApiApplicationFactory.CreateAsync([], new LocalApiApplicationOptions
            {
                Composition = SalrynLocalComposition.Create(DbPaths.CreateAtRoot(tempRoot)),
                Url = "http://127.0.0.1:0",
                EnableStaticWebAssets = true,
                WriteDevelopmentDatabasePath = false
            });
            await app.StartAsync();

            using HttpClient client = new() { BaseAddress = new Uri(app.Urls.Single()) };

            using HttpResponseMessage healthResp = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
            using JsonDocument healthDoc = JsonDocument.Parse(await healthResp.Content.ReadAsStringAsync());
            Assert.Equal("Salryn.Cafe", healthDoc.RootElement.GetProperty("productId").GetString());

            using HttpResponseMessage cafeDiagResp = await client.GetAsync("/api/system/cafe-dist");
            Assert.Equal(HttpStatusCode.OK, cafeDiagResp.StatusCode);
            using JsonDocument cafeDiagDoc = JsonDocument.Parse(await cafeDiagResp.Content.ReadAsStringAsync());
            Assert.True(cafeDiagDoc.RootElement.GetProperty("resolved").GetBoolean());
            Assert.True(cafeDiagDoc.RootElement.GetProperty("indexHtmlExists").GetBoolean());

            using HttpResponseMessage cafeNoSlashResp = await client.GetAsync("/cafe");
            Assert.Equal(HttpStatusCode.OK, cafeNoSlashResp.StatusCode);

            using HttpResponseMessage cafeRootResp = await client.GetAsync("/cafe/");
            Assert.Equal(HttpStatusCode.OK, cafeRootResp.StatusCode);
            string cafeBody = await cafeRootResp.Content.ReadAsStringAsync();
            Assert.Contains("Salryn Cafe", cafeBody, StringComparison.Ordinal);

            Match assetMatch = Regex.Match(indexHtml, @"/cafe/assets/[^""'\s>]+");
            Assert.True(assetMatch.Success, "Expected at least one hashed asset URL under /cafe/assets/");
            using HttpResponseMessage assetResp = await client.GetAsync(assetMatch.Value);
            Assert.Equal(HttpStatusCode.OK, assetResp.StatusCode);

            using HttpResponseMessage cafeSubrouteResp = await client.GetAsync("/cafe/orders");
            string subrouteBody = await cafeSubrouteResp.Content.ReadAsStringAsync();
            Assert.True(cafeSubrouteResp.StatusCode == HttpStatusCode.OK, $"Expected 200 for /cafe/orders, got {(int)cafeSubrouteResp.StatusCode}: {subrouteBody}");
            Assert.Contains("Salryn Cafe", subrouteBody, StringComparison.Ordinal);

            // Unified installed R3 default entry point and deep routes must resolve to the same Cafe SPA shell.
            foreach (string r3Route in new[] { "/cafe/r3", "/cafe/r3/today", "/cafe/r3/pos", "/cafe/backoffice", "/cafe/central" })
            {
                using HttpResponseMessage r3Resp = await client.GetAsync(r3Route);
                string r3Body = await r3Resp.Content.ReadAsStringAsync();
                Assert.True(r3Resp.StatusCode == HttpStatusCode.OK, $"Expected 200 for {r3Route}, got {(int)r3Resp.StatusCode}: {r3Body}");
                Assert.Contains("Salryn Cafe", r3Body, StringComparison.Ordinal);
            }

            using HttpResponseMessage missingAssetResp = await client.GetAsync("/cafe/assets/nonexistent-bundle.js");
            Assert.Equal(HttpStatusCode.NotFound, missingAssetResp.StatusCode);

            await app.StopAsync();
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PackagingContracts_RequireLocalApiAndCafeDist_AndDockerCopyClosureCompilesCentralServer()
    {
        string repoRoot = FindRepoRoot();

        string installerWorkflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "salryn-cafe-installer-qa.yml"));
        Assert.Contains("src/Salryn.Api.Local/Salryn.Api.Local.csproj", installerWorkflow, StringComparison.Ordinal);
        Assert.Contains("apps/cafe-web", installerWorkflow, StringComparison.Ordinal);
        Assert.Contains("Salryn.Api.Local.exe", installerWorkflow, StringComparison.Ordinal);
        Assert.Contains("cafe-dist", installerWorkflow, StringComparison.Ordinal);

        string localInstallerScript = File.ReadAllText(Path.Combine(repoRoot, "installer", "Build-SalrynCafeInstaller.ps1"));
        Assert.Contains("apps\\cafe-web", localInstallerScript, StringComparison.Ordinal);
        Assert.Contains("Salryn.Api.Local.exe", localInstallerScript, StringComparison.Ordinal);
        Assert.Contains("cafe-dist\\index.html", localInstallerScript, StringComparison.Ordinal);

        string dockerfile = File.ReadAllText(Path.Combine(repoRoot, "deploy", "central", "Dockerfile"));
        Assert.Contains("src/Salryn.Persistence.Sqlite", dockerfile, StringComparison.Ordinal);
        Assert.Contains("src/Salryn.Core", dockerfile, StringComparison.Ordinal);
        Assert.Contains("src/Salryn.Infrastructure/Data", dockerfile, StringComparison.Ordinal);

        // Materialize an isolated temp directory containing ONLY the files copied by deploy/central/Dockerfile
        // and verify dotnet publish succeeds without access to uncopied repository folders.
        string dockerCtx = Path.Combine(Path.GetTempPath(), "salryn-central-docker-closure-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyRelativeFile(repoRoot, dockerCtx, "Directory.Build.props");
            CopyRelativeDir(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Core"));
            CopyRelativeDir(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Sync.Contracts"));
            CopyRelativeDir(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Persistence.Sqlite"));
            CopyRelativeDir(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Infrastructure", "Data"));
            CopyRelativeFile(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Infrastructure", "Paths", "DbPaths.cs"));
            CopyRelativeFile(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Infrastructure", "Paths", "SalrynProductIdentity.cs"));
            CopyRelativeDir(repoRoot, dockerCtx, Path.Combine("src", "Salryn.Sync.Central.Server"));

            string publishOut = Path.Combine(dockerCtx, "publish");
            string csprojRel = Path.Combine("src", "Salryn.Sync.Central.Server", "Salryn.Sync.Central.Server.csproj");
            string nugetCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

            bool restoredOffline = false;
            if (Directory.Exists(nugetCache))
            {
                string offlineNugetConfig = Path.Combine(dockerCtx, "nuget.offline.config");
                File.WriteAllText(offlineNugetConfig, $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <packageSources>
                        <clear />
                        <add key="local-cache" value="{System.Security.SecurityElement.Escape(nugetCache)}" />
                      </packageSources>
                    </configuration>
                    """);
                int restoreExit = RunDotnet(dockerCtx, ["restore", csprojRel, "--configfile", offlineNugetConfig, "--disable-build-servers", "-m:1", "--nologo"], out _, out _);
                restoredOffline = restoreExit == 0;
            }

            List<string> publishArgs = ["publish", csprojRel, "-c", "Release", "-o", publishOut, "--disable-build-servers", "-m:1", "--nologo"];
            if (restoredOffline)
            {
                publishArgs.Add("--no-restore");
            }

            int exitCode = RunDotnet(dockerCtx, publishArgs, out string stdout, out string stderr);
            Assert.True(exitCode == 0, $"Central Docker COPY closure publish failed ({exitCode}):\n{stdout}\n{stderr}");
            Assert.True(File.Exists(Path.Combine(publishOut, "Salryn.Sync.Central.Server.dll")));
        }
        finally
        {
            try { Directory.Delete(dockerCtx, recursive: true); } catch { }
        }
    }

    private static int RunDotnet(string workDir, IEnumerable<string> args, out string stdout, out string stderr)
    {
        ProcessStartInfo psi = new("dotnet")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet");
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(180_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"dotnet timed out after 180s in {workDir}");
        }

        stdout = stdoutTask.GetAwaiter().GetResult();
        stderr = stderrTask.GetAwaiter().GetResult();
        return proc.ExitCode;
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

        throw new InvalidOperationException("Could not locate Salryn.sln from test base directory.");
    }

    private static void CopyRelativeFile(string srcRoot, string dstRoot, string relPath)
    {
        string src = Path.Combine(srcRoot, relPath);
        string dst = Path.Combine(dstRoot, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
    }

    private static void CopyRelativeDir(string srcRoot, string dstRoot, string relDir)
    {
        string src = Path.Combine(srcRoot, relDir);
        foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(srcRoot, file);
            if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string dst = Path.Combine(dstRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: true);
        }
    }
}

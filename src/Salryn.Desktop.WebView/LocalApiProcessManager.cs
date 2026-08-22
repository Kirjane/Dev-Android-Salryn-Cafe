using System.Diagnostics;
using System.Net.Http;

namespace Salryn.Desktop.WebView;

/// <summary>
/// Ensures the Salryn local API is reachable for the native host.
/// Installed layout: starts <c>Salryn.Api.Local.exe</c> beside this shell.
/// Dev layout: launches <c>dotnet run --project</c> against the repository when present.
/// This wrapper performs no business logic and never touches the database directly.
/// </summary>
internal sealed class LocalApiProcessManager : IDisposable
{
    private static readonly HttpClient HealthClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    private Process? _apiProcess;

    public bool StartedByHost { get; private set; }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            using HttpResponseMessage response = await HealthClient
                .GetAsync(DevHostConfig.HealthUrl)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures the API is healthy, starting it if required.
    /// Returns true once /health responds successfully within the timeout.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(TimeSpan timeout)
    {
        if (await IsHealthyAsync().ConfigureAwait(false))
        {
            return true;
        }

        if (!TryStartApiProcess())
        {
            return false;
        }

        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await IsHealthyAsync().ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        return false;
    }

    private bool TryStartApiProcess()
    {
        if (TryStartInstalledApiExecutable())
        {
            return true;
        }

        return TryStartDevApiProcess();
    }

    /// <summary>
    /// Production/installer layout: Salryn.Api.Local.exe ships next to the WebView shell.
    /// </summary>
    private bool TryStartInstalledApiExecutable()
    {
        string apiExe = Path.Combine(AppContext.BaseDirectory, "Salryn.Api.Local.exe");
        if (!File.Exists(apiExe))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = apiExe,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Production host binds localhost; urls may also come from launchSettings/defaults.
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(DevHostConfig.ApiBaseUrl);

        try
        {
            _apiProcess = Process.Start(startInfo);
            StartedByHost = _apiProcess is not null;
            return StartedByHost;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Repository / developer layout: start Api.Local via dotnet run --dev.
    /// </summary>
    private bool TryStartDevApiProcess()
    {
        string? apiProjectPath = ResolveApiProjectPath();

        if (apiProjectPath is null)
        {
            return false;
        }

        string? repositoryRoot = FindRepositoryRoot(Path.GetDirectoryName(apiProjectPath)!);

        if (repositoryRoot is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = repositoryRoot,
        };

        startInfo.Environment["SALRYN_PROJECT_ROOT"] = repositoryRoot;

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(apiProjectPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--dev");

        try
        {
            _apiProcess = Process.Start(startInfo);
            StartedByHost = _apiProcess is not null;
            return StartedByHost;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ResolveApiProjectPath()
    {
        foreach (string root in CandidateRoots())
        {
            string candidate = Path.Combine(
                root, "src", "Salryn.Api.Local", "Salryn.Api.Local.csproj");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        string? fromBase = FindRepositoryRoot(AppContext.BaseDirectory);
        if (fromBase is not null)
        {
            yield return fromBase;
        }

        string? fromCurrent = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (fromCurrent is not null && !string.Equals(fromCurrent, fromBase, StringComparison.OrdinalIgnoreCase))
        {
            yield return fromCurrent;
        }
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public void Dispose()
    {
        if (_apiProcess is null)
        {
            return;
        }

        try
        {
            if (StartedByHost && !_apiProcess.HasExited)
            {
                _apiProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup of the child process.
        }
        finally
        {
            _apiProcess.Dispose();
            _apiProcess = null;
        }
    }
}

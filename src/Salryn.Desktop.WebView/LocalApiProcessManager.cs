using System.Diagnostics;
using System.Net.Http;

namespace Salryn.Desktop.WebView;

/// <summary>
/// Ensures the Salryn local API is reachable for the dev host.
/// If it is not already running, it can launch <c>Salryn.Api.Local</c> as a dev child process.
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
    /// Ensures the API is healthy, starting it in dev mode if required.
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
        string? apiProjectPath = ResolveApiProjectPath();

        if (apiProjectPath is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(apiProjectPath)!,
        };

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
            // Best effort cleanup of the dev child process.
        }
        finally
        {
            _apiProcess.Dispose();
            _apiProcess = null;
        }
    }
}

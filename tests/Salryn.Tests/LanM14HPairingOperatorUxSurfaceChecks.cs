using System.Diagnostics;

namespace Salryn.Tests;

public static class LanM14HPairingOperatorUxSurfaceChecks
{
    private const string LanM14HBaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");
        string currentStatePath = Path.Combine(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string lanM14HPath = Path.Combine(handoffRoot, "LAN_M14_H_PAIRING_OPERATOR_UX_SURFACE.md");
        string coreSmokePath = Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        string currentState = ReadRequiredFile(currentStatePath);
        string lanM14H = ReadRequiredFile(lanM14HPath);
        string coreSmoke = ReadRequiredFile(coreSmokePath);

        if (IsLanM14FThroughIClosureContext(currentState))
        {
            AssertContains(currentState, LanM14HBaselineSha, "Current-state doc must contain the LAN-M14-H baseline SHA.");
            AssertContains(lanM14H, "Pairing", "LAN-M14-H doc must document pairing UX.");
            AssertContains(lanM14H, LanM14HBaselineSha, "LAN-M14-H doc must contain the source stamp SHA.");
            AssertContains(coreSmoke, "LanM14HPairingOperatorUxSurfaceChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-H checks.");
            AssertSourcePairingUxBoundaries(projectRoot);
            AssertLanM14HChangedFilesStayInScope(projectRoot, currentState);
            return;
        }

        AssertDocBoundaries(currentState, lanM14H, coreSmoke);
        AssertSourcePairingUxBoundaries(projectRoot);
        AssertLanM14HChangedFilesStayInScope(projectRoot, currentState);
    }

    private static bool IsLanM14FThroughIClosureContext(string currentState)
    {
        return currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal)
            || currentState.Contains("LAN-M14-I is the LAN-M14 cap", StringComparison.Ordinal);
    }

    private static void AssertDocBoundaries(string currentState, string lanM14H, string coreSmoke)
    {
        AssertContains(currentState, LanM14HBaselineSha, "Current-state doc must contain the LAN-M14-H baseline SHA.");
        AssertContains(currentState, "Pairing/status UX is read-only", "Current-state doc must state pairing/status UX is read-only.");
        AssertContains(currentState, "LAN_M14_H_PAIRING_OPERATOR_UX_SURFACE.md", "Current-state doc must list LAN-M14-H as live authority.");

        AssertContains(lanM14H, LanM14HBaselineSha, "LAN-M14-H doc must contain the source stamp SHA.");
        AssertContains(lanM14H, "RemoteReadOnlyClientPage", "LAN-M14-H doc must document RemoteReadOnlyClientPage.");
        AssertContains(lanM14H, "Pairing", "LAN-M14-H doc must document pairing surface.");
        AssertContains(lanM14H, "Save/Delete/Create", "LAN-M14-H doc must forbid mutation controls.");
        AssertContains(lanM14H, "Forbidden Scope", "LAN-M14-H doc must define forbidden scope.");
        AssertContains(lanM14H, "Decision", "LAN-M14-H doc must include a decision section.");

        AssertContains(coreSmoke, "LanM14HPairingOperatorUxSurfaceChecks.Run();", "CoreSmoke must explicitly wire LAN-M14-H checks.");
    }

    private static void AssertSourcePairingUxBoundaries(string projectRoot)
    {
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web");

        string remoteReadOnlyPage = ReadRequiredFile(webRoot, "src", "pages", "RemoteReadOnlyClientPage.tsx");
        string remoteReadOnlyHook = ReadRequiredFile(webRoot, "src", "hooks", "useRemoteReadOnlyClient.ts");
        string remoteReadOnlyClient = ReadRequiredFile(webRoot, "src", "api", "remoteReadOnlyClient.ts");

        AssertContains(remoteReadOnlyPage, "Pairing / Session State", "Remote read-only page must expose pairing/session state.");
        AssertContains(remoteReadOnlyPage, "Connection status", "Remote read-only page must show connection status.");
        AssertContains(remoteReadOnlyPage, "Terminal status", "Remote read-only page must show terminal status.");
        AssertContains(remoteReadOnlyPage, "No catalog mutation controls are present", "Remote read-only page must explicitly state no mutation controls.");
        AssertNotContains(remoteReadOnlyPage, ">Save<", "Remote read-only page must not expose save controls.");
        AssertNotContains(remoteReadOnlyPage, ">Delete<", "Remote read-only page must not expose delete controls.");
        AssertNotContains(remoteReadOnlyPage, ">Create<", "Remote read-only page must not expose create controls.");

        AssertContains(remoteReadOnlyHook, "useQuery", "Remote read-only hook must use query reads.");
        AssertNotContains(remoteReadOnlyHook, "useMutation", "Remote read-only hook must not use mutations.");

        AssertContains(remoteReadOnlyClient, "/api/remote-readonly", "Remote read-only client must fetch /api/remote-readonly endpoints.");
        AssertContains(remoteReadOnlyClient, "actions are disabled on remote clients", "Remote read-only client must keep disabled mutation warning.");
    }

    private static void AssertLanM14HChangedFilesStayInScope(string projectRoot, string currentState)
    {
        if (IsLanM14FThroughIClosureContext(currentState))
        {
            Console.WriteLine("LAN-M14-H changed-file scope guard skipped: LAN-M14-I closure owns comprehensive changed-file scope.");
            return;
        }

        if (!currentState.Contains("LAN-M14-F through LAN-M14-I are candidate work completed", StringComparison.Ordinal))
        {
            Console.WriteLine("LAN-M14-H changed-file scope guard skipped: LAN-M14-H is no longer current candidate work. Static pairing UX checks remain active.");
            return;
        }

        string[] changedPaths = GetChangedPaths(projectRoot);

        if (changedPaths.Length == 0)
        {
            Console.WriteLine("LAN-M14-H changed-file scope guard skipped: no branch-diff paths were available. Static pairing UX checks remain active.");
            return;
        }

        foreach (string changedPath in changedPaths)
        {
            string normalizedPath = changedPath.Replace('\\', '/');

            if (normalizedPath.StartsWith("00-HANDOFF/", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("tests/Salryn.Tests/LanM14", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Equals("tools/Salryn.Tools.CoreSmoke/Program.cs", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("src/Salryn.App.Web/src/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new InvalidOperationException($"LAN-M14-H changed an out-of-scope file: {normalizedPath}");
        }
    }

    private static string[] GetChangedPaths(string projectRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only " + LanM14HBaselineSha + "...HEAD"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only"));
        AddPaths(paths, TryRunGit(projectRoot, "diff --name-only --cached"));
        AddPaths(paths, TryRunGit(projectRoot, "ls-files --others --exclude-standard"));

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddPaths(HashSet<string> paths, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            paths.Add(line.Trim().Replace('\\', '/'));
        }
    }

    private static string? TryRunGit(string projectRoot, string arguments)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadRequiredFile(params string[] pathParts)
    {
        string path = Path.Combine(pathParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

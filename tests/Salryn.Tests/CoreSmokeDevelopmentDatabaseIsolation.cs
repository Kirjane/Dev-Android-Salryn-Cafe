using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Salryn.Tests;

internal static class CoreSmokeDevelopmentDatabaseIsolation
{
    internal const string EnvironmentVariable = "SALRYN_CORE_SMOKE_DEVELOPMENT_ROOT";

    private static readonly object SyncRoot = new();
    private static string? s_isolatedRoot;

    [ModuleInitializer]
    internal static void Configure()
    {
        if (!IsCoreSmokeProcess())
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            {
                return;
            }

            s_isolatedRoot = Path.Combine(
                Path.GetTempPath(),
                "salryn-coresmoke-devdb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(s_isolatedRoot);
            Environment.SetEnvironmentVariable(EnvironmentVariable, s_isolatedRoot);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        }
    }

    private static bool IsCoreSmokeProcess()
    {
        string processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (string.Equals(processName, "Salryn.Tools.CoreSmoke", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string commandLine = Environment.CommandLine;
        return commandLine.Contains("Salryn.Tools.CoreSmoke", StringComparison.OrdinalIgnoreCase);
    }

    private static void Cleanup()
    {
        lock (SyncRoot)
        {
            string? root = s_isolatedRoot;
            s_isolatedRoot = null;
            Environment.SetEnvironmentVariable(EnvironmentVariable, null);

            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

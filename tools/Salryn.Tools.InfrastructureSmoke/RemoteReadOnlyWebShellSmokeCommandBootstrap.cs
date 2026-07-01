using System.Runtime.CompilerServices;

internal static class RemoteReadOnlyWebShellSmokeCommandBootstrap
{
    [ModuleInitializer]
    internal static void RunIfRequested()
    {
        if (!Environment.GetCommandLineArgs().Skip(1).Any(arg => string.Equals(arg, "remote-readonly-web-shell", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RemoteReadOnlyWebShellSmokeChecks.Run(Directory.GetCurrentDirectory());
        System.Environment.ExitCode = 0;
        System.Environment.Exit(0);
    }
}

using System.Runtime.CompilerServices;

internal static class RemoteReadOnlyLanUsabilitySmokeCommandBootstrap
{
    [ModuleInitializer]
    internal static void RunIfRequested()
    {
        if (!Environment.GetCommandLineArgs().Skip(1).Any(arg => string.Equals(arg, "remote-readonly-lan-usability", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        RemoteReadOnlyLanUsabilitySmokeChecks.Run(Directory.GetCurrentDirectory());
        System.Environment.ExitCode = 0;
        System.Environment.Exit(0);
    }
}

using System.Runtime.CompilerServices;
using Salryn.Tests;

namespace Salryn.Tools.CoreSmoke;

internal static class LanM8ReactRemoteReadOnlyShellCoreSmokeBootstrap
{
    [ModuleInitializer]
    internal static void RunLanM8Guard()
    {
        LanM8ReactRemoteReadOnlyShellRuntimeChecks.Run();
    }
}

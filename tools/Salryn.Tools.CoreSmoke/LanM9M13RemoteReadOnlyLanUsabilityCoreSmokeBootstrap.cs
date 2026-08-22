using System.Runtime.CompilerServices;
using Salryn.Tests;

namespace Salryn.Tools.CoreSmoke;

internal static class LanM9M13RemoteReadOnlyLanUsabilityCoreSmokeBootstrap
{
    [ModuleInitializer]
    internal static void RunLanM9M13Guard()
    {
        LanM9M13RemoteReadOnlyLanUsabilityChecks.Run();
    }
}

namespace Salryn.Tests;

/// <summary>
/// PRE-I code-proven QA hardening orchestrator.
/// Prefer behavioral isolated-root checks; use structural source contracts only where UI has no harness.
/// </summary>
public static class PreICodeProvenQaHardeningChecks
{
    public static void Run()
    {
        PreICodeProvenFreshStateChecks.Run();
        PreICodeProvenBackupRestoreChecks.Run();
        PreICodeProvenTerminalLifecycleChecks.Run();
        PreICodeProvenClientSurfaceGuards.Run();
    }
}

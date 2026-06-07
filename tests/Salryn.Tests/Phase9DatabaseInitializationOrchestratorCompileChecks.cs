using Salryn.Infrastructure.Data.Initialization;

namespace Salryn.Tests;

public static class Phase9DatabaseInitializationOrchestratorCompileChecks
{
    public static void Run()
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment("C:\\SalrynDevCheck");

        if (string.IsNullOrWhiteSpace(orchestrator.DatabasePath))
        {
            throw new InvalidOperationException("Database path check failed.");
        }

        if (orchestrator.ConfiguredMigrationCount != 1)
        {
            throw new InvalidOperationException("Configured migration count check failed.");
        }

        var statusResult = orchestrator.CheckStatus();

        if (statusResult.IsFailure)
        {
            throw new InvalidOperationException("Database initialization status check failed.");
        }

        if (statusResult.Value.ConfiguredMigrationCount != 1)
        {
            throw new InvalidOperationException("Status migration count check failed.");
        }
    }
}

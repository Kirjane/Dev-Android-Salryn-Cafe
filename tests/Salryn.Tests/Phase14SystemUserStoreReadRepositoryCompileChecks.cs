using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase14SystemUserStoreReadRepositoryCompileChecks
{
    public static void Run()
    {
        DbPaths paths = DbPaths.CreateDevelopment("C:\\SalrynDevCheck");

        SqliteConnectionFactory connectionFactory = new(paths);

        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        IStoreSettingsReadRepository storeSettingsRepository =
            repositoryFactory.CreateStoreSettingsReadRepository();

        ICashierSessionReadRepository cashierSessionRepository =
            repositoryFactory.CreateCashierSessionReadRepository();

        IAuditReadRepository auditRepository =
            repositoryFactory.CreateAuditReadRepository();

        IErrorLogReadRepository errorLogRepository =
            repositoryFactory.CreateErrorLogReadRepository();

        SqliteUserReadRepository concreteUserRepository = new(connectionFactory);
        SqliteStoreSettingsReadRepository concreteStoreSettingsRepository = new(connectionFactory);
        SqliteCashierSessionReadRepository concreteCashierSessionRepository = new(connectionFactory);
        SqliteAuditReadRepository concreteAuditRepository = new(connectionFactory);
        SqliteErrorLogReadRepository concreteErrorLogRepository = new(connectionFactory);

        if (userRepository is null)
        {
            throw new InvalidOperationException("User repository factory check failed.");
        }

        if (storeSettingsRepository is null)
        {
            throw new InvalidOperationException("Store settings repository factory check failed.");
        }

        if (cashierSessionRepository is null)
        {
            throw new InvalidOperationException("Cashier session repository factory check failed.");
        }

        if (auditRepository is null)
        {
            throw new InvalidOperationException("Audit repository factory check failed.");
        }

        if (errorLogRepository is null)
        {
            throw new InvalidOperationException("Error log repository factory check failed.");
        }

        _ = concreteUserRepository;
        _ = concreteStoreSettingsRepository;
        _ = concreteCashierSessionRepository;
        _ = concreteAuditRepository;
        _ = concreteErrorLogRepository;
    }
}

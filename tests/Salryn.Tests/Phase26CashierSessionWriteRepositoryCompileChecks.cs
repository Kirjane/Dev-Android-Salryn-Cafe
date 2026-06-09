using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Phase26CashierSessionWriteRepositoryCompileChecks
{
    public static void Compile()
    {
        DbPaths paths = DbPaths.CreateDevelopment("compile-only");
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory factory = new(connectionFactory);

        ICashierSessionWriteRepository repository =
            factory.CreateCashierSessionWriteRepository();

        Guid sessionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        CashierSession session = new()
        {
            Id = sessionId,
            UserId = userId,
            OpenedAt = DateTimeOffset.UtcNow,
            ClosedAt = null,
            OpeningCash = Money.FromCents(1000),
            ExpectedCash = Money.FromCents(1000),
            CountedCash = null,
            Variance = Money.Zero,
            Status = CashierSessionStatus.Open,
            Notes = "Compile check session."
        };

        SqliteCashierSessionWriteRepository concreteRepository = new(connectionFactory);

        _ = repository;
        _ = concreteRepository;
        _ = session;
        _ = repository.OpenAsync(session);
        _ = repository.CloseAsync(sessionId, userId, Money.FromCents(1000), "close");
        _ = repository.ForceCloseAsync(sessionId, userId, null, "force close");
    }
}

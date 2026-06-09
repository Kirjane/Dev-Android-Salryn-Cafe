using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class CashierSessionReadWriteIntegrationSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== CASHIER SESSION READ/WRITE INTEGRATION SMOKE CHECKS =====");

        EnsureDatabaseReady(projectRoot);

        DbPaths paths = DbPaths.CreateDevelopment(projectRoot);
        SqliteConnectionFactory connectionFactory = new(paths);
        SqliteRepositoryFactory repositoryFactory = new(connectionFactory);

        ICashierSessionWriteRepository writeRepository =
            repositoryFactory.CreateCashierSessionWriteRepository();

        ICashierSessionReadRepository readRepository =
            repositoryFactory.CreateCashierSessionReadRepository();

        IUserReadRepository userRepository =
            repositoryFactory.CreateUserReadRepository();

        UserReadModel cashier = GetSmokeCashier(userRepository);

        ExpectNoOpenSession(
            readRepository,
            cashier.Id,
            "CashierSessionReadWrite.StartNoOpenSession");

        ExpectMissingSession(
            readRepository,
            Guid.NewGuid(),
            "CashierSessionReadWrite.MissingSessionBeforeWrites");

        CashierSession firstSession = CreateOpenSession(
            cashier.Id,
            openingCashCents: 10000,
            notes: "Phase 28 first open session");

        RequireSuccess(
            writeRepository.OpenAsync(firstSession).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.OpenFirstSession");

        CashierSessionReadModel firstById = RequireSession(
            readRepository.FindByIdAsync(firstSession.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindFirstById");

        VerifyOpenSession(
            firstById,
            firstSession,
            cashier.DisplayName,
            expectedNotes: "Phase 28 first open session");

        CashierSessionReadModel firstOpenByUser = RequireSession(
            readRepository.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindFirstOpenByUser");

        VerifySameSession(
            firstOpenByUser,
            firstById,
            "CashierSessionReadWrite.FirstOpenByUserMatchesById");

        RequireSuccess(
            writeRepository.CloseAsync(
                firstSession.Id,
                cashier.Id,
                Money.FromCents(12345),
                "Phase 28 close first session").GetAwaiter().GetResult(),
            "CashierSessionReadWrite.CloseFirstSession");

        CashierSessionReadModel firstClosed = RequireSession(
            readRepository.FindByIdAsync(firstSession.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindFirstClosedById");

        VerifyClosedSession(
            firstClosed,
            firstSession,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            countedCashCents: 12345,
            varianceCents: 2345,
            expectedNotes: "Phase 28 close first session");

        ExpectNoOpenSession(
            readRepository,
            cashier.Id,
            "CashierSessionReadWrite.NoOpenAfterFirstClose");

        CashierSession secondSession = CreateOpenSession(
            cashier.Id,
            openingCashCents: 8000,
            notes: "Phase 28 second open after close");

        RequireSuccess(
            writeRepository.OpenAsync(secondSession).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.OpenSecondSession");

        CashierSessionReadModel secondOpenByUser = RequireSession(
            readRepository.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindSecondOpenByUser");

        VerifyOpenSession(
            secondOpenByUser,
            secondSession,
            cashier.DisplayName,
            expectedNotes: "Phase 28 second open after close");

        CashierSessionReadModel firstStillClosed = RequireSession(
            readRepository.FindByIdAsync(firstSession.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindFirstStillClosed");

        VerifyClosedSession(
            firstStillClosed,
            firstSession,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            countedCashCents: 12345,
            varianceCents: 2345,
            expectedNotes: "Phase 28 close first session");

        RequireSuccess(
            writeRepository.ForceCloseAsync(
                secondSession.Id,
                cashier.Id,
                Money.FromCents(7000),
                "Phase 28 force close second session").GetAwaiter().GetResult(),
            "CashierSessionReadWrite.ForceCloseSecondSession");

        CashierSessionReadModel secondForceClosed = RequireSession(
            readRepository.FindByIdAsync(secondSession.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindSecondForceClosed");

        VerifyClosedSession(
            secondForceClosed,
            secondSession,
            cashier.DisplayName,
            CashierSessionStatus.ForceClosed,
            countedCashCents: 7000,
            varianceCents: -1000,
            expectedNotes: "Phase 28 force close second session");

        ExpectNoOpenSession(
            readRepository,
            cashier.Id,
            "CashierSessionReadWrite.NoOpenAfterSecondForceClose");

        CashierSession thirdSession = CreateOpenSession(
            cashier.Id,
            openingCashCents: 0,
            notes: "   ");

        RequireSuccess(
            writeRepository.OpenAsync(thirdSession).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.OpenThirdSessionBlankNotes");

        CashierSessionReadModel thirdOpen = RequireSession(
            readRepository.FindByIdAsync(thirdSession.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindThirdOpen");

        VerifyOpenSession(
            thirdOpen,
            thirdSession,
            cashier.DisplayName,
            expectedNotes: null);

        RequireSuccess(
            writeRepository.CloseAsync(
                thirdSession.Id,
                cashier.Id,
                Money.Zero,
                null).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.CloseThirdSessionNullNotes");

        CashierSessionReadModel thirdClosed = RequireSession(
            readRepository.FindByIdAsync(thirdSession.Id).GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindThirdClosed");

        VerifyClosedSession(
            thirdClosed,
            thirdSession,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            countedCashCents: 0,
            varianceCents: 0,
            expectedNotes: null);

        ExpectNoOpenSession(
            readRepository,
            cashier.Id,
            "CashierSessionReadWrite.FinalNoOpenSession");

        Console.WriteLine("Cashier session read/write integration smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void EnsureDatabaseReady(string projectRoot)
    {
        DatabaseInitializationOrchestrator orchestrator =
            DefaultDatabaseInitializationFactory.CreateDevelopment(projectRoot);

        bool needsRepositoryBootstrap = !File.Exists(orchestrator.DatabasePath);

        if (!needsRepositoryBootstrap)
        {
            DatabaseInitializationStatus status = RequireSuccess(
                orchestrator.CheckStatus(),
                "CashierSessionReadWrite.CheckStatus");

            needsRepositoryBootstrap =
                !status.FirstRunStatus.IsDatabaseInitialized ||
                status.FirstRunStatus.IsFirstRunRequired;
        }

        if (needsRepositoryBootstrap)
        {
            RepositorySmokeChecks.Run(projectRoot);
        }
    }

    private static UserReadModel GetSmokeCashier(IUserReadRepository userRepository)
    {
        UserReadModel? admin = RequireSuccess(
            userRepository.FindByUsernameAsync("admin-smoke").GetAwaiter().GetResult(),
            "CashierSessionReadWrite.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "CashierSessionReadWrite.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Cashier session read/write integration smoke expected at least one user.");
        }

        return users[0];
    }

    private static CashierSession CreateOpenSession(
        Guid userId,
        long openingCashCents,
        string? notes)
    {
        Money openingCash = Money.FromCents(openingCashCents);

        return new CashierSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OpenedAt = DateTimeOffset.UtcNow,
            ClosedAt = null,
            OpeningCash = openingCash,
            ExpectedCash = openingCash,
            CountedCash = null,
            Variance = Money.Zero,
            Status = CashierSessionStatus.Open,
            Notes = notes
        };
    }

    private static void VerifyOpenSession(
        CashierSessionReadModel actual,
        CashierSession expected,
        string expectedUserDisplayName,
        string? expectedNotes)
    {
        if (actual.Id != expected.Id ||
            actual.UserId != expected.UserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.OpenedAt != expected.OpenedAt ||
            actual.ClosedAt is not null ||
            actual.OpeningCash.Cents != expected.OpeningCash.Cents ||
            actual.ExpectedCash.Cents != expected.ExpectedCash.Cents ||
            actual.CountedCash is not null ||
            actual.Variance.Cents != 0 ||
            actual.Status != CashierSessionStatus.Open ||
            !string.Equals(actual.Notes, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Open cashier session read/write verification failed.");
        }
    }

    private static void VerifyClosedSession(
        CashierSessionReadModel actual,
        CashierSession original,
        string expectedUserDisplayName,
        CashierSessionStatus expectedStatus,
        long? countedCashCents,
        long varianceCents,
        string? expectedNotes)
    {
        long? actualCountedCashCents = actual.CountedCash?.Cents;

        if (actual.Id != original.Id ||
            actual.UserId != original.UserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.OpenedAt != original.OpenedAt ||
            actual.ClosedAt is null ||
            actual.ClosedAt.Value < original.OpenedAt ||
            actual.OpeningCash.Cents != original.OpeningCash.Cents ||
            actual.ExpectedCash.Cents != original.ExpectedCash.Cents ||
            actualCountedCashCents != countedCashCents ||
            actual.Variance.Cents != varianceCents ||
            actual.Status != expectedStatus ||
            !string.Equals(actual.Notes, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Closed cashier session read/write verification failed.");
        }
    }

    private static void VerifySameSession(
        CashierSessionReadModel actual,
        CashierSessionReadModel expected,
        string area)
    {
        if (actual.Id != expected.Id ||
            actual.UserId != expected.UserId ||
            !string.Equals(actual.UserDisplayName, expected.UserDisplayName, StringComparison.Ordinal) ||
            actual.OpenedAt != expected.OpenedAt ||
            actual.ClosedAt != expected.ClosedAt ||
            actual.OpeningCash.Cents != expected.OpeningCash.Cents ||
            actual.ExpectedCash.Cents != expected.ExpectedCash.Cents ||
            actual.CountedCash?.Cents != expected.CountedCash?.Cents ||
            actual.Variance.Cents != expected.Variance.Cents ||
            actual.Status != expected.Status ||
            !string.Equals(actual.Notes, expected.Notes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{area} session snapshot mismatch.");
        }
    }

    private static void ExpectNoOpenSession(
        ICashierSessionReadRepository readRepository,
        Guid userId,
        string area)
    {
        CashierSessionReadModel? openSession = RequireSuccess(
            readRepository.FindOpenSessionByUserIdAsync(userId).GetAwaiter().GetResult(),
            area);

        if (openSession is not null)
        {
            throw new InvalidOperationException($"{area} expected no open cashier session.");
        }
    }

    private static void ExpectMissingSession(
        ICashierSessionReadRepository readRepository,
        Guid sessionId,
        string area)
    {
        CashierSessionReadModel? session = RequireSuccess(
            readRepository.FindByIdAsync(sessionId).GetAwaiter().GetResult(),
            area);

        if (session is not null)
        {
            throw new InvalidOperationException($"{area} expected missing cashier session.");
        }
    }

    private static CashierSessionReadModel RequireSession(
        Result<CashierSessionReadModel?> result,
        string area)
    {
        CashierSessionReadModel? session = RequireSuccess(result, area);

        if (session is null)
        {
            throw new InvalidOperationException($"{area} returned no cashier session.");
        }

        return session;
    }

    private static T RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }
}


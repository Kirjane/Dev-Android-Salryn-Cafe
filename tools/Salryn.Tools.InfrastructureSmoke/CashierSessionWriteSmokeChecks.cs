using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class CashierSessionWriteSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== CASHIER SESSION WRITE SMOKE CHECKS =====");

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

        CashierSession firstSession = CreateOpenSession(
            cashier.Id,
            openingCashCents: 1000,
            notes: "Phase 26 first open session");

        RequireSuccess(
            writeRepository.OpenAsync(firstSession).GetAwaiter().GetResult(),
            "CashierSessionSmoke.OpenFirstSession");

        CashierSessionReadModel openByUser = RequireSession(
            readRepository.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionSmoke.FindOpenByUser");

        VerifyOpenSession(openByUser, firstSession, cashier.DisplayName);

        CashierSessionReadModel byId = RequireSession(
            readRepository.FindByIdAsync(firstSession.Id).GetAwaiter().GetResult(),
            "CashierSessionSmoke.FindById");

        VerifyOpenSession(byId, firstSession, cashier.DisplayName);

        CashierSession duplicateOpen = CreateOpenSession(
            cashier.Id,
            openingCashCents: 2000,
            notes: "duplicate open should fail");

        ExpectFailure(
            writeRepository.OpenAsync(duplicateOpen).GetAwaiter().GetResult(),
            "CashierSessionSmoke.DuplicateOpen",
            ErrorType.Conflict,
            "CashierSessions.OpenSessionExists");

        RequireSuccess(
            writeRepository.CloseAsync(
                firstSession.Id,
                cashier.Id,
                Money.FromCents(1250),
                "Phase 26 close first session").GetAwaiter().GetResult(),
            "CashierSessionSmoke.CloseFirstSession");

        CashierSessionReadModel closed = RequireSession(
            readRepository.FindByIdAsync(firstSession.Id).GetAwaiter().GetResult(),
            "CashierSessionSmoke.FindClosedById");

        VerifyClosedSession(
            closed,
            firstSession,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            countedCashCents: 1250,
            varianceCents: 250,
            expectedNotes: "Phase 26 close first session");

        CashierSessionReadModel? noOpen = RequireSuccess(
            readRepository.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionSmoke.FindNoOpenAfterClose");

        if (noOpen is not null)
        {
            throw new InvalidOperationException("Cashier session close verification failed. Open session still exists.");
        }

        ExpectFailure(
            writeRepository.CloseAsync(
                firstSession.Id,
                cashier.Id,
                Money.FromCents(1250),
                "close already closed").GetAwaiter().GetResult(),
            "CashierSessionSmoke.CloseAlreadyClosed",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        CashierSession secondSession = CreateOpenSession(
            cashier.Id,
            openingCashCents: 3000,
            notes: "Phase 26 second open session");

        RequireSuccess(
            writeRepository.OpenAsync(secondSession).GetAwaiter().GetResult(),
            "CashierSessionSmoke.OpenSecondSession");

        RequireSuccess(
            writeRepository.ForceCloseAsync(
                secondSession.Id,
                cashier.Id,
                Money.FromCents(2500),
                "Phase 26 force close second session").GetAwaiter().GetResult(),
            "CashierSessionSmoke.ForceCloseSecondSession");

        CashierSessionReadModel forceClosed = RequireSession(
            readRepository.FindByIdAsync(secondSession.Id).GetAwaiter().GetResult(),
            "CashierSessionSmoke.FindForceClosedById");

        VerifyClosedSession(
            forceClosed,
            secondSession,
            cashier.DisplayName,
            CashierSessionStatus.ForceClosed,
            countedCashCents: 2500,
            varianceCents: -500,
            expectedNotes: "Phase 26 force close second session");

        Console.WriteLine("Cashier session write smoke checks succeeded.");
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
                "CashierSessionSmoke.CheckStatus");

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
            "CashierSessionSmoke.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "CashierSessionSmoke.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Cashier session smoke expected at least one user.");
        }

        return users[0];
    }

    private static CashierSession CreateOpenSession(
        Guid userId,
        long openingCashCents,
        string notes)
    {
        return new CashierSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OpenedAt = DateTimeOffset.UtcNow,
            ClosedAt = null,
            OpeningCash = Money.FromCents(openingCashCents),
            ExpectedCash = Money.FromCents(openingCashCents),
            CountedCash = null,
            Variance = Money.Zero,
            Status = CashierSessionStatus.Open,
            Notes = notes
        };
    }

    private static void VerifyOpenSession(
        CashierSessionReadModel actual,
        CashierSession expected,
        string expectedUserDisplayName)
    {
        if (actual.Id != expected.Id ||
            actual.UserId != expected.UserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.ClosedAt is not null ||
            actual.OpeningCash.Cents != expected.OpeningCash.Cents ||
            actual.ExpectedCash.Cents != expected.ExpectedCash.Cents ||
            actual.CountedCash is not null ||
            actual.Variance.Cents != 0 ||
            actual.Status != CashierSessionStatus.Open ||
            !string.Equals(actual.Notes, expected.Notes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Open cashier session verification failed.");
        }
    }

    private static void VerifyClosedSession(
        CashierSessionReadModel actual,
        CashierSession original,
        string expectedUserDisplayName,
        CashierSessionStatus expectedStatus,
        long countedCashCents,
        long varianceCents,
        string expectedNotes)
    {
        if (actual.Id != original.Id ||
            actual.UserId != original.UserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.ClosedAt is null ||
            actual.OpeningCash.Cents != original.OpeningCash.Cents ||
            actual.ExpectedCash.Cents != original.ExpectedCash.Cents ||
            actual.CountedCash?.Cents != countedCashCents ||
            actual.Variance.Cents != varianceCents ||
            actual.Status != expectedStatus ||
            !string.Equals(actual.Notes, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Closed cashier session verification failed.");
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

    private static void ExpectFailure(
        Result result,
        string area,
        ErrorType expectedType,
        string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"{area} expected failure but succeeded.");
        }

        if (result.ErrorDetail.Type != expectedType ||
            !string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{area} expected {expectedType}/{expectedCode}, got {result.ErrorDetail.Type}/{result.ErrorDetail.Code}: {result.Error}");
        }
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

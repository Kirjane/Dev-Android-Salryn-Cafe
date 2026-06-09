using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class CashierSessionServiceEdgeSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== CASHIER SESSION SERVICE EDGE SMOKE CHECKS =====");

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

        CashierSessionService service = new(writeRepository, readRepository);

        ExpectFailure(
            service.FindOpenSessionByUserIdAsync(Guid.Empty).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.FindOpenInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            service.FindByIdAsync(Guid.Empty).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.FindByIdInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        ExpectMissingSession(
            service,
            Guid.NewGuid(),
            "CashierSessionServiceEdge.FindMissingSession");

        ExpectNoOpenSession(
            service,
            cashier.Id,
            "CashierSessionServiceEdge.StartNoOpen");

        ExpectFailure(
            service.OpenAsync(new OpenCashierSessionRequest(
                Guid.Empty,
                Money.FromCents(1000),
                "empty user")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.OpenInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(-1),
                "negative opening cash")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.OpenNegativeOpeningCash",
            ErrorType.Validation,
            "CashierSessions.InvalidMoneyAmount");

        ExpectFailure(
            service.OpenAsync(new OpenCashierSessionRequest(
                Guid.NewGuid(),
                Money.FromCents(1000),
                "missing user reference")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.OpenMissingUserReference",
            ErrorType.Validation,
            "CashierSessions.InvalidReference");

        CashierSessionReadModel baseOpen = RequireSuccess(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(1000),
                "Phase 30 base open")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.OpenBase");

        VerifyOpenSession(
            baseOpen,
            cashier.Id,
            cashier.DisplayName,
            openingCashCents: 1000,
            expectedNotes: "Phase 30 base open");

        ExpectFailure(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(2000),
                "duplicate open")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.DuplicateOpen",
            ErrorType.Conflict,
            "CashierSessions.OpenSessionExists");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                Guid.Empty,
                cashier.Id,
                Money.FromCents(1000),
                "empty session id")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                baseOpen.Id,
                Guid.Empty,
                Money.FromCents(1000),
                "empty user id")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                baseOpen.Id,
                cashier.Id,
                Money.FromCents(-1),
                "negative counted cash")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseNegativeCountedCash",
            ErrorType.Validation,
            "CashierSessions.InvalidMoneyAmount");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                Guid.NewGuid(),
                cashier.Id,
                Money.FromCents(1000),
                "missing session")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseMissingSession",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                baseOpen.Id,
                Guid.NewGuid(),
                Money.FromCents(1000),
                "wrong user")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseWrongUser",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        CashierSessionReadModel baseClosed = RequireSuccess(
            service.CloseAsync(new CloseCashierSessionRequest(
                baseOpen.Id,
                cashier.Id,
                Money.FromCents(1250),
                "Phase 30 base close")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseBase");

        VerifyClosedSession(
            baseClosed,
            baseOpen.Id,
            cashier.Id,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            openingCashCents: 1000,
            expectedCashCents: 1000,
            countedCashCents: 1250,
            varianceCents: 250,
            expectedNotes: "Phase 30 base close");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                baseOpen.Id,
                cashier.Id,
                Money.FromCents(1250),
                "already closed")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.CloseAlreadyClosed",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        ExpectNoOpenSession(
            service,
            cashier.Id,
            "CashierSessionServiceEdge.NoOpenAfterBaseClose");

        CashierSessionReadModel forceOpen = RequireSuccess(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(3000),
                "Phase 30 force open")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.OpenForceSession");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                Guid.Empty,
                cashier.Id,
                Money.FromCents(3000),
                "empty session id")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                Guid.Empty,
                Money.FromCents(3000),
                "empty user id")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                cashier.Id,
                Money.FromCents(-1),
                "negative counted cash")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseNegativeCountedCash",
            ErrorType.Validation,
            "CashierSessions.InvalidMoneyAmount");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                cashier.Id,
                Money.FromCents(3000),
                "   ")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseBlankReason",
            ErrorType.Validation,
            "CashierSessions.ForceCloseReasonRequired");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                cashier.Id,
                Money.FromCents(3000),
                null!)).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseNullReason",
            ErrorType.Validation,
            "CashierSessions.ForceCloseReasonRequired");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                Guid.NewGuid(),
                cashier.Id,
                Money.FromCents(3000),
                "missing session")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseMissingSession",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                Guid.NewGuid(),
                Money.FromCents(3000),
                "wrong user")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseWrongUser",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        CashierSessionReadModel forceClosed = RequireSuccess(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                cashier.Id,
                Money.FromCents(2500),
                "Phase 30 force close with count")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseWithCount");

        VerifyClosedSession(
            forceClosed,
            forceOpen.Id,
            cashier.Id,
            cashier.DisplayName,
            CashierSessionStatus.ForceClosed,
            openingCashCents: 3000,
            expectedCashCents: 3000,
            countedCashCents: 2500,
            varianceCents: -500,
            expectedNotes: "Phase 30 force close with count");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                forceOpen.Id,
                cashier.Id,
                Money.FromCents(2500),
                "already force closed")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseAlreadyClosed",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        ExpectNoOpenSession(
            service,
            cashier.Id,
            "CashierSessionServiceEdge.NoOpenAfterForceClose");

        CashierSessionReadModel blankNotesOpen = RequireSuccess(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.Zero,
                "   ")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.OpenBlankNotes");

        VerifyOpenSession(
            blankNotesOpen,
            cashier.Id,
            cashier.DisplayName,
            openingCashCents: 0,
            expectedNotes: null);

        CashierSessionReadModel forceClosedWithoutCount = RequireSuccess(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                blankNotesOpen.Id,
                cashier.Id,
                null,
                "   Phase 30 trimmed reason   ")).GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.ForceCloseWithoutCountTrimmedReason");

        VerifyClosedSession(
            forceClosedWithoutCount,
            blankNotesOpen.Id,
            cashier.Id,
            cashier.DisplayName,
            CashierSessionStatus.ForceClosed,
            openingCashCents: 0,
            expectedCashCents: 0,
            countedCashCents: null,
            varianceCents: 0,
            expectedNotes: "Phase 30 trimmed reason");

        ExpectNoOpenSession(
            service,
            cashier.Id,
            "CashierSessionServiceEdge.FinalNoOpen");

        Console.WriteLine("Cashier session service edge smoke checks succeeded.");
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
                "CashierSessionServiceEdge.CheckStatus");

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
            "CashierSessionServiceEdge.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "CashierSessionServiceEdge.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Cashier session service edge smoke expected at least one user.");
        }

        return users[0];
    }

    private static void VerifyOpenSession(
        CashierSessionReadModel actual,
        Guid expectedUserId,
        string expectedUserDisplayName,
        long openingCashCents,
        string? expectedNotes)
    {
        if (actual.Id == Guid.Empty ||
            actual.UserId != expectedUserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.ClosedAt is not null ||
            actual.OpeningCash.Cents != openingCashCents ||
            actual.ExpectedCash.Cents != openingCashCents ||
            actual.CountedCash is not null ||
            actual.Variance.Cents != 0 ||
            actual.Status != CashierSessionStatus.Open ||
            !string.Equals(actual.Notes, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cashier session service edge open verification failed.");
        }
    }

    private static void VerifyClosedSession(
        CashierSessionReadModel actual,
        Guid expectedSessionId,
        Guid expectedUserId,
        string expectedUserDisplayName,
        CashierSessionStatus expectedStatus,
        long openingCashCents,
        long expectedCashCents,
        long? countedCashCents,
        long varianceCents,
        string? expectedNotes)
    {
        if (actual.Id != expectedSessionId ||
            actual.UserId != expectedUserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.ClosedAt is null ||
            actual.OpeningCash.Cents != openingCashCents ||
            actual.ExpectedCash.Cents != expectedCashCents ||
            actual.CountedCash?.Cents != countedCashCents ||
            actual.Variance.Cents != varianceCents ||
            actual.Status != expectedStatus ||
            !string.Equals(actual.Notes, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cashier session service edge closed verification failed.");
        }
    }

    private static void ExpectNoOpenSession(
        ICashierSessionService service,
        Guid userId,
        string area)
    {
        CashierSessionReadModel? openSession = RequireSuccess(
            service.FindOpenSessionByUserIdAsync(userId).GetAwaiter().GetResult(),
            area);

        if (openSession is not null)
        {
            throw new InvalidOperationException($"{area} expected no open cashier session.");
        }
    }

    private static void ExpectMissingSession(
        ICashierSessionService service,
        Guid sessionId,
        string area)
    {
        CashierSessionReadModel? session = RequireSuccess(
            service.FindByIdAsync(sessionId).GetAwaiter().GetResult(),
            area);

        if (session is not null)
        {
            throw new InvalidOperationException($"{area} expected missing cashier session.");
        }
    }

    private static void ExpectFailure<T>(
        Result<T> result,
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
}

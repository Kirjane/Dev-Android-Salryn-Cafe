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

internal static class CashierSessionServiceSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== CASHIER SESSION SERVICE SMOKE CHECKS =====");

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
            "CashierSessionService.FindOpenInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            service.FindByIdAsync(Guid.Empty).GetAwaiter().GetResult(),
            "CashierSessionService.FindByIdInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        CashierSessionReadModel? startOpen = RequireSuccess(
            service.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionService.StartNoOpen");

        if (startOpen is not null)
        {
            throw new InvalidOperationException("Cashier session service smoke expected no open session at start.");
        }

        ExpectFailure(
            service.OpenAsync(new OpenCashierSessionRequest(
                Guid.Empty,
                Money.FromCents(1000),
                "invalid user")).GetAwaiter().GetResult(),
            "CashierSessionService.OpenInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        CashierSessionReadModel opened = RequireSuccess(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(5000),
                "Phase 29 service open")).GetAwaiter().GetResult(),
            "CashierSessionService.Open");

        VerifyOpenSession(
            opened,
            cashier.Id,
            cashier.DisplayName,
            openingCashCents: 5000,
            expectedNotes: "Phase 29 service open");

        CashierSessionReadModel? openedByUser = RequireSuccess(
            service.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionService.FindOpenByUser");

        if (openedByUser is null || openedByUser.Id != opened.Id)
        {
            throw new InvalidOperationException("Cashier session service open-by-user verification failed.");
        }

        ExpectFailure(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(7000),
                "duplicate open")).GetAwaiter().GetResult(),
            "CashierSessionService.DuplicateOpen",
            ErrorType.Conflict,
            "CashierSessions.OpenSessionExists");

        ExpectFailure(
            service.CloseAsync(new CloseCashierSessionRequest(
                Guid.Empty,
                cashier.Id,
                Money.FromCents(5000),
                "invalid session")).GetAwaiter().GetResult(),
            "CashierSessionService.CloseInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        CashierSessionReadModel closed = RequireSuccess(
            service.CloseAsync(new CloseCashierSessionRequest(
                opened.Id,
                cashier.Id,
                Money.FromCents(5500),
                "Phase 29 service close")).GetAwaiter().GetResult(),
            "CashierSessionService.Close");

        VerifyClosedSession(
            closed,
            opened.Id,
            cashier.Id,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            openingCashCents: 5000,
            expectedCashCents: 5000,
            countedCashCents: 5500,
            varianceCents: 500,
            expectedNotes: "Phase 29 service close");

        CashierSessionReadModel? noOpenAfterClose = RequireSuccess(
            service.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionService.NoOpenAfterClose");

        if (noOpenAfterClose is not null)
        {
            throw new InvalidOperationException("Cashier session service close left an open session.");
        }

        CashierSessionReadModel secondOpened = RequireSuccess(
            service.OpenAsync(new OpenCashierSessionRequest(
                cashier.Id,
                Money.FromCents(9000),
                "Phase 29 service force open")).GetAwaiter().GetResult(),
            "CashierSessionService.OpenSecond");

        ExpectFailure(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                secondOpened.Id,
                cashier.Id,
                Money.FromCents(9000),
                "   ")).GetAwaiter().GetResult(),
            "CashierSessionService.ForceCloseBlankReason",
            ErrorType.Validation,
            "CashierSessions.ForceCloseReasonRequired");

        CashierSessionReadModel forceClosed = RequireSuccess(
            service.ForceCloseAsync(new ForceCloseCashierSessionRequest(
                secondOpened.Id,
                cashier.Id,
                null,
                "Phase 29 service force close without count")).GetAwaiter().GetResult(),
            "CashierSessionService.ForceCloseWithoutCount");

        VerifyClosedSession(
            forceClosed,
            secondOpened.Id,
            cashier.Id,
            cashier.DisplayName,
            CashierSessionStatus.ForceClosed,
            openingCashCents: 9000,
            expectedCashCents: 9000,
            countedCashCents: null,
            varianceCents: 0,
            expectedNotes: "Phase 29 service force close without count");

        CashierSessionReadModel? finalOpen = RequireSuccess(
            service.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionService.FinalNoOpen");

        if (finalOpen is not null)
        {
            throw new InvalidOperationException("Cashier session service smoke left an open session.");
        }

        Console.WriteLine("Cashier session service smoke checks succeeded.");
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
                "CashierSessionService.CheckStatus");

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
            "CashierSessionService.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "CashierSessionService.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Cashier session service smoke expected at least one user.");
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
            throw new InvalidOperationException("Cashier session service open verification failed.");
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
            throw new InvalidOperationException("Cashier session service closed verification failed.");
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

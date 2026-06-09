using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Paths;

internal static class CashierSessionWriteEdgeSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== CASHIER SESSION WRITE EDGE SMOKE CHECKS =====");

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

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                cashier.Id,
                1000,
                "invalid empty session id",
                sessionIdOverride: Guid.Empty)).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                Guid.Empty,
                1000,
                "invalid empty user id")).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                cashier.Id,
                1000,
                "invalid status",
                statusOverride: CashierSessionStatus.Closed)).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenInvalidStatus",
            ErrorType.Validation,
            "CashierSessions.OpenStatusRequired");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                cashier.Id,
                1000,
                "open cannot have close data",
                closedAtOverride: DateTimeOffset.UtcNow,
                countedCashOverride: Money.FromCents(1000))).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenWithCloseData",
            ErrorType.Validation,
            "CashierSessions.OpenSessionCannotHaveCloseData");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                cashier.Id,
                1000,
                "expected mismatch",
                expectedCashOverride: Money.FromCents(900))).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenExpectedMismatch",
            ErrorType.Validation,
            "CashierSessions.ExpectedCashMustMatchOpeningCash");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                cashier.Id,
                1000,
                "variance not zero",
                varianceOverride: Money.FromCents(1))).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenVarianceNotZero",
            ErrorType.Validation,
            "CashierSessions.OpenVarianceMustBeZero");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                Guid.NewGuid(),
                1000,
                "missing user reference")).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenInvalidReference",
            ErrorType.Validation,
            "CashierSessions.InvalidReference");

        CashierSession baseSession = CreateOpenSession(
            cashier.Id,
            1000,
            "Phase 27 base session");

        RequireSuccess(
            writeRepository.OpenAsync(baseSession).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenBaseSession");

        ExpectFailure(
            writeRepository.OpenAsync(CreateOpenSession(
                cashier.Id,
                2000,
                "duplicate open session")).GetAwaiter().GetResult(),
            "CashierSessionEdge.DuplicateOpenSession",
            ErrorType.Conflict,
            "CashierSessions.OpenSessionExists");

        ExpectFailure(
            writeRepository.CloseAsync(
                Guid.Empty,
                cashier.Id,
                Money.FromCents(1000),
                "empty session id").GetAwaiter().GetResult(),
            "CashierSessionEdge.CloseInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        ExpectFailure(
            writeRepository.CloseAsync(
                baseSession.Id,
                Guid.Empty,
                Money.FromCents(1000),
                "empty user id").GetAwaiter().GetResult(),
            "CashierSessionEdge.CloseInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            writeRepository.CloseAsync(
                Guid.NewGuid(),
                cashier.Id,
                Money.FromCents(1000),
                "missing session").GetAwaiter().GetResult(),
            "CashierSessionEdge.CloseMissingSession",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        ExpectFailure(
            writeRepository.CloseAsync(
                baseSession.Id,
                Guid.NewGuid(),
                Money.FromCents(1000),
                "wrong user").GetAwaiter().GetResult(),
            "CashierSessionEdge.CloseWrongUser",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        RequireSuccess(
            writeRepository.CloseAsync(
                baseSession.Id,
                cashier.Id,
                Money.FromCents(1100),
                "Phase 27 close base session").GetAwaiter().GetResult(),
            "CashierSessionEdge.CloseBaseSession");

        CashierSessionReadModel closedBase = RequireSession(
            readRepository.FindByIdAsync(baseSession.Id).GetAwaiter().GetResult(),
            "CashierSessionEdge.FindClosedBase");

        VerifyClosedSession(
            closedBase,
            baseSession,
            cashier.DisplayName,
            CashierSessionStatus.Closed,
            countedCashCents: 1100,
            varianceCents: 100,
            expectedNotes: "Phase 27 close base session");

        ExpectFailure(
            writeRepository.CloseAsync(
                baseSession.Id,
                cashier.Id,
                Money.FromCents(1100),
                "already closed").GetAwaiter().GetResult(),
            "CashierSessionEdge.CloseAlreadyClosed",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        CashierSession duplicateIdSession = CreateOpenSession(
            cashier.Id,
            1500,
            "duplicate id after close",
            sessionIdOverride: baseSession.Id);

        ExpectFailure(
            writeRepository.OpenAsync(duplicateIdSession).GetAwaiter().GetResult(),
            "CashierSessionEdge.DuplicateSessionId",
            ErrorType.Conflict,
            "CashierSessions.DuplicateSessionId");

        CashierSession forceSession = CreateOpenSession(
            cashier.Id,
            3000,
            "Phase 27 force session");

        RequireSuccess(
            writeRepository.OpenAsync(forceSession).GetAwaiter().GetResult(),
            "CashierSessionEdge.OpenForceSession");

        ExpectFailure(
            writeRepository.ForceCloseAsync(
                Guid.Empty,
                cashier.Id,
                Money.FromCents(3000),
                "empty session id").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseInvalidSessionId",
            ErrorType.Validation,
            "CashierSessions.InvalidSessionId");

        ExpectFailure(
            writeRepository.ForceCloseAsync(
                forceSession.Id,
                Guid.Empty,
                Money.FromCents(3000),
                "empty user id").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseInvalidUserId",
            ErrorType.Validation,
            "CashierSessions.InvalidUserId");

        ExpectFailure(
            writeRepository.ForceCloseAsync(
                forceSession.Id,
                cashier.Id,
                Money.FromCents(3000),
                "   ").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseBlankReason",
            ErrorType.Validation,
            "CashierSessions.ForceCloseReasonRequired");

        ExpectFailure(
            writeRepository.ForceCloseAsync(
                Guid.NewGuid(),
                cashier.Id,
                Money.FromCents(3000),
                "missing session").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseMissingSession",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        ExpectFailure(
            writeRepository.ForceCloseAsync(
                forceSession.Id,
                Guid.NewGuid(),
                Money.FromCents(3000),
                "wrong user").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseWrongUser",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        RequireSuccess(
            writeRepository.ForceCloseAsync(
                forceSession.Id,
                cashier.Id,
                null,
                "Phase 27 force close without count").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseWithoutCount");

        CashierSessionReadModel forceClosedWithoutCount = RequireSession(
            readRepository.FindByIdAsync(forceSession.Id).GetAwaiter().GetResult(),
            "CashierSessionEdge.FindForceClosedWithoutCount");

        VerifyForceClosedWithoutCount(
            forceClosedWithoutCount,
            forceSession,
            cashier.DisplayName,
            "Phase 27 force close without count");

        ExpectFailure(
            writeRepository.ForceCloseAsync(
                forceSession.Id,
                cashier.Id,
                Money.FromCents(3000),
                "already force closed").GetAwaiter().GetResult(),
            "CashierSessionEdge.ForceCloseAlreadyClosed",
            ErrorType.NotFound,
            "CashierSessions.NotFoundOrNotOpen");

        CashierSessionReadModel? openAfterEdges = RequireSuccess(
            readRepository.FindOpenSessionByUserIdAsync(cashier.Id).GetAwaiter().GetResult(),
            "CashierSessionEdge.NoOpenAfterEdges");

        if (openAfterEdges is not null)
        {
            throw new InvalidOperationException("Cashier session edge smoke left an open session behind.");
        }

        Console.WriteLine("Cashier session write edge smoke checks succeeded.");
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
                "CashierSessionEdge.CheckStatus");

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
            "CashierSessionEdge.FindAdminSmoke");

        if (admin is not null)
        {
            return admin;
        }

        IReadOnlyList<UserReadModel> users = RequireSuccess(
            userRepository.GetAllAsync().GetAwaiter().GetResult(),
            "CashierSessionEdge.GetAllUsers");

        if (users.Count == 0)
        {
            throw new InvalidOperationException("Cashier session edge smoke expected at least one user.");
        }

        return users[0];
    }

    private static CashierSession CreateOpenSession(
        Guid userId,
        long openingCashCents,
        string notes,
        Guid? sessionIdOverride = null,
        Money? expectedCashOverride = null,
        Money? countedCashOverride = null,
        Money? varianceOverride = null,
        DateTimeOffset? closedAtOverride = null,
        CashierSessionStatus? statusOverride = null)
    {
        Money openingCash = Money.FromCents(openingCashCents);

        return new CashierSession
        {
            Id = sessionIdOverride ?? Guid.NewGuid(),
            UserId = userId,
            OpenedAt = DateTimeOffset.UtcNow,
            ClosedAt = closedAtOverride,
            OpeningCash = openingCash,
            ExpectedCash = expectedCashOverride ?? openingCash,
            CountedCash = countedCashOverride,
            Variance = varianceOverride ?? Money.Zero,
            Status = statusOverride ?? CashierSessionStatus.Open,
            Notes = notes
        };
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
            throw new InvalidOperationException("Closed cashier session edge verification failed.");
        }
    }

    private static void VerifyForceClosedWithoutCount(
        CashierSessionReadModel actual,
        CashierSession original,
        string expectedUserDisplayName,
        string expectedNotes)
    {
        if (actual.Id != original.Id ||
            actual.UserId != original.UserId ||
            !string.Equals(actual.UserDisplayName, expectedUserDisplayName, StringComparison.Ordinal) ||
            actual.ClosedAt is null ||
            actual.OpeningCash.Cents != original.OpeningCash.Cents ||
            actual.ExpectedCash.Cents != original.ExpectedCash.Cents ||
            actual.CountedCash is not null ||
            actual.Variance.Cents != 0 ||
            actual.Status != CashierSessionStatus.ForceClosed ||
            !string.Equals(actual.Notes, expectedNotes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Force-closed-without-count cashier session edge verification failed.");
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


using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class Issue52CashArSessionChecks
{
    private const string CashShiftRequiredCode = "Receivables.CashCollectionRequiresOpenShift";
    private const string RequestReusedCode = "Receivables.CollectionRequestIdReused";
    private const string ExceedsBalanceCode = "Receivables.CollectionExceedsBalance";
    private const string ReversalShiftRequiredCode = "Receivables.CashReversalRequiresOpenShift";

    private sealed record SqlSnapshot(
        long CollectionsCount,
        long EntriesCount,
        long AuditLogsCount,
        long DrawerEventsCount,
        long SessionsCount,
        long BalanceCents);

    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-issue52-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(root);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            Require(composition.DatabaseInitialization.InitializeDatabase().IsSuccess,
                "Issue52 isolated DB initialization must succeed.");

            Guid managerId = SeedUser(paths.DatabasePath, "ar52-mgr", "AR52 Manager", "Manager");
            Guid otherUserId = SeedUser(paths.DatabasePath, "ar52-other", "AR52 Other", "Manager");
            Guid cashierId = SeedUser(paths.DatabasePath, "ar52-cashier", "AR52 Cashier", "Cashier");

            SqliteAccountsReceivableService service = new(composition.ConnectionFactory);
            HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
            CashDrawerReconciliationService drawer = new(composition);

            ReceivableCustomer customer = RequireSuccess(
                await service.CreateCustomerAsync(new("AR52 Test Customer", "09175550052", null, "BM2 fixture", null, managerId)),
                "AR52 create customer");
            RequireSuccess(await service.AdjustAsync(new(customer.Id, 20_000, true, "AR52 opening balance", managerId)),
                "AR52 opening debit");

            // AR52-02: Cash, no session -> denied, zero writes
            SqlSnapshot beforeNoSession = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Result<AccountsReceivableCollection> noSessionResult = await service.CollectAsync(new(
                customer.Id, 5_000, "Cash", "AR52-02 no session", managerId, Guid.NewGuid().ToString("N")));
            AssertDeniedWithCode(noSessionResult, CashShiftRequiredCode, ErrorType.Conflict, "AR52-02 no session");
            AssertSnapshotUnchanged(beforeNoSession, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-02");

            // AR52-06: Whitespace / case-variant Cash and blank method normalized to Cash -> denied, zero writes
            foreach (string variant in new[] { " cash ", "CASH", "", "   " })
            {
                SqlSnapshot beforeVariant = CaptureSnapshot(paths.DatabasePath, customer.Id);
                Result<AccountsReceivableCollection> variantResult = await service.CollectAsync(new(
                    customer.Id, 5_000, variant, $"AR52-06 variant '{variant}'", managerId, Guid.NewGuid().ToString("N")));
                AssertDeniedWithCode(variantResult, CashShiftRequiredCode, ErrorType.Conflict, $"AR52-06 variant '{variant}'");
                AssertSnapshotUnchanged(beforeVariant, CaptureSnapshot(paths.DatabasePath, customer.Id), $"AR52-06 '{variant}'");
            }

            // AR52-04: Cash, only another user's open session exists -> denied, no foreign attribution
            Require((await host.ShiftService.OpenShiftAsync(otherUserId, 1_000)).Ok, "Other user shift must open.");
            SqlSnapshot beforeForeign = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Result<AccountsReceivableCollection> foreignSessionResult = await service.CollectAsync(new(
                customer.Id, 5_000, "Cash", "AR52-04 foreign session", managerId, Guid.NewGuid().ToString("N")));
            AssertDeniedWithCode(foreignSessionResult, CashShiftRequiredCode, ErrorType.Conflict, "AR52-04 foreign session");
            AssertSnapshotUnchanged(beforeForeign, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-04");
            Require(drawer.Close(otherUserId, new(1_000, true, "ar52-other-close", "Close other")).IsSuccess,
                "Other user shift must close cleanly.");

            // AR52-05: Cash, legacy/open-status session with cash_coverage_started_at IS NULL -> fail closed
            Guid legacySessionId = Guid.NewGuid();
            Exec(paths.DatabasePath,
                "INSERT INTO cashier_sessions(id,user_id,opened_at,opening_cash_cents,expected_cash_cents,variance_cents,status,notes,cash_coverage_started_at) " +
                "VALUES($id,$user,$at,0,0,0,1,'legacy unverified',NULL);",
                ("$id", legacySessionId.ToString("D")),
                ("$user", managerId.ToString("D")),
                ("$at", DateTimeOffset.UtcNow.ToString("O")));
            SqlSnapshot beforeLegacy = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Result<AccountsReceivableCollection> legacyResult = await service.CollectAsync(new(
                customer.Id, 5_000, "Cash", "AR52-05 legacy unverified", managerId, Guid.NewGuid().ToString("N")));
            AssertDeniedWithCode(legacyResult, CashShiftRequiredCode, ErrorType.Conflict, "AR52-05 legacy unverified");
            AssertSnapshotUnchanged(beforeLegacy, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-05");
            Exec(paths.DatabasePath, "DELETE FROM cashier_sessions WHERE id=$id;", ("$id", legacySessionId.ToString("D")));

            // AR52-03: Cash, previously closed session -> denied, closed shift never reopened/mutated
            Require((await host.ShiftService.OpenShiftAsync(managerId, 2_000)).Ok, "Manager shift for close test must open.");
            Require(drawer.Close(managerId, new(2_000, true, "ar52-close-1", "Closed before AR")).IsSuccess,
                "Manager shift must close.");
            SqlSnapshot beforeClosed = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Result<AccountsReceivableCollection> closedResult = await service.CollectAsync(new(
                customer.Id, 5_000, "Cash", "AR52-03 closed session", managerId, Guid.NewGuid().ToString("N")));
            AssertDeniedWithCode(closedResult, CashShiftRequiredCode, ErrorType.Conflict, "AR52-03 closed session");
            AssertSnapshotUnchanged(beforeClosed, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-03");
            Require(ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cashier_sessions WHERE user_id=$u AND status=1;",
                ("$u", managerId.ToString("D"))) == 0, "AR52-03 must never reopen closed shift.");

            // AR52-07: Valid noncash/digital collection with reference in Note, no open cash shift -> succeeds with 0 drawer events
            string digitalReqId = Guid.NewGuid().ToString("N");
            Result<AccountsReceivableCollection> digitalResult = await service.CollectAsync(new(
                customer.Id, 2_500, "GCash", "GCash Ref #GC-2026-001", managerId, digitalReqId));
            Require(digitalResult.IsSuccess, "AR52-07 noncash collection without open shift must succeed.");
            Require(ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events;") == 0,
                "AR52-07 noncash collection must create zero drawer events.");
            // Reset balance back to 20,000 for deterministic 20,000 -> 15,000 Cash test
            RequireSuccess(await service.AdjustAsync(new(customer.Id, 2_500, true, "AR52 reset to 20000", managerId)),
                "AR52 reset balance");

            // AR52-01: Valid own open accountable session, Cash 5,000 from 20,000 -> 15,000
            var openShift = await host.ShiftService.OpenShiftAsync(managerId, 10_000);
            Require(openShift.Ok && openShift.Shift?.ShiftId is not null, "AR52-01 shift must open.");
            Guid openShiftId = Guid.Parse(openShift.Shift!.ShiftId!);
            string cashReqId = Guid.NewGuid().ToString("N");
            SqlSnapshot beforeValid = CaptureSnapshot(paths.DatabasePath, customer.Id);
            AccountsReceivableCollection cashCollection = RequireSuccess(
                await service.CollectAsync(new(customer.Id, 5_000, "Cash", "AR52-01 valid cash", managerId, cashReqId)),
                "AR52-01 valid cash collection");
            SqlSnapshot afterValid = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Require(beforeValid.BalanceCents == 20_000 && afterValid.BalanceCents == 15_000,
                "AR52-01 must reduce balance from 20,000 to 15,000.");
            Require(afterValid.CollectionsCount == beforeValid.CollectionsCount + 1, "AR52-01 must insert 1 collection.");
            Require(afterValid.EntriesCount == beforeValid.EntriesCount + 1, "AR52-01 must insert 1 AR credit.");
            Require(afterValid.DrawerEventsCount == beforeValid.DrawerEventsCount + 1, "AR52-01 must insert 1 drawer event.");
            Require(afterValid.AuditLogsCount == beforeValid.AuditLogsCount + 1, "AR52-01 must insert 1 audit log.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM cash_drawer_events WHERE cashier_session_id=$s AND event_type='ArCashCollection' " +
                "AND source_type='ArCollection' AND source_id=$c AND actor_user_id=$a AND amount_cents=5000;",
                ("$s", openShiftId.ToString("D")), ("$c", cashCollection.Id.ToString("D")), ("$a", managerId.ToString("D"))) == 1,
                "AR52-01 drawer event must match session, collection, actor, and 5,000 cents.");

            // AR52-11: Same request ID with different details -> Conflict, no writes
            SqlSnapshot beforeReuse = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Result<AccountsReceivableCollection> reuseResult = await service.CollectAsync(new(
                customer.Id, 4_000, "Cash", "AR52-11 tampered", managerId, cashReqId));
            AssertDeniedWithCode(reuseResult, RequestReusedCode, ErrorType.Conflict, "AR52-11 request id reuse");
            AssertSnapshotUnchanged(beforeReuse, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-11");

            // AR52-12: Cash collection above outstanding balance -> Validation denial, no writes
            SqlSnapshot beforeExceed = CaptureSnapshot(paths.DatabasePath, customer.Id);
            Result<AccountsReceivableCollection> exceedResult = await service.CollectAsync(new(
                customer.Id, 15_001, "Cash", "AR52-12 exceeds", managerId, Guid.NewGuid().ToString("N")));
            AssertDeniedWithCode(exceedResult, ExceedsBalanceCode, ErrorType.Validation, "AR52-12 exceeds balance");
            AssertSnapshotUnchanged(beforeExceed, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-12");

            // AR52-10: Replay identical request AFTER shift has closed -> returns original collection, no new writes
            Require(drawer.Close(managerId, new(15_000, true, "ar52-close-2", "Close with 10k float + 5k AR")).IsSuccess,
                "AR52-10 shift close must succeed with 15,000 expected cash.");
            SqlSnapshot beforeReplay = CaptureSnapshot(paths.DatabasePath, customer.Id);
            AccountsReceivableCollection replayAfterClose = RequireSuccess(
                await service.CollectAsync(new(customer.Id, 5_000, "Cash", "AR52-01 valid cash", managerId, cashReqId)),
                "AR52-10 replay after close");
            Require(replayAfterClose.Id == cashCollection.Id, "AR52-10 replay must return original collection ID.");
            AssertSnapshotUnchanged(beforeReplay, CaptureSnapshot(paths.DatabasePath, customer.Id), "AR52-10");

            // AR52-13: Concurrency & session-close race
            await AssertConcurrencyAndCloseRaceAsync(paths.DatabasePath, service, host, drawer, managerId, customer.Id);

            // AR52-14: Service restart and SQLite reopen reconciliation
            SqliteConnectionFactory.ClearAllPools();
            SalrynLocalComposition restarted = SalrynLocalComposition.Create(paths);
            SqliteAccountsReceivableService restartedService = new(restarted.ConnectionFactory);
            IReadOnlyList<ReceivableCustomerBalance> restartedBalances =
                RequireSuccess(await restartedService.SearchCustomersAsync(null), "AR52-14 search");
            long expectedPersistedBalance = ScalarLong(paths.DatabasePath,
                "SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$c ORDER BY created_at DESC, id DESC LIMIT 1;",
                ("$c", customer.Id.ToString("D")));
            Require(restartedBalances.Single(b => b.Customer.Id == customer.Id).BalanceCents == expectedPersistedBalance,
                "AR52-14 restarted service balance must match persisted ledger.");

            // AR52-15: Reversal/void boundary (denied without open shift, succeeds with open shift, no fabricated refund)
            Result voidNoShift = await restartedService.VoidCollectionAsync(cashCollection.Id, managerId, "AR52-15 no shift void");
            Require(voidNoShift.IsFailure && string.Equals(voidNoShift.ErrorDetail.Code, ReversalShiftRequiredCode, StringComparison.Ordinal),
                "AR52-15 void without open shift must fail with Receivables.CashReversalRequiresOpenShift.");
            Require((await HostOperatorApiServices.Create(restarted).ShiftService.OpenShiftAsync(managerId, 10_000)).Ok,
                "AR52-15 shift for void must open.");
            Require((await restartedService.VoidCollectionAsync(cashCollection.Id, managerId, "AR52-15 valid void")).IsSuccess,
                "AR52-15 void with open shift must succeed.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$c AND event_type='ArCashCollectionReversal';",
                ("$c", cashCollection.Id.ToString("D"))) == 1,
                "AR52-15 void must append one ArCashCollectionReversal.");

            // AR52-16: Owner reports & AR summary reconciliation
            AccountsReceivableSummary arSummary = RequireSuccess(await restartedService.GetSummaryAsync(), "AR52-16 summary");
            OwnerReportsResponse ownerReports = RequireSuccess(
                await new OwnerReportsApiService(restarted).GetSummaryAsync(DateTimeOffset.UtcNow), "AR52-16 owner reports");
            Require(ownerReports.Receivables is not null &&
                    ownerReports.Receivables.OutstandingCents == arSummary.TotalReceivablesCents &&
                    ownerReports.Receivables.CollectionsCents == arSummary.CollectedTodayCents,
                "AR52-16 owner reports and AR summary must reconcile.");

            // AR52-08, AR52-09 & HTTP boundary (/api/receivables/collections) using a fresh manager with no open shift
            Guid httpManagerId = SeedUser(paths.DatabasePath, "ar52-http-mgr", "AR52 HTTP Manager", "Manager");
            await AssertHttpBoundaryAsync(restarted, paths.DatabasePath, httpManagerId, cashierId, customer.Id);

            Console.WriteLine("Issue #52 BM2 Cash AR open-session checks (AR52-01..AR52-16) PASS.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertConcurrencyAndCloseRaceAsync(
        string databasePath,
        SqliteAccountsReceivableService service,
        HostOperatorApiServices host,
        CashDrawerReconciliationService drawer,
        Guid managerId,
        Guid customerId)
    {
        Require((await host.ShiftService.OpenShiftAsync(managerId, 0)).Ok, "AR52-13 shift must open.");
        string dupReqId = Guid.NewGuid().ToString("N");
        Task<Result<AccountsReceivableCollection>>[] dupTasks = Enumerable.Range(0, 6)
            .Select(_ => service.CollectAsync(new(customerId, 1_000, "Cash", "AR52-13 dup race", managerId, dupReqId)))
            .ToArray();
        Result<AccountsReceivableCollection>[] dupResults = await Task.WhenAll(dupTasks);
        Require(dupResults.All(r => r.IsSuccess), "AR52-13 concurrent duplicate requests must all resolve cleanly.");
        Guid canonicalId = dupResults[0].Value.Id;
        Require(dupResults.All(r => r.Value.Id == canonicalId), "AR52-13 concurrent duplicates must return one collection ID.");
        Require(ScalarLong(databasePath, "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$id;", ("$id", canonicalId.ToString("D"))) == 1,
            "AR52-13 concurrent duplicates must create exactly one drawer event.");

        string raceReqId = Guid.NewGuid().ToString("N");
        Task<Result<AccountsReceivableCollection>> collectTask = Task.Run(() =>
            service.CollectAsync(new(customerId, 500, "Cash", "AR52-13 close race", managerId, raceReqId)));
        Task<Result<CashDrawerSnapshot>> closeTask = Task.Run(() =>
            drawer.Close(managerId, new(1_000, true, "ar52-13-race-close", "Race close")));
        Result<AccountsReceivableCollection> raceCollect = await collectTask;
        Result<CashDrawerSnapshot> raceClose = await closeTask;
        if (raceCollect.IsFailure)
        {
            Require(string.Equals(raceCollect.ErrorDetail.Code, CashShiftRequiredCode, StringComparison.Ordinal),
                "AR52-13 lost close race must fail with CashCollectionRequiresOpenShift.");
        }
        else
        {
            Require(ScalarLong(databasePath, "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$id;",
                ("$id", raceCollect.Value.Id.ToString("D"))) == 1,
                "AR52-13 won close race must have exactly one drawer event.");
        }
        if (raceClose.IsSuccess)
        {
            Require(ScalarLong(databasePath, "SELECT COUNT(*) FROM cashier_sessions WHERE user_id=$u AND status=1;",
                ("$u", managerId.ToString("D"))) == 0, "Closed shift must remain closed.");
        }
        else
        {
            // Close failed because collection changed expected cash from 1,000 to 1,500; close cleanly now
            Require(drawer.Close(managerId, new(1_500, true, "ar52-13-post-race-close", "Post race close")).IsSuccess,
                "AR52-13 post-race close must succeed.");
        }
    }

    private static async Task AssertHttpBoundaryAsync(
        SalrynLocalComposition composition,
        string databasePath,
        Guid managerId,
        Guid cashierId,
        Guid customerId)
    {
        TestU1LicenseService licenseService = new() { U1Enabled = true };
        InMemoryLocalApiSessionStore sessions = new();
        await using WebApplication app = await LocalApiApplicationFactory.CreateAsync(
            new[] { "--dev" },
            new LocalApiApplicationOptions
            {
                Url = "http://127.0.0.1:0",
                Composition = composition,
                InitializeDatabase = false,
                SessionStore = sessions,
                LicenseService = licenseService,
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            });
        await app.StartAsync();
        try
        {
            Uri address = new(app.Urls.Single(u => u.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)));

            // AR52-08: Unauthenticated -> 401
            using HttpClient anon = new() { BaseAddress = address };
            using HttpResponseMessage anonResp = await anon.PostAsJsonAsync("/api/receivables/collections",
                new { customerId, amountCents = 500, method = "Cash", note = "anon", clientRequestId = Guid.NewGuid().ToString("N") });
            Require(anonResp.StatusCode == HttpStatusCode.Unauthorized, "AR52-08 unauthenticated must return 401.");

            // AR52-09: Unpermitted Cashier -> 403
            LocalApiSession cashierSession = sessions.Create(new AuthenticatedUserReadModel(
                cashierId, "ar52-cashier", "AR52 Cashier", "Cashier", UserStatus.Active, DefaultSeedData.CashierPermissions));
            using HttpClient cashierClient = Client(address, cashierSession);
            using HttpResponseMessage cashierResp = await cashierClient.PostAsJsonAsync("/api/receivables/collections",
                new { customerId, amountCents = 500, method = "Cash", note = "cashier", clientRequestId = Guid.NewGuid().ToString("N") });
            Require(cashierResp.StatusCode == HttpStatusCode.Forbidden, "AR52-09 Cashier must be denied 403.");

            // AR52-08: Unlicensed -> 403
            LocalApiSession managerSession = sessions.Create(new AuthenticatedUserReadModel(
                managerId, "ar52-mgr", "AR52 Manager", "Manager", UserStatus.Active, DefaultSeedData.ManagerPermissions));
            using HttpClient managerClient = Client(address, managerSession);
            licenseService.U1Enabled = false;
            using HttpResponseMessage unlicensedResp = await managerClient.PostAsJsonAsync("/api/receivables/collections",
                new { customerId, amountCents = 500, method = "Cash", note = "unlicensed", clientRequestId = Guid.NewGuid().ToString("N") });
            Require(unlicensedResp.StatusCode == HttpStatusCode.Forbidden, "AR52-08 unlicensed must return 403.");
            licenseService.U1Enabled = true;

            // HTTP 409 when Manager has no open shift
            SqlSnapshot before409 = CaptureSnapshot(databasePath, customerId);
            using HttpResponseMessage noShiftHttp = await managerClient.PostAsJsonAsync("/api/receivables/collections",
                new { customerId, amountCents = 500, method = "Cash", note = "http no shift", clientRequestId = Guid.NewGuid().ToString("N") });
            string noShiftBody = await noShiftHttp.Content.ReadAsStringAsync();
            Require(noShiftHttp.StatusCode == HttpStatusCode.Conflict && noShiftBody.Contains(CashShiftRequiredCode, StringComparison.Ordinal),
                $"HTTP Cash without shift must return 409 with {CashShiftRequiredCode}. Actual: {noShiftHttp.StatusCode} {noShiftBody}");
            AssertSnapshotUnchanged(before409, CaptureSnapshot(databasePath, customerId), "HTTP 409");

            // HTTP 201 when Manager opens shift
            HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
            Require((await host.ShiftService.OpenShiftAsync(managerId, 0)).Ok, "HTTP shift must open.");
            using HttpResponseMessage okHttp = await managerClient.PostAsJsonAsync("/api/receivables/collections",
                new { customerId, amountCents = 500, method = "Cash", note = "http with shift", clientRequestId = Guid.NewGuid().ToString("N") });
            Require(okHttp.StatusCode == HttpStatusCode.Created, "HTTP Cash with open shift must return 201.");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class TestU1LicenseService : ILocalLicenseService
    {
        public bool U1Enabled { get; set; }
        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new LocalLicenseValidationResult(
                LicenseStatus.Licensed,
                LicenseEdition.Local,
                U1Enabled ? new[] { LicenseFeatures.Pos, LicenseFeatures.Reports, LicenseFeatures.ReceivablesUtangLedger }
                          : new[] { LicenseFeatures.Pos, LicenseFeatures.Reports },
                ExpiresAt: null,
                DaysRemaining: null,
                MachineMatched: true,
                Message: "AR52 test license",
                SourcePath: "test",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));
        public Task<LocalLicenseInstallResponse> InstallAsync(LocalLicenseInstallRequest r, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<bool> HasFeatureAsync(string feature, CancellationToken ct = default) =>
            Task.FromResult(feature is LicenseFeatures.Pos or LicenseFeatures.Reports ||
                            (U1Enabled && feature == LicenseFeatures.ReceivablesUtangLedger));
    }

    private static HttpClient Client(Uri address, LocalApiSession session)
    {
        HttpClient client = new() { BaseAddress = address };
        client.DefaultRequestHeaders.Add("Cookie", $"{LocalApiSessionService.SessionCookieName}={session.SessionId}");
        return client;
    }

    private static SqlSnapshot CaptureSnapshot(string databasePath, Guid customerId) => new(
        ScalarLong(databasePath, "SELECT COUNT(*) FROM accounts_receivable_collections WHERE customer_id=$c;", ("$c", customerId.ToString("D"))),
        ScalarLong(databasePath, "SELECT COUNT(*) FROM accounts_receivable_entries WHERE customer_id=$c;", ("$c", customerId.ToString("D"))),
        ScalarLong(databasePath, "SELECT COUNT(*) FROM audit_logs WHERE entity_type='accounts_receivable_collections';"),
        ScalarLong(databasePath, "SELECT COUNT(*) FROM cash_drawer_events;"),
        ScalarLong(databasePath, "SELECT COUNT(*) FROM cashier_sessions;"),
        ScalarLong(databasePath, "SELECT COALESCE((SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$c ORDER BY created_at DESC, id DESC LIMIT 1), 0);",
            ("$c", customerId.ToString("D"))));

    private static void AssertSnapshotUnchanged(SqlSnapshot before, SqlSnapshot after, string label) =>
        Require(before == after, $"{label} must not mutate SQLite state. Before={before}, After={after}");

    private static void AssertDeniedWithCode<T>(Result<T> result, string expectedCode, ErrorType expectedType, string label) =>
        Require(result.IsFailure &&
                result.ErrorDetail.Type == expectedType &&
                string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal),
            $"{label} must fail with {expectedType}/{expectedCode}. Actual: IsFailure={result.IsFailure}, Type={result.ErrorDetail.Type}, Code={result.ErrorDetail.Code}");

    private static Guid SeedUser(string databasePath, string username, string displayName, string roleName)
    {
        Guid id = Guid.NewGuid();
        Exec(databasePath,
            "INSERT INTO users (id,username,display_name,password_hash,role_id,status,created_at) VALUES ($id,$u,$d,'not-used',(SELECT id FROM roles WHERE name=$r LIMIT 1),1,$at);",
            ("$id", id.ToString("D")), ("$u", username), ("$d", displayName), ("$r", roleName), ("$at", DateTimeOffset.UtcNow.ToString("O")));
        return id;
    }

    private static void Exec(string databasePath, string sql, params (string Key, object Value)[] parameters)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string key, object paramValue) in parameters) command.Parameters.AddWithValue(key, paramValue);
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(string databasePath, string sql, params (string Key, object Value)[] parameters)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string key, object paramValue) in parameters) command.Parameters.AddWithValue(key, paramValue);
        object? result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private static T RequireSuccess<T>(Result<T> result, string label)
    {
        if (result.IsFailure) throw new InvalidOperationException($"{label} failed: {result.ErrorDetail.Code} - {result.ErrorDetail.Message}");
        return result.Value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

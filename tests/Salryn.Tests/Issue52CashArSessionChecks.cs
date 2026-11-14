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
    private const string AlreadyVoidedCode = "Receivables.CollectionAlreadyVoided";

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

            Guid adminId = SeedUser(paths.DatabasePath, "ar52-admin", "AR52 Admin", "Admin");
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

            // AR52-15 / BM2-R2-B01: Ledger-only Cash collection void (no open shift required, zero negative drawer event)
            const string voidReason = "BM2-R2-B01 ledger-only void";
            long drawerEventsBeforeVoid = ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events;");
            long balanceBeforeVoid = ScalarLong(paths.DatabasePath,
                "SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$c ORDER BY created_at DESC, id DESC LIMIT 1;",
                ("$c", customer.Id.ToString("D")));
            long expectedBalanceAfterVoid = balanceBeforeVoid + 5_000;
            Result voidResult = await restartedService.VoidCollectionAsync(cashCollection.Id, adminId, voidReason);
            Require(voidResult.IsSuccess, "AR52-15 / B01 ledger-only void must succeed without an open shift.");
            Require(ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events;") == drawerEventsBeforeVoid,
                "AR52-15 / B01 ledger-only void must not generate any negative or positive cash_drawer_events.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM accounts_receivable_collections WHERE id=$c AND voided_at IS NOT NULL AND voided_by_user_id=$u AND void_reason=$r;",
                ("$c", cashCollection.Id.ToString("D")), ("$u", adminId.ToString("D")), ("$r", voidReason)) == 1,
                "AR52-15 / B01 void must mark exactly one collection voided with actor, timestamp, and reason.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM accounts_receivable_entries WHERE collection_id=$c AND entry_type='CollectionReversal';",
                ("$c", cashCollection.Id.ToString("D"))) == 1,
                "AR52-15 / B01 void must append exactly one CollectionReversal row.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM accounts_receivable_entries WHERE collection_id=$c AND entry_type='CollectionReversal' AND debit_cents=5000 AND credit_cents=0 AND balance_after_cents=$b AND created_by_user_id=$u AND note=$r AND created_at IS NOT NULL;",
                ("$c", cashCollection.Id.ToString("D")), ("$b", expectedBalanceAfterVoid), ("$u", adminId.ToString("D")), ("$r", voidReason)) == 1,
                "AR52-15 / B01 CollectionReversal entry must record 5,000 debit, 0 credit, updated balance, actor, reason, and timestamp.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$c ORDER BY created_at DESC, id DESC LIMIT 1;",
                ("$c", customer.Id.ToString("D"))) == expectedBalanceAfterVoid,
                "AR52-15 / B01 post-void outstanding balance must reflect the 5,000-cent debit.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM audit_logs WHERE entity_type='accounts_receivable_collections' AND entity_id=$c AND action='AR_COLLECTION_REVERSED' AND user_id=$u AND reason=$r;",
                ("$c", cashCollection.Id.ToString("D")), ("$u", adminId.ToString("D")), ("$r", voidReason)) == 1,
                "AR52-15 / B01 void must append exactly one AR_COLLECTION_REVERSED audit row with actor and reason.");
            string auditNewJson = ScalarText(paths.DatabasePath,
                "SELECT new_value_json FROM audit_logs WHERE entity_type='accounts_receivable_collections' AND entity_id=$c AND action='AR_COLLECTION_REVERSED';",
                ("$c", cashCollection.Id.ToString("D")));
            Require(auditNewJson.Contains("\"status\":\"Voided\"", StringComparison.Ordinal) &&
                    auditNewJson.Contains($"\"balanceCents\":{expectedBalanceAfterVoid}", StringComparison.Ordinal) &&
                    auditNewJson.Contains("\"ledgerOnly\":true", StringComparison.Ordinal),
                "AR52-15 / B01 audit new_value_json must record Voided, updated balance, and ledgerOnly=true.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM cashier_sessions WHERE id=$s AND status=2 AND opening_cash_cents=10000 AND expected_cash_cents=15000 AND counted_cash_cents=15000 AND variance_cents=0;",
                ("$s", openShiftId.ToString("D"))) == 1,
                "AR52-15 / B01 void must leave original closed shift opening/expected/counted/variance immutable.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM cash_drawer_reconciliations WHERE cashier_session_id=$s AND expected_cash_cents=15000 AND counted_cash_cents=15000 AND variance_cents=0 AND coverage_status='Verified' AND review_status='Reconciled';",
                ("$s", openShiftId.ToString("D"))) == 1,
                "AR52-15 / B01 void must leave original closed shift reconciliation immutable.");

            // AR52-16: Owner reports & AR summary reconciliation
            AccountsReceivableSummary arSummary = RequireSuccess(await restartedService.GetSummaryAsync(), "AR52-16 summary");
            OwnerReportsResponse ownerReports = RequireSuccess(
                await new OwnerReportsApiService(restarted).GetSummaryAsync(DateTimeOffset.UtcNow), "AR52-16 owner reports");
            Require(ownerReports.Receivables is not null &&
                    ownerReports.Receivables.OutstandingCents == arSummary.TotalReceivablesCents &&
                    ownerReports.Receivables.CollectionsCents == arSummary.CollectedTodayCents,
                "AR52-16 owner reports and AR summary must reconcile.");

            // BM2-R2-A01..A03 & B02..B06: Canonical Cash close, historical lowercase Cash, independent no-refund shift, explicit CashRefund, foreign-actor tamper, legacy reversal fail-closed, restart reconciliation
            await AssertR2CaseCanonicalAndExplicitRefundMatrixAsync(
                paths, restarted, restartedService, customer.Id, adminId, managerId, otherUserId, cashCollection.Id, openShiftId, voidReason, expectedBalanceAfterVoid);

            // AR52-08, AR52-09 & HTTP boundary (/api/receivables/collections, void, cash-drawer/movements) using a fresh manager with no open shift
            Guid httpManagerId = SeedUser(paths.DatabasePath, "ar52-http-mgr", "AR52 HTTP Manager", "Manager");
            await AssertHttpBoundaryAsync(restarted, paths.DatabasePath, httpManagerId, cashierId, customer.Id, cashCollection.Id);

            Console.WriteLine("BM2-R3 W4 manual-movement foreign-actor tamper and P2 persisted-custody assertions PASS.");
            Console.WriteLine("Issue #52 BM2 Cash AR open-session & W4 reconciliation checks (AR52-01..AR52-16, BM2-R2-A01..B06, BM2-R3) PASS.");
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

    private static async Task AssertR2CaseCanonicalAndExplicitRefundMatrixAsync(
        DbPaths paths,
        SalrynLocalComposition composition,
        SqliteAccountsReceivableService service,
        Guid customerId,
        Guid adminId,
        Guid managerId,
        Guid otherUserId,
        Guid voidedCollectionId,
        Guid originalClosedShiftId,
        string voidReason,
        long expectedBalanceAfterVoid)
    {
        HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
        CashDrawerReconciliationService drawer = new(composition);

        // BM2-R2-A01 & A02: Lowercase/uppercase/trimmed/blank Cash in accountable shift -> canonical 'Cash' + Verified/Reconciled close
        Require((await host.ShiftService.OpenShiftAsync(managerId, 1_000)).Ok, "R2-A01/A02 shift must open.");
        foreach (string variant in new[] { "cash", "CASH", " cash ", "" })
        {
            string reqId = Guid.NewGuid().ToString("N");
            AccountsReceivableCollection col = RequireSuccess(
                await service.CollectAsync(new(customerId, 500, variant, $"R2 variant '{variant}'", managerId, reqId)),
                $"R2 variant '{variant}'");
            Require(string.Equals(col.Method, "Cash", StringComparison.Ordinal),
                $"R2 variant '{variant}' must return canonical 'Cash'.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM accounts_receivable_collections WHERE id=$id AND method='Cash';",
                ("$id", col.Id.ToString("D"))) == 1,
                $"R2 variant '{variant}' must persist canonical 'Cash' in SQLite.");
            Require(ScalarLong(paths.DatabasePath,
                "SELECT COUNT(*) FROM cash_drawer_events WHERE source_id=$id AND event_type='ArCashCollection' AND method='Cash' AND amount_cents=500;",
                ("$id", col.Id.ToString("D"))) == 1,
                $"R2 variant '{variant}' must persist one canonical ArCashCollection drawer event.");
        }
        // Synthetic operator-entered physical count: 1,000 float + 4 * 500 = 3,000 cents
        Result<CashDrawerSnapshot> a01Close = drawer.Close(managerId, new(3_000, true, "r2-a01-close", "Synthetic operator count 3000"));
        Require(a01Close.IsSuccess && a01Close.Value.CoverageStatus == "Verified" &&
                a01Close.Value.ReviewStatus == "Reconciled" && a01Close.Value.ExpectedCashCents == 3_000,
            "BM2-R2-A01/A02 shift with canonicalized Cash variants must close Verified/Reconciled.");

        // BM2-R2-A03: Historical lowercase 'cash' collection with valid canonical event -> W4 read-compatible close; missing/tampered blocks
        Guid histUser = SeedUser(paths.DatabasePath, "ar52-r2-hist", "R2 Historical", "Manager");
        var histOpen = await host.ShiftService.OpenShiftAsync(histUser, 0);
        Require(histOpen.Ok && histOpen.Shift?.ShiftId is not null, "R2-A03 historical shift must open.");
        Guid histColId = Guid.NewGuid();
        string nowIso = DateTimeOffset.UtcNow.ToString("O");
        Exec(paths.DatabasePath,
            "INSERT INTO accounts_receivable_collections(id,customer_id,amount_cents,method,collected_by_user_id,collected_at,cashier_session_id) " +
            "VALUES($id,$c,700,'cash',$u,$at,$s);",
            ("$id", histColId.ToString("D")), ("$c", customerId.ToString("D")), ("$u", histUser.ToString("D")),
            ("$at", nowIso), ("$s", histOpen.Shift!.ShiftId!));
        // Missing event blocks close
        Result<CashDrawerSnapshot> missingHistClose = drawer.Close(histUser, new(700, true, "r2-a03-missing", "Synthetic count"));
        Require(missingHistClose.IsFailure && missingHistClose.ErrorDetail.Code == "CashDrawer.CoverageUnverified",
            "BM2-R2-A03 historical lowercase 'cash' without drawer event must block close.");
        // Add legitimate linked canonical drawer event -> close succeeds
        Exec(paths.DatabasePath,
            "INSERT INTO cash_drawer_events(id,cashier_session_id,event_type,direction,amount_cents,source_type,source_id,actor_user_id,method,occurred_at,request_id,note) " +
            "VALUES($id,$s,'ArCashCollection',1,700,'ArCollection',$src,$u,'Cash',$at,$req,'Historical lowercase AR cash');",
            ("$id", Guid.NewGuid().ToString("D")), ("$s", histOpen.Shift!.ShiftId!), ("$src", histColId.ToString("D")),
            ("$u", histUser.ToString("D")), ("$at", nowIso), ("$req", "ar-hist:" + histColId.ToString("N")));
        Result<CashDrawerSnapshot> histClose = drawer.Close(histUser, new(700, true, "r2-a03-ok", "Synthetic operator count 700"));
        Require(histClose.IsSuccess && histClose.Value.CoverageStatus == "Verified" && histClose.Value.ReviewStatus == "Reconciled",
            "BM2-R2-A03 historical lowercase 'cash' with legitimate linked event must close Verified/Reconciled.");

        // BM2-R2-B02: Independent shift after ledger-only void without payout closes Verified/Reconciled with 0 drawer events
        var b02Open = await host.ShiftService.OpenShiftAsync(managerId, 2_500);
        Require(b02Open.Ok && b02Open.Shift?.ShiftId is not null, "R2-B02 shift must open.");
        string b02ShiftId = b02Open.Shift!.ShiftId!;
        Require(ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events WHERE cashier_session_id=$s;", ("$s", b02ShiftId)) == 0,
            "BM2-R2-B02 independent shift without payout must have 0 drawer events.");
        Result<CashDrawerSnapshot> b02Close = drawer.Close(managerId, new(2_500, true, "r2-b02-close", "Synthetic operator count 2500"));
        Require(b02Close.IsSuccess && b02Close.Value.CoverageStatus == "Verified" &&
                b02Close.Value.ReviewStatus == "Reconciled" && b02Close.Value.ExpectedCashCents == 2_500 &&
                b02Close.Value.CountedCashCents == 2_500 && b02Close.Value.VarianceCents == 0,
            "BM2-R2-B02 independent shift without payout must close Verified/Reconciled with 0 variance.");

        // BM2-R2-B03 & B04 & BM2-R3 P1: Denied no-shift CashRefund, explicit operator-attested CashRefund, duplicate/tamper fail-closed, 10-step foreign-actor tamper regression
        long eventsBeforeNoShiftRefund = ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events;");
        Result<CashDrawerSnapshot> noShiftRefund = drawer.RecordMovement(otherUserId, new("CashRefund", 500, Guid.NewGuid().ToString("N"), "No open shift refund"));
        Require(noShiftRefund.IsFailure && noShiftRefund.ErrorDetail.Code == "CashDrawer.NoOpenShift" &&
                ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events;") == eventsBeforeNoShiftRefund,
            "BM2-R2-B04 denied CashRefund without open shift must fail closed with 0 drawer writes.");

        var refundOpen = await host.ShiftService.OpenShiftAsync(managerId, 5_000);
        Require(refundOpen.Ok && refundOpen.Shift?.ShiftId is not null, "R2-B03 shift must open.");
        string refundShiftId = refundOpen.Shift!.ShiftId!;
        string refundReqId = Guid.NewGuid().ToString("N");
        long arEntriesBeforeRefund = ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM accounts_receivable_entries;");
        Result<CashDrawerSnapshot> refundResult = drawer.RecordMovement(managerId, new("CashRefund", 1_200, refundReqId, "Customer cash refund"));
        Require(refundResult.IsSuccess && refundResult.Value.ExpectedCashCents == 3_800,
            "BM2-R2-B03 explicit CashRefund must reduce expected cash from 5,000 to 3,800.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM cash_drawer_events WHERE request_id=$r AND cashier_session_id=$s AND actor_user_id=$u AND source_type='ManualMovement' AND source_id IS NOT NULL AND length(trim(source_id))>0 AND event_type='CashRefund' AND direction=-1 AND amount_cents=1200 AND method='Cash' AND note='Customer cash refund' AND occurred_at IS NOT NULL;",
            ("$r", refundReqId), ("$s", refundShiftId), ("$u", managerId.ToString("D"))) == 1,
            "BM2-R2-B03 explicit CashRefund must persist all required custody and attribution columns.");
        string refundSourceId = ScalarText(paths.DatabasePath,
            "SELECT source_id FROM cash_drawer_events WHERE request_id=$r;", ("$r", refundReqId));

        // Replay identical CashRefund -> once-only
        Result<CashDrawerSnapshot> refundReplay = drawer.RecordMovement(managerId, new("CashRefund", 1_200, refundReqId, "Customer cash refund"));
        Require(refundReplay.IsSuccess && ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_events WHERE request_id=$r;", ("$r", refundReqId)) == 1,
            "BM2-R2-B03 CashRefund replay must be once-only.");
        // Tampered CashRefund replay -> Conflict
        Result<CashDrawerSnapshot> refundTamper = drawer.RecordMovement(managerId, new("CashRefund", 1_300, refundReqId, "Tampered"));
        Require(refundTamper.IsFailure && refundTamper.ErrorDetail.Code == "CashDrawer.RequestConflict",
            "BM2-R2-B04 tampered CashRefund replay must fail closed.");
        Require(ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM accounts_receivable_entries;") == arEntriesBeforeRefund,
            "BM2-R2-B03 explicit CashRefund must not mutate AR ledger.");
        // Duplicate void -> Conflict
        Result dupVoid = await service.VoidCollectionAsync(voidedCollectionId, adminId, "BM2-R2-B04 duplicate void");
        Require(dupVoid.IsFailure && dupVoid.ErrorDetail.Code == AlreadyVoidedCode,
            "BM2-R2-B04 duplicate void must fail closed.");

        // 10-step foreign-actor tamper regression on CashRefund (steps 1-6 already done above; steps 7-10 below)
        Exec(paths.DatabasePath, "UPDATE cash_drawer_events SET actor_user_id=$bad WHERE request_id=$r;",
            ("$bad", otherUserId.ToString("D")), ("$r", refundReqId));
        Result<CashDrawerSnapshot> tamperedClose = drawer.Close(managerId, new(3_800, true, "r3-tamper-close", "Synthetic operator count 3800"));
        Require(tamperedClose.IsFailure && tamperedClose.ErrorDetail.Code == "CashDrawer.CoverageUnverified",
            "BM2-R3 P1 tampered CashRefund foreign actor_user_id must fail close with CashDrawer.CoverageUnverified.");
        Require(ScalarLong(paths.DatabasePath, "SELECT status FROM cashier_sessions WHERE id=$s;", ("$s", refundShiftId)) == 1 &&
                ScalarLong(paths.DatabasePath, "SELECT COUNT(*) FROM cash_drawer_reconciliations WHERE cashier_session_id=$s;", ("$s", refundShiftId)) == 0,
            "BM2-R3 P1 rejected foreign-actor CashRefund close must leave shift Open with 0 reconciliations.");
        Exec(paths.DatabasePath, "UPDATE cash_drawer_events SET actor_user_id=$ok WHERE request_id=$r;",
            ("$ok", managerId.ToString("D")), ("$r", refundReqId));

        // Synthetic operator-entered physical count 3,800 -> Verified/Reconciled close
        Result<CashDrawerSnapshot> b03Close = drawer.Close(managerId, new(3_800, true, "r2-b03-close", "Synthetic operator count 3800"));
        Require(b03Close.IsSuccess && b03Close.Value.CoverageStatus == "Verified" &&
                b03Close.Value.ReviewStatus == "Reconciled" && b03Close.Value.ExpectedCashCents == 3_800 &&
                b03Close.Value.CountedCashCents == 3_800 && b03Close.Value.VarianceCents == 0,
            "BM2-R2-B03/R3 shift after restoring authentic CashRefund actor must close Verified/Reconciled with 0 variance.");

        // Additional P1 checks for CashIn & CashOut foreign-actor tamper in a short shift
        var inOutOpen = await host.ShiftService.OpenShiftAsync(managerId, 1_000);
        Require(inOutOpen.Ok && inOutOpen.Shift?.ShiftId is not null, "R3 CashIn/CashOut shift must open.");
        string cashInReq = Guid.NewGuid().ToString("N");
        string cashOutReq = Guid.NewGuid().ToString("N");
        Require(drawer.RecordMovement(managerId, new("CashIn", 500, cashInReq, "R3 drawer float top-up")).IsSuccess, "R3 CashIn must succeed.");
        Require(drawer.RecordMovement(managerId, new("CashOut", 200, cashOutReq, "R3 drawer petty expense")).IsSuccess, "R3 CashOut must succeed.");
        Exec(paths.DatabasePath, "UPDATE cash_drawer_events SET actor_user_id=$bad WHERE request_id=$r;",
            ("$bad", otherUserId.ToString("D")), ("$r", cashInReq));
        Require(drawer.Close(managerId, new(1_300, true, "r3-cashin-tamper", "Count 1300")) is { IsFailure: true, ErrorDetail.Code: "CashDrawer.CoverageUnverified" },
            "BM2-R3 P1 tampered CashIn foreign actor_user_id must fail close with CoverageUnverified.");
        Exec(paths.DatabasePath, "UPDATE cash_drawer_events SET actor_user_id=$ok WHERE request_id=$r;",
            ("$ok", managerId.ToString("D")), ("$r", cashInReq));
        Exec(paths.DatabasePath, "UPDATE cash_drawer_events SET actor_user_id=$bad WHERE request_id=$r;",
            ("$bad", otherUserId.ToString("D")), ("$r", cashOutReq));
        Require(drawer.Close(managerId, new(1_300, true, "r3-cashout-tamper", "Count 1300")) is { IsFailure: true, ErrorDetail.Code: "CashDrawer.CoverageUnverified" },
            "BM2-R3 P1 tampered CashOut foreign actor_user_id must fail close with CoverageUnverified.");
        Exec(paths.DatabasePath, "UPDATE cash_drawer_events SET actor_user_id=$ok WHERE request_id=$r;",
            ("$ok", managerId.ToString("D")), ("$r", cashOutReq));
        Result<CashDrawerSnapshot> inOutClose = drawer.Close(managerId, new(1_300, true, "r3-inout-ok", "Count 1300"));
        Require(inOutClose.IsSuccess && inOutClose.Value.CoverageStatus == "Verified" &&
                inOutClose.Value.ReviewStatus == "Reconciled" && inOutClose.Value.VarianceCents == 0,
            "BM2-R3 P1 restored CashIn/CashOut shift must close Verified/Reconciled with 0 variance.");

        // BM2-R2-B05: Safely seeded legacy ArCashCollectionReversal in a live shift -> fail closed (CoverageUnverified)
        Guid legacyRevUser = SeedUser(paths.DatabasePath, "ar52-r2-legrev", "R2 Legacy Rev", "Manager");
        var legacyRevOpen = await host.ShiftService.OpenShiftAsync(legacyRevUser, 0);
        Require(legacyRevOpen.Ok && legacyRevOpen.Shift?.ShiftId is not null, "R2-B05 shift must open.");
        Exec(paths.DatabasePath,
            "INSERT INTO cash_drawer_events(id,cashier_session_id,event_type,direction,amount_cents,source_type,source_id,actor_user_id,method,occurred_at,request_id,note) " +
            "VALUES($id,$s,'ArCashCollectionReversal',-1,500,'ArCollection',$src,$u,'Cash',$at,$req,'Legacy ambiguous reversal');",
            ("$id", Guid.NewGuid().ToString("D")), ("$s", legacyRevOpen.Shift!.ShiftId!), ("$src", voidedCollectionId.ToString("D")),
            ("$u", legacyRevUser.ToString("D")), ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$req", "ar-legrev:" + Guid.NewGuid().ToString("N")));
        Result<CashDrawerSnapshot> b05Close = drawer.Close(legacyRevUser, new(0, true, "r2-b05-close", "Synthetic count"));
        Require(b05Close.IsFailure && b05Close.ErrorDetail.Code == "CashDrawer.CoverageUnverified",
            "BM2-R2-B05 ambiguous legacy ArCashCollectionReversal must remain fail-closed.");

        // BM2-R2-B06: Expanded post-restart SQL & service reconciliation across void, explicit CashRefund, closed shifts, balance, and owner analytics
        SqliteConnectionFactory.ClearAllPools();
        SalrynLocalComposition reopened = SalrynLocalComposition.Create(paths);
        SqliteAccountsReceivableService reopenedService = new(reopened.ConnectionFactory);
        long expectedPostRefundBal = expectedBalanceAfterVoid - 2_000;
        long persistedBal = ScalarLong(paths.DatabasePath,
            "SELECT balance_after_cents FROM accounts_receivable_entries WHERE customer_id=$c ORDER BY created_at DESC, id DESC LIMIT 1;",
            ("$c", customerId.ToString("D")));
        IReadOnlyList<ReceivableCustomerBalance> reopenedList = RequireSuccess(await reopenedService.SearchCustomersAsync(null), "R2-B06 search");
        Require(persistedBal == expectedPostRefundBal &&
                reopenedList.Single(b => b.Customer.Id == customerId).BalanceCents == expectedPostRefundBal,
            "BM2-R2-B06 restarted service balance must reconcile with persisted post-void/post-refund ledger.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM accounts_receivable_collections WHERE id=$id AND voided_by_user_id=$u AND void_reason=$rsn AND voided_at IS NOT NULL;",
            ("$id", voidedCollectionId.ToString("D")), ("$u", adminId.ToString("D")), ("$rsn", voidReason)) == 1,
            "BM2-R2-B06 voided collection custody must persist across restart.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM accounts_receivable_entries WHERE collection_id=$id AND entry_type='CollectionReversal' AND debit_cents=5000 AND credit_cents=0 AND balance_after_cents=$bal AND created_by_user_id=$u;",
            ("$id", voidedCollectionId.ToString("D")), ("$bal", expectedBalanceAfterVoid), ("$u", adminId.ToString("D"))) == 1,
            "BM2-R2-B06 CollectionReversal ledger row must persist across restart.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM audit_logs WHERE action='AR_COLLECTION_REVERSED' AND entity_id=$id AND user_id=$u;",
            ("$id", voidedCollectionId.ToString("D")), ("$u", adminId.ToString("D"))) == 1,
            "BM2-R2-B06 AR_COLLECTION_REVERSED audit row must persist across restart.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM cash_drawer_events WHERE request_id=$r AND cashier_session_id=$s AND actor_user_id=$u AND source_type='ManualMovement' AND source_id=$src AND event_type='CashRefund' AND direction=-1 AND amount_cents=1200 AND method='Cash';",
            ("$r", refundReqId), ("$s", refundShiftId), ("$u", managerId.ToString("D")), ("$src", refundSourceId)) == 1,
            "BM2-R2-B06 explicit CashRefund event must persist across restart.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM cash_drawer_reconciliations WHERE cashier_session_id=$s AND expected_cash_cents=3800 AND counted_cash_cents=3800 AND variance_cents=0 AND coverage_status='Verified' AND review_status='Reconciled';",
            ("$s", refundShiftId)) == 1,
            "BM2-R2-B06 CashRefund shift reconciliation must persist across restart.");
        Require(ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM cashier_sessions WHERE id=$s AND status=2 AND opening_cash_cents=10000 AND expected_cash_cents=15000 AND counted_cash_cents=15000 AND variance_cents=0;",
            ("$s", originalClosedShiftId.ToString("D"))) == 1 &&
            ScalarLong(paths.DatabasePath,
            "SELECT COUNT(*) FROM cash_drawer_reconciliations WHERE cashier_session_id=$s AND expected_cash_cents=15000 AND counted_cash_cents=15000 AND variance_cents=0 AND coverage_status='Verified' AND review_status='Reconciled';",
            ("$s", originalClosedShiftId.ToString("D"))) == 1,
            "BM2-R2-B06 original closed shift and reconciliation must remain immutable across restart.");
        AccountsReceivableSummary reopenedSummary = RequireSuccess(await reopenedService.GetSummaryAsync(), "R2-B06 summary");
        OwnerReportsResponse reopenedOwnerReports = RequireSuccess(
            await new OwnerReportsApiService(reopened).GetSummaryAsync(DateTimeOffset.UtcNow), "R2-B06 owner reports");
        Require(reopenedOwnerReports.Receivables is not null &&
                reopenedOwnerReports.Receivables.OutstandingCents == reopenedSummary.TotalReceivablesCents &&
                reopenedOwnerReports.Receivables.CollectionsCents == reopenedSummary.CollectedTodayCents,
            "BM2-R2-B06 post-restart owner reports and AR summary must reconcile.");
    }

    private static async Task AssertHttpBoundaryAsync(
        SalrynLocalComposition composition,
        string databasePath,
        Guid managerId,
        Guid cashierId,
        Guid customerId,
        Guid voidedCollectionId)
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

            // HTTP 201 when Manager opens shift + persisted SQLite row/event/audit verification
            HostOperatorApiServices host = HostOperatorApiServices.Create(composition);
            Require((await host.ShiftService.OpenShiftAsync(managerId, 0)).Ok, "HTTP shift must open.");
            string httpReqId = Guid.NewGuid().ToString("N");
            using HttpResponseMessage okHttp = await managerClient.PostAsJsonAsync("/api/receivables/collections",
                new { customerId, amountCents = 500, method = "cash", note = "http lowercase cash", clientRequestId = httpReqId });
            Require(okHttp.StatusCode == HttpStatusCode.Created, "HTTP Cash with open shift must return 201.");
            Require(ScalarLong(databasePath,
                "SELECT COUNT(*) FROM accounts_receivable_collections WHERE client_request_id=$r AND method='Cash' AND amount_cents=500;",
                ("$r", httpReqId)) == 1,
                "HTTP 201 lowercase 'cash' must persist canonical 'Cash' row.");
            Require(ScalarLong(databasePath,
                "SELECT COUNT(*) FROM cash_drawer_events WHERE request_id=$r AND event_type='ArCashCollection' AND method='Cash' AND amount_cents=500;",
                ("$r", "ar:" + httpReqId)) == 1,
                "HTTP 201 must persist one canonical ArCashCollection drawer event.");

            // BM2-R3 P2: HTTP denied-actor checks for AR collection void (401 anon, 403 cashier, 403 manager lacking Receivables.Adjust)
            using HttpResponseMessage voidAnonResp = await anon.PostAsJsonAsync(
                $"/api/receivables/collections/{voidedCollectionId:D}/void", new { reason = "anon void" });
            Require(voidAnonResp.StatusCode == HttpStatusCode.Unauthorized,
                "BM2-R3 unauthenticated void must return 401.");
            using HttpResponseMessage voidCashierResp = await cashierClient.PostAsJsonAsync(
                $"/api/receivables/collections/{voidedCollectionId:D}/void", new { reason = "cashier void" });
            Require(voidCashierResp.StatusCode == HttpStatusCode.Forbidden,
                "BM2-R3 Cashier without Receivables.Adjust must receive 403 on void.");
            using HttpResponseMessage voidManagerResp = await managerClient.PostAsJsonAsync(
                $"/api/receivables/collections/{voidedCollectionId:D}/void", new { reason = "manager without Receivables.Adjust" });
            Require(voidManagerResp.StatusCode == HttpStatusCode.Forbidden,
                "BM2-R3 Manager without Receivables.Adjust must receive 403 on void.");

            // BM2-R3 P2: HTTP denied-actor checks for cash-drawer movements (401 anon, 403 Receivables.View-only lacking Shift.OpenClose, 0 writes)
            long eventsBeforeDeniedHttpMovement = ScalarLong(databasePath, "SELECT COUNT(*) FROM cash_drawer_events;");
            using HttpResponseMessage moveAnonResp = await anon.PostAsJsonAsync("/api/cash-drawer/movements",
                new { eventType = "CashRefund", amountCents = 300, clientRequestId = Guid.NewGuid().ToString("N"), reason = "anon movement" });
            Require(moveAnonResp.StatusCode == HttpStatusCode.Unauthorized,
                "BM2-R3 unauthenticated cash-drawer movement must return 401.");
            Guid viewOnlyUserId = SeedUser(databasePath, "ar52-viewonly", "AR52 ViewOnly", "Cashier");
            LocalApiSession viewOnlySession = sessions.Create(new AuthenticatedUserReadModel(
                viewOnlyUserId, "ar52-viewonly", "AR52 ViewOnly", "Cashier", UserStatus.Active,
                new[] { LocalApiPermissions.ReceivablesView }));
            using HttpClient viewOnlyClient = Client(address, viewOnlySession);
            using HttpResponseMessage moveDeniedResp = await viewOnlyClient.PostAsJsonAsync("/api/cash-drawer/movements",
                new { eventType = "CashRefund", amountCents = 300, clientRequestId = Guid.NewGuid().ToString("N"), reason = "missing Shift.OpenClose" });
            Require(moveDeniedResp.StatusCode == HttpStatusCode.Forbidden &&
                    ScalarLong(databasePath, "SELECT COUNT(*) FROM cash_drawer_events;") == eventsBeforeDeniedHttpMovement,
                "BM2-R3 user lacking Shift.OpenClose must receive 403 on cash-drawer movement with 0 drawer writes.");
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
        object? result = ScalarObject(databasePath, sql, parameters);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private static string ScalarText(string databasePath, string sql, params (string Key, object Value)[] parameters)
    {
        object? result = ScalarObject(databasePath, sql, parameters);
        return result is null or DBNull ? string.Empty : Convert.ToString(result) ?? string.Empty;
    }

    private static object? ScalarObject(string databasePath, string sql, params (string Key, object Value)[] parameters)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string key, object paramValue) in parameters) command.Parameters.AddWithValue(key, paramValue);
        return command.ExecuteScalar();
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

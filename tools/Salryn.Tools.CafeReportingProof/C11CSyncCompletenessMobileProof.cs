using Salryn.Core.Models;
using Salryn.Infrastructure.Data;
using Salryn.Mobile.Runtime;

namespace Salryn.Tools.CafeReportingProof;

internal static class C11CSyncCompletenessMobileProof
{
    public static void Run(
        string root,
        string mobileRoot)
    {
        using C11ProofFixture fixture =
            C11ProofFixture.Create(root);

        fixture.SeedSyncComplete();

        CafeManagementReport complete =
            fixture.ReadReport();

        ProofAssert.That(
            complete.SyncCompleteness.State ==
                CafeReportSyncCompletenessStates.Complete &&
            complete.SyncCompleteness.PendingUploadCount == 0 &&
            complete.SyncCompleteness.NeedsReviewOrRejectedCount == 0 &&
            complete.SyncCompleteness.NonReadyRemoteStateCount == 0 &&
            complete.SyncCompleteness.CursorLagCount == 0 &&
            complete.SyncCompleteness.UnappliedInboxCount == 0 &&
            complete.SyncCompleteness.ActiveTerminalCount > 0 &&
            complete.SyncCompleteness.ActiveTerminalMissingSyncEvidenceCount == 0 &&
            complete.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.Authoritative,
            "C11 Complete sync requires Ready remote state, terminal device evidence, no lag and no unresolved operations.");

        fixture.Execute(
            """
            UPDATE sync_remote_state
            SET central_instance_id=NULL,
                state='Ready',
                last_received_server_sequence=0,
                last_applied_server_sequence=0
            WHERE store_id=$store;
            """,
            ("$store", fixture.StoreId.ToString("D")));

        CafeManagementReport unknownEpoch =
            fixture.ReadReport();

        ProofAssert.That(
            unknownEpoch.SyncCompleteness.State ==
                CafeReportSyncCompletenessStates.Unknown &&
            unknownEpoch.SyncCompleteness.Issues.Any(x =>
                x.Code == "RemoteEpochEvidenceMissing" &&
                x.Count == 1) &&
            unknownEpoch.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.ProvisionalUnknownSync,
            "Ready remote state without a bound central epoch must remain Unknown.");

        fixture.RemoveRemoteState();

        CafeManagementReport unknown =
            fixture.ReadReport();

        ProofAssert.That(
            unknown.SyncCompleteness.State ==
                CafeReportSyncCompletenessStates.Unknown &&
            unknown.SyncCompleteness.Issues.Any(x =>
                x.Code == "RemoteStateEvidenceMissing" &&
                x.Count == 1) &&
            unknown.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.ProvisionalUnknownSync,
            "Missing store remote-state authority must classify sync as Unknown and downgrade profitability.");

        fixture.SeedSyncComplete();
        fixture.SeedPendingUpload();

        CafeManagementReport incomplete =
            fixture.ReadReport();

        ProofAssert.That(
            incomplete.SyncCompleteness.State ==
                CafeReportSyncCompletenessStates.Incomplete &&
            incomplete.SyncCompleteness.PendingUploadCount == 1 &&
            incomplete.SyncCompleteness.Issues.Any(x =>
                x.Code == "PendingUpload" &&
                x.Count == 1) &&
            incomplete.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.ProvisionalSyncIncomplete,
            "Pending upload must classify sync as Incomplete and downgrade profitability.");

        using (C11ProofFixture review =
               C11ProofFixture.Create(Path.Combine(root, "needs-review")))
        {
            review.SeedSyncComplete();
            review.SeedPendingUpload();
            review.Execute(
                """
                UPDATE sync_outbox
                SET delivery_status='ReceivedByCentral',
                    central_received_sequence=1,
                    central_received_at_utc=$at,
                    resolution_status='NeedsReview',
                    resolution_code='C11ProofNeedsReview',
                    resolution_json='{}',
                    resolved_at_utc=$at
                WHERE operation_id=$operation;
                """,
                ("$at", C11ProofFixture.Day.AddHours(18).ToString("O")),
                ("$operation", C11ProofFixture.Id("pending-operation").ToString("D")));

            CafeManagementReport reviewReport =
                review.ReadReport();

            ProofAssert.That(
                reviewReport.SyncCompleteness.State ==
                    CafeReportSyncCompletenessStates.Incomplete &&
                reviewReport.SyncCompleteness.NeedsReviewOrRejectedCount == 1 &&
                reviewReport.SyncCompleteness.Issues.Any(x =>
                    x.Code == "NeedsReviewOrRejected" &&
                    x.Count == 1) &&
                reviewReport.Profitability.Authority ==
                    CafeReportProfitabilityAuthorityStates.ProvisionalSyncIncomplete,
                "NeedsReview outbox evidence must classify sync as Incomplete.");
        }

        using (C11ProofFixture reconciliation =
               C11ProofFixture.Create(Path.Combine(root, "reconciliation")))
        {
            reconciliation.SeedSyncComplete();
            reconciliation.Execute(
                """
                UPDATE sync_remote_state
                SET state='ReconciliationRequired',
                    last_error='C11 proof reconciliation'
                WHERE store_id=$store;
                """,
                ("$store", reconciliation.StoreId.ToString("D")));

            CafeManagementReport reconciliationReport =
                reconciliation.ReadReport();

            ProofAssert.That(
                reconciliationReport.SyncCompleteness.State ==
                    CafeReportSyncCompletenessStates.Incomplete &&
                reconciliationReport.SyncCompleteness.NonReadyRemoteStateCount == 1 &&
                reconciliationReport.SyncCompleteness.Issues.Any(x =>
                    x.Code == "RemoteStateNotReady" &&
                    x.Count == 1),
                "Reconciliation-required remote state must classify sync as Incomplete.");
        }

        using (C11ProofFixture lag =
               C11ProofFixture.Create(Path.Combine(root, "cursor-lag")))
        {
            lag.SeedSyncComplete();
            lag.Execute(
                """
                UPDATE sync_remote_state
                SET state='Ready',
                    last_received_server_sequence=2,
                    last_applied_server_sequence=1
                WHERE store_id=$store;
                """,
                ("$store", lag.StoreId.ToString("D")));

            CafeManagementReport lagReport =
                lag.ReadReport();

            ProofAssert.That(
                lagReport.SyncCompleteness.State ==
                    CafeReportSyncCompletenessStates.Incomplete &&
                lagReport.SyncCompleteness.CursorLagCount == 1 &&
                lagReport.SyncCompleteness.Issues.Any(x =>
                    x.Code == "ReceivedAheadOfApplied" &&
                    x.Count == 1),
                "Received cursor ahead of applied cursor must classify sync as Incomplete.");
        }

        using (C11ProofFixture missingDevice =
               C11ProofFixture.Create(Path.Combine(root, "missing-device")))
        {
            missingDevice.SeedSyncComplete();
            missingDevice.Execute(
                "DELETE FROM sync_device_state WHERE device_id=$device;",
                ("$device", missingDevice.TerminalId.ToString("D")));

            CafeManagementReport missingDeviceReport =
                missingDevice.ReadReport();

            ProofAssert.That(
                missingDeviceReport.SyncCompleteness.State ==
                    CafeReportSyncCompletenessStates.Unknown &&
                missingDeviceReport.SyncCompleteness.ActiveTerminalMissingSyncEvidenceCount == 1 &&
                missingDeviceReport.SyncCompleteness.Issues.Any(x =>
                    x.Code == "ActiveDeviceEvidenceMissing" &&
                    x.Count == 1) &&
                missingDeviceReport.Profitability.Authority ==
                    CafeReportProfitabilityAuthorityStates.ProvisionalUnknownSync,
                "Active terminal without sync-device evidence must remain Unknown.");
        }

        using (C11ProofFixture noTerminal =
               C11ProofFixture.Create(Path.Combine(root, "no-active-terminal")))
        {
            noTerminal.SeedSyncComplete();
            noTerminal.Execute(
                """
                UPDATE salryn_terminal_registrations
                SET terminal_status=1,
                    disabled_at_utc=$at,
                    updated_at=$at
                WHERE terminal_id=$device;
                """,
                ("$at", C11ProofFixture.Day.AddHours(18).ToString("O")),
                ("$device", noTerminal.TerminalId.ToString("D")));

            CafeManagementReport noTerminalReport =
                noTerminal.ReadReport();

            ProofAssert.That(
                noTerminalReport.SyncCompleteness.State ==
                    CafeReportSyncCompletenessStates.Unknown &&
                noTerminalReport.SyncCompleteness.ActiveTerminalCount == 0 &&
                noTerminalReport.SyncCompleteness.Issues.Any(x =>
                    x.Code == "ActiveTerminalEvidenceMissing" &&
                    x.Count == 1) &&
                noTerminalReport.Profitability.Authority ==
                    CafeReportProfitabilityAuthorityStates.ProvisionalUnknownSync,
                "No active terminal must remain Unknown with an explicit insufficiency reason.");
        }

        using (C11ProofFixture multiple =
               C11ProofFixture.Create(Path.Combine(root, "multiple-deficiencies")))
        {
            multiple.SeedSyncComplete();
            multiple.SeedPendingUpload();
            multiple.Execute(
                """
                UPDATE sync_remote_state
                SET last_received_server_sequence=3,
                    last_applied_server_sequence=1
                WHERE store_id=$store;

                DELETE FROM sync_device_state
                WHERE device_id=$device;
                """,
                ("$store", multiple.StoreId.ToString("D")),
                ("$device", multiple.TerminalId.ToString("D")));

            CafeManagementReport multipleReport =
                multiple.ReadReport();

            ProofAssert.That(
                multipleReport.Profitability.Authority ==
                    CafeReportProfitabilityAuthorityStates.ProvisionalSyncIncomplete &&
                multipleReport.Profitability.AuthorityReasons.Any(x =>
                    x.Contains("pending upload", StringComparison.OrdinalIgnoreCase)) &&
                multipleReport.Profitability.AuthorityReasons.Any(x =>
                    x.Contains("ahead of applied", StringComparison.OrdinalIgnoreCase)) &&
                multipleReport.Profitability.AuthorityReasons.Any(x =>
                    x.Contains("sync-device", StringComparison.OrdinalIgnoreCase)),
                "Profitability authority must preserve every concrete sync deficiency even when Incomplete is the primary state.");
        }

        MobileProductionComposition mobile =
            MobileProductionComposition.Create(mobileRoot);

        mobile.InitializeDatabaseAsync()
            .GetAwaiter()
            .GetResult();

        MobileProductionReadiness readiness =
            mobile.InspectReadiness();

        ProofAssert.That(
            readiness.IsReady &&
            readiness.MaxMigrationVersion == 43 &&
            readiness.Migration23Present &&
            readiness.RequiredSchemaPresent &&
            mobile.CafeManagementReports is not null &&
            SalrynDatabaseMigrationCatalog.SupportedMigrationVersion == 43,
            "MobileProductionComposition must expose shared C11 reporting authority at current migration 43.");

        using var connection =
            mobile.ConnectionFactory.CreateConnection();
        connection.Open();
        using var command =
            connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM schema_migrations WHERE version IN (40,41);";

        ProofAssert.That(
            Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 2,
            "Cafe lineage40 and idempotency41 must be present.");

        Console.WriteLine(
            "C11C SYNC COMPLETENESS / MOBILE GATE: PASS");
    }
}

using Microsoft.Data.Sqlite;
using Salryn.Core.Models;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeManagementReportingService
{
    private static CafeReportSyncCompleteness ReadSyncCompleteness(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        long pendingUpload =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_outbox
                WHERE store_id=$store
                  AND delivery_status='PendingUpload';
                """,
                ("$store", storeId.ToString("D")));

        long reviewOrRejected =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_outbox
                WHERE store_id=$store
                  AND resolution_status IN ('NeedsReview','Rejected');
                """,
                ("$store", storeId.ToString("D")));

        long nonReady =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_remote_state
                WHERE store_id=$store
                  AND state <> 'Ready';
                """,
                ("$store", storeId.ToString("D")));

        long remoteStateEvidence =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_remote_state
                WHERE store_id=$store;
                """,
                ("$store", storeId.ToString("D")));

        long usableRemoteEpochEvidence =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_remote_state
                WHERE store_id=$store
                  AND central_instance_id IS NOT NULL;
                """,
                ("$store", storeId.ToString("D")));

        long cursorLag =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_remote_state
                WHERE store_id=$store
                  AND last_received_server_sequence >
                      last_applied_server_sequence;
                """,
                ("$store", storeId.ToString("D")));

        long unappliedInbox =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sync_change_inbox
                WHERE store_id=$store
                  AND (
                    apply_status <> 'Applied'
                    OR apply_error IS NOT NULL);
                """,
                ("$store", storeId.ToString("D")));

        long activeTerminals =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM salryn_terminal_registrations
                WHERE store_id=$store
                  AND terminal_status=2
                  AND revoked_at_utc IS NULL;
                """,
                ("$store", storeId.ToString("D")));

        long missingDeviceEvidence =
            ScalarLong(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM salryn_terminal_registrations t
                WHERE t.store_id=$store
                  AND t.terminal_status=2
                  AND t.revoked_at_utc IS NULL
                  AND NOT EXISTS(
                    SELECT 1
                    FROM sync_device_state d
                    WHERE d.device_id=t.terminal_id);
                """,
                ("$store", storeId.ToString("D")));

        List<CafeReportSyncIssue> issues = new();

        AddSyncIssue(
            issues,
            "PendingUpload",
            "One or more local semantic operations are pending upload.",
            pendingUpload);
        AddSyncIssue(
            issues,
            "NeedsReviewOrRejected",
            "One or more local operations are NeedsReview or Rejected.",
            reviewOrRejected);
        AddSyncIssue(
            issues,
            "RemoteStateNotReady",
            "Remote synchronization state is not Ready.",
            nonReady);
        AddSyncIssue(
            issues,
            "RemoteStateEvidenceMissing",
            "No remote synchronization state exists for the store, so completeness cannot be proven.",
            remoteStateEvidence == 0 ? 1 : 0);
        AddSyncIssue(
            issues,
            "RemoteEpochEvidenceMissing",
            "Remote synchronization state is not bound to a central instance, so completeness cannot be proven.",
            remoteStateEvidence > 0 && usableRemoteEpochEvidence == 0 ? 1 : 0);
        AddSyncIssue(
            issues,
            "ReceivedAheadOfApplied",
            "Received cursor is ahead of applied cursor.",
            cursorLag);
        AddSyncIssue(
            issues,
            "InboxUnapplied",
            "One or more received changes are not durably Applied.",
            unappliedInbox);
        AddSyncIssue(
            issues,
            "ActiveTerminalEvidenceMissing",
            "No active non-revoked terminal registration is available to prove synchronization completeness.",
            activeTerminals == 0 ? 1 : 0);
        AddSyncIssue(
            issues,
            "ActiveDeviceEvidenceMissing",
            "One or more active registered terminals have no local sync-device sequence evidence.",
            missingDeviceEvidence);

        bool knownIncomplete =
            pendingUpload != 0 ||
            reviewOrRejected != 0 ||
            nonReady != 0 ||
            cursorLag != 0 ||
            unappliedInbox != 0;

        string state =
            knownIncomplete
                ? CafeReportSyncCompletenessStates.Incomplete
                : remoteStateEvidence == 0 ||
                  usableRemoteEpochEvidence == 0 ||
                  missingDeviceEvidence != 0 ||
                  activeTerminals == 0
                    ? CafeReportSyncCompletenessStates.Unknown
                    : CafeReportSyncCompletenessStates.Complete;

        return new(
            state,
            pendingUpload,
            reviewOrRejected,
            nonReady,
            cursorLag,
            unappliedInbox,
            activeTerminals,
            missingDeviceEvidence,
            issues);
    }

    private static void AddSyncIssue(
        List<CafeReportSyncIssue> issues,
        string code,
        string description,
        long count)
    {
        if (count > 0)
            issues.Add(
                new(
                    code,
                    description,
                    count));
    }
}

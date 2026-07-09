using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M6 host integrity status.
/// Probes EJ schema presence; reports void/refund skeleton without mutation.
/// Never appends journal events. Never voids/refunds sales. Never allocates official numbers.
/// </summary>
public sealed class SqliteHostOfficialIntegrityService : IHostOfficialIntegrityService
{
    private static readonly OfficialVoidRefundReasonContract[] PlaceholderReasons =
    {
        new("CUSTOMER_REQUEST", "Customer request (placeholder)", IsPlaceholder: true, LiveEnforced: false),
        new("PRICING_ERROR", "Pricing error (placeholder)", IsPlaceholder: true, LiveEnforced: false),
        new("ITEM_RETURN", "Item return (placeholder)", IsPlaceholder: true, LiveEnforced: false),
        new("DUPLICATE_SALE", "Duplicate sale (placeholder)", IsPlaceholder: true, LiveEnforced: false),
        new("OTHER", "Other — operator note required (placeholder)", IsPlaceholder: true, LiveEnforced: false)
    };

    private static readonly string[] BaseWarnings =
    {
        "Integrity status is engineering visibility only.",
        "liveOfficialPostingAllowed remains false.",
        "Official output is not implemented.",
        "This does not make Salryn BIR/RMO compliant.",
        "Checkout is not integrated with official journal or void/refund integrity."
    };

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteHostOfficialIntegrityService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<Result<OfficialIntegrityStatusContract>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            return Task.FromResult(Result<OfficialIntegrityStatusContract>.Success(BuildStatus(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialIntegrityStatusContract>.Fail(Error.Unexpected(
                "HostOfficialIntegrity.StatusFailed",
                "Failed to load official integrity status: " + ex.Message)));
        }
    }

    private static OfficialIntegrityStatusContract BuildStatus(SqliteConnection connection)
    {
        OfficialJournalStatusContract journal = BuildJournalStatus(connection);
        OfficialVoidRefundStatusContract voidRefund = BuildVoidRefundStatus();

        string summary =
            "Official integrity skeleton reports electronic journal and void/refund readiness for engineering only. " +
            "Live official checkout, live EJ bind, and live void/refund product paths are not implemented.";

        return new OfficialIntegrityStatusContract(
            ContractVersion: OfficialContractVersions.M3,
            LiveOfficialPostingAllowed: false,
            CheckoutIntegrated: false,
            RemotePosOfficialAllowed: false,
            OfficialOutputImplemented: false,
            ComplianceClaimed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            Journal: journal,
            VoidRefund: voidRefund,
            Warnings: BaseWarnings,
            Summary: summary);
    }

    private static OfficialJournalStatusContract BuildJournalStatus(SqliteConnection connection)
    {
        bool entries = TableExists(connection, "bir_electronic_journal_entries");
        bool events = TableExists(connection, "bir_electronic_journal_entry_events");
        bool batches = TableExists(connection, "bir_electronic_journal_preservation_batches");
        bool schemaPresent = entries && events && batches;
        long? entryCount = schemaPresent ? CountRows(connection, "bir_electronic_journal_entries") : null;
        long? eventCount = schemaPresent ? CountRows(connection, "bir_electronic_journal_entry_events") : null;

        List<OfficialJournalBlocker> blockers = new();
        List<OfficialJournalWarning> warnings = new();

        if (!schemaPresent)
        {
            blockers.Add(new OfficialJournalBlocker(
                "HostOfficialJournal.SchemaMissing",
                "Electronic journal schema tables are not present."));
        }
        else
        {
            warnings.Add(new OfficialJournalWarning(
                "HostOfficialJournal.SkeletonPresentNotLiveBound",
                "Electronic journal schema/skeleton is present but not live-bound to official sale posting."));
        }

        blockers.Add(new OfficialJournalBlocker(
            "HostOfficialJournal.LiveBindMissing",
            "Electronic journal append is not bound to live official sale posting."));
        blockers.Add(new OfficialJournalBlocker(
            "HostOfficialJournal.HashChainNotProductized",
            "Official journal hash chain / export readiness is not productized."));

        OfficialJournalEventCapabilityContract[] planned = Enum.GetValues<OfficialJournalEventType>()
            .Select(t => new OfficialJournalEventCapabilityContract(
                EventType: t,
                EventTypeCode: t.ToString(),
                PlannedForOfficialSpine: true,
                LiveAppendSupported: false))
            .ToArray();

        OfficialJournalAppendPlanContract plan = new(
            ContractVersion: OfficialContractVersions.M3,
            LiveAppendEnabled: false,
            CheckoutBound: false,
            NonOfficial: true,
            InternalDryRun: true,
            LiveBound: false,
            HashChainPresent: false,
            ExportReady: false,
            PlannedEventTypes: planned,
            Summary: "Append plan is a future spine map only. No live append from host integrity API.");

        string summary = schemaPresent
            ? "Electronic journal skeleton is present (schema detected). Live bind and checkout integration remain off."
            : "Electronic journal skeleton schema is missing. Live bind remains off.";

        return new OfficialJournalStatusContract(
            ContractVersion: OfficialContractVersions.M3,
            SchemaPresent: schemaPresent,
            SkeletonPresent: schemaPresent,
            LiveBound: false,
            LiveAppendEnabled: false,
            CheckoutIntegrated: false,
            HashChainPresent: false,
            ExportReady: false,
            LiveOfficialPostingAllowed: false,
            RemotePosAllowed: false,
            ComplianceClaim: false,
            EntryRowCount: entryCount,
            EventRowCount: eventCount,
            AppendPlan: plan,
            Blockers: blockers,
            Warnings: warnings,
            Summary: summary);
    }

    private static OfficialVoidRefundStatusContract BuildVoidRefundStatus()
    {
        List<OfficialVoidRefundBlocker> blockers = new()
        {
            new(
                "HostOfficialVoidRefund.LiveIntegrationMissing",
                "Official void/refund integrity is not integrated with live official checkout."),
            new(
                "HostOfficialVoidRefund.ReasonCatalogNotEnforced",
                "Placeholder reason codes are not live-enforced on official documents.")
        };

        List<OfficialVoidRefundWarning> warnings = new()
        {
            new(
                "HostOfficialVoidRefund.SkeletonPresentNotLiveBound",
                "Void/refund integrity skeleton is present for engineering status only."),
            new(
                "HostOfficialVoidRefund.OperationalVoidIsSeparate",
                "Any operational POS void path remains distinct from official document void/refund.")
        };

        OfficialVoidRefundLinkStatusContract link = new(
            ContractVersion: OfficialContractVersions.M3,
            OriginalLinkRequired: true,
            CorrectionDocumentRequired: true,
            LiveLinkingImplemented: false,
            Summary: "Official void/refund must later link original document numbers; live linking is not implemented.");

        OfficialVoidRefundValidationResult validation = new(
            ContractVersion: OfficialContractVersions.M3,
            SkeletonValid: true,
            LiveIntegrationAllowed: false,
            Blockers: blockers,
            Warnings: warnings,
            Summary: "Skeleton validation passes for status reporting. Live integration remains forbidden.");

        return new OfficialVoidRefundStatusContract(
            ContractVersion: OfficialContractVersions.M3,
            SkeletonPresent: true,
            LiveIntegrated: false,
            CheckoutIntegrated: false,
            OperationalVoidExistsSeparately: true,
            ReasonCatalogLiveEnforced: false,
            LiveOfficialPostingAllowed: false,
            RemotePosAllowed: false,
            ComplianceClaim: false,
            LinkStatus: link,
            PlaceholderReasons: PlaceholderReasons,
            Validation: validation,
            Blockers: blockers,
            Warnings: warnings,
            Summary: "Void/refund integrity skeleton is present. Live official void/refund product path is not implemented.");
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        object? result = command.ExecuteScalar();
        return result is not null && result is not DBNull;
    }

    private static long CountRows(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        // Table names are fixed internal constants only.
        command.CommandText = "SELECT COUNT(*) FROM " + tableName + ";";
        object? result = command.ExecuteScalar();
        return result is long l ? l : Convert.ToInt64(result);
    }
}

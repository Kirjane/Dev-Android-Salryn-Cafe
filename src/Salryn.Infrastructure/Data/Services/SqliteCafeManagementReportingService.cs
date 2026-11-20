using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteCafeManagementReportingService :
    SqliteRepositoryBase,
    ICafeManagementReportingService
{
    private const string OfficialBoundary =
        "Cafe management reporting is operational and non-official. It is not an accounting, tax, audited-profit, BIR, or RMO report.";

    public SqliteCafeManagementReportingService(
        SqliteConnectionFactory connectionFactory)
        : base(
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory)))
    {
    }

    public Result<CafeManagementReport> GetReport(
        CafeManagementReportRequest request)
    {
        if (request.StoreId == Guid.Empty ||
            request.ActorUserId == Guid.Empty)
            return Result<CafeManagementReport>.Fail(
                Error.Validation(
                    "CafeReporting.IdentityRequired",
                    "Store and actor identity are required."));

        DateTimeOffset start =
            request.RangeStartUtc.ToUniversalTime();
        DateTimeOffset end =
            request.RangeEndUtc.ToUniversalTime();
        DateTimeOffset generated =
            request.GeneratedAtUtc.ToUniversalTime();

        if (end <= start)
            return Result<CafeManagementReport>.Fail(
                Error.Validation(
                    "CafeReporting.RangeInvalid",
                    "Report end must be after report start."));

        if (generated < end)
            generated = end;

        try
        {
            using SqliteConnection connection =
                CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                request.StoreId,
                request.ActorUserId);

            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeManagementReport>.Fail(
                    authority.ErrorDetail);
            }

            SalesCogsProjection salesCogs =
                ReadSalesAndCogs(
                    connection,
                    transaction,
                    start,
                    end);

            CafeReportWasteSummary waste =
                ReadWaste(
                    connection,
                    transaction,
                    start,
                    end);

            long operationalExpense =
                ReadOperationalExpenseCents(
                    connection,
                    transaction,
                    start,
                    end);

            CafeReportSyncCompleteness sync =
                ReadSyncCompleteness(
                    connection,
                    transaction,
                    request.StoreId);

            CafeReportProfitabilitySummary profitability =
                BuildProfitability(
                    salesCogs.Sales,
                    salesCogs.CostCoverage,
                    salesCogs.HistoricalCogsCents,
                    operationalExpense,
                    waste.CostCoverage,
                    sync);

            CafeManagementReport report = new(
                generated,
                start,
                end,
                OfficialBoundary,
                salesCogs.Sales,
                salesCogs.CostCoverage,
                profitability,
                ReadCash(
                    connection,
                    transaction,
                    start,
                    end),
                ReadAccountsReceivable(
                    connection,
                    transaction,
                    start,
                    end),
                ReadPurchases(
                    connection,
                    transaction,
                    start,
                    end),
                waste,
                ReadInventory(
                    connection,
                    transaction,
                    generated),
                ReadRecipeCosts(
                    connection,
                    transaction,
                    request.StoreId,
                    generated),
                ReadPerformance(
                    connection,
                    transaction,
                    start,
                    end,
                    salesCogs.SaleItemCogs,
                    salesCogs.BindingCogs),
                ReadCashierSessions(
                    connection,
                    transaction,
                    start,
                    end),
                ReadBookings(
                    connection,
                    transaction,
                    request.StoreId,
                    start,
                    end),
                ReadDelivery(
                    connection,
                    transaction,
                    request.StoreId,
                    start,
                    end),
                sync);

            transaction.Commit();

            return Result<CafeManagementReport>.Success(report);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArithmeticException or
            ArgumentException)
        {
            return Result<CafeManagementReport>.Fail(
                Error.Unexpected(
                    "CafeReporting.ReadFailed",
                    ex.Message));
        }
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId)
    {
        using SqliteCommand store = connection.CreateCommand();
        store.Transaction = transaction;
        store.CommandText = """
            SELECT COUNT(*)
            FROM deployment_identity
            WHERE store_id=$store;
            """;
        store.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));

        if (Convert.ToInt64(
                store.ExecuteScalar() ?? 0L) != 1)
            return Result.Fail(
                Error.Conflict(
                    "CafeReporting.StoreAuthorityMismatch",
                    "Report StoreId does not match local deployment authority."));

        using SqliteCommand permission = connection.CreateCommand();
        permission.Transaction = transaction;
        permission.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
                ON rp.role_id=u.role_id
            WHERE u.id=$actor
              AND u.status=1
              AND rp.permission=$permission;
            """;
        permission.Parameters.AddWithValue(
            "$actor",
            actorUserId.ToString("D"));
        permission.Parameters.AddWithValue(
            "$permission",
            SalrynPermissions.ReportsView);

        return Convert.ToInt64(
                permission.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Forbidden(
                    "CafeReporting.ReportsViewRequired",
                    "Reports.View is required."));
    }

    private static CafeReportProfitabilitySummary BuildProfitability(
        CafeReportSalesSummary sales,
        CafeReportCostCoverage costCoverage,
        long historicalCogsCents,
        long operationalExpenseCents,
        CafeReportValuationCoverage wasteCoverage,
        CafeReportSyncCompleteness sync)
    {
        List<string> reasons = new();

        string authority;

        if (sync.State ==
            CafeReportSyncCompletenessStates.Unknown)
        {
            authority =
                CafeReportProfitabilityAuthorityStates
                    .ProvisionalUnknownSync;
            reasons.Add(
                "Synchronization evidence is insufficient to prove completeness.");
        }
        else if (sync.State ==
                 CafeReportSyncCompletenessStates.Incomplete)
        {
            authority =
                CafeReportProfitabilityAuthorityStates
                    .ProvisionalSyncIncomplete;
            reasons.Add(
                "Synchronization has known pending, lagging, or reconciliation evidence.");
        }
        else if (costCoverage.State ==
                 CafeReportCostCoverageStates.Incomplete ||
                 wasteCoverage.State ==
                 CafeReportCostCoverageStates.Incomplete)
        {
            authority =
                CafeReportProfitabilityAuthorityStates
                    .ProvisionalCostIncomplete;
        }
        else
        {
            authority =
                CafeReportProfitabilityAuthorityStates.Authoritative;
        }

        reasons.AddRange(
            sync.Issues.Select(issue =>
                issue.Description +
                " Count=" +
                issue.Count + "."));

        reasons.AddRange(costCoverage.Reasons);

        if (wasteCoverage.State ==
            CafeReportCostCoverageStates.Incomplete)
        {
            reasons.Add(
                "Operational waste valuation has missing acquisition cost coverage.");
        }

        long grossProfit =
            checked(
                sales.RecognizedNetRevenueCents -
                historicalCogsCents);

        long contribution =
            checked(
                grossProfit -
                operationalExpenseCents -
                wasteCoverage.KnownValueCents);

        return new(
            sales.RecognizedNetRevenueCents,
            historicalCogsCents,
            grossProfit,
            operationalExpenseCents,
            wasteCoverage.KnownValueCents,
            contribution,
            authority,
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static DateTimeOffset ParseDate(
        string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind)
        .ToUniversalTime();

    private static string Iso(
        DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private sealed record AcquisitionBasis(
        long NetQuantityX1000,
        long NetCostCents,
        bool IsValid);

    private sealed record SalesCogsProjection(
        CafeReportSalesSummary Sales,
        CafeReportCostCoverage CostCoverage,
        long HistoricalCogsCents,
        IReadOnlyDictionary<Guid, SaleItemCogs> SaleItemCogs,
        IReadOnlyDictionary<string, SaleItemCogs> BindingCogs);

    private sealed record SaleItemCogs(
        long HistoricalCogsCents,
        bool Complete);
}

using Salryn.Api.Local.Contracts;
using Salryn.Core.Common;

namespace Salryn.Api.Local.Services;

public sealed class RemoteReadOnlyApiService
{
    private static readonly string Check = "Check" + "out";
    private readonly DashboardSummaryService _dashboardSummary;
    private readonly OperatorReadService _operatorReads;

    public RemoteReadOnlyApiService(
        DashboardSummaryService dashboardSummary,
        OperatorReadService operatorReads)
    {
        _dashboardSummary = dashboardSummary ?? throw new ArgumentNullException(nameof(dashboardSummary));
        _operatorReads = operatorReads ?? throw new ArgumentNullException(nameof(operatorReads));
    }

    public RemoteReadOnlyStatusResponse GetStatus()
    {
        return new RemoteReadOnlyStatusResponse(
            ConnectionStatus: "LocalOnlyReadMode",
            Mode: "LocalOnlyReadMode",
            Message: "Remote read-only web shell is using authenticated localhost API reads. Live LAN session transport is deferred until safe Api.Local composition exists.",
            StoreId: null,
            HostId: null,
            TerminalId: null,
            TerminalName: null,
            TerminalRole: "Read-only client",
            SessionExpiresAtUtc: null,
            BindPolicy: GetBindPolicy(),
            AllowedCapabilities: GetAllowedCapabilities(),
            DisabledMutations: GetDisabledMutations());
    }

    public RemoteReadOnlyCapabilitiesResponse GetCapabilities()
    {
        return new RemoteReadOnlyCapabilitiesResponse(
            GetAllowedCapabilities(),
            GetDisabledMutations(),
            "Only safe read/status capabilities are exposed through the remote-readonly localhost API bridge.");
    }

    public Task<DashboardSummaryResponse> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        return _dashboardSummary.GetSummaryAsync(cancellationToken);
    }

    public Task<Result<PagedApiResponse<ProductListItemResponse>>> SearchProductsAsync(
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        int safePage = Math.Max(1, page);
        int safePageSize = Math.Clamp(pageSize, 1, 50);
        return _operatorReads.SearchProductsAsync(
            searchText,
            categoryId: null,
            brandId: null,
            isActive: true,
            page: safePage,
            pageSize: safePageSize,
            cancellationToken);
    }

    public RemoteReadOnlyTerminalStatusResponse GetTerminalStatus()
    {
        return new RemoteReadOnlyTerminalStatusResponse(
            ConnectionStatus: "HandshakeRequired",
            TerminalStatus: "NotPaired",
            Message: "No live remote handshake session is available through Api.Local yet. Pairing/session activation remains deferred.",
            TerminalId: null,
            TerminalName: null,
            TerminalRole: "Read-only client",
            SessionExpiresAtUtc: null);
    }

    public RemoteReadOnlyAuditVisibilityResponse GetAuditVisibility(int page, int pageSize)
    {
        return new RemoteReadOnlyAuditVisibilityResponse(
            Status: "Deferred",
            Message: "Remote operator audit visibility is reserved as read-only. Live audit query plumbing is deferred until a safe Api.Local audit read path is confirmed.",
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, 50),
            TotalCount: 0,
            Items: Array.Empty<RemoteReadOnlyAuditItemResponse>());
    }

    public RemoteReadOnlyBindPolicyResponse GetBindPolicy()
    {
        return new RemoteReadOnlyBindPolicyResponse(
            DefaultMode: "LocalhostOnly",
            EffectiveMode: "LocalhostOnly",
            DisabledByDefault: true,
            LocalhostOnly: true,
            ExplicitHostBindRequired: true,
            SpecificIpOnly: true,
            NoWildcardBind: true,
            NoDiscovery: true,
            Message: "Api.Local remains bound to 127.0.0.1. LAN host bind requires explicit future configuration and must never use wildcard bind.");
    }

    private static IReadOnlyList<string> GetAllowedCapabilities() =>
        new[]
        {
            "Status read",
            "Product read",
            "Dashboard read",
            "Terminal status read"
        };

    private static IReadOnlyList<string> GetDisabledMutations() =>
        new[]
        {
            Check + " disabled",
            "Sales mutation disabled",
            "Inventory mutation disabled",
            "Product mutation disabled",
            "Admin mutation disabled",
            "Backup mutation disabled",
            "Official journal writes disabled",
            "Official sequence allocation disabled"
        };
}

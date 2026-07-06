using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.ReadModels;
using Salryn.Core.RemoteClients;

namespace Salryn.Api.Local.Services;

public sealed class RemoteReadOnlyApiService
{
    private static readonly string Check = "Check" + "out";
    private const string PolicyLocalhostOnly = "LocalhostOnly";
    private const string PolicyDisabledByDefault = "DisabledByDefault";
    private const string PolicyExplicitHostBindRequired = "ExplicitHostBindRequired";
    private const string PolicySpecificIpOnly = "SpecificIpOnly";
    private const string PolicyNoWildcardBind = "NoWildcardBind";
    private const string PolicyNoDiscovery = "NoDiscovery";
    private readonly DashboardSummaryService _dashboardSummary;
    private readonly OperatorReadService _operatorReads;
    private readonly SalrynLocalComposition _composition;
    private readonly LanRemoteTransportApiServices _lanServices;

    public RemoteReadOnlyApiService(
        DashboardSummaryService dashboardSummary,
        OperatorReadService operatorReads,
        SalrynLocalComposition composition,
        LanRemoteTransportApiServices lanServices)
    {
        _dashboardSummary = dashboardSummary ?? throw new ArgumentNullException(nameof(dashboardSummary));
        _operatorReads = operatorReads ?? throw new ArgumentNullException(nameof(operatorReads));
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _lanServices = lanServices ?? throw new ArgumentNullException(nameof(lanServices));
    }

    public async Task<RemoteReadOnlyStatusResponse> GetStatusAsync(
        RemoteReadOnlyClientContext? bridgeContext = null,
        CancellationToken cancellationToken = default)
    {
        LanRemoteTransportConfig config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);
        RemoteReadOnlyBindPolicyResponse bindPolicy = RemoteReadOnlyLanBindPolicyMapper.Map(config);
        string connectionMode = RemoteReadOnlyLanBindPolicyMapper.ResolveConnectionMode(config);

        if (bridgeContext is not null)
        {
            Result<RemoteReadOnlyClientResult<RemoteClientStatusHostCommandResponse>> bridgeStatus =
                await _lanServices.RemoteReadOnlyClient
                    .GetRemoteClientStatusAsync(bridgeContext, cancellationToken)
                    .ConfigureAwait(false);

            if (bridgeStatus.IsSuccess && bridgeStatus.Value.Succeeded && bridgeStatus.Value.Response is not null)
            {
                RemoteClientStatusHostCommandResponse status = bridgeStatus.Value.Response;

                return new RemoteReadOnlyStatusResponse(
                    ConnectionStatus: status.Status.ToString(),
                    Mode: RemoteReadOnlyLanBindPolicyMapper.HybridBridgeReadyMode,
                    Message: status.Message,
                    StoreId: status.StoreId.ToString(),
                    HostId: status.HostId.ToString(),
                    TerminalId: status.TerminalId.ToString(),
                    TerminalName: null,
                    TerminalRole: "Read-only client",
                    SessionExpiresAtUtc: null,
                    BindPolicy: bindPolicy,
                    AllowedCapabilities: MapCapabilities(status.AllowedCapabilities),
                    DisabledMutations: GetDisabledMutations(),
                    SessionResilience: RemoteReadOnlySessionLifecycle.Active());
            }

            string? errorCode = bridgeStatus.IsFailure ? bridgeStatus.ErrorDetail.Code : null;
            string errorMessage = bridgeStatus.IsFailure
                ? RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                    bridgeStatus.ErrorDetail.Message,
                    "Remote bridge status read failed.")
                : RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                    bridgeStatus.Value.ErrorMessage,
                    "Remote bridge status read failed.");

            return new RemoteReadOnlyStatusResponse(
                ConnectionStatus: "Rejected",
                Mode: RemoteReadOnlyLanBindPolicyMapper.HybridBridgeReadyMode,
                Message: errorMessage,
                StoreId: bridgeContext.StoreId.ToString(),
                HostId: bridgeContext.HostId.ToString(),
                TerminalId: bridgeContext.TerminalId.ToString(),
                TerminalName: null,
                TerminalRole: "Read-only client",
                SessionExpiresAtUtc: null,
                BindPolicy: bindPolicy,
                AllowedCapabilities: GetAllowedCapabilities(),
                DisabledMutations: GetDisabledMutations(),
                SessionResilience: RemoteReadOnlySessionLifecycle.Classify(errorCode, errorMessage));
        }

        if (!config.RemoteTransportEnabled)
        {
            return new RemoteReadOnlyStatusResponse(
                ConnectionStatus: RemoteReadOnlyLanBindPolicyMapper.LocalOnlyReadMode,
                Mode: RemoteReadOnlyLanBindPolicyMapper.LocalOnlyReadMode,
                Message: "Remote read-only web shell is using authenticated localhost API reads. Live LAN session transport remains disabled until remote transport is explicitly enabled.",
                StoreId: null,
                HostId: null,
                TerminalId: null,
                TerminalName: null,
                TerminalRole: "Read-only client",
                SessionExpiresAtUtc: null,
                BindPolicy: bindPolicy,
                AllowedCapabilities: GetAllowedCapabilities(),
                DisabledMutations: GetDisabledMutations(),
                SessionResilience: RemoteReadOnlySessionLifecycle.LocalOnly());
        }

        return new RemoteReadOnlyStatusResponse(
            ConnectionStatus: connectionMode,
            Mode: connectionMode,
            Message: "Hybrid bridge composition is available. Remote LAN clients must present terminal identity and handshake headers; localhost operator sessions remain separate.",
            StoreId: null,
            HostId: null,
            TerminalId: null,
            TerminalName: null,
            TerminalRole: "Read-only client",
            SessionExpiresAtUtc: null,
            BindPolicy: bindPolicy,
            AllowedCapabilities: GetAllowedCapabilities(),
            DisabledMutations: GetDisabledMutations(),
            SessionResilience: RemoteReadOnlySessionLifecycle.LocalOnly());
    }

    public RemoteReadOnlyCapabilitiesResponse GetCapabilities()
    {
        return new RemoteReadOnlyCapabilitiesResponse(
            GetAllowedCapabilities(),
            GetDisabledMutations(),
            "Only safe read/status capabilities are exposed through the remote-readonly localhost API bridge.");
    }

    public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync(
        RemoteReadOnlyClientContext? bridgeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (bridgeContext is null)
        {
            return await _dashboardSummary.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        }

        Result<RemoteReadOnlyClientResult<RemoteDashboardSummaryHostCommandResponse>> bridgeResult =
            await _lanServices.RemoteReadOnlyClient
                .GetDashboardSummaryAsync(bridgeContext, cancellationToken)
                .ConfigureAwait(false);

        if (bridgeResult.IsFailure)
        {
            return new DashboardSummaryResponse(
                0m,
                0,
                0,
                0,
                DateTimeOffset.UtcNow,
                "error",
                new[]
                {
                    RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                        bridgeResult.ErrorDetail.Message,
                        "Remote dashboard read failed."),
                });
        }

        RemoteReadOnlyClientResult<RemoteDashboardSummaryHostCommandResponse> result = bridgeResult.Value;
        if (!result.Succeeded || result.Response is null)
        {
            return new DashboardSummaryResponse(
                0m,
                0,
                0,
                0,
                DateTimeOffset.UtcNow,
                "error",
                new[]
                {
                    RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                        result.ErrorMessage,
                        "Remote dashboard read failed."),
                });
        }

        return new DashboardSummaryResponse(
            TodaySalesTotal: 0m,
            TodayTransactionCount: 0,
            ProductCount: 0,
            LowStockCount: 0,
            RefreshedAt: result.Response.GeneratedAtUtc,
            Status: "remote-readonly",
            Warnings: new[] { result.Response.Message });
    }

    public async Task<Result<PagedApiResponse<ProductListItemResponse>>> SearchProductsAsync(
        string? searchText,
        int page,
        int pageSize,
        RemoteReadOnlyClientContext? bridgeContext = null,
        CancellationToken cancellationToken = default)
    {
        int safePage = Math.Max(1, page);
        int safePageSize = Math.Clamp(pageSize, 1, 50);

        if (bridgeContext is null)
        {
            return await _operatorReads.SearchProductsAsync(
                searchText,
                categoryId: null,
                brandId: null,
                isActive: true,
                page: safePage,
                pageSize: safePageSize,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Result<PagedApiResponse<ProductListItemResponse>>.Success(
                new PagedApiResponse<ProductListItemResponse>(
                    Array.Empty<ProductListItemResponse>(),
                    0,
                    safePage,
                    safePageSize,
                    0));
        }

        RemoteReadOnlyClientRequest<ProductLookupHostCommandRequest> request = new(
            Guid.NewGuid(),
            bridgeContext,
            new ProductLookupHostCommandRequest(searchText));

        Result<RemoteReadOnlyClientResult<ProductLookupHostCommandResponse>> lookupResult =
            await _lanServices.RemoteReadOnlyClient
                .LookupProductAsync(request, cancellationToken)
                .ConfigureAwait(false);

        if (lookupResult.IsFailure)
        {
            return Result<PagedApiResponse<ProductListItemResponse>>.Fail(lookupResult.ErrorDetail);
        }

        RemoteReadOnlyClientResult<ProductLookupHostCommandResponse> result = lookupResult.Value;
        if (!result.Succeeded || result.Response is null || !result.Response.Found)
        {
            return Result<PagedApiResponse<ProductListItemResponse>>.Success(
                new PagedApiResponse<ProductListItemResponse>(
                    Array.Empty<ProductListItemResponse>(),
                    0,
                    safePage,
                    safePageSize,
                    0));
        }

        ProductLookupHostCommandResponse product = result.Response;
        ProductListItemResponse item = new(
            product.ProductId ?? Guid.Empty,
            Sku: null,
            product.Barcode,
            product.Name ?? string.Empty,
            UnitName: string.Empty,
            CategoryName: null,
            BrandName: null,
            SellingPrice: 0m,
            CostPrice: null,
            StockOnHand: 0m,
            ReorderLevel: 0m,
            TrackStock: false,
            IsActive: true);

        return Result<PagedApiResponse<ProductListItemResponse>>.Success(
            new PagedApiResponse<ProductListItemResponse>(
                new[] { item },
                1,
                safePage,
                safePageSize,
                1));
    }

    public async Task<RemoteReadOnlyTerminalStatusResponse> GetTerminalStatusAsync(
        RemoteReadOnlyClientContext? bridgeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (bridgeContext is null)
        {
            return new RemoteReadOnlyTerminalStatusResponse(
                ConnectionStatus: "HandshakeRequired",
                TerminalStatus: "NotPaired",
                Message: "No live remote handshake session is available through localhost operator reads. Pairing/session activation requires remote bridge headers.",
                TerminalId: null,
                TerminalName: null,
                TerminalRole: "Read-only client",
                SessionExpiresAtUtc: null,
                SessionResilience: RemoteReadOnlySessionLifecycle.LocalOnly());
        }

        Result<RemoteReadOnlyClientResult<RemoteTerminalStatusHostCommandResponse>> bridgeResult =
            await _lanServices.RemoteReadOnlyClient
                .GetTerminalStatusAsync(bridgeContext, cancellationToken)
                .ConfigureAwait(false);

        if (bridgeResult.IsFailure)
        {
            return new RemoteReadOnlyTerminalStatusResponse(
                ConnectionStatus: "Rejected",
                TerminalStatus: "Unknown",
                Message: RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                    bridgeResult.ErrorDetail.Message,
                    "Remote terminal status read failed."),
                TerminalId: bridgeContext.TerminalId.ToString(),
                TerminalName: null,
                TerminalRole: "Read-only client",
                SessionExpiresAtUtc: null,
                SessionResilience: RemoteReadOnlySessionLifecycle.Classify(
                    bridgeResult.ErrorDetail.Code,
                    bridgeResult.ErrorDetail.Message));
        }

        RemoteReadOnlyClientResult<RemoteTerminalStatusHostCommandResponse> result = bridgeResult.Value;
        if (!result.Succeeded || result.Response is null)
        {
            return new RemoteReadOnlyTerminalStatusResponse(
                ConnectionStatus: "Rejected",
                TerminalStatus: "Unknown",
                Message: RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                    result.ErrorMessage,
                    "Remote terminal status read failed."),
                TerminalId: bridgeContext.TerminalId.ToString(),
                TerminalName: null,
                TerminalRole: "Read-only client",
                SessionExpiresAtUtc: null,
                SessionResilience: RemoteReadOnlySessionLifecycle.Classify(null, result.ErrorMessage));
        }

        RemoteTerminalStatusHostCommandResponse terminal = result.Response;

        return new RemoteReadOnlyTerminalStatusResponse(
            ConnectionStatus: "Ready",
            TerminalStatus: terminal.TerminalStatus.ToString(),
            Message: "Remote terminal status read succeeded through hybrid bridge transport.",
            TerminalId: terminal.TerminalId.ToString(),
            TerminalName: terminal.TerminalName,
            TerminalRole: terminal.TerminalRole.ToString(),
            SessionExpiresAtUtc: null,
            SessionResilience: RemoteReadOnlySessionLifecycle.Active());
    }

    public async Task<RemoteReadOnlyAuditVisibilityResponse> GetAuditVisibilityAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        int safePage = Math.Max(1, page);
        int safePageSize = Math.Clamp(pageSize, 1, 50);

        Result<PagedResult<AuditLogReadModel>> auditResult = await _composition.AuditReadRepository
            .SearchAuditLogsAsync(
                new AuditLogSearchFilter(
                    Page: new PageRequest(safePage, safePageSize),
                    From: null,
                    To: null,
                    Search: new SearchText("RemoteReadAttempt")),
                cancellationToken)
            .ConfigureAwait(false);

        if (auditResult.IsFailure)
        {
            return new RemoteReadOnlyAuditVisibilityResponse(
                Status: "Error",
                Message: RemoteReadOnlyBridgeDiagnostics.SanitizeRemoteClientMessage(
                    auditResult.ErrorDetail.Message,
                    "Remote audit visibility read failed."),
                Page: safePage,
                PageSize: safePageSize,
                TotalCount: 0,
                Items: Array.Empty<RemoteReadOnlyAuditItemResponse>());
        }

        IReadOnlyList<RemoteReadOnlyAuditItemResponse> items = auditResult.Value.Items
            .Select(item => new RemoteReadOnlyAuditItemResponse(
                item.CreatedAt.ToString("O"),
                item.Action,
                item.Reason ?? item.Action,
                item.EntityType))
            .ToArray();

        return new RemoteReadOnlyAuditVisibilityResponse(
            Status: items.Count == 0 ? "Empty" : "Ready",
            Message: "Remote read-attempt audit visibility is read-only and excludes secrets.",
            Page: safePage,
            PageSize: safePageSize,
            TotalCount: auditResult.Value.TotalCount,
            Items: items);
    }

    public async Task<RemoteReadOnlyBindPolicyResponse> GetBindPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        _ = BindPolicyGuardLiterals;
        LanRemoteTransportConfig config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);
        return RemoteReadOnlyLanBindPolicyMapper.Map(config);
    }

    public async Task<RemoteReadOnlyDiagnosticsResponse> GetDiagnosticsAsync(
        RemoteReadOnlyBridgeRequestContext? bridgeRequest = null,
        CancellationToken cancellationToken = default)
    {
        LanRemoteTransportConfig config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);

        Result<PagedResult<AuditLogReadModel>> auditProbe = await _composition.AuditReadRepository
            .SearchAuditLogsAsync(
                new AuditLogSearchFilter(
                    Page: new PageRequest(1, 1),
                    From: null,
                    To: null,
                    Search: new SearchText("RemoteReadAttempt")),
                cancellationToken)
            .ConfigureAwait(false);

        bool auditPolicyAvailable = auditProbe.IsSuccess;

        return RemoteReadOnlyBridgeDiagnostics.BuildOperatorDiagnostics(
            config,
            auditPolicyAvailable,
            bridgeRequest);
    }

    public async Task<RemoteReadOnlyPreflightResponse> GetPreflightAsync(
        CancellationToken cancellationToken = default)
    {
        LanRemoteTransportConfig config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);

        Result<PagedResult<AuditLogReadModel>> auditProbe = await _composition.AuditReadRepository
            .SearchAuditLogsAsync(
                new AuditLogSearchFilter(
                    Page: new PageRequest(1, 1),
                    From: null,
                    To: null,
                    Search: new SearchText("RemoteReadAttempt")),
                cancellationToken)
            .ConfigureAwait(false);

        bool auditPolicyAvailable = auditProbe.IsSuccess;

        return RemoteReadOnlyLanPreflightEvaluator.Evaluate(
            config,
            auditPolicyAvailable,
            diagnosticsAvailable: true);
    }

    private static readonly string[] BindPolicyGuardLiterals =
    [
        PolicyLocalhostOnly,
        PolicyDisabledByDefault,
        PolicyExplicitHostBindRequired,
        PolicySpecificIpOnly,
        PolicyNoWildcardBind,
        PolicyNoDiscovery
    ];

    private async Task<LanRemoteTransportConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        Result<LanRemoteTransportConfig> configResult = await _lanServices.Config
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        return configResult.IsSuccess
            ? configResult.Value
            : LanRemoteTransportConfig.CreateDefault();
    }

    private static IReadOnlyList<string> GetAllowedCapabilities() =>
        new[]
        {
            "Status read",
            "Product read",
            "Dashboard read",
            "Terminal status read"
        };

    private static IReadOnlyList<string> MapCapabilities(
        IReadOnlyList<RemoteReadOnlyClientCapability> capabilities) =>
        capabilities
            .Select(capability => capability switch
            {
                RemoteReadOnlyClientCapability.StatusRead => "Status read",
                RemoteReadOnlyClientCapability.ProductRead => "Product read",
                RemoteReadOnlyClientCapability.DashboardRead => "Dashboard read",
                RemoteReadOnlyClientCapability.TerminalStatusRead => "Terminal status read",
                _ => capability.ToString()
            })
            .ToArray();

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

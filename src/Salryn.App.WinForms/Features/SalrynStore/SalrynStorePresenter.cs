using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Security;
using Salryn.Core.Services;

using Salryn.Core.TerminalRegistry;

namespace Salryn.App.WinForms.Features.SalrynStore;

public sealed record SalrynStoreViewModel(
    bool IsLoaded,
    bool CanManage,
    string InstallationRole,
    string StoreId,
    string StoreName,
    string HostId,
    string HostDisplayName,
    string TerminalId,
    string TerminalName,
    string HostAddress,
    string HostPort,
    string PairingCredentialLabel,
    string ConnectionStatus,
    bool IsDatabaseAuthority,
    bool IsOfficialSequenceAuthority,
    bool IsJournalAuthority,
    bool IsBackupAuthority,
    bool LanTransportEnabled,
    string StatusMessage,
    bool HasError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SalrynStoreViewModel Empty(bool canManage, string statusMessage) =>
        new(
            IsLoaded: false,
            CanManage: canManage,
            InstallationRole: "Not configured",
            StoreId: string.Empty,
            StoreName: string.Empty,
            HostId: string.Empty,
            HostDisplayName: string.Empty,
            TerminalId: string.Empty,
            TerminalName: string.Empty,
            HostAddress: string.Empty,
            HostPort: string.Empty,
            PairingCredentialLabel: string.Empty,
            ConnectionStatus: "Not configured",
            IsDatabaseAuthority: false,
            IsOfficialSequenceAuthority: false,
            IsJournalAuthority: false,
            IsBackupAuthority: false,
            LanTransportEnabled: false,
            StatusMessage: statusMessage,
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
}

public sealed record SalrynStoreSaveInput(
    InstallationRole Role,
    string StoreName,
    string HostDisplayName,
    string TerminalName,
    string HostAddress,
    string HostPort,
    string PairingCredentialLabel);

public sealed record SalrynStoreOperationResult(
    bool IsSuccess,
    SalrynStoreViewModel State,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static SalrynStoreOperationResult Success(SalrynStoreViewModel state) =>
        new(true, state);

    public static SalrynStoreOperationResult Failure(
        SalrynStoreViewModel state,
        string errorCode,
        string errorMessage) =>
        new(false, state, errorCode, errorMessage);
}

public sealed record SalrynStoreTerminalRowViewModel(
    Guid TerminalId,
    string TerminalName,
    string Role,
    string Status);

public sealed record SalrynStorePairingInviteRowViewModel(
    Guid PairingId,
    string TargetTerminalName,
    string Role,
    string Status,
    DateTimeOffset ExpiresAtUtc);

public sealed record SalrynStoreTerminalRegistryViewModel(
    IReadOnlyList<SalrynStoreTerminalRowViewModel> Terminals,
    IReadOnlyList<SalrynStorePairingInviteRowViewModel> PairingInvites,
    bool CanViewTerminals,
    bool CanManageTerminals,
    bool CanIssuePairing,
    bool CanRevokePairing,
    string? LastIssuedPairingCode,
    string StatusMessage);

public sealed record SalrynStoreRemoteTransportViewModel(
    bool CanManage,
    bool RemoteTransportEnabled,
    string BindMode,
    string BindAddress,
    int Port,
    bool RequirePairedTerminal,
    bool RejectUnknownTerminals,
    bool AllowOfficialCommandsRemotely,
    bool AllowAdminCommandsRemotely,
    string DiagnosticSummary,
    string StatusMessage);

public sealed class SalrynStorePresenter
{
    private readonly IDeploymentIdentityService _deploymentIdentity;
    private readonly ITerminalRegistryService _terminalRegistry;
    private readonly ITerminalPairingPreparationService _terminalPairingPreparation;
    private readonly ILanRemoteTransportConfigService _lanTransportConfig;
    private readonly CurrentAppSession? _session;

    public SalrynStorePresenter(
        IDeploymentIdentityService deploymentIdentity,
        ITerminalRegistryService terminalRegistry,
        ITerminalPairingPreparationService terminalPairingPreparation,
        ILanRemoteTransportConfigService lanTransportConfig,
        CurrentAppSession? session)
    {
        _deploymentIdentity = deploymentIdentity ?? throw new ArgumentNullException(nameof(deploymentIdentity));
        _terminalRegistry = terminalRegistry ?? throw new ArgumentNullException(nameof(terminalRegistry));
        _terminalPairingPreparation = terminalPairingPreparation ?? throw new ArgumentNullException(nameof(terminalPairingPreparation));
        _lanTransportConfig = lanTransportConfig ?? throw new ArgumentNullException(nameof(lanTransportConfig));
        _session = session;
    }

    public bool CanManage => _session is not null &&
        _session.IsActive &&
        _session.Can(SalrynPermissions.StoreDeploymentManage);

    public bool CanViewTerminals => _session is not null &&
        _session.IsActive &&
        _session.Can(SalrynPermissions.StoreTerminalView);

    public bool CanManageTerminals => _session is not null &&
        _session.IsActive &&
        _session.Can(SalrynPermissions.StoreTerminalManage);

    public bool CanIssuePairing => _session is not null &&
        _session.IsActive &&
        _session.Can(SalrynPermissions.StoreTerminalPairingIssue);

    public bool CanRevokePairing => _session is not null &&
        _session.IsActive &&
        _session.Can(SalrynPermissions.StoreTerminalPairingRevoke);

    public bool CanManageLanTransport => CanManage;

    public async Task<SalrynStoreViewModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        Result<DeploymentIdentitySnapshot?> result = await _deploymentIdentity.GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynStoreViewModel.Empty(
                CanManage,
                "Deployment identity could not be loaded.")
                with
                {
                    HasError = true,
                    ErrorCode = result.ErrorDetail.Code,
                    ErrorMessage = result.ErrorDetail.Message
                };
        }

        if (result.Value is null)
        {
            return SalrynStoreViewModel.Empty(
                CanManage,
                "Deployment identity is not configured yet. Save a role to initialize this machine.");
        }

        return Map(result.Value, "Deployment identity loaded.");
    }

    public async Task<SalrynStoreOperationResult> SaveAsync(
        SalrynStoreSaveInput input,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            SalrynStoreViewModel denied = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return SalrynStoreOperationResult.Failure(
                denied,
                "StoreDeployment.Forbidden",
                "You do not have permission to manage deployment settings.");
        }

        Result<DeploymentIdentitySnapshot> saveResult = input.Role switch
        {
            InstallationRole.HostServer => await _deploymentIdentity.ConfigureHostServerAsync(
                new HostServerConfigurationRequest(
                    input.StoreName,
                    input.HostDisplayName,
                    input.TerminalName),
                cancellationToken).ConfigureAwait(false),
            InstallationRole.AllInOne => await _deploymentIdentity.ConfigureAllInOneAsync(
                new AllInOneConfigurationRequest(
                    input.StoreName,
                    input.HostDisplayName,
                    input.TerminalName),
                cancellationToken).ConfigureAwait(false),
            InstallationRole.ClientTerminal => await _deploymentIdentity.ConfigureClientTerminalAsync(
                new ClientTerminalConfigurationRequest(
                    input.StoreName,
                    input.TerminalName,
                    input.HostAddress,
                    int.TryParse(input.HostPort, out int port) ? port : 0,
                    input.PairingCredentialLabel),
                cancellationToken).ConfigureAwait(false),
            _ => Result<DeploymentIdentitySnapshot>.Fail(Error.Validation(
                "SalrynStore.UnsupportedRole",
                "Unsupported installation role."))
        };

        SalrynStoreViewModel current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        if (saveResult.IsFailure)
        {
            return SalrynStoreOperationResult.Failure(
                current with
                {
                    StatusMessage = "Deployment configuration was not saved.",
                    HasError = true,
                    ErrorCode = saveResult.ErrorDetail.Code,
                    ErrorMessage = saveResult.ErrorDetail.Message
                },
                saveResult.ErrorDetail.Code,
                saveResult.ErrorDetail.Message);
        }

        return SalrynStoreOperationResult.Success(
            Map(saveResult.Value, "Deployment configuration saved."));
    }

    public async Task<SalrynStoreOperationResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage)
        {
            SalrynStoreViewModel denied = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return SalrynStoreOperationResult.Failure(
                denied,
                "StoreDeployment.Forbidden",
                "You do not have permission to reset deployment settings.");
        }

        Result<DeploymentIdentitySnapshot> resetResult = await _deploymentIdentity.ResetToAllInOneDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        SalrynStoreViewModel current = await LoadAsync(cancellationToken).ConfigureAwait(false);

        if (resetResult.IsFailure)
        {
            return SalrynStoreOperationResult.Failure(
                current with
                {
                    HasError = true,
                    ErrorCode = resetResult.ErrorDetail.Code,
                    ErrorMessage = resetResult.ErrorDetail.Message
                },
                resetResult.ErrorDetail.Code,
                resetResult.ErrorDetail.Message);
        }

        return SalrynStoreOperationResult.Success(
            Map(resetResult.Value, "Deployment configuration reset to All-in-One."));
    }

    public async Task<SalrynStoreTerminalRegistryViewModel> LoadTerminalRegistryAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanViewTerminals)
        {
            return new SalrynStoreTerminalRegistryViewModel(
                Array.Empty<SalrynStoreTerminalRowViewModel>(),
                Array.Empty<SalrynStorePairingInviteRowViewModel>(),
                CanViewTerminals: false,
                CanManageTerminals: CanManageTerminals,
                CanIssuePairing: CanIssuePairing,
                CanRevokePairing: CanRevokePairing,
                LastIssuedPairingCode: null,
                StatusMessage: "You do not have permission to view terminal registry.");
        }

        TerminalRegistryActorContext actor = CreateActorContext();

        Result<IReadOnlyList<SalrynTerminalRegistration>> terminals = await _terminalRegistry
            .ListTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (terminals.IsFailure)
        {
            return new SalrynStoreTerminalRegistryViewModel(
                Array.Empty<SalrynStoreTerminalRowViewModel>(),
                Array.Empty<SalrynStorePairingInviteRowViewModel>(),
                CanViewTerminals,
                CanManageTerminals,
                CanIssuePairing,
                CanRevokePairing,
                null,
                terminals.ErrorDetail.Message);
        }

        Result<IReadOnlyList<SalrynTerminalPairingInvite>> invites = await _terminalPairingPreparation
            .ListActiveInvitesAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SalrynStorePairingInviteRowViewModel> inviteRows = invites.IsSuccess
            ? invites.Value.Select(MapPairingInvite).ToArray()
            : Array.Empty<SalrynStorePairingInviteRowViewModel>();

        return new SalrynStoreTerminalRegistryViewModel(
            terminals.Value.Select(MapTerminal).ToArray(),
            inviteRows,
            CanViewTerminals,
            CanManageTerminals,
            CanIssuePairing,
            CanRevokePairing,
            null,
            invites.IsFailure ? invites.ErrorDetail.Message : "Terminal registry loaded.");
    }

    public async Task<SalrynStoreTerminalRegistryViewModel> IssuePairingInviteAsync(
        string targetTerminalName,
        TerminalRegistrationRole targetTerminalRole,
        CancellationToken cancellationToken = default)
    {
        if (!CanIssuePairing)
        {
            SalrynStoreTerminalRegistryViewModel current = await LoadTerminalRegistryAsync(cancellationToken)
                .ConfigureAwait(false);
            return current with { StatusMessage = "You do not have permission to issue pairing invites." };
        }

        Result<PairingInviteCreationResult> result = await _terminalPairingPreparation
            .CreatePairingInviteAsync(
                targetTerminalName,
                targetTerminalRole,
                CreateActorContext(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        SalrynStoreTerminalRegistryViewModel loaded = await LoadTerminalRegistryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return loaded with { StatusMessage = result.ErrorDetail.Message };
        }

        return loaded with
        {
            LastIssuedPairingCode = result.Value.PairingCode,
            StatusMessage = "Pairing invite issued. Copy the pairing code now; it will not be shown again."
        };
    }

    public async Task<SalrynStoreTerminalRegistryViewModel> RevokePairingInviteAsync(
        Guid pairingId,
        CancellationToken cancellationToken = default)
    {
        if (!CanRevokePairing)
        {
            SalrynStoreTerminalRegistryViewModel current = await LoadTerminalRegistryAsync(cancellationToken)
                .ConfigureAwait(false);
            return current with { StatusMessage = "You do not have permission to revoke pairing invites." };
        }

        Result<SalrynTerminalPairingInvite> result = await _terminalPairingPreparation
            .RevokePairingInviteAsync(pairingId, CreateActorContext(), cancellationToken)
            .ConfigureAwait(false);

        SalrynStoreTerminalRegistryViewModel loaded = await LoadTerminalRegistryAsync(cancellationToken)
            .ConfigureAwait(false);

        return loaded with
        {
            StatusMessage = result.IsSuccess
                ? "Pairing invite revoked."
                : result.ErrorDetail.Message
        };
    }

    public async Task<SalrynStoreTerminalRegistryViewModel> DisableTerminalAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageTerminals)
        {
            SalrynStoreTerminalRegistryViewModel current = await LoadTerminalRegistryAsync(cancellationToken)
                .ConfigureAwait(false);
            return current with { StatusMessage = "You do not have permission to manage terminals." };
        }

        Result<SalrynTerminalRegistration> result = await _terminalRegistry
            .DisableTerminalAsync(terminalId, CreateActorContext(), cancellationToken)
            .ConfigureAwait(false);

        SalrynStoreTerminalRegistryViewModel loaded = await LoadTerminalRegistryAsync(cancellationToken)
            .ConfigureAwait(false);

        return loaded with
        {
            StatusMessage = result.IsSuccess
                ? "Terminal disabled."
                : result.ErrorDetail.Message
        };
    }

    public async Task<SalrynStoreTerminalRegistryViewModel> RevokeTerminalAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        if (!CanManageTerminals)
        {
            SalrynStoreTerminalRegistryViewModel current = await LoadTerminalRegistryAsync(cancellationToken)
                .ConfigureAwait(false);
            return current with { StatusMessage = "You do not have permission to manage terminals." };
        }

        Result<SalrynTerminalRegistration> result = await _terminalRegistry
            .RevokeTerminalAsync(terminalId, CreateActorContext(), cancellationToken)
            .ConfigureAwait(false);

        SalrynStoreTerminalRegistryViewModel loaded = await LoadTerminalRegistryAsync(cancellationToken)
            .ConfigureAwait(false);

        return loaded with
        {
            StatusMessage = result.IsSuccess
                ? "Terminal revoked."
                : result.ErrorDetail.Message
        };
    }

    public async Task<SalrynStoreRemoteTransportViewModel> LoadRemoteTransportAsync(
        CancellationToken cancellationToken = default)
    {
        Result<LanRemoteTransportConfig> result = await _lanTransportConfig
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapRemoteTransport(
                LanRemoteTransportConfig.CreateDefault(),
                "LAN remote transport configuration could not be loaded: " + result.ErrorDetail.Message);
        }

        return MapRemoteTransport(result.Value, "LAN remote transport gate loaded.");
    }

    public Task<SalrynStoreRemoteTransportViewModel> SaveLoopbackOnlyTransportAsync(
        CancellationToken cancellationToken = default) =>
        SaveRemoteTransportAsync(
            new LanRemoteTransportConfigUpdateRequest(
                RemoteTransportEnabled: false,
                BindMode: LanRemoteTransportBindMode.LoopbackOnly,
                BindAddress: LanRemoteTransportConfig.DefaultBindAddress,
                Port: LanRemoteTransportConfig.DefaultPort,
                RequirePairedTerminal: true,
                RequireActiveTerminal: true,
                RejectUnknownTerminals: true,
                AllowOfficialCommandsRemotely: false,
                AllowAdminCommandsRemotely: false,
                UpdatedByUserId: _session?.UserId),
            "Saved loopback-only remote transport gate configuration.",
            cancellationToken);

    public Task<SalrynStoreRemoteTransportViewModel> SaveExplicitLanTransportAsync(
        string bindAddress,
        string portText,
        CancellationToken cancellationToken = default)
    {
        int port = int.TryParse(portText, out int parsedPort)
            ? parsedPort
            : LanRemoteTransportConfig.DefaultPort;

        return SaveRemoteTransportAsync(
            new LanRemoteTransportConfigUpdateRequest(
                RemoteTransportEnabled: true,
                BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
                BindAddress: bindAddress,
                Port: port,
                RequirePairedTerminal: true,
                RequireActiveTerminal: true,
                RejectUnknownTerminals: true,
                AllowOfficialCommandsRemotely: false,
                AllowAdminCommandsRemotely: false,
                UpdatedByUserId: _session?.UserId),
            "Saved explicit LAN remote transport gate configuration.",
            cancellationToken);
    }

    public async Task<SalrynStoreRemoteTransportViewModel> DisableRemoteTransportAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanManageLanTransport)
        {
            SalrynStoreRemoteTransportViewModel current = await LoadRemoteTransportAsync(cancellationToken)
                .ConfigureAwait(false);
            return current with { StatusMessage = "You do not have permission to manage LAN remote transport." };
        }

        Result<LanRemoteTransportConfig> result = await _lanTransportConfig
            .DisableAsync(_session?.UserId, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? MapRemoteTransport(result.Value, "Remote transport disabled.")
            : (await LoadRemoteTransportAsync(cancellationToken).ConfigureAwait(false)) with { StatusMessage = result.ErrorDetail.Message };
    }

    private async Task<SalrynStoreRemoteTransportViewModel> SaveRemoteTransportAsync(
        LanRemoteTransportConfigUpdateRequest request,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (!CanManageLanTransport)
        {
            SalrynStoreRemoteTransportViewModel current = await LoadRemoteTransportAsync(cancellationToken)
                .ConfigureAwait(false);
            return current with { StatusMessage = "You do not have permission to manage LAN remote transport." };
        }

        Result<LanRemoteTransportConfig> result = await _lanTransportConfig
            .SaveAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? MapRemoteTransport(result.Value, successMessage)
            : (await LoadRemoteTransportAsync(cancellationToken).ConfigureAwait(false)) with { StatusMessage = result.ErrorDetail.Message };
    }

    public string BuildDiagnosticSummary(SalrynStoreViewModel model)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "SALRYN Deployment Diagnostic Summary",
            $"Installation Role: {model.InstallationRole}",
            $"Store ID: {model.StoreId}",
            $"Store Name: {model.StoreName}",
            $"Host ID: {model.HostId}",
            $"Host Display Name: {model.HostDisplayName}",
            $"Terminal ID: {model.TerminalId}",
            $"Terminal Name: {model.TerminalName}",
            $"Host Address: {model.HostAddress}",
            $"Host Port: {model.HostPort}",
            $"Pairing Credential Label: {model.PairingCredentialLabel}",
            $"Connection Status: {model.ConnectionStatus}",
            $"Database Authority: {model.IsDatabaseAuthority}",
            $"Official Sequence Authority: {model.IsOfficialSequenceAuthority}",
            $"Journal Authority: {model.IsJournalAuthority}",
            $"Backup Authority: {model.IsBackupAuthority}",
            $"LAN Transport Enabled: {model.LanTransportEnabled}"
        });
    }

    private SalrynStoreRemoteTransportViewModel MapRemoteTransport(
        LanRemoteTransportConfig config,
        string statusMessage)
    {
        string diagnostic = string.Join(Environment.NewLine, new[]
        {
            "LAN-M5 Remote Transport Gate",
            $"Remote transport enabled: {config.RemoteTransportEnabled}",
            $"Bind mode: {config.BindMode}",
            $"Bind address: {config.BindAddress}",
            $"Port: {config.Port}",
            $"Require paired terminal: {config.RequirePairedTerminal}",
            $"Reject unknown terminals: {config.RejectUnknownTerminals}",
            $"Allow official commands remotely: {config.AllowOfficialCommandsRemotely}",
            $"Allow admin commands remotely: {config.AllowAdminCommandsRemotely}",
            "Pairing activation and remote client handshake foundation exists.",
            "Remote client handshake only. Remote cashier/admin execution is not enabled yet.",
            "Terminal credential is never displayed after activation.",
            "Clients must not open the SQLite database directly."
        });

        return new SalrynStoreRemoteTransportViewModel(
            CanManageLanTransport,
            config.RemoteTransportEnabled,
            config.BindMode.ToString(),
            config.BindAddress,
            config.Port,
            config.RequirePairedTerminal,
            config.RejectUnknownTerminals,
            config.AllowOfficialCommandsRemotely,
            config.AllowAdminCommandsRemotely,
            diagnostic,
            statusMessage);
    }

    private SalrynStoreViewModel Map(DeploymentIdentitySnapshot snapshot, string statusMessage)
    {
        return new SalrynStoreViewModel(
            IsLoaded: true,
            CanManage: CanManage,
            InstallationRole: snapshot.Host.InstallationRole.ToString(),
            StoreId: snapshot.Store.StoreId.ToString("D"),
            StoreName: snapshot.Store.StoreName,
            HostId: snapshot.Host.HostId.ToString("D"),
            HostDisplayName: snapshot.Host.HostDisplayName,
            TerminalId: snapshot.Terminal.TerminalId.ToString("D"),
            TerminalName: snapshot.Terminal.TerminalName,
            HostAddress: snapshot.ClientConnection.HostAddress ?? string.Empty,
            HostPort: snapshot.ClientConnection.HostPort?.ToString() ?? string.Empty,
            PairingCredentialLabel: snapshot.ClientConnection.PairingCredentialLabel ?? string.Empty,
            ConnectionStatus: FormatConnectionStatus(snapshot.ClientConnection.ConnectionStatus),
            IsDatabaseAuthority: snapshot.Authority.IsDatabaseAuthority,
            IsOfficialSequenceAuthority: snapshot.Authority.IsOfficialSequenceAuthority,
            IsJournalAuthority: snapshot.Authority.IsJournalAuthority,
            IsBackupAuthority: snapshot.Authority.IsBackupAuthority,
            LanTransportEnabled: snapshot.Authority.LanTransportEnabled,
            StatusMessage: statusMessage,
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static string FormatConnectionStatus(ClientHostConnectionStatus status)
    {
        return status switch
        {
            ClientHostConnectionStatus.NotConfigured => "Not configured",
            ClientHostConnectionStatus.TransportNotImplemented => "Transport not implemented",
            ClientHostConnectionStatus.ConfiguredButOffline => "Configured but offline",
            _ => status.ToString()
        };
    }

    private TerminalRegistryActorContext CreateActorContext()
    {
        Guid actorUserId = _session?.UserId ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
        IReadOnlyCollection<string> permissions = _session?.Permissions.ToArray()
            ?? Array.Empty<string>();

        return new TerminalRegistryActorContext(actorUserId, permissions);
    }

    private static SalrynStoreTerminalRowViewModel MapTerminal(SalrynTerminalRegistration terminal) =>
        new(
            terminal.TerminalId,
            terminal.TerminalName,
            terminal.TerminalRole.ToString(),
            terminal.Status.ToString());

    private static SalrynStorePairingInviteRowViewModel MapPairingInvite(SalrynTerminalPairingInvite invite) =>
        new(
            invite.PairingId,
            invite.TargetTerminalName,
            invite.TargetTerminalRole.ToString(),
            invite.Status.ToString(),
            invite.ExpiresAtUtc);
}

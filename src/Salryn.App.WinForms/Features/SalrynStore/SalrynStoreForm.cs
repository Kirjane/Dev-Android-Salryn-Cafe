using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Deployment;
using Salryn.Core.TerminalRegistry;

namespace Salryn.App.WinForms.Features.SalrynStore;

public sealed class SalrynStoreForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly SalrynStorePresenter _presenter;

    private readonly Label _statusLabel = new();
    private readonly ComboBox _roleCombo = new();
    private readonly TextBox _storeNameBox = new();
    private readonly TextBox _hostDisplayNameBox = new();
    private readonly TextBox _terminalNameBox = new();
    private readonly TextBox _hostAddressBox = new();
    private readonly TextBox _hostPortBox = new();
    private readonly TextBox _pairingLabelBox = new();
    private readonly Label _storeIdLabel = new();
    private readonly Label _hostIdLabel = new();
    private readonly Label _terminalIdLabel = new();
    private readonly Label _connectionStatusLabel = new();
    private readonly Label _authorityLabel = new();
    private readonly Label _warningsLabel = new();
    private readonly Button _saveButton = new();
    private readonly Button _resetButton = new();
    private readonly Button _copyDiagnosticsButton = new();
    private readonly Button _refreshButton = new();
    private readonly ListView _terminalListView = new();
    private readonly ListView _pairingInviteListView = new();
    private readonly TextBox _pairingTargetNameBox = new();
    private readonly ComboBox _pairingTargetRoleCombo = new();
    private readonly Label _pairingCodeLabel = new();
    private readonly Label _terminalRegistryStatusLabel = new();
    private readonly Button _issuePairingButton = new();
    private readonly Button _revokePairingButton = new();
    private readonly Button _disableTerminalButton = new();
    private readonly Button _revokeTerminalButton = new();
    private readonly Button _refreshTerminalsButton = new();
    private readonly CheckBox _remoteTransportEnabledCheckBox = new();
    private readonly Label _remoteBindModeLabel = new();
    private readonly TextBox _remoteBindAddressBox = new();
    private readonly TextBox _remotePortBox = new();
    private readonly CheckBox _requirePairedTerminalCheckBox = new();
    private readonly CheckBox _rejectUnknownTerminalCheckBox = new();
    private readonly CheckBox _allowOfficialRemoteCheckBox = new();
    private readonly CheckBox _allowAdminRemoteCheckBox = new();
    private readonly TextBox _remoteDiagnosticBox = new();
    private readonly Button _saveLoopbackTransportButton = new();
    private readonly Button _saveExplicitLanTransportButton = new();
    private readonly Button _disableRemoteTransportButton = new();

    private SalrynStoreViewModel _state = SalrynStoreViewModel.Empty(false, "Loading...");
    private SalrynStoreTerminalRegistryViewModel _terminalRegistryState =
        new(Array.Empty<SalrynStoreTerminalRowViewModel>(), Array.Empty<SalrynStorePairingInviteRowViewModel>(), false, false, false, false, null, "Loading...");
    private SalrynStoreRemoteTransportViewModel _remoteTransportState =
        new(false, false, "LoopbackOnly", "127.0.0.1", 58161, true, true, false, false, string.Empty, "Loading...");
    private bool _isBusy;
    private bool _isRenderingRemoteTransport;

    public SalrynStoreForm(SalrynAppServices services, CurrentAppSession? session)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _presenter = new SalrynStorePresenter(
            services.DeploymentIdentity,
            services.TerminalRegistry,
            services.TerminalPairingPreparation,
            services.CreateLanRemoteTransportConfigService(),
            session);

        Text = "Salryn Store";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 900);
        Size = new Size(960, 980);
        Font = new Font("Segoe UI", 9F);
        BackColor = SalrynUi.Colors.AppBackground;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Salryn Store",
            "Manage local deployment identity and future LAN readiness."), 0, 0);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SalrynUi.Colors.AppBackground
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 38,
            BackColor = SalrynUi.Colors.AppBackground,
            Padding = new Padding(0, 0, 0, 12)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddSection(content, 0, "Deployment Role");
        AddField(content, 1, "Installation Role", _roleCombo);
        _roleCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _roleCombo.Items.AddRange(new object[]
        {
            InstallationRole.HostServer,
            InstallationRole.AllInOne,
            InstallationRole.ClientTerminal
        });

        AddSection(content, 2, "Store Identity");
        AddField(content, 3, "Store Name", _storeNameBox);
        AddReadOnlyField(content, 4, "Store ID", _storeIdLabel);

        AddSection(content, 5, "Local Machine Identity");
        AddField(content, 6, "Host Display Name", _hostDisplayNameBox);
        AddField(content, 7, "Terminal Name", _terminalNameBox);
        AddReadOnlyField(content, 8, "Host ID", _hostIdLabel);
        AddReadOnlyField(content, 9, "Terminal ID", _terminalIdLabel);

        AddSection(content, 10, "Host Connection");
        AddField(content, 11, "Host IP / Hostname", _hostAddressBox);
        AddField(content, 12, "Host Port", _hostPortBox);
        AddField(content, 13, "Pairing Credential Label", _pairingLabelBox);
        AddReadOnlyField(content, 14, "Connection Status", _connectionStatusLabel);

        AddSection(content, 15, "Authority Status");
        _authorityLabel.AutoSize = true;
        _authorityLabel.MaximumSize = new Size(680, 0);
        content.Controls.Add(_authorityLabel, 0, 16);
        content.SetColumnSpan(_authorityLabel, 2);

        AddSection(content, 17, "Terminal Registry");
        ConfigureTerminalListView();
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
        content.Controls.Add(_terminalListView, 0, 18);
        content.SetColumnSpan(_terminalListView, 2);

        var terminalActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };
        ConfigureActionButton(_disableTerminalButton, "Disable Terminal");
        ConfigureActionButton(_revokeTerminalButton, "Revoke Terminal");
        ConfigureActionButton(_refreshTerminalsButton, "Refresh Terminals");
        terminalActions.Controls.Add(_disableTerminalButton);
        terminalActions.Controls.Add(_revokeTerminalButton);
        terminalActions.Controls.Add(_refreshTerminalsButton);
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        content.Controls.Add(terminalActions, 0, 19);
        content.SetColumnSpan(terminalActions, 2);

        AddSection(content, 20, "Pairing Preparation");
        AddField(content, 21, "Target Terminal Name", _pairingTargetNameBox);
        _pairingTargetRoleCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _pairingTargetRoleCombo.Items.AddRange(new object[]
        {
            TerminalRegistrationRole.CashierClient,
            TerminalRegistrationRole.AdminClient
        });
        _pairingTargetRoleCombo.SelectedIndex = 0;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        content.Controls.Add(new Label { Text = "Target Terminal Role", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 22);
        _pairingTargetRoleCombo.Dock = DockStyle.Fill;
        content.Controls.Add(_pairingTargetRoleCombo, 1, 22);

        ConfigurePairingInviteListView();
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
        content.Controls.Add(_pairingInviteListView, 0, 23);
        content.SetColumnSpan(_pairingInviteListView, 2);

        var pairingActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };
        ConfigureActionButton(_issuePairingButton, "Issue Pairing Code");
        ConfigureActionButton(_revokePairingButton, "Revoke Pairing Invite");
        pairingActions.Controls.Add(_issuePairingButton);
        pairingActions.Controls.Add(_revokePairingButton);
        content.RowCount = 25;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        content.Controls.Add(pairingActions, 0, 24);
        content.SetColumnSpan(pairingActions, 2);

        _pairingCodeLabel.AutoSize = true;
        _pairingCodeLabel.MaximumSize = new Size(680, 0);
        content.RowCount = 26;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        content.Controls.Add(_pairingCodeLabel, 0, 25);
        content.SetColumnSpan(_pairingCodeLabel, 2);

        _terminalRegistryStatusLabel.AutoSize = true;
        _terminalRegistryStatusLabel.MaximumSize = new Size(680, 0);
        content.RowCount = 27;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        content.Controls.Add(_terminalRegistryStatusLabel, 0, 26);
        content.SetColumnSpan(_terminalRegistryStatusLabel, 2);

        AddSection(content, 27, "Remote Transport Gate");
        ConfigureReadOnlyCheckBox(_remoteTransportEnabledCheckBox, "Remote transport enabled");
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        content.Controls.Add(_remoteTransportEnabledCheckBox, 0, 28);
        content.SetColumnSpan(_remoteTransportEnabledCheckBox, 2);
        AddReadOnlyField(content, 29, "Bind Mode", _remoteBindModeLabel);
        AddField(content, 30, "Bind Address", _remoteBindAddressBox);
        AddField(content, 31, "Port", _remotePortBox);
        ConfigureReadOnlyCheckBox(_requirePairedTerminalCheckBox, "Require paired terminal");
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        content.Controls.Add(_requirePairedTerminalCheckBox, 0, 32);
        content.SetColumnSpan(_requirePairedTerminalCheckBox, 2);
        ConfigureReadOnlyCheckBox(_rejectUnknownTerminalCheckBox, "Reject unknown terminals");
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        content.Controls.Add(_rejectUnknownTerminalCheckBox, 0, 33);
        content.SetColumnSpan(_rejectUnknownTerminalCheckBox, 2);
        ConfigureReadOnlyCheckBox(_allowOfficialRemoteCheckBox, "Allow official commands remotely");
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        content.Controls.Add(_allowOfficialRemoteCheckBox, 0, 34);
        content.SetColumnSpan(_allowOfficialRemoteCheckBox, 2);
        ConfigureReadOnlyCheckBox(_allowAdminRemoteCheckBox, "Allow admin commands remotely");
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        content.Controls.Add(_allowAdminRemoteCheckBox, 0, 35);
        content.SetColumnSpan(_allowAdminRemoteCheckBox, 2);

        var remoteActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };
        ConfigureActionButton(_saveLoopbackTransportButton, "Save Loopback-Only Config");
        ConfigureActionButton(_saveExplicitLanTransportButton, "Save Explicit LAN Config");
        ConfigureActionButton(_disableRemoteTransportButton, "Disable Remote Transport");
        remoteActions.Controls.Add(_saveLoopbackTransportButton);
        remoteActions.Controls.Add(_saveExplicitLanTransportButton);
        remoteActions.Controls.Add(_disableRemoteTransportButton);
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        content.Controls.Add(remoteActions, 0, 36);
        content.SetColumnSpan(remoteActions, 2);

        _remoteDiagnosticBox.Multiline = true;
        _remoteDiagnosticBox.ReadOnly = true;
        _remoteDiagnosticBox.ScrollBars = ScrollBars.Vertical;
        _remoteDiagnosticBox.Height = 120;
        _remoteDiagnosticBox.Dock = DockStyle.Fill;
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
        content.Controls.Add(_remoteDiagnosticBox, 0, 37);
        content.SetColumnSpan(_remoteDiagnosticBox, 2);

        scroll.Controls.Add(content);
        root.Controls.Add(scroll, 0, 1);

        _warningsLabel.Dock = DockStyle.Fill;
        _warningsLabel.Padding = new Padding(8);
        _warningsLabel.BackColor = SalrynUi.Colors.Surface;
        _warningsLabel.BorderStyle = BorderStyle.FixedSingle;
        _warningsLabel.Text =
            "Client terminals do not own the database. Clients must not open shared SQLite. Remote client handshake only. Remote cashier/admin execution is not enabled yet." +
            Environment.NewLine +
            "Pairing preparation only. Cross-PC LAN client connection is not enabled yet. Cross-PC LAN business execution is not enabled yet.";
        root.Controls.Add(_warningsLabel, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        ConfigureActionButton(_saveButton, "Save Configuration");
        ConfigureActionButton(_resetButton, "Reset to All-in-One");
        ConfigureActionButton(_copyDiagnosticsButton, "Copy Diagnostic Summary");
        ConfigureActionButton(_refreshButton, "Refresh");

        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_resetButton);
        actions.Controls.Add(_copyDiagnosticsButton);
        actions.Controls.Add(_refreshButton);
        root.Controls.Add(actions, 0, 3);

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 28;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_statusLabel);
    }

    private void WireEvents()
    {
        _roleCombo.SelectedIndexChanged += (_, _) => UpdateRoleFieldState();
        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        _resetButton.Click += async (_, _) => await ResetAsync().ConfigureAwait(true);
        _copyDiagnosticsButton.Click += (_, _) => CopyDiagnostics();
        _refreshButton.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);
        _refreshTerminalsButton.Click += async (_, _) => await LoadTerminalRegistryAsync().ConfigureAwait(true);
        _issuePairingButton.Click += async (_, _) => await IssuePairingInviteAsync().ConfigureAwait(true);
        _revokePairingButton.Click += async (_, _) => await RevokePairingInviteAsync().ConfigureAwait(true);
        _disableTerminalButton.Click += async (_, _) => await DisableSelectedTerminalAsync().ConfigureAwait(true);
        _revokeTerminalButton.Click += async (_, _) => await RevokeSelectedTerminalAsync().ConfigureAwait(true);
        _remoteTransportEnabledCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_isRenderingRemoteTransport)
            {
                _saveExplicitLanTransportButton.Enabled =
                    !_isBusy && _remoteTransportState.CanManage && _remoteTransportEnabledCheckBox.Checked;
            }
        };
        _saveLoopbackTransportButton.Click += async (_, _) => await SaveLoopbackTransportAsync().ConfigureAwait(true);
        _saveExplicitLanTransportButton.Click += async (_, _) => await SaveExplicitLanTransportAsync().ConfigureAwait(true);
        _disableRemoteTransportButton.Click += async (_, _) => await DisableRemoteTransportAsync().ConfigureAwait(true);
    }

    private async Task LoadAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);

        try
        {
            _state = await _presenter.LoadAsync().ConfigureAwait(true);
            Render();
            await LoadTerminalRegistryAsync().ConfigureAwait(true);
            await LoadRemoteTransportAsync().ConfigureAwait(true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAsync()
    {
        if (_isBusy || !_state.CanManage)
        {
            return;
        }

        SetBusy(true);

        try
        {
            InstallationRole role = _roleCombo.SelectedItem is InstallationRole selected
                ? selected
                : InstallationRole.AllInOne;

            SalrynStoreOperationResult result = await _presenter.SaveAsync(
                new SalrynStoreSaveInput(
                    role,
                    _storeNameBox.Text,
                    _hostDisplayNameBox.Text,
                    _terminalNameBox.Text,
                    _hostAddressBox.Text,
                    _hostPortBox.Text,
                    _pairingLabelBox.Text)).ConfigureAwait(true);

            _state = result.State;
            Render();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ResetAsync()
    {
        if (_isBusy || !_state.CanManage)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            "Reset local deployment configuration to All-in-One authority?",
            "Reset Deployment Configuration",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);

        try
        {
            SalrynStoreOperationResult result = await _presenter.ResetAsync().ConfigureAwait(true);
            _state = result.State;
            Render();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyDiagnostics()
    {
        Clipboard.SetText(_presenter.BuildDiagnosticSummary(_state));
        _statusLabel.Text = "Diagnostic summary copied to clipboard.";
    }

    private void Render()
    {
        _statusLabel.ForeColor = _state.HasError ? Color.DarkRed : SystemColors.ControlText;
        _statusLabel.Text = _state.HasError
            ? $"{_state.StatusMessage} {_state.ErrorMessage}"
            : _state.StatusMessage;

        _storeNameBox.Text = _state.StoreName;
        _hostDisplayNameBox.Text = _state.HostDisplayName;
        _terminalNameBox.Text = _state.TerminalName;
        _hostAddressBox.Text = _state.HostAddress;
        _hostPortBox.Text = string.IsNullOrWhiteSpace(_state.HostPort) ? "58161" : _state.HostPort;
        _pairingLabelBox.Text = _state.PairingCredentialLabel;
        _storeIdLabel.Text = _state.StoreId;
        _hostIdLabel.Text = _state.HostId;
        _terminalIdLabel.Text = _state.TerminalId;
        _connectionStatusLabel.Text = _state.ConnectionStatus;
        _authorityLabel.Text =
            $"Database authority: {_state.IsDatabaseAuthority}{Environment.NewLine}" +
            $"Official sequence authority: {_state.IsOfficialSequenceAuthority}{Environment.NewLine}" +
            $"Journal authority: {_state.IsJournalAuthority}{Environment.NewLine}" +
            $"Backup authority: {_state.IsBackupAuthority}{Environment.NewLine}" +
            $"LAN transport enabled: {_state.LanTransportEnabled}";

        if (Enum.TryParse(_state.InstallationRole, out InstallationRole role))
        {
            _roleCombo.SelectedItem = role;
        }
        else
        {
            _roleCombo.SelectedItem = InstallationRole.AllInOne;
        }

        bool canEdit = _state.CanManage;
        _saveButton.Enabled = canEdit;
        _resetButton.Enabled = canEdit;
        _storeNameBox.ReadOnly = !canEdit;
        _hostDisplayNameBox.ReadOnly = !canEdit;
        _terminalNameBox.ReadOnly = !canEdit;
        _hostAddressBox.ReadOnly = !canEdit;
        _hostPortBox.ReadOnly = !canEdit;
        _pairingLabelBox.ReadOnly = !canEdit;
        _roleCombo.Enabled = canEdit;

        UpdateRoleFieldState();
        RenderTerminalRegistry();
    }

    private async Task LoadTerminalRegistryAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);

        try
        {
            _terminalRegistryState = await _presenter.LoadTerminalRegistryAsync().ConfigureAwait(true);
            RenderTerminalRegistry();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task IssuePairingInviteAsync()
    {
        if (_isBusy || !_terminalRegistryState.CanIssuePairing)
        {
            return;
        }

        TerminalRegistrationRole role = _pairingTargetRoleCombo.SelectedItem is TerminalRegistrationRole selected
            ? selected
            : TerminalRegistrationRole.CashierClient;

        SetBusy(true);

        try
        {
            _terminalRegistryState = await _presenter.IssuePairingInviteAsync(
                _pairingTargetNameBox.Text,
                role).ConfigureAwait(true);
            RenderTerminalRegistry();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RevokePairingInviteAsync()
    {
        if (_isBusy || !_terminalRegistryState.CanRevokePairing || _pairingInviteListView.SelectedItems.Count == 0)
        {
            return;
        }

        Guid pairingId = Guid.Parse(_pairingInviteListView.SelectedItems[0].Text);
        SetBusy(true);

        try
        {
            _terminalRegistryState = await _presenter.RevokePairingInviteAsync(pairingId).ConfigureAwait(true);
            RenderTerminalRegistry();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DisableSelectedTerminalAsync()
    {
        if (_isBusy || !_terminalRegistryState.CanManageTerminals || _terminalListView.SelectedItems.Count == 0)
        {
            return;
        }

        Guid terminalId = Guid.Parse(_terminalListView.SelectedItems[0].Text);
        SetBusy(true);

        try
        {
            _terminalRegistryState = await _presenter.DisableTerminalAsync(terminalId).ConfigureAwait(true);
            RenderTerminalRegistry();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RevokeSelectedTerminalAsync()
    {
        if (_isBusy || !_terminalRegistryState.CanManageTerminals || _terminalListView.SelectedItems.Count == 0)
        {
            return;
        }

        Guid terminalId = Guid.Parse(_terminalListView.SelectedItems[0].Text);
        SetBusy(true);

        try
        {
            _terminalRegistryState = await _presenter.RevokeTerminalAsync(terminalId).ConfigureAwait(true);
            RenderTerminalRegistry();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderTerminalRegistry()
    {
        _terminalListView.Items.Clear();
        foreach (SalrynStoreTerminalRowViewModel terminal in _terminalRegistryState.Terminals)
        {
            ListViewItem item = new(terminal.TerminalId.ToString("D"));
            item.SubItems.Add(terminal.TerminalName);
            item.SubItems.Add(terminal.Role);
            item.SubItems.Add(terminal.Status);
            _terminalListView.Items.Add(item);
        }

        _pairingInviteListView.Items.Clear();
        foreach (SalrynStorePairingInviteRowViewModel invite in _terminalRegistryState.PairingInvites)
        {
            ListViewItem item = new(invite.PairingId.ToString("D"));
            item.SubItems.Add(invite.TargetTerminalName);
            item.SubItems.Add(invite.Role);
            item.SubItems.Add(invite.Status);
            item.SubItems.Add(invite.ExpiresAtUtc.ToLocalTime().ToString("g"));
            _pairingInviteListView.Items.Add(item);
        }

        _pairingCodeLabel.Text = string.IsNullOrWhiteSpace(_terminalRegistryState.LastIssuedPairingCode)
            ? string.Empty
            : $"Pairing code (shown once): {_terminalRegistryState.LastIssuedPairingCode}";
        _terminalRegistryStatusLabel.Text = _terminalRegistryState.StatusMessage;

        bool canView = _terminalRegistryState.CanViewTerminals;
        _terminalListView.Enabled = canView;
        _pairingInviteListView.Enabled = canView;
        _issuePairingButton.Enabled = _terminalRegistryState.CanIssuePairing;
        _revokePairingButton.Enabled = _terminalRegistryState.CanRevokePairing;
        _disableTerminalButton.Enabled = _terminalRegistryState.CanManageTerminals;
        _revokeTerminalButton.Enabled = _terminalRegistryState.CanManageTerminals;
        _pairingTargetNameBox.ReadOnly = !_terminalRegistryState.CanIssuePairing;
        _pairingTargetRoleCombo.Enabled = _terminalRegistryState.CanIssuePairing;
    }

    private async Task LoadRemoteTransportAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);

        try
        {
            _remoteTransportState = await _presenter.LoadRemoteTransportAsync().ConfigureAwait(true);
            RenderRemoteTransport();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveLoopbackTransportAsync()
    {
        if (_isBusy || !_remoteTransportState.CanManage)
        {
            return;
        }

        SetBusy(true);

        try
        {
            _remoteTransportState = await _presenter.SaveLoopbackOnlyTransportAsync().ConfigureAwait(true);
            RenderRemoteTransport();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveExplicitLanTransportAsync()
    {
        if (_isBusy || !_remoteTransportState.CanManage || !_remoteTransportEnabledCheckBox.Checked)
        {
            return;
        }

        SetBusy(true);

        try
        {
            _remoteTransportState = await _presenter.SaveExplicitLanTransportAsync(
                _remoteBindAddressBox.Text,
                _remotePortBox.Text).ConfigureAwait(true);
            RenderRemoteTransport();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DisableRemoteTransportAsync()
    {
        if (_isBusy || !_remoteTransportState.CanManage)
        {
            return;
        }

        SetBusy(true);

        try
        {
            _remoteTransportState = await _presenter.DisableRemoteTransportAsync().ConfigureAwait(true);
            RenderRemoteTransport();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderRemoteTransport()
    {
        _isRenderingRemoteTransport = true;

        try
        {
            _remoteTransportEnabledCheckBox.Checked = _remoteTransportState.RemoteTransportEnabled;
            _remoteBindModeLabel.Text = _remoteTransportState.BindMode;
            _remoteBindAddressBox.Text = _remoteTransportState.BindAddress;
            _remotePortBox.Text = _remoteTransportState.Port.ToString();
            _requirePairedTerminalCheckBox.Checked = _remoteTransportState.RequirePairedTerminal;
            _rejectUnknownTerminalCheckBox.Checked = _remoteTransportState.RejectUnknownTerminals;
            _allowOfficialRemoteCheckBox.Checked = _remoteTransportState.AllowOfficialCommandsRemotely;
            _allowAdminRemoteCheckBox.Checked = _remoteTransportState.AllowAdminCommandsRemotely;
            _remoteDiagnosticBox.Text = _remoteTransportState.DiagnosticSummary + Environment.NewLine + _remoteTransportState.StatusMessage;
        }
        finally
        {
            _isRenderingRemoteTransport = false;
        }

        bool canManage = _remoteTransportState.CanManage;
        _remoteTransportEnabledCheckBox.Enabled = canManage;
        _remoteBindAddressBox.ReadOnly = !canManage;
        _remotePortBox.ReadOnly = !canManage;
        _saveLoopbackTransportButton.Enabled = canManage;
        _saveExplicitLanTransportButton.Enabled = canManage && _remoteTransportEnabledCheckBox.Checked;
        _disableRemoteTransportButton.Enabled = canManage;
    }

    private void UpdateRoleFieldState()
    {
        bool isClient = _roleCombo.SelectedItem is InstallationRole.ClientTerminal;

        _hostDisplayNameBox.ReadOnly = isClient || !_state.CanManage;
        _hostAddressBox.Enabled = isClient;
        _hostPortBox.Enabled = isClient;
        _pairingLabelBox.Enabled = isClient;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UseWaitCursor = isBusy;
        _saveButton.Enabled = !isBusy && _state.CanManage;
        _resetButton.Enabled = !isBusy && _state.CanManage;
        _refreshButton.Enabled = !isBusy;
        _copyDiagnosticsButton.Enabled = !isBusy;
        _saveLoopbackTransportButton.Enabled = !isBusy && _remoteTransportState.CanManage;
        _saveExplicitLanTransportButton.Enabled = !isBusy && _remoteTransportState.CanManage && _remoteTransportEnabledCheckBox.Checked;
        _disableRemoteTransportButton.Enabled = !isBusy && _remoteTransportState.CanManage;
    }

    private static void AddSection(TableLayoutPanel panel, int row, string title)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        Label label = SalrynUi.CreateSectionTitle(title);
        panel.Controls.Add(label, 0, row);
        panel.SetColumnSpan(label, 2);
    }

    private static void AddField(TableLayoutPanel panel, int row, string labelText, Control editor)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        panel.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);

        editor.Dock = DockStyle.Fill;
        panel.Controls.Add(editor, 1, row);
    }

    private static void AddReadOnlyField(TableLayoutPanel panel, int row, string labelText, Label valueLabel)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        panel.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);

        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(valueLabel, 1, row);
    }

    private static void ConfigureActionButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Padding = new Padding(12, 6, 12, 6);
    }

    private static void ConfigureReadOnlyCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
    }

    private void ConfigureTerminalListView()
    {
        _terminalListView.View = View.Details;
        _terminalListView.FullRowSelect = true;
        _terminalListView.Height = 120;
        _terminalListView.Columns.Add("Terminal ID", 220);
        _terminalListView.Columns.Add("Name", 160);
        _terminalListView.Columns.Add("Role", 140);
        _terminalListView.Columns.Add("Status", 100);
    }

    private void ConfigurePairingInviteListView()
    {
        _pairingInviteListView.View = View.Details;
        _pairingInviteListView.FullRowSelect = true;
        _pairingInviteListView.Height = 120;
        _pairingInviteListView.Columns.Add("Pairing ID", 220);
        _pairingInviteListView.Columns.Add("Target Name", 140);
        _pairingInviteListView.Columns.Add("Role", 120);
        _pairingInviteListView.Columns.Add("Status", 80);
        _pairingInviteListView.Columns.Add("Expires", 120);
    }
}

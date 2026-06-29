using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.Models;

namespace Salryn.App.WinForms.Features.Users;

// Kirch file guide: Users Admin MVP form.
// UI location: opened from Shell > Users.
// Frontend note: controls are safe to restyle; keep all validation and repository writes presenter-backed.

public sealed class UsersAdminForm : Form
{
    private readonly UsersAdminPresenter _presenter;

    private DataGridView _usersGrid = null!;
    private TextBox _usernameTextBox = null!;
    private TextBox _displayNameTextBox = null!;
    private ComboBox _roleComboBox = null!;
    private TextBox _passwordTextBox = null!;
    private TextBox _confirmPasswordTextBox = null!;
    private Button _createButton = null!;
    private Button _saveButton = null!;
    private Button _resetPasswordButton = null!;
    private Button _deactivateButton = null!;
    private Button _reactivateButton = null!;
    private Button _refreshButton = null!;
    private Label _statusLabel = null!;

    // Kirch B7: hover guidance for the user admin actions.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

    public UsersAdminForm(UsersAdminPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        InitializeComponent();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        await LoadUsersAsync();
    }

    private void InitializeComponent()
    {
        Text = "Users Admin";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 660);
        Size = new Size(1120, 700);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Users",
            "Manage operator accounts: create, edit roles, reset passwords, and control access."), 0, 0);

        _usersGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        _usersGrid.SelectionChanged += (_, _) => PopulateSelectedUser();

        AddTextColumn("Username", "Username", 160);
        AddTextColumn("DisplayName", "Display Name", 220);
        AddTextColumn("RoleName", "Role", 130);
        AddTextColumn("StatusText", "Status", 100);
        AddTextColumn("CreatedAtText", "Created", 150);

        SalrynUi.ApplyLedgerGridStyle(_usersGrid);
        _usersGrid.CellFormatting += UsersGridStatusFormatting;

        root.Controls.Add(_usersGrid, 0, 1);

        root.Controls.Add(BuildEditor(), 0, 2);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SalrynUi.Colors.TextMuted
        };
        SalrynUi.ConfigureFooterStatus(_statusLabel);

        root.Controls.Add(_statusLabel, 0, 3);
        UpdateActionState();
    }

    // Client-side semantic coloring of the Status column from existing row values.
    private void UsersGridStatusFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        DataGridViewColumn column = _usersGrid.Columns[e.ColumnIndex];

        if (string.Equals(column.HeaderText, "Status", StringComparison.Ordinal))
        {
            SalrynUi.ApplyStatusCellColor(e.CellStyle, Convert.ToString(e.Value) ?? string.Empty);
        }
    }

    private Control BuildEditor()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SalrynUi.Colors.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10, 6, 10, 6)
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 4,
            Padding = new Padding(0, 8, 0, 0)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        panel.Controls.Add(BuildLabel("Username"), 0, 0);
        panel.Controls.Add(BuildLabel("Display Name"), 1, 0);
        panel.Controls.Add(BuildLabel("Role"), 2, 0);
        panel.Controls.Add(BuildLabel("Password"), 3, 0);
        panel.Controls.Add(BuildLabel("Confirm"), 4, 0);

        _usernameTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "username"
        };

        _displayNameTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "display name"
        };

        _roleComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(UsersAdminRoleOptionViewModel.RoleName),
            ValueMember = nameof(UsersAdminRoleOptionViewModel.RoleId)
        };

        _passwordTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "new password"
        };

        _confirmPasswordTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "confirm"
        };

        _refreshButton = BuildSemanticButton("Refresh", SalrynUi.ConfigureSecondaryButton);
        _refreshButton.Click += async (_, _) => await LoadUsersAsync();

        panel.Controls.Add(_usernameTextBox, 0, 1);
        panel.Controls.Add(_displayNameTextBox, 1, 1);
        panel.Controls.Add(_roleComboBox, 2, 1);
        panel.Controls.Add(_passwordTextBox, 3, 1);
        panel.Controls.Add(_confirmPasswordTextBox, 4, 1);
        panel.Controls.Add(_refreshButton, 5, 1);

        // Semantic action styling: create=success, save=primary, reset=warning,
        // deactivate=danger, reactivate=success.
        _createButton = BuildSemanticButton("Create", SalrynUi.ConfigureSuccessButton);
        _createButton.Click += async (_, _) => await CreateUserAsync();

        _saveButton = BuildSemanticButton("Save", SalrynUi.ConfigurePrimaryButton);
        _saveButton.Click += async (_, _) => await SaveUserAsync();

        _resetPasswordButton = BuildSemanticButton("Reset Password", SalrynUi.ConfigureWarningButton);
        _resetPasswordButton.Click += async (_, _) => await ResetPasswordAsync();

        _deactivateButton = BuildSemanticButton("Deactivate", SalrynUi.ConfigureDangerButton);
        _deactivateButton.Click += async (_, _) => await DeactivateUserAsync();

        _reactivateButton = BuildSemanticButton("Reactivate", SalrynUi.ConfigureSuccessButton);
        _reactivateButton.Click += async (_, _) => await ReactivateUserAsync();

        _toolTip.SetToolTip(_createButton, "Create a new user from the fields above.");
        _toolTip.SetToolTip(_saveButton, "Save username, display name, and role changes for the selected user.");
        _toolTip.SetToolTip(_resetPasswordButton, "Set a new password for the selected user.");
        _toolTip.SetToolTip(_deactivateButton, "Disable sign-in for the selected user (a reason is required).");
        _toolTip.SetToolTip(_reactivateButton, "Re-enable sign-in for the selected user.");
        _toolTip.SetToolTip(_refreshButton, "Reload the user list from the database.");

        panel.Controls.Add(BuildLabel("Actions"), 0, 2);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 2)!, 6);

        panel.Controls.Add(_createButton, 0, 3);
        panel.Controls.Add(_saveButton, 1, 3);
        panel.Controls.Add(_resetPasswordButton, 2, 3);
        panel.Controls.Add(_deactivateButton, 3, 3);
        panel.Controls.Add(_reactivateButton, 4, 3);

        card.Controls.Add(panel);
        card.Controls.Add(SalrynUi.CreateSectionTitle("User Editor"));
        return card;
    }

    private static Label BuildLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Color.FromArgb(75, 85, 99),
            TextAlign = ContentAlignment.BottomLeft
        };
    }

    // Build a semantic action button styled via SalrynUi, kept fill-docked for the editor grid.
    private static Button BuildSemanticButton(
        string text,
        Action<Button, string> configure)
    {
        var button = new Button();
        configure(button, text);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(4);
        return button;
    }

    private void AddTextColumn(
        string propertyName,
        string headerText,
        int width)
    {
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
    }

    private async Task LoadUsersAsync()
    {
        SetBusy(true, "Loading users...");

        UsersAdminOperationResult result = await _presenter.LoadAsync();

        Render(result.State);
        SetBusy(false, result.IsSuccess ? result.State.StatusMessage : result.ErrorMessage ?? "Users load failed.");
    }

    private async Task CreateUserAsync()
    {
        UsersAdminOperationResult result = await _presenter.CreateUserAsync(
            new UsersAdminCreateInput(
                _usernameTextBox.Text,
                _displayNameTextBox.Text,
                GetSelectedRoleId(),
                _passwordTextBox.Text,
                _confirmPasswordTextBox.Text));

        Render(result.State);
        AfterOperation(result, "User created.");
    }

    private async Task SaveUserAsync()
    {
        UsersAdminRowViewModel? selected = GetSelectedUser();

        UsersAdminOperationResult result = await _presenter.UpdateUserAsync(
            new UsersAdminUpdateInput(
                selected?.UserId ?? Guid.Empty,
                _usernameTextBox.Text,
                _displayNameTextBox.Text,
                GetSelectedRoleId()));

        Render(result.State);
        AfterOperation(result, "User updated.");
    }

    private async Task ResetPasswordAsync()
    {
        UsersAdminRowViewModel? selected = GetSelectedUser();

        UsersAdminOperationResult result = await _presenter.ResetPasswordAsync(
            new UsersAdminPasswordResetInput(
                selected?.UserId ?? Guid.Empty,
                _passwordTextBox.Text,
                _confirmPasswordTextBox.Text));

        Render(result.State);
        AfterOperation(result, "Password reset.");
    }

    private async Task DeactivateUserAsync()
    {
        UsersAdminRowViewModel? selected = GetSelectedUser();

        if (selected is null)
        {
            J20MvpUi.ShowBlockedAction(this, "No User Selected", "Select a user in the list before deactivating.");
            return;
        }

        // Kirch B7: confirm this account-disabling action before asking for the required reason.
        if (!J20MvpUi.ConfirmDestructiveAction(
                this,
                "Confirm Deactivate User",
                "Deactivate \"" + selected.DisplayName + "\"? They will no longer be able to sign in until reactivated."))
        {
            _statusLabel.Text = "Deactivate cancelled.";
            return;
        }

        string? reason = PromptDeactivateReason();

        if (string.IsNullOrWhiteSpace(reason))
        {
            _statusLabel.Text = "Deactivate cancelled. A reason is required.";
            return;
        }

        UsersAdminOperationResult result = await _presenter.SetUserStatusAsync(
            selected?.UserId ?? Guid.Empty,
            UserStatus.Inactive,
            reason);

        Render(result.State);
        AfterOperation(result, "User deactivated.");
    }

    private async Task ReactivateUserAsync()
    {
        UsersAdminRowViewModel? selected = GetSelectedUser();

        UsersAdminOperationResult result = await _presenter.SetUserStatusAsync(
            selected?.UserId ?? Guid.Empty,
            UserStatus.Active,
            reason: null);

        Render(result.State);
        AfterOperation(result, "User reactivated.");
    }

    private void Render(UsersAdminViewModel state)
    {
        UsersAdminRoleOptionViewModel[] roles = state.Roles.ToArray();
        _roleComboBox.DataSource = roles;
        _usersGrid.DataSource = state.Users.ToList();
        _statusLabel.Text = state.HasError
            ? $"{state.ErrorCode}: {state.ErrorMessage}"
            : state.StatusMessage;

        PopulateSelectedUser();
        UpdateActionState();
    }

    private void PopulateSelectedUser()
    {
        UsersAdminRowViewModel? selected = GetSelectedUser();

        if (selected is null)
        {
            UpdateActionState();
            return;
        }

        _usernameTextBox.Text = selected.Username;
        _displayNameTextBox.Text = selected.DisplayName;
        SelectRole(selected.RoleId, selected.RoleName);
        _passwordTextBox.Clear();
        _confirmPasswordTextBox.Clear();
        UpdateActionState();
    }

    private void SelectRole(Guid roleId, string roleName)
    {
        foreach (object item in _roleComboBox.Items)
        {
            if (item is not UsersAdminRoleOptionViewModel option)
            {
                continue;
            }

            if (option.RoleId == roleId ||
                string.Equals(option.RoleName, roleName, StringComparison.OrdinalIgnoreCase))
            {
                _roleComboBox.SelectedItem = option;
                return;
            }
        }

        _roleComboBox.SelectedIndex = _roleComboBox.Items.Count > 0 ? 0 : -1;
    }

    private UsersAdminRowViewModel? GetSelectedUser()
    {
        if (_usersGrid.CurrentRow?.DataBoundItem is UsersAdminRowViewModel row)
        {
            return row;
        }

        return null;
    }

    private Guid GetSelectedRoleId()
    {
        return _roleComboBox.SelectedItem is UsersAdminRoleOptionViewModel role
            ? role.RoleId
            : Guid.Empty;
    }

    private void UpdateActionState()
    {
        bool hasSelection = GetSelectedUser() is not null;
        _saveButton.Enabled = hasSelection;
        _resetPasswordButton.Enabled = hasSelection;
        _deactivateButton.Enabled = hasSelection;
        _reactivateButton.Enabled = hasSelection;
    }

    private void AfterOperation(
        UsersAdminOperationResult result,
        string successMessage)
    {
        _passwordTextBox.Clear();
        _confirmPasswordTextBox.Clear();
        _statusLabel.Text = result.IsSuccess
            ? successMessage
            : $"{result.ErrorCode}: {result.ErrorMessage}";
    }

    private void SetBusy(bool isBusy, string status)
    {
        _createButton.Enabled = !isBusy;
        _saveButton.Enabled = !isBusy && GetSelectedUser() is not null;
        _resetPasswordButton.Enabled = !isBusy && GetSelectedUser() is not null;
        _deactivateButton.Enabled = !isBusy && GetSelectedUser() is not null;
        _reactivateButton.Enabled = !isBusy && GetSelectedUser() is not null;
        _refreshButton.Enabled = !isBusy;
        _statusLabel.Text = status;
    }

    private string? PromptDeactivateReason()
    {
        using var dialog = new Form
        {
            Text = "Deactivate User",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(420, 150)
        };

        var label = new Label
        {
            Left = 16,
            Top = 14,
            Width = 388,
            Text = "Reason is required when deactivating a user."
        };

        var reasonBox = new TextBox
        {
            Left = 16,
            Top = 42,
            Width = 388
        };

        var okButton = new Button
        {
            Text = "Deactivate",
            DialogResult = DialogResult.OK,
            Left = 206,
            Width = 96,
            Top = 92
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Left = 310,
            Width = 94,
            Top = 92
        };

        dialog.Controls.Add(label);
        dialog.Controls.Add(reasonBox);
        dialog.Controls.Add(okButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) == DialogResult.OK
            ? reasonBox.Text
            : null;
    }
}

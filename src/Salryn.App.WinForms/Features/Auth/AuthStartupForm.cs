using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.Auth;

public sealed class AuthStartupForm : Form
{
    private readonly IAuthenticationService _authenticationService;

    private bool _isFirstRun;

    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private Label _messageLabel = null!;
    private TextBox _usernameTextBox = null!;
    private TextBox _displayNameTextBox = null!;
    private TextBox _passwordTextBox = null!;
    private TextBox _confirmPasswordTextBox = null!;
    private Button _submitButton = null!;
    private Button _cancelButton = null!;

    public AuthStartupForm(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService ??
            throw new ArgumentNullException(nameof(authenticationService));

        InitializeComponent();

        Shown += async (_, _) => await ConfigureModeAsync();
    }

    public CurrentAppSession? AuthenticatedSession { get; private set; }

    // Kirch: Builds the login and first-run admin setup screen used before opening the main shell.
    private void InitializeComponent()
    {
        Text = "SALRYN Login";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 520);
        BackColor = Color.FromArgb(246, 247, 249);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(28)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        Controls.Add(root);

        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "SALRYN",
            Font = new Font(Font.FontFamily, 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 24, 39),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _subtitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Loading authentication mode...",
            Font = new Font(Font.FontFamily, 10, FontStyle.Regular),
            ForeColor = Color.FromArgb(75, 85, 99),
            TextAlign = ContentAlignment.TopLeft
        };

        root.Controls.Add(_titleLabel, 0, 0);
        root.Controls.Add(_subtitleLabel, 0, 1);

        _usernameTextBox = AddField(root, 2, "Username");
        _displayNameTextBox = AddField(root, 3, "Display name");
        _passwordTextBox = AddField(root, 4, "Password");
        _confirmPasswordTextBox = AddField(root, 5, "Confirm password");

        _passwordTextBox.UseSystemPasswordChar = true;
        _confirmPasswordTextBox.UseSystemPasswordChar = true;

        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Empty,
            ForeColor = Color.FromArgb(185, 28, 28),
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(0, 12, 0, 0)
        };

        root.Controls.Add(_messageLabel, 0, 6);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        _submitButton = new Button
        {
            Width = 120,
            Height = 36,
            Text = "Continue",
            BackColor = Color.FromArgb(31, 41, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        _submitButton.FlatAppearance.BorderSize = 0;
        _submitButton.Click += async (_, _) => await SubmitAsync();

        _cancelButton = new Button
        {
            Width = 100,
            Height = 36,
            Text = "Cancel"
        };

        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        actions.Controls.Add(_submitButton);
        actions.Controls.Add(_cancelButton);

        root.Controls.Add(actions, 0, 7);
    }

    private static TextBox AddField(
        TableLayoutPanel root,
        int row,
        string label)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var fieldLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = label,
            ForeColor = Color.FromArgb(55, 65, 81),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = SystemFonts.MessageBoxFont
        };

        panel.Controls.Add(fieldLabel, 0, 0);
        panel.Controls.Add(textBox, 0, 1);

        root.Controls.Add(panel, 0, row);

        return textBox;
    }

    private async Task ConfigureModeAsync()
    {
        SetBusy(true, "Checking users...");

        Result<bool> hasAnyActiveUserResult =
            await _authenticationService.HasAnyActiveUserAsync();

        if (hasAnyActiveUserResult.IsFailure)
        {
            SetBusy(false, hasAnyActiveUserResult.Error);
            return;
        }

        _isFirstRun = !hasAnyActiveUserResult.Value;

        if (_isFirstRun)
        {
            Text = "SALRYN First-Run Setup";
            _subtitleLabel.Text = "Create the first Admin account for this local SALRYN database.";
            _displayNameTextBox.Visible = true;
            _confirmPasswordTextBox.Visible = true;
            _submitButton.Text = "Create Admin";
        }
        else
        {
            Text = "SALRYN Login";
            _subtitleLabel.Text = "Login with an active SALRYN user account.";
            _displayNameTextBox.Visible = false;
            _confirmPasswordTextBox.Visible = false;
            _submitButton.Text = "Login";
        }

        SetBusy(false, string.Empty);
        _usernameTextBox.Focus();
    }

    private async Task SubmitAsync()
    {
        SetBusy(true, string.Empty);

        Result<AuthenticatedUserReadModel> result = _isFirstRun
            ? await _authenticationService.CreateFirstAdminAsync(
                new FirstRunAdminSetupRequest(
                    _usernameTextBox.Text,
                    _displayNameTextBox.Text,
                    _passwordTextBox.Text,
                    _confirmPasswordTextBox.Text))
            : await _authenticationService.LoginAsync(
                new LoginRequest(
                    _usernameTextBox.Text,
                    _passwordTextBox.Text));

        if (result.IsFailure)
        {
            SetBusy(false, result.Error);
            return;
        }

        AuthenticatedSession = ToCurrentAppSession(result.Value);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static CurrentAppSession ToCurrentAppSession(
        AuthenticatedUserReadModel user)
    {
        return new CurrentAppSession(
            user.UserId,
            user.Username,
            user.DisplayName,
            user.RoleName,
            user.Status,
            user.Permissions,
            DateTimeOffset.UtcNow);
    }

    private void SetBusy(bool isBusy, string message)
    {
        _submitButton.Enabled = !isBusy;
        _cancelButton.Enabled = !isBusy;
        _usernameTextBox.Enabled = !isBusy;
        _displayNameTextBox.Enabled = !isBusy;
        _passwordTextBox.Enabled = !isBusy;
        _confirmPasswordTextBox.Enabled = !isBusy;
        _messageLabel.Text = message;
    }
}


using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;

namespace Salryn.App.WinForms.Features.Backup;

// Kirch file guide: operator-safe local database backup screen.
// UI location: opened from Shell > Backup.
// Frontend note: keep this backup-only until destructive recovery safeguards are explicitly scoped.
public sealed class BackupForm : Form
{
    private readonly SalrynAppServices _services;

    private readonly TextBox _databasePathTextBox = new();
    private readonly TextBox _backupFolderTextBox = new();
    private readonly Button _createBackupButton = new();
    private readonly Label _statusLabel = new();

    public BackupForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Text = "SALRYN Backup";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 420);
        Size = new Size(820, 460);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        RenderPaths();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Database Backup",
            "Create a local copy of the current SALRYN SQLite database."), 0, 0);

        root.Controls.Add(BuildReadOnlyField("Live database", _databasePathTextBox), 0, 1);
        root.Controls.Add(BuildReadOnlyField("Backup folder", _backupFolderTextBox), 0, 2);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = J20MvpUi.Colors.AppBackground,
            Padding = new Padding(0, 8, 0, 0)
        };

        J20MvpUi.ConfigurePrimaryButton(_createBackupButton, "Create Backup");
        _createBackupButton.Click += (_, _) => CreateBackup();
        actions.Controls.Add(_createBackupButton);

        root.Controls.Add(actions, 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.TopLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        _statusLabel.Padding = new Padding(4, 8, 4, 4);
        root.Controls.Add(_statusLabel, 0, 4);
    }

    private static Control BuildReadOnlyField(string labelText, TextBox textBox)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        Label label = new()
        {
            Dock = DockStyle.Fill,
            Text = labelText,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = J20MvpUi.Colors.TextStrong,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        textBox.Dock = DockStyle.Fill;
        textBox.ReadOnly = true;
        textBox.BackColor = Color.White;

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(textBox, 0, 1);

        return panel;
    }

    private void RenderPaths()
    {
        _databasePathTextBox.Text = _services.DbPaths.DatabasePath;
        _backupFolderTextBox.Text = _services.DbPaths.BackupPath;
        _statusLabel.Text = "Backup ready. This workflow creates a local database copy only.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private void CreateBackup()
    {
        _createBackupButton.Enabled = false;
        _statusLabel.Text = "Creating database backup...";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;

        try
        {
            var result = _services.CreateLocalDatabaseBackup();

            if (result.IsFailure)
            {
                SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
                return;
            }

            _statusLabel.Text = "Backup created: " + result.Value;
            _statusLabel.ForeColor = J20MvpUi.Colors.Success;
        }
        catch (InvalidOperationException ex)
        {
            SetError("Backup failed: " + ex.Message);
        }
        finally
        {
            _createBackupButton.Enabled = true;
        }
    }

    private void SetError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
    }
}

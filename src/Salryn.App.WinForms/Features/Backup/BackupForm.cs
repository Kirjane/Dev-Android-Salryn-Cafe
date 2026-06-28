using Salryn.App.WinForms.Composition;
using Salryn.App.WinForms.Features.Mvp;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.Backup;

// Kirch file guide: operator-safe local database backup and guarded recovery screen.
// UI location: opened from Shell > Backup.
// Frontend note: keep database verification and restore work delegated to AppServices boundaries.
public sealed class BackupForm : Form
{
    private readonly SalrynAppServices _services;
    private readonly IBackupRecoveryService _backupRecovery;

    private readonly TextBox _databasePathTextBox = new();
    private readonly TextBox _backupFolderTextBox = new();
    private readonly Button _createBackupButton = new();
    private readonly Button _refreshBackupsButton = new();
    private readonly Button _verifyBackupButton = new();
    private readonly Button _restoreBackupButton = new();
    private readonly TextBox _restoreConfirmationTextBox = new();
    private readonly DataGridView _backupsGrid = J20MvpUi.Grid();
    private readonly Label _verificationLabel = new();
    private readonly Label _statusLabel = new();

    private BackupVerificationSummary? _verifiedBackup;

    public BackupForm(SalrynAppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _backupRecovery = _services.CreateBackupRecoveryService();

        Text = "SALRYN Backup";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 680);
        Size = new Size(1040, 740);
        Font = new Font("Segoe UI", 9F);
        BackColor = J20MvpUi.Colors.AppBackground;

        BuildLayout();
        RenderPaths();
        RefreshBackups();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = J20MvpUi.RootLayout(rowCount: 7);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

        Controls.Add(root);

        root.Controls.Add(J20MvpUi.Header(
            "Database Backup Recovery",
            "Create, list, verify, and guarded-restore local SALRYN SQLite backups."), 0, 0);

        root.Controls.Add(BuildReadOnlyField("Live database", _databasePathTextBox), 0, 1);
        root.Controls.Add(BuildReadOnlyField("Backup folder", _backupFolderTextBox), 0, 2);

        root.Controls.Add(BuildActions(), 0, 3);
        root.Controls.Add(BuildBackupsGrid(), 0, 4);
        root.Controls.Add(BuildVerificationPanel(), 0, 5);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        _statusLabel.Padding = new Padding(4, 8, 4, 4);
        root.Controls.Add(_statusLabel, 0, 6);
    }

    private Control BuildActions()
    {
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

        J20MvpUi.ConfigureSecondaryButton(_refreshBackupsButton, "Refresh List");
        _refreshBackupsButton.Click += (_, _) => RefreshBackups();
        actions.Controls.Add(_refreshBackupsButton);

        J20MvpUi.ConfigureSecondaryButton(_verifyBackupButton, "Verify Backup");
        _verifyBackupButton.Click += (_, _) => VerifySelectedBackup();
        actions.Controls.Add(_verifyBackupButton);

        J20MvpUi.ConfigureSecondaryButton(_restoreBackupButton, "Restore Backup");
        _restoreBackupButton.Click += (_, _) => RestoreVerifiedBackup();
        actions.Controls.Add(_restoreBackupButton);

        return actions;
    }

    private Control BuildBackupsGrid()
    {
        _backupsGrid.AutoGenerateColumns = false;
        _backupsGrid.SelectionChanged += (_, _) =>
        {
            _verifiedBackup = null;
            _verificationLabel.Text = "Select a backup and click Verify Backup.";
            _restoreBackupButton.Enabled = false;
        };

        AddTextColumn("FilePath", "Backup File", 520);
        AddTextColumn("SizeBytes", "Size", 110);
        AddTextColumn("LastModifiedAt", "Last Modified", 180);

        return _backupsGrid;
    }

    private Control BuildVerificationPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = J20MvpUi.Colors.AppBackground
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        Label title = new()
        {
            Dock = DockStyle.Fill,
            Text = "Verification Summary / Restore Confirmation",
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = J20MvpUi.Colors.TextStrong,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _verificationLabel.Dock = DockStyle.Fill;
        _verificationLabel.TextAlign = ContentAlignment.TopLeft;
        _verificationLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        _verificationLabel.BorderStyle = BorderStyle.FixedSingle;
        _verificationLabel.Padding = new Padding(8);

        _restoreConfirmationTextBox.Dock = DockStyle.Fill;
        _restoreConfirmationTextBox.PlaceholderText = "Type RESTORE SALRYN BACKUP before Restore Backup";

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_verificationLabel, 0, 1);
        panel.Controls.Add(_restoreConfirmationTextBox, 0, 2);

        return panel;
    }

    private void AddTextColumn(
        string propertyName,
        string headerText,
        int width)
    {
        _backupsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
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
        _verificationLabel.Text = "Select a backup and click Verify Backup.";
        _statusLabel.Text = "Backup recovery ready. Restore requires a verified backup and typed confirmation.";
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
        _restoreBackupButton.Enabled = false;
    }

    private void CreateBackup()
    {
        SetBusy("Creating database backup...");

        try
        {
            var result = _services.CreateLocalDatabaseBackup();

            if (result.IsFailure)
            {
                SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
                return;
            }

            SetSuccess("Backup created: " + result.Value);
            RefreshBackups();
        }
        catch (InvalidOperationException ex)
        {
            SetError("Backup failed: " + ex.Message);
        }
        finally
        {
            SetReady();
        }
    }

    private void RefreshBackups()
    {
        var result = _backupRecovery.ListBackups();

        if (result.IsFailure)
        {
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        _backupsGrid.DataSource = result.Value.ToList();
        _verifiedBackup = null;
        _restoreBackupButton.Enabled = false;
        _verificationLabel.Text = result.Value.Count == 0
            ? "No backup files found. Create a backup first."
            : "Select a backup and click Verify Backup.";
        SetSuccess($"Listed {result.Value.Count} backup file(s).");
    }

    private void VerifySelectedBackup()
    {
        BackupFileSummary? selectedBackup = GetSelectedBackup();

        if (selectedBackup is null)
        {
            SetError("Select a backup file before verification.");
            return;
        }

        var result = _backupRecovery.VerifyBackup(selectedBackup.FilePath);

        if (result.IsFailure)
        {
            _verifiedBackup = null;
            _restoreBackupButton.Enabled = false;
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        _verifiedBackup = result.Value;
        _restoreBackupButton.Enabled = result.Value.IsRestorable;
        _verificationLabel.Text = FormatVerification(result.Value);
        SetSuccess(result.Value.IsRestorable
            ? "Backup verified and eligible for guarded restore."
            : "Backup verified but minimum SALRYN shape is incomplete.");
    }

    private void RestoreVerifiedBackup()
    {
        if (_verifiedBackup is null)
        {
            SetError("Verify a selected backup before restore.");
            return;
        }

        var result = _backupRecovery.RestoreVerifiedBackup(
            _verifiedBackup,
            _restoreConfirmationTextBox.Text);

        if (result.IsFailure)
        {
            SetError($"{result.ErrorDetail.Code}: {result.ErrorDetail.Message}");
            return;
        }

        _restoreConfirmationTextBox.Clear();
        SetSuccess(
            "Restore completed from: " + result.Value.RestoredBackupPath +
            Environment.NewLine +
            "Safety backup created first: " + result.Value.SafetyBackupPath);
    }

    private BackupFileSummary? GetSelectedBackup()
    {
        if (_backupsGrid.CurrentRow?.DataBoundItem is BackupFileSummary backup)
        {
            return backup;
        }

        return null;
    }

    private static string FormatVerification(BackupVerificationSummary summary)
    {
        return string.Join(
            Environment.NewLine,
            "Path: " + summary.FilePath,
            "Size: " + summary.SizeBytes + " bytes",
            "Last modified: " + summary.LastModifiedAt,
            "Core tables exist: " + YesNo(summary.CoreTablesExist),
            "App metadata exists: " + YesNo(summary.AppMetadataExists),
            "Migration metadata exists: " + YesNo(summary.MigrationMetadataExists),
            "User count: " + CountOrUnknown(summary.UserCount),
            "Product count: " + CountOrUnknown(summary.ProductCount),
            "Sale count: " + CountOrUnknown(summary.SaleCount));
    }

    private static string CountOrUnknown(long? value)
    {
        return value.HasValue ? value.Value.ToString() : "Unknown";
    }

    private static string YesNo(bool value)
    {
        return value ? "Yes" : "No";
    }

    private void SetBusy(string message)
    {
        _createBackupButton.Enabled = false;
        _refreshBackupsButton.Enabled = false;
        _verifyBackupButton.Enabled = false;
        _restoreBackupButton.Enabled = false;
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.TextMuted;
    }

    private void SetReady()
    {
        _createBackupButton.Enabled = true;
        _refreshBackupsButton.Enabled = true;
        _verifyBackupButton.Enabled = true;
        _restoreBackupButton.Enabled = _verifiedBackup?.IsRestorable == true;
    }

    private void SetSuccess(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Success;
    }

    private void SetError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = J20MvpUi.Colors.Danger;
        SetReady();
    }
}

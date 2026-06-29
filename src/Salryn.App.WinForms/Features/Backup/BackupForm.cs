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

    // Kirch B7: hover guidance for the backup/restore actions.
    private readonly ToolTip _toolTip = J20MvpUi.CreateToolTip();

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
        BackColor = SalrynUi.Colors.AppBackground;

        BuildLayout();
        RenderPaths();
        RefreshBackups();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(SalrynUi.Spacing.FormPadding),
            BackColor = SalrynUi.Colors.AppBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

        Controls.Add(root);

        root.Controls.Add(SalrynUi.CreatePageHeader(
            "Database Backup Recovery",
            "Create, list, verify, and guarded-restore local SALRYN SQLite backups."), 0, 0);

        root.Controls.Add(BuildReadOnlyField("Live database", _databasePathTextBox), 0, 1);
        root.Controls.Add(BuildReadOnlyField("Backup folder", _backupFolderTextBox), 0, 2);

        root.Controls.Add(BuildActions(), 0, 3);
        root.Controls.Add(BuildBackupsGrid(), 0, 4);
        root.Controls.Add(BuildVerificationPanel(), 0, 5);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        SalrynUi.SetStatusNeutral(_statusLabel, string.Empty);
        SalrynUi.ConfigureFooterStatus(_statusLabel);
        root.Controls.Add(_statusLabel, 0, 6);
    }

    private Control BuildActions()
    {
        // Group the backup actions inside a clear command surface.
        Panel commandBar = SalrynUi.CreateCommandBar();

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = SalrynUi.Colors.Surface
        };

        // Create Backup is the affirmative action (success); restore stays a guarded danger action.
        SalrynUi.ConfigureSuccessButton(_createBackupButton, "Create Backup");
        _toolTip.SetToolTip(_createBackupButton, "Save a fresh copy of the live database into the backup folder.");
        _createBackupButton.Click += (_, _) => CreateBackup();
        actions.Controls.Add(_createBackupButton);

        SalrynUi.ConfigureSecondaryButton(_refreshBackupsButton, "Refresh List");
        _toolTip.SetToolTip(_refreshBackupsButton, "Re-read the backup folder and list available backup files.");
        _refreshBackupsButton.Click += (_, _) => RefreshBackups();
        actions.Controls.Add(_refreshBackupsButton);

        SalrynUi.ConfigureWarningButton(_verifyBackupButton, "Verify Backup");
        _toolTip.SetToolTip(_verifyBackupButton, "Check the selected backup's shape before it can be restored.");
        _verifyBackupButton.Click += (_, _) => VerifySelectedBackup();
        actions.Controls.Add(_verifyBackupButton);

        SalrynUi.ConfigureDangerButton(_restoreBackupButton, "Restore Backup");
        _toolTip.SetToolTip(_restoreBackupButton, "Replace the live database with the verified backup (a safety backup is made first).");
        _restoreBackupButton.Click += (_, _) => RestoreVerifiedBackup();
        actions.Controls.Add(_restoreBackupButton);

        actions.Controls.Add(J20MvpUi.InfoButton(
            "About Backup & Restore",
            "Create Backup copies the live database into the backup folder shown above." + Environment.NewLine +
            Environment.NewLine +
            "Restore is guarded in three ways:" + Environment.NewLine +
            "1. The backup must pass Verify Backup." + Environment.NewLine +
            "2. You must type the exact confirmation phrase." + Environment.NewLine +
            "3. A safety backup of the current database is taken before restoring." + Environment.NewLine +
            Environment.NewLine +
            "Restoring replaces all current data with the backup's data."));

        commandBar.Controls.Add(actions);
        return commandBar;
    }

    private Control BuildBackupsGrid()
    {
        SalrynUi.ApplyLedgerGridStyle(_backupsGrid);
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
            BackColor = SalrynUi.Colors.AppBackground
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        Label title = SalrynUi.CreateSectionTitle("Verification Summary / Restore Confirmation");

        _verificationLabel.Dock = DockStyle.Fill;
        _verificationLabel.TextAlign = ContentAlignment.TopLeft;
        _verificationLabel.ForeColor = SalrynUi.Colors.TextMuted;
        _verificationLabel.BackColor = SalrynUi.Colors.Surface;
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
            BackColor = SalrynUi.Colors.AppBackground
        };

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        Label label = SalrynUi.CreateFieldLabel(labelText);

        textBox.Dock = DockStyle.Fill;
        textBox.ReadOnly = true;
        textBox.BackColor = SalrynUi.Colors.Surface;
        textBox.BorderStyle = BorderStyle.FixedSingle;

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(textBox, 0, 1);

        return panel;
    }

    private void RenderPaths()
    {
        _databasePathTextBox.Text = _services.DbPaths.DatabasePath;
        _backupFolderTextBox.Text = _services.DbPaths.BackupPath;
        _verificationLabel.Text = "Select a backup and click Verify Backup.";
        SalrynUi.SetStatusNeutral(_statusLabel, "Backup recovery ready. Restore requires a verified backup and typed confirmation.");
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

        // Kirch B7: explicit confirmation popup before this irreversible, data-replacing action.
        if (!J20MvpUi.ConfirmDestructiveAction(
                this,
                "Confirm Database Restore",
                "Restore will replace ALL current data with the verified backup:" + Environment.NewLine +
                Environment.NewLine +
                _verifiedBackup.FilePath + Environment.NewLine +
                Environment.NewLine +
                "A safety backup of the current database is taken first. Continue?"))
        {
            SetStatusMessage("Restore cancelled by operator.");
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
        SalrynUi.SetStatusNeutral(_statusLabel, message);
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
        SalrynUi.SetStatusSuccess(_statusLabel, message);
    }

    private void SetStatusMessage(string message)
    {
        SalrynUi.SetStatusNeutral(_statusLabel, message);
        SetReady();
    }

    private void SetError(string message)
    {
        SalrynUi.SetStatusError(_statusLabel, message);
        SetReady();
    }
}

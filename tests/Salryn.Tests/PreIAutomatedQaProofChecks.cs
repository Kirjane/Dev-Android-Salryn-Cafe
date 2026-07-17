using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Auth;
using Salryn.Composition.Local;
using Salryn.Core.Deployment;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// PRE-I automated QA proof: isolated fresh state, lifecycle behavior, and honest source/visibility contracts.
/// Browser observations are intentionally kept out of this gate and reported separately.
/// </summary>
public static class PreIAutomatedQaProofChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string app = Read(root, "src", "Salryn.App.Web", "src", "App.tsx");
        string license = Read(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx");
        string terminalActivation = Read(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx");
        string hostLan = Read(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx");
        string lifecycle = Read(root, "src", "Salryn.App.Web", "src", "utils", "terminalLifecycleUx.ts");
        string backupPage = Read(root, "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx");
        string backupApi = Read(root, "src", "Salryn.App.Web", "src", "api", "backups.ts");
        string recovery = Read(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteBackupRecoveryService.cs");
        string checkout = Read(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx");
        string credits = Read(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx");
        string resetHelper = Read(root, "tools", "dev", "Backup-And-Reset-DevSalrynState.ps1");

        AssertFreshRouteContract(app);
        AssertLicenseLandingContract(license);
        AssertTerminalActivationContract(terminalActivation);
        AssertTerminalLifecycleContract(hostLan, lifecycle);
        AssertBackupRestoreContract(backupPage, backupApi, recovery);
        AssertReplaceLicenseContract(license);
        AssertCheckoutCompletionContract(checkout);
        AssertForbiddenClaimsAbsent(license, terminalActivation, hostLan, backupPage, checkout, credits);
        AssertSafeResetHelper(resetHelper);

        RunFreshStateBehavior();
        RunTerminalLifecycleBehavior();
    }

    private static void AssertFreshRouteContract(string app)
    {
        RequireOrder(app, "if (!isHostLicenseReady", "if (bootstrapStatus?.firstRunRequired)", "Fresh route must prefer License before Bootstrap.");
        RequireOrder(app, "if (bootstrapStatus?.firstRunRequired)", "if (!sessionQuery.data?.isAuthenticated)", "Bootstrap must precede Login after licensing.");
        RequireContains(app, "return <div className=\"min-h-screen", "Unlicensed route renders the fresh License page shell.");
        RequireContains(app, "<LicensePage />", "Unlicensed route renders LicensePage.");
    }

    private static void AssertLicenseLandingContract(string license)
    {
        RequireContains(license, "SALRYN", "Fresh SALRYN wordmark.");
        RequireContains(license, "by Kirjane Labs", "Fresh Kirjane Labs identity.");
        RequireContains(license, "Activate Salryn", "Fresh activation CTA.");
        RequireContains(license, "Import existing license", "Fresh import CTA.");
        RequireContains(license, "salryn.info@gmail.com", "Fresh support email.");
        RequireContains(license, "Copy Host Machine Code", "Fresh machine-code action.");
        RequireNotContains(license, "sr-only", "License page must not carry a hidden source-guard dump.");
        RequireNotContains(license, "find Host License ID", "Fresh user must not be assigned a Host License ID task.");
        RequireNotContains(license, "find Store ID", "Fresh user must not be assigned a Store ID task.");
    }

    private static void AssertTerminalActivationContract(string terminalActivation)
    {
        RequireNotContains(terminalActivation, "sr-only", "Terminal Activation must not carry hidden test copy.");
        RequireContains(terminalActivation, "This Terminal Machine Code", "Visible Terminal Machine Code guidance.");
        RequireContains(terminalActivation, "Copy Terminal Machine Code", "Visible Terminal Machine Code copy action.");
        RequireContains(terminalActivation, "do not need a Host License ID or Store ID", "Terminal user is not assigned Host/Store ID work.");
        RequireContains(terminalActivation, "Import Signed Terminal License JSON", "Terminal-only signed import title.");
        RequireContains(terminalActivation, "Host licenses cannot activate a terminal", "Host license refusal remains clear.");
        RequireContains(terminalActivation, "salryn.info@gmail.com", "Terminal support email.");
        RequireNotContains(terminalActivation, "payload", "No payload jargon in normal Terminal Activation copy.");
        RequireNotContains(terminalActivation, "schema", "No schema jargon in normal Terminal Activation copy.");
        RequireNotContains(terminalActivation, "wrapper", "No wrapper jargon in normal Terminal Activation copy.");
        RequireNotContains(terminalActivation, " raw ", "No raw-data jargon in normal Terminal Activation copy.");
    }

    private static void AssertTerminalLifecycleContract(string hostLan, string lifecycle)
    {
        RequireContains(lifecycle, "return status === \"Active\" && isManagedRemoteLanTerminal(role);", "Disable is Active-only.");
        RequireContains(lifecycle, "return status === \"Disabled\" && isManagedRemoteLanTerminal(role);", "Enable/Remove are Disabled-only.");
        RequireContains(hostLan, "const showDisable = canDisableTerminal", "Active row uses Disable visibility gate.");
        RequireContains(hostLan, "const showEnable = canEnableDisabledTerminal", "Disabled row uses Enable visibility gate.");
        RequireContains(hostLan, "const showRemove = canRemoveTerminal", "Disabled row uses Remove visibility gate.");
        RequireContains(hostLan, "Disable terminal", "Disable operator label.");
        RequireContains(hostLan, "Enable terminal", "Enable operator label.");
        RequireContains(hostLan, "Remove terminal", "Remove operator label.");
        RequireContains(hostLan, "variant=\"ghost\"", "Clear sessions remains a secondary action.");
        RequireContains(hostLan, "pendingDisableTerminal", "Disable confirmation state.");
        RequireContains(hostLan, "pendingEnableTerminal", "Enable confirmation state.");
        RequireContains(hostLan, "pendingRemoveTerminal", "Remove confirmation state.");
        RequireContains(hostLan, "pendingClearSessionsTerminal", "Clear sessions confirmation state.");
        RequireContains(hostLan, "Clear operator sessions?", "Clear sessions ConfirmDialog title.");
        RequireContains(hostLan, "revokeTerminalMutation.mutate", "Remove keeps revoke semantics internally.");
        RequireNotContains(hostLan, "Restore (not supported)", "No false Restore action.");
        RequireNotContains(hostLan, ">Revoke<", "No primary Revoke label.");
    }

    private static void AssertBackupRestoreContract(string backupPage, string backupApi, string recovery)
    {
        RequireContains(backupPage, "Type RESTORE SALRYN BACKUP to enable restore", "Visible full restore phrase.");
        RequireContains(backupPage, "restoreBackup(restoreTarget, restoreText)", "UI sends the entered phrase unchanged.");
        RequireContains(backupPage, "Backup format version", "Soft user-facing format label.");
        RequireNotContains(backupPage, "Schema / app migration", "No schema/migration label.");
        RequireContains(backupApi, "RESTORE_CONFIRMATION_INPUT = \"RESTORE SALRYN BACKUP\"", "Web API contract phrase.");
        RequireContains(backupApi, "confirmation", "Web API request includes confirmation.");
        RequireContains(recovery, "RestoreConfirmationText = \"RESTORE SALRYN BACKUP\"", "Backend confirmation phrase.");
    }

    private static void AssertReplaceLicenseContract(string license)
    {
        RequireContains(license, "canInstallWizard = licenseText.trim().length > 0 && termsAccepted && !busy", "First activation requires terms plus document.");
        RequireContains(license, "canInstallReplace = licenseText.trim().length > 0 && replaceConfirmed && termsAccepted && !busy", "Replacement requires text plus both re-acknowledgements.");
        RequireContains(license, "step === \"import\" ? \"terms\" : step", "Direct import cannot skip terms.");
        RequireContains(license, "I have re-read and accept the Salryn Activation Terms for this replacement.", "Replacement terms re-ack copy.");
        RequireContains(license, "I understand this replaces the active local license for this device.", "Replacement consequence re-ack copy.");
        RequireContains(license, "disabled={!canInstallReplace}", "Replacement install button uses the full gate.");
    }

    private static void AssertCheckoutCompletionContract(string checkout)
    {
        RequireOrder(checkout, "Cash sale committed", "Support details", "Receipt success must appear before support details.");
        RequireOrder(checkout, "Support details", "Sale ID:", "Sale ID must remain inside/after collapsed support details.");
        RequireContains(checkout, "Keep this receipt number for your store records.", "Receipt reference is primary cashier copy.");
        RequireNotContains(checkout, "debug", "Normal Checkout page must not use debug wording.");
    }

    private static void AssertForbiddenClaimsAbsent(params string[] clientSources)
    {
        string joined = string.Join("\n", clientSources);
        string[] forbidden =
        {
            "cloud activation",
            "BIR-ready",
            "official receipt accreditation",
            "ESC/POS supported",
            "cash drawer supported",
            "drawer kick",
            "cutter supported",
            "hardware bundle",
            "Products of Kirjane Labs",
            "mock sample",
            "Build Type",
            ">Publisher<"
        };

        foreach (string claim in forbidden)
        {
            RequireNotContains(joined, claim, "Forbidden client-facing claim regression.");
        }
    }

    private static void AssertSafeResetHelper(string helper)
    {
        // Compat wrapper: must reset BOTH .salryn-dev and LocalAppData for --dev QA (not LocalAppData-only).
        RequireContains(helper, "[switch]$WhatIf", "Fresh-state helper supports dry run.");
        RequireContains(helper, "BOTH", "Fresh-state helper must state BOTH .salryn-dev and LocalAppData.");
        RequireContains(helper, ".salryn-dev", "Fresh-state helper must include project --dev root.");
        RequireContains(helper, "LocalAppData", "Fresh-state helper must include LocalAppData.");
        RequireContains(helper, "Backup-And-Reset-AllDevState.ps1", "Fresh-state helper delegates to AllDevState.");
        RequireNotContains(helper, "Remove-Item", "Fresh-state helper must not delete local state.");
    }

    private static void RunFreshStateBehavior()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pre-i-auto-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

            using (var connection = new SqliteConnection("Data Source=" + paths.DatabasePath))
            {
                connection.Open();
                RequireTrue(ScalarLong(connection, "SELECT COUNT(*) FROM users") == 0, "Fresh DB must have zero users.");
                RequireTrue(ScalarLong(connection, "SELECT COUNT(*) FROM cashier_sessions") == 0, "Fresh DB must have zero cashier sessions.");
                RequireTrue(ScalarLong(connection, "SELECT COUNT(*) FROM deployment_identity") == 1, "Fresh DB must contain only its generated deployment identity baseline.");
                string storeName = ScalarString(connection, "SELECT store_name FROM store_settings LIMIT 1");
                RequireNotContains(storeName, "DEVKIRCH", "Fresh store state must not contain DEVKIRCH residue.");
                RequireNotContains(storeName, "Salryn Dev", "Fresh store state must not contain seeded dev-store residue.");
            }

            string licenseRoot = Path.Combine(tempRoot, "license-state");
            LocalLicenseService licenseService = new(
                new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(licenseRoot, null)),
                new LocalLicenseValidator());
            LocalLicenseValidationResult status = licenseService.GetStatusAsync().GetAwaiter().GetResult();
            RequireTrue(status.Status == LicenseStatus.Unlicensed, "Fresh isolated license root must be Unlicensed.");
            RequireTrue(status.LicenseType is null, "Fresh isolated license root must have no Host or Terminal license type.");
            RequireTrue(!File.Exists(Path.Combine(licenseRoot, LocalLicenseFileStore.DefaultFileName)), "Fresh isolated root must not contain a license file.");

            InMemoryLocalApiSessionStore sessionStore = new();
            LocalApiSessionService sessions = new(composition.Authentication, sessionStore);
            RequireTrue(!sessions.GetSession(null).IsAuthenticated, "Fresh API session must be unauthenticated.");
            RequireTrue(sessionStore.Get(Guid.NewGuid().ToString("N")) is null, "Fresh session store must contain no active session.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
    }

    private static void RunTerminalLifecycleBehavior()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-pre-i-auto-terminal-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

            SqliteTerminalRegistryRepository repository = new(composition.ConnectionFactory);
            DeploymentIdentityService deploymentIdentity = new(new SqliteDeploymentIdentityRepository(composition.ConnectionFactory));
            TerminalRegistryService registry = new(
                repository,
                deploymentIdentity,
                new SalrynStoreHostAuthorityGuard(() => deploymentIdentity.CurrentSnapshot));

            Guid actorId = Guid.NewGuid();
            TerminalRegistryActorContext actor = new(
                actorId,
                new[] { SalrynPermissions.StoreTerminalView, SalrynPermissions.StoreTerminalManage });
            Guid terminalId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SalrynTerminalRegistration active = new(
                terminalId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Automated QA Counter",
                TerminalRegistrationRole.CashierClient,
                TerminalRegistrationStatus.Active,
                now,
                now,
                null,
                null,
                actorId,
                "PRE-I isolated lifecycle proof");

            RequireTrue(repository.UpsertAsync(active).GetAwaiter().GetResult().IsSuccess, "Seed active terminal in isolated DB.");

            var disabled = registry.DisableTerminalAsync(terminalId, actor).GetAwaiter().GetResult();
            RequireTrue(disabled.IsSuccess && disabled.Value.Status == TerminalRegistrationStatus.Disabled, "Active terminal must disable.");
            RequireTrue(disabled.Value.DisabledAtUtc is not null, "Disabled terminal records disabled timestamp.");

            var enabled = registry.EnableTerminalAsync(terminalId, actor).GetAwaiter().GetResult();
            RequireTrue(enabled.IsSuccess && enabled.Value.Status == TerminalRegistrationStatus.Active, "Disabled terminal must enable.");
            RequireTrue(enabled.Value.DisabledAtUtc is null, "Enable clears disabled timestamp.");

            var enableActive = registry.EnableTerminalAsync(terminalId, actor).GetAwaiter().GetResult();
            RequireTrue(enableActive.IsFailure && enableActive.ErrorDetail.Code == "TerminalRegistry.EnableRequiresDisabled", "Active terminal cannot be enabled again.");

            RequireTrue(registry.DisableTerminalAsync(terminalId, actor).GetAwaiter().GetResult().IsSuccess, "Terminal disables before removal.");
            var removed = registry.RevokeTerminalAsync(terminalId, actor).GetAwaiter().GetResult();
            RequireTrue(removed.IsSuccess && removed.Value.Status == TerminalRegistrationStatus.Revoked, "Disabled terminal can be removed through revoke semantics.");
            RequireTrue(removed.Value.RevokedAtUtc is not null, "Removed terminal records revoked timestamp.");

            var enableRemoved = registry.EnableTerminalAsync(terminalId, actor).GetAwaiter().GetResult();
            RequireTrue(enableRemoved.IsFailure && enableRemoved.ErrorDetail.Code == "TerminalRegistry.CannotEnableRevoked", "Removed terminal cannot be enabled.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarString(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }

    private static void RequireOrder(string text, string first, string second, string message)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        if (a < 0 || b < 0 || a >= b)
            throw new InvalidOperationException(message + " Order failed: " + first + " then " + second);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

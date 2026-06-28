namespace Salryn.Tests;

public static class PosStabB4UsersAdminChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");
        string presenterText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Users",
            "UsersAdminPresenter.cs");
        string formText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Users",
            "UsersAdminForm.cs");
        string appBridgeText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "UsersAdminAppServices.cs");
        string readContractsText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Repositories",
            "ReadRepositoryInterfaces.cs");
        string writeContractsText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Repositories",
            "WriteRepositoryInterfaces.cs");
        string readModelsText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "ReadModels",
            "SecurityReadModels.cs");
        string repositoryFactoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "SqliteRepositoryFactory.cs");
        string sqliteReadRepositoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "System",
            "SqliteUserReadRepository.cs");
        string sqliteWriteRepositoryText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "System",
            "SqliteUserWriteRepository.cs");
        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        string usersRouteText = SliceBetween(
            shellText,
            "if (module.Key == ShellModuleKeys.Users)",
            "if (module.Key == ShellModuleKeys.Backup)");

        RequireContains(usersRouteText, "OpenUsers();", "B4 Shell Users route must open the real Users form.");
        RequireContains(shellText, "using Salryn.App.WinForms.Features.Users;", "B4 Shell must reference the Users feature namespace.");
        RequireContains(shellText, "private void OpenUsers()", "B4 Shell must own a Users route method.");
        RequireContains(shellText, "new UsersAdminForm(", "B4 Shell must construct UsersAdminForm.");
        RequireContains(shellText, "new UsersAdminPresenter(", "B4 Shell must route Users through a presenter boundary.");
        RequireContains(shellText, "_services.UserReadRepository", "B4 Users route must use AppServices user read repository.");
        RequireContains(shellText, "_services.CreateUserWriteRepository()", "B4 Users route must use AppServices users write bridge.");
        RequireDoesNotContain(usersRouteText, "ShowPlaceholder", "B4 Users route must not fall through the placeholder branch.");

        RequireContains(formText, "public sealed class UsersAdminForm : Form", "B4 Users form must exist.");
        RequireContains(formText, "UsersAdminPresenter", "B4 Users form must use a presenter boundary.");
        RequireContains(formText, "_usersGrid", "B4 Users form must list user rows.");
        RequireContains(formText, "CreateUserAsync", "B4 Users form must expose create workflow.");
        RequireContains(formText, "SaveUserAsync", "B4 Users form must expose update workflow.");
        RequireContains(formText, "ResetPasswordAsync", "B4 Users form must expose password reset workflow.");
        RequireContains(formText, "DeactivateUserAsync", "B4 Users form must expose deactivate workflow.");
        RequireContains(formText, "ReactivateUserAsync", "B4 Users form must expose reactivate workflow.");
        RequireContains(formText, "PromptDeactivateReason", "B4 Users deactivation must require an admin reason prompt.");
        RequireContains(formText, "A reason is required", "B4 Users form must surface deactivate reason requirement.");

        RequireContains(presenterText, "_userReads.GetAllAsync", "B4 Users presenter must list users through the existing user read path.");
        RequireContains(presenterText, "_userReads.GetRolesAsync", "B4 Users presenter must load roles from the user read boundary.");
        RequireContains(presenterText, "SalrynPasswordHasher.HashPassword", "B4 Users password writes must use the existing password hashing pattern.");
        RequireContains(presenterText, "ValidatePassword", "B4 Users presenter must validate password and confirmation.");
        RequireContains(presenterText, "Users.PasswordMismatch", "B4 Users presenter must reject mismatched password confirmation.");
        RequireContains(presenterText, "userId == _session.UserId", "B4 Users presenter must block current user self-deactivation.");
        RequireContains(presenterText, "Users.SelfDeactivateBlocked", "B4 Users presenter must surface self-deactivation guard.");
        RequireContains(presenterText, "Users.DeactivateReasonRequired", "B4 Users presenter must require deactivation reason.");
        RequireContains(presenterText, "_userWrites.CreateAsync", "B4 Users create must go through the write boundary.");
        RequireContains(presenterText, "_userWrites.UpdateAsync", "B4 Users update must go through the write boundary.");
        RequireContains(presenterText, "_userWrites.ResetPasswordAsync", "B4 Users password reset must go through the write boundary.");
        RequireContains(presenterText, "_userWrites.SetStatusAsync", "B4 Users status change must go through the write boundary.");

        RequireContains(readModelsText, "UserRoleReadModel", "B4 Core must define role options for Users Admin.");
        RequireContains(readContractsText, "GetRolesAsync", "B4 User read boundary must expose role loading.");
        RequireContains(writeContractsText, "UserAdminCreateRequest", "B4 Core must define typed user create request.");
        RequireContains(writeContractsText, "UserAdminUpdateRequest", "B4 Core must define typed user update request.");
        RequireContains(writeContractsText, "UserAdminPasswordResetRequest", "B4 Core must define typed password reset request.");
        RequireContains(writeContractsText, "UserAdminStatusChangeRequest", "B4 Core must define typed user status request.");
        RequireContains(writeContractsText, "IUserWriteRepository", "B4 Core must define users write repository boundary.");

        RequireContains(sqliteReadRepositoryText, "GetRolesAsync", "B4 SQLite user read repository must implement role loading.");
        RequireContains(sqliteWriteRepositoryText, "SqliteUserWriteRepository", "B4 Infrastructure must implement users write repository.");
        RequireContains(sqliteWriteRepositoryText, "IUserWriteRepository", "B4 SQLite users repository must implement the Core boundary.");
        RequireContains(sqliteWriteRepositoryText, "RoleExists", "B4 Users writer must validate existing active roles.");
        RequireContains(sqliteWriteRepositoryText, "UsernameExists", "B4 Users writer must block duplicate usernames.");
        RequireContains(sqliteWriteRepositoryText, "request.UserId == request.ChangedByUserId", "B4 Users writer must keep backend self-deactivation guard.");
        RequireContains(sqliteWriteRepositoryText, "Users.SelfDeactivateBlocked", "B4 Users writer must surface self-deactivation guard.");
        RequireContains(sqliteWriteRepositoryText, "Users.DeactivateReasonRequired", "B4 Users writer must require deactivation reason.");
        RequireContains(repositoryFactoryText, "CreateUserWriteRepository", "B4 repository factory must expose users writer creation.");
        RequireContains(appBridgeText, "CreateUserWriteRepository", "B4 AppServices users bridge must expose writer creation.");

        RequireNoDirectSql(formText, "Users form");
        RequireNoDirectSql(presenterText, "Users presenter");
        RequireNoForbiddenMarkers(shellText, "Shell");
        RequireNoForbiddenMarkers(formText, "Users form");
        RequireNoForbiddenMarkers(presenterText, "Users presenter");
        RequireNoForbiddenMarkers(sqliteWriteRepositoryText, "Users writer");

        RequireContains(
            coreSmokeText,
            "PosStabB4UsersAdminChecks.Run();",
            "CoreSmoke must run POS-STAB-B4 users admin checks.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(
        DirectoryInfo repoRoot,
        params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static string SliceBetween(
        string text,
        string startMarker,
        string endMarker)
    {
        int start = text.IndexOf(startMarker, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"Required marker was not found: {startMarker}");
        }

        int end = text.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            throw new InvalidOperationException($"Required marker was not found: {endMarker}");
        }

        return text[start..end];
    }

    private static void RequireNoDirectSql(string text, string surface)
    {
        RequireDoesNotContain(text, "Microsoft.Data.Sqlite", $"B4 {surface} must not reference SQLite directly.");
        RequireDoesNotContain(text, "SqliteConnection", $"B4 {surface} must not create SQLite connections.");
        RequireDoesNotContain(text, "INSERT ", $"B4 {surface} must not issue direct insert SQL.");
        RequireDoesNotContain(text, "UPDATE ", $"B4 {surface} must not issue direct update SQL.");
        RequireDoesNotContain(text, "DELETE ", $"B4 {surface} must not issue direct delete SQL.");
    }

    private static void RequireNoForbiddenMarkers(string text, string surface)
    {
        foreach (string marker in ForbiddenMarkers())
        {
            RequireDoesNotContain(text, marker, $"B4 {surface} must not introduce forbidden marker: {marker}");
        }
    }

    private static string[] ForbiddenMarkers()
    {
        return new[]
        {
            "R" + "MO",
            "P" + "DF",
            "Cry" + "stal",
            "." + "rpt",
            "ESC" + "/" + "POS",
            "raw " + "USB",
            "scan" + "ner",
            "hard" + "ware"
        };
    }

    private static void RequireContains(
        string text,
        string value,
        string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(
        string text,
        string value,
        string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

namespace Salryn.Tests;

public static class Phase66G0AuthenticatedRoleIdPropagationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string authModelsText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Security", "AuthenticationModels.cs");
        string authServiceText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "AuthenticationService.cs");
        string sqliteAuthText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteAuthenticationRepository.cs");
        string authStartupText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Auth", "AuthStartupForm.cs");
        string currentSessionText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "CurrentAppSession.cs");
        string sessionProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostSessionIdentityProvider.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string lifecycleText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66G0_AUTHENTICATED_ROLE_ID_PROPAGATION.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(authModelsText, "public sealed record AuthenticatedUserReadModel", "AuthenticatedUserReadModel must remain the authentication result model.");
        RequireContains(authModelsText, "public Guid RoleId { get; init; } = Guid.Empty;", "AuthenticatedUserReadModel must carry RoleId.");

        RequireContains(authServiceText, "RoleId = profile.RoleId", "Login must propagate RoleId from UserPermissionReadModel.");
        RequireContains(authServiceText, "UserPermissionReadModel? profile = permissionResult.Value;", "Login must still load the permission profile.");
        RequireNotContains(authServiceText, "RoleId = Guid.Empty", "Login must not force empty RoleId.");

        RequireContains(sqliteAuthText, "RoleId = Guid.Parse(DefaultSeedData.AdminRoleId)", "First-run admin must propagate Admin RoleId.");
        RequireContains(sqliteAuthText, "DefaultSeedData.AdminRoleId", "First-run admin must use seeded Admin role identity.");
        RequireNotContains(sqliteAuthText, "RoleId = Guid.Empty", "First-run admin must not force empty RoleId.");

        RequireContains(authStartupText, "user.RoleId", "AuthStartupForm must pass authenticated RoleId into CurrentAppSession.");
        RequireContains(authStartupText, "new CurrentAppSession(", "AuthStartupForm must still create CurrentAppSession.");

        RequireContains(currentSessionText, "public Guid RoleId { get; }", "CurrentAppSession must expose RoleId.");
        RequireContains(currentSessionText, "RoleId = roleId;", "CurrentAppSession must store RoleId.");
        RequireContains(sessionProviderText, "session.RoleId == Guid.Empty", "Session identity provider must still reject empty RoleId.");
        RequireContains(adapterText, "roleId: roleId", "Adapter must still pass real RoleId into request context provider.");

        RequireNotContains(authModelsText, ".Start(", "Auth models must not start runtime.");
        RequireNotContains(authServiceText, ".Start(", "Auth service must not start runtime.");
        RequireNotContains(sqliteAuthText, ".Start(", "Sqlite auth repository must not start runtime.");
        RequireNotContains(authStartupText, ".Start(", "Auth startup form must not start runtime.");
        RequireNotContains(lifecycleText, ".Start(", "Lifecycle holder must remain passive in 66G0.");
        RequireNotContains(programText, "Runtime.Start", "Program must not start runtime in 66G0.");
        RequireNotContains(programText, ".Start(", "Program must not start runtime in 66G0.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime in 66G0.");

        RequireContains(handoffText, "Status: Authenticated RoleId propagation only.", "66G0 handoff must mark RoleId-only scope.");
        RequireContains(handoffText, "No runtime activation was added.", "66G0 handoff must block runtime activation.");
        RequireContains(handoffText, "AuthStartupForm now passes RoleId into CurrentAppSession.", "66G0 handoff must document session fix.");
        RequireContains(handoffText, "The Local Host session identity provider can now receive non-empty RoleId from normal auth flow.", "66G0 handoff must document Local Host impact.");

        RequireContains(coreSmokeText, "Phase66DSessionIdentityProviderAdapterWiringChecks.Run();", "CoreSmoke must still run 66D.");
        RequireContains(coreSmokeText, "Phase66G0AuthenticatedRoleIdPropagationChecks.Run();", "CoreSmoke must run 66G0.");

        int index66D = coreSmokeText.IndexOf("Phase66DSessionIdentityProviderAdapterWiringChecks.Run();", StringComparison.Ordinal);
        int index66G0 = coreSmokeText.IndexOf("Phase66G0AuthenticatedRoleIdPropagationChecks.Run();", StringComparison.Ordinal);

        if (index66D < 0 || index66G0 < 0 || index66G0 < index66D)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66G0 must run after Phase 66D.");
        }
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

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
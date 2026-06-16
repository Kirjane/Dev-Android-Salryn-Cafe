namespace Salryn.Tests;

public static class Phase65ZIdentityBoundaryWiringChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string securityReadModelsText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "ReadModels", "SecurityReadModels.cs");
        string permissionRepositoryText = ReadRequiredFile(repoRoot, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteUserPermissionReadRepository.cs");
        string currentAppSessionText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "CurrentAppSession.cs");
        string bootstrapperText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "AppSessionBootstrapper.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string providerText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_65Z_IDENTITY_BOUNDARY_WIRING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(securityReadModelsText, "Guid RoleId,", "UserPermissionReadModel must expose real RoleId.");
        RequireContains(securityReadModelsText, "Guid.Empty", "Read model compatibility constructor must not invent a real role identity.");

        RequireContains(permissionRepositoryText, "u.role_id,", "Permission repository must select role_id from users.");
        RequireContains(permissionRepositoryText, "Guid roleId = Guid.Empty;", "Permission repository must initialize role_id.");
        RequireContains(permissionRepositoryText, "roleId = Guid.Parse(reader.GetString(1));", "Permission repository must read role_id.");
        RequireContains(permissionRepositoryText, "roleId,", "Permission repository must pass RoleId into UserPermissionReadModel.");

        RequireContains(currentAppSessionText, "Guid roleId,", "CurrentAppSession must accept RoleId.");
        RequireContains(currentAppSessionText, "RoleId = roleId;", "CurrentAppSession must store RoleId.");
        RequireContains(currentAppSessionText, "public Guid RoleId { get; }", "CurrentAppSession must expose RoleId.");
        RequireContains(currentAppSessionText, "Guid.Empty", "Legacy session constructor must remain explicit missing identity compatibility only.");

        RequireContains(bootstrapperText, "profile.RoleId,", "AppSessionBootstrapper must carry RoleId from permission profile into CurrentAppSession.");

        RequireContains(appServicesText, "new TerminalIdentity(", "Standalone profile must now include local terminal identity.");
        RequireContains(appServicesText, "Guid.Parse(\"44444444-4444-4444-4444-444444444444\")", "Standalone terminal identity must be deterministic.");
        RequireContains(appServicesText, "\"standalone-local-terminal\"", "Standalone terminal device key must be deterministic.");
        RequireContains(appServicesText, "Environment.MachineName", "Standalone terminal must bind to local machine name.");
        RequireContains(appServicesText, "SalrynMachineRole.StandaloneHost", "Standalone terminal must remain local standalone host role.");
        RequireNotContains(appServicesText, "Terminal: null", "Standalone profile must no longer have null terminal identity.");

        RequireContains(providerText, "public bool HasTerminalIdentity =>", "Request context provider must still expose terminal readiness.");
        RequireContains(providerText, "public bool CanCreateAuthenticatedContext(Guid? userId, Guid? roleId)", "Request context provider must still require supplied user and role id.");
        RequireContains(providerText, "TerminalId: terminal.TerminalId", "Request context provider must use terminal id from profile.");
        RequireContains(providerText, "DeviceKey: terminal.DeviceKey", "Request context provider must use device key from profile.");
        RequireNotContains(providerText, "CurrentAppSession", "Provider must not directly depend on UI session boundary.");
        RequireNotContains(providerText, "CheckoutSessionContext", "Provider must not directly depend on checkout boundary.");

        RequireNotContains(adapterText, "SalrynLocalHostRequestContextProvider", "65Z must not make adapter consume provider yet.");
        RequireContains(adapterText, "CreateStoppedRuntimeBridgeContext()", "Adapter placeholder context must remain isolated until explicit consumption phase.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");

        foreach (string marker in new[]
        {
            "SalrynLocalHostRequestContextProvider",
            "SalrynLocalHostRuntimeLifecycleHolder",
            "HostRequestContext",
            "TerminalIdentity",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop(",
            "DispatchAsync"
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of marker: {marker}");
            RequireNotContains(shellText, marker, $"Shell must remain free of marker: {marker}");
        }

        RequireContains(handoffText, "Status: Identity boundary wiring only.", "65Z handoff must mark identity-only status.");
        RequireContains(handoffText, "No runtime activation was added.", "65Z handoff must block runtime activation.");
        RequireContains(handoffText, "No adapter consumption of the request context provider was added.", "65Z handoff must block adapter consumption.");
        RequireContains(handoffText, "No Program or Shell behavior was changed.", "65Z handoff must protect Program and Shell.");

        RequireContains(coreSmokeText, "Phase65XPassiveLocalHostProvidersAppServicesWiringChecks.Run();", "CoreSmoke must still run 65X.");
        RequireContains(coreSmokeText, "Phase65ZIdentityBoundaryWiringChecks.Run();", "CoreSmoke must run 65Z.");

        int index65X = coreSmokeText.IndexOf("Phase65XPassiveLocalHostProvidersAppServicesWiringChecks.Run();", StringComparison.Ordinal);
        int index65Z = coreSmokeText.IndexOf("Phase65ZIdentityBoundaryWiringChecks.Run();", StringComparison.Ordinal);

        if (index65X < 0 || index65Z < 0 || index65Z < index65X)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 65Z must run after Phase 65X.");
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
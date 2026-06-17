namespace Salryn.Tests;

public static class Phase66DSessionIdentityProviderAdapterWiringChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string sessionIdentityProviderText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostSessionIdentityProvider.cs");
        string adapterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "CompositionLocalHostProductLookupGatewayAdapter.cs");
        string appServicesText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynAppServices.cs");
        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string programText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Program.cs");
        string providerText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRequestContextProvider.cs");
        string lifecycleText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Composition", "SalrynLocalHostRuntimeLifecycleHolder.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "PHASE_66D_SESSION_IDENTITY_PROVIDER_ADAPTER_WIRING.md");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(sessionIdentityProviderText, "public sealed class SalrynLocalHostSessionIdentityProvider", "Session identity provider must exist.");
        RequireContains(sessionIdentityProviderText, "CurrentAppSession?", "Session identity provider must observe CurrentAppSession.");
        RequireContains(sessionIdentityProviderText, "public void SetSession(CurrentAppSession? session)", "Session identity provider must accept shell session passively.");
        RequireContains(sessionIdentityProviderText, "public bool TryGetAuthenticatedIdentity(out Guid userId, out Guid roleId)", "Session identity provider must expose identity retrieval.");
        RequireContains(sessionIdentityProviderText, "session.UserId == Guid.Empty", "Session identity provider must reject empty user id.");
        RequireContains(sessionIdentityProviderText, "session.RoleId == Guid.Empty", "Session identity provider must reject empty role id.");
        RequireContains(sessionIdentityProviderText, "!session.IsActive", "Session identity provider must reject inactive session.");
        RequireNotContains(sessionIdentityProviderText, "DispatchAsync", "Session identity provider must not dispatch.");
        RequireNotContains(sessionIdentityProviderText, ".Start(", "Session identity provider must not start runtime.");
        RequireNotContains(sessionIdentityProviderText, ".Stop(", "Session identity provider must not stop runtime.");
        RequireNotContains(sessionIdentityProviderText, "Microsoft.Data.Sqlite", "Session identity provider must not access SQLite.");

        RequireContains(adapterText, "private readonly SalrynLocalHostSessionIdentityProvider? sessionIdentityProvider;", "Adapter must hold session identity provider.");
        RequireContains(adapterText, "SalrynLocalHostSessionIdentityProvider sessionIdentityProvider", "Adapter must expose session-provider constructor.");
        RequireContains(adapterText, "this.sessionIdentityProvider = sessionIdentityProvider;", "Adapter must store session identity provider.");
        RequireContains(adapterText, "sessionProvider.TryGetAuthenticatedIdentity(", "Adapter must read session identity through provider.");
        RequireContains(adapterText, "out Guid userId", "Adapter must receive real user identity from session provider.");
        RequireContains(adapterText, "out Guid roleId", "Adapter must receive real role identity from session provider.");
        RequireContains(adapterText, "userId: userId", "Adapter must pass real user identity to request context provider.");
        RequireContains(adapterText, "roleId: roleId", "Adapter must pass real role identity to request context provider.");
        RequireContains(adapterText, "cashierSessionId: null", "Adapter must still not invent cashier session identity.");
        RequireContains(adapterText, "Context: context", "Adapter must keep provider-created HostRequestContext.");
        RequireContains(adapterText, "authenticated app session identity is not available yet", "Adapter must fail closed when session identity is missing.");
        RequireContains(adapterText, "holder.Composition.Runtime.DispatchAsync<LocalHostProductLookupRequest, LocalHostProductLookupResponse>", "Adapter dispatch bridge remains but runtime start is still absent.");

        RequireNotContains(adapterText, "userId: null", "Adapter must no longer pass null user identity.");
        RequireNotContains(adapterText, "roleId: null", "Adapter must no longer pass null role identity.");
        RequireNotContains(adapterText, "new HostRequestContext(", "Adapter must not construct HostRequestContext directly.");
        RequireNotContains(adapterText, "CreateStoppedRuntimeBridgeContext", "Adapter placeholder context must remain removed.");
        RequireNotContains(adapterText, ".Start(", "Adapter must not start runtime.");
        RequireNotContains(adapterText, ".Stop(", "Adapter must not stop runtime.");
        RequireNotContains(adapterText, "HttpClient", "Adapter must not add HTTP transport.");
        RequireNotContains(adapterText, "Socket", "Adapter must not add socket transport.");
        RequireNotContains(adapterText, "Tcp", "Adapter must not add TCP transport.");
        RequireNotContains(adapterText, "Udp", "Adapter must not add UDP transport.");

        RequireContains(appServicesText, "public SalrynLocalHostSessionIdentityProvider LocalHostSessionIdentityProvider { get; }", "AppServices must expose passive session identity provider.");
        RequireContains(appServicesText, "LocalHostSessionIdentityProvider =", "AppServices must create passive session identity provider.");
        RequireContains(appServicesText, "new SalrynLocalHostSessionIdentityProvider();", "AppServices must construct passive session identity provider.");
        RequireContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider, LocalHostSessionIdentityProvider);", "AppServices must pass session provider into adapter.");
        RequireNotContains(appServicesText, "new CompositionLocalHostProductLookupGatewayAdapter(LocalHostProductLookupCompositionHolder, LocalHostRequestContextProvider);", "AppServices must not use old provider-only adapter wiring.");
        RequireNotContains(appServicesText, ".Start(", "AppServices must not start runtime.");
        RequireNotContains(appServicesText, ".Stop(", "AppServices must not stop runtime.");
        RequireNotContains(appServicesText, "DispatchAsync", "AppServices must not dispatch.");

        RequireContains(shellText, "_services.LocalHostSessionIdentityProvider.SetSession(session);", "Shell must publish current session to passive identity provider.");
        RequireNotContains(shellText, "Runtime.Start", "Shell must not start runtime.");
        RequireNotContains(shellText, "Runtime.Stop", "Shell must not stop runtime.");
        RequireNotContains(shellText, "DispatchAsync", "Shell must not dispatch.");
        RequireNotContains(shellText, "HttpClient", "Shell must not add HTTP transport.");
        RequireNotContains(shellText, "Socket", "Shell must not add socket transport.");
        RequireNotContains(shellText, "Tcp", "Shell must not add TCP transport.");
        RequireNotContains(shellText, "Udp", "Shell must not add UDP transport.");

        foreach (string marker in new[]
        {
            "SalrynLocalHostSessionIdentityProvider",
            "SalrynLocalHostRequestContextProvider",
            "SalrynLocalHostRuntimeLifecycleHolder",
            "HostRequestContext",
            "TerminalIdentity",
            "LocalHostProductLookup",
            "Runtime.Start",
            "Runtime.Stop",
            ".Start(",
            ".Stop(",
            "DispatchAsync"
        })
        {
            RequireNotContains(programText, marker, $"Program must remain free of marker: {marker}");
        }

        RequireContains(providerText, "context = new HostRequestContext(", "Request context provider must remain HostRequestContext owner.");
        RequireContains(providerText, "UserId: userId.GetValueOrDefault()", "Request context provider must map supplied user id.");
        RequireContains(providerText, "RoleId: roleId.GetValueOrDefault()", "Request context provider must map supplied role id.");
        RequireNotContains(providerText, "CurrentAppSession", "Request context provider must not directly depend on shell session.");

        RequireContains(lifecycleText, "GetSnapshot()", "Lifecycle holder must remain passive.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStart(DateTimeOffset startedAt)", "Lifecycle holder must expose controlled runtime start after 66G1.");
        RequireContains(lifecycleText, "public SalrynLocalHostRuntimeLifecycleResult TryStop()", "Lifecycle holder must expose controlled runtime stop after 66G1.");
        RequireNotContains(lifecycleText, "DispatchAsync", "Lifecycle holder must not dispatch.");

        RequireContains(handoffText, "Status: Session identity provider wiring only.", "66D handoff must mark session identity provider wiring only.");
        RequireContains(handoffText, "No runtime activation was added.", "66D handoff must block runtime activation.");
        RequireContains(handoffText, "No Program behavior was changed.", "66D handoff must protect Program.");
        RequireContains(handoffText, "Shell only publishes the existing CurrentAppSession into the passive identity provider.", "66D handoff must document shell-only passive publish.");
        RequireContains(handoffText, "The adapter now fails closed when no active session identity is available.", "66D handoff must document fail-closed session behavior.");

        RequireContains(coreSmokeText, "Phase66BAdapterRequestContextProviderConsumptionChecks.Run();", "CoreSmoke must still run 66B.");
        RequireContains(coreSmokeText, "Phase66DSessionIdentityProviderAdapterWiringChecks.Run();", "CoreSmoke must run 66D.");

        int index66B = coreSmokeText.IndexOf("Phase66BAdapterRequestContextProviderConsumptionChecks.Run();", StringComparison.Ordinal);
        int index66D = coreSmokeText.IndexOf("Phase66DSessionIdentityProviderAdapterWiringChecks.Run();", StringComparison.Ordinal);

        if (index66B < 0 || index66D < 0 || index66D < index66B)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. Phase 66D must run after Phase 66B.");
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
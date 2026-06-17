namespace Salryn.Tests;

// Kirch: Seals the app Local Host ProductLookup composition path before any app runtime consumer is added.
public static class Phase65DAppLocalHostCompositionReadinessSealChecks
{
    // Kirch: Proves the app composition path is guarded, readiness-aligned, and still inactive in Program/Shell.
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string appProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Salryn.App.WinForms.csproj");

        string appServicesText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Composition",
            "SalrynAppServices.cs");

        string programText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Program.cs");

        string shellText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.App.WinForms",
            "Features",
            "Shell",
            "SalrynShellForm.cs");

        string hostProjectText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Salryn.Host.Local.csproj");

        string hostComposerText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Composition",
            "LocalHostBootstrapCompositionModels.cs");

        string readinessText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Host.Local",
            "Readiness",
            "LocalHostOperationSurfaceReadinessModels.cs");

        string phase65BText = ReadRequiredFile(
            repoRoot,
            "tests",
            "Salryn.Tests",
            "Phase65BAppLocalHostCompositionReferenceSliceChecks.cs");

        string phase65CText = ReadRequiredFile(
            repoRoot,
            "tests",
            "Salryn.Tests",
            "Phase65CAppLocalHostCompositionCreationGuardChecks.cs");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Core\\Salryn.Core.csproj\" />",
            "App.WinForms must keep the Core reference.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Infrastructure\\Salryn.Infrastructure.csproj\" />",
            "App.WinForms must keep Infrastructure reference for composition root wiring.");

        RequireContains(
            appProjectText,
            "<ProjectReference Include=\"..\\Salryn.Host.Local\\Salryn.Host.Local.csproj\" />",
            "App.WinForms must keep the controlled Host.Local reference from 65B.");

        RequireContains(
            hostProjectText,
            "..\\Salryn.Core\\Salryn.Core.csproj",
            "Host.Local must still reference Core.");

        RequireNotContains(
            hostProjectText,
            "Salryn.Infrastructure",
            "Host.Local must not reference Infrastructure.");

        RequireNotContains(
            hostProjectText,
            "Salryn.App.WinForms",
            "Host.Local must not reference App.WinForms.");

        RequireContains(
            appServicesText,
            "public LocalHostBootstrapCompositionResult CreateLocalHostProductLookupComposition(StoreDeploymentProfile profile)",
            "App services must expose the guarded Local Host ProductLookup composition method.");

        RequireContains(
            appServicesText,
            "ArgumentNullException.ThrowIfNull(profile);",
            "App services must guard the deployment profile.");

        RequireContains(
            appServicesText,
            "LocalHostBootstrapCompositionResult result =",
            "App services must capture the composition result.");

        RequireContains(
            appServicesText,
            "CreateUncheckedLocalHostProductLookupComposition(profile);",
            "App services must isolate raw composition behind the guarded method.");

        RequireContains(
            appServicesText,
            "if (!LocalHostBootstrapCompositionRules.IsCompositionValid(result))",
            "App services must validate the composition result.");

        RequireContains(
            appServicesText,
            "throw new InvalidOperationException(\"Invalid app Local Host ProductLookup composition.\");",
            "App services must fail closed for invalid composition.");

        RequireContains(
            appServicesText,
            "return result;",
            "App services must only return the validated composition result.");

        RequireContains(
            appServicesText,
            "private LocalHostBootstrapCompositionResult CreateUncheckedLocalHostProductLookupComposition(StoreDeploymentProfile profile)",
            "Raw Local Host composition must stay private.");

        RequireContains(
            appServicesText,
            "LocalHostBootstrapComposer composer = new();",
            "Raw Local Host composer creation must stay inside the app composition root.");

        RequireContains(
            appServicesText,
            "return composer.ComposeWithProductLookup(",
            "App composition must use the explicit ProductLookup Host.Local path.");

        RequireContains(
            appServicesText,
            "ProductReadRepository);",
            "App composition must pass the existing Core product repository contract.");

        RequireContains(
            hostComposerText,
            "public static bool IsCompositionValid(LocalHostBootstrapCompositionResult? result)",
            "Host.Local composition validity rule must exist.");

        RequireContains(
            hostComposerText,
            "result.Snapshot.State == LocalHostRuntimeState.Stopped",
            "Host.Local composition validity must require stopped runtime state.");

        RequireContains(
            hostComposerText,
            "result.Snapshot.RegisteredContractCount == result.Snapshot.RegisteredOperationCount",
            "Host.Local composition validity must require operation/contract alignment.");

        RequireContains(
            hostComposerText,
            "result.Snapshot.RegisteredHandlerCount == result.Snapshot.RegisteredOperationCount",
            "Host.Local composition validity must require operation/handler alignment.");

        RequireContains(
            readinessText,
            "public sealed class LocalHostOperationSurfaceReadinessInspector",
            "Host.Local readiness inspector must exist.");

        RequireContains(
            readinessText,
            "LocalHostOperationSurfaceReadinessFailureReason.RuntimeNotStopped",
            "Readiness inspector must reject running runtime state.");

        RequireContains(
            readinessText,
            "Registered operation count does not match the Host operation catalog.",
            "Readiness inspector must check operation count alignment.");

        RequireContains(
            readinessText,
            "Registered contract count does not match the Host operation catalog.",
            "Readiness inspector must check contract count alignment.");

        RequireContains(
            readinessText,
            "Registered handler count does not match the Host operation catalog.",
            "Readiness inspector must check handler count alignment.");

        RequireContains(
            phase65BText,
            "Phase65BAppLocalHostCompositionReferenceSliceChecks",
            "65B reference slice check must remain present.");

        RequireContains(
            phase65CText,
            "Phase65CAppLocalHostCompositionCreationGuardChecks",
            "65C creation guard check must remain present.");

        RequireNoRuntimeStartOrDispatch(appServicesText, "SalrynAppServices");
        RequireNoRuntimeStartOrDispatch(programText, "Program");
        RequireNoRuntimeStartOrDispatch(shellText, "Shell");

        RequireNotContains(
            programText,
            "CreateLocalHostProductLookupComposition",
            "65D must not call Local Host composition from Program.");

        RequireNotContains(
            shellText,
            "CreateLocalHostProductLookupComposition",
            "65D must not call Local Host composition from Shell.");

        RequireNotContains(
            shellText,
            "LocalHostBootstrapCompositionResult",
            "65D must not pass Local Host composition results into Shell.");

        RequireNotContains(
            shellText,
            "ProductLookup",
            "65D must not add ProductLookup UI flow.");

        AssertNoLocalHostOutsideComposition(repoRoot);
        AssertNoInfrastructureOutsideComposition(repoRoot);
    }

    // Kirch: Finds the repository root for text-based readiness seal checks.
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

    // Kirch: Reads a required repository file.
    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    // Kirch: Requires a source marker to exist.
    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Requires a source marker to be absent.
    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    // Kirch: Keeps 65D readiness-only by rejecting runtime activation, dispatch, and transport markers.
    private static void RequireNoRuntimeStartOrDispatch(string text, string owner)
    {
        string[] forbiddenPatterns =
        {
            ".Start(",
            "Runtime.Start",
            "DispatchAsync<",
            "TcpClient",
            "TcpListener",
            "HttpClient",
            "UdpClient",
            "System.Net.Sockets"
        };

        foreach (string forbiddenPattern in forbiddenPatterns)
        {
            if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{owner} must not start runtime, dispatch, or add transport behavior in 65D. Found {forbiddenPattern}");
            }
        }
    }

    // Kirch: Ensures Host.Local wiring remains inside App.WinForms Composition only.
    private static void AssertNoLocalHostOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(
            repoRoot.FullName,
            "src",
            "Salryn.App.WinForms");

        string compositionRoot = Path.Combine(
            appRoot,
            "Composition");

        string[] forbiddenPatterns =
        {
            "Salryn.Host.Local",
            "LocalHostBootstrapComposer",
            "LocalHostBootstrapCompositionResult",
            "ComposeWithProductLookup",
            "CreateLocalHostProductLookupComposition",
            "CreateUncheckedLocalHostProductLookupComposition"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(compositionRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string forbiddenPattern in forbiddenPatterns)
            {
                if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Local Host wiring leaked outside App.WinForms composition root: {file} contains {forbiddenPattern}");
                }
            }
        }
    }

    // Kirch: Ensures Infrastructure wiring still stays inside the WinForms composition root.
    private static void AssertNoInfrastructureOutsideComposition(DirectoryInfo repoRoot)
    {
        string appRoot = Path.Combine(
            repoRoot.FullName,
            "src",
            "Salryn.App.WinForms");

        string compositionRoot = Path.Combine(
            appRoot,
            "Composition");

        string[] forbiddenPatterns =
        {
            "Salryn.Infrastructure",
            "Microsoft.Data.Sqlite",
            "SqliteConnectionFactory",
            "SqliteRepositoryFactory",
            "DefaultDatabaseInitializationFactory"
        };

        foreach (string file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            string normalizedLifecycleScanPath = file.Replace('\\', '/');

            if (normalizedLifecycleScanPath.EndsWith("/Composition/SalrynLocalHostRuntimeLifecycleHolder.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(compositionRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            foreach (string forbiddenPattern in forbiddenPatterns)
            {
                if (text.Contains(forbiddenPattern, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Infrastructure wiring leaked outside App.WinForms composition root: {file} contains {forbiddenPattern}");
                }
            }
        }
    }
}

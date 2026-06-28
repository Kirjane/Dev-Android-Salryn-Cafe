using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Infrastructure.Paths;

namespace Salryn.App.WinForms;

// Kirch file guide: app launch mode resolver.
// UI location: runs before any form opens and decides which database path AppServices should use.
// Frontend note: do not put visual styling here; this only separates production and development launch paths.

internal enum SalrynAppLaunchMode
{
    Production = 0,
    Development = 1
}

internal sealed record SalrynAppLaunchPlan(
    SalrynAppLaunchMode Mode,
    string? ProjectRoot);

internal static class SalrynAppLaunchModeResolver
{
    public const string DevelopmentArgument = "--dev";
    public const string DevelopmentEnvironmentVariable = "SALRYN_APP_ENV";
    public const string ProjectRootEnvironmentVariable = "SALRYN_PROJECT_ROOT";

    public static SalrynAppServices CreateServices(IReadOnlyList<string>? args)
    {
        SalrynAppLaunchPlan plan = Resolve(args);

        if (plan.Mode == SalrynAppLaunchMode.Production)
        {
            Result writabilityResult = DbPaths.CreateProduction().ValidateProductionWritability();

            if (writabilityResult.IsFailure)
            {
                throw new InvalidOperationException(
                    writabilityResult.ErrorDetail.Code + ": " + writabilityResult.ErrorDetail.Message);
            }
        }

        return plan.Mode == SalrynAppLaunchMode.Development
            ? SalrynAppServices.CreateDevelopment(plan.ProjectRoot!)
            : SalrynAppServices.CreateProduction();
    }

    public static SalrynAppLaunchPlan Resolve(IReadOnlyList<string>? args)
    {
        if (!IsDevelopmentRequested(args))
        {
            return new SalrynAppLaunchPlan(
                SalrynAppLaunchMode.Production,
                ProjectRoot: null);
        }

        return new SalrynAppLaunchPlan(
            SalrynAppLaunchMode.Development,
            ResolveProjectRoot());
    }

    private static bool IsDevelopmentRequested(IReadOnlyList<string>? args)
    {
        if (args is not null &&
            args.Any(item => string.Equals(item, DevelopmentArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string? appEnvironment =
            Environment.GetEnvironmentVariable(DevelopmentEnvironmentVariable);

        return string.Equals(appEnvironment, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(appEnvironment, "Dev", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProjectRoot()
    {
        string? configuredRoot =
            Environment.GetEnvironmentVariable(ProjectRootEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string fullConfiguredRoot = Path.GetFullPath(configuredRoot);

            if (File.Exists(Path.Combine(fullConfiguredRoot, "Salryn.sln")))
            {
                return fullConfiguredRoot;
            }

            throw new InvalidOperationException(
                $"Configured SALRYN_PROJECT_ROOT does not contain Salryn.sln: {fullConfiguredRoot}");
        }

        string? currentDirectoryRoot = TryFindRepositoryRoot(Directory.GetCurrentDirectory());

        if (currentDirectoryRoot is not null)
        {
            return currentDirectoryRoot;
        }

        string? baseDirectoryRoot = TryFindRepositoryRoot(AppContext.BaseDirectory);

        if (baseDirectoryRoot is not null)
        {
            return baseDirectoryRoot;
        }

        throw new InvalidOperationException(
            "Could not locate Salryn repository root for development launch. Set SALRYN_PROJECT_ROOT.");
    }

    private static string? TryFindRepositoryRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
using Salryn.Api.Local.Composition;

namespace Salryn.Api.Local;

internal enum SalrynApiLaunchMode
{
    Production = 0,
    Development = 1
}

internal sealed record SalrynApiLaunchPlan(
    SalrynApiLaunchMode Mode,
    string? ProjectRoot);

internal static class ApiLaunchModeResolver
{
    public const string DevelopmentArgument = "--dev";
    public const string SmokeArgument = "--smoke";
    public const string SmokeIfSupportedArgument = "--smoke-if-supported";
    public const string DevelopmentEnvironmentVariable = "SALRYN_APP_ENV";
    public const string ProjectRootEnvironmentVariable = "SALRYN_PROJECT_ROOT";

    public static SalrynApiComposition CreateComposition(IReadOnlyList<string>? args)
    {
        SalrynApiLaunchPlan plan = Resolve(args);

        return plan.Mode == SalrynApiLaunchMode.Development
            ? SalrynApiComposition.CreateDevelopment(plan.ProjectRoot!)
            : SalrynApiComposition.CreateProduction();
    }

    public static SalrynApiLaunchPlan Resolve(IReadOnlyList<string>? args)
    {
        if (!IsDevelopmentRequested(args))
        {
            return new SalrynApiLaunchPlan(
                SalrynApiLaunchMode.Production,
                ProjectRoot: null);
        }

        return new SalrynApiLaunchPlan(
            SalrynApiLaunchMode.Development,
            ResolveProjectRoot());
    }

    public static bool IsSmokeRequested(IReadOnlyList<string>? args)
    {
        if (args is null)
        {
            return false;
        }

        return args.Any(item =>
            string.Equals(item, SmokeArgument, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item, SmokeIfSupportedArgument, StringComparison.OrdinalIgnoreCase));
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

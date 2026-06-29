namespace Salryn.Composition.Local;

public enum SalrynLocalLaunchMode
{
    Production = 0,
    Development = 1
}

public sealed record SalrynLocalLaunchPlan(
    SalrynLocalLaunchMode Mode,
    string? ProjectRoot);

public static class SalrynLocalLaunchModeResolver
{
    public const string DevelopmentArgument = "--dev";
    public const string DevelopmentEnvironmentVariable = "SALRYN_APP_ENV";
    public const string ProjectRootEnvironmentVariable = "SALRYN_PROJECT_ROOT";

    public static SalrynLocalComposition CreateComposition(IReadOnlyList<string>? args)
    {
        SalrynLocalLaunchPlan plan = Resolve(args);

        return plan.Mode == SalrynLocalLaunchMode.Development
            ? SalrynLocalComposition.CreateDevelopment(plan.ProjectRoot!)
            : SalrynLocalComposition.CreateProduction();
    }

    public static SalrynLocalLaunchPlan Resolve(IReadOnlyList<string>? args)
    {
        if (!IsDevelopmentRequested(args))
        {
            return new SalrynLocalLaunchPlan(
                SalrynLocalLaunchMode.Production,
                ProjectRoot: null);
        }

        return new SalrynLocalLaunchPlan(
            SalrynLocalLaunchMode.Development,
            ResolveProjectRoot());
    }

    public static bool IsDevelopmentRequested(IReadOnlyList<string>? args)
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

    public static string ResolveProjectRoot()
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

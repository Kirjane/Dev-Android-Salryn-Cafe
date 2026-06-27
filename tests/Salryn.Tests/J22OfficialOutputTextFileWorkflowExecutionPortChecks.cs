using System.Reflection;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.Tests;

public static class J22OfficialOutputTextFileWorkflowExecutionPortChecks
{
    public static void Run()
    {
        AssertCorePortContract();
        AssertInfrastructureRunnerImplementsPort();
        AssertAppGatewayDependsOnPort();
        AssertHostLocalDependencyDirection();
        AssertNoLocalHostExecutionHandler();
        AssertSourceGuards();
    }

    private static void AssertCorePortContract()
    {
        Type portType = typeof(IOfficialOutputTextFileWorkflowExecutionPort);

        if (!portType.IsInterface || !portType.Name.Contains("TextFile", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("10C-A must add an explicit text-file Core workflow execution port.");
        }

        MethodInfo? runAsync = portType.GetMethod(nameof(IOfficialOutputTextFileWorkflowExecutionPort.RunAsync));

        if (runAsync is null)
        {
            throw new InvalidOperationException("10C-A Core port must expose RunAsync.");
        }

        if (runAsync.ReturnType != typeof(Task<Result<OfficialExternalOutputWorkflowResult>>))
        {
            throw new InvalidOperationException("10C-A Core port must return Result<OfficialExternalOutputWorkflowResult> asynchronously.");
        }

        ParameterInfo[] parameters = runAsync.GetParameters();

        if (parameters.Length != 2
            || parameters[0].ParameterType != typeof(OfficialExternalOutputWorkflowRequest)
            || parameters[1].ParameterType != typeof(CancellationToken)
            || !parameters[1].HasDefaultValue)
        {
            throw new InvalidOperationException("10C-A Core port must accept OfficialExternalOutputWorkflowRequest and optional CancellationToken.");
        }
    }

    private static void AssertInfrastructureRunnerImplementsPort()
    {
        if (!typeof(IOfficialOutputTextFileWorkflowExecutionPort).IsAssignableFrom(typeof(OfficialExternalOutputTextFileWorkflowRunner)))
        {
            throw new InvalidOperationException("10C-A Infrastructure runner must implement the Core official output text-file workflow execution port.");
        }
    }

    private static void AssertAppGatewayDependsOnPort()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string adapter = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");

        RequireContains(
            adapter,
            "private readonly IOfficialOutputTextFileWorkflowExecutionPort _workflowRunner;",
            "10C-A app gateway must store the Core official output text-file workflow execution port.");
        RequireContains(
            adapter,
            "IOfficialOutputTextFileWorkflowExecutionPort? workflowRunner = null",
            "10C-A app gateway constructor must accept the Core official output text-file workflow execution port.");
        RequireContains(
            adapter,
            "new OfficialExternalOutputTextFileWorkflowRunner()",
            "10C-A app gateway may still default to the Infrastructure runner from the app composition layer.");
        RequireDoesNotContain(
            adapter,
            "private readonly OfficialExternalOutputTextFileWorkflowRunner _workflowRunner;",
            "10C-A app gateway must not store only the concrete Infrastructure runner.");
    }

    private static void AssertHostLocalDependencyDirection()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string hostProject = Read(root, "src", "Salryn.Host.Local", "Salryn.Host.Local.csproj");

        RequireContains(
            hostProject,
            "..\\Salryn.Core\\Salryn.Core.csproj",
            "Host.Local must still reference Core.");
        RequireDoesNotContain(
            hostProject,
            "Salryn.Infrastructure",
            "10C-A must not add a Host.Local reference to Infrastructure.");
        RequireDoesNotContain(
            hostProject,
            "Salryn.App.WinForms",
            "10C-A must not add a Host.Local reference to App.WinForms.");
    }

    private static void AssertNoLocalHostExecutionHandler()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string hostRoot = Path.Combine(root.FullName, "src", "Salryn.Host.Local");
        string bootstrap = Read(root, "src", "Salryn.Host.Local", "Bootstrap", "LocalHostHandlerBootstrapModels.cs");

        RequireContains(
            bootstrap,
            "LocalHostBootstrapNotImplementedHandler<LocalHostOfficialOutputTextFileWorkflowRequest, LocalHostOfficialOutputTextFileWorkflowResponse>",
            "10C-A must leave Local Host official output text-file workflow execution fail-closed through the placeholder.");

        if (Directory.EnumerateFiles(hostRoot, "*.cs", SearchOption.AllDirectories)
            .Any(static file => Path.GetFileName(file).Contains("LocalHostOfficialOutputTextFileWorkflowHandler", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("10C-A must not add the real Local Host official output text-file workflow handler yet.");
        }

        foreach (string file in Directory.EnumerateFiles(hostRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            RequireDoesNotContain(
                text,
                "OfficialExternalOutputTextFileWorkflowRunner",
                "Host.Local must not call the Infrastructure official output text-file workflow runner.");
            RequireDoesNotContain(
                text,
                "SalrynAppServices",
                "Host.Local must not call App.WinForms services.");
            RequireDoesNotContain(
                text,
                "CompositionOfficialOutputTextFileWorkflowGatewayAdapter",
                "Host.Local must not call the App.WinForms official output text-file gateway.");
        }
    }

    private static void AssertSourceGuards()
    {
        DirectoryInfo root = FindRepositoryRoot();
        string port = Read(root, "src", "Salryn.Core", "Services", "OfficialOutputTextFileWorkflowExecutionPort.cs");
        string runner = Read(root, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialExternalOutputTextFileWorkflowRunner.cs");
        string adapter = Read(root, "src", "Salryn.App.WinForms", "Composition", "CompositionOfficialOutputTextFileWorkflowGatewayAdapter.cs");

        RequireContains(
            port,
            "public interface IOfficialOutputTextFileWorkflowExecutionPort",
            "10C-A Core port source must exist.");
        RequireContains(
            port,
            "Task<Result<OfficialExternalOutputWorkflowResult>> RunAsync",
            "10C-A Core port must expose the existing workflow result shape.");
        RequireContains(
            port,
            "OfficialExternalOutputWorkflowRequest request",
            "10C-A Core port must use the existing workflow request shape.");
        RequireContains(
            runner,
            "OfficialExternalOutputTextFileWorkflowRunner : IOfficialOutputTextFileWorkflowExecutionPort",
            "10C-A Infrastructure runner source must implement the port.");

        foreach (string text in new[] { port, runner, adapter })
        {
            foreach (string forbidden in ForbiddenMarkers())
            {
                RequireDoesNotContain(
                    text,
                    forbidden,
                    "10C-A changed official output text-file workflow files must not contain forbidden marker: " + forbidden);
            }
        }
    }

    private static IEnumerable<string> ForbiddenMarkers()
    {
        yield return "Crystal" + "Decisions";
        yield return "." + "rpt";
        yield return "System.Windows." + "Forms";
        yield return "Microsoft.Data." + "Sqlite";
        yield return "Sql" + "ite";
        yield return "SQL" + "ite";
        yield return "ESC" + "/" + "POS";
        yield return "Print" + "Document";
        yield return "Printer" + "Settings";
        yield return "System.Drawing." + "Printing";
        yield return "Pdf" + "Document";
        yield return "Export" + "ToDisk";
        yield return "Complete" + "Sale";
        yield return "Official" + "Checkout";
        yield return "Electronic" + "Journal" + "Append";
        yield return "Official" + "ZReading" + "Service";
        yield return "Finalize" + "Async";
        yield return "Save" + "Async";
        yield return "IN" + "SERT";
        yield return "UP" + "DATE";
        yield return "DE" + "LETE";
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;
        foreach (string part in parts) path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}

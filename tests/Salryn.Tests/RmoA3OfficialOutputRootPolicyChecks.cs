using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.OfficialOutput;

namespace Salryn.Tests;

public static class RmoA3OfficialOutputRootPolicyChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();
        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string policySource = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileOutputPathPolicy.cs");
        string adapterSource = Read(repoRoot, "src", "Salryn.Infrastructure", "OfficialOutput", "OfficialTextFileExternalOutputAdapter.cs");
        string navigation = Read(repoRoot, "src", "Salryn.App.Web", "src", "navigation.ts");
        string readOnlyEndpoints = Read(repoRoot, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs");

        RequireContains(coreSmoke, "RmoA3OfficialOutputRootPolicyChecks.Run();", "CoreSmoke must run RMO-A3 output-root policy checks.");
        RequireContains(policySource, "ValidateWritableOutputRoot", "Output-root policy must expose writability validation.");
        RequireContains(policySource, "OutputRootMustExist", "Output-root policy must reject missing roots.");
        RequireContains(policySource, "OutputRootSystemDirectoryRejected", "Output-root policy must reject system directories.");
        RequireContains(policySource, "OutputRootDriveRootRejected", "Output-root policy must reject drive roots.");
        RequireContains(policySource, ProbeFilePrefix, "Output-root policy must use a controlled writability probe.");
        RequireContains(adapterSource, "ValidateWritableOutputRoot", "Adapter must validate writable output root before writing.");
        RequireContains(adapterSource, "MapWriteException", "Adapter must map write exceptions to controlled validation errors.");
        RequireNotContains(adapterSource, "reprint", "Adapter must not add repeat-run/reprint policy.");
        RequireNotContains(policySource, "reprint", "Output-root policy must not add repeat-run/reprint policy.");
        RequireNotContains(navigation, "Official Output Text File", "Unsupported official-output work must not appear in store navigation.");
        RequireNotContains(navigation, "enabled: false", "Store navigation must not render disabled developer-only surfaces.");
        RequireNotContains(readOnlyEndpoints, "official-output", "Read-only API must not expose official-output endpoints.");

        OfficialTextFileOutputPathPolicy policy = new();
        string validRoot = CreateTempDirectory();

        try
        {
            RequireFailure(
                policy.ResolveOutputRoot(" "),
                "OfficialTextFileOutput.OutputRootRequired",
                "Blank output root must be rejected.");

            string? driveRoot = Path.GetPathRoot(validRoot);

            if (!string.IsNullOrWhiteSpace(driveRoot))
            {
                RequireFailure(
                    policy.ResolveOutputRoot(driveRoot),
                    "OfficialTextFileOutput.OutputRootDriveRootRejected",
                    "Drive root must be rejected.");
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            if (!string.IsNullOrWhiteSpace(windowsDirectory) && Directory.Exists(windowsDirectory))
            {
                RequireFailure(
                    policy.ResolveOutputRoot(windowsDirectory),
                    "OfficialTextFileOutput.OutputRootSystemDirectoryRejected",
                    "Windows directory must be rejected.");
            }

            string programFilesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrWhiteSpace(programFilesDirectory) && Directory.Exists(programFilesDirectory))
            {
                RequireFailure(
                    policy.ResolveOutputRoot(programFilesDirectory),
                    "OfficialTextFileOutput.OutputRootSystemDirectoryRejected",
                    "Program Files directory must be rejected.");
            }

            string fileRoot = Path.Combine(validRoot, "file-root.txt");
            File.WriteAllText(fileRoot, "root-file");

            RequireFailure(
                policy.ResolveOutputRoot(fileRoot),
                "OfficialTextFileOutput.OutputRootMustBeDirectory",
                "File path root must be rejected.");

            string missingRoot = Path.Combine(
                Path.GetTempPath(),
                "salryn-missing-root-" + Guid.NewGuid().ToString("N"));

            RequireFailure(
                policy.ResolveOutputRoot(missingRoot),
                "OfficialTextFileOutput.OutputRootMustExist",
                "Missing output root must be rejected.");

            Result<string> traversal = policy.ResolveChild(validRoot, "../escape.txt");
            Require(traversal.IsFailure, "Traversal path must be blocked.");
            Require(
                traversal.ErrorDetail.Code is "OfficialTextFileOutput.PathTraversalRejected"
                    or "OfficialTextFileOutput.RelativePathInvalid",
                "Traversal path must map to a controlled validation error.");

            RequireFailure(
                policy.ResolveOfficialOutputDirectory(validRoot, ".."),
                "OfficialTextFileOutput.PackageHashInvalid",
                "Package hash traversal must be blocked.");

            Result<string> writable = policy.ValidateWritableOutputRoot(validRoot);
            RequireSuccess(writable, "Valid temp directory root must succeed.");

            string expectedLayout = Path.Combine("official-output", "package-hash");
            Result<string> layout = policy.ResolveOfficialOutputDirectory(validRoot, "package-hash");
            RequireSuccess(layout, "Official output directory layout must resolve.");
            RequireContains(layout.Value, expectedLayout.Replace('/', Path.DirectorySeparatorChar), "Artifact layout must remain official-output/{packageHash}.");

            string[] probeFiles = Directory.GetFiles(validRoot, ProbeFilePrefix + "*", SearchOption.TopDirectoryOnly);
            Require(probeFiles.Length == 0, "Writability probe files must be cleaned up.");

            string applicationRoot = AppContext.BaseDirectory;

            if (!string.IsNullOrWhiteSpace(applicationRoot) && Directory.Exists(applicationRoot))
            {
                RequireFailure(
                    policy.ResolveOutputRoot(applicationRoot),
                    "OfficialTextFileOutput.OutputRootSystemDirectoryRejected",
                    "Application directory must be rejected.");
            }

            OfficialTextFileExternalOutputAdapter adapter = new();
            OfficialOutputExportPackage package = J21Rmo242023OfficialOutputPipelineFixture.CreatePackage();
            OfficialOutputDeliveryLedgerPackage deliveryLedger =
                J21Rmo242023OfficialOutputPipelineFixture.CreateDeliveryLedger(
                    J21Rmo242023OfficialOutputPipelineFixture.CreateDelivery(package));
            OfficialExternalOutputDispatchPlan plan =
                J21Rmo242023OfficialOutputPipelineFixture.CreateFileDispatchPlan(package, deliveryLedger);
            OfficialExternalOutputExecutionRequest request = new(
                plan,
                package,
                deliveryLedger,
                validRoot,
                J21Rmo242023OfficialOutputPipelineFixture.Timestamp,
                J21Rmo242023OfficialOutputPipelineFixture.UserId,
                "POS-RMO-A3",
                null);

            Result<OfficialExternalOutputExecutionResult> execution = adapter.ExecuteAsync(request).GetAwaiter().GetResult();
            RequireSuccess(execution, "Adapter must still write artifacts under a valid root.");

            foreach (OfficialExternalOutputWrittenArtifact artifact in execution.Value.WrittenArtifacts)
            {
                RequireContains(artifact.RelativePath, "official-output/", "Artifact paths must remain under official-output/{packageHash}.");
            }
        }
        finally
        {
            TryDeleteDirectory(validRoot);
        }
    }

    private const string ProbeFilePrefix = ".salryn-write-probe-";

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "salryn-rmo-a3-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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

    private static string Read(DirectoryInfo root, params string[] parts)
    {
        string path = root.FullName;

        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
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
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireFailure(Result<string> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(
                message + $" Expected {expectedCode}, got {(result.IsSuccess ? "success" : result.ErrorDetail.Code)}.");
        }
    }

    private static void RequireSuccess(Result<string> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + $" Error: {result.ErrorDetail.Code}");
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(message + $" Error: {result.ErrorDetail.Code}");
        }
    }
}

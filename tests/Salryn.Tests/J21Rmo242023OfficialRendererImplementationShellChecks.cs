using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialRendererImplementationShellChecks
{
    private static readonly Guid RequestedByUserId = Guid.Parse("25666666-6666-6666-6666-666666666661");
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string rendererText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialNoOutputReportRenderer.cs");
        string coreProjectText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Salryn.Core.csproj");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(rendererText, "OfficialNoOutputReportRenderer : IOfficialReportRenderer", "09F must add a Core renderer implementation shell.");
        RequireContains(rendererText, "OfficialReportRenderRequest request", "09F renderer must consume the official render request.");
        RequireContains(rendererText, "OfficialReportBindingDocument sourceDocument", "09F renderer must consume the official binding document DTO.");
        RequireContains(rendererText, "OfficialReportRendererContractValidator.ValidateRequest(request)", "09F renderer must validate requests through the official contract validator.");
        RequireContains(rendererText, "OfficialReportRendererContractValidator.NormalizeRequest(request)", "09F renderer must normalize requests through the official contract validator.");
        RequireContains(rendererText, "OfficialReportRendererContractValidator.ValidateContract(RendererContract)", "09F renderer must validate its renderer contract.");
        RequireContains(rendererText, "Task.FromResult", "09F renderer shell must remain in-process and deterministic.");
        RequireContains(rendererText, "ArtifactReference: null", "09F renderer shell must not return a file or print artifact reference.");
        RequireContains(rendererText, "CreatedAt: normalizedRequest.RequestedAt", "09F renderer shell must not use the system clock.");

        RequireDoesNotContain(rendererText, "Crystal", "09F renderer shell must not reference Crystal Reports.");
        RequireDoesNotContain(rendererText, ".rpt", "09F renderer shell must not reference report templates.");
        RequireDoesNotContain(rendererText, "ReportDocument", "09F renderer shell must not create report documents.");
        RequireDoesNotContain(rendererText, "PdfDocument", "09F renderer shell must not create PDF documents.");
        RequireDoesNotContain(rendererText, "ExportToDisk", "09F renderer shell must not export files.");
        RequireDoesNotContain(rendererText, "File.Write", "09F renderer shell must not write files.");
        RequireDoesNotContain(rendererText, "FileStream", "09F renderer shell must not open file streams.");
        RequireDoesNotContain(rendererText, "Process.Start", "09F renderer shell must not launch external renderers.");
        RequireDoesNotContain(rendererText, "PrintDocument", "09F renderer shell must not create print documents.");
        RequireDoesNotContain(rendererText, "System.Drawing.Printing", "09F renderer shell must not reference printing APIs.");
        RequireDoesNotContain(rendererText, "System.Windows.Forms", "09F renderer shell must not reference UI APIs.");
        RequireDoesNotContain(rendererText, "Salryn.Infrastructure", "09F renderer shell must not reference Infrastructure.");
        RequireDoesNotContain(rendererText, "Microsoft.Data.Sqlite", "09F renderer shell must not reference SQLite packages.");
        RequireDoesNotContain(rendererText, "Sqlite", "09F renderer shell must not mention SQLite.");
        RequireDoesNotContain(rendererText, "EscPos", "09F renderer shell must not add ESC/POS output.");
        RequireDoesNotContain(rendererText, "ESC/POS", "09F renderer shell must not add ESC/POS output.");

        RequireDoesNotContain(coreProjectText, "Crystal", "09F must not add Crystal packages to Core.");
        RequireDoesNotContain(coreProjectText, "Pdf", "09F must not add PDF packages to Core.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialRendererImplementationShellChecks.Run();", "CoreSmoke must run 09F renderer implementation shell checks.");

        int contractProofIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialRendererAdapterContractProofChecks.Run();",
            StringComparison.Ordinal);
        int implementationShellIndex = coreSmokeText.IndexOf(
            "J21Rmo242023OfficialRendererImplementationShellChecks.Run();",
            StringComparison.Ordinal);

        if (contractProofIndex < 0 || implementationShellIndex < 0 || implementationShellIndex < contractProofIndex)
        {
            throw new InvalidOperationException("CoreSmoke order is invalid. 09F must run after the renderer adapter contract proof.");
        }

        VerifyRuntimeBehavior();
    }

    private static void VerifyRuntimeBehavior()
    {
        OfficialNoOutputReportRenderer renderer = new();

        RequireSuccess(
            OfficialReportRendererContractValidator.ValidateContract(OfficialNoOutputReportRenderer.RendererContract),
            "09F renderer contract must validate.");

        OfficialReportRenderRequest previewRequest = CreateValidRequest(OfficialReportRenderFormat.Preview) with
        {
            ArtifactLabel = null,
            RequestedAt = null
        };

        Result<OfficialReportRenderResult> previewResult =
            renderer.RenderAsync(previewRequest).GetAwaiter().GetResult();

        RequireSuccess(previewResult, "09F preview placeholder request must succeed.");
        RequireEqual(OfficialReportRenderFormat.Preview, previewResult.Value.Artifact.Format, "09F renderer must preserve requested format.");
        RequireEqual("SALRYN official preview placeholder (10 binding tables)", previewResult.Value.Artifact.ArtifactLabel, "09F renderer must return deterministic preview placeholder label.");
        RequireEqual(null, previewResult.Value.Artifact.ArtifactReference, "09F renderer must not return an artifact reference.");
        RequireEqual(null, previewResult.Value.Artifact.CreatedAt, "09F renderer must not invent an artifact timestamp.");
        RequireEqual(OfficialNoOutputReportRenderer.RendererContract, previewResult.Value.RendererContract, "09F renderer must return its validated contract.");

        Result<OfficialReportRenderResult> repeatedPreviewResult =
            renderer.RenderAsync(previewRequest).GetAwaiter().GetResult();

        RequireSuccess(repeatedPreviewResult, "09F repeated preview placeholder request must succeed.");
        RequireEqual(previewResult.Value.Artifact, repeatedPreviewResult.Value.Artifact, "09F renderer placeholder output must be deterministic.");

        Result<OfficialReportRenderResult> exportResult =
            renderer.RenderAsync(CreateValidRequest(OfficialReportRenderFormat.Export) with
            {
                ArtifactLabel = "  SALRYN official export placeholder  "
            }).GetAwaiter().GetResult();

        RequireSuccess(exportResult, "09F export placeholder request must succeed with an artifact label.");
        RequireEqual(OfficialReportRenderFormat.Export, exportResult.Value.Artifact.Format, "09F renderer must preserve export intent.");
        RequireEqual("SALRYN official export placeholder", exportResult.Value.Artifact.ArtifactLabel, "09F renderer must normalize artifact label.");
        RequireEqual(null, exportResult.Value.Artifact.ArtifactReference, "09F export placeholder must not create an output reference.");
        RequireEqual(RequestedAt, exportResult.Value.Artifact.CreatedAt, "09F renderer must preserve requested timestamp.");

        Result<OfficialReportRenderResult> invalidResult =
            renderer.RenderAsync(CreateValidRequest(OfficialReportRenderFormat.Export) with
            {
                SourceDocument = CreateBindingDocumentWithout("Evidence")
            }).GetAwaiter().GetResult();

        RequireFailureCode(
            invalidResult,
            "OfficialReportRenderer.RequiredBindingTableMissing",
            "09F renderer must reject invalid binding DTOs before returning placeholders.");

        Result<OfficialReportRenderResult> missingLabelResult =
            renderer.RenderAsync(CreateValidRequest(OfficialReportRenderFormat.Pdf) with
            {
                ArtifactLabel = " "
            }).GetAwaiter().GetResult();

        RequireFailureCode(
            missingLabelResult,
            "OfficialReportRenderer.ArtifactLabelRequired",
            "09F renderer must preserve validator rules for output-intent labels.");
    }

    private static OfficialReportRenderRequest CreateValidRequest(
        OfficialReportRenderFormat format)
    {
        return new OfficialReportRenderRequest(
            SourceDocument: CreateBindingDocument(),
            Format: format,
            ArtifactLabel: "SALRYN official placeholder",
            RequestedByUserId,
            RequestedAt,
            TerminalId: "POS-RENDER",
            CopyModeMarker: "ORIGINAL",
            Metadata: new Dictionary<string, string>
            {
                [" phase "] = " 09F "
            });
    }

    private static OfficialReportBindingDocument CreateBindingDocument()
    {
        return new OfficialReportBindingDocument(
            OfficialReportRendererContractValidator.RequiredTableNames
                .Select(CreateRequiredTable)
                .ToArray());
    }

    private static OfficialReportBindingDocument CreateBindingDocumentWithout(string tableName)
    {
        return new OfficialReportBindingDocument(
            CreateBindingDocument().Tables
                .Where(table => !string.Equals(table.Name, tableName, StringComparison.Ordinal))
                .ToArray());
    }

    private static OfficialReportBindingTable CreateRequiredTable(string tableName)
    {
        IReadOnlyList<string> columns = OfficialReportRendererContractValidator.RequiredTableColumns[tableName];

        return new OfficialReportBindingTable(
            tableName,
            columns,
            new[]
            {
                new OfficialReportBindingRow(columns
                    .Select(column => new OfficialReportBindingCell(column, tableName + "." + column))
                    .ToArray())
            });
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

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} {result.ErrorDetail.Code}: {result.Error}");
        }
    }

    private static void RequireFailureCode<T>(Result<T> result, string expectedCode, string message)
    {
        if (result.IsSuccess || result.ErrorDetail.Code != expectedCode)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }
}

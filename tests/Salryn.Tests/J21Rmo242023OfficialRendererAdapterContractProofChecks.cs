using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialRendererAdapterContractProofChecks
{
    private static readonly Guid RequestedByUserId = Guid.Parse("25666666-6666-6666-6666-666666666661");
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string contractText = ReadRequiredFile(repoRoot, "src", "Salryn.Core", "Services", "OfficialReportRendererContracts.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        RequireContains(contractText, "OfficialReportRenderFormat", "Renderer adapter proof must expose render format intent.");
        RequireContains(contractText, "Preview", "Renderer adapter proof must expose preview intent.");
        RequireContains(contractText, "Pdf", "Renderer adapter proof must expose PDF intent without implementing it.");
        RequireContains(contractText, "PrintJob", "Renderer adapter proof must expose print-job intent without implementing it.");
        RequireContains(contractText, "Export", "Renderer adapter proof must expose export intent.");
        RequireContains(contractText, "OfficialReportRenderRequest", "Renderer adapter proof must expose render request.");
        RequireContains(contractText, "OfficialReportRenderResult", "Renderer adapter proof must expose render result.");
        RequireContains(contractText, "OfficialReportRenderedArtifact", "Renderer adapter proof must expose artifact metadata.");
        RequireContains(contractText, "OfficialReportRendererCapability", "Renderer adapter proof must expose capabilities.");
        RequireContains(contractText, "OfficialReportRendererContract", "Renderer adapter proof must expose renderer contract.");
        RequireContains(contractText, "IOfficialReportRenderer", "Renderer adapter proof must expose renderer interface.");
        RequireContains(contractText, "OfficialReportRendererContractValidator", "Renderer adapter proof must expose validator.");
        RequireContains(contractText, "OfficialReportBindingDocument", "Renderer adapter must consume report binding DTOs.");
        RequireNotContains(contractText, "OfficialPrintableDocument", "Renderer adapter must not consume printable documents directly.");
        RequireNotContains(contractText, "CrystalDecisions", "Renderer adapter proof must not reference Crystal Reports packages.");
        RequireNotContains(contractText, ".rpt", "Renderer adapter proof must not mention report templates.");
        RequireNotContains(contractText, "System.Windows.Forms", "Renderer adapter proof must not reference WinForms.");
        RequireNotContains(contractText, "Salryn.Infrastructure", "Renderer adapter proof must not reference Infrastructure.");
        RequireNotContains(contractText, "Sqlite", "Renderer adapter proof must not reference SQLite.");
        RequireNotContains(contractText, "File.Write", "Renderer adapter proof must not write files.");
        RequireNotContains(contractText, "Process.Start", "Renderer adapter proof must not launch external renderers.");
        RequireNotContains(contractText, "INSERT ", "Renderer adapter proof must not persist data.");
        RequireNotContains(contractText, "UPDATE ", "Renderer adapter proof must not mutate data.");
        RequireNotContains(contractText, "DELETE ", "Renderer adapter proof must not delete data.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialRendererAdapterContractProofChecks.Run();", "CoreSmoke must run renderer adapter contract proof checks.");

        VerifyRuntimeContractBehavior();
    }

    private static void VerifyRuntimeContractBehavior()
    {
        OfficialReportBindingDocument bindingDocument = CreateBindingDocument();

        Result previewValidation = OfficialReportRendererContractValidator.ValidateRequest(new OfficialReportRenderRequest(
            SourceDocument: bindingDocument,
            Format: OfficialReportRenderFormat.Preview,
            ArtifactLabel: null,
            RequestedByUserId,
            RequestedAt,
            TerminalId: "  POS-RENDER  ",
            CopyModeMarker: "  ORIGINAL  ",
            Metadata: new Dictionary<string, string>
            {
                [" z "] = " last ",
                [" a "] = " first "
            }));

        RequireSuccess(previewValidation, "Preview renderer request should not require artifact label.");

        OfficialReportRenderRequest normalized = OfficialReportRendererContractValidator.NormalizeRequest(new OfficialReportRenderRequest(
            SourceDocument: bindingDocument,
            Format: OfficialReportRenderFormat.Preview,
            ArtifactLabel: "  Preview Artifact  ",
            RequestedByUserId,
            RequestedAt,
            TerminalId: "  POS-RENDER  ",
            CopyModeMarker: "  ORIGINAL  ",
            Metadata: new Dictionary<string, string>
            {
                [" z "] = " last ",
                [" a "] = " first "
            }));

        RequireEqual("Preview Artifact", normalized.ArtifactLabel, "Renderer artifact label must be normalized.");
        RequireEqual("POS-RENDER", normalized.TerminalId, "Renderer terminal id must be normalized.");
        RequireEqual("ORIGINAL", normalized.CopyModeMarker, "Renderer copy marker must be normalized.");
        RequireEqual("a|z", string.Join("|", normalized.Metadata!.Keys), "Renderer metadata keys must be deterministic.");

        foreach (OfficialReportRenderFormat format in new[]
        {
            OfficialReportRenderFormat.Pdf,
            OfficialReportRenderFormat.PrintJob,
            OfficialReportRenderFormat.Export
        })
        {
            RequireSuccess(
                OfficialReportRendererContractValidator.ValidateRequest(CreateValidRequest(format)),
                format + " renderer request should validate with artifact label.");
        }

        ExpectFailure(
            OfficialReportRendererContractValidator.ValidateRequest(null),
            "OfficialReportRenderer.RequestRequired");

        ExpectFailure(
            OfficialReportRendererContractValidator.ValidateRequest(CreateValidRequest(OfficialReportRenderFormat.Pdf) with
            {
                SourceDocument = null
            }),
            "OfficialReportRenderer.SourceDocumentRequired");

        ExpectFailure(
            OfficialReportRendererContractValidator.ValidateRequest(CreateValidRequest((OfficialReportRenderFormat)999)),
            "OfficialReportRenderer.UnsupportedFormat");

        ExpectFailure(
            OfficialReportRendererContractValidator.ValidateRequest(CreateValidRequest(OfficialReportRenderFormat.Pdf) with
            {
                ArtifactLabel = " "
            }),
            "OfficialReportRenderer.ArtifactLabelRequired");

        ExpectFailure(
            OfficialReportRendererContractValidator.ValidateRequest(CreateValidRequest(OfficialReportRenderFormat.Export) with
            {
                SourceDocument = CreateBindingDocumentWithout("Evidence")
            }),
            "OfficialReportRenderer.RequiredBindingTableMissing");

        RequireSuccess(
            OfficialReportRendererContractValidator.ValidateContract(new OfficialReportRendererContract(
                "In-memory renderer contract proof",
                new[]
                {
                    new OfficialReportRendererCapability(OfficialReportRenderFormat.Preview, "Preview intent."),
                    new OfficialReportRendererCapability(OfficialReportRenderFormat.Pdf, "PDF intent."),
                    new OfficialReportRendererCapability(OfficialReportRenderFormat.PrintJob, "Print-job intent."),
                    new OfficialReportRendererCapability(OfficialReportRenderFormat.Export, "Export intent.")
                })),
            "Renderer contract capability declaration should validate.");

        InMemoryRenderer renderer = new();
        Result<OfficialReportRenderResult> renderResult =
            renderer.RenderAsync(CreateValidRequest(OfficialReportRenderFormat.Preview)).GetAwaiter().GetResult();

        RequireSuccess(renderResult, "In-memory renderer contract proof should accept binding document.");
        RequireEqual(OfficialReportRenderFormat.Preview, renderResult.Value.Artifact.Format, "Renderer result must preserve format intent.");
        RequireEqual("SALRYN official preview", renderResult.Value.Artifact.ArtifactLabel, "Renderer result must preserve artifact label.");
    }

    private static OfficialReportRenderRequest CreateValidRequest(
        OfficialReportRenderFormat format)
    {
        return new OfficialReportRenderRequest(
            SourceDocument: CreateBindingDocument(),
            Format: format,
            ArtifactLabel: "SALRYN official preview",
            RequestedByUserId,
            RequestedAt,
            TerminalId: "POS-RENDER",
            CopyModeMarker: "ORIGINAL",
            Metadata: null);
    }

    private static OfficialReportBindingDocument CreateBindingDocument()
    {
        return new OfficialReportBindingDocument(new[]
        {
            Table("Header", "OfficialDocumentNumber", "OR-2026-RENDER-001"),
            Table("MachineProfile", "RegisteredTaxpayerName", "Renderer Taxpayer"),
            Table("LineItems", "Description", "Renderer Item"),
            Table("Payments", "Method", "Cash"),
            Table("Totals", "TotalCents", "1000"),
            Table("Evidence", "SnapshotPayloadHash", "renderer-hash"),
            Table("Lifecycle", "State", "Active"),
            Table("Corrections", "CorrectionNote", "Renderer correction."),
            Table("LifecycleIssues", "Code", string.Empty),
            Table("CopyAudit", "OutputMarker", "ORIGINAL")
        });
    }

    private static OfficialReportBindingDocument CreateBindingDocumentWithout(string tableName)
    {
        return new OfficialReportBindingDocument(
            CreateBindingDocument().Tables
                .Where(table => !string.Equals(table.Name, tableName, StringComparison.Ordinal))
                .ToArray());
    }

    private static OfficialReportBindingTable Table(
        string name,
        string columnName,
        string value)
    {
        return new OfficialReportBindingTable(
            name,
            new[] { columnName },
            new[]
            {
                new OfficialReportBindingRow(new[]
                {
                    new OfficialReportBindingCell(columnName, value)
                })
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

    private static void ExpectFailure(Result result, string expectedCode)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException($"Expected renderer contract failure {expectedCode}.");
        }

        RequireEqual(expectedCode, result.ErrorDetail.Code, "Unexpected renderer contract validation code.");
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

    private static void RequireEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    private sealed class InMemoryRenderer : IOfficialReportRenderer
    {
        public Task<Result<OfficialReportRenderResult>> RenderAsync(
            OfficialReportRenderRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Result validation = OfficialReportRendererContractValidator.ValidateRequest(request);

            if (validation.IsFailure)
            {
                return Task.FromResult(Result<OfficialReportRenderResult>.Fail(validation.ErrorDetail));
            }

            OfficialReportRenderRequest normalizedRequest =
                OfficialReportRendererContractValidator.NormalizeRequest(request);

            OfficialReportRendererContract contract = new(
                "In-memory renderer contract proof",
                new[]
                {
                    new OfficialReportRendererCapability(OfficialReportRenderFormat.Preview, "Preview intent.")
                });

            return Task.FromResult(Result<OfficialReportRenderResult>.Success(
                new OfficialReportRenderResult(
                    new OfficialReportRenderedArtifact(
                        normalizedRequest.Format,
                        normalizedRequest.ArtifactLabel ?? "preview",
                        ArtifactReference: null,
                        CreatedAt: normalizedRequest.RequestedAt),
                    contract)));
        }
    }
}

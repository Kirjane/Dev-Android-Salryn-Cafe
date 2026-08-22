namespace Salryn.Tests;

public static class RmoEngineeringCompletionCandidateChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string coreSmoke = Read(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = Read(repoRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");
        string permissions = Read(repoRoot, "src", "Salryn.Core", "Security", "SalrynPermissions.cs");
        string engine = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentTaxBucketEngine.cs");
        string audit = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentAuditEventService.cs");
        string lifecycle = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialDocumentLifecycleReadService.cs");
        string xReading = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialXReadingService.cs");
        string evidencePackage = Read(repoRoot, "src", "Salryn.Core", "Services", "OfficialEngineeringEvidencePackageBuilder.cs");
        string sealPath = Path.Combine(repoRoot.FullName, "00-COMPLIANCE", "RMO_ENGINEERING_COMPLETION_CANDIDATE_SEAL.md");

        RequireContains(coreSmoke, "RmoEngineeringVatTaxBucketEngineChecks.Run();", "CoreSmoke must run VAT engine checks.");
        RequireContains(coreSmoke, "RmoEngineeringCompletionCandidateChecks.Run();", "CoreSmoke must run engineering completion guard.");
        RequireContains(infraSmoke, "rmo-engineering-candidate", "InfrastructureSmoke must expose rmo-engineering-candidate command.");
        RequireContains(engine, "class OfficialDocumentTaxBucketEngine", "VAT engine must exist.");
        RequireContains(audit, "Refund = 4", "Audit event enum must include Refund.");
        RequireContains(lifecycle, "Refunded = 5", "Lifecycle state must include Refunded.");
        RequireContains(xReading, "OfficialXReadingLifecycleSummary", "X-reading must expose lifecycle summary.");
        RequireContains(xReading, "OfficialXReadingVatBucketSummary", "X-reading must expose VAT bucket summary.");
        RequireContains(evidencePackage, "NON_CERTIFICATION_FOOTER", "Evidence package must include non-certification footer.");
        RequireContains(permissions, "OfficialDocument.Refund", "Permissions must include refund permission.");
        RequireContains(permissions, "OfficialEvidence.Export", "Permissions must include evidence export permission.");

        if (!File.Exists(sealPath))
        {
            throw new InvalidOperationException("Engineering completion candidate seal document is required.");
        }

        string seal = File.ReadAllText(sealPath);
        RequireContains(seal, "engineering candidate only", "Seal must state engineering candidate only.");
        RequireContains(seal, "not BIR certification", "Seal must deny BIR certification.");
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

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }
}

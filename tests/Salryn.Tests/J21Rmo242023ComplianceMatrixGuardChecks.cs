namespace Salryn.Tests;

public static class J21Rmo242023ComplianceMatrixGuardChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_1_RMO_24_2023_COMPLIANCE_MATRIX_GUARDRAILS.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(matrixText, "J21-1 status: guardrail only.", "J21-1 matrix must declare guardrail-only scope.");
        RequireContains(matrixText, "No schema change.", "J21-1 matrix must explicitly block schema changes.");
        RequireContains(matrixText, "No multi-tenant work.", "J21-1 matrix must explicitly block multi-tenant work.");

        RequireContains(matrixText, "RMO-24-2023-C01 Machine and registration profile", "Matrix must include machine/profile control.");
        RequireContains(matrixText, "RMO-24-2023-C02 Official receipt/invoice sequence", "Matrix must include official sequence control.");
        RequireContains(matrixText, "RMO-24-2023-C03 Official receipt/invoice required header fields", "Matrix must include receipt header control.");
        RequireContains(matrixText, "RMO-24-2023-C04 Item and sale amount breakdown", "Matrix must include item/amount breakdown control.");
        RequireContains(matrixText, "RMO-24-2023-C05 VAT, non-VAT, exempt, and zero-rated buckets", "Matrix must include VAT bucket control.");
        RequireContains(matrixText, "RMO-24-2023-C06 Customer and discount eligibility fields", "Matrix must include customer/discount control.");
        RequireContains(matrixText, "RMO-24-2023-C07 Tamper resistance and official transaction integrity", "Matrix must include tamper/integrity control.");
        RequireContains(matrixText, "RMO-24-2023-C08 Electronic journal and audit journal", "Matrix must include e-journal/audit journal control.");
        RequireContains(matrixText, "RMO-24-2023-C09 X-reading and Z-reading", "Matrix must include X/Z reading control.");
        RequireContains(matrixText, "RMO-24-2023-C10 Backend reports", "Matrix must include backend report control.");
        RequireContains(matrixText, "RMO-24-2023-C11 Data transmission and export readiness", "Matrix must include export readiness control.");
        RequireContains(matrixText, "RMO-24-2023-C12 Backup, retention, and recovery", "Matrix must include retention/recovery control.");
        RequireContains(matrixText, "RMO-24-2023-C13 Accreditation evidence pack", "Matrix must include accreditation evidence control.");

        RequireContains(matrixText, "Do not call any output \"Official Receipt\" or \"Official Invoice\" until C01-C05 are implemented and tested.", "Matrix must block premature official receipt/invoice labels.");
        RequireContains(matrixText, "Do not implement multi-tenant until machine/profile/sequence ownership is modeled.", "Matrix must block premature multi-tenant work.");
        RequireContains(matrixText, "Every compliance commit must reference at least one RMO control ID from this matrix.", "Matrix must require control IDs in compliance work.");

        RequireContains(handoffText, "Status: compliance planning guardrail only.", "J21-1 handoff must declare planning-only scope.");
        RequireContains(handoffText, "No schema change.", "J21-1 handoff must preserve no-schema boundary.");
        RequireContains(handoffText, "No multi-tenant work.", "J21-1 handoff must preserve no-multi-tenant boundary.");
        RequireContains(handoffText, "J21-2 must begin with machine/POS registration profile planning and schema, not UI polish.", "J21-1 handoff must point to the correct next phase.");

        RequireContains(coreSmokeText, "J21Rmo242023ComplianceMatrixGuardChecks.Run();", "Core smoke must run J21 RMO compliance matrix guards.");
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
}
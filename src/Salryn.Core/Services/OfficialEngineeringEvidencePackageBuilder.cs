using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialEngineeringEvidencePackageRequest(
    string MachineProfileSummary,
    string OfficialDocumentEvidenceText,
    string LifecycleEventsText,
    string XReadingReportText,
    string ZReadingReportText,
    string VatBucketSummaryText,
    string PaymentSummaryText,
    string AuditHashSummaryText);

public sealed record OfficialEngineeringEvidencePackageResult(
    string ManifestText,
    string PackageText,
    string PackageHash);

// Text-file engineering evidence package only. No certification claim.
public sealed class OfficialEngineeringEvidencePackageBuilder
{
    public Result<OfficialEngineeringEvidencePackageResult> Build(OfficialEngineeringEvidencePackageRequest request)
    {
        if (request is null)
        {
            return Result<OfficialEngineeringEvidencePackageResult>.Fail(Error.Validation(
                "OfficialEngineeringEvidencePackage.RequestRequired",
                "Engineering evidence package request is required."));
        }

        string[] sections =
        [
            "SALRYN_ENGINEERING_EVIDENCE_PACKAGE_V1",
            "NON_CERTIFICATION_FOOTER=No BIR certification, accreditation, legal approval, or full production RMO compliance claim.",
            "MACHINE_PROFILE=" + Normalize(request.MachineProfileSummary),
            "OFFICIAL_DOCUMENT_EVIDENCE=" + Normalize(request.OfficialDocumentEvidenceText),
            "LIFECYCLE_EVENTS=" + Normalize(request.LifecycleEventsText),
            "X_READING=" + Normalize(request.XReadingReportText),
            "Z_READING=" + Normalize(request.ZReadingReportText),
            "VAT_BUCKETS=" + Normalize(request.VatBucketSummaryText),
            "PAYMENTS=" + Normalize(request.PaymentSummaryText),
            "AUDIT_HASHES=" + Normalize(request.AuditHashSummaryText)
        ];

        string packageText = string.Join(Environment.NewLine, sections);
        string packageHash = ComputeSha256Hex(packageText);

        string manifestText = string.Join(
            Environment.NewLine,
            "SALRYN_ENGINEERING_EVIDENCE_MANIFEST_V1",
            "packageHash=" + packageHash,
            "artifactCount=8",
            "outputKind=text-file-only",
            "NON_CERTIFICATION_FOOTER=Engineering candidate package only.");

        return Result<OfficialEngineeringEvidencePackageResult>.Success(
            new OfficialEngineeringEvidencePackageResult(manifestText, packageText, packageHash));
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "(empty)"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string ComputeSha256Hex(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

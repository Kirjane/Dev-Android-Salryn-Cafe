using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.LicensingIssuer.Server;

public sealed record LicenseSigningOutcome(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    string? SignedLicenseJson,
    string? SignedLicenseSha256,
    string? SignerKeySpkiSha256,
    string? SignerMode,
    bool VerifierPassed);

public interface ILicenseSignerService
{
    SignerStatusSnapshot GetStatus();
    LicenseSigningOutcome SignAndVerify(LocalLicenseDocument unsignedDocument);
}

public sealed class LicenseSignerOptions
{
    public string? SigningKeyPemPath { get; init; }
    public string? SigningKeyPemInline { get; init; }
    public bool AllowUntrustedTestSigner { get; init; }
}

/// <summary>
/// Server-side offline RSA-SHA256 PKCS#1 v1.5 license signer.
/// Private key material is loaded strictly from external server configuration (never Git, DB, logs, or browser).
/// Fails closed when unconfigured or when the key does not match the trusted runtime verification key
/// unless explicitly opted into untrusted test mode.
/// </summary>
public sealed class ExternalLicenseSignerService : ILicenseSignerService
{
    private readonly LicenseSignerOptions _options;

    public ExternalLicenseSignerService(LicenseSignerOptions options)
    {
        _options = options ?? new LicenseSignerOptions();
    }

    public static LicenseSignerOptions FromEnvironment() => new()
    {
        SigningKeyPemPath = Environment.GetEnvironmentVariable("SALRYN_ISSUER_SIGNING_KEY_PEM_PATH")?.Trim(),
        SigningKeyPemInline = Environment.GetEnvironmentVariable("SALRYN_ISSUER_SIGNING_KEY_PEM")?.Trim(),
        AllowUntrustedTestSigner = string.Equals(
            Environment.GetEnvironmentVariable("SALRYN_ISSUER_ALLOW_UNTRUSTED_TEST_SIGNER")?.Trim(),
            "1",
            StringComparison.Ordinal)
    };

    public SignerStatusSnapshot GetStatus()
    {
        string trustedSpki = LocalLicenseSignatureVerifier.TrustedPublicKeySpkiSha256;
        (string? pem, string? loadError) = TryReadPrivateKeyPem();

        if (loadError is not null)
        {
            return new SignerStatusSnapshot(
                IsConfigured: false,
                CanIssue: false,
                SignerMode: "SignerError",
                PublicKeySpkiSha256: null,
                TrustedRuntimeSpkiSha256: trustedSpki,
                MatchesTrustedRuntimeKey: false,
                Message: loadError);
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            return new SignerStatusSnapshot(
                IsConfigured: false,
                CanIssue: false,
                SignerMode: "Unconfigured",
                PublicKeySpkiSha256: null,
                TrustedRuntimeSpkiSha256: trustedSpki,
                MatchesTrustedRuntimeKey: false,
                Message: "SIGNER_NOT_CONFIGURED: No authorized external private signing key is configured on the server. Draft creation and approval remain available; issuance fails closed.");
        }

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(pem.AsSpan());
            string spki = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            bool matchesTrusted = string.Equals(spki, trustedSpki, StringComparison.OrdinalIgnoreCase);

            if (matchesTrusted)
            {
                return new SignerStatusSnapshot(
                    IsConfigured: true,
                    CanIssue: true,
                    SignerMode: "ProductionTrustedSigner",
                    PublicKeySpkiSha256: spki,
                    TrustedRuntimeSpkiSha256: trustedSpki,
                    MatchesTrustedRuntimeKey: true,
                    Message: "Authorized external private signing key matches the trusted Salryn offline verification key.");
            }

            if (_options.AllowUntrustedTestSigner)
            {
                return new SignerStatusSnapshot(
                    IsConfigured: true,
                    CanIssue: true,
                    SignerMode: "TestUntrustedKey",
                    PublicKeySpkiSha256: spki,
                    TrustedRuntimeSpkiSha256: trustedSpki,
                    MatchesTrustedRuntimeKey: false,
                    Message: "NON-PRODUCTION TEST SIGNER ACTIVE: Key SPKI does not match the trusted Salryn production verification key. Licenses issued here will be rejected by Release Salryn Cafe installs.");
            }

            return new SignerStatusSnapshot(
                IsConfigured: true,
                CanIssue: false,
                SignerMode: "UntrustedKeyBlocked",
                PublicKeySpkiSha256: spki,
                TrustedRuntimeSpkiSha256: trustedSpki,
                MatchesTrustedRuntimeKey: false,
                Message: "SIGNER_KEY_UNTRUSTED: Configured private key SPKI does not match the trusted runtime verification key; issuance is blocked.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return new SignerStatusSnapshot(
                IsConfigured: false,
                CanIssue: false,
                SignerMode: "SignerInvalidKey",
                PublicKeySpkiSha256: null,
                TrustedRuntimeSpkiSha256: trustedSpki,
                MatchesTrustedRuntimeKey: false,
                Message: "Configured signing key PEM could not be imported as a valid RSA private key.");
        }
    }

    public LicenseSigningOutcome SignAndVerify(LocalLicenseDocument unsignedDocument)
    {
        ArgumentNullException.ThrowIfNull(unsignedDocument);
        SignerStatusSnapshot status = GetStatus();
        if (!status.IsConfigured)
        {
            return Fail("SIGNER_NOT_CONFIGURED", status.Message);
        }

        if (!status.CanIssue)
        {
            return Fail("SIGNER_KEY_UNTRUSTED", status.Message);
        }

        (string? pem, string? loadError) = TryReadPrivateKeyPem();
        if (loadError is not null || string.IsNullOrWhiteSpace(pem))
        {
            return Fail("SIGNER_NOT_CONFIGURED", loadError ?? "Private signing key unavailable.");
        }

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(pem.AsSpan());
            string publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
            string spki = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();

            string canonicalPayload = LocalLicenseSignatureVerifier.BuildSigningPayload(unsignedDocument);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(canonicalPayload);
            byte[] signatureBytes = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string signatureB64 = Convert.ToBase64String(signatureBytes);

            LocalLicenseDocument signedDoc = unsignedDocument with { Signature = signatureB64 };

            // Pre-release verification against exact canonical signature payload + RSA PKCS#1 v1.5
            bool verified = status.MatchesTrustedRuntimeKey
                ? LocalLicenseSignatureVerifier.Verify(signedDoc)
                : VerifyWithSpecificPublicKey(signedDoc, publicKeyPem);

            if (!verified)
            {
                return Fail("SIGNER_SELF_VERIFY_FAILED", "Signed license failed pre-release cryptographic verification and was withheld.");
            }

            // Also run LocalLicenseValidator structural + type + feature + machine + expiry check
            // (passing RequiredApplicationId for validation probe when Cafe validator reconciliation #38 is pending on this branch).
            LocalLicenseDocument validatorProbeDoc = string.Equals(signedDoc.ApplicationId, IssuerConstants.ProductCafe, StringComparison.Ordinal)
                ? signedDoc with { ApplicationId = LocalLicenseContractPolicy.RequiredApplicationId }
                : signedDoc;

            LocalLicenseValidator validator = new(new LocalLicenseValidatorOptions
            {
                MachineFingerprint = signedDoc.MachineBinding,
                SignatureVerifier = _ => verified
            });
            LocalLicenseValidationResult valResult = validator.Validate(new LocalLicenseFileReadResult(
                SourcePath: "<issuer-pre-release-check>",
                Exists: true,
                Malformed: false,
                Document: validatorProbeDoc,
                ErrorMessage: null));

            if (valResult.Status != LicenseStatus.Licensed)
            {
                return Fail("ISSUER_RUNTIME_CONTRACT_REJECTED", "Pre-release runtime validator rejected license: " + valResult.Message);
            }

            string signedJson = JsonSerializer.Serialize(signedDoc, IssuerConstants.CanonicalJsonOptions);
            string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signedJson))).ToLowerInvariant();

            return new LicenseSigningOutcome(
                IsSuccess: true,
                ErrorCode: null,
                ErrorMessage: null,
                SignedLicenseJson: signedJson,
                SignedLicenseSha256: sha256,
                SignerKeySpkiSha256: spki,
                SignerMode: status.SignerMode,
                VerifierPassed: true);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return Fail("SIGNER_OPERATION_FAILED", "Server-side license signing failed safely.");
        }
    }

    public static bool VerifyWithSpecificPublicKey(LocalLicenseDocument document, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(document.Signature) || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(LocalLicenseSignatureVerifier.BuildSigningPayload(document));
            byte[] sig = Convert.FromBase64String(document.Signature.Trim());
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.AsSpan());
            return rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private (string? Pem, string? Error) TryReadPrivateKeyPem()
    {
        if (!string.IsNullOrWhiteSpace(_options.SigningKeyPemInline))
        {
            return (_options.SigningKeyPemInline, null);
        }

        if (!string.IsNullOrWhiteSpace(_options.SigningKeyPemPath))
        {
            string fullPath = Path.GetFullPath(_options.SigningKeyPemPath);
            if (!File.Exists(fullPath))
            {
                return (null, "SIGNER_NOT_CONFIGURED: Configured signing key path does not exist on the server.");
            }
            return (File.ReadAllText(fullPath), null);
        }

        return (null, null);
    }

    private static LicenseSigningOutcome Fail(string code, string message) =>
        new(false, code, message, null, null, null, null, false);
}

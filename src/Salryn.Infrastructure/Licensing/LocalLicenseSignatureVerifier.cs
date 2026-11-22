using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public static class LocalLicenseSignatureVerifier
{
    // Canonical PRE-I beta verification key. Public verification material only.
    // Signing material stays outside the POS repository (LicensingOps secrets).
    public const string TrustedPublicKeySpkiSha256 =
        "30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4";

    private const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0wlZMUtjZTr8uC/sGrs1\n" +
        "wMVUkexpcH3DQUX4SIvKkZucgUoSmi65gH76qldGLd2V2uiipmMf+N7f5S5WLt3g\n" +
        "fXRm9XOhnqE+HzaCFFSmoTgFfTrcldXhJ56ODjdfjv5e2YKxhXlLkZ+aOd3DCy5z\n" +
        "sgh4cqJ98iHinuo4pLCrnjY8n0zrzrN+Q5BNQjPMtFu16q2cn7DBVymKUKNWtaXF\n" +
        "TcfFnvq6/uEkUXjB2zvQuWdmJwSVdqGo3JuJCEwZv/EAoIbBf11Uf7Nz9E+lzKqO\n" +
        "lbFkxWbqU5F6cGKHEw1R7jfYO5kkgH2rhgbbqWE/tCel9Daw4mlt62MOXRJmKrGn\n" +
        "CQIDAQAB\n" +
        "-----END PUBLIC KEY-----";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool Verify(LocalLicenseDocument document) =>
        VerifyWithPublicKeyPem(document, PublicKeyPem);

    public static bool VerifyWithPublicKeyPem(LocalLicenseDocument document, string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Signature) || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(BuildSigningPayload(document));
            byte[] signature = Convert.FromBase64String(document.Signature.Trim());

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.AsSpan());
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    public static string GetTrustedPublicKeySpkiSha256()
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(PublicKeyPem.AsSpan());
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    public static string BuildSigningPayload(LocalLicenseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LocalLicenseSignaturePayload payload = new(
            Clean(document.LicenseId),
            Clean(document.CustomerName),
            document.Edition,
            document.IssuedAt,
            document.ExpiresAt,
            Clean(document.MachineBinding),
            document.Features?
                .Where(feature => !string.IsNullOrWhiteSpace(feature))
                .Select(feature => feature.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            document.SchemaVersion,
            Clean(document.ApplicationId),
            Clean(document.StoreId),
            document.AllowedLanTerminals,
            document.SupportExpiresAt,
            document.UpdatesUntil,
            document.LicenseType,
            Clean(document.HostLicenseId),
            Clean(document.TerminalId));

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record LocalLicenseSignaturePayload(
        string? LicenseId,
        string? CustomerName,
        LicenseEdition Edition,
        DateTimeOffset IssuedAt,
        DateTimeOffset? ExpiresAt,
        string? MachineBinding,
        IReadOnlyList<string>? Features,
        int SchemaVersion,
        string? ApplicationId,
        string? StoreId,
        int? AllowedLanTerminals,
        DateTimeOffset? SupportExpiresAt,
        DateTimeOffset? UpdatesUntil,
        LicenseType? LicenseType,
        string? HostLicenseId,
        string? TerminalId);
}

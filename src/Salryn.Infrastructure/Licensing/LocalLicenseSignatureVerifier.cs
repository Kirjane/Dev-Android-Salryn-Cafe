using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public static class LocalLicenseSignatureVerifier
{
    // Beta key alignment (LIC-CLOSEOUT): public verification material only.
    // Signing material stays outside the POS repository (LicensingOps secrets).
    private const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAsBeocMHSZe36naBP98g7\n" +
        "qE38392z4QMEUSSqUs/Xgch2JnTixxaGqA/ZbyenNw33WCU5C2Ya4LP4e7W0NmmU\n" +
        "8NJ4b9RGrocSRKctiJoqgg+fG0/5LojjYl+GxyO2fuyKbYifoyieqkhKF83lrhhV\n" +
        "xi0ekIrkloD/t/QSkXH7im9aI6UmSEGQd9QIFjWUkpf1MR70/KKyYZ/8Z9DJrUQ5\n" +
        "VbCooi7lYZHDoIh8KJwUGBFReVf/N3KBnCAwhapMRXNekNj8DVtTNdUmUHM0+GZk\n" +
        "D3PJAaQAqdGFsdopky+JnH6h2CXQx6YwsrXpxJGYEQna9XNH0iKSbP5jVMM72DpV\n" +
        "EwIDAQAB\n" +
        "-----END PUBLIC KEY-----";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool Verify(LocalLicenseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Signature))
        {
            return false;
        }

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(BuildSigningPayload(document));
            byte[] signature = Convert.FromBase64String(document.Signature.Trim());

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem.AsSpan());
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
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

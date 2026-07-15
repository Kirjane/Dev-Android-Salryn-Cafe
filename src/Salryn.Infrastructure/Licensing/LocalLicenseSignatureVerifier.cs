using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;

namespace Salryn.Infrastructure.Licensing;

public static class LocalLicenseSignatureVerifier
{
    private const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAshRhcfmSFkjdJg1kHu8i\n" +
        "+Hek3oe4WZ9EwW+zOAC9y25DjF9rJmkerC0+7v6ejeT43VpXoRXOhVjpQ9tUby+Z\n" +
        "Pu2ANe1MLM6a13Jbm/T7NhlkbnyUL+TdVHs5evbUgMx3rUqaZFQZwXET+Orss3sp\n" +
        "leTaAiik5ImCTK4LY3FFtmhABC1uewZp/juaDlcSVMIthnihHpxtZ+oYum1ltnB8\n" +
        "tHWWfl4O/Ms9q0nrYyxze5RevACRJOusmhZH8CQiwoCNIfr0tlP0a91tAuH8tSE3\n" +
        "kJS03r8YEmnqkYSnC46buIt7tOtVZ1QmyubUTtViwLsA8fArhNHavzFfH/zrpkJa\n" +
        "awIDAQAB\n" +
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

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
        "97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59";

    private const string PublicKeyPem = "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA/EF2fGqnPKhCmwOkv8Ib\n" +
        "jUdYycma6PcdCxJfEQZCqxeJLi6qbabtqVsRUUvnd0HXN0tHqQTrtTjHEOKfYW0R\n" +
        "tWm/ZM/yADyQrTpqjsif+z1CNmYldNe+lECVIWFj7ZKF5adYwEZQENQW+5WIeId+\n" +
        "ooqwHJvMgCGouYSzgzZcLrEEUfAIwosW/q6/M4HDDcB2z40AbjvJ37+DI86sZv20\n" +
        "sJDA0mYOQY/LwXJfVN7Jaj5FZzUalqTaN7VT8EOhHVubGzt0E769fIOSt9n0JbRv\n" +
        "V+qlbweAo1ZuR6tmA5v6e3KkYK7PKs5B8DPdPRoNUvCDJhiql4fZjYI56s0Gy9ZB\n" +
        "bwIDAQAB\n" +
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

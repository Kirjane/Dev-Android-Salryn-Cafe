using System.Security.Cryptography;
using System.Text;

namespace Salryn.Sync.Central.Server;

public static class CentralCrypto
{
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Sha256Hex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    public static string Sha256Hex(byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsAscii(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static string GenerateOpaqueToken(int byteCount = 32)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string GenerateJoinCode()
    {
        char[] chars = new char[9];
        byte[] random = RandomNumberGenerator.GetBytes(8);
        for (int i = 0; i < 4; i++)
        {
            chars[i] = JoinCodeAlphabet[random[i] % JoinCodeAlphabet.Length];
        }
        chars[4] = '-';
        for (int i = 0; i < 4; i++)
        {
            chars[5 + i] = JoinCodeAlphabet[random[4 + i] % JoinCodeAlphabet.Length];
        }
        return new string(chars);
    }

    public static string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must not be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return $"pbkdf2-sha256${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}";
    }

    public static bool VerifyPassword(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        string[] parts = encodedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int iterations) || iterations < 10_000)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static (string PrivateKeyPem, string PublicKeySpkiBase64) CreateEcdsaP256KeyPair()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privatePem = ecdsa.ExportPkcs8PrivateKeyPem();
        string publicSpki = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        return (privatePem, publicSpki);
    }

    public static bool TryValidateEcdsaP256PublicKey(string publicKeySpkiBase64, out string thumbprint)
    {
        thumbprint = string.Empty;
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64))
        {
            return false;
        }

        try
        {
            byte[] spki = Convert.FromBase64String(publicKeySpkiBase64);
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out int bytesRead);
            if (bytesRead != spki.Length || ecdsa.KeySize != 256)
            {
                return false;
            }

            thumbprint = Sha256Hex(spki);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyEcdsaP256Signature(string publicKeySpkiBase64, string payload, string signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64) || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        try
        {
            byte[] spki = Convert.FromBase64String(publicKeySpkiBase64);
            byte[] signature = Convert.FromBase64String(signatureBase64);
            byte[] data = Encoding.UTF8.GetBytes(payload);
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    public static string SignEcdsaP256WithPem(string privateKeyPem, string payload)
    {
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        byte[] signature = ecdsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signature);
    }

    public static string BuildChallengeSigningPayload(
        string challengeId,
        string nonceBase64,
        Guid deviceId,
        string productId,
        Guid tenantId,
        Guid storeId)
    {
        return $"{challengeId}|{nonceBase64}|{deviceId:D}|{productId}|{tenantId:D}|{storeId:D}";
    }

    public static string BuildLeaseSigningPayload(
        Guid deviceId,
        Guid tenantId,
        Guid storeId,
        string productId,
        long serverEpoch,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        return $"{deviceId:D}|{tenantId:D}|{storeId:D}|{productId}|{serverEpoch}|{issuedAtUtc:O}|{expiresAtUtc:O}";
    }
}

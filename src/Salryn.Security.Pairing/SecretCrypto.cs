using System.Security.Cryptography;
using System.Text;

namespace Salryn.Security.Pairing;

/// <summary>
/// Cryptographic primitives for I4 pairing, challenge-response, and constant-time token verification.
/// </summary>
public static class SecretCrypto
{
    private const string JoinCodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string GenerateHighEntropyToken(int byteCount = 32)
    {
        if (byteCount < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), "Tokens must be at least 32 bytes (256 bits).");
        }

        byte[] bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string GenerateJoinCode(int length = 8)
    {
        if (length < 6)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Join code must be at least 6 characters.");
        }

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = JoinCodeAlphabet[RandomNumberGenerator.GetInt32(JoinCodeAlphabet.Length)];
        }

        return new string(chars);
    }

    public static byte[] ComputeSha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    public static bool FixedTimeEquals(byte[]? left, byte[]? right)
    {
        if (left is null || right is null || left.Length == 0 || left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static bool FixedTimeEqualsSecretHash(string rawSecret, byte[] expectedSha256Hash)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            return false;
        }

        byte[] actualHash = ComputeSha256(rawSecret.Trim());
        return FixedTimeEquals(actualHash, expectedSha256Hash);
    }

    public static PairingResult<string> ValidateAndThumbprintEcdsaP256Spki(byte[]? spkiPublicKey)
    {
        if (spkiPublicKey is null || spkiPublicKey.Length == 0)
        {
            return PairingResult<string>.Fail(
                PairingErrorCodes.InvalidPublicKey,
                "An ECDSA P-256 SubjectPublicKeyInfo public key is required.",
                httpStatusCode: 400);
        }

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spkiPublicKey, out int bytesRead);
            if (bytesRead != spkiPublicKey.Length)
            {
                return PairingResult<string>.Fail(
                    PairingErrorCodes.InvalidPublicKey,
                    "Public key contains trailing bytes.",
                    httpStatusCode: 400);
            }

            if (ecdsa.KeySize != 256)
            {
                return PairingResult<string>.Fail(
                    PairingErrorCodes.InvalidPublicKey,
                    $"Only ECDSA P-256 (256-bit) keys are supported; received {ecdsa.KeySize}-bit key.",
                    httpStatusCode: 400);
            }

            string thumbprint = Convert.ToHexString(SHA256.HashData(spkiPublicKey)).ToLowerInvariant();
            return PairingResult<string>.Success(thumbprint);
        }
        catch (CryptographicException)
        {
            return PairingResult<string>.Fail(
                PairingErrorCodes.InvalidPublicKey,
                "Public key is not a valid ECDSA P-256 SubjectPublicKeyInfo.",
                httpStatusCode: 400);
        }
    }

    public static byte[] SignEcdsaP256(ECDsa privateKey, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        return privateKey.SignData(payload, HashAlgorithmName.SHA256);
    }

    public static bool VerifyEcdsaP256(byte[] spkiPublicKey, ReadOnlySpan<byte> payload, byte[]? signature)
    {
        if (spkiPublicKey is null || spkiPublicKey.Length == 0 || signature is null || signature.Length == 0)
        {
            return false;
        }

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spkiPublicKey, out int bytesRead);
            if (bytesRead != spkiPublicKey.Length || ecdsa.KeySize != 256)
            {
                return false;
            }

            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

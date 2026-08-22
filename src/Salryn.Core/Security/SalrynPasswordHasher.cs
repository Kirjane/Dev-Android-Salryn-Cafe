using System.Security.Cryptography;
using Salryn.Core.Common;

namespace Salryn.Core.Security;

public static class SalrynPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;
    private const string FormatPrefix = "PBKDF2-SHA256.V1";

    // Kirch: Creates a salted PBKDF2 password hash for local offline authentication.
    public static Result<string> HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return Result<string>.Fail(Error.Validation(
                "Auth.PasswordRequired",
                "Password is required."));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        string encoded = string.Join(
            '$',
            FormatPrefix,
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));

        return Result<string>.Success(encoded);
    }

    // Kirch: Verifies a plain password against SALRYN's stored PBKDF2 hash format.
    public static bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split('$');

        if (parts.Length != 4 || parts[0] != FormatPrefix)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHash);
    }
}

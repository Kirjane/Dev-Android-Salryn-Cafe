using System.Security.Cryptography;

namespace Salryn.Infrastructure.Security;

public sealed class PasswordHashOptions
{
    public int Iterations { get; init; } = 210_000;

    public int SaltSizeBytes { get; init; } = 16;

    public int HashSizeBytes { get; init; } = 32;
}

public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string storedHash);
}

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string FormatMarker = "PBKDF2-SHA256";

    private readonly PasswordHashOptions _options;

    public Pbkdf2PasswordHasher()
        : this(new PasswordHashOptions())
    {
    }

    public Pbkdf2PasswordHasher(PasswordHashOptions options)
    {
        _options = options;
    }

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(_options.SaltSizeBytes);

        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _options.Iterations,
            HashAlgorithmName.SHA256,
            _options.HashSizeBytes);

        return string.Join(
            "|",
            FormatMarker,
            _options.Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split('|');

        if (parts.Length != 4 || parts[0] != FormatMarker)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int iterations))
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

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

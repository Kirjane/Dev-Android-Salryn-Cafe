using System.Security.Cryptography;
using System.Text;

namespace Salryn.Core.TerminalRegistry;

public static class TerminalPairingSecretHasher
{
    public static string GeneratePairingCode()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "X")
            .Replace("/", "Y");
    }

    public static string HashPairingCode(string pairingCode)
    {
        if (string.IsNullOrWhiteSpace(pairingCode))
        {
            throw new ArgumentException("Pairing code is required.", nameof(pairingCode));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(pairingCode.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

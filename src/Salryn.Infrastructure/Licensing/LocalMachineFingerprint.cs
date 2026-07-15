using System.Security.Cryptography;
using System.Text;

namespace Salryn.Infrastructure.Licensing;

public static class LocalMachineFingerprint
{
    public static string Current()
    {
        string material = string.Join(
            "|",
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.Platform.ToString());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

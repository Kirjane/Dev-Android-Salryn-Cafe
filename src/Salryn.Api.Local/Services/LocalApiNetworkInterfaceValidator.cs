using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Salryn.Core.Common;
using Salryn.Core.Host;

namespace Salryn.Api.Local.Services;

public static class LocalApiNetworkInterfaceValidator
{
    public static Result ValidateExplicitLanBind(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return Result.Fail(Error.Validation(
                "LanBind.BindAddressRequired",
                "An explicit LAN bind IP address is required."));
        }

        string trimmed = bindAddress.Trim();

        if (SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(trimmed))
        {
            return Result.Fail(Error.Validation(
                "LanBind.WildcardForbidden",
                "Wildcard bind addresses are forbidden. Use an explicit LAN IPv4 address assigned to this host."));
        }

        if (SalrynHostCommandLoopbackRules.IsLoopbackOrigin(trimmed))
        {
            return Result.Fail(Error.Validation(
                "LanBind.LoopbackForbidden",
                "LAN client bind must use a non-loopback IPv4 address assigned to this host."));
        }

        if (!IPAddress.TryParse(trimmed, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return Result.Fail(Error.Validation(
                "LanBind.InvalidIpv4",
                "LAN bind must be an explicit IPv4 address assigned to this host."));
        }

        if (!IsAssignedToLocalInterface(trimmed))
        {
            return Result.Fail(Error.Validation(
                "LanBind.IpNotOnHost",
                $"LAN bind IP {trimmed} is not assigned to a local network interface on this host."));
        }

        return Result.Success();
    }

    public static bool IsAssignedToLocalInterface(string bindAddress)
    {
        if (!IPAddress.TryParse(bindAddress.Trim(), out IPAddress? target)
            || target.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork
                    && address.Address.Equals(target))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
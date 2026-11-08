using System.Net;
using System.Net.Sockets;

namespace Golether.Core.Networking;

/// <summary>
/// Tells the addresses reachable from the Internet from those of a home network, a provider's network or this computer.
/// </summary>
public static class AddressScope
{
    /// <summary>
    /// Tells whether an address can be reached from other networks: not private, shared (CGNAT), loopback, link-local,
    /// multicast or reserved.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> for an address of the Internet.</returns>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => false,
                100 => b[1] is < 64 or > 127,
                169 => b[1] != 254,
                172 => b[1] is < 16 or > 31,
                192 => b[1] != 168,
                >= 224 => false,
                _ => true,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal &&
                   !address.IsIPv6Multicast && !address.Equals(IPAddress.IPv6None) && (first & 0xFE) != 0xFC;
        }

        return false;
    }

    /// <summary>
    /// Tells whether a host of an endpoint is a public address; a name is taken as public, it is resolved elsewhere.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns><see langword="true"/> for a public address or a name.</returns>
    public static bool IsPublicHost(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return !IPAddress.TryParse(host.Trim('[', ']'), out var address) || IsPublic(address);
    }
}

using System.Net;
using System.Net.Sockets;

namespace MiHouse.Core.Lan;

/// <summary>
/// Classifies addresses that can plausibly be reached on the local network.
/// </summary>
public static class LanAddress
{
    /// <summary>
    /// Returns true for IPv4 private ranges (<c>10/8</c>, <c>172.16/12</c>, <c>192.168/16</c>)
    /// and IPv6 unique-local (<c>fc00::/7</c>) or link-local (<c>fe80::/10</c>) addresses.
    /// Public, loopback and invalid addresses return false.
    /// </summary>
    public static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) == 0xFC
                || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        }

        return false;
    }
}

using MiHouse.Core.Lan;

namespace MiHouse.Core.Tests.Lan;

public sealed class LanAddressTests
{
    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.254", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("192.168.255.254", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fd12:3456:789a::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("febf::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("111.196.128.224", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.169.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("2408:8456:1234::1", false)]
    [InlineData("::1", false)]
    [InlineData("not-an-ip", false)]
    [InlineData("", false)]
    public void ClassifiesAddresses(string ip, bool expected)
    {
        Assert.Equal(expected, LanAddress.IsPrivate(ip));
    }
}

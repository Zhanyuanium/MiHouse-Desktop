using MiHouse.Core.Crypto;

namespace MiHouse.Core.Tests.Crypto;

public sealed class Rc4Tests
{
    [Theory]
    [InlineData("Key", "Plaintext", "BBF316E8D940AF0AD3")]
    [InlineData("Wiki", "pedia", "1021BF0420")]
    [InlineData("Secret", "Attack at dawn", "45A01F645FC35B383552544B9BF5")]
    public void EncryptsKnownVectors(string key, string plaintext, string expectedHex)
    {
        var cipher = Rc4.Transform(ToBytes(key), ToBytes(plaintext));

        Assert.Equal(expectedHex, Convert.ToHexString(cipher));
    }

    [Fact]
    public void RoundTrips()
    {
        var key = ToBytes("mihouse");
        var plaintext = ToBytes("{\"method\":\"get_properties\"}");

        var cipher = Rc4.Transform(key, plaintext);
        var decrypted = Rc4.Transform(key, cipher);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, cipher);
    }

    [Fact]
    public void DiscardingKeystreamChangesOutput()
    {
        var key = ToBytes("mihouse");
        var plaintext = ToBytes("0123456789abcdef");

        var direct = Rc4.Transform(key, plaintext);
        var discarded = Rc4.Transform(key, plaintext, discardBytes: 1024);

        Assert.NotEqual(direct, discarded);
        Assert.Equal(plaintext, Rc4.Transform(key, discarded, discardBytes: 1024));
    }

    [Fact]
    public void MatchesRfc6229AfterDiscardingFirstKilobyte()
    {
        // RFC 6229 section 2: key 0x0102030405, keystream starting at offset 1024.
        byte[] key = [0x01, 0x02, 0x03, 0x04, 0x05];

        var keystream = Rc4.Transform(key, new byte[16], discardBytes: 1024);

        Assert.Equal("30ABBCC7C20B01609F23EE2D5F6BB7DF", Convert.ToHexString(keystream));
    }

    [Fact]
    public void RejectsEmptyKey()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rc4.Transform([], [1, 2, 3]));
    }

    private static byte[] ToBytes(string value) => System.Text.Encoding.UTF8.GetBytes(value);
}

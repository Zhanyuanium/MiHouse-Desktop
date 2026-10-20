using System.Security.Cryptography;
using System.Text;
using MiHouse.Core.Cloud;
using MiHouse.Core.Crypto;

namespace MiHouse.Core.Tests.Cloud;

public sealed class MiHomeRequestSignerTests
{
    private const string _ssecurity = "MDEyMzQ1Njc4OWFiY2RlZg==";
    private const string _nonce = "bm9uY2UwMDE=";

    [Fact]
    public void CreateNonceEncodesRandomBytesThenMinuteCounter()
    {
        var clock = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var signer = new MiHomeRequestSigner(() => clock, () => [1, 2, 3, 4, 5, 6, 7, 8]);

        var nonce = signer.CreateNonce();
        var bytes = Convert.FromBase64String(nonce);

        // 0x0102030405060708 followed by 28_333_333 (0x01B05515).
        Assert.Equal("010203040506070801B05515", Convert.ToHexString(bytes));
    }

    [Fact]
    public void CreateNonceOmitsLeadingZeroBytesOfMinuteCounter()
    {
        var signer = new MiHomeRequestSigner(() => DateTimeOffset.FromUnixTimeMilliseconds(0), () => new byte[8]);

        var bytes = Convert.FromBase64String(signer.CreateNonce());

        Assert.Equal(8, bytes.Length);
    }

    [Fact]
    public void SignedNonceMatchesIndependentSha256()
    {
        var signer = new MiHomeRequestSigner();

        var expected = Convert.ToBase64String(SHA256.HashData(
            [.. Convert.FromBase64String(_ssecurity), .. Convert.FromBase64String(_nonce)]));

        Assert.Equal(expected, signer.SignedNonce(_ssecurity, _nonce));
    }

    [Fact]
    public void BuildSignatureStringUsesPostPathAndFieldOrder()
    {
        KeyValuePair<string, string>[] fields =
        [
            new("data", """{"x":1}"""),
            new("z", "2"),
        ];

        var signature = MiHomeRequestSigner.BuildSignatureString(
            "https://api.io.mi.com/app/home/device_list",
            fields,
            "signed-nonce");

        Assert.Equal("""POST&/home/device_list&data={"x":1}&z=2&signed-nonce""", signature);
    }

    [Fact]
    public void BuildSignatureStringOmitsSeparatorWhenFieldsAreEmpty()
    {
        var signature = MiHomeRequestSigner.BuildSignatureString(
            "https://api.io.mi.com/app/home/device_list",
            [],
            "signed-nonce");

        Assert.Equal("POST&/home/device_list&signed-nonce", signature);
    }

    [Theory]
    [InlineData("https://api.io.mi.com/app/home/device_list", "/home/device_list")]
    [InlineData("https://de.api.io.mi.com/app/v2/miotspec/prop/get", "/v2/miotspec/prop/get")]
    public void PathForSignatureStripsHostAndAppPrefix(string url, string expected)
    {
        Assert.Equal(expected, MiHomeRequestSigner.PathForSignature(url));
    }

    [Fact]
    public void SignFormEncryptsFieldsAndAppendsMetadataInWireOrder()
    {
        var signer = new MiHomeRequestSigner(_ssecurity, () => _nonce, null, null);
        KeyValuePair<string, string>[] fields = [new("data", """{"a":1}""")];

        var signed = signer.SignForm("https://api.io.mi.com/app/home/device_list", fields, out var nonce);

        Assert.Equal(_nonce, nonce);
        Assert.Equal(
            new[] { "data", "rc4_hash__", "signature", "ssecurity", "_nonce" },
            signed.Select(field => field.Key));

        var values = signed.ToDictionary(field => field.Key, field => field.Value);
        var signedNonce = signer.SignedNonce(_ssecurity, _nonce);

        Assert.Equal(fields[0].Value, DecryptValue(_ssecurity, _nonce, values["data"]));

        var expectedHash = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(
            $$"""POST&/home/device_list&data={{fields[0].Value}}&{{signedNonce}}""")));
        Assert.Equal(expectedHash, DecryptValue(_ssecurity, _nonce, values["rc4_hash__"]));

        var expectedSignature = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(
            $$"""POST&/home/device_list&data={{values["data"]}}&rc4_hash__={{values["rc4_hash__"]}}&{{signedNonce}}""")));
        Assert.Equal(expectedSignature, values["signature"]);

        Assert.Equal(_ssecurity, values["ssecurity"]);
        Assert.Equal(_nonce, values["_nonce"]);
    }

    [Fact]
    public void DecryptResponseReversesRc4Encryption()
    {
        const string plaintext = """{"code":0,"result":{"list":[]}}""";
        var signer = new MiHomeRequestSigner();
        var key = Convert.FromBase64String(signer.SignedNonce(_ssecurity, _nonce));
        var body = Convert.ToBase64String(
            Rc4.Transform(key, Encoding.UTF8.GetBytes(plaintext), discardBytes: 1024));

        Assert.Equal(plaintext, signer.DecryptResponse(_ssecurity, _nonce, body));
    }

    [Fact]
    public void SignFormWithoutConfiguredSsecurityThrows()
    {
        var signer = new MiHomeRequestSigner();

        Assert.Throws<InvalidOperationException>(() =>
            signer.SignForm("https://api.io.mi.com/app/home/device_list", [], out _));
    }

    private static string DecryptValue(string ssecurity, string nonce, string value)
    {
        var key = SHA256.HashData(
            [.. Convert.FromBase64String(ssecurity), .. Convert.FromBase64String(nonce)]);

        var plaintext = Rc4.Transform(key, Convert.FromBase64String(value), discardBytes: 1024);
        return Encoding.UTF8.GetString(plaintext);
    }
}

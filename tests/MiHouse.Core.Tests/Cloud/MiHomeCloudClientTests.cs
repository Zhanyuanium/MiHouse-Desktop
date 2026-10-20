using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Cloud;
using MiHouse.Core.Crypto;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;

namespace MiHouse.Core.Tests.Cloud;

public sealed class MiHomeCloudClientTests
{
    private const string _ssecurity = "MDEyMzQ1Njc4OWFiY2RlZg==";
    private const string _nonce = "bm9uY2UwMDE=";
    private const string _serviceToken = "service-token";
    private const string _deviceListPayload =
        """{"getVirtualModel":true,"getHuamiDevices":1,"get_split_device":false,"support_smart_home":true}""";

    private static readonly MiHomeSession _session = new("12345", _serviceToken, _ssecurity, "cn");

    [Fact]
    public async Task GetDevicesAsyncSignsRequestAndMapsDevices()
    {
        const string response = """
            &&&START&&&{"code":0,"result":{"list":[
            {"did":"111","model":"yeelink.light.color1","name":"Lamp","room_name":"Living Room","isOnline":true},
            {"did":"222","model":"lumi.gateway.v3","name":"Gateway","roomName":"Hall","online":1},
            {"did":"333","model":"chuangmi.plug.v3","isOnline":false,"online":true}
            ]}}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        var devices = await client.GetDevicesAsync(_session, CancellationToken.None);

        Assert.Equal(
            new MiDevice("111", "yeelink.light.color1", "Lamp", "Living Room", true),
            devices[0]);
        Assert.Equal(new MiDevice("222", "lumi.gateway.v3", "Gateway", "Hall", true), devices[1]);
        Assert.Equal(new MiDevice("333", "chuangmi.plug.v3", "chuangmi.plug.v3", null, false), devices[2]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://api.io.mi.com/app/home/device_list"), handler.RequestUri);
        Assert.Equal(
            $"userId=12345; serviceToken={_serviceToken}; yetAnotherServiceToken={_serviceToken}; " +
            "locale=zh_CN; timezone=GMT+08:00; is_daylight=0; dst_offset=0; channel=MI_APP_STORE",
            handler.Cookie);
        Assert.DoesNotContain("cUserId", handler.Cookie);
        Assert.Equal("identity", handler.AcceptEncoding);
        Assert.Equal("PROTOCAL-HTTP2", handler.ProtocolFlag);
        Assert.Equal("ENCRYPT-RC4", handler.EncryptAlgorithm);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);

        var form = ParseForm(handler.Body!);
        Assert.Equal(
            new[] { "data", "rc4_hash__", "signature", "ssecurity", "_nonce" },
            form.Select(field => field.Key));

        var values = form.ToDictionary(field => field.Key, field => field.Value);
        var signedNonce = SignedNonce(_ssecurity, _nonce);
        var decryptedPayload = DecryptValue(values["data"]);

        Assert.Equal(_deviceListPayload, decryptedPayload);
        Assert.Equal(_ssecurity, values["ssecurity"]);
        Assert.Equal(_nonce, values["_nonce"]);

        var expectedHash = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(
            $$"""POST&/home/device_list&data={{_deviceListPayload}}&{{signedNonce}}""")));
        Assert.Equal(expectedHash, DecryptValue(values["rc4_hash__"]));

        var expectedSignature = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(
            $$"""POST&/home/device_list&data={{values["data"]}}&rc4_hash__={{values["rc4_hash__"]}}&{{signedNonce}}""")));
        Assert.Equal(expectedSignature, values["signature"]);
    }

    [Fact]
    public async Task GetDevicesAsyncDecryptsEncryptedResponse()
    {
        const string payload = """
            {"code":0,"result":{"list":[{"did":"42","model":"model.x","name":"Encrypted","isOnline":true}]}}
            """;
        var handler = new StubHttpMessageHandler(EncryptResponse(payload));
        var client = CreateClient(handler);

        var devices = await client.GetDevicesAsync(_session, CancellationToken.None);

        Assert.Equal(new MiDevice("42", "model.x", "Encrypted", null, true), Assert.Single(devices));
    }

    [Theory]
    [InlineData("""{"code":0,"result":[{"siid":2,"piid":1,"code":0,"value":true},{"siid":2,"piid":2,"code":-1},{"siid":3,"piid":1,"value":42}]}""")]
    [InlineData("""{"code":0,"result":{"result":[{"siid":2,"piid":1,"code":0,"value":true},{"siid":2,"piid":2,"code":-1},{"siid":3,"piid":1,"value":42}]}}""")]
    public async Task GetPropertiesAsyncParsesBothResultShapesAndSkipsFailures(string response)
    {
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);
        MiPropertyKey[] properties = [new(2, 1), new(2, 2), new(3, 1)];

        var values = await client.GetPropertiesAsync(_session, "111", properties, CancellationToken.None);

        Assert.Equal(2, values.Count);
        Assert.Equal(new MiPropertyKey(2, 1), values[0].Key);
        Assert.True(values[0].Value.GetBoolean());
        Assert.Equal(new MiPropertyKey(3, 1), values[1].Key);
        Assert.Equal(42, values[1].Value.GetInt32());

        Assert.Equal(
            """{"datasource":1,"params":[{"did":"111","siid":2,"piid":1},{"did":"111","siid":2,"piid":2},{"did":"111","siid":3,"piid":1}]}""",
            DecryptRequestPayload(handler));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-7, false)]
    public async Task SetPropertyAsyncChecksEntryCodes(int entryCode, bool succeeds)
    {
        var response = $$"""
            {"code":0,"result":[{"did":"111","siid":2,"piid":1,"code":{{entryCode}},"message":"rejected","description":"detail"}]}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        if (succeeds)
        {
            await client.SetPropertyAsync(
                _session,
                "111",
                new MiPropertyKey(2, 1),
                JsonSerializer.SerializeToElement(true),
                CancellationToken.None);
        }
        else
        {
            var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
                _session,
                "111",
                new MiPropertyKey(2, 1),
                JsonSerializer.SerializeToElement(true),
                CancellationToken.None));

            Assert.Equal(entryCode, error.Code);
            Assert.Equal("rejected", error.Message);
            Assert.Equal("detail", error.Description);
        }

        Assert.Equal(
            """{"params":[{"did":"111","siid":2,"piid":1,"value":true}]}""",
            DecryptRequestPayload(handler));
    }

    [Fact]
    public async Task SetPropertyAsyncRejectsOuterCodeOne()
    {
        const string response = """{"code":1,"result":[{"did":"111","siid":2,"piid":1,"code":0}]}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
            _session,
            "111",
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(1, error.Code);
    }

    [Theory]
    [InlineData("""{"code":0}""")]
    [InlineData("""{"code":0,"result":[]}""")]
    public async Task SetPropertyAsyncRequiresResultEntry(string response)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
            _session,
            "111",
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
    }

    [Fact]
    public async Task InvokeActionAsyncReturnsOutput()
    {
        const string response = """{"code":0,"result":{"code":0,"out":[1,2]}}""";
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);
        JsonElement[] input = [JsonSerializer.SerializeToElement(true), JsonSerializer.SerializeToElement(5)];

        var result = await client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            input,
            CancellationToken.None);

        Assert.Equal("[1,2]", result.GetRawText());
        Assert.Equal(
            """{"params":{"did":"111","siid":2,"aiid":1,"in":[true,5]}}""",
            DecryptRequestPayload(handler));
    }

    [Fact]
    public async Task InvokeActionAsyncWithoutOutputReturnsEmptyArray()
    {
        const string response = """{"code":0,"result":{"code":0}}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var result = await client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            [],
            CancellationToken.None);

        Assert.Equal("[]", result.GetRawText());
    }

    [Fact]
    public async Task InvokeActionAsyncChecksInnerCode()
    {
        const string response = """{"code":0,"result":{"code":-3,"message":"busy","description":"device busy"}}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(-3, error.Code);
        Assert.Equal("busy", error.Message);
        Assert.Equal("device busy", error.Description);
    }

    [Fact]
    public async Task InvokeActionAsyncRequiresResultObject()
    {
        const string response = """{"code":0}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
    }

    [Fact]
    public async Task NonZeroResponseCodeThrowsWithCloudDetails()
    {
        const string response = """{"code":-6,"message":"login required","description":"auth failed"}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetDevicesAsync(_session, CancellationToken.None));

        Assert.Equal(-6, error.Code);
        Assert.Equal("login required", error.Message);
        Assert.Equal("auth failed", error.Description);
    }

    [Theory]
    [InlineData("""{"result":{"list":[]}}""")]
    [InlineData("""{"code":"0","result":{"list":[]}}""")]
    public async Task MissingOrNonNumericCodeIsAProtocolError(string response)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetDevicesAsync(_session, CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
    }

    [Fact]
    public async Task HttpServiceUnavailableThrowsBeforeParsingErrorBody()
    {
        const string response = """{"code":-6,"message":"login required"}""";
        var client = CreateClient(new StubHttpMessageHandler(response, HttpStatusCode.ServiceUnavailable));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetDevicesAsync(_session, CancellationToken.None));

        Assert.Equal(503, error.Code);
        Assert.Contains("503", error.Message);
        Assert.DoesNotContain("login required", error.Message);
    }

    [Fact]
    public async Task HttpForbiddenWithHtmlBodyThrowsSessionExpired()
    {
        const string response = "<html><body>Forbidden</body></html>";
        var client = CreateClient(new StubHttpMessageHandler(response, HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetDevicesAsync(_session, CancellationToken.None));

        Assert.Equal(403, error.Code);
        Assert.Contains("session may have expired", error.Message);
    }

    [Fact]
    public async Task FormValuesWithPlusArePercentEncodedAndDecodeAsPlus()
    {
        const string expectedData =
            "0L4aCjnr68anfQJZmLAT2c9w37ei1aYstHAicoEcJx0CyN1jpUJAqRw5cb+4b4VLsv5+4h7e+laO/BaNejBPtAAw2xmIosNh/6kL30yA3P3/JrewyF1H5pidwCN8QpU=";
        var handler = new StubHttpMessageHandler("""{"code":0,"result":{"list":[]}}""");
        var client = CreateClient(handler);

        await client.GetDevicesAsync(_session, CancellationToken.None);

        var body = handler.Body!;
        Assert.Contains("%2B", body);

        var values = ParseForm(body).ToDictionary(field => field.Key, field => field.Value);
        Assert.Equal(expectedData, values["data"]);
        Assert.Contains("+", values["data"]);
    }
    private static MiHomeCloudClient CreateClient(StubHttpMessageHandler handler)
        => new(new HttpClient(handler), () => _nonce);

    private static string DecryptRequestPayload(StubHttpMessageHandler handler)
    {
        var values = ParseForm(handler.Body!).ToDictionary(field => field.Key, field => field.Value);
        return DecryptValue(values["data"]);
    }

    private static string DecryptValue(string value)
    {
        var key = Convert.FromBase64String(SignedNonce(_ssecurity, _nonce));
        var plaintext = Rc4.Transform(key, Convert.FromBase64String(value), discardBytes: 1024);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static string EncryptResponse(string plaintext)
    {
        var key = Convert.FromBase64String(SignedNonce(_ssecurity, _nonce));
        var cipher = Rc4.Transform(key, Encoding.UTF8.GetBytes(plaintext), discardBytes: 1024);
        return Convert.ToBase64String(cipher);
    }

    private static string SignedNonce(string ssecurity, string nonce)
        => Convert.ToBase64String(SHA256.HashData(
            [.. Convert.FromBase64String(ssecurity), .. Convert.FromBase64String(nonce)]));

    private static IReadOnlyList<KeyValuePair<string, string>> ParseForm(string body)
        => body.Split('&')
            .Select(part =>
            {
                var separator = part.IndexOf('=');
                return new KeyValuePair<string, string>(
                    WebUtility.UrlDecode(part[..separator]),
                    WebUtility.UrlDecode(part[(separator + 1)..]));
            })
            .ToArray();

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        public string? Cookie { get; private set; }

        public string? AcceptEncoding { get; private set; }

        public string? ProtocolFlag { get; private set; }

        public string? EncryptAlgorithm { get; private set; }

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Cookie = ReadHeader(request, "Cookie");
            AcceptEncoding = ReadHeader(request, "Accept-Encoding");
            ProtocolFlag = ReadHeader(request, "x-xiaomi-protocal-flag-cli");
            EncryptAlgorithm = ReadHeader(request, "MIOT-ENCRYPT-ALGORITHM");
            ContentType = request.Content?.Headers.ContentType?.ToString();

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody),
            };
        }

        private static string? ReadHeader(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values)
                ? string.Join(", ", values)
                : null;
    }
}

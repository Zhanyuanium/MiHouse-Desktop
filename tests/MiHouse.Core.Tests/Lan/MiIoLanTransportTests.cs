using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Cloud;
using MiHouse.Core.Devices;
using MiHouse.Core.Lan;

namespace MiHouse.Core.Tests.Lan;

public sealed class MiIoLanTransportTests
{
    private static readonly byte[] _tokenBytes = MiIoLanTransport.ParseToken("0123456789abcdef");
    private static readonly LanEndpoint _endpoint = new("device-1", "127.0.0.1", "0123456789abcdef");

    [Fact]
    public void BuildHeaderPacksKnownRequest()
    {
        var header = MiIoLanTransport.BuildHeader(0x01020304, 0x11223344, 48);

        Assert.Equal("21310030", Convert.ToHexString(header[..4]));
        Assert.Equal("00000000", Convert.ToHexString(header[4..8]));
        Assert.Equal("01020304", Convert.ToHexString(header[8..12]));
        Assert.Equal("11223344", Convert.ToHexString(header[12..16]));
        Assert.Equal("00000000000000000000000000000000", Convert.ToHexString(header[16..32]));
    }

    [Fact]
    public void BuildHandshakePacketUsesDocumentedBytes()
    {
        var packet = MiIoLanTransport.BuildHandshakePacket();

        Assert.Equal("21310020" + new string('F', 56), Convert.ToHexString(packet));
    }

    [Fact]
    public void ParseHandshakeResponseReadsDeviceIdAndToken()
    {
        var reply = new byte[32];
        BinaryPrimitives.WriteUInt16BigEndian(reply, 0x2131);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), 32);
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8), 0x01020304);
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12), 0x55667788);
        _tokenBytes.CopyTo(reply.AsSpan(16));

        var (deviceId, stamp, token) = MiIoLanTransport.ParseHandshakeResponse(reply);

        Assert.Equal(0x01020304u, deviceId);
        Assert.Equal(0x55667788u, stamp);
        Assert.Equal(_tokenBytes, token);
    }

    [Fact]
    public async Task RequestUsesHandshakeTimestamp()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            HandshakeStamp = 0x11223344,
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1,"code":0,"value":1}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None);

        var stamp = BinaryPrimitives.ReadUInt32BigEndian(device.Packets[1].AsSpan(12));
        Assert.Equal(0x11223344u, stamp);
    }

    [Fact]
    public async Task RequestUsesCurrentTimeWhenHandshakeTimestampIsZero()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1,"code":0,"value":1}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var before = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None);
        var after = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var stamp = BinaryPrimitives.ReadUInt32BigEndian(device.Packets[1].AsSpan(12));
        Assert.InRange(stamp, before, after);
    }

    [Fact]
    public void RequestChecksumMatchesIndependentMd5()
    {
        var plaintext = Encoding.UTF8.GetBytes(
            """{"id":1,"method":"get_properties","params":[]}""");

        var packet = MiIoLanTransport.BuildRequestPacket(
            0x01020304,
            0x11223344,
            _tokenBytes,
            plaintext);

        var expected = MD5.HashData([.. packet[..16], .. _tokenBytes, .. packet[32..]]);

        Assert.Equal(expected, packet[16..32]);
    }

    [Fact]
    public void ChecksumMatchesFixedVector()
    {
        byte[] header =
        [
            0x21, 0x31, 0x00, 0x30, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x02, 0x03, 0x04, 0x11, 0x22, 0x33, 0x44,
        ];
        byte[] token =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        ];
        byte[] ciphertext =
        [
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
        ];

        var checksum = MiIoLanTransport.ComputeChecksum(header, token, ciphertext);

        Assert.Equal("AA9A0A0FD036FF039BD68F757CF2D78D", Convert.ToHexString(checksum));
    }

    [Fact]
    public void EncryptAndDecryptRoundTripWithFixedToken()
    {
        var plaintext = Encoding.UTF8.GetBytes("""{"id":1,"method":"get_properties"}""");

        var cipher = MiIoLanTransport.EncryptPayload(_tokenBytes, plaintext);

        Assert.Equal(0, cipher.Length % 16);
        Assert.NotEqual(plaintext, cipher);
        Assert.Equal(plaintext, MiIoLanTransport.DecryptPayload(_tokenBytes, cipher));
    }

    [Fact]
    public void HexAndAsciiTokensAreEquivalent()
    {
        var fromHex = MiIoLanTransport.ParseToken(Convert.ToHexString(_tokenBytes));
        var fromAscii = MiIoLanTransport.ParseToken("0123456789abcdef");

        Assert.Equal(_tokenBytes, fromHex);
        Assert.Equal(_tokenBytes, fromAscii);
    }

    [Fact]
    public async Task GetPropertiesAsyncSendsExpectedRequestJson()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$"""
                {"id":{{id}},"result":[
                {"did":"device-1","siid":2,"piid":1,"code":0,"value":true},
                {"did":"device-1","siid":3,"piid":1,"code":0,"value":5}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var values = await transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1), new MiPropertyKey(3, 1)],
            CancellationToken.None);

        var request = ParseRequest(device);
        Assert.Equal(1, request.GetProperty("id").GetInt32());
        Assert.Equal("get_properties", request.GetProperty("method").GetString());

        var parameters = request.GetProperty("params");
        Assert.Equal(2, parameters.GetArrayLength());
        Assert.Equal(0, device.RawRequests[0][^1]);
        Assert.Equal("device-1", parameters[0].GetProperty("did").GetString());
        Assert.Equal(2, parameters[0].GetProperty("siid").GetInt32());
        Assert.Equal(1, parameters[0].GetProperty("piid").GetInt32());

        Assert.Equal(2, values.Count);
        Assert.Equal(new MiPropertyKey(2, 1), values[0].Key);
        Assert.True(values[0].Value.GetBoolean());
        Assert.Equal(new MiPropertyKey(3, 1), values[1].Key);
        Assert.Equal(5, values[1].Value.GetInt32());
    }

    [Fact]
    public async Task SetPropertyAsyncSendsExpectedRequestJson()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1,"code":0}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await transport.SetPropertyAsync(
            _endpoint,
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None);

        var request = ParseRequest(device);
        Assert.Equal("set_properties", request.GetProperty("method").GetString());

        var parameter = Assert.Single(request.GetProperty("params").EnumerateArray());
        Assert.Equal("device-1", parameter.GetProperty("did").GetString());
        Assert.Equal(2, parameter.GetProperty("siid").GetInt32());
        Assert.Equal(1, parameter.GetProperty("piid").GetInt32());
        Assert.True(parameter.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task InvokeActionAsyncSendsExpectedRequestJson()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"result":{"code":0,"out":[1,2]}}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var result = await transport.InvokeActionAsync(
            _endpoint,
            new MiActionKey(2, 1),
            [JsonSerializer.SerializeToElement(true), JsonSerializer.SerializeToElement(5)],
            CancellationToken.None);

        var request = ParseRequest(device);
        Assert.Equal("action", request.GetProperty("method").GetString());

        var parameters = request.GetProperty("params");
        Assert.Equal("device-1", parameters.GetProperty("did").GetString());
        Assert.Equal(2, parameters.GetProperty("siid").GetInt32());
        Assert.Equal(1, parameters.GetProperty("aiid").GetInt32());

        var input = parameters.GetProperty("in");
        Assert.Equal(2, input.GetArrayLength());
        Assert.True(input[0].GetBoolean());
        Assert.Equal(5, input[1].GetInt32());
        Assert.Equal("[1,2]", result.GetRawText());
    }

    [Fact]
    public async Task ActionReturnsEmptyArrayWhenOutputAbsent()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"result":{"code":0}}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var result = await transport.InvokeActionAsync(
            _endpoint,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None);

        Assert.Equal("[]", result.GetRawText());
    }

    [Fact]
    public async Task ActionResultCodeSurfacesAsDeviceError()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"result":{"code":-5001,"message":"busy"}}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => transport.InvokeActionAsync(
            _endpoint,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(-5001, error.Code);
        Assert.True(error.IsDeviceError);
        Assert.Equal("busy", error.Message);
    }

    [Fact]
    public async Task SetPropertyEntryCodeSurfacesAsDeviceError()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1,"code":-704053100}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => transport.SetPropertyAsync(
            _endpoint,
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(-704053100, error.Code);
        Assert.True(error.IsDeviceError);
    }

    [Fact]
    public async Task SetPropertyToleratesEntryCodeOne()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1,"code":1}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await transport.SetPropertyAsync(
            _endpoint,
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None);
    }

    [Fact]
    public async Task SetPropertyRequiresNonEmptyResult()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"result":[]}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => transport.SetPropertyAsync(
            _endpoint,
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(-1, error.Code);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task SetPropertyRequiresEntryCode()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1}]}
                """,
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => transport.SetPropertyAsync(
            _endpoint,
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(-1, error.Code);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task ActionMalformedResultThrowsOutcomeUnknown()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"result":[]}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanOutcomeUnknownException>(() => transport.InvokeActionAsync(
            _endpoint,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));
    }

    [Fact]
    public async Task ActionMissingResultCodeThrowsOutcomeUnknown()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"result":{}}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanOutcomeUnknownException>(() => transport.InvokeActionAsync(
            _endpoint,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));
    }

    [Fact]
    public async Task ActionTimeoutThrowsOutcomeUnknownWithoutRetry()
    {
        var device = new FakeDevice(_tokenBytes);
        device.RawResponseFactory = _ => throw new TimeoutException("no reply");
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanOutcomeUnknownException>(() => transport.InvokeActionAsync(
            _endpoint,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(2, device.Packets.Count);
    }

    [Fact]
    public async Task ResponseWithTrailingNulsParses()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""
                {"id":{{{id}}},"result":[{"did":"device-1","siid":2,"piid":1,"code":0,"value":1}]}
                """ + "\0\0",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var values = await transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None);

        Assert.Equal(1, Assert.Single(values).Value.GetInt32());
    }

    [Fact]
    public async Task TamperedChecksumThrowsLanUnavailable()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponsePacketMutator = packet =>
            {
                packet[16] ^= 0xFF;
                return packet;
            },
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanUnavailableException>(() => transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None));
    }

    [Fact]
    public async Task WrongDeviceIdThrowsLanUnavailable()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponsePacketMutator = packet =>
            {
                BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), 0x01020305);
                return packet;
            },
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanUnavailableException>(() => transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None));
    }

    [Fact]
    public async Task BadLengthThrowsLanUnavailable()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponsePacketMutator = packet =>
            {
                BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)(packet.Length + 1));
                return packet;
            },
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanUnavailableException>(() => transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None));
    }

    [Fact]
    public void LanEndpointToStringHidesToken()
    {
        var endpoint = new LanEndpoint("did", "192.168.1.2", "supersecret-token");

        var text = endpoint.ToString();

        Assert.Contains("token=***", text);
        Assert.DoesNotContain("supersecret-token", text);
    }

    [Fact]
    public async Task DeviceErrorSurfacesAsDeviceMiHomeException()
    {
        var device = new FakeDevice(_tokenBytes)
        {
            ResponseFactory = id => $$$"""{"id":{{{id}}},"error":{"code":-5001,"message":"busy"}}""",
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None));

        Assert.Equal(-5001, error.Code);
        Assert.True(error.IsDeviceError);
        Assert.Equal("busy", error.Message);
    }

    [Fact]
    public async Task UndecryptableResponseThrowsLanUnavailable()
    {
        var device = new FakeDevice(_tokenBytes);
        device.RawResponseFactory = _ =>
        {
            var header = MiIoLanTransport.BuildHeader(device.DeviceId, 0, 35);
            var response = new byte[35];
            header.CopyTo(response, 0);
            response[32] = 1;
            response[33] = 2;
            response[34] = 3;
            return response;
        };
        var transport = new MiIoLanTransport(device.SendAsync);

        await Assert.ThrowsAsync<LanUnavailableException>(() => transport.GetPropertiesAsync(
            _endpoint,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None));
    }

    private static JsonElement ParseRequest(FakeDevice device)
    {
        using var document = JsonDocument.Parse(Assert.Single(device.Requests));
        return document.RootElement.Clone();
    }

    private sealed class FakeDevice
    {
        private readonly byte[] _token;

        public FakeDevice(byte[] token, uint deviceId = 0x01020304)
        {
            _token = token;
            DeviceId = deviceId;
        }

        public uint DeviceId { get; }

        public uint HandshakeStamp { get; set; }

        public List<byte[]> Packets { get; } = [];

        public List<byte[]> RawRequests { get; } = [];

        public List<string> Requests { get; } = [];

        public Func<int, string> ResponseFactory { get; set; } =
            static id => $$"""{"id":{{id}},"result":["ok"]}""";

        public Func<byte[], byte[]>? RawResponseFactory { get; set; }

        public Func<byte[], byte[]>? ResponsePacketMutator { get; set; }

        public Task<byte[]> SendAsync(byte[] packet, CancellationToken cancellationToken)
        {
            Packets.Add(packet);

            if (packet.Length == 32 && packet[3] == 32 && packet[4] == 0xFF)
            {
                return Task.FromResult(BuildHandshakeReply());
            }

            if (RawResponseFactory is not null)
            {
                return Task.FromResult(RawResponseFactory(packet));
            }

            var plaintext = MiIoLanTransport.DecryptPayload(_token, packet.AsSpan(32));
            RawRequests.Add(plaintext);
            var json = Encoding.UTF8.GetString(plaintext).TrimEnd('\0');
            Requests.Add(json);

            using var document = JsonDocument.Parse(json);
            var id = document.RootElement.GetProperty("id").GetInt32();
            var payload = MiIoLanTransport.EncryptPayload(
                _token,
                Encoding.UTF8.GetBytes(ResponseFactory(id)));

            var header = MiIoLanTransport.BuildHeader(DeviceId, 0, 32 + payload.Length);
            MD5.HashData([.. header[..16], .. _token, .. payload]).CopyTo(header.AsSpan(16));

            var response = new byte[header.Length + payload.Length];
            header.CopyTo(response, 0);
            payload.CopyTo(response, header.Length);
            return Task.FromResult(ResponsePacketMutator?.Invoke(response) ?? response);
        }

        private byte[] BuildHandshakeReply()
        {
            var reply = new byte[32];
            BinaryPrimitives.WriteUInt16BigEndian(reply, 0x2131);
            BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), 32);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8), DeviceId);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12), HandshakeStamp);
            _token.CopyTo(reply.AsSpan(16));
            return reply;
        }
    }
}

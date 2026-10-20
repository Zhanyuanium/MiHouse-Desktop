using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Cloud;
using MiHouse.Core.Devices;
using MiHouse.Core.Lan;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.Core.Tests.Devices;

public sealed class DeviceControllerTests
{
    private const string _token = "0123456789abcdef";
    private static readonly MiHomeSession _session = new("12345", "service-token", "MDEyMzQ1Njc4OWFiY2RlZg==", "cn");

    [Fact]
    public async Task GetSpecAsyncPrefersSpecTypeOverModel()
    {
        var urnSpec = CreateSpec("urn:device:from-urn", 2);
        var modelSpec = CreateSpec("urn:device:from-model", 2);
        var specs = new FakeSpecCatalog();
        specs.ByUrn[urnSpec.Type] = urnSpec;
        specs.ByModel["model.x"] = modelSpec;
        var controller = CreateController(specs);
        var device = new MiDevice("1", "model.x", "Device", null, true, SpecType: urnSpec.Type);

        var spec = await controller.GetSpecAsync(device, CancellationToken.None);

        Assert.Same(urnSpec, spec);
        Assert.Equal(new[] { urnSpec.Type }, specs.UrnRequests);
        Assert.Empty(specs.ModelRequests);
    }

    [Fact]
    public async Task GetSpecAsyncFallsBackToModelWhenSpecTypeReturnsNull()
    {
        var modelSpec = CreateSpec("urn:device:from-model", 2);
        var specs = new FakeSpecCatalog();
        specs.ByModel["model.x"] = modelSpec;
        var controller = CreateController(specs);
        var device = new MiDevice("1", "model.x", "Device", null, true, SpecType: "urn:device:missing");

        var spec = await controller.GetSpecAsync(device, CancellationToken.None);

        Assert.Same(modelSpec, spec);
        Assert.Equal(new[] { "urn:device:missing" }, specs.UrnRequests);
        Assert.Equal(new[] { "model.x" }, specs.ModelRequests);
    }

    [Fact]
    public async Task GetSpecAsyncFallsBackToModelWhenSpecTypeThrows()
    {
        var modelSpec = CreateSpec("urn:device:from-model", 2);
        var specs = new FakeSpecCatalog();
        specs.ByModel["model.x"] = modelSpec;
        specs.ThrowingUrns.Add("urn:device:broken");
        var controller = CreateController(specs);
        var device = new MiDevice("1", "model.x", "Device", null, true, SpecType: "urn:device:broken");

        var spec = await controller.GetSpecAsync(device, CancellationToken.None);

        Assert.Same(modelSpec, spec);
        Assert.Equal(new[] { "model.x" }, specs.ModelRequests);
    }

    [Fact]
    public async Task GetSpecAsyncReturnsNullWhenNeitherSourceResolves()
    {
        var controller = CreateController(new FakeSpecCatalog());
        var device = new MiDevice("1", "model.x", "Device", null, true, SpecType: "urn:device:missing");

        var spec = await controller.GetSpecAsync(device, CancellationToken.None);

        Assert.Null(spec);
    }

    [Fact]
    public async Task GetSpecAsyncFiltersServicesForSplitChannel()
    {
        var spec = CreateSpec("urn:device:switch", 2, 3, 4);
        var specs = new FakeSpecCatalog();
        specs.ByUrn[spec.Type] = spec;
        var controller = CreateController(specs);
        var device = new MiDevice("parent.s4", "model.x", "Channel 4", null, true, "parent", 4, spec.Type);

        var filtered = await controller.GetSpecAsync(device, CancellationToken.None);

        Assert.NotNull(filtered);
        Assert.Equal(spec.Type, filtered.Type);
        Assert.Equal(spec.Description, filtered.Description);
        Assert.Equal(4, Assert.Single(filtered.Services).Iid);
        Assert.Equal(3, spec.Services.Count);
    }

    [Fact]
    public async Task GetSpecAsyncReturnsAllServicesForNormalDevice()
    {
        var spec = CreateSpec("urn:device:switch", 2, 3, 4);
        var specs = new FakeSpecCatalog();
        specs.ByUrn[spec.Type] = spec;
        var controller = CreateController(specs);
        var device = new MiDevice("normal", "model.x", "Normal", null, true, SpecType: spec.Type);

        var resolved = await controller.GetSpecAsync(device, CancellationToken.None);

        Assert.Same(spec, resolved);
        Assert.Equal(3, resolved!.Services.Count);
    }

    [Theory]
    [InlineData(null, "device.s4")]
    [InlineData("2000420370", "2000420370")]
    public async Task ReadWriteInvokeUseControlDid(string? parentDid, string expectedDid)
    {
        var cloud = new FakeCloud();
        var controller = CreateController(new FakeSpecCatalog(), cloud);
        var device = new MiDevice(
            "device.s4",
            "model.x",
            "Channel 4",
            null,
            true,
            parentDid,
            parentDid is null ? null : 4);
        MiPropertyKey property = new(4, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(7))];
        cloud.InvokeResult = JsonSerializer.SerializeToElement("done");

        var value = await controller.ReadAsync(device, [property], CancellationToken.None);
        await controller.WriteAsync(device, property, JsonSerializer.SerializeToElement(true), CancellationToken.None);
        var output = await controller.InvokeAsync(device, new MiActionKey(4, 1), [], CancellationToken.None);

        var read = Assert.Single(cloud.Reads);
        Assert.Equal(expectedDid, read.Did);
        Assert.Same(_session, read.Session);
        Assert.Equal(7, value[property].GetInt32());

        var write = Assert.Single(cloud.Writes);
        Assert.Equal(expectedDid, write.Did);
        Assert.Equal(property, write.Property);
        Assert.True(write.Value.GetBoolean());

        var invoke = Assert.Single(cloud.Invokes);
        Assert.Equal(expectedDid, invoke.Did);
        Assert.Equal(new MiActionKey(4, 1), invoke.Action);
        Assert.Equal("done", output.GetString());
    }

    [Fact]
    public async Task ReadAsyncRejectsOtherChannelSiid()
    {
        var cloud = new FakeCloud();
        var controller = CreateController(new FakeSpecCatalog(), cloud);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.ReadAsync(SplitDevice(), [new MiPropertyKey(2, 1)], CancellationToken.None));

        Assert.Contains("4", error.Message);
        Assert.Empty(cloud.Reads);
    }

    [Fact]
    public async Task WriteAsyncRejectsOtherChannelSiid()
    {
        var cloud = new FakeCloud();
        var controller = CreateController(new FakeSpecCatalog(), cloud);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => controller.WriteAsync(
            SplitDevice(),
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Contains("4", error.Message);
        Assert.Empty(cloud.Writes);
    }

    [Fact]
    public async Task InvokeAsyncRejectsOtherChannelSiid()
    {
        var cloud = new FakeCloud();
        var controller = CreateController(new FakeSpecCatalog(), cloud);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => controller.InvokeAsync(
            SplitDevice(),
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Contains("4", error.Message);
        Assert.Empty(cloud.Invokes);
    }

    [Fact]
    public async Task ReadAsyncPropagatesMiHomeExceptionCodeForSplitChannel()
    {
        var cloud = new FakeCloud { Failure = new MiHomeException(-704053100, "rejected") };
        var controller = CreateController(new FakeSpecCatalog(), cloud);

        var error = await Assert.ThrowsAsync<MiHomeException>(() =>
            controller.ReadAsync(SplitDevice(), [new MiPropertyKey(4, 1)], CancellationToken.None));

        Assert.Equal(-704053100, error.Code);
        Assert.Single(cloud.Reads);
    }

    [Fact]
    public async Task ReadWriteInvokePreferLanWhenIpAndTokenExist()
    {
        var cloud = new FakeCloud();
        var lan = new FakeLanTransport();
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        MiPropertyKey property = new(2, 1);
        lan.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(9))];
        lan.InvokeResult = JsonSerializer.SerializeToElement("lan");
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var values = await controller.ReadAsync(device, [property], CancellationToken.None);
        await controller.WriteAsync(
            device,
            property,
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None);
        var output = await controller.InvokeAsync(
            device,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None);

        Assert.Equal(9, values[property].GetInt32());
        Assert.Equal("lan", output.GetString());
        Assert.Single(lan.Reads);
        Assert.Single(lan.Writes);
        Assert.Single(lan.Invokes);
        Assert.Empty(cloud.Reads);
        Assert.Empty(cloud.Writes);
        Assert.Empty(cloud.Invokes);
        Assert.Equal(device.Did, lan.Reads[0].Endpoint.Did);
        Assert.Equal("192.168.1.50", lan.Reads[0].Endpoint.Ip);
        Assert.Equal(_token, lan.Reads[0].Endpoint.Token);
    }

    [Fact]
    public async Task ReadAsyncFallsBackToCloudWhenLanUnavailable()
    {
        var cloud = new FakeCloud();
        MiPropertyKey property = new(2, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(7))];
        var lan = new FakeLanTransport { Failure = new LanUnavailableException("offline") };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var values = await controller.ReadAsync(device, [property], CancellationToken.None);

        Assert.Equal(7, values[property].GetInt32());
        Assert.Single(lan.Reads);
        Assert.Single(cloud.Reads);
    }

    [Fact]
    public async Task ReadAsyncPropagatesDeviceErrorFromLanWithoutCloudFallback()
    {
        var cloud = new FakeCloud();
        var lan = new FakeLanTransport
        {
            Failure = new MiHomeException(-704053100, "rejected", isDeviceError: true),
        };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => controller.ReadAsync(
            device,
            [new MiPropertyKey(2, 1)],
            CancellationToken.None));

        Assert.Equal(-704053100, error.Code);
        Assert.True(error.IsDeviceError);
        Assert.Single(lan.Reads);
        Assert.Empty(cloud.Reads);
    }

    [Fact]
    public async Task WriteAsyncPropagatesDeviceErrorFromLanWithoutCloudFallback()
    {
        var cloud = new FakeCloud();
        var lan = new FakeLanTransport
        {
            Failure = new MiHomeException(-704053100, "rejected", isDeviceError: true),
        };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => controller.WriteAsync(
            device,
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(-704053100, error.Code);
        Assert.True(error.IsDeviceError);
        Assert.Single(lan.Writes);
        Assert.Empty(cloud.Writes);
    }

    [Fact]
    public async Task InvokeAsyncPropagatesDeviceErrorFromLanWithoutCloudFallback()
    {
        var cloud = new FakeCloud();
        var lan = new FakeLanTransport
        {
            Failure = new MiHomeException(-5001, "busy", isDeviceError: true),
        };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => controller.InvokeAsync(
            device,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(-5001, error.Code);
        Assert.True(error.IsDeviceError);
        Assert.Single(lan.Invokes);
        Assert.Empty(cloud.Invokes);
    }

    [Fact]
    public async Task InvokeAsyncPropagatesOutcomeUnknownWithoutCloudFallback()
    {
        var cloud = new FakeCloud();
        var lan = new FakeLanTransport
        {
            Failure = new LanOutcomeUnknownException("outcome unknown"),
        };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        await Assert.ThrowsAsync<LanOutcomeUnknownException>(() => controller.InvokeAsync(
            device,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Single(lan.Invokes);
        Assert.Empty(cloud.Invokes);
    }

    [Fact]
    public async Task InvokeAsyncMalformedLanReplyDoesNotFallBackToCloud()
    {
        var cloud = new FakeCloud();
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var token = MiIoLanTransport.ParseToken(_token);
        var controller = new DeviceController(
            cloud,
            new FakeSpecCatalog(),
            _session,
            new MiIoLanTransport(MalformedActionSender(token)),
            secrets);

        await Assert.ThrowsAsync<LanOutcomeUnknownException>(() => controller.InvokeAsync(
            device,
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Empty(cloud.Invokes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsyncUsesCloudWhenLanCannotBeUsed(bool hasToken)
    {
        var cloud = new FakeCloud();
        MiPropertyKey property = new(2, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(1))];
        var lan = new FakeLanTransport();
        var secrets = new InMemoryDeviceSecretStore();
        var device = hasToken
            ? new MiDevice("device-1", "model.x", "Device", null, true, LocalIp: "192.168.1.50")
            : new MiDevice("device-1", "model.x", "Device", null, true);
        if (!hasToken)
        {
            secrets.SetToken(device.Did, _token);
        }

        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        await controller.ReadAsync(device, [property], CancellationToken.None);

        Assert.Empty(lan.Reads);
        Assert.Single(cloud.Reads);
    }

    [Fact]
    public async Task SplitDeviceUsesParentTokenAndControlDidOnLan()
    {
        var cloud = new FakeCloud();
        var lan = new FakeLanTransport();
        var secrets = new InMemoryDeviceSecretStore();
        var device = new MiDevice(
            "child.s4",
            "model.x",
            "Child",
            null,
            true,
            "parent-did",
            4,
            LocalIp: "192.168.1.50");
        secrets.SetToken("parent-did", _token);
        MiPropertyKey property = new(4, 1);
        lan.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(3))];
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var values = await controller.ReadAsync(device, [property], CancellationToken.None);

        var call = Assert.Single(lan.Reads);
        Assert.Equal("parent-did", call.Endpoint.Did);
        Assert.Equal("192.168.1.50", call.Endpoint.Ip);
        Assert.Equal(_token, call.Endpoint.Token);
        Assert.Equal(4, Assert.Single(call.Properties).Siid);
        Assert.Equal(3, values[property].GetInt32());
        Assert.Empty(cloud.Reads);
    }

    [Fact]
    public async Task ReadAsyncSkipsLanForPublicLocalIp()
    {
        var cloud = new FakeCloud();
        MiPropertyKey property = new(2, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(7))];
        var lan = new FakeLanTransport();
        var secrets = new InMemoryDeviceSecretStore();
        var device = new MiDevice(
            "device-1",
            "model.x",
            "Device",
            null,
            true,
            LocalIp: "111.196.128.224");
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(new FakeSpecCatalog(), cloud, lan, secrets);

        var values = await controller.ReadAsync(device, [property], CancellationToken.None);

        Assert.Equal(7, values[property].GetInt32());
        Assert.Empty(lan.Reads);
        Assert.Single(cloud.Reads);
    }

    [Fact]
    public async Task LanFailureBlocksFurtherAttemptsForFiveMinutes()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var cloud = new FakeCloud();
        MiPropertyKey property = new(2, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(1))];
        var lan = new FakeLanTransport { Failure = new LanUnavailableException("offline") };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(
            new FakeSpecCatalog(),
            cloud,
            lan,
            secrets,
            clock: () => now);

        await controller.ReadAsync(device, [property], CancellationToken.None);
        await controller.ReadAsync(device, [property], CancellationToken.None);

        Assert.Single(lan.Reads);
        Assert.Equal(2, cloud.Reads.Count);
    }

    [Fact]
    public async Task LanRetriedAfterBlockExpires()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var cloud = new FakeCloud();
        MiPropertyKey property = new(2, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(1))];
        var lan = new FakeLanTransport { Failure = new LanUnavailableException("offline") };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(
            new FakeSpecCatalog(),
            cloud,
            lan,
            secrets,
            clock: () => now);

        await controller.ReadAsync(device, [property], CancellationToken.None);
        now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
        await controller.ReadAsync(device, [property], CancellationToken.None);

        Assert.Equal(2, lan.Reads.Count);
        Assert.Equal(2, cloud.Reads.Count);
    }

    [Fact]
    public async Task LanSuccessClearsExistingBlock()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        var cloud = new FakeCloud();
        MiPropertyKey property = new(2, 1);
        cloud.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(1))];
        var lan = new FakeLanTransport { Failure = new LanUnavailableException("offline") };
        var secrets = new InMemoryDeviceSecretStore();
        var device = LanDevice();
        secrets.SetToken(device.Did, _token);
        var controller = CreateController(
            new FakeSpecCatalog(),
            cloud,
            lan,
            secrets,
            clock: () => now);

        await controller.ReadAsync(device, [property], CancellationToken.None);

        now += TimeSpan.FromMinutes(6);
        lan.Failure = null;
        lan.ReadResult = [new MiPropertyValue(property, JsonSerializer.SerializeToElement(9))];
        await controller.ReadAsync(device, [property], CancellationToken.None);

        now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000) + TimeSpan.FromMinutes(1);
        await controller.ReadAsync(device, [property], CancellationToken.None);

        Assert.Equal(3, lan.Reads.Count);
        Assert.Single(cloud.Reads);
    }

    [Fact]
    public async Task ReadAsyncReturnsEmptyDictionaryWithoutCloudCall()
    {
        var cloud = new FakeCloud();
        var controller = CreateController(new FakeSpecCatalog(), cloud);
        var device = new MiDevice("1", "model.x", "Device", null, true);

        var values = await controller.ReadAsync(device, [], CancellationToken.None);

        Assert.Empty(values);
        Assert.Empty(cloud.Reads);
    }

    [Fact]
    public async Task ReadAsyncCollapsesDuplicateKeysLastWins()
    {
        var cloud = new FakeCloud();
        var controller = CreateController(new FakeSpecCatalog(), cloud);
        var device = new MiDevice("1", "model.x", "Device", null, true);
        MiPropertyKey duplicate = new(2, 1);
        MiPropertyKey other = new(3, 1);
        cloud.ReadResult =
        [
            new MiPropertyValue(duplicate, JsonSerializer.SerializeToElement(1)),
            new MiPropertyValue(duplicate, JsonSerializer.SerializeToElement(2)),
            new MiPropertyValue(other, JsonSerializer.SerializeToElement(true)),
        ];

        var values = await controller.ReadAsync(device, [duplicate, other], CancellationToken.None);

        Assert.Equal(2, values.Count);
        Assert.Equal(2, values[duplicate].GetInt32());
        Assert.True(values[other].GetBoolean());
    }

    private static DeviceController CreateController(
        FakeSpecCatalog specs,
        FakeCloud? cloud = null,
        FakeLanTransport? lan = null,
        IDeviceSecretStore? secrets = null,
        Func<DateTimeOffset>? clock = null)
        => new(cloud ?? new FakeCloud(), specs, _session, lan, secrets, clock);

    private static MiDevice SplitDevice()
        => new("device.s4", "model.x", "Channel 4", null, true, "2000420370", 4);

    private static MiDevice LanDevice()
        => new("device-1", "model.x", "Device", null, true, LocalIp: "192.168.1.50");

    private static Func<byte[], CancellationToken, Task<byte[]>> MalformedActionSender(byte[] token)
        => (packet, _) =>
        {
            if (packet.Length == 32 && packet[3] == 32 && packet[4] == 0xFF)
            {
                var reply = new byte[32];
                BinaryPrimitives.WriteUInt16BigEndian(reply, 0x2131);
                BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(2), 32);
                BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(8), 0x01020304);
                token.CopyTo(reply.AsSpan(16));
                return Task.FromResult(reply);
            }

            var request = MiIoLanTransport.DecryptPayload(token, packet.AsSpan(32));
            using var document = JsonDocument.Parse(request.AsMemory(0, request.Length - 1));
            var id = document.RootElement.GetProperty("id").GetInt32();

            var payload = MiIoLanTransport.EncryptPayload(
                token,
                Encoding.UTF8.GetBytes($$$"""{"id":{{{id}}},"result":[]}"""));
            var header = MiIoLanTransport.BuildHeader(0x01020304, 0, 32 + payload.Length);
            MiIoLanTransport.ComputeChecksum(header.AsSpan(0, 16), token, payload)
                .CopyTo(header.AsSpan(16));

            var response = new byte[header.Length + payload.Length];
            header.CopyTo(response, 0);
            payload.CopyTo(response, header.Length);
            return Task.FromResult(response);
        };

    private static SpecInstance CreateSpec(string type, params int[] serviceIds)
        => new(
            type,
            "Description",
            serviceIds
                .Select(id => new SpecService(id, $"urn:service:{id}", $"Service {id}", [], []))
                .ToArray());

    private sealed class FakeSpecCatalog : ISpecCatalog
    {
        public Dictionary<string, SpecInstance?> ByUrn { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SpecInstance?> ByModel { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ThrowingUrns { get; } = new(StringComparer.Ordinal);

        public List<string> UrnRequests { get; } = [];

        public List<string> ModelRequests { get; } = [];

        public Task<SpecInstance?> GetAsync(string urn, CancellationToken cancellationToken)
        {
            UrnRequests.Add(urn);
            if (ThrowingUrns.Contains(urn))
            {
                throw new MiSpecException("spec unavailable");
            }

            return Task.FromResult(ByUrn.GetValueOrDefault(urn));
        }

        public Task<SpecInstance?> GetForModelAsync(string model, CancellationToken cancellationToken)
        {
            ModelRequests.Add(model);
            return Task.FromResult(ByModel.GetValueOrDefault(model));
        }
    }

    private sealed class FakeCloud : IMiHomeCloudClient
    {
        public List<ReadCall> Reads { get; } = [];

        public List<WriteCall> Writes { get; } = [];

        public List<InvokeCall> Invokes { get; } = [];

        public IReadOnlyList<MiPropertyValue> ReadResult { get; set; } = [];

        public Exception? Failure { get; set; }

        public JsonElement InvokeResult { get; set; }

        public Task<IReadOnlyList<MiHome>> GetHomesAsync(
            MiHomeSession session,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MiDevice>> GetDevicesAsync(
            MiHomeSession session,
            MiHome home,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MiScene>> GetScenesAsync(
            MiHomeSession session,
            MiHome home,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RunSceneAsync(
            MiHomeSession session,
            MiHome home,
            string sceneId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
            MiHomeSession session,
            string did,
            IReadOnlyList<MiPropertyKey> properties,
            CancellationToken cancellationToken)
        {
            Reads.Add(new ReadCall(session, did, properties));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(ReadResult);
        }

        public Task SetPropertyAsync(
            MiHomeSession session,
            string did,
            MiPropertyKey property,
            JsonElement value,
            CancellationToken cancellationToken)
        {
            Writes.Add(new WriteCall(session, did, property, value));
            return Task.CompletedTask;
        }

        public Task<JsonElement> InvokeActionAsync(
            MiHomeSession session,
            string did,
            MiActionKey action,
            IReadOnlyList<JsonElement> input,
            CancellationToken cancellationToken)
        {
            Invokes.Add(new InvokeCall(session, did, action, input));
            return Task.FromResult(InvokeResult);
        }
    }

    private sealed class FakeLanTransport : ILanTransport
    {
        public List<LanReadCall> Reads { get; } = [];

        public List<LanWriteCall> Writes { get; } = [];

        public List<LanInvokeCall> Invokes { get; } = [];

        public IReadOnlyList<MiPropertyValue> ReadResult { get; set; } = [];

        public JsonElement InvokeResult { get; set; }

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
            LanEndpoint endpoint,
            IReadOnlyList<MiPropertyKey> properties,
            CancellationToken cancellationToken)
        {
            Reads.Add(new LanReadCall(endpoint, properties));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(ReadResult);
        }

        public Task SetPropertyAsync(
            LanEndpoint endpoint,
            MiPropertyKey property,
            JsonElement value,
            CancellationToken cancellationToken)
        {
            Writes.Add(new LanWriteCall(endpoint, property, value));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }

        public Task<JsonElement> InvokeActionAsync(
            LanEndpoint endpoint,
            MiActionKey action,
            IReadOnlyList<JsonElement> input,
            CancellationToken cancellationToken)
        {
            Invokes.Add(new LanInvokeCall(endpoint, action, input));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(InvokeResult);
        }
    }

    private sealed record LanReadCall(LanEndpoint Endpoint, IReadOnlyList<MiPropertyKey> Properties);

    private sealed record LanWriteCall(LanEndpoint Endpoint, MiPropertyKey Property, JsonElement Value);

    private sealed record LanInvokeCall(
        LanEndpoint Endpoint,
        MiActionKey Action,
        IReadOnlyList<JsonElement> Input);

    private sealed record ReadCall(MiHomeSession Session, string Did, IReadOnlyList<MiPropertyKey> Properties);

    private sealed record WriteCall(
        MiHomeSession Session,
        string Did,
        MiPropertyKey Property,
        JsonElement Value);

    private sealed record InvokeCall(
        MiHomeSession Session,
        string Did,
        MiActionKey Action,
        IReadOnlyList<JsonElement> Input);
}

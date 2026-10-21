using System.Net;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Spec;

namespace MiHouse.Core.Tests.Spec;

public sealed class SpecCatalogTests : IDisposable
{
    private const string _deviceType = "urn:miot-spec-v2:device:light:0000A001:test-one:1";
    private const string _modePropertyType = "urn:miot-spec-v2:property:mode:00000008:test-one:1";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "MiHouse.Core.Tests", Guid.NewGuid().ToString("N"));

    private const string _indexJson = """
        {
          "instances": [
            {
              "model": "test.light.one",
              "type": "urn:miot-spec-v2:device:light:0000A001:test-one:1",
              "status": "released",
              "version": 1
            },
            {
              "model": "test.other.two",
              "type": "urn:miot-spec-v2:device:outlet:0000A002:test-two:1",
              "status": "released",
              "version": 1
            }
          ]
        }
        """;

    private const string _specJson = """
        {
          "type": "urn:miot-spec-v2:device:light:0000A001:test-one:1",
          "description": "Test Light",
          "services": [
            {
              "iid": 2,
              "type": "urn:miot-spec-v2:service:light:00007802:test-one:1",
              "description": "Light",
              "comment": "灯光",
              "properties": [
                {
                  "iid": 1,
                  "type": "urn:miot-spec-v2:property:on:00000006:test-one:1",
                  "description": "Switch Status",
                  "comment": "   ",
                  "format": "bool",
                  "access": ["read", "write", "notify"]
                },
                {
                  "iid": 2,
                  "type": "urn:miot-spec-v2:property:mode:00000008:test-one:1",
                  "description": "Mode",
                  "comment": "模式",
                  "format": "uint8",
                  "access": ["read", "write"],
                  "value-list": [
                    { "value": 1, "description": "Red" },
                    { "value": "2", "description": "Blue" }
                  ]
                },
                {
                  "iid": 3,
                  "type": "urn:miot-spec-v2:property:temperature:00000007:test-one:1",
                  "format": "float",
                  "value-range": ["2000", 6500.5, 1]
                }
              ],
              "actions": [
                {
                  "iid": 1,
                  "type": "urn:miot-spec-v2:action:toggle:00002811:test-one:1",
                  "description": "Toggle",
                  "in": [
                    {
                      "iid": 1,
                      "type": "urn:miot-spec-v2:property:brightness:0000000D:test-one:1",
                      "description": "Brightness",
                      "format": "uint8",
                      "value-range": [1, 100, 1]
                    }
                  ]
                },
                {
                  "iid": 2,
                  "type": "urn:miot-spec-v2:action:set-mode:00002812:test-one:1",
                  "description": "Set Mode",
                  "in": [2]
                }
              ]
            },
            {
              "iid": 3,
              "type": "urn:miot-spec-v2:service:battery:00007803:test-one:1"
            }
          ]
        }
        """;

    [Fact]
    public async Task GetForModelAsync_ResolvesSpecFromModelIndex()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var spec = await catalog.GetForModelAsync("test.light.one", CancellationToken.None);

        var instance = Assert.IsType<SpecInstance>(spec);
        Assert.Equal(_deviceType, instance.Type);
        Assert.Equal("Test Light", instance.Description);
        Assert.Equal(2, instance.Services.Count);

        var light = instance.Services[0];
        Assert.Equal(2, light.Iid);
        Assert.Equal("Light", light.Description);
        Assert.Equal(3, light.Properties.Count);

        var power = light.Properties[0];
        Assert.Equal(1, power.Iid);
        Assert.Equal("bool", power.Format);
        Assert.True(power.Readable);
        Assert.True(power.Writable);
        Assert.True(power.Notifiable);
        Assert.Null(power.Values);
        Assert.Null(power.Range);

        var mode = light.Properties[1];
        Assert.Equal(2, mode.Iid);
        Assert.Equal(_modePropertyType, mode.Type);
        Assert.Equal("Mode", mode.Description);
        Assert.Equal("uint8", mode.Format);
        Assert.True(mode.Readable);
        Assert.True(mode.Writable);
        Assert.False(mode.Notifiable);
        Assert.Equal(
            new List<SpecValueList> { new(1, "Red"), new(2, "Blue") },
            mode.Values);

        var temperature = light.Properties[2];
        Assert.Equal(3, temperature.Iid);
        Assert.Equal(string.Empty, temperature.Description);
        Assert.Equal("float", temperature.Format);
        Assert.False(temperature.Readable);
        Assert.False(temperature.Writable);
        Assert.False(temperature.Notifiable);
        Assert.Equal(new SpecRange(2000, 6500.5, 1), temperature.Range);

        var toggle = light.Actions[0];
        Assert.Equal(1, toggle.Iid);
        Assert.Equal("Toggle", toggle.Description);
        var brightness = Assert.Single(toggle.Inputs);
        Assert.Equal(1, brightness.Iid);
        Assert.Equal("Brightness", brightness.Description);
        Assert.Equal("uint8", brightness.Format);
        Assert.Equal(new SpecRange(1, 100, 1), brightness.Range);

        var setMode = light.Actions[1];
        var modeInput = Assert.Single(setMode.Inputs);
        Assert.Equal(2, modeInput.Iid);
        Assert.Equal(_modePropertyType, modeInput.Type);

        var battery = instance.Services[1];
        Assert.Equal(3, battery.Iid);
        Assert.Equal(string.Empty, battery.Description);
        Assert.Empty(battery.Properties);
        Assert.Empty(battery.Actions);

        Assert.Equal(1, handler.CountRequests("/instances"));
        Assert.Equal(1, handler.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetForModelAsync_ParsesCommentsAndDisplayNames()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var spec = await catalog.GetForModelAsync("test.light.one", CancellationToken.None);

        var instance = Assert.IsType<SpecInstance>(spec);

        var light = instance.Services[0];
        Assert.Equal("Light", light.Description);
        Assert.Equal("灯光", light.Comment);
        Assert.Equal("灯光", light.DisplayName);

        var power = light.Properties[0];
        Assert.Equal("Switch Status", power.Description);
        Assert.Null(power.Comment);
        Assert.Equal("Switch Status", power.DisplayName);

        var mode = light.Properties[1];
        Assert.Equal("Mode", mode.Description);
        Assert.Equal("模式", mode.Comment);
        Assert.Equal("模式", mode.DisplayName);

        var temperature = light.Properties[2];
        Assert.Null(temperature.Comment);
        Assert.Equal(string.Empty, temperature.DisplayName);

        var battery = instance.Services[1];
        Assert.Null(battery.Comment);
        Assert.Equal(string.Empty, battery.DisplayName);
    }

    [Fact]
    public async Task GetForModelAsync_UnknownModel_ReturnsNullWithoutFetchingSpec()
    {
        var handler = new StubHandler(_ => JsonResponse(_indexJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var spec = await catalog.GetForModelAsync("unknown.model", CancellationToken.None);

        Assert.Null(spec);
        Assert.Equal(1, handler.CountRequests("/instances"));
        Assert.Equal(0, handler.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetForModelAsync_IndexFetchedOnceAcrossCalls()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        await catalog.GetForModelAsync("test.light.one", CancellationToken.None);
        await catalog.GetForModelAsync("test.other.two", CancellationToken.None);

        Assert.Equal(1, handler.CountRequests("/instances"));
        Assert.Equal(2, handler.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetAsync_KnownUrn_ResolvesAndCaches()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var first = await catalog.GetAsync(_deviceType, CancellationToken.None);
        var second = await catalog.GetAsync(_deviceType, CancellationToken.None);

        var instance = Assert.IsType<SpecInstance>(first);
        Assert.Equal(_deviceType, instance.Type);
        Assert.Same(first, second);
        Assert.Equal(1, handler.CountRequests("/instance"));
        Assert.Equal(0, handler.CountRequests("/instances"));
    }

    [Fact]
    public async Task GetAsync_UnknownUrn_ReturnsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var spec = await catalog.GetAsync("urn:miot-spec-v2:device:light:0000A001:missing:1", CancellationToken.None);

        Assert.Null(spec);
        Assert.Equal(1, handler.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetAsync_MalformedBody_ThrowsMiSpecException()
    {
        var handler = new StubHandler(_ => JsonResponse("""{ "type": "urn:miot-spec-v2:device:light:0000A001:test-one:1", "services": ["""));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetAsync(_deviceType, CancellationToken.None));

        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task GetAsync_AndGetForModelAsync_ShareCache()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var fromModel = await catalog.GetForModelAsync("test.light.one", CancellationToken.None);
        var fromUrn = await catalog.GetAsync(_deviceType, CancellationToken.None);

        Assert.NotNull(fromModel);
        Assert.Same(fromModel, fromUrn);
        Assert.Equal(1, handler.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetForModelAsync_DuplicateModels_HighestVersionWinsWhenNewerComesFirst()
    {
        var index = BuildIndexJson(
            ("test.light.one", "urn:miot-spec-v2:device:light:0000A001:test-one:2", 2),
            ("test.light.one", "urn:miot-spec-v2:device:light:0000A001:test-one:1", 1));

        await AssertFetchedTypeAsync(index, "urn:miot-spec-v2:device:light:0000A001:test-one:2");
    }

    [Fact]
    public async Task GetForModelAsync_DuplicateModels_HighestVersionWinsWhenNewerComesLast()
    {
        var index = BuildIndexJson(
            ("test.light.one", "urn:miot-spec-v2:device:light:0000A001:test-one:1", 1),
            ("test.light.one", "urn:miot-spec-v2:device:light:0000A001:test-one:3", 3));

        await AssertFetchedTypeAsync(index, "urn:miot-spec-v2:device:light:0000A001:test-one:3");
    }

    [Fact]
    public async Task GetForModelAsync_DuplicateModels_MissingVersionTreatedAsZero()
    {
        var index = BuildIndexJson(
            ("test.light.one", "urn:miot-spec-v2:device:light:0000A001:test-one:1", 1),
            ("test.light.one", "urn:miot-spec-v2:device:light:0000A001:test-one:0", null));

        await AssertFetchedTypeAsync(index, "urn:miot-spec-v2:device:light:0000A001:test-one:1");
    }

    [Fact]
    public async Task GetForModelAsync_MalformedSpecJson_ThrowsMiSpecException()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse("""{ "type": "urn:miot-spec-v2:device:light:0000A001:test-one:1", "services": ["""));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetForModelAsync("test.light.one", CancellationToken.None));

        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task GetForModelAsync_EmptyIndexObject_ThrowsMiSpecException()
    {
        var diskCache = new SpecDiskCache(_directory);
        var handler = new StubHandler(_ => JsonResponse("{}"));
        var catalog = new SpecCatalog(new HttpClient(handler), diskCache);

        await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetForModelAsync("test.light.one", CancellationToken.None));

        Assert.Null(diskCache.TryRead("instances?status=released"));
    }

    [Fact]
    public async Task GetAsync_EmptySpecObject_ThrowsMiSpecException()
    {
        var diskCache = new SpecDiskCache(_directory);
        var handler = new StubHandler(_ => JsonResponse("{}"));
        var catalog = new SpecCatalog(new HttpClient(handler), diskCache);

        await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetAsync(_deviceType, CancellationToken.None));

        Assert.Null(diskCache.TryRead(_deviceType));
    }

    [Fact]
    public async Task GetAsync_SpecWithNullService_ThrowsMiSpecException()
    {
        var handler = new StubHandler(_ => JsonResponse(
            """{"type":"urn:miot-spec-v2:device:light:0000A001:test-one:1","services":[null]}"""));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetAsync(_deviceType, CancellationToken.None));

        Assert.Contains("empty service", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"type":"urn:miot-spec-v2:device:light:0000A001:test-one:1"}""")]
    [InlineData("""{"services":[]}""")]
    [InlineData("""{"type":"urn:miot-spec-v2:device:light:0000A001:test-one:1","services":null}""")]
    public async Task GetAsync_SpecMissingRequiredFields_ThrowsMiSpecException(string json)
    {
        var handler = new StubHandler(_ => JsonResponse(json));
        var catalog = new SpecCatalog(new HttpClient(handler));

        await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetAsync(_deviceType, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_StructurallyInvalidCachedSpec_IsMissAndReplacedByNetwork()
    {
        var diskCache = new SpecDiskCache(_directory);
        diskCache.Write(_deviceType, "{}");
        var handler = new StubHandler(_ => JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler), diskCache);

        var spec = await catalog.GetAsync(_deviceType, CancellationToken.None);

        var instance = Assert.IsType<SpecInstance>(spec);
        Assert.Equal(_deviceType, instance.Type);
        Assert.Equal(1, handler.CountRequests("/instance"));
        Assert.Equal(_specJson, diskCache.TryRead(_deviceType));
    }

    [Fact]
    public async Task GetForModelAsync_StructurallyInvalidCachedIndex_IsMissAndReplacedByNetwork()
    {
        var diskCache = new SpecDiskCache(_directory);
        diskCache.Write("instances?status=released", "{}");
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler), diskCache);

        var spec = await catalog.GetForModelAsync("test.light.one", CancellationToken.None);

        Assert.IsType<SpecInstance>(spec);
        Assert.Equal(1, handler.CountRequests("/instances"));
        Assert.Equal(_indexJson, diskCache.TryRead("instances?status=released"));
    }

    [Fact]
    public async Task GetAsync_StructurallyInvalidNetworkSpec_IsNotCached()
    {
        var diskCache = new SpecDiskCache(_directory);
        var handler = new StubHandler(_ => JsonResponse("{}"));
        var catalog = new SpecCatalog(new HttpClient(handler), diskCache);

        await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetAsync(_deviceType, CancellationToken.None));

        Assert.Null(diskCache.TryRead(_deviceType));
    }

    [Fact]
    public async Task GetForModelAsync_MissingSpec_ReturnsNull()
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var spec = await catalog.GetForModelAsync("test.light.one", CancellationToken.None);

        Assert.Null(spec);
        Assert.Equal(1, handler.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetForModelAsync_WithDiskCache_WritesFetchedSpec()
    {
        var diskCache = new SpecDiskCache(_directory);
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler), diskCache);

        await catalog.GetForModelAsync("test.light.one", CancellationToken.None);

        Assert.Equal(_specJson, diskCache.TryRead(_deviceType));
    }

    [Fact]
    public async Task GetForModelAsync_SecondCatalog_ResolvesFromDiskWhenNetworkFails()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateOnlineCatalog(now);
        await first.GetForModelAsync("test.light.one", CancellationToken.None);

        var offline = new StubHandler(_ => throw new HttpRequestException("offline"));
        var second = new SpecCatalog(
            new HttpClient(offline),
            new SpecDiskCache(_directory),
            new StubTimeProvider(now));

        var spec = await second.GetForModelAsync("test.light.one", CancellationToken.None);

        var instance = Assert.IsType<SpecInstance>(spec);
        Assert.Equal(_deviceType, instance.Type);
        Assert.Equal("Test Light", instance.Description);
        Assert.Equal(0, offline.CountRequests("/instances"));
        Assert.Equal(0, offline.CountRequests("/instance"));
    }

    [Fact]
    public async Task GetForModelAsync_ExpiredIndexCache_RefetchesIndex()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateOnlineCatalog(now);
        await first.GetForModelAsync("test.light.one", CancellationToken.None);

        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        var second = new SpecCatalog(
            new HttpClient(handler),
            new SpecDiskCache(_directory),
            new StubTimeProvider(now.AddHours(25)));

        await second.GetForModelAsync("test.light.one", CancellationToken.None);

        Assert.Equal(1, handler.CountRequests("/instances"));
    }

    [Fact]
    public async Task GetForModelAsync_ExpiredIndexCacheAndNetworkFailure_UsesStaleIndex()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateOnlineCatalog(now);
        await first.GetForModelAsync("test.light.one", CancellationToken.None);

        var offline = new StubHandler(_ => throw new HttpRequestException("offline"));
        var second = new SpecCatalog(
            new HttpClient(offline),
            new SpecDiskCache(_directory),
            new StubTimeProvider(now.AddHours(25)));

        var spec = await second.GetForModelAsync("test.light.one", CancellationToken.None);

        var instance = Assert.IsType<SpecInstance>(spec);
        Assert.Equal(_deviceType, instance.Type);
        Assert.Equal(1, offline.CountRequests("/instances"));
    }

    [Fact]
    public async Task GetForModelAsync_StalledIndexBody_FailsWithinRequestTimeout()
    {
        var handler = new StubHandler(_ => StalledResponse());
        var catalog = new SpecCatalog(
            new HttpClient(handler),
            requestTimeout: TimeSpan.FromMilliseconds(200));

        var exception = await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetForModelAsync("test.light.one", CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task GetAsync_StalledInstanceBody_FailsWithinRequestTimeout()
    {
        var handler = new StubHandler(_ => StalledResponse());
        var catalog = new SpecCatalog(
            new HttpClient(handler),
            requestTimeout: TimeSpan.FromMilliseconds(200));

        var exception = await Assert.ThrowsAsync<MiSpecException>(
            () => catalog.GetAsync(_deviceType, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    private static HttpResponseMessage StalledResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream()),
        };
    }

    private SpecCatalog CreateOnlineCatalog(DateTimeOffset now)
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(_indexJson)
            : JsonResponse(_specJson));
        return new SpecCatalog(
            new HttpClient(handler),
            new SpecDiskCache(_directory),
            new StubTimeProvider(now));
    }

    private static async Task AssertFetchedTypeAsync(string indexJson, string expectedType)
    {
        var handler = new StubHandler(request => IsIndexRequest(request)
            ? JsonResponse(indexJson)
            : JsonResponse(_specJson));
        var catalog = new SpecCatalog(new HttpClient(handler));

        var spec = await catalog.GetForModelAsync("test.light.one", CancellationToken.None);

        Assert.NotNull(spec);
        var specRequest = Assert.Single(handler.Requests("/instance"));
        Assert.Equal(expectedType, Uri.UnescapeDataString(specRequest.Query["?type=".Length..]));
    }

    private static string BuildIndexJson(params (string Model, string Type, int? Version)[] entries)
    {
        var items = entries.Select(entry => entry.Version is null
            ? $$"""{ "model": "{{entry.Model}}", "type": "{{entry.Type}}" }"""
            : $$"""{ "model": "{{entry.Model}}", "type": "{{entry.Type}}", "version": {{entry.Version.Value}} }""");

        return $$"""{ "instances": [{{string.Join(", ", items)}}] }""";
    }

    private static bool IsIndexRequest(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/instances", StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private readonly List<Uri> _requests = [];
        private readonly Lock _sync = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int CountRequests(string pathSuffix)
        {
            lock (_sync)
            {
                return _requests.Count(uri => uri.AbsolutePath.EndsWith(pathSuffix, StringComparison.Ordinal));
            }
        }

        public IReadOnlyList<Uri> Requests(string pathSuffix)
        {
            lock (_sync)
            {
                return [.. _requests.Where(uri => uri.AbsolutePath.EndsWith(pathSuffix, StringComparison.Ordinal))];
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _requests.Add(request.RequestUri!);
            }

            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // Headers arrive, but the body never completes until the read token is cancelled.
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

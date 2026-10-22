using MiHouse.Core.Devices;
using MiHouse.Core.Session;

namespace MiHouse.Core.Tests.Devices;

public sealed class DeviceClassificationTests : IDisposable
{
    private const string _switchSpec =
        "urn:miot-spec-v2:device:switch:0000A003:test-switch:1:0000D010";

    private static readonly MiHomeSession _session =
        new("12345", "service-token", "MDEyMzQ1Njc4OWFiY2RlZg==", "cn");

    private static readonly IReadOnlyDictionary<int, SwitchSubclass> _catalog =
        new Dictionary<int, SwitchSubclass>
        {
            [7] = new(7, "吸顶灯", 12),
            [8] = new(8, "插座", 9),
        };

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mihouse-tests", Guid.NewGuid().ToString("N"));

    private static MiDevice ChannelTwo => new(
        "parent-1.s2", "m.switch", "Switch 1", null, true, "parent-1", 2, _switchSpec);

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(null, 1)]
    public void MemberIndex_UsesChannelIndexOrStandaloneSlot(int? channelSiid, int expected)
    {
        var device = new MiDevice("d", "m", "D", null, true, ChannelSiid: channelSiid);

        Assert.Equal(expected, DeviceClassification.MemberIndex(device));
    }

    [Fact]
    public void Apply_ResolvesCategoryFromCatalogEntry()
    {
        var applied = DeviceClassification.Apply(ChannelTwo, 7, _catalog);

        Assert.Equal(7, applied.SubclassId);
        Assert.Equal("light", applied.SubclassCategory);
        Assert.Equal(ChannelTwo.ParentDid, applied.ParentDid);
        Assert.Equal(ChannelTwo.ChannelSiid, applied.ChannelSiid);
    }

    [Fact]
    public void Apply_KeepsUnclassifiedCategoryWhenCatalogMissesTheId()
    {
        var applied = DeviceClassification.Apply(ChannelTwo, 5, _catalog);

        Assert.Equal(5, applied.SubclassId);
        Assert.Null(applied.SubclassCategory);
    }

    [Fact]
    public void Apply_WithNoIdClearsTheCategory()
    {
        var device = ChannelTwo with { SubclassId = 7, SubclassCategory = "light" };

        var applied = DeviceClassification.Apply(device, null, _catalog);

        Assert.Null(applied.SubclassId);
        Assert.Null(applied.SubclassCategory);
    }

    [Fact]
    public async Task EnrichAsync_ClassifiesSplitChannelsAndStandaloneSwitch()
    {
        var cloud = new FakeCloud
        {
            Catalog =
            [
                new SwitchSubclass(2, "壁灯", 12),
                new SwitchSubclass(3, "落地灯", 12),
                new SwitchSubclass(5, "吊灯", 12),
            ],
        };
        cloud.Memberships["parent-1"] = new Dictionary<int, int> { [1] = 2, [2] = 5 };
        MiDevice parent = new("parent-1", "m.switch", "Parent", null, true, SpecType: _switchSpec);
        MiDevice channelThree = new(
            "parent-1.s3", "m.switch", "Switch 2", null, true, "parent-1", 3, _switchSpec);
        MiDevice standalone = new(
            "standalone-1", "m.switch", "Standalone", null, true, SpecType: _switchSpec, SubclassId: 3);
        MiDevice unknown = new("unknown-1", "m.switch", "Unknown", null, true, SpecType: _switchSpec);

        DeviceClassificationResult result = await CreateClassification(cloud).EnrichAsync(
            _session,
            [parent, ChannelTwo, channelThree, standalone, unknown],
            [],
            CancellationToken.None);

        Assert.True(result.IsComplete);
        Dictionary<string, MiDevice> byDid = result.Devices.ToDictionary(device => device.Did);
        Assert.Null(byDid["parent-1"].SubclassCategory);
        Assert.Equal("light", byDid["parent-1.s2"].SubclassCategory);
        Assert.Equal("light", byDid["parent-1.s3"].SubclassCategory);
        Assert.Equal("light", byDid["standalone-1"].SubclassCategory);
        Assert.Null(byDid["unknown-1"].SubclassCategory);
        Assert.Equal(new[] { "parent-1" }, Assert.Single(cloud.MembershipRequests));
    }

    [Fact]
    public async Task EnrichAsync_KeepsStandaloneClassificationWhenMembershipFails()
    {
        var cloud = new FakeCloud
        {
            Catalog = [new SwitchSubclass(3, "落地灯", 12)],
            MembershipFailure = new InvalidOperationException("membership unavailable"),
        };
        MiDevice standalone = new(
            "standalone-1", "m.switch", "Standalone", null, true, SpecType: _switchSpec, SubclassId: 3);

        DeviceClassificationResult result = await CreateClassification(cloud).EnrichAsync(
            _session,
            [ChannelTwo, standalone],
            [],
            CancellationToken.None);

        Assert.False(result.IsComplete);
        Dictionary<string, MiDevice> byDid = result.Devices.ToDictionary(device => device.Did);
        Assert.Null(byDid["parent-1.s2"].SubclassCategory);
        Assert.Equal("light", byDid["standalone-1"].SubclassCategory);
    }

    [Fact]
    public async Task EnrichAsync_RestoresPreviousCategoryWhenIncomplete()
    {
        var cloud = new FakeCloud
        {
            Catalog = [new SwitchSubclass(2, "壁灯", 12)],
            MembershipFailure = new InvalidOperationException("membership unavailable"),
        };
        MiDevice cached = ChannelTwo with { SubclassId = 2, SubclassCategory = "light" };

        DeviceClassificationResult result = await CreateClassification(cloud).EnrichAsync(
            _session,
            [ChannelTwo],
            [cached],
            CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Equal("light", Assert.Single(result.Devices).SubclassCategory);
    }

    [Fact]
    public async Task EnrichAsync_RestoresPreviousCategoryWhenCatalogFails()
    {
        var cloud = new FakeCloud { CatalogFailure = new InvalidOperationException("catalog unavailable") };
        MiDevice cached = ChannelTwo with { SubclassId = 2, SubclassCategory = "light" };

        DeviceClassificationResult result = await CreateClassification(cloud).EnrichAsync(
            _session,
            [ChannelTwo],
            [cached],
            CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Equal("light", Assert.Single(result.Devices).SubclassCategory);
        Assert.Empty(cloud.MembershipRequests);
    }

    [Fact]
    public async Task EnrichAsync_MarksIncompleteWhenCatalogHasNoEntries()
    {
        var cloud = new FakeCloud();

        DeviceClassificationResult result = await CreateClassification(cloud).EnrichAsync(
            _session,
            [ChannelTwo],
            [],
            CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Null(Assert.Single(result.Devices).SubclassCategory);
    }

    [Fact]
    public async Task EnrichAsync_DoesNotRestorePreviousCategoryWhenComplete()
    {
        var cloud = new FakeCloud
        {
            Catalog = [new SwitchSubclass(2, "壁灯", 9)],
        };
        cloud.Memberships["parent-1"] = new Dictionary<int, int> { [1] = 2 };
        MiDevice cached = ChannelTwo with { SubclassId = 2, SubclassCategory = "light" };

        DeviceClassificationResult result = await CreateClassification(cloud).EnrichAsync(
            _session,
            [ChannelTwo],
            [cached],
            CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal("other", Assert.Single(result.Devices).SubclassCategory);
    }

    [Fact]
    public async Task EnrichAsync_PropagatesCancellation()
    {
        using var lifetime = new CancellationTokenSource();
        await lifetime.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClassification(new FakeCloud()).EnrichAsync(
                _session,
                [ChannelTwo],
                [],
                lifetime.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private DeviceClassification CreateClassification(FakeCloud cloud)
        => new(cloud, new SwitchSubclassCatalog(Path.Combine(_root, "switch-subclass"), cloud));

    private sealed class FakeCloud : IMiHomeCloudClient
    {
        public IReadOnlyList<SwitchSubclass> Catalog { get; set; } = [];

        public Dictionary<string, IReadOnlyDictionary<int, int>> Memberships { get; } =
            new(StringComparer.Ordinal);

        public Exception? CatalogFailure { get; set; }

        public Exception? MembershipFailure { get; set; }

        public List<string[]> MembershipRequests { get; } = [];

        public Task<IReadOnlyList<SwitchSubclass>> GetSwitchSubclassesAsync(
            MiHomeSession session,
            CancellationToken cancellationToken)
            => CatalogFailure is null
                ? Task.FromResult(Catalog)
                : Task.FromException<IReadOnlyList<SwitchSubclass>>(CatalogFailure);

        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>> GetSwitchMembershipsAsync(
            MiHomeSession session,
            IReadOnlyList<string> parentDids,
            CancellationToken cancellationToken)
        {
            MembershipRequests.Add(parentDids.ToArray());
            return MembershipFailure is null
                ? Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>>(
                    new Dictionary<string, IReadOnlyDictionary<int, int>>(Memberships, StringComparer.Ordinal))
                : Task.FromException<IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>>(MembershipFailure);
        }

        public Task<IReadOnlyList<MiHome>> GetHomesAsync(
            MiHomeSession session,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MiDevice>> GetDevicesAsync(
            MiHomeSession session,
            MiHome home,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
            MiHomeSession session,
            string did,
            IReadOnlyList<MiPropertyKey> properties,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SetPropertyAsync(
            MiHomeSession session,
            string did,
            MiPropertyKey property,
            System.Text.Json.JsonElement value,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<System.Text.Json.JsonElement> InvokeActionAsync(
            MiHomeSession session,
            string did,
            MiActionKey action,
            IReadOnlyList<System.Text.Json.JsonElement> input,
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
    }
}

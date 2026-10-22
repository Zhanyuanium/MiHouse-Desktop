using System.Text.Json;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;

namespace MiHouse.Core.Tests.Devices;

public sealed class SwitchSubclassCatalogTests : IDisposable
{
    private static readonly MiHomeSession _session =
        new("12345", "service-token", "MDEyMzQ1Njc4OWFiY2RlZg==", "cn");

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mihouse-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAsync_EmptyNetworkCatalog_KeepsExpiredCacheOnDisk()
    {
        WriteCache(DateTimeOffset.UtcNow.AddDays(-8), new SwitchSubclass(7, "吸顶灯", 12));
        string before = File.ReadAllText(CachePath);
        var cloud = new FakeCloud();

        var entries = await new SwitchSubclassCatalog(_root, cloud).GetAsync(_session, CancellationToken.None);

        Assert.Equal(7, Assert.Single(entries).Key);
        Assert.Equal(1, cloud.Requests);
        Assert.Equal(before, File.ReadAllText(CachePath));
    }

    [Fact]
    public async Task GetAsync_ExpiredCacheSurvivesNetworkFailure()
    {
        WriteCache(DateTimeOffset.UtcNow.AddDays(-8), new SwitchSubclass(7, "吸顶灯", 12));
        var cloud = new FakeCloud { Failure = new InvalidOperationException("offline") };

        var entries = await new SwitchSubclassCatalog(_root, cloud).GetAsync(_session, CancellationToken.None);

        Assert.Equal(7, Assert.Single(entries).Key);
        Assert.Equal(1, cloud.Requests);
    }

    [Fact]
    public async Task GetAsync_EmptyDiskEntries_IsTreatedAsMiss()
    {
        WriteCache(DateTimeOffset.UtcNow);
        var cloud = new FakeCloud { Catalog = [new SwitchSubclass(8, "插座", 9)] };

        var entries = await new SwitchSubclassCatalog(_root, cloud).GetAsync(_session, CancellationToken.None);

        Assert.Equal(8, Assert.Single(entries).Key);
        Assert.Equal(1, cloud.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CachePath => Path.Combine(_root, "switch-subclass.json");

    private void WriteCache(DateTimeOffset fetchedAtUtc, params SwitchSubclass[] entries)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(CachePath, JsonSerializer.Serialize(new
        {
            Version = 1,
            FetchedAtUtc = fetchedAtUtc,
            Entries = entries,
        }));
    }

    private sealed class FakeCloud : IMiHomeCloudClient
    {
        public IReadOnlyList<SwitchSubclass> Catalog { get; set; } = [];

        public Exception? Failure { get; set; }

        public int Requests { get; private set; }

        public Task<IReadOnlyList<SwitchSubclass>> GetSwitchSubclassesAsync(
            MiHomeSession session,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Failure is null
                ? Task.FromResult(Catalog)
                : Task.FromException<IReadOnlyList<SwitchSubclass>>(Failure);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>> GetSwitchMembershipsAsync(
            MiHomeSession session,
            IReadOnlyList<string> parentDids,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

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
            JsonElement value,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<JsonElement> InvokeActionAsync(
            MiHomeSession session,
            string did,
            MiActionKey action,
            IReadOnlyList<JsonElement> input,
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

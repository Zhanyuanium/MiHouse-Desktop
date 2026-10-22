using System.Text.Json;
using MiHouse.Core.Devices;

namespace MiHouse.Core.Tests.Devices;

public sealed class SnapshotStoreTests : IDisposable
{
    private const long _userId = 4271;
    private static readonly MiHome _home = new(
        "home-1",
        "Test home",
        "4271",
        false,
        [
            new MiRoom("room-1", "Living room", ["did-1", "did-2"]),
            new MiRoom("room-2", "Kitchen", [])
        ]);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "MiHouse.Core.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveHomes_ThenTryLoad_RoundTripsRoomsAndSelectedHome()
    {
        var store = new SnapshotStore(_root);
        MiHome second = _home with { Id = "home-2", Name = "Second home" };

        store.SaveHomes(_userId, [_home, second], 1001);

        Assert.True(store.TryLoadHomes(_userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId));
        Assert.Equal(1001L, selectedHomeId);
        Assert.Collection(
            homes,
            home =>
            {
                Assert.Equal(_home.Id, home.Id);
                Assert.Equal(_home.Name, home.Name);
                Assert.Equal(_home.OwnerUserId, home.OwnerUserId);
                Assert.False(home.IsShared);
                Assert.Collection(
                    home.Rooms,
                    room =>
                    {
                        Assert.Equal("room-1", room.Id);
                        Assert.Equal("Living room", room.Name);
                        Assert.Equal(new[] { "did-1", "did-2" }, room.DeviceIds);
                    },
                    room =>
                    {
                        Assert.Equal("room-2", room.Id);
                        Assert.Equal("Kitchen", room.Name);
                        Assert.Empty(room.DeviceIds);
                    });
            },
            home => Assert.Equal("home-2", home.Id));
    }

    [Fact]
    public void SaveHomes_WithoutSelection_RoundTripsNullSelection()
    {
        var store = new SnapshotStore(_root);

        store.SaveHomes(_userId, [_home], null);

        Assert.True(store.TryLoadHomes(_userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId));
        Assert.Null(selectedHomeId);
        Assert.Single(homes);
    }

    [Fact]
    public void SaveDevices_ThenTryLoad_RoundTripsSplitDeviceFields()
    {
        var store = new SnapshotStore(_root);
        MiDevice parent = new("parent-1", "yeelink.light.color1", "Ceiling light", "Living room", true,
            null, null, "urn:miot-spec-v2:device:light:0000A001:test:1", "192.168.1.20");
        MiDevice child = new("child-1", "yeelink.light.color1", "Left lamp", null, false,
            "parent-1", 3, null, null);
        MiDevice incomplete = new("incomplete-1", "yeelink.light.color1", "Broken channel", null, true,
            IsSplitMetadataIncomplete: true);

        store.SaveDevices(_userId, _home, [parent, child, incomplete]);

        Assert.True(store.TryLoadDevices(_userId, _home, out IReadOnlyList<MiDevice> devices));
        Assert.Collection(
            devices,
            device => Assert.Equal(parent, device),
            device =>
            {
                Assert.Equal(child, device);
                Assert.Equal("parent-1", device.ParentDid);
                Assert.Equal(3, device.ChannelSiid);
                Assert.Equal("parent-1", device.ControlDid);
            },
            device =>
            {
                Assert.Equal(incomplete, device);
                Assert.True(device.IsSplitMetadataIncomplete);
            });
    }

    [Fact]
    public void SaveDevices_ThenTryLoad_RoundTripsSubclassClassification()
    {
        var store = new SnapshotStore(_root);
        MiDevice device = new("parent-1.s2", "m.switch", "Switch 1", null, true,
            "parent-1", 2, "urn:miot-spec-v2:device:switch:0000A003:test:1", null, false, 7, "light");

        store.SaveDevices(_userId, _home, [device]);

        Assert.True(store.TryLoadDevices(_userId, _home, out IReadOnlyList<MiDevice> devices));
        MiDevice loaded = Assert.Single(devices);
        Assert.Equal(device, loaded);
        Assert.Equal(7, loaded.SubclassId);
        Assert.Equal("light", loaded.SubclassCategory);
    }

    [Fact]
    public void TryLoadDevices_SnapshotWithoutSubclassFields_LoadsNulls()
    {
        var store = new SnapshotStore(_root);
        WriteDevicesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Devices = new[] { new { Did = "did-1", Model = "model", Name = "Device", IsOnline = true } }
        }));

        Assert.True(store.TryLoadDevices(_userId, _home, out IReadOnlyList<MiDevice> devices));
        MiDevice device = Assert.Single(devices);
        Assert.Null(device.SubclassId);
        Assert.Null(device.SubclassCategory);
    }

    [Fact]
    public void TryLoadDevices_SnapshotWithoutSplitMarker_LoadsOrdinaryDevice()
    {
        var store = new SnapshotStore(_root);
        WriteDevicesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Devices = new[] { new { Did = "did-1", Model = "model", Name = "Device", IsOnline = true } }
        }));

        Assert.True(store.TryLoadDevices(_userId, _home, out IReadOnlyList<MiDevice> devices));
        MiDevice device = Assert.Single(devices);
        Assert.False(device.IsSplitMetadataIncomplete);
        Assert.False(device.IsSplitIncomplete);
    }

    [Fact]
    public void SaveHomes_Twice_ReplacesAtomically()
    {
        var store = new SnapshotStore(_root);
        MiHome replacement = _home with { Name = "Replacement" };

        store.SaveHomes(_userId, [_home], 1);
        store.SaveHomes(_userId, [replacement], 2);

        Assert.True(store.TryLoadHomes(_userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId));
        Assert.Equal(2L, selectedHomeId);
        Assert.Equal("Replacement", Assert.Single(homes).Name);
        Assert.Equal(new[] { "homes.json" }, Directory.GetFiles(UserDirectory()).Select(Path.GetFileName));
    }

    [Fact]
    public void TryLoadHomes_CorruptJson_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteHomesRaw("{ not valid json");

        Assert.False(store.TryLoadHomes(_userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId));
        Assert.Empty(homes);
        Assert.Null(selectedHomeId);
    }

    [Fact]
    public void TryLoadHomes_UnexpectedJsonShape_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteHomesRaw("[]");

        Assert.False(store.TryLoadHomes(_userId, out _, out _));
    }

    [Fact]
    public void TryLoadHomes_StructurallyInvalidEntry_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteHomesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow,
            SelectedHomeId = (long?)1,
            Homes = new[] { new { Id = "home-1", Name = "Home", OwnerUserId = "1", IsShared = false, Rooms = (object?)null } }
        }));

        Assert.False(store.TryLoadHomes(_userId, out _, out _));
    }

    [Fact]
    public void TryLoadDevices_EntryWithoutDid_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteDevicesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Devices = new[] { new { Did = (string?)null, Model = "model", Name = "Device", IsOnline = true } }
        }));

        Assert.False(store.TryLoadDevices(_userId, _home, out _));
    }

    [Fact]
    public void TryLoadHomes_UnknownSchemaVersion_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteHomesRaw(JsonSerializer.Serialize(new
        {
            Version = 99,
            SavedAtUtc = DateTimeOffset.UtcNow,
            SelectedHomeId = (long?)1,
            Homes = new[] { _home }
        }));

        Assert.False(store.TryLoadHomes(_userId, out _, out _));
    }

    [Fact]
    public void TryLoadDevices_UnknownSchemaVersion_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteDevicesRaw(JsonSerializer.Serialize(new
        {
            Version = 99,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Devices = Array.Empty<MiDevice>()
        }));

        Assert.False(store.TryLoadDevices(_userId, _home, out IReadOnlyList<MiDevice> devices));
        Assert.Empty(devices);
    }

    [Fact]
    public void TryLoadHomes_EntryOlderThanThirtyDays_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteHomesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow.AddDays(-31),
            SelectedHomeId = (long?)1,
            Homes = new[] { _home }
        }));

        Assert.False(store.TryLoadHomes(_userId, out _, out _));
    }

    [Fact]
    public void TryLoadHomes_RecentEntry_IsAccepted()
    {
        var store = new SnapshotStore(_root);
        WriteHomesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow.AddDays(-29),
            SelectedHomeId = (long?)1,
            Homes = new[] { _home }
        }));

        Assert.True(store.TryLoadHomes(_userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId));
        Assert.Equal(1L, selectedHomeId);
        Assert.Single(homes);
    }

    [Fact]
    public void TryLoadDevices_EntryOlderThanThirtyDays_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteDevicesRaw(JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow.AddDays(-31),
            Devices = Array.Empty<MiDevice>()
        }));

        Assert.False(store.TryLoadDevices(_userId, _home, out _));
    }

    [Fact]
    public void Users_AreIsolated()
    {
        const long otherUserId = 9001;
        var store = new SnapshotStore(_root);
        MiHome otherHome = _home with { Id = "home-other", OwnerUserId = "9001" };

        store.SaveHomes(_userId, [_home], 1);
        store.SaveDevices(_userId, _home, [new MiDevice("did-1", "model", "Device", null, true)]);

        Assert.False(store.TryLoadHomes(otherUserId, out _, out _));
        Assert.False(store.TryLoadDevices(otherUserId, otherHome, out _));

        store.SaveHomes(otherUserId, [otherHome], 2);

        Assert.True(store.TryLoadHomes(_userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId));
        Assert.Equal(_home.Id, Assert.Single(homes).Id);
        Assert.Equal(1L, selectedHomeId);
        Assert.True(store.TryLoadHomes(otherUserId, out IReadOnlyList<MiHome> otherHomes, out long? otherSelectedHomeId));
        Assert.Equal(otherHome.Id, Assert.Single(otherHomes).Id);
        Assert.Equal(2L, otherSelectedHomeId);
    }

    [Fact]
    public void SaveScenes_ThenTryLoad_RoundTripsScenes()
    {
        var store = new SnapshotStore(_root);
        MiScene enabled = new("scene-1", "Movie time", true, "https://example.test/icon.png");
        MiScene disabled = new("scene-2", "Night", false, null);

        store.SaveScenes(_userId, _home, [enabled, disabled]);

        Assert.True(store.TryLoadScenes(_userId, _home, out IReadOnlyList<MiScene> scenes));
        Assert.Collection(
            scenes,
            scene => Assert.Equal(enabled, scene),
            scene => Assert.Equal(disabled, scene));
    }

    [Fact]
    public void SaveScenes_ReplacesPreviousEntryAtomically()
    {
        var store = new SnapshotStore(_root);

        store.SaveScenes(_userId, _home, [new MiScene("scene-1", "First", true, null)]);
        store.SaveScenes(_userId, _home, [new MiScene("scene-2", "Second", false, null)]);

        Assert.True(store.TryLoadScenes(_userId, _home, out IReadOnlyList<MiScene> scenes));
        Assert.Equal("scene-2", Assert.Single(scenes).Id);
        Assert.Equal(
            new[] { $"scenes-{_home.Id}-{_home.OwnerUserId}.json" },
            Directory.GetFiles(UserDirectory()).Select(path => Path.GetFileName(path)!));
    }

    [Fact]
    public void Scenes_AreIsolatedPerHome()
    {
        var store = new SnapshotStore(_root);
        MiHome other = _home with { Id = "home-2" };

        store.SaveScenes(_userId, _home, [new MiScene("scene-1", "First", true, null)]);
        store.SaveScenes(_userId, other, [new MiScene("scene-2", "Second", true, null)]);

        Assert.True(store.TryLoadScenes(_userId, _home, out IReadOnlyList<MiScene> scenes));
        Assert.Equal("scene-1", Assert.Single(scenes).Id);
        Assert.True(store.TryLoadScenes(_userId, other, out IReadOnlyList<MiScene> otherScenes));
        Assert.Equal("scene-2", Assert.Single(otherScenes).Id);
    }

    [Fact]
    public void TryLoadScenes_CorruptJson_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteScenesRaw(_home, "{ not valid json");

        Assert.False(store.TryLoadScenes(_userId, _home, out IReadOnlyList<MiScene> scenes));
        Assert.Empty(scenes);
    }

    [Fact]
    public void TryLoadScenes_UnexpectedJsonShape_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteScenesRaw(_home, "[]");

        Assert.False(store.TryLoadScenes(_userId, _home, out _));
    }

    [Fact]
    public void TryLoadScenes_EntryWithoutId_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteScenesRaw(_home, JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Scenes = new[] { new { Id = (string?)null, Name = "Scene", Enabled = true } }
        }));

        Assert.False(store.TryLoadScenes(_userId, _home, out _));
    }

    [Fact]
    public void TryLoadScenes_EntryOlderThanThirtyDays_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteScenesRaw(_home, JsonSerializer.Serialize(new
        {
            Version = 1,
            SavedAtUtc = DateTimeOffset.UtcNow.AddDays(-31),
            Scenes = Array.Empty<MiScene>()
        }));

        Assert.False(store.TryLoadScenes(_userId, _home, out IReadOnlyList<MiScene> scenes));
        Assert.Empty(scenes);
    }

    [Fact]
    public void TryLoadScenes_UnknownSchemaVersion_ReturnsFalse()
    {
        var store = new SnapshotStore(_root);
        WriteScenesRaw(_home, JsonSerializer.Serialize(new
        {
            Version = 99,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Scenes = new[] { new MiScene("scene-1", "Scene", true, null) }
        }));

        Assert.False(store.TryLoadScenes(_userId, _home, out _));
    }

    [Fact]
    public void UnusableDirectory_DoesNotThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        File.WriteAllText(_root, "blocked");
        var store = new SnapshotStore(_root);

        store.SaveHomes(_userId, [_home], 1);
        store.SaveDevices(_userId, _home, [new MiDevice("did-1", "model", "Device", null, true)]);
        store.SaveScenes(_userId, _home, [new MiScene("scene-1", "Scene", true, null)]);

        Assert.False(store.TryLoadHomes(_userId, out _, out _));
        Assert.False(store.TryLoadDevices(_userId, _home, out _));
        Assert.False(store.TryLoadScenes(_userId, _home, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        else if (File.Exists(_root))
        {
            File.Delete(_root);
        }
    }

    private string UserDirectory() => Path.Combine(_root, _userId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void WriteHomesRaw(string json)
    {
        Directory.CreateDirectory(UserDirectory());
        File.WriteAllText(Path.Combine(UserDirectory(), "homes.json"), json);
    }

    private void WriteDevicesRaw(string json)
    {
        Directory.CreateDirectory(UserDirectory());
        File.WriteAllText(Path.Combine(UserDirectory(), $"devices-{_home.Id}-{_home.OwnerUserId}.json"), json);
    }

    private void WriteScenesRaw(MiHome home, string json)
    {
        Directory.CreateDirectory(UserDirectory());
        File.WriteAllText(Path.Combine(UserDirectory(), $"scenes-{home.Id}-{home.OwnerUserId}.json"), json);
    }
}

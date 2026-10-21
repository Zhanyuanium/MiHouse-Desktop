using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MiHouse.Core.Devices;

/// <summary>
/// Persists the home, device and scene structure of the last successful cloud refresh so the
/// dashboard can paint before the network answers. Entries are best-effort cache data:
/// every IO or parsing failure is treated as a cache miss.
/// </summary>
public sealed class SnapshotStore
{
    private const int _schemaVersion = 1;
    private static readonly TimeSpan _maximumAge = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _root;

    /// <summary>
    /// Creates a store rooted at <paramref name="rootDirectory"/>; each user gets a subdirectory.
    /// </summary>
    public SnapshotStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = rootDirectory;
    }

    /// <summary>
    /// Stores the homes shown at the end of a successful home list refresh.
    /// </summary>
    public void SaveHomes(long userId, IReadOnlyList<MiHome> homes, long? selectedHomeId)
    {
        ArgumentNullException.ThrowIfNull(homes);
        Write(GetUserDirectory(userId), "homes.json",
            new HomesFile(_schemaVersion, DateTimeOffset.UtcNow, selectedHomeId, homes));
    }

    /// <summary>
    /// Reads the homes stored for <paramref name="userId"/>. Returns <see langword="false"/>
    /// when no usable entry exists.
    /// </summary>
    public bool TryLoadHomes(long userId, out IReadOnlyList<MiHome> homes, out long? selectedHomeId)
    {
        homes = [];
        selectedHomeId = null;
        if (!TryRead(Path.Combine(GetUserDirectory(userId), "homes.json"), out HomesFile? file) ||
            file is null || file.Version != _schemaVersion || IsExpired(file.SavedAtUtc) ||
            file.Homes is null || !IsUsable(file.Homes))
        {
            return false;
        }

        homes = file.Homes;
        selectedHomeId = file.SelectedHomeId;
        return true;
    }

    /// <summary>
    /// Stores the devices of <paramref name="home"/> after a successful device refresh.
    /// </summary>
    public void SaveDevices(long userId, MiHome home, IReadOnlyList<MiDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(devices);
        Write(GetUserDirectory(userId), GetDevicesFileName(home),
            new DevicesFile(_schemaVersion, DateTimeOffset.UtcNow, devices));
    }

    /// <summary>
    /// Reads the devices stored for <paramref name="home"/>. Returns <see langword="false"/>
    /// when no usable entry exists.
    /// </summary>
    public bool TryLoadDevices(long userId, MiHome home, out IReadOnlyList<MiDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(home);
        devices = [];
        if (!TryRead(Path.Combine(GetUserDirectory(userId), GetDevicesFileName(home)), out DevicesFile? file) ||
            file is null || file.Version != _schemaVersion || IsExpired(file.SavedAtUtc) ||
            file.Devices is null || !IsUsable(file.Devices))
        {
            return false;
        }

        devices = file.Devices;
        return true;
    }

    /// <summary>
    /// Stores the manual scenes of <paramref name="home"/> after a successful scene refresh.
    /// </summary>
    public void SaveScenes(long userId, MiHome home, IReadOnlyList<MiScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(scenes);
        Write(GetUserDirectory(userId), GetScenesFileName(home),
            new ScenesFile(_schemaVersion, DateTimeOffset.UtcNow, scenes));
    }

    /// <summary>
    /// Reads the scenes stored for <paramref name="home"/>. Returns <see langword="false"/>
    /// when no usable entry exists.
    /// </summary>
    public bool TryLoadScenes(long userId, MiHome home, out IReadOnlyList<MiScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(home);
        scenes = [];
        if (!TryRead(Path.Combine(GetUserDirectory(userId), GetScenesFileName(home)), out ScenesFile? file) ||
            file is null || file.Version != _schemaVersion || IsExpired(file.SavedAtUtc) ||
            file.Scenes is null || !IsUsable(file.Scenes))
        {
            return false;
        }

        scenes = file.Scenes;
        return true;
    }

    private static bool IsUsable(IReadOnlyList<MiHome> homes)
    {
        return homes.All(home => home is not null && home.Rooms is not null &&
            home.Rooms.All(room => room is not null && room.DeviceIds is not null));
    }

    private static bool IsUsable(IReadOnlyList<MiDevice> devices)
    {
        return devices.All(device => device is not null && !string.IsNullOrEmpty(device.Did));
    }

    private static bool IsUsable(IReadOnlyList<MiScene> scenes)
    {
        return scenes.All(scene => scene is not null && !string.IsNullOrEmpty(scene.Id));
    }

    private static string GetDevicesFileName(MiHome home)
    {
        return $"devices-{Sanitize(home.Id)}-{Sanitize(home.OwnerUserId)}.json";
    }

    private static string GetScenesFileName(MiHome home)
    {
        return $"scenes-{Sanitize(home.Id)}-{Sanitize(home.OwnerUserId)}.json";
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    private static bool IsExpired(DateTimeOffset savedAtUtc)
    {
        return DateTimeOffset.UtcNow - savedAtUtc > _maximumAge;
    }

    private string GetUserDirectory(long userId)
    {
        return Path.Combine(_root, userId.ToString(CultureInfo.InvariantCulture));
    }

    private static void Write<T>(string directory, string fileName, T value)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, _json));
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
            }
        }
    }

    private static bool TryRead<T>(string path, out T? value)
        where T : class
    {
        value = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), _json);
            return value is not null;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
        }
    }

    private static bool IsCacheFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException
            or JsonException;
    }

    private sealed record HomesFile(
        int Version,
        DateTimeOffset SavedAtUtc,
        long? SelectedHomeId,
        IReadOnlyList<MiHome> Homes);

    private sealed record DevicesFile(
        int Version,
        DateTimeOffset SavedAtUtc,
        IReadOnlyList<MiDevice> Devices);

    private sealed record ScenesFile(
        int Version,
        DateTimeOffset SavedAtUtc,
        IReadOnlyList<MiScene> Scenes);
}

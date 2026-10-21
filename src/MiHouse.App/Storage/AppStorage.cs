using System.Runtime.InteropServices;
using Windows.Storage;

namespace MiHouse.App.Storage;

/// <summary>
/// Resolves the directories that hold persisted application data. Packaged runs use the MSIX
/// package data folders; the fallback below exists only so unpackaged development runs work.
/// </summary>
internal sealed class AppStorage
{
    private const string _unpackagedFolderName = "MiHouse-Unpackaged";

    private AppStorage()
    {
        try
        {
            var data = ApplicationData.Current;
            StateDirectory = data.LocalFolder.Path;
            CacheDirectory = data.LocalCacheFolder.Path;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            // Without package identity ApplicationData is unavailable, so dev runs would crash.
            // Keep them in a separate directory that never touches the package's data folders.
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _unpackagedFolderName);

            StateDirectory = root;
            CacheDirectory = Path.Combine(root, "cache");
        }
    }

    /// <summary>
    /// Gets the resolved storage locations.
    /// </summary>
    public static AppStorage Current { get; } = new();

    /// <summary>
    /// Gets the durable package state directory. Sessions, the device id and logs live here.
    /// </summary>
    public string StateDirectory { get; }

    /// <summary>
    /// Gets the package cache directory. Spec and cloud snapshots live here and may be discarded.
    /// </summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// Returns the path of <paramref name="parts"/> under <see cref="StateDirectory"/>.
    /// </summary>
    public string StatePath(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return parts.Length == 0
            ? StateDirectory
            : Path.Combine([StateDirectory, .. parts]);
    }

    /// <summary>
    /// Returns the path of <paramref name="parts"/> under <see cref="CacheDirectory"/>.
    /// </summary>
    public string CachePath(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return parts.Length == 0
            ? CacheDirectory
            : Path.Combine([CacheDirectory, .. parts]);
    }
}

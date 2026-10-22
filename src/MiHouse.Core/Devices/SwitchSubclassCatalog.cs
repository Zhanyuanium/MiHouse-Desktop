using System.Text.Json;
using MiHouse.Core.Session;

namespace MiHouse.Core.Devices;

/// <summary>
/// Reads the cloud's switch subclass catalog and keeps it in memory and on disk between starts,
/// so switch channels stay classified after a restart and while offline. Every IO or parsing
/// failure is treated as a cache miss.
/// </summary>
public sealed class SwitchSubclassCatalog
{
    private const int _schemaVersion = 1;
    private const string _fileName = "switch-subclass.json";
    private static readonly TimeSpan _maximumAge = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyDictionary<int, SwitchSubclass> _empty =
        new Dictionary<int, SwitchSubclass>();

    private readonly string _directory;
    private readonly IMiHomeCloudClient _cloud;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<int, SwitchSubclass>? _entries;
    private DateTimeOffset _fetchedAtUtc;
    private bool _diskRead;

    /// <summary>
    /// Creates a catalog caching its entries under <paramref name="cacheDirectory"/>.
    /// </summary>
    public SwitchSubclassCatalog(string cacheDirectory, IMiHomeCloudClient cloud)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(cloud);

        _directory = cacheDirectory;
        _cloud = cloud;
    }

    /// <summary>
    /// Returns the catalog keyed by subclass id. An expired in-memory or on-disk copy is
    /// refreshed from the cloud; when that request fails the expired copy is returned, and an
    /// empty catalog when nothing is cached.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, SwitchSubclass>> GetAsync(
        MiHomeSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Reads, refreshes and disk writes share one lock: the catalog is fetched rarely, and a
        // concurrent reader must never observe a half-updated cache.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetFresh(out IReadOnlyDictionary<int, SwitchSubclass> fresh))
            {
                return fresh;
            }

            if (!_diskRead)
            {
                _diskRead = true;
                if (TryReadDisk(out IReadOnlyDictionary<int, SwitchSubclass> entries, out DateTimeOffset fetchedAtUtc))
                {
                    _entries = entries;
                    _fetchedAtUtc = fetchedAtUtc;
                }
            }

            if (TryGetFresh(out fresh))
            {
                return fresh;
            }

            IReadOnlyDictionary<int, SwitchSubclass> stale = _entries ?? _empty;
            try
            {
                IReadOnlyList<SwitchSubclass> fetched = await _cloud
                    .GetSwitchSubclassesAsync(session, cancellationToken)
                    .ConfigureAwait(false);

                if (fetched.Count == 0)
                {
                    // An empty catalog cannot be legitimate while devices exist, and caching it
                    // would erase every known classification on the next offline start.
                    return stale;
                }

                IReadOnlyDictionary<int, SwitchSubclass> entries = Build(fetched);
                _entries = entries;
                _fetchedAtUtc = DateTimeOffset.UtcNow;
                WriteDisk(entries, _fetchedAtUtc);
                return entries;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Classification is optional decoration: keep serving the expired catalog.
                return stale;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFresh(out IReadOnlyDictionary<int, SwitchSubclass> entries)
    {
        entries = _empty;
        if (_entries is null || DateTimeOffset.UtcNow - _fetchedAtUtc > _maximumAge)
        {
            return false;
        }

        entries = _entries;
        return true;
    }

    private static IReadOnlyDictionary<int, SwitchSubclass> Build(IReadOnlyList<SwitchSubclass> entries)
    {
        var lookup = new Dictionary<int, SwitchSubclass>(entries.Count);
        foreach (SwitchSubclass entry in entries)
        {
            lookup[entry.Id] = entry;
        }

        return lookup;
    }

    private bool TryReadDisk(
        out IReadOnlyDictionary<int, SwitchSubclass> entries,
        out DateTimeOffset fetchedAtUtc)
    {
        entries = _empty;
        fetchedAtUtc = default;
        try
        {
            string path = Path.Combine(_directory, _fileName);
            if (!File.Exists(path))
            {
                return false;
            }

            CatalogFile? file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), _json);
            if (file is null || file.Version != _schemaVersion || file.Entries is null || file.Entries.Count == 0)
            {
                return false;
            }

            entries = Build(file.Entries);
            fetchedAtUtc = file.FetchedAtUtc;
            return true;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            return false;
        }
    }

    private void WriteDisk(IReadOnlyDictionary<int, SwitchSubclass> entries, DateTimeOffset fetchedAtUtc)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, _fileName);
            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(
                new CatalogFile(_schemaVersion, fetchedAtUtc, entries.Values.ToArray()), _json));
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

    private sealed record CatalogFile(
        int Version,
        DateTimeOffset FetchedAtUtc,
        IReadOnlyList<SwitchSubclass> Entries);
}

using System.Security.Cryptography;
using System.Text;

namespace MiHouse.Core.Spec;

/// <summary>
/// Persists raw miot-spec JSON documents in a local directory so that definitions
/// and the instance index survive application restarts.
/// </summary>
public sealed class SpecDiskCache
{
    private readonly string _directory;

    /// <summary>
    /// Creates a cache rooted at <paramref name="directory"/>.
    /// </summary>
    public SpecDiskCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>
    /// Returns the document stored for <paramref name="key"/>, or <see langword="null"/>
    /// when no readable entry exists.
    /// </summary>
    public string? TryRead(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var path = GetPath(key);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Stores <paramref name="json"/> for <paramref name="key"/>, replacing any existing entry.
    /// </summary>
    public void Write(string key, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(json);

        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var path = GetPath(key);
            tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
        }
        catch (Exception ex) when (IsIoFailure(ex))
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
        catch (Exception ex) when (IsIoFailure(ex))
        {
        }
    }

    private static bool IsIoFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException;
    }

    private string GetPath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_directory, Convert.ToHexStringLower(hash) + ".json");
    }
}

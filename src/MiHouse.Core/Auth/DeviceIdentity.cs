using System.Security.Cryptography;

namespace MiHouse.Core.Auth;

/// <summary>
/// Anonymous device id sent with account requests. Xiaomi uses it for risk scoring, so it is
/// created once and then reused; rotating it makes captcha challenges more likely.
/// </summary>
internal sealed class DeviceIdentity
{
    private const string _alphabet = "abcdefghijklmnopqrstuvwxyz";
    private const int _idLength = 16;

    private readonly string _filePath;
    private readonly object _gate = new();
    private string? _value;

    /// <summary>
    /// Initializes a new instance that stores the id in the current Windows user profile.
    /// </summary>
    internal DeviceIdentity()
        : this(DefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance that stores the id at <paramref name="filePath"/>.
    /// </summary>
    /// <param name="filePath">File that holds the id.</param>
    internal DeviceIdentity(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
    }

    /// <summary>
    /// Gets the default location of the device id file.
    /// </summary>
    internal static string DefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiHouse",
        "device-id");

    /// <summary>
    /// Returns the persisted device id, creating it on first use.
    /// </summary>
    internal string GetOrCreate()
    {
        if (_value is not null)
        {
            return _value;
        }

        lock (_gate)
        {
            return _value ??= TryRead() ?? CreateAndStore();
        }
    }

    private string? TryRead()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var value = File.ReadAllText(_filePath).Trim();
            return IsValid(value) ? value : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string CreateAndStore()
    {
        var buffer = new char[_idLength];
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index] = _alphabet[RandomNumberGenerator.GetInt32(_alphabet.Length)];
        }

        var value = new string(buffer);

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, value);
        }
        catch (IOException)
        {
            // A read-only profile still allows the login to proceed with an in-memory id.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return value;
    }

    private static bool IsValid(string value)
    {
        if (value.Length != _idLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < 'a' or > 'z')
            {
                return false;
            }
        }

        return true;
    }
}

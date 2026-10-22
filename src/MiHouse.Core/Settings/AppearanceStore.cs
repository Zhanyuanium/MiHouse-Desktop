using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiHouse.Core.Settings;

/// <summary>
/// Persists <see cref="AppearanceSettings"/> as plain JSON in a state directory. Appearance is
/// not a critical path, so every read and write failure degrades silently to defaults or a no-op.
/// </summary>
public sealed class AppearanceStore
{
    private const string _fileName = "appearance.json";
    private const int _version = 1;

    private readonly string _directory;
    private readonly string _filePath;

    public AppearanceStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _directory = stateDirectory;
        _filePath = Path.Combine(stateDirectory, _fileName);
    }

    /// <summary>
    /// Reads the stored appearance settings, falling back to defaults field-by-field when the
    /// file is missing, malformed or contains unknown values. Never throws.
    /// </summary>
    public AppearanceSettings Load()
    {
        var settings = new AppearanceSettings();

        Dto? dto = null;
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                dto = JsonSerializer.Deserialize<Dto>(json, _options);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Corrupt or unreadable settings fall back to defaults below.
        }

        if (dto is not null)
        {
            settings.ThemeMode = dto.ThemeMode;
            settings.AccentSource = dto.AccentSource;
            settings.CustomAccentColor = dto.CustomAccentColor;
            settings.UseColoredIcons = dto.UseColoredIcons;
            settings.AnimationsEnabled = dto.AnimationsEnabled;
        }

        return settings;
    }

    /// <summary>
    /// Writes <paramref name="settings"/> to disk using a temp file and an atomic move, so a
    /// reader never observes a partially written file. IO failures are ignored.
    /// </summary>
    public void Save(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dto = new Dto
        {
            Version = _version,
            ThemeMode = settings.ThemeMode,
            AccentSource = settings.AccentSource,
            CustomAccentColor = settings.CustomAccentColor,
            UseColoredIcons = settings.UseColoredIcons,
            AnimationsEnabled = settings.AnimationsEnabled,
        };

        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(dto, _options);
            tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
            tempPath = null;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Appearance is not a critical path; a failed save is silently ignored.
        }
        finally
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
            }
        }
    }

    private static readonly JsonSerializerOptions _options = new()
    {
        Converters =
        {
            // Enums serialize as their string name, matching JsonStringEnumConverter, but the
            // lenient converters below return the default instead of throwing on unknown values.
            new LenientStringEnumConverter<AppThemeMode>(),
            new LenientStringEnumConverter<AccentColorSource>(),
            new JsonStringEnumConverter(),
        },
        PropertyNameCaseInsensitive = true,
    };

    private static bool IsRecoverable(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or System.Security.SecurityException
            or JsonException;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
        }
    }

    private sealed class Dto
    {
        public int Version { get; set; }

        public AppThemeMode ThemeMode { get; set; }

        public AccentColorSource AccentSource { get; set; }

        public string CustomAccentColor { get; set; } = AppearanceSettings.DefaultAccentColor;

        public bool UseColoredIcons { get; set; } = true;

        public bool AnimationsEnabled { get; set; } = true;
    }

    private sealed class LenientStringEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out TEnum value))
            {
                return value;
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}

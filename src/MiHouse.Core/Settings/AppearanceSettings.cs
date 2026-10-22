using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiHouse.Core.Settings;

/// <summary>
/// Selects how the application follows the Windows light/dark theme.
/// </summary>
public enum AppThemeMode
{
    /// <summary>Follow the operating system theme.</summary>
    System,

    /// <summary>Force the light theme.</summary>
    Light,

    /// <summary>Force the dark theme.</summary>
    Dark,
}

/// <summary>
/// Selects where the application accent color comes from.
/// </summary>
public enum AccentColorSource
{
    /// <summary>Use the Windows system accent color.</summary>
    System,

    /// <summary>Use <see cref="AppearanceSettings.CustomAccentColor"/>.</summary>
    Custom,
}

/// <summary>
/// User-facing appearance preferences: theme mode, accent color, colored icons and animations.
/// </summary>
public sealed class AppearanceSettings : INotifyPropertyChanged
{
    /// <summary>
    /// The fallback accent color, stored as an uppercase <c>#RRGGBB</c> value.
    /// </summary>
    public const string DefaultAccentColor = "#0078D4";

    private AppThemeMode _themeMode = AppThemeMode.System;
    private AccentColorSource _accentSource = AccentColorSource.System;
    private string _customAccentColor = DefaultAccentColor;
    private bool _useColoredIcons = true;
    private bool _animationsEnabled = true;

    /// <summary>
    /// Gets or sets the light/dark theme mode. Defaults to <see cref="AppThemeMode.System"/>.
    /// </summary>
    public AppThemeMode ThemeMode
    {
        get => _themeMode;
        set => SetField(ref _themeMode, value);
    }

    /// <summary>
    /// Gets or sets the accent color source. Defaults to <see cref="AccentColorSource.System"/>.
    /// </summary>
    public AccentColorSource AccentSource
    {
        get => _accentSource;
        set => SetField(ref _accentSource, value);
    }

    /// <summary>
    /// Gets or sets the custom accent color as <c>#RRGGBB</c>. Values are normalized to
    /// uppercase; anything that does not match <c>#RRGGBB</c> falls back to
    /// <see cref="DefaultAccentColor"/>.
    /// </summary>
    public string CustomAccentColor
    {
        get => _customAccentColor;
        set => SetField(ref _customAccentColor, Normalize(value));
    }

    /// <summary>
    /// Gets or sets whether icons use the accent color (<see langword="true"/>) or a neutral
    /// color (<see langword="false"/>). Defaults to <see langword="true"/>.
    /// </summary>
    public bool UseColoredIcons
    {
        get => _useColoredIcons;
        set => SetField(ref _useColoredIcons, value);
    }

    /// <summary>
    /// Gets or sets whether the application's own animations play. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set => SetField(ref _animationsEnabled, value);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after any property changes so callers can persist the new values.
    /// </summary>
    public event EventHandler? Changed;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Normalize(string? value)
    {
        if (value is { Length: 7 } && value[0] == '#' && IsHex(value[1..]))
        {
            return value.ToUpperInvariant();
        }

        return DefaultAccentColor;
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}

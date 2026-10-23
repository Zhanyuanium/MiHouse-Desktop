using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MiHouse.Core.Settings;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MiHouse.App.Appearance;

/// <summary>
/// Applies <see cref="AppearanceSettings"/> to the live UI: theme mode, accent color, colored
/// icons and the title-bar caption buttons. Own brushes are mutable <see cref="SolidColorBrush"/>
/// instances referenced by <c>StaticResource</c>; the service repaints them in place so already-
/// resolved controls redraw immediately.
/// </summary>
public sealed class AppearanceService
{
    private readonly AppearanceSettings _appearance;
    private readonly UISettings _uiSettings;
    private MainWindow? _window;
    private ResourceDictionary? _resources;
    private bool _initialized;

    private SolidColorBrush? _accentBase;
    private SolidColorBrush? _appAccent;
    private SolidColorBrush? _appAccentSubtle;
    private SolidColorBrush? _appOnAccent;
    private SolidColorBrush? _deviceIcon;
    private SolidColorBrush? _deviceIconBackdrop;

    private readonly Dictionary<string, SolidColorBrush?> _systemBrushes = new()
    {
        ["AccentFillColorDefaultBrush"] = null,
        ["AccentFillColorSecondaryBrush"] = null,
        ["AccentFillColorTertiaryBrush"] = null,
        ["TextOnAccentFillColorPrimaryBrush"] = null,
        ["TextOnAccentFillColorSecondaryBrush"] = null,
        ["AccentTextFillColorPrimaryBrush"] = null,
        ["AccentTextFillColorSecondaryBrush"] = null,
        ["AccentTextFillColorTertiaryBrush"] = null,
    };

    private Color _currentAccent;

    /// <summary>Text drawn on an accent fill in a light theme.</summary>
    private static readonly Color _lightOnAccent = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Text drawn on an accent fill in a dark theme.</summary>
    private static readonly Color _darkOnAccent = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    /// <summary>Raised after the accent brushes have been repainted.</summary>
    public static event Action? AccentApplied;

    public AppearanceService(AppearanceSettings appearance)
    {
        _appearance = appearance;
        _uiSettings = new UISettings();
        _appearance.PropertyChanged += OnAppearancePropertyChanged;
        _uiSettings.ColorValuesChanged += (_, _) => Run(ApplyAccent);
        HighContrast.Changed += () => Run(ApplyAccent);
    }

    /// <summary>
    /// Resolves the mutable brush instances from the application resources. Call once after
    /// <c>InitializeComponent</c> so the merged dictionaries are available.
    /// </summary>
    public void Initialize(ResourceDictionary resources)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _resources = resources;
        _accentBase = FindBrush(resources, "AccentBaseBrush");
        _appAccent = FindBrush(resources, "AppAccentBrush");
        _appAccentSubtle = FindBrush(resources, "AppAccentSubtleBrush");
        _appOnAccent = FindBrush(resources, "AppOnAccentBrush");
        _deviceIcon = FindBrush(resources, "DeviceIconBrush");
        _deviceIconBackdrop = FindBrush(resources, "DeviceIconBackdropBrush");
        foreach (string key in _systemBrushes.Keys.ToList())
        {
            _systemBrushes[key] = FindBrush(resources, key);
        }

        ApplyAccent();
    }

    /// <summary>Wires a window to the service and applies its current settings.</summary>
    public void Attach(MainWindow window)
    {
        _window = window;
        window.Root.ActualThemeChanged += (_, _) => Run(OnActualThemeChanged);
        ApplyThemeMode();
        ApplyTitleBar();
        ApplyAccent();

        // RequestedTheme only takes effect on the next layout pass, so the first pass can still
        // see the previous theme. ActualThemeChanged covers that case, and this extra pass makes
        // sure the accent matches the final theme even when the theme did not change at all.
        window.DispatcherQueue.TryEnqueue(() => Run(ApplyAccent));
    }

    private static SolidColorBrush? FindBrush(ResourceDictionary resources, string key)
    {
        try
        {
            return resources[key] as SolidColorBrush;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnAppearancePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppearanceSettings.ThemeMode):
                Run(() =>
                {
                    ApplyThemeMode();
                    ApplyTitleBar();
                    // The accent steps follow the theme; ActualThemeChanged recomputes them once
                    // the new theme is actually applied.
                    ApplyAccent();
                });
                break;
            case nameof(AppearanceSettings.AccentSource):
            case nameof(AppearanceSettings.CustomAccentColor):
                Run(ApplyAccent);
                break;
            case nameof(AppearanceSettings.UseColoredIcons):
                Run(ApplyDeviceIcons);
                break;
        }
    }

    private void OnActualThemeChanged()
    {
        // The accent steps depend on the theme, so a theme switch has to recompute them.
        ApplyAccent();
        ApplyTitleBar();
    }

    private void ApplyThemeMode()
    {
        if (_window is null)
        {
            return;
        }

        _window.Root.RequestedTheme = _appearance.ThemeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void ApplyAccent()
    {
        Color baseAccent = ResolveAccentColor();
        AccentPalette palette = AccentPalette.FromBase(baseAccent);
        bool dark = _window?.Root.ActualTheme == ElementTheme.Dark;

        // The base accent is the colour the user picked (or the system accent). Light themes
        // draw it with a darker step, dark themes with a lighter, less saturated step, the same
        // way the system derives its own accent brushes from SystemAccentColor*.
        Color effective = dark ? palette.Light2 : palette.Dark1;
        Color onAccent = dark ? _darkOnAccent : _lightOnAccent;

        _currentAccent = effective;

        Repaint(_accentBase, baseAccent);
        Repaint(_appAccent, effective);
        Repaint(_appAccentSubtle, effective, 0.12);
        Repaint(_appOnAccent, onAccent);

        // High contrast owns every system color: overriding these keys there would break the
        // contrast guarantees of the HC theme dictionaries.
        if (!HighContrast.IsEnabled)
        {
            Repaint(_systemBrushes["AccentFillColorDefaultBrush"], effective);
            Repaint(_systemBrushes["AccentFillColorSecondaryBrush"], effective, 0.9);
            Repaint(_systemBrushes["AccentFillColorTertiaryBrush"], effective, 0.8);
            Repaint(_systemBrushes["TextOnAccentFillColorPrimaryBrush"], onAccent);
            Repaint(_systemBrushes["TextOnAccentFillColorSecondaryBrush"], onAccent, 0.7);
            Repaint(_systemBrushes["AccentTextFillColorPrimaryBrush"], dark ? palette.Light3 : palette.Dark2);
            Repaint(_systemBrushes["AccentTextFillColorSecondaryBrush"], dark ? palette.Light3 : palette.Dark3);
            Repaint(_systemBrushes["AccentTextFillColorTertiaryBrush"], dark ? palette.Light2 : palette.Dark1);

            ReplaceColor("SystemAccentColor", palette.Accent);
            ReplaceColor("SystemAccentColorLight1", palette.Light1);
            ReplaceColor("SystemAccentColorLight2", palette.Light2);
            ReplaceColor("SystemAccentColorLight3", palette.Light3);
            ReplaceColor("SystemAccentColorDark1", palette.Dark1);
            ReplaceColor("SystemAccentColorDark2", palette.Dark2);
            ReplaceColor("SystemAccentColorDark3", palette.Dark3);
        }

        ApplyDeviceIcons();
        AccentApplied?.Invoke();
    }

    private Color ResolveAccentColor()
    {
        if (HighContrast.IsEnabled || _appearance.AccentSource == AccentColorSource.System)
        {
            return _uiSettings.GetColorValue(UIColorType.Accent);
        }

        return ParseHexColor(_appearance.CustomAccentColor);
    }

    private void ApplyDeviceIcons()
    {
        if (_deviceIcon is null || _deviceIconBackdrop is null)
        {
            return;
        }

        if (_appearance.UseColoredIcons && !HighContrast.IsEnabled)
        {
            Repaint(_deviceIcon, _currentAccent);
            Repaint(_deviceIconBackdrop, _currentAccent, 0.12);
        }
        else
        {
            // Theme-aware neutral: the resource lookup for TextFillColorSecondaryBrush resolves a
            // theme dictionary entry only on some WinUI versions, so derive from the live theme.
            bool dark = _window?.Root.ActualTheme == ElementTheme.Dark;
            Color neutral = dark
                ? Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x9A, 0x00, 0x00, 0x00);
            Repaint(_deviceIcon, neutral);
            Repaint(_deviceIconBackdrop, neutral, 0.12);
        }
    }

    private void ApplyTitleBar()
    {
        if (_window is null)
        {
            return;
        }

        var bar = _window.AppWindow.TitleBar;
        if (_window.Root.ActualTheme == ElementTheme.Dark)
        {
            bar.ButtonBackgroundColor = Transparent;
            bar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            bar.ButtonHoverBackgroundColor = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
            bar.ButtonPressedBackgroundColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
            bar.ButtonInactiveBackgroundColor = Transparent;
            bar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
        }
        else
        {
            bar.ButtonBackgroundColor = Transparent;
            bar.ButtonForegroundColor = Color.FromArgb(0xFF, 0x17, 0x17, 0x17);
            bar.ButtonHoverBackgroundColor = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
            bar.ButtonPressedBackgroundColor = Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
            bar.ButtonInactiveBackgroundColor = Transparent;
            bar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x75, 0x75, 0x75);
        }
    }

    private void Run(Action action)
    {
        if (_window?.DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            queue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    private static void Repaint(SolidColorBrush? brush, Color color, double? opacity = null)
    {
        if (brush is null)
        {
            return;
        }

        brush.Color = color;
        if (opacity is { } value)
        {
            brush.Opacity = value;
        }
    }

    private void ReplaceColor(string key, Color color)
    {
        try
        {
            if (_resources is not null)
            {
                _resources[key] = color;
            }
            else
            {
                Application.Current.Resources[key] = color;
            }
        }
        catch (Exception)
        {
        }
    }


    private static Color ParseHexColor(string hex)
    {
        try
        {
            return ParseHex(hex);
        }
        catch (Exception)
        {
            return ParseHex(AppearanceSettings.DefaultAccentColor);
        }
    }

    private static Color ParseHex(string hex)
    {
        string value = hex.TrimStart('#');
        return Color.FromArgb(
            0xFF,
            Convert.ToByte(value.Substring(0, 2), 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }


    private static Color Transparent => Color.FromArgb(0x00, 0x00, 0x00, 0x00);
}

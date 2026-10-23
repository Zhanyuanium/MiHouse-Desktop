using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHouse.App.Appearance;
using MiHouse.App.ViewModels;
using MiHouse.Core.Settings;
using Windows.UI;

namespace MiHouse.App.Views;

public sealed partial class SettingsPage : Page, INotifyPropertyChanged
{
    /// <summary>右侧控件列的宽度（宽窗口）。</summary>
    private const double _wideControlWidth = 220;

    /// <summary>右侧控件列加色块按钮预留槽的总宽度（宽窗口）。</summary>
    private const double _wideZoneWidth = 260;

    /// <summary>右侧控件列的宽度（窄窗口）。</summary>
    private const double _narrowControlWidth = 140;

    /// <summary>右侧控件列加色块按钮预留槽的总宽度（窄窗口）。</summary>
    private const double _narrowZoneWidth = 180;

    /// <summary>低于该宽度时收窄右侧控件列，避免描述文字被挤没。</summary>
    private const double _narrowLayoutThreshold = 560;

    private bool _syncing;
    private double _controlWidth = _wideControlWidth;
    private double _controlZoneWidth = _wideZoneWidth;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>下拉框宽度，随窗口宽度收窄。</summary>
    public double ControlWidth => _controlWidth;

    /// <summary>右侧控件列总宽度，随窗口宽度收窄。</summary>
    public double ControlZoneWidth => _controlZoneWidth;

    public SettingsPage()
    {
        InitializeComponent();
        AboutCard.Header = $"关于 {AppResources.DisplayName}";
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HighContrast.Changed -= OnHighContrastChanged;
    }

    private AppearanceSettings? Appearance => (DataContext as DevicesViewModel)?.Appearance;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HighContrast.Changed -= OnHighContrastChanged;
        HighContrast.Changed += OnHighContrastChanged;
        SyncFromSettings();
        UpdateAccentControlsEnabled();
    }

    private void OnHighContrastChanged() => DispatcherQueue.TryEnqueue(UpdateAccentControlsEnabled);

    /// <summary>
    /// Keeps the trailing control column inside the card: on narrow windows the dropdowns
    /// shrink so the description keeps enough room and nothing overlaps or overflows.
    /// </summary>
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < _narrowLayoutThreshold;
        double zone = narrow ? _narrowZoneWidth : _wideZoneWidth;
        double control = narrow ? _narrowControlWidth : _wideControlWidth;
        if (Math.Abs(zone - _controlZoneWidth) < 0.5 && Math.Abs(control - _controlWidth) < 0.5)
        {
            return;
        }

        _controlZoneWidth = zone;
        _controlWidth = control;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ControlZoneWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ControlWidth)));
    }

    private void SyncFromSettings()
    {
        AppearanceSettings? appearance = Appearance;
        if (appearance is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            ThemeModeSelector.SelectedIndex = (int)appearance.ThemeMode;
            AccentSourceSelector.SelectedIndex = (int)appearance.AccentSource;
            AccentPicker.Color = ParseHex(appearance.CustomAccentColor);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnThemeModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && Appearance is { } appearance && ThemeModeSelector.SelectedIndex >= 0)
        {
            appearance.ThemeMode = (AppThemeMode)ThemeModeSelector.SelectedIndex;
        }
    }

    private void OnAccentSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && Appearance is { } appearance && AccentSourceSelector.SelectedIndex >= 0)
        {
            appearance.AccentSource = (AccentColorSource)AccentSourceSelector.SelectedIndex;
        }
    }

    private void OnAccentColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncing || Appearance is not { } appearance)
        {
            return;
        }

        _syncing = true;
        try
        {
            appearance.CustomAccentColor = ToHex(args.NewColor);
            if (appearance.AccentSource != AccentColorSource.Custom)
            {
                appearance.AccentSource = AccentColorSource.Custom;
            }
        }
        finally
        {
            _syncing = false;
        }

        AccentSourceSelector.SelectedIndex = (int)AccentColorSource.Custom;
    }

    private void UpdateAccentControlsEnabled()
    {
        bool enabled = !HighContrast.IsEnabled;
        AccentSourceSelector.IsEnabled = enabled;
        AccentSwatchButton.IsEnabled = enabled;
    }

    private static Color ParseHex(string hex)
    {
        string value = hex.TrimStart('#');
        if (value.Length != 6)
        {
            return Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
        }

        return Color.FromArgb(
            0xFF,
            Convert.ToByte(value.Substring(0, 2), 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

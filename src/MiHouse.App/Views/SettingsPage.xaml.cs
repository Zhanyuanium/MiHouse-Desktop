using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiHouse.App.Appearance;
using MiHouse.App.ViewModels;
using MiHouse.Core.Settings;
using Windows.UI;

namespace MiHouse.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _syncing;

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

using MiHouse.Core.Settings;
using Windows.UI.ViewManagement;

namespace MiHouse.App.Appearance;

/// <summary>
/// Single gate for the application's own motion. Motion plays only when the user has not turned
/// it off in the app and Windows still has animations enabled. Self-drawn animations and built-in
/// progress indicators read <see cref="IsEnabled"/> instead of consulting
/// <see cref="UISettings.AnimationsEnabled"/> directly.
/// </summary>
public static class AppMotion
{
    private static AppearanceSettings? _appearance;
    private static UISettings? _uiSettings;
    private static bool _isEnabled = true;

    /// <summary>Raised when <see cref="IsEnabled"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>Gets whether the application's own motion may play.</summary>
    public static bool IsEnabled => _isEnabled;

    /// <summary>
    /// Wires the app and system animation toggles together. Call once at startup with the loaded
    /// settings instance.
    /// </summary>
    public static void Initialize(AppearanceSettings appearance)
    {
        _appearance = appearance;
        _uiSettings = new UISettings();
        _uiSettings.AnimationsEnabledChanged += (_, _) => Refresh();
        appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppearanceSettings.AnimationsEnabled))
            {
                Refresh();
            }
        };
        Refresh();
    }

    private static void Refresh()
    {
        bool next = (_appearance?.AnimationsEnabled ?? true) && (_uiSettings?.AnimationsEnabled ?? true);
        if (next == _isEnabled)
        {
            return;
        }

        _isEnabled = next;
        Changed?.Invoke();
    }
}

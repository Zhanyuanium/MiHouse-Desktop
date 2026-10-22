using Windows.UI.ViewManagement;

namespace MiHouse.App.Appearance;

/// <summary>
/// Guards high-contrast detection. In a sandboxed AppContainer the
/// <see cref="AccessibilitySettings.HighContrastChanged"/> event can fail to subscribe
/// (COMException 0x80070490), so the event is best-effort and every read degrades to
/// <see langword="false"/> rather than crashing.
/// </summary>
public static class HighContrast
{
    private static AccessibilitySettings? _settings;

    /// <summary>Gets whether the system is in high-contrast mode.</summary>
    public static bool IsEnabled { get; private set; }

    /// <summary>Raised when <see cref="IsEnabled"/> changes, when the event is available.</summary>
    public static event Action? Changed;

    /// <summary>Detects the initial state and, when possible, subscribes to changes.</summary>
    public static void Initialize()
    {
        try
        {
            _settings = new AccessibilitySettings();
            IsEnabled = _settings.HighContrast;
        }
        catch (Exception)
        {
            _settings = null;
            IsEnabled = false;
        }

        if (_settings is null)
        {
            return;
        }

        try
        {
            _settings.HighContrastChanged += OnHighContrastChanged;
        }
        catch (Exception)
        {
            // The change event cannot be subscribed in this sandbox; keep the initial value.
        }
    }

    private static void OnHighContrastChanged(AccessibilitySettings sender, object args)
    {
        try
        {
            IsEnabled = _settings?.HighContrast ?? false;
            Changed?.Invoke();
        }
        catch (Exception)
        {
            IsEnabled = false;
        }
    }
}

using Microsoft.Windows.ApplicationModel.Resources;

namespace MiHouse.App;

/// <summary>
/// Localized application strings that must also match the MSIX manifest. The defaults keep
/// the app usable when the package resources cannot be loaded, such as a raw unpackaged run.
/// </summary>
internal static class AppResources
{
    private const string _fallbackDisplayName = "MiHouse";

    public static string DisplayName { get; } = ReadString("AppDisplayName", _fallbackDisplayName);

    private static string ReadString(string key, string fallback)
    {
        try
        {
            string value = new ResourceLoader().GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

using Microsoft.Windows.ApplicationModel.Resources;

namespace MiHouse.App;

/// <summary>
/// Localized application strings that must also match the MSIX manifest. The defaults keep
/// the app usable when the package resources cannot be loaded, such as a raw unpackaged run.
/// </summary>
internal static class AppResources
{
    private const string _fallbackDisplayName = "MiHouse";
    private const string _fallbackDescription = "Unofficial Xiaomi Mi Home desktop client";
    private const string _fallbackAboutTitle = "About MiHouse";

    public static string DisplayName { get; } = ReadString("AppDisplayName", _fallbackDisplayName);

    /// <summary>Product name shown next to the icon in About; Chinese locales include the English name.</summary>
    public static string AboutDisplayName { get; } = ReadString("AppDisplayNameBilingual", DisplayName);

    /// <summary>Dialog and settings-row title; Chinese joins 关于 to the name without a space.</summary>
    public static string AboutTitle { get; } = ReadString("AboutTitle", _fallbackAboutTitle);

    public static string Description { get; } = ReadString("AppDescription", _fallbackDescription);

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

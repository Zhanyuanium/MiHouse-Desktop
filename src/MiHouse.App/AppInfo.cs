using System.Reflection;

namespace MiHouse.App;

/// <summary>
/// Facts about this build that the About dialog shows. The publisher string is the friendly
/// name in <c>Properties/PublisherDisplayName</c>; the signing certificate and the MSIX
/// <c>Identity/@Publisher</c> deliberately use the ASCII subject <c>CN=Zhanyuanium</c> so the
/// two X.500 names compare exactly.
/// </summary>
internal static class AppInfo
{
    /// <summary>Human-readable publisher name, shown in the About dialog and Windows settings.</summary>
    public const string Publisher = "浪迹星海@Zhanyuanium";

    /// <summary>SPDX identifier of the license this project is distributed under.</summary>
    public const string License = "GPL-3.0-or-later";

    /// <summary>Public source repository.</summary>
    public const string RepositoryUrl = "https://github.com/Zhanyuanium/MiHouse-Desktop";

    private static readonly string _informationalVersion = ReadInformationalVersion();

    /// <summary>Product name for the About header; Chinese locales append the English name.</summary>
    public static string AboutDisplayName => AppResources.AboutDisplayName;

    /// <summary>Localized "About <name>" title for the dialog and the settings row.</summary>
    public static string AboutTitle => AppResources.AboutTitle;

    /// <summary>Short application description, localized and kept in sync with the MSIX description.</summary>
    public static string Description => AppResources.Description;

    /// <summary>Product version without build metadata, for example <c>1.0.0</c>.</summary>
    public static string Version { get; } = _informationalVersion.Split('+', 2)[0];

    /// <summary>Abbreviated source revision of this build, or an empty string when unavailable.</summary>
    public static string BuildRevision { get; } = ReadBuildRevision();

    /// <summary>Copyright line taken from the assembly metadata.</summary>
    public static string Copyright { get; } = ReadCopyright();

    /// <summary>Absolute path to the bundled GPL text, or <see langword="null"/> when it is missing.</summary>
    public static string? LicenseTextPath { get; } = ExistingFile("LICENSE.txt");

    /// <summary>Absolute path to the bundled third-party notices, or <see langword="null"/> when missing.</summary>
    public static string? ThirdPartyNoticesPath { get; } = ExistingFile("THIRD-PARTY-NOTICES.txt");

    private static string ReadInformationalVersion()
    {
        Assembly assembly = typeof(AppInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0.0";
    }

    private static string ReadBuildRevision()
    {
        int separator = _informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (separator < 0 || separator == _informationalVersion.Length - 1)
        {
            return string.Empty;
        }

        string revision = _informationalVersion[(separator + 1)..];
        return revision.Length > 7 ? revision[..7] : revision;
    }

    private static string ReadCopyright() =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    private static string? ExistingFile(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(path) ? path : null;
    }
}

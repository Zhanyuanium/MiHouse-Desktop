namespace MiHouse.Core.Cloud;

/// <summary>
/// Mi Home cloud hosts.
/// </summary>
public static class MiHomeEndpoints
{
    private static readonly HashSet<string> _knownRegions =
        new(StringComparer.OrdinalIgnoreCase) { "cn", "de", "i2", "ru", "sg", "us" };

    /// <summary>
    /// Regions the client probes when the account region is unknown.
    /// </summary>
    public static IReadOnlyList<string> Regions { get; } = ["cn", "de", "i2", "ru", "sg", "us"];

    /// <summary>
    /// Returns the API base address for a region, for example <c>https://api.io.mi.com/app/</c>.
    /// </summary>
    public static Uri AppBase(string region)
    {
        if (!_knownRegions.Contains(region))
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "Unknown Mi Home region.");
        }

        return region.Equals("cn", StringComparison.OrdinalIgnoreCase)
            ? new Uri("https://api.io.mi.com/app/")
            : new Uri($"https://{region}.api.io.mi.com/app/");
    }
}

namespace MiHouse.Core.Spec;

/// <summary>
/// UI section a spec service belongs to.
/// </summary>
public enum SpecSection
{
    /// <summary>Primary controls and everyday status.</summary>
    Normal,

    /// <summary>Advanced settings, maintenance and diagnostics.</summary>
    Advanced,
}

/// <summary>
/// Classifies spec services into normal and advanced/diagnostic sections.
/// </summary>
/// <remarks>
/// This is a heuristic based on the miot-spec service categories published by device
/// vendors, not on per-device exceptions. Matching uses only the category segment of a
/// service URN, so keywords in the vendor namespace or model name are ignored.
/// </remarks>
public static class SpecSections
{
    private static readonly HashSet<string> _advancedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "device-information",
        "machine-state",
        "system-parm",
        "enhance",
        "flag-bit",
        "dev-mode",
        "maintenance",
        "alarm",
        "fault",
        "power-consumption",
        "electricity",
        "diagnostic",
    };

    /// <summary>
    /// Classifies a service by its type URN.
    /// </summary>
    public static SpecSection Classify(SpecService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return IsAdvancedService(service.Type) ? SpecSection.Advanced : SpecSection.Normal;
    }

    /// <summary>
    /// Returns whether a service type belongs to an advanced or diagnostic category.
    /// Blank types and URNs without a recognizable service category are normal.
    /// </summary>
    public static bool IsAdvancedService(string? serviceType)
    {
        var category = GetServiceCategory(serviceType);
        return category is not null && _advancedCategories.Contains(category);
    }

    private static string? GetServiceCategory(string? serviceType)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
        {
            return null;
        }

        var segments = serviceType.Trim().Split(':');
        if (segments.Length < 4
            || !segments[0].Equals("urn", StringComparison.OrdinalIgnoreCase)
            || !IsSpecNamespace(segments[1])
            || !segments[2].Equals("service", StringComparison.OrdinalIgnoreCase)
            || segments[3].Length == 0)
        {
            return null;
        }

        return segments[3];
    }

    private static bool IsSpecNamespace(string segment)
    {
        return segment.Equals("miot-spec-v2", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith("-spec", StringComparison.OrdinalIgnoreCase);
    }
}

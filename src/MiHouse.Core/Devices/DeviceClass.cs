namespace MiHouse.Core.Devices;

/// <summary>
/// Helpers for grouping devices by the category segment of their miot-spec URN.
/// </summary>
public static class DeviceClass
{
    private const int _otherSortOrder = 1000;

    /// <summary>
    /// Extracts the device category from a miot-spec URN, for example
    /// <c>light</c> from <c>urn:miot-spec-v2:device:light:0000A001:test-one:1</c>.
    /// Vendor URNs such as <c>urn:yeelink-spec:device:light:...</c> are accepted.
    /// </summary>
    /// <returns>The lower-case category, or <see langword="null"/> for blank or malformed URNs.</returns>
    public static string? FromSpecType(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var segments = urn.Trim().Split(':');
        if (segments.Length < 7)
        {
            return null;
        }

        if (!segments[0].Equals("urn", StringComparison.OrdinalIgnoreCase)
            || !IsSpecNamespace(segments[1])
            || !segments[2].Equals("device", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var category = segments[3];
        if (!IsCategory(category))
        {
            return null;
        }

        for (var index = 4; index < segments.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(segments[index]))
            {
                return null;
            }
        }

        return category.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the Chinese label shown for a device category.
    /// </summary>
    /// <returns>
    /// The known label; otherwise the trimmed category as supplied, or <c>其他</c> when blank.
    /// </returns>
    public static string DisplayName(string? category)
    {
        var trimmed = category?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "其他";
        }

        return trimmed.ToLowerInvariant() switch
        {
            "light" => "灯",
            "air-conditioner" => "空调",
            "switch" => "开关",
            "sensor" => "传感器",
            "curtain" => "窗帘",
            "air-purifier" => "空气净化器",
            "humidifier" => "加湿器",
            "cooker" => "厨电",
            "gateway" => "网关",
            "camera" => "摄像头",
            "water-purifier" => "净水器",
            "heater" => "取暖器",
            "fan" => "风扇",
            "vacuum" => "扫地机",
            _ => trimmed,
        };
    }

    /// <summary>
    /// Ordering key for quick-switch groups: light, switch, air-conditioner, curtain,
    /// heater/fan, air-purifier/humidifier, sensor, then everything else.
    /// </summary>
    public static int SortOrder(string? category)
    {
        var trimmed = category?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return _otherSortOrder;
        }

        return trimmed.ToLowerInvariant() switch
        {
            "light" => 100,
            "switch" => 200,
            "air-conditioner" => 300,
            "curtain" => 400,
            "heater" => 500,
            "fan" => 510,
            "air-purifier" => 600,
            "humidifier" => 610,
            "sensor" => 700,
            _ => _otherSortOrder,
        };
    }

    private static bool IsSpecNamespace(string segment)
    {
        return segment.Equals("miot-spec-v2", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith("-spec", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCategory(string category)
    {
        if (category.Length == 0 || !char.IsAsciiLetter(category[0]))
        {
            return false;
        }

        foreach (var character in category)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}


namespace MiHouse.Core.Devices;

/// <summary>
/// Helpers for grouping devices by the category segment of their miot-spec URN.
/// </summary>
public static class DeviceClass
{
    private const int _otherSortOrder = 2000;

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
    /// The known label; otherwise <c>其他</c>.
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
            "health-pot" => "养生壶",
            "hood" => "油烟机",
            "water-heater" => "热水器",
            "induction-cooker" => "电磁炉",
            "occupancy-sensor" => "人体传感器",
            "speaker" => "音箱",
            "air-fryer" => "空气炸锅",
            "temperature-humidity-sensor" => "温湿度传感器",
            "toothbrush" => "牙刷",
            "rice-cooker" => "电饭煲",
            "microwave" => "微波炉",
            "kettle" => "电水壶",
            "dehumidifier" => "除湿机",
            "plug" => "插座",
            "power-strip" => "插排",
            "door-lock" => "门锁",
            "door-sensor" => "门窗传感器",
            "motion-sensor" => "人体传感器",
            "button" => "无线开关",
            "washer" => "洗衣机",
            "refrigerator" => "冰箱",
            "router" => "路由器",
            "tv" => "电视",
            "robot-vacuum" => "扫地机",
            _ => "其他",
        };
    }

    /// <summary>
    /// Ordering key for quick-switch groups: lighting, switches and plugs, climate,
    /// coverage and sensors, air and cleaning, kitchen, then media and the rest.
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
            "button" => 210,
            "plug" => 220,
            "power-strip" => 230,
            "air-conditioner" => 300,
            "curtain" => 400,
            "door-lock" => 410,
            "door-sensor" => 420,
            "motion-sensor" => 430,
            "occupancy-sensor" => 440,
            "heater" => 500,
            "fan" => 510,
            "water-heater" => 520,
            "dehumidifier" => 530,
            "air-purifier" => 600,
            "humidifier" => 610,
            "temperature-humidity-sensor" => 620,
            "sensor" => 700,
            "vacuum" => 800,
            "robot-vacuum" => 810,
            "washer" => 820,
            "refrigerator" => 830,
            "water-purifier" => 840,
            "cooker" => 900,
            "rice-cooker" => 910,
            "microwave" => 920,
            "induction-cooker" => 930,
            "air-fryer" => 940,
            "kettle" => 950,
            "health-pot" => 960,
            "hood" => 970,
            "speaker" => 1000,
            "tv" => 1010,
            "router" => 1020,
            "gateway" => 1030,
            "camera" => 1040,
            "toothbrush" => 1050,
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

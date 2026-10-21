using MiHouse.Core.Devices;

namespace MiHouse.Core.Tests.Devices;

public sealed class DeviceClassTests
{
    [Theory]
    [InlineData("urn:miot-spec-v2:device:light:0000A001:test-one:1", "light")]
    [InlineData("urn:miot-spec-v2:device:air-conditioner:0000A004:test-ac:1", "air-conditioner")]
    [InlineData("urn:miot-spec-v2:device:switch:0000A003:test-switch:1:0000D010", "switch")]
    [InlineData("urn:yeelink-spec:device:curtain:0000A00C:test-curtain:1", "curtain")]
    [InlineData("URN:MIOT-SPEC-V2:DEVICE:LIGHT:0000A001:test-one:1", "light")]
    public void FromSpecType_ExtractsCategory(string urn, string expected)
    {
        Assert.Equal(expected, DeviceClass.FromSpecType(urn));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-urn")]
    [InlineData("urn:miot-spec-v2:service:light:0000A001:test-one:1")]
    [InlineData("urn:miot-spec-v2:device:light:0000A001")]
    [InlineData("urn:miot-spec-v2:device::0000A001:test-one:1")]
    [InlineData("urn:other:device:light:0000A001:test-one:1")]
    [InlineData("urn:miot-spec-v2:device:light:0000A001::1")]
    [InlineData("urn:miot-spec-v2:device:air conditioner:0000A001:test-one:1")]
    public void FromSpecType_ReturnsNullForMalformedUrn(string? urn)
    {
        Assert.Null(DeviceClass.FromSpecType(urn));
    }

    [Theory]
    [InlineData("light", "灯")]
    [InlineData("air-conditioner", "空调")]
    [InlineData("switch", "开关")]
    [InlineData("sensor", "传感器")]
    [InlineData("curtain", "窗帘")]
    [InlineData("air-purifier", "空气净化器")]
    [InlineData("humidifier", "加湿器")]
    [InlineData("cooker", "厨电")]
    [InlineData("gateway", "网关")]
    [InlineData("camera", "摄像头")]
    [InlineData("water-purifier", "净水器")]
    [InlineData("heater", "取暖器")]
    [InlineData("fan", "风扇")]
    [InlineData("vacuum", "扫地机")]
    [InlineData("health-pot", "养生壶")]
    [InlineData("hood", "油烟机")]
    [InlineData("water-heater", "热水器")]
    [InlineData("induction-cooker", "电磁炉")]
    [InlineData("occupancy-sensor", "人体传感器")]
    [InlineData("speaker", "音箱")]
    [InlineData("air-fryer", "空气炸锅")]
    [InlineData("temperature-humidity-sensor", "温湿度传感器")]
    [InlineData("toothbrush", "牙刷")]
    [InlineData("rice-cooker", "电饭煲")]
    [InlineData("microwave", "微波炉")]
    [InlineData("kettle", "电水壶")]
    [InlineData("dehumidifier", "除湿机")]
    [InlineData("plug", "插座")]
    [InlineData("power-strip", "插排")]
    [InlineData("door-lock", "门锁")]
    [InlineData("door-sensor", "门窗传感器")]
    [InlineData("motion-sensor", "人体传感器")]
    [InlineData("button", "无线开关")]
    [InlineData("washer", "洗衣机")]
    [InlineData("refrigerator", "冰箱")]
    [InlineData("router", "路由器")]
    [InlineData("tv", "电视")]
    [InlineData("robot-vacuum", "扫地机")]
    [InlineData("LIGHT", "灯")]
    [InlineData("  Hood  ", "油烟机")]
    public void DisplayName_ReturnsLabelForKnownCategory(string category, string expected)
    {
        Assert.Equal(expected, DeviceClass.DisplayName(category));
    }

    [Fact]
    public void DisplayName_FallsBackToOtherNeverRawEnglish()
    {
        Assert.Equal("其他", DeviceClass.DisplayName("toaster"));
        Assert.Equal("其他", DeviceClass.DisplayName("  toaster  "));
        Assert.Equal("其他", DeviceClass.DisplayName("not-a-real-category"));
        Assert.Equal("其他", DeviceClass.DisplayName(null));
        Assert.Equal("其他", DeviceClass.DisplayName("   "));
    }

    [Fact]
    public void SortOrder_IsMonotonicForQuickSwitchGroups()
    {
        string[] orderedCategories =
        [
            "light",
            "switch",
            "button",
            "plug",
            "power-strip",
            "air-conditioner",
            "curtain",
            "door-lock",
            "door-sensor",
            "motion-sensor",
            "occupancy-sensor",
            "heater",
            "fan",
            "water-heater",
            "dehumidifier",
            "air-purifier",
            "humidifier",
            "temperature-humidity-sensor",
            "sensor",
            "vacuum",
            "robot-vacuum",
            "washer",
            "refrigerator",
            "water-purifier",
            "cooker",
            "rice-cooker",
            "microwave",
            "induction-cooker",
            "air-fryer",
            "kettle",
            "health-pot",
            "hood",
            "speaker",
            "tv",
            "router",
            "gateway",
            "camera",
            "toothbrush",
        ];

        var previous = int.MinValue;
        foreach (var category in orderedCategories)
        {
            var order = DeviceClass.SortOrder(category);

            Assert.True(order > previous, $"{category} must sort after the previous group.");
            previous = order;
        }
    }

    [Fact]
    public void SortOrder_PutsUnlistedCategoriesLast()
    {
        Assert.Equal(DeviceClass.SortOrder("toaster"), DeviceClass.SortOrder(null));
        Assert.True(DeviceClass.SortOrder("toaster") > DeviceClass.SortOrder("toothbrush"));
        Assert.Equal(DeviceClass.SortOrder("toaster"), DeviceClass.SortOrder("unknown"));
    }
}

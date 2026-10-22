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

    [Theory]
    [InlineData(12, "light")]
    [InlineData(2, "other")]
    [InlineData(3, "other")]
    [InlineData(4, "other")]
    [InlineData(5, "other")]
    [InlineData(7, "other")]
    [InlineData(9, "other")]
    [InlineData(11, "other")]
    [InlineData(16, "other")]
    [InlineData(99, "other")]
    public void FromSubclassCategory_MapsOnlyLighting(int categoryId, string expected)
    {
        Assert.Equal(expected, DeviceClass.FromSubclassCategory(categoryId));
    }

    [Fact]
    public void ResolveCategory_PrefersSubclassThenDeviceSpecThenDefinition()
    {
        var classified = new MiDevice("1", "m", "D", null, true,
            SpecType: "urn:miot-spec-v2:device:switch:0000A003:test-switch:1:0000D010",
            SubclassCategory: "light");
        var unclassified = classified with { SubclassCategory = null };

        Assert.Equal("light", DeviceClass.ResolveCategory(
            classified,
            "urn:miot-spec-v2:device:fan:0000A00C:test-fan:1"));
        Assert.Equal("switch", DeviceClass.ResolveCategory(
            unclassified,
            "urn:miot-spec-v2:device:fan:0000A00C:test-fan:1"));
        Assert.Equal("fan", DeviceClass.ResolveCategory(
            unclassified with { SpecType = null },
            "urn:miot-spec-v2:device:fan:0000A00C:test-fan:1"));
        Assert.Null(DeviceClass.ResolveCategory(unclassified with { SpecType = null }, null));
    }

    [Fact]
    public void IsSplitParent_TrueOnlyForASwitchParentOfSplitRows()
    {
        const string switchUrn = "urn:miot-spec-v2:device:switch:0000A003:test-w3:1";
        var parent = new MiDevice("parent", "m", "Parent", null, true, SpecType: switchUrn);
        var channel = new MiDevice("parent.s2", "m", "Channel", null, true, "parent", 2, switchUrn);
        var standalone = new MiDevice("standalone", "m", "Standalone", null, true, SpecType: switchUrn);
        // A non-switch device that happens to have sub-devices keeps its own row.
        var gateway = new MiDevice("gateway", "m", "Gateway", null, true,
            SpecType: "urn:miot-spec-v2:device:gateway:0000A019:test-gw:1");
        var splitParentDids = new HashSet<string>(StringComparer.Ordinal) { "parent", "gateway" };

        Assert.True(DeviceClass.IsSplitParent(parent, splitParentDids));
        Assert.False(DeviceClass.IsSplitParent(channel, splitParentDids));
        Assert.False(DeviceClass.IsSplitParent(standalone, splitParentDids));
        Assert.False(DeviceClass.IsSplitParent(gateway, splitParentDids));
    }
}

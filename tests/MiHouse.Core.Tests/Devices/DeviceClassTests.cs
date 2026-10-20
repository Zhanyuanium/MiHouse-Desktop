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
    [InlineData("LIGHT", "灯")]
    public void DisplayName_ReturnsLabelForKnownCategory(string category, string expected)
    {
        Assert.Equal(expected, DeviceClass.DisplayName(category));
    }

    [Fact]
    public void DisplayName_FallsBackToCategoryOrOther()
    {
        Assert.Equal("toaster", DeviceClass.DisplayName("toaster"));
        Assert.Equal("toaster", DeviceClass.DisplayName("  toaster  "));
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
            "air-conditioner",
            "curtain",
            "heater",
            "fan",
            "air-purifier",
            "humidifier",
            "sensor",
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
        Assert.True(DeviceClass.SortOrder("toaster") > DeviceClass.SortOrder("sensor"));
        Assert.Equal(DeviceClass.SortOrder("gateway"), DeviceClass.SortOrder("unknown"));
    }
}

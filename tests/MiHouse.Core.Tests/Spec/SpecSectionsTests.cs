using MiHouse.Core.Spec;

namespace MiHouse.Core.Tests.Spec;

public sealed class SpecSectionsTests
{
    [Theory]
    [InlineData("device-information")]
    [InlineData("machine-state")]
    [InlineData("system-parm")]
    [InlineData("enhance")]
    [InlineData("flag-bit")]
    [InlineData("dev-mode")]
    [InlineData("maintenance")]
    [InlineData("alarm")]
    [InlineData("fault")]
    [InlineData("power-consumption")]
    [InlineData("electricity")]
    [InlineData("diagnostic")]
    public void Classify_MarksAdvancedCategoriesAsAdvanced(string category)
    {
        var urn = $"urn:miot-spec-v2:service:{category}:00007801:test-one:1";
        var service = new SpecService(1, urn, category, [], []);

        Assert.Equal(SpecSection.Advanced, SpecSections.Classify(service));
        Assert.True(SpecSections.IsAdvancedService(urn));
    }

    [Theory]
    [InlineData("urn:miot-spec-v2:service:switch:0000780C:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:light:00007802:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:air-conditioner:0000780D:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:fan:00007803:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:environment:0000780E:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:indicator-light:00007804:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:sensor:0000780F:test-one:1")]
    [InlineData("urn:acme-spec:service:custom:00007801:test-one:1")]
    public void Classify_MarksRepresentativeNormalServicesAsNormal(string urn)
    {
        var service = new SpecService(1, urn, "Service", [], []);

        Assert.Equal(SpecSection.Normal, SpecSections.Classify(service));
        Assert.False(SpecSections.IsAdvancedService(urn));
    }

    [Fact]
    public void IsAdvancedService_MatchesVendorNamespaceByCategory()
    {
        const string urn = "urn:zhimi-spec:service:system-parm:00007801:test-one:1";
        var service = new SpecService(1, urn, "System Parameters", [], []);

        Assert.True(SpecSections.IsAdvancedService(urn));
        Assert.Equal(SpecSection.Advanced, SpecSections.Classify(service));
    }

    [Fact]
    public void Classify_TreatsNullOrBlankTypeAsNormal()
    {
        Assert.False(SpecSections.IsAdvancedService(null));
        Assert.False(SpecSections.IsAdvancedService(""));
        Assert.False(SpecSections.IsAdvancedService("   "));
        Assert.Equal(SpecSection.Normal, SpecSections.Classify(new SpecService(1, string.Empty, "", [], [])));
        Assert.Equal(SpecSection.Normal, SpecSections.Classify(new SpecService(1, null!, "", [], [])));
    }

    [Theory]
    [InlineData("urn:miot-spec-v2:service:switch:0000780C:alarm-monitor:1")]
    [InlineData("urn:alarm-spec:service:switch:0000780C:test-one:1")]
    [InlineData("urn:miot-spec-v2:service:sensor:0000780D:fault-detector:1")]
    [InlineData("machine-state")]
    public void IsAdvancedService_IgnoresKeywordsOutsideServiceCategory(string urn)
    {
        Assert.False(SpecSections.IsAdvancedService(urn));
    }

    [Fact]
    public void IsAdvancedService_IgnoresCase()
    {
        Assert.True(SpecSections.IsAdvancedService(
            "URN:MIOT-SPEC-V2:SERVICE:MACHINE-STATE:00007801:test-one:1"));
    }
}

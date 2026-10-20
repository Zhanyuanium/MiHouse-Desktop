using MiHouse.Core.Devices;

namespace MiHouse.Core.Tests.Devices;

public sealed class InMemoryDeviceSecretStoreTests
{
    [Fact]
    public void ClearRemovesEveryToken()
    {
        var store = new InMemoryDeviceSecretStore();
        store.SetToken("a", "token-a");
        store.SetToken("b", "token-b");

        store.Clear();

        Assert.Null(store.GetToken("a"));
        Assert.Null(store.GetToken("b"));
    }

    [Fact]
    public void KeysAreCaseSensitive()
    {
        var store = new InMemoryDeviceSecretStore();
        store.SetToken("Device", "token");

        Assert.Equal("token", store.GetToken("Device"));
        Assert.Null(store.GetToken("device"));
    }
}

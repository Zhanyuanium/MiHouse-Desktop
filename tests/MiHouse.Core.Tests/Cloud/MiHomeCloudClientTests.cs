using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Cloud;
using MiHouse.Core.Crypto;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;

namespace MiHouse.Core.Tests.Cloud;

public sealed class MiHomeCloudClientTests
{
    private const string _ssecurity = "MDEyMzQ1Njc4OWFiY2RlZg==";
    private const string _nonce = "bm9uY2UwMDE=";
    private const string _serviceToken = "service-token";
    private const string _homesPayload =
        """{"limit":300,"fetch_share":true,"fetch_share_dev":true,"fetch_cariot":true,"plat_form":0,"app_ver":9,"fg":true}""";

    private static readonly MiHomeSession _session = new("12345", _serviceToken, _ssecurity, "cn");

    [Fact]
    public async Task GetHomesAsyncParsesOwnedAndSharedHomes()
    {
        const string response = """
            &&&START&&&{"code":0,"result":{
            "homelist":[
            {"id":1001,"uid":9001,"name":"Owned Home","shareflag":1,"roomlist":[
            {"id":11,"name":"Living","dids":["a","b"]},
            {"id":12,"dids":null},
            {"id":"13","name":"","dids":[]}]},
            {"id":"1002","uid":"9002","roomlist":null}],
            "share_home_list":[
            {"id":2001,"uid":"8001","name":"Shared Home","shareflag":0,
            "roomlist":[{"id":"21","name":"Guest","dids":["c"]}]}]
            }}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        var homes = await client.GetHomesAsync(_session, CancellationToken.None);

        Assert.Equal(3, homes.Count);

        Assert.Equal("1001", homes[0].Id);
        Assert.Equal("Owned Home", homes[0].Name);
        Assert.Equal("9001", homes[0].OwnerUserId);
        Assert.False(homes[0].IsShared);
        Assert.Equal(3, homes[0].Rooms.Count);
        AssertRoom(homes[0].Rooms[0], "11", "Living", "a", "b");
        AssertRoom(homes[0].Rooms[1], "12", "房间 12");
        AssertRoom(homes[0].Rooms[2], "13", "房间 13");

        Assert.Equal("1002", homes[1].Id);
        Assert.Equal("家庭 1002", homes[1].Name);
        Assert.Equal("9002", homes[1].OwnerUserId);
        Assert.False(homes[1].IsShared);
        Assert.Empty(homes[1].Rooms);

        Assert.Equal("2001", homes[2].Id);
        Assert.Equal("Shared Home", homes[2].Name);
        Assert.Equal("8001", homes[2].OwnerUserId);
        Assert.True(homes[2].IsShared);
        AssertRoom(Assert.Single(homes[2].Rooms), "21", "Guest", "c");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(new Uri("https://api.io.mi.com/app/v2/homeroom/gethome"), handler.RequestUri);
        Assert.Equal(
            $"userId=12345; serviceToken={_serviceToken}; yetAnotherServiceToken={_serviceToken}; " +
            "locale=zh_CN; timezone=GMT+08:00; is_daylight=0; dst_offset=0; channel=MI_APP_STORE",
            handler.Cookie);
        Assert.Equal("identity", handler.AcceptEncoding);
        Assert.Equal("PROTOCAL-HTTP2", handler.ProtocolFlag);
        Assert.Equal("ENCRYPT-RC4", handler.EncryptAlgorithm);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);

        var form = ParseForm(handler.Bodies[0]);
        Assert.Equal(
            new[] { "data", "rc4_hash__", "signature", "ssecurity", "_nonce" },
            form.Select(field => field.Key));

        var values = form.ToDictionary(field => field.Key, field => field.Value);
        var signedNonce = SignedNonce(_ssecurity, _nonce);
        var decryptedPayload = DecryptValue(values["data"]);

        Assert.Equal(_homesPayload, decryptedPayload);
        Assert.Equal(_ssecurity, values["ssecurity"]);
        Assert.Equal(_nonce, values["_nonce"]);

        var expectedHash = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(
            $$"""POST&/v2/homeroom/gethome&data={{_homesPayload}}&{{signedNonce}}""")));
        Assert.Equal(expectedHash, DecryptValue(values["rc4_hash__"]));

        var expectedSignature = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(
            $$"""POST&/v2/homeroom/gethome&data={{values["data"]}}&rc4_hash__={{values["rc4_hash__"]}}&{{signedNonce}}""")));
        Assert.Equal(expectedSignature, values["signature"]);
    }

    [Theory]
    [InlineData("""{"code":0,"result":{"homelist":null,"share_home_list":null}}""")]
    [InlineData("""{"code":0,"result":{"homelist":[]}}""")]
    [InlineData("""{"code":0,"result":null}""")]
    public async Task GetHomesAsyncToleratesMissingOrNullLists(string response)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var homes = await client.GetHomesAsync(_session, CancellationToken.None);

        Assert.Empty(homes);
    }

    [Theory]
    [InlineData("""{"code":0,"result":{"homelist":null,"share_home_list":[{"id":"2001","uid":"8001","name":"Shared","roomlist":[]}]}}""", 1)]
    [InlineData("""{"code":0,"result":{"homelist":[],"share_home_list":[{"id":"2001","uid":"8001","name":"Shared","roomlist":[]},{"id":"2002","uid":"8002","name":"Other","roomlist":[]}]}}""", 2)]
    public async Task GetHomesAsyncReturnsSharedHomesWhenOwnedListIsEmpty(string response, int expectedCount)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var homes = await client.GetHomesAsync(_session, CancellationToken.None);

        Assert.Equal(expectedCount, homes.Count);
        Assert.All(homes, home => Assert.True(home.IsShared));
    }

    [Fact]
    public async Task GetHomesAsyncDecryptsEncryptedResponse()
    {
        const string payload = """
            {"code":0,"result":{"homelist":[{"id":"7","uid":"8","name":"Encrypted","roomlist":[]}]}}
            """;
        var handler = new StubHttpMessageHandler(EncryptResponse(payload));
        var client = CreateClient(handler);

        var homes = await client.GetHomesAsync(_session, CancellationToken.None);

        var home = Assert.Single(homes);
        Assert.Equal("7", home.Id);
        Assert.Equal("Encrypted", home.Name);
        Assert.Equal("8", home.OwnerUserId);
        Assert.False(home.IsShared);
        Assert.Empty(home.Rooms);
    }

    [Fact]
    public async Task GetDevicesAsyncJoinsRoomsAndPaginates()
    {
        var home = new MiHome(
            "1001",
            "Owned Home",
            "9001",
            false,
            [new MiRoom("11", "Living", ["a", "orphan"]), new MiRoom("12", "Hall", ["b"])]);
        string[] responses =
        [
            """
            {"code":0,"result":{"device_info":[
            {"did":"a","model":"m.a","name":"A","isOnline":true},
            {"did":"b","model":"m.b","name":"B","online":1},
            {"did":"c","model":"m.c","name":"C","isOnline":false}],
            "has_more":true,"max_did":"b"}}
            """,
            """
            {"code":0,"result":{"device_info":[
            {"did":"d","model":"m.d","isOnline":true}],
            "has_more":false,"max_did":"d"}}
            """,
        ];
        var handler = new StubHttpMessageHandler(responses);
        var client = CreateClient(handler);

        var devices = await client.GetDevicesAsync(_session, home, CancellationToken.None);

        Assert.Equal(4, devices.Count);
        Assert.Equal(new MiDevice("a", "m.a", "A", "Living", true), devices[0]);
        Assert.Equal(new MiDevice("b", "m.b", "B", "Hall", true), devices[1]);
        Assert.Equal(new MiDevice("c", "m.c", "C", null, false), devices[2]);
        Assert.Equal(new MiDevice("d", "m.d", "m.d", null, true), devices[3]);

        const string firstPayload =
            """{"home_id":1001,"home_owner":9001,"limit":300,"start_did":"","get_split_device":true,"support_smart_home":true,"get_cariot_device":true,"get_third_device":true}""";
        const string secondPayload =
            """{"home_id":1001,"home_owner":9001,"limit":300,"start_did":"b","get_split_device":true,"support_smart_home":true,"get_cariot_device":true,"get_third_device":true}""";
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(firstPayload, DecryptRequestPayload(handler, index: 0));
        Assert.Equal(secondPayload, DecryptRequestPayload(handler, index: 1));
        Assert.All(
            handler.RequestUris,
            uri => Assert.Equal(new Uri("https://api.io.mi.com/app/v2/home/home_device_list"), uri));
    }

    [Fact]
    public async Task GetDevicesAsyncKeepsStringIdsAndToleratesNullDeviceInfo()
    {
        var home = new MiHome("home-1", "Shared Home", "owner-1", true, []);
        const string response = """{"code":0,"result":{"device_info":null,"has_more":false}}""";
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        var devices = await client.GetDevicesAsync(_session, home, CancellationToken.None);

        Assert.Empty(devices);
        Assert.Equal(
            """{"home_id":"home-1","home_owner":"owner-1","limit":300,"start_did":"","get_split_device":true,"support_smart_home":true,"get_cariot_device":true,"get_third_device":true}""",
            DecryptRequestPayload(handler));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetDevicesAsyncThrowsWhenMaxDidIsEmpty()
    {
        const string response = """{"code":0,"result":{"device_info":null,"has_more":true,"max_did":""}}""";
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
        Assert.Contains("cursor is empty", error.Message);
        Assert.False(error.IsDeviceError);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetDevicesAsyncThrowsWhenMaxDidRepeats()
    {
        const string response = """{"code":0,"result":{"device_info":null,"has_more":true,"max_did":"b"}}""";
        var handler = new StubHttpMessageHandler([response, response]);
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
        Assert.Contains("'b'", error.Message);
        Assert.False(error.IsDeviceError);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetDevicesAsyncThrowsWhenPagingCursorLoopsBack()
    {
        string[] responses =
        [
            """{"code":0,"result":{"device_info":null,"has_more":true,"max_did":"a"}}""",
            """{"code":0,"result":{"device_info":null,"has_more":true,"max_did":"b"}}""",
            """{"code":0,"result":{"device_info":null,"has_more":true,"max_did":"a"}}""",
        ];
        var handler = new StubHttpMessageHandler(responses);
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
        Assert.Contains("'a'", error.Message);
        Assert.Contains("already used", error.Message);
        Assert.False(error.IsDeviceError);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task GetDevicesAsyncKeepsLeadingZerosInNumericLookingIds()
    {
        var home = new MiHome("00123", "Home", "0009001", false, []);
        const string response = """{"code":0,"result":{"device_info":null,"has_more":false}}""";
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        await client.GetDevicesAsync(_session, home, CancellationToken.None);

        Assert.Equal(
            """{"home_id":"00123","home_owner":"0009001","limit":300,"start_did":"","get_split_device":true,"support_smart_home":true,"get_cariot_device":true,"get_third_device":true}""",
            DecryptRequestPayload(handler));
    }

    [Fact]
    public async Task GetDevicesAsyncReadsTopLevelSubclassId()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"a","model":"m.a","name":"A","isOnline":true,"subclass_id":7},
            {"did":"b","model":"m.b","name":"B","isOnline":true,"subclass_id":"8"},
            {"did":"c","model":"m.c","name":"C","isOnline":true}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var devices = await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None);

        Assert.Equal(new int?[] { 7, 8, null }, devices.Select(device => device.SubclassId));
        Assert.All(devices, device => Assert.Null(device.SubclassCategory));
    }

    [Fact]
    public async Task GetSwitchSubclassesAsyncParsesListAndSkipsIncompleteEntries()
    {
        const string response = """
            {"code":0,"result":{"list":[
            {"id":7,"chinese_name":"吸顶灯","category_id":12,"plugin_type":"light"},
            {"id":8,"english_name":"Socket","category_id":9,"plugin_type":"socket"},
            {"id":9,"chinese_name":"","english_name":"","category_id":2},
            {"id":10,"chinese_name":"No Category"},
            {"chinese_name":"No Id","category_id":12}]}}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        var entries = await client.GetSwitchSubclassesAsync(_session, CancellationToken.None);

        Assert.Equal(
            [new SwitchSubclass(7, "吸顶灯", 12), new SwitchSubclass(8, "Socket", 9), new SwitchSubclass(9, "9", 2)],
            entries);
        Assert.Equal("{}", DecryptRequestPayload(handler));
        Assert.Equal(new Uri("https://api.io.mi.com/app/v2/device/switch_subclass"), handler.RequestUri);
    }

    [Fact]
    public async Task GetSwitchMembershipsAsyncParsesMemberShipKeys()
    {
        const string response = """
            {"code":0,"result":{"list":[
            {"did":"parent-1","member_ship":{
            "1":{"subclass_id":7},
            "2":{"subclass_id":8},
            "3":{"subclass_id":"10"},
            "bad":{"subclass_id":9},
            "4":null,
            "5":{"subclass_id":""}}},
            {"did":"parent-2","member_ship":null},
            {"member_ship":{"1":{"subclass_id":11}}},
            {"did":"parent-3","member_ship":{"1":{"subclass_id":12}}}]}}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        var memberships = await client.GetSwitchMembershipsAsync(
            _session,
            ["parent-1", "parent-2", "parent-3"],
            CancellationToken.None);

        Assert.Equal(2, memberships.Count);
        Assert.Equal(3, memberships["parent-1"].Count);
        Assert.Equal(7, memberships["parent-1"][1]);
        Assert.Equal(8, memberships["parent-1"][2]);
        Assert.Equal(10, memberships["parent-1"][3]);
        Assert.False(memberships.ContainsKey("parent-2"));
        Assert.Equal(12, memberships["parent-3"][1]);
        Assert.Equal(
            """{"get_sub_relation":true,"dids":["parent-1","parent-2","parent-3"]}""",
            DecryptRequestPayload(handler));
        Assert.Equal(new Uri("https://api.io.mi.com/app/v2/device/deviceinfo"), handler.RequestUri);
    }

    [Fact]
    public async Task GetSwitchSubclassesAsyncThrowsWhenResultIsMissing()
    {
        var client = CreateClient(new StubHttpMessageHandler("""{"code":0}"""));

        var error = await Assert.ThrowsAsync<MiHomeException>(() =>
            client.GetSwitchSubclassesAsync(_session, CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
    }

    [Fact]
    public async Task GetSwitchMembershipsAsyncThrowsWhenResultIsMissing()
    {
        var client = CreateClient(new StubHttpMessageHandler("""{"code":0,"result":{}}"""));

        var error = await Assert.ThrowsAsync<MiHomeException>(() =>
            client.GetSwitchMembershipsAsync(_session, ["parent-1"], CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
    }

    [Fact]
    public async Task GetSwitchMembershipsAsyncAcceptsAnEmptyList()
    {
        var client = CreateClient(new StubHttpMessageHandler("""{"code":0,"result":{"list":[]}}"""));

        var memberships = await client.GetSwitchMembershipsAsync(
            _session,
            ["parent-1"],
            CancellationToken.None);

        Assert.Empty(memberships);
    }

    [Fact]
    public async Task GetSwitchMembershipsAsyncChunksLargeDeviceLists()
    {
        string[] dids = Enumerable.Range(0, 51).Select(index => $"did-{index}").ToArray();
        string first = JsonSerializer.Serialize(new
        {
            code = 0,
            result = new
            {
                list = dids.Take(50).Select(did => new
                {
                    did,
                    member_ship = new Dictionary<string, object> { ["1"] = new { subclass_id = 7 } },
                }).ToArray(),
            },
        });
        string second = JsonSerializer.Serialize(new
        {
            code = 0,
            result = new
            {
                list = new[]
                {
                    new
                    {
                        did = "did-50",
                        member_ship = new Dictionary<string, object> { ["1"] = new { subclass_id = 8 } },
                    },
                },
            },
        });
        var handler = new StubHttpMessageHandler([first, second]);
        var client = CreateClient(handler);

        var memberships = await client.GetSwitchMembershipsAsync(
            _session,
            dids,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(51, memberships.Count);
        Assert.Equal(7, memberships["did-0"][1]);
        Assert.Equal(8, memberships["did-50"][1]);
        Assert.Equal(
            """{"get_sub_relation":true,"dids":["did-50"]}""",
            DecryptRequestPayload(handler, index: 1));
    }

    [Fact]
    public async Task GetDevicesAsyncJoinsFirstRoomAndMatchesNumericDid()
    {
        var home = new MiHome(
            "1001",
            "Home",
            "9001",
            false,
            [new MiRoom("11", "First", ["42"]), new MiRoom("12", "Second", ["42", "x"])]);
        const string response = """
            {"code":0,"result":{"device_info":[{"did":42,"model":"m","name":"Numeric"}],"has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var devices = await client.GetDevicesAsync(_session, home, CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Equal("42", device.Did);
        Assert.Equal("First", device.RoomName);
    }

    [Fact]
    public async Task GetDevicesAsyncParsesSplitDeviceControlTarget()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"2000420370.s4","model":"m.switch","name":"Channel 4","isOnline":true,
            "extra":{"split":{"parentId":"2000420370","moduleId":4,"channel":[1]}}},
            {"did":"normal-1","model":"m.normal","name":"Normal","isOnline":false},
            {"did":"split-absent","model":"m","name":"Split Absent","isOnline":true,"extra":{"other":1}},
            {"did":"split-null","model":"m","name":"Split Null","isOnline":true,"extra":{"split":null}}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var devices = await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None);

        Assert.Equal(4, devices.Count);

        Assert.Equal("2000420370", devices[0].ParentDid);
        Assert.Equal(4, devices[0].ChannelSiid);
        Assert.Equal("2000420370", devices[0].ControlDid);
        Assert.False(devices[0].IsSplitIncomplete);
        Assert.False(devices[0].IsSplitMetadataIncomplete);

        Assert.Null(devices[1].ParentDid);
        Assert.Null(devices[1].ChannelSiid);
        Assert.Equal("normal-1", devices[1].ControlDid);
        Assert.False(devices[1].IsSplitMetadataIncomplete);

        Assert.Null(devices[2].ParentDid);
        Assert.Null(devices[2].ChannelSiid);
        Assert.Equal("split-absent", devices[2].ControlDid);
        Assert.False(devices[2].IsSplitMetadataIncomplete);

        Assert.Null(devices[3].ParentDid);
        Assert.Null(devices[3].ChannelSiid);
        Assert.Equal("split-null", devices[3].ControlDid);
        Assert.False(devices[3].IsSplitMetadataIncomplete);
    }

    [Fact]
    public async Task GetDevicesAsyncParsesStringModuleIdAndNumericParentId()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"a.s2","model":"m","name":"A","isOnline":true,
            "extra":{"split":{"parentId":1234,"moduleId":"2"}}}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var device = Assert.Single(await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Equal("a.s2", device.Did);
        Assert.Equal("1234", device.ParentDid);
        Assert.Equal(2, device.ChannelSiid);
        Assert.Equal("1234", device.ControlDid);
        Assert.False(device.IsSplitIncomplete);
        Assert.False(device.IsSplitMetadataIncomplete);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"parentId":"","moduleId":""}""")]
    [InlineData("""{"parentId":null,"moduleId":null}""")]
    public async Task GetDevicesAsyncTreatsEmptySplitObjectAsOrdinaryDevice(string split)
    {
        string response = $$$"""
            {"code":0,"result":{"device_info":[
            {"did":"a.s2","model":"m","name":"A","isOnline":true,
            "extra":{"split":{{{split}}}}}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var device = Assert.Single(await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Null(device.ParentDid);
        Assert.Null(device.ChannelSiid);
        Assert.False(device.IsSplitMetadataIncomplete);
        Assert.False(device.IsSplitIncomplete);
    }

    [Theory]
    [InlineData("""{"parentId":"2000420370"}""")]
    [InlineData("""{"parentId":"2000420370","moduleId":"abc"}""")]
    [InlineData("""{"parentId":"2000420370","moduleId":0}""")]
    [InlineData("""{"parentId":"2000420370","moduleId":-3}""")]
    public async Task GetDevicesAsyncMarksSplitIncompleteWhenChannelIsUnusable(string split)
    {
        string response = $$$"""
            {"code":0,"result":{"device_info":[
            {"did":"a.s2","model":"m","name":"A","isOnline":true,
            "extra":{"split":{{{split}}}}}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var device = Assert.Single(await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Equal("2000420370", device.ParentDid);
        Assert.Null(device.ChannelSiid);
        Assert.True(device.IsSplitIncomplete);
        Assert.True(device.IsSplitMetadataIncomplete);
        Assert.Equal("2000420370", device.ControlDid);
    }

    [Fact]
    public async Task GetDevicesAsyncMarksSplitIncompleteWhenOnlyChannelIsMalformed()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"a.s2","model":"m","name":"A","isOnline":true,
            "extra":{"split":{"moduleId":"garbage"}}}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var device = Assert.Single(await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Null(device.ParentDid);
        Assert.Null(device.ChannelSiid);
        Assert.True(device.IsSplitMetadataIncomplete);
    }

    [Fact]
    public async Task GetDevicesAsyncMarksSplitIncompleteWhenParentIsMissing()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"a.s2","model":"m","name":"A","isOnline":true,
            "extra":{"split":{"moduleId":2}}}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var device = Assert.Single(await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None));

        Assert.Null(device.ParentDid);
        Assert.Equal(2, device.ChannelSiid);
        Assert.True(device.IsSplitIncomplete);
        Assert.True(device.IsSplitMetadataIncomplete);
    }

    [Fact]
    public async Task GetDevicesAsyncParsesLocalIpAndStoresToken()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"111","model":"m","name":"A","isOnline":true,
            "localip":"192.168.1.20","token":"0123456789abcdef"},
            {"did":"222","model":"m","name":"B","isOnline":true,"localip":"","token":""},
            {"did":"","model":"m","name":"C","isOnline":true,
            "localip":"10.0.0.5","token":"should-not-be-stored"}],
            "has_more":false}}
            """;
        var secrets = new InMemoryDeviceSecretStore();
        var client = CreateClient(new StubHttpMessageHandler(response), secrets);

        var devices = await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None);

        Assert.Equal(2, devices.Count);
        Assert.Equal("192.168.1.20", devices[0].LocalIp);
        Assert.Null(devices[1].LocalIp);
        Assert.Equal("0123456789abcdef", secrets.GetToken("111"));
        Assert.Null(secrets.GetToken("222"));
        Assert.Null(secrets.GetToken(string.Empty));
    }

    [Fact]
    public async Task GetDevicesAsyncParsesSpecType()
    {
        const string response = """
            {"code":0,"result":{"device_info":[
            {"did":"a","model":"m","name":"A","isOnline":true,
            "spec_type":"urn:miot-spec-v2:device:switch:0000A003:foo:1"},
            {"did":"b","model":"m","name":"B","isOnline":true},
            {"did":"c","model":"m","name":"C","isOnline":true,"spec_type":""}],
            "has_more":false}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var devices = await client.GetDevicesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None);

        Assert.Equal("urn:miot-spec-v2:device:switch:0000A003:foo:1", devices[0].SpecType);
        Assert.Null(devices[1].SpecType);
        Assert.Null(devices[2].SpecType);
    }

    [Fact]
    public async Task GetScenesAsyncSendsOwnerScopedPayloadAndParsesScenes()
    {
        const string response = """
            {"code":0,"result":{"manual_scene_info_list":[
            {"scene_id":1001,"name":"Morning","enable":false,
            "icon_url":"https://example.test/morning.png"},
            {"scene_id":"1002","name":"Night","enable":1}]}}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);
        var home = new MiHome("2001", "Shared Home", "8001", true, []);

        var scenes = await client.GetScenesAsync(_session, home, CancellationToken.None);

        Assert.Equal(2, scenes.Count);
        Assert.Equal(
            new MiScene("1001", "Morning", false, "https://example.test/morning.png"),
            scenes[0]);
        Assert.Equal(new MiScene("1002", "Night", true, null), scenes[1]);
        Assert.Equal(
            """{"app_version":12,"get_type":2,"home_id":"2001","owner_uid":8001}""",
            DecryptRequestPayload(handler));
        Assert.Equal(
            new Uri("https://api.io.mi.com/app/appgateway/miot/appsceneservice/AppSceneService/GetSimpleSceneList"),
            handler.RequestUri);
    }

    [Theory]
    [InlineData("""{"code":0,"result":{"manual_scene_info_list":null}}""")]
    [InlineData("""{"code":0,"result":{}}""")]
    [InlineData("""{"code":0,"result":null}""")]
    public async Task GetScenesAsyncReturnsEmptyWhenListMissingOrNull(string response)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var scenes = await client.GetScenesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None);

        Assert.Empty(scenes);
    }

    [Fact]
    public async Task RunSceneAsyncSendsExactPayload()
    {
        var handler = new StubHttpMessageHandler("""{"code":0}""");
        var client = CreateClient(handler);

        await client.RunSceneAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            "scene-7",
            CancellationToken.None);

        Assert.Equal(
            """{"scene_id":"scene-7","scene_type":2,"phone_id":"null","home_id":"1001","owner_uid":9001}""",
            DecryptRequestPayload(handler));
        Assert.Equal(
            new Uri("https://api.io.mi.com/app/appgateway/miot/appsceneservice/AppSceneService/NewRunScene"),
            handler.RequestUri);
    }

    [Fact]
    public async Task GetScenesAsyncParsesSceneEdgeCases()
    {
        const string response = """
            {"code":0,"result":{"manual_scene_info_list":[
            {"scene_id":"s1","name":"Valid","enable":true,"icon_url":"https://example.test/icon.png"},
            {"scene_id":"s2","enable":0},
            {"scene_id":"s3","name":"Empty Icon","enable":false,"icon_url":""},
            {"name":"No Id","enable":true}]}}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var scenes = await client.GetScenesAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            CancellationToken.None);

        Assert.Equal(3, scenes.Count);
        Assert.Equal(
            new MiScene("s1", "Valid", true, "https://example.test/icon.png"),
            scenes[0]);
        Assert.Equal(new MiScene("s2", "场景 s2", false, null), scenes[1]);
        Assert.Equal(new MiScene("s3", "Empty Icon", false, null), scenes[2]);
    }

    [Fact]
    public async Task RunSceneAsyncPreservesStringOwnerUidForSharedHome()
    {
        var handler = new StubHttpMessageHandler("""{"code":0}""");
        var client = CreateClient(handler);
        var home = new MiHome("home-9", "Shared Home", "00123", true, []);

        await client.RunSceneAsync(_session, home, "scene-1", CancellationToken.None);

        var payload = DecryptRequestPayload(handler);
        Assert.Equal(
            """{"scene_id":"scene-1","scene_type":2,"phone_id":"null","home_id":"home-9","owner_uid":"00123"}""",
            payload);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("owner_uid").ValueKind);
        Assert.Equal("00123", document.RootElement.GetProperty("owner_uid").GetString());
        Assert.Equal("home-9", document.RootElement.GetProperty("home_id").GetString());
    }

    [Fact]
    public async Task RunSceneAsyncThrowsNonZeroOuterCode()
    {
        const string response = """{"code":-6,"message":"denied"}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.RunSceneAsync(
            _session,
            new MiHome("1001", "Home", "9001", false, []),
            "scene-7",
            CancellationToken.None));

        Assert.Equal(-6, error.Code);
        Assert.Equal("denied", error.Message);
        Assert.False(error.IsDeviceError);
    }

    [Theory]
    [InlineData("""{"code":0,"result":[{"siid":2,"piid":1,"code":0,"value":true},{"siid":2,"piid":2,"code":-1},{"siid":3,"piid":1,"value":42}]}""")]
    [InlineData("""{"code":0,"result":{"result":[{"siid":2,"piid":1,"code":0,"value":true},{"siid":2,"piid":2,"code":-1},{"siid":3,"piid":1,"value":42}]}}""")]
    public async Task GetPropertiesAsyncParsesBothResultShapesAndSkipsFailures(string response)
    {
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);
        MiPropertyKey[] properties = [new(2, 1), new(2, 2), new(3, 1)];

        var values = await client.GetPropertiesAsync(_session, "111", properties, CancellationToken.None);

        Assert.Equal(2, values.Count);
        Assert.Equal(new MiPropertyKey(2, 1), values[0].Key);
        Assert.True(values[0].Value.GetBoolean());
        Assert.Equal(new MiPropertyKey(3, 1), values[1].Key);
        Assert.Equal(42, values[1].Value.GetInt32());

        Assert.Equal(
            """{"datasource":1,"params":[{"did":"111","siid":2,"piid":1},{"did":"111","siid":2,"piid":2},{"did":"111","siid":3,"piid":1}]}""",
            DecryptRequestPayload(handler));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-7, false)]
    public async Task SetPropertyAsyncChecksEntryCodes(int entryCode, bool succeeds)
    {
        var response = $$"""
            {"code":0,"result":[{"did":"111","siid":2,"piid":1,"code":{{entryCode}},"message":"rejected","description":"detail"}]}
            """;
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);

        if (succeeds)
        {
            await client.SetPropertyAsync(
                _session,
                "111",
                new MiPropertyKey(2, 1),
                JsonSerializer.SerializeToElement(true),
                CancellationToken.None);
        }
        else
        {
            var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
                _session,
                "111",
                new MiPropertyKey(2, 1),
                JsonSerializer.SerializeToElement(true),
                CancellationToken.None));

            Assert.Equal(entryCode, error.Code);
            Assert.Equal("rejected", error.Message);
            Assert.Equal("detail", error.Description);
            Assert.True(error.IsDeviceError);
        }

        Assert.Equal(
            """{"params":[{"did":"111","siid":2,"piid":1,"value":true}]}""",
            DecryptRequestPayload(handler));
    }

    [Fact]
    public async Task SetPropertyAsyncRejectsOuterCodeOne()
    {
        const string response = """{"code":1,"result":[{"did":"111","siid":2,"piid":1,"code":0}]}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
            _session,
            "111",
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(1, error.Code);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task SetPropertyAsyncMarksSplitChannelEntryCodeAsDeviceError()
    {
        const string response = """
            {"code":0,"result":[{"did":"2000420370","siid":4,"piid":1,"code":-704053100}]}
            """;
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
            _session,
            "2000420370.s4",
            new MiPropertyKey(4, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(-704053100, error.Code);
        Assert.True(error.IsDeviceError);
    }

    [Theory]
    [InlineData("""{"code":0}""")]
    [InlineData("""{"code":0,"result":[]}""")]
    public async Task SetPropertyAsyncRequiresResultEntry(string response)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.SetPropertyAsync(
            _session,
            "111",
            new MiPropertyKey(2, 1),
            JsonSerializer.SerializeToElement(true),
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task InvokeActionAsyncReturnsOutput()
    {
        const string response = """{"code":0,"result":{"code":0,"out":[1,2]}}""";
        var handler = new StubHttpMessageHandler(response);
        var client = CreateClient(handler);
        JsonElement[] input = [JsonSerializer.SerializeToElement(true), JsonSerializer.SerializeToElement(5)];

        var result = await client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            input,
            CancellationToken.None);

        Assert.Equal("[1,2]", result.GetRawText());
        Assert.Equal(
            """{"params":{"did":"111","siid":2,"aiid":1,"in":[true,5]}}""",
            DecryptRequestPayload(handler));
    }

    [Fact]
    public async Task InvokeActionAsyncWithoutOutputReturnsEmptyArray()
    {
        const string response = """{"code":0,"result":{"code":0}}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var result = await client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            [],
            CancellationToken.None);

        Assert.Equal("[]", result.GetRawText());
    }

    [Fact]
    public async Task InvokeActionAsyncChecksInnerCode()
    {
        const string response = """{"code":0,"result":{"code":-3,"message":"busy","description":"device busy"}}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(-3, error.Code);
        Assert.Equal("busy", error.Message);
        Assert.Equal("device busy", error.Description);
        Assert.True(error.IsDeviceError);
    }

    [Fact]
    public async Task InvokeActionAsyncRequiresResultObject()
    {
        const string response = """{"code":0}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(() => client.InvokeActionAsync(
            _session,
            "111",
            new MiActionKey(2, 1),
            [],
            CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
    }

    [Fact]
    public async Task NonZeroResponseCodeThrowsWithCloudDetails()
    {
        const string response = """{"code":-6,"message":"login required","description":"auth failed"}""";
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetHomesAsync(_session, CancellationToken.None));

        Assert.Equal(-6, error.Code);
        Assert.Equal("login required", error.Message);
        Assert.Equal("auth failed", error.Description);
        Assert.False(error.IsDeviceError);
    }

    [Theory]
    [InlineData("""{"result":{"homelist":[]}}""")]
    [InlineData("""{"code":"0","result":{"homelist":[]}}""")]
    public async Task MissingOrNonNumericCodeIsAProtocolError(string response)
    {
        var client = CreateClient(new StubHttpMessageHandler(response));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetHomesAsync(_session, CancellationToken.None));

        Assert.Equal(MiHomeCloudClient.ProtocolErrorCode, error.Code);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task HttpServiceUnavailableThrowsBeforeParsingErrorBody()
    {
        const string response = """{"code":-6,"message":"login required"}""";
        var client = CreateClient(new StubHttpMessageHandler(response, HttpStatusCode.ServiceUnavailable));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetHomesAsync(_session, CancellationToken.None));

        Assert.Equal(503, error.Code);
        Assert.Contains("503", error.Message);
        Assert.DoesNotContain("login required", error.Message);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task HttpForbiddenWithHtmlBodyThrowsSessionExpired()
    {
        const string response = "<html><body>Forbidden</body></html>";
        var client = CreateClient(new StubHttpMessageHandler(response, HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<MiHomeException>(
            () => client.GetHomesAsync(_session, CancellationToken.None));

        Assert.Equal(403, error.Code);
        Assert.Contains("session may have expired", error.Message);
        Assert.False(error.IsDeviceError);
    }

    [Fact]
    public async Task FormValuesWithPlusArePercentEncodedAndDecodeAsPlus()
    {
        const string expectedData = "0L4RBiDU9pbpO1MF+f0R2dcxjZyjyKJy8zV9crscI1xJ6t1hr0l6qVZiMvbFbIVJz7d6/ALPiRCN7wuNd00WsxM1xwiGusRm+rxIj0izzuTBMqydzRAQs5aF1SF5eJ6gr5s6nQOlOABEdUde0c9r";
        var handler = new StubHttpMessageHandler("""{"code":0,"result":{}}""");
        var client = CreateClient(handler);

        await client.GetHomesAsync(_session, CancellationToken.None);

        var body = handler.Bodies[0];
        Assert.Contains("%2B", body);

        var values = ParseForm(body).ToDictionary(field => field.Key, field => field.Value);
        Assert.Equal(expectedData, values["data"]);
        Assert.Contains("+", values["data"]);
    }

    private static MiHomeCloudClient CreateClient(
        StubHttpMessageHandler handler,
        IDeviceSecretStore? secrets = null)
        => new(new HttpClient(handler), () => _nonce, secrets);

    private static void AssertRoom(MiRoom room, string id, string name, params string[] deviceIds)
    {
        Assert.Equal(id, room.Id);
        Assert.Equal(name, room.Name);
        Assert.Equal(deviceIds, room.DeviceIds);
    }

    private static string DecryptRequestPayload(StubHttpMessageHandler handler, int index = 0)
    {
        var values = ParseForm(handler.Bodies[index]).ToDictionary(field => field.Key, field => field.Value);
        return DecryptValue(values["data"]);
    }

    private static string DecryptValue(string value)
    {
        var key = Convert.FromBase64String(SignedNonce(_ssecurity, _nonce));
        var plaintext = Rc4.Transform(key, Convert.FromBase64String(value), discardBytes: 1024);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static string EncryptResponse(string plaintext)
    {
        var key = Convert.FromBase64String(SignedNonce(_ssecurity, _nonce));
        var cipher = Rc4.Transform(key, Encoding.UTF8.GetBytes(plaintext), discardBytes: 1024);
        return Convert.ToBase64String(cipher);
    }

    private static string SignedNonce(string ssecurity, string nonce)
        => Convert.ToBase64String(SHA256.HashData(
            [.. Convert.FromBase64String(ssecurity), .. Convert.FromBase64String(nonce)]));

    private static IReadOnlyList<KeyValuePair<string, string>> ParseForm(string body)
        => body.Split('&')
            .Select(part =>
            {
                var separator = part.IndexOf('=');
                return new KeyValuePair<string, string>(
                    WebUtility.UrlDecode(part[..separator]),
                    WebUtility.UrlDecode(part[(separator + 1)..]));
            })
            .ToArray();

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyList<string> _responses;
        private readonly HttpStatusCode _statusCode;
        private int _index;

        public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
            : this([responseBody], statusCode)
        {
        }

        public StubHttpMessageHandler(IReadOnlyList<string> responses, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responses = responses;
            _statusCode = statusCode;
        }

        public int RequestCount => Bodies.Count;

        public List<string> Bodies { get; } = [];

        public List<Uri?> RequestUris { get; } = [];

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Cookie { get; private set; }

        public string? AcceptEncoding { get; private set; }

        public string? ProtocolFlag { get; private set; }

        public string? EncryptAlgorithm { get; private set; }

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var responseBody = _responses[Math.Min(_index, _responses.Count - 1)];
            var requestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Bodies.Add(requestBody ?? string.Empty);
            RequestUris.Add(request.RequestUri);

            if (_index == 0)
            {
                Method = request.Method;
                RequestUri = request.RequestUri;
                Cookie = ReadHeader(request, "Cookie");
                AcceptEncoding = ReadHeader(request, "Accept-Encoding");
                ProtocolFlag = ReadHeader(request, "x-xiaomi-protocal-flag-cli");
                EncryptAlgorithm = ReadHeader(request, "MIOT-ENCRYPT-ALGORITHM");
                ContentType = request.Content?.Headers.ContentType?.ToString();
            }

            _index++;

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(responseBody),
            };
        }

        private static string? ReadHeader(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values)
                ? string.Join(", ", values)
                : null;
    }
}

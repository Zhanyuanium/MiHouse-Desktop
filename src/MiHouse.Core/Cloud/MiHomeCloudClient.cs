using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Devices;
using MiHouse.Core.Session;

namespace MiHouse.Core.Cloud;

/// <summary>
/// Mi Home cloud client. Requests are signed with <see cref="MiHomeRequestSigner"/> and sent
/// with the session cookies the cloud expects.
/// </summary>
public sealed class MiHomeCloudClient : IMiHomeCloudClient
{
    internal const int ProtocolErrorCode = -1;
    private const string _startMarker = "&&&START&&&";
    private const string _homesPath = "v2/homeroom/gethome";
    private const string _homesPayload =
        """{"limit":300,"fetch_share":true,"fetch_share_dev":true,"fetch_cariot":true,"plat_form":0,"app_ver":9,"fg":true}""";
    private const string _homeDeviceListPath = "v2/home/home_device_list";
    private const string _sceneListPath =
        "appgateway/miot/appsceneservice/AppSceneService/GetSimpleSceneList";
    private const string _runScenePath =
        "appgateway/miot/appsceneservice/AppSceneService/NewRunScene";

    private readonly HttpClient _httpClient;
    private readonly MiHomeRequestSigner _signer;
    private readonly IDeviceSecretStore? _secrets;

    /// <summary>
    /// Creates a client over the supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Sender used for every request.</param>
    /// <param name="nonceFactory">
    /// Overrides nonce generation, mainly for tests. Defaults to
    /// <see cref="MiHomeRequestSigner.CreateNonce"/>.
    /// </param>
    /// <param name="secrets">
    /// When supplied, LAN tokens reported by the cloud are copied into this store.
    /// </param>
    public MiHomeCloudClient(
        HttpClient httpClient,
        Func<string>? nonceFactory = null,
        IDeviceSecretStore? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _secrets = secrets;
        _signer = nonceFactory is null
            ? new MiHomeRequestSigner()
            : new MiHomeRequestSigner(null, nonceFactory, null, null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MiHome>> GetHomesAsync(
        MiHomeSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        // has_more/max_id are deliberately ignored: the cloud caps gethome at 300 homes per
        // response, so accounts above that limit only see the first page.
        var response = await PostAsync(session, _homesPath, _homesPayload, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, _homesPath);

        if (!response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var homes = new List<MiHome>();

        // Array placement, not shareflag, decides whether a home is shared.
        AppendHomes(result, "homelist", isShared: false, homes);
        AppendHomes(result, "share_home_list", isShared: true, homes);

        return homes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MiDevice>> GetDevicesAsync(
        MiHomeSession session,
        MiHome home,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(home);

        var roomNames = BuildRoomLookup(home);
        var devices = new List<MiDevice>();
        var seenStartDids = new HashSet<string>(StringComparer.Ordinal);
        var startDid = string.Empty;

        while (seenStartDids.Add(startDid))
        {
            var payload = BuildHomeDeviceListPayload(home, startDid);
            var response = await PostAsync(session, _homeDeviceListPath, payload, cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccess(response, _homeDeviceListPath);

            if (!response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                break;
            }

            AppendDevices(result, roomNames, devices);

            if (!ReadBoolean(result, "has_more"))
            {
                break;
            }

            var next = ReadIdentifier(result, "max_did");
            if (string.IsNullOrEmpty(next))
            {
                throw ProtocolError(
                    _homeDeviceListPath,
                    $"device paging cursor is empty after '{startDid}'.");
            }

            if (string.Equals(next, startDid, StringComparison.Ordinal))
            {
                throw ProtocolError(
                    _homeDeviceListPath,
                    $"device paging cursor '{next}' repeats the current cursor.");
            }

            if (seenStartDids.Contains(next))
            {
                throw ProtocolError(
                    _homeDeviceListPath,
                    $"device paging cursor '{next}' was already used; paging would loop.");
            }

            startDid = next;
        }

        return devices;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MiScene>> GetScenesAsync(
        MiHomeSession session,
        MiHome home,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(home);

        var payload = BuildSceneListPayload(home);
        var response = await PostAsync(session, _sceneListPath, payload, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, _sceneListPath);

        if (!response.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("manual_scene_info_list", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var scenes = new List<MiScene>(list.GetArrayLength());
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadIdentifier(item, "scene_id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"场景 {id}";
            }

            var iconUrl = ReadString(item, "icon_url");
            if (string.IsNullOrWhiteSpace(iconUrl))
            {
                iconUrl = null;
            }

            scenes.Add(new MiScene(id, name, ReadBoolean(item, "enable"), iconUrl));
        }

        return scenes;
    }

    /// <inheritdoc />
    public async Task RunSceneAsync(
        MiHomeSession session,
        MiHome home,
        string sceneId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(sceneId);

        var payload = BuildRunScenePayload(home, sceneId);
        var response = await PostAsync(session, _runScenePath, payload, cancellationToken)
            .ConfigureAwait(false);

        EnsureSuccess(response, _runScenePath);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
        MiHomeSession session,
        string did,
        IReadOnlyList<MiPropertyKey> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.Count == 0)
        {
            return [];
        }

        var payload = JsonSerializer.Serialize(new
        {
            datasource = 1,
            @params = properties
                .Select(property => new { did, siid = property.Siid, piid = property.Piid })
                .ToArray(),
        });

        const string path = "v2/miotspec/prop/get";
        var response = await PostAsync(session, path, payload, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, path);

        return ParseProperties(response);
    }

    /// <inheritdoc />
    public async Task SetPropertyAsync(
        MiHomeSession session,
        string did,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(did);

        var payload = JsonSerializer.Serialize(new
        {
            @params = new[]
            {
                new { did, siid = property.Siid, piid = property.Piid, value },
            },
        });

        const string path = "v2/miotspec/prop/set";
        var response = await PostAsync(session, path, payload, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response, path);
        EnsurePropertySetSuccess(response, path);
    }

    /// <inheritdoc />
    public async Task<JsonElement> InvokeActionAsync(
        MiHomeSession session,
        string did,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(input);

        var payload = JsonSerializer.Serialize(new
        {
            @params = new
            {
                did,
                siid = action.Siid,
                aiid = action.Aiid,
                @in = input.ToArray(),
            },
        });

        const string path = "v2/miotspec/action";
        var response = await PostAsync(session, path, payload, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, path);

        return ParseActionResult(response, path);
    }

    private async Task<JsonElement> PostAsync(
        MiHomeSession session,
        string relativePath,
        string payload,
        CancellationToken cancellationToken)
    {
        var url = new Uri(MiHomeEndpoints.AppBase(session.Region), relativePath);
        KeyValuePair<string, string>[] fields = [new("data", payload)];

        var signedFields = _signer.SignForm(session.Ssecurity, url.ToString(), fields, out var nonce);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(session));
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        request.Headers.TryAddWithoutValidation("x-xiaomi-protocal-flag-cli", "PROTOCAL-HTTP2");
        request.Headers.TryAddWithoutValidation("MIOT-ENCRYPT-ALGORITHM", "ENCRYPT-RC4");

        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(EncodeForm(signedFields)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response, relativePath);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var plaintext = ReadBody(session, nonce, body);

        using var document = JsonDocument.Parse(plaintext);
        return document.RootElement.Clone();
    }

    private string ReadBody(MiHomeSession session, string nonce, string body)
    {
        var text = StripStartMarker(body);
        if (LooksLikeJson(text))
        {
            return text;
        }

        // Encrypted responses are a bare Base64 RC4 blob with no JSON marker.
        return StripStartMarker(_signer.DecryptResponse(session.Ssecurity, nonce, text));
    }

    private static string StripStartMarker(string body)
    {
        var text = body.Trim();
        return text.StartsWith(_startMarker, StringComparison.Ordinal)
            ? text[_startMarker.Length..].TrimStart()
            : text;
    }

    private static bool LooksLikeJson(string text)
        => text.Length > 0 && text[0] is '{' or '[';

    private static string BuildCookieHeader(MiHomeSession session)
        => string.Join(
            "; ",
            $"userId={session.UserId}",
            $"serviceToken={session.ServiceToken}",
            $"yetAnotherServiceToken={session.ServiceToken}",
            "locale=zh_CN",
            "timezone=GMT+08:00",
            "is_daylight=0",
            "dst_offset=0",
            "channel=MI_APP_STORE");

    private static string EncodeForm(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('&');
            }

            builder
                .Append(Uri.EscapeDataString(fields[index].Key))
                .Append('=')
                .Append(Uri.EscapeDataString(fields[index].Value));
        }

        return builder.ToString();
    }

    private static MiHomeException CreateHttpException(HttpResponseMessage response, string path)
    {
        var status = (int)response.StatusCode;
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.StatusCode.ToString()
            : response.ReasonPhrase;

        var message = status is 401 or 403
            ? $"Mi Home cloud request '{path}' failed with HTTP {status} {reason}; the session may have expired."
            : $"Mi Home cloud request '{path}' failed with HTTP {status} {reason}.";

        return new MiHomeException(status, message);
    }

    private static void EnsureSuccess(JsonElement response, string path)
    {
        var code = ReadRequiredInt32(response, path);
        if (code != 0)
        {
            throw CreateException(response, path, code);
        }
    }

    private static void EnsurePropertySetSuccess(JsonElement response, string path)
    {
        if (!response.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
        {
            throw ProtocolError(path, "the property write response has no result entries.");
        }

        foreach (var entry in result.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw ProtocolError(path, "a property write result entry is not an object.");
            }

            var code = ReadRequiredInt32(entry, path);

            // Some gateway-backed writes answer 1 instead of 0 while still accepting the value.
            if (code is not (0 or 1))
            {
                throw CreateException(entry, path, code, isDeviceError: true);
            }
        }
    }

    private static int ReadRequiredInt32(JsonElement element, string path)
    {
        if (!element.TryGetProperty("code", out var code)
            || code.ValueKind != JsonValueKind.Number
            || !code.TryGetInt32(out var value))
        {
            throw ProtocolError(path, "the response has no numeric 'code'.");
        }

        return value;
    }

    private static MiHomeException ProtocolError(string path, string detail)
        => new(ProtocolErrorCode, $"Unexpected Mi Home cloud response from '{path}': {detail}");

    private static MiHomeException CreateException(
        JsonElement response,
        string path,
        int code,
        bool isDeviceError = false)
    {
        var description = ReadString(response, "description");
        var message = ReadString(response, "message");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = description;
        }

        message ??= $"Mi Home cloud request '{path}' failed with code {code}.";
        return new MiHomeException(code, message, description, isDeviceError);
    }

    private static void AppendHomes(
        JsonElement result,
        string propertyName,
        bool isShared,
        List<MiHome> homes)
    {
        if (!result.TryGetProperty(propertyName, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadIdentifier(item, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"家庭 {id}";
            }

            homes.Add(new MiHome(
                id,
                name,
                ReadIdentifier(item, "uid") ?? string.Empty,
                isShared,
                ParseRooms(item)));
        }
    }

    private static IReadOnlyList<MiRoom> ParseRooms(JsonElement home)
    {
        if (!home.TryGetProperty("roomlist", out var roomlist) || roomlist.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rooms = new List<MiRoom>(roomlist.GetArrayLength());
        foreach (var item in roomlist.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadIdentifier(item, "id") ?? string.Empty;
            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"房间 {id}";
            }

            var deviceIds = new List<string>();
            if (item.TryGetProperty("dids", out var dids) && dids.ValueKind == JsonValueKind.Array)
            {
                foreach (var did in dids.EnumerateArray())
                {
                    var deviceId = NormalizeIdentifier(did);
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        deviceIds.Add(deviceId);
                    }
                }
            }

            rooms.Add(new MiRoom(id, name, deviceIds));
        }

        return rooms;
    }

    private static Dictionary<string, string> BuildRoomLookup(MiHome home)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var room in home.Rooms)
        {
            foreach (var deviceId in room.DeviceIds)
            {
                lookup.TryAdd(deviceId, room.Name);
            }
        }

        return lookup;
    }

    private void AppendDevices(
        JsonElement result,
        IReadOnlyDictionary<string, string> roomNames,
        List<MiDevice> devices)
    {
        if (!result.TryGetProperty("device_info", out var deviceInfo)
            || deviceInfo.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in deviceInfo.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var did = ReadIdentifier(item, "did");
            if (string.IsNullOrEmpty(did))
            {
                continue;
            }

            var model = ReadString(item, "model") ?? string.Empty;
            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = model;
            }

            // The cloud sends no room on devices; membership comes from the home's rooms.
            var room = roomNames.TryGetValue(did, out var roomName) ? roomName : null;
            var (parentDid, channelSiid, splitMetadataIncomplete) = ReadSplit(item);

            var specType = ReadString(item, "spec_type");
            if (string.IsNullOrWhiteSpace(specType))
            {
                specType = null;
            }

            var localIp = ReadString(item, "localip");
            if (string.IsNullOrWhiteSpace(localIp))
            {
                localIp = null;
            }

            var token = ReadString(item, "token");
            if (!string.IsNullOrEmpty(token))
            {
                _secrets?.SetToken(did, token);
            }

            devices.Add(new MiDevice(
                did,
                model,
                name,
                room,
                ReadOnline(item),
                parentDid,
                channelSiid,
                specType,
                localIp,
                splitMetadataIncomplete));
        }
    }

    private static string BuildSceneListPayload(MiHome home)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("app_version", 12);
            writer.WriteNumber("get_type", 2);
            writer.WriteString("home_id", home.Id);
            WriteNumericOrString(writer, "owner_uid", home.OwnerUserId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildRunScenePayload(MiHome home, string sceneId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("scene_id", sceneId);
            writer.WriteNumber("scene_type", 2);
            writer.WriteString("phone_id", "null");
            writer.WriteString("home_id", home.Id);
            WriteNumericOrString(writer, "owner_uid", home.OwnerUserId);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildHomeDeviceListPayload(MiHome home, string startDid)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteNumericOrString(writer, "home_id", home.Id);
            WriteNumericOrString(writer, "home_owner", home.OwnerUserId);
            writer.WriteNumber("limit", 300);
            writer.WriteString("start_did", startDid);
            writer.WriteBoolean("get_split_device", true);
            writer.WriteBoolean("support_smart_home", true);
            writer.WriteBoolean("get_cariot_device", true);
            writer.WriteBoolean("get_third_device", true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNumericOrString(Utf8JsonWriter writer, string name, string value)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number.ToString(CultureInfo.InvariantCulture) == value)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static IReadOnlyList<MiPropertyValue> ParseProperties(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result))
        {
            return [];
        }

        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("result", out var nested))
        {
            result = nested;
        }

        if (result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<MiPropertyValue>();
        foreach (var item in result.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if ((ReadInt32(item, "code") ?? 0) != 0)
            {
                continue;
            }

            var siid = ReadInt32(item, "siid");
            var piid = ReadInt32(item, "piid");
            if (siid is null || piid is null || !item.TryGetProperty("value", out var value))
            {
                continue;
            }

            values.Add(new MiPropertyValue(new MiPropertyKey(siid.Value, piid.Value), value.Clone()));
        }

        return values;
    }

    private static JsonElement ParseActionResult(JsonElement response, string path)
    {
        if (!response.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            throw ProtocolError(path, "the action response has no result object.");
        }

        var code = ReadRequiredInt32(result, path);
        if (code != 0)
        {
            throw CreateException(result, path, code, isDeviceError: true);
        }

        return result.TryGetProperty("out", out var output) ? output.Clone() : EmptyArrayElement();
    }

    private static JsonElement EmptyArrayElement()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private static bool ReadOnline(JsonElement item)
    {
        if (!item.TryGetProperty("isOnline", out var online)
            && !item.TryGetProperty("online", out online))
        {
            return false;
        }

        return online.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when online.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String => online.GetString() is "1" or "true",
            _ => false,
        };
    }

    private static (string? ParentDid, int? ChannelSiid, bool Incomplete) ReadSplit(JsonElement device)
    {
        if (!device.TryGetProperty("extra", out var extra)
            || extra.ValueKind != JsonValueKind.Object
            || !extra.TryGetProperty("split", out var split)
            || split.ValueKind != JsonValueKind.Object)
        {
            // No split object: an ordinary device, not a malformed split identity.
            return (null, null, false);
        }

        var parentDid = split.TryGetProperty("parentId", out var parent)
            ? NormalizeIdentifier(parent)
            : null;
        if (string.IsNullOrWhiteSpace(parentDid))
        {
            parentDid = null;
        }

        var channelSiid = split.TryGetProperty("moduleId", out var module)
            ? ReadInt32Value(module)
            : null;
        if (channelSiid is not > 0)
        {
            channelSiid = null;
        }

        // A present split object that does not name both halves must fail closed instead of
        // falling back to parent-wide control as if this were an ordinary device.
        return (parentDid, channelSiid, parentDid is null || channelSiid is null);
    }

    private static bool ReadBoolean(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String => value.GetString() is "1" or "true",
            _ => false,
        };
    }

    private static string? ReadIdentifier(JsonElement item, string name)
        => item.TryGetProperty(name, out var property) ? NormalizeIdentifier(property) : null;

    private static string? NormalizeIdentifier(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null,
        };

    private static int? ReadInt32Value(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number) => number,
            _ => null,
        };

    private static int? ReadInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) => value,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

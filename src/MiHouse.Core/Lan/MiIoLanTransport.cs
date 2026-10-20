using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiHouse.Core.Cloud;
using MiHouse.Core.Devices;

namespace MiHouse.Core.Lan;

/// <summary>
/// miIO protocol over UDP. Each operation performs a handshake, then sends one encrypted request
/// and reads one encrypted response.
/// </summary>
public sealed class MiIoLanTransport : ILanTransport
{
    private const ushort _magic = 0x2131;
    private const int _headerLength = 32;
    private const int _protocolErrorCode = -1;
    private const int _port = 54321;
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<byte[], CancellationToken, Task<byte[]>>? _send;
    private int _requestId;

    /// <summary>
    /// Creates a transport that talks to devices over UDP port 54321.
    /// </summary>
    public MiIoLanTransport()
    {
    }

    internal MiIoLanTransport(Func<byte[], CancellationToken, Task<byte[]>> send)
    {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
        LanEndpoint endpoint,
        IReadOnlyList<MiPropertyKey> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.Count == 0)
        {
            return [];
        }

        var result = await ExecuteAsync(
            endpoint,
            "get_properties",
            writer =>
            {
                writer.WriteStartArray();
                foreach (var property in properties)
                {
                    writer.WriteStartObject();
                    writer.WriteString("did", endpoint.Did);
                    writer.WriteNumber("siid", property.Siid);
                    writer.WriteNumber("piid", property.Piid);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            },
            isAction: false,
            cancellationToken).ConfigureAwait(false);

        return ParseProperties(result);
    }

    /// <inheritdoc />
    public async Task SetPropertyAsync(
        LanEndpoint endpoint,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var result = await ExecuteAsync(
            endpoint,
            "set_properties",
            writer =>
            {
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("did", endpoint.Did);
                writer.WriteNumber("siid", property.Siid);
                writer.WriteNumber("piid", property.Piid);
                writer.WritePropertyName("value");
                value.WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndArray();
            },
            isAction: false,
            cancellationToken).ConfigureAwait(false);

        EnsureSetResult(result);
    }

    /// <inheritdoc />
    public async Task<JsonElement> InvokeActionAsync(
        LanEndpoint endpoint,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(input);

        return await ExecuteAsync(
            endpoint,
            "action",
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("did", endpoint.Did);
                writer.WriteNumber("siid", action.Siid);
                writer.WriteNumber("aiid", action.Aiid);
                writer.WritePropertyName("in");
                writer.WriteStartArray();
                foreach (var item in input)
                {
                    item.WriteTo(writer);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            },
            isAction: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> ExecuteAsync(
        LanEndpoint endpoint,
        string method,
        Action<Utf8JsonWriter> writeParameters,
        bool isAction,
        CancellationToken cancellationToken)
    {
        var token = ParseToken(endpoint.Token);
        var requestSent = false;

        try
        {
            var handshake = await SendWithRetryAsync(
                endpoint,
                BuildHandshakePacket(),
                cancellationToken).ConfigureAwait(false);

            var (deviceId, deviceStamp, _) = ParseHandshakeResponse(handshake);
            var requestId = Interlocked.Increment(ref _requestId);
            var payload = AppendNul(BuildRequestJson(requestId, method, writeParameters));
            var stamp = deviceStamp == 0 ? Stamp() : deviceStamp;
            var packet = BuildRequestPacket(deviceId, stamp, token, payload);

            // An action is sent at most once: repeating it could run it twice.
            requestSent = isAction;
            var response = isAction
                ? await SendActionOnceAsync(endpoint, packet, cancellationToken).ConfigureAwait(false)
                : await SendWithRetryAsync(endpoint, packet, cancellationToken).ConfigureAwait(false);

            var plaintext = TrimTrailingNuls(DecryptResponse(response, deviceId, token));
            var result = ParseEnvelope(plaintext, requestId);

            return isAction ? ParseActionResult(result) : result;
        }
        catch (MiHomeException)
        {
            throw;
        }
        catch (LanOutcomeUnknownException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LanUnavailableException exception) when (requestSent)
        {
            throw new LanOutcomeUnknownException(
                "The action was sent but its outcome could not be confirmed.",
                exception);
        }
        catch (LanUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (requestSent)
        {
            throw new LanOutcomeUnknownException(
                "The action was sent but its outcome could not be confirmed.",
                exception);
        }
        catch (Exception exception)
        {
            throw new LanUnavailableException(
                "The device did not return a usable miIO response.",
                exception);
        }
    }

    private async Task<byte[]> SendActionOnceAsync(
        LanEndpoint endpoint,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        var send = _send;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);

        try
        {
            return send is null
                ? await SendUdpAsync(endpoint, packet, timeout.Token).ConfigureAwait(false)
                : await send(packet, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanOutcomeUnknownException("The action was sent but no reply arrived.", exception);
        }
        catch (TimeoutException exception)
        {
            throw new LanOutcomeUnknownException("The action was sent but no reply arrived.", exception);
        }
        catch (LanUnavailableException exception)
        {
            throw new LanOutcomeUnknownException("The action was sent but no reply arrived.", exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanOutcomeUnknownException("The action was sent but no reply arrived.", exception);
        }
    }

    private async Task<byte[]> SendWithRetryAsync(
        LanEndpoint endpoint,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        var send = _send;
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            try
            {
                return send is null
                    ? await SendUdpAsync(endpoint, packet, timeout.Token).ConfigureAwait(false)
                    : await send(packet, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 0)
                {
                    continue;
                }

                throw new LanUnavailableException("The device did not respond in time.", exception);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException exception)
            {
                if (attempt == 0)
                {
                    continue;
                }

                throw new LanUnavailableException("The device did not respond in time.", exception);
            }
            catch (LanUnavailableException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new LanUnavailableException("The device is unreachable.", exception);
            }
        }
    }

    private static async Task<byte[]> SendUdpAsync(
        LanEndpoint endpoint,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(endpoint.Ip, out var address))
        {
            throw new LanUnavailableException("The device address is not a valid IP address.");
        }

        using var client = new UdpClient(address.AddressFamily);

        // Connecting the socket makes the kernel discard datagrams from any other endpoint.
        client.Connect(new IPEndPoint(address, _port));
        await client.SendAsync(packet, cancellationToken).ConfigureAwait(false);

        var response = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return response.Buffer;
    }

    private static byte[] BuildRequestJson(
        int id,
        string method,
        Action<Utf8JsonWriter> writeParameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            writeParameters(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static byte[] BuildHandshakePacket()
    {
        var packet = new byte[_headerLength];
        BinaryPrimitives.WriteUInt16BigEndian(packet, _magic);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), _headerLength);
        packet.AsSpan(4).Fill(0xFF);
        return packet;
    }

    internal static byte[] BuildHeader(uint deviceId, uint stamp, int totalLength)
    {
        var header = new byte[_headerLength];
        BinaryPrimitives.WriteUInt16BigEndian(header, _magic);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), checked((ushort)totalLength));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), deviceId);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), stamp);
        return header;
    }

    internal static byte[] BuildRequestPacket(
        uint deviceId,
        uint stamp,
        byte[] token,
        byte[] plaintext)
    {
        var ciphertext = EncryptPayload(token, plaintext);
        var header = BuildHeader(deviceId, stamp, _headerLength + ciphertext.Length);
        var checksum = ComputeChecksum(header.AsSpan(0, 16), token, ciphertext);
        checksum.CopyTo(header.AsSpan(16));

        var packet = new byte[header.Length + ciphertext.Length];
        header.CopyTo(packet, 0);
        ciphertext.CopyTo(packet, header.Length);
        return packet;
    }

    internal static byte[] ComputeChecksum(
        ReadOnlySpan<byte> headerPrefix,
        ReadOnlySpan<byte> token,
        ReadOnlySpan<byte> ciphertext)
    {
        var input = new byte[headerPrefix.Length + token.Length + ciphertext.Length];
        headerPrefix.CopyTo(input);
        token.CopyTo(input.AsSpan(headerPrefix.Length));
        ciphertext.CopyTo(input.AsSpan(headerPrefix.Length + token.Length));
        return MD5.HashData(input);
    }

    private static byte[] AppendNul(byte[] json)
    {
        var plaintext = new byte[json.Length + 1];
        json.CopyTo(plaintext, 0);

        // The reference implementation terminates request JSON with a NUL before encryption.
        return plaintext;
    }

    private static ReadOnlyMemory<byte> TrimTrailingNuls(byte[] plaintext)
    {
        var length = plaintext.Length;
        while (length > 0 && plaintext[length - 1] == 0)
        {
            length--;
        }

        return plaintext.AsMemory(0, length);
    }

    internal static (uint DeviceId, uint Stamp, byte[] Token) ParseHandshakeResponse(
        ReadOnlySpan<byte> response)
    {
        if (response.Length < _headerLength || BinaryPrimitives.ReadUInt16BigEndian(response) != _magic)
        {
            throw new LanUnavailableException("The device did not answer the miIO handshake.");
        }

        return (
            BinaryPrimitives.ReadUInt32BigEndian(response[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(response[12..]),
            response[16.._headerLength].ToArray());
    }

    internal static byte[] ParseToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new LanUnavailableException("The device token is invalid.");
        }

        if (token.Length == 32 && IsHexadecimal(token))
        {
            return Convert.FromHexString(token);
        }

        var bytes = Encoding.UTF8.GetBytes(token);
        if (bytes.Length != 16)
        {
            throw new LanUnavailableException("The device token is invalid.");
        }

        return bytes;
    }

    private static bool IsHexadecimal(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    internal static byte[] EncryptPayload(byte[] token, byte[] plaintext)
        => Transform(token, plaintext, encrypt: true);

    internal static byte[] DecryptPayload(byte[] token, ReadOnlySpan<byte> ciphertext)
        => Transform(token, ciphertext, encrypt: false);

    private static byte[] Transform(byte[] token, ReadOnlySpan<byte> data, bool encrypt)
    {
        if (token.Length != 16)
        {
            throw new LanUnavailableException("The device token is invalid.");
        }

        var key = MD5.HashData(token);
        var ivInput = new byte[key.Length + token.Length];
        key.CopyTo(ivInput, 0);
        token.CopyTo(ivInput, key.Length);
        var iv = MD5.HashData(ivInput);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        var bytes = data.ToArray();
        return transform.TransformFinalBlock(bytes, 0, bytes.Length);
    }

    private static byte[] DecryptResponse(byte[] response, uint expectedDeviceId, byte[] token)
    {
        if (response.Length < _headerLength
            || BinaryPrimitives.ReadUInt16BigEndian(response) != _magic)
        {
            throw new LanUnavailableException("The device returned a malformed packet.");
        }

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2));
        if (totalLength != response.Length)
        {
            throw new LanUnavailableException("The device returned a packet with an invalid length.");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(8)) != expectedDeviceId)
        {
            throw new LanUnavailableException("The device reply came from an unexpected device id.");
        }

        var ciphertext = response.AsSpan(_headerLength);
        var expectedChecksum = ComputeChecksum(response.AsSpan(0, 16), token, ciphertext);
        if (!CryptographicOperations.FixedTimeEquals(
                expectedChecksum,
                response.AsSpan(16, 16)))
        {
            throw new LanUnavailableException("The device reply checksum did not match.");
        }

        return DecryptPayload(token, ciphertext);
    }

    private static JsonElement ParseEnvelope(ReadOnlyMemory<byte> plaintext, int expectedId)
    {
        using var document = JsonDocument.Parse(plaintext);
        var root = document.RootElement;

        if (!root.TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt32(out var id)
            || id != expectedId)
        {
            throw new LanUnavailableException("The device returned an unexpected response id.");
        }

        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind != JsonValueKind.Object)
            {
                throw new LanUnavailableException("The device returned a malformed error.");
            }

            var code = ReadInt32(error, "code")
                ?? throw new LanUnavailableException("The device returned a malformed error.");
            var message = ReadString(error, "message") ?? $"Device error {code}.";

            throw new MiHomeException(code, message, isDeviceError: true);
        }

        if (!root.TryGetProperty("result", out var result))
        {
            throw new LanUnavailableException("The device response had no result.");
        }

        return result.Clone();
    }

    private static void EnsureSetResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
        {
            throw ProtocolError("the property write response has no result entries");
        }

        foreach (var entry in result.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw ProtocolError("a property write result entry is not an object");
            }

            var code = ReadInt32(entry, "code")
                ?? throw ProtocolError("a property write result entry has no numeric code");

            if (code is 0 or 1)
            {
                continue;
            }

            throw DeviceError(entry, code);
        }
    }

    private static JsonElement ParseActionResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("code", out var codeElement)
            || codeElement.ValueKind != JsonValueKind.Number
            || !codeElement.TryGetInt32(out var code))
        {
            throw new LanUnavailableException("The action result has no numeric code.");
        }

        if (code != 0)
        {
            throw DeviceError(result, code);
        }

        return result.TryGetProperty("out", out var output) ? output.Clone() : EmptyArrayElement();
    }

    private static MiHomeException ProtocolError(string detail)
        => new(
            _protocolErrorCode,
            $"Unexpected miIO response: {detail}.",
            isDeviceError: false);

    private static MiHomeException DeviceError(JsonElement result, int code)
        => new(
            code,
            ReadString(result, "message") ?? $"Device error {code}.",
            isDeviceError: true);

    private static JsonElement EmptyArrayElement()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<MiPropertyValue> ParseProperties(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new LanUnavailableException("The device returned a malformed property result.");
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

            values.Add(new MiPropertyValue(
                new MiPropertyKey(siid.Value, piid.Value),
                value.Clone()));
        }

        return values;
    }

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

    // Current UTC seconds is the reference default. Device-specific stamp bases are unverified.
    private static uint Stamp() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

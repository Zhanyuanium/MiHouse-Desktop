using System.Collections.Concurrent;
using System.Text.Json;
using MiHouse.Core.Lan;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.Core.Devices;

/// <summary>
/// Device-level control. Spec lookups prefer the device's exact <c>spec_type</c> and fall back to
/// the model index; control calls target <see cref="MiDevice.ControlDid"/>. When a local IP and
/// token are known, LAN is tried first and the cloud is the fallback.
/// </summary>
public sealed class DeviceController : IDeviceController
{
    private readonly IMiHomeCloudClient _cloud;
    private readonly ISpecCatalog _specs;
    private readonly MiHomeSession _session;
    private static readonly TimeSpan _lanBlockDuration = TimeSpan.FromMinutes(5);
    private readonly ILanTransport? _lan;
    private readonly IDeviceSecretStore? _secrets;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lanBlockedUntil =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a controller for one authenticated session.
    /// </summary>
    public DeviceController(
        IMiHomeCloudClient cloud,
        ISpecCatalog specs,
        MiHomeSession session,
        ILanTransport? lan = null,
        IDeviceSecretStore? secrets = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(session);

        _cloud = cloud;
        _specs = specs;
        _session = session;
        _lan = lan;
        _secrets = secrets;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<SpecInstance?> GetSpecAsync(MiDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        SpecInstance? spec = null;
        if (!string.IsNullOrEmpty(device.SpecType))
        {
            try
            {
                spec = await _specs.GetAsync(device.SpecType, cancellationToken).ConfigureAwait(false);
            }
            catch (MiSpecException)
            {
                spec = null;
            }
        }

        spec ??= await _specs.GetForModelAsync(device.Model, cancellationToken).ConfigureAwait(false);
        if (spec is null)
        {
            return null;
        }

        if (device.ChannelSiid is not int channelSiid)
        {
            return spec;
        }

        var services = spec.Services.Where(service => service.Iid == channelSiid).ToArray();
        return spec with { Services = services };
    }

    /// <summary>
    /// Reads properties from the device's control target.
    /// </summary>
    /// <remarks>
    /// A missing key means the device either did not return the value or rejected it; the
    /// per-property response codes are not preserved.
    /// </remarks>
    public async Task<IReadOnlyDictionary<MiPropertyKey, JsonElement>> ReadAsync(
        MiDevice device,
        IReadOnlyList<MiPropertyKey> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(properties);

        var values = new Dictionary<MiPropertyKey, JsonElement>();
        if (properties.Count == 0)
        {
            return values;
        }

        foreach (var property in properties)
        {
            EnsureChannel(device, property.Siid);
        }

        var lan = _lan;
        var lanEndpoint = CreateLanEndpoint(device);
        if (lan is not null && lanEndpoint is not null)
        {
            try
            {
                var lanValues = await lan
                    .GetPropertiesAsync(lanEndpoint, properties, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var value in lanValues)
                {
                    values[value.Key] = value.Value;
                }

                ClearLanBlock(device.ControlDid);
                return values;
            }
            catch (LanUnavailableException)
            {
                values.Clear();
                BlockLan(device.ControlDid);
            }
        }

        var read = await _cloud
            .GetPropertiesAsync(_session, device.ControlDid, properties, cancellationToken)
            .ConfigureAwait(false);

        foreach (var value in read)
        {
            values[value.Key] = value.Value;
        }

        return values;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        MiDevice device,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        EnsureChannel(device, property.Siid);

        var lan = _lan;
        var lanEndpoint = CreateLanEndpoint(device);
        if (lan is not null && lanEndpoint is not null)
        {
            try
            {
                await lan
                    .SetPropertyAsync(lanEndpoint, property, value, cancellationToken)
                    .ConfigureAwait(false);

                ClearLanBlock(device.ControlDid);
                return;
            }
            catch (LanUnavailableException)
            {
                BlockLan(device.ControlDid);
            }
        }

        await _cloud
            .SetPropertyAsync(_session, device.ControlDid, property, value, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JsonElement> InvokeAsync(
        MiDevice device,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(input);
        EnsureChannel(device, action.Siid);

        var lan = _lan;
        var lanEndpoint = CreateLanEndpoint(device);
        if (lan is not null && lanEndpoint is not null)
        {
            try
            {
                var output = await lan
                    .InvokeActionAsync(lanEndpoint, action, input, cancellationToken)
                    .ConfigureAwait(false);

                ClearLanBlock(device.ControlDid);
                return output;
            }
            catch (LanUnavailableException)
            {
                BlockLan(device.ControlDid);
            }
        }

        return await _cloud
            .InvokeActionAsync(_session, device.ControlDid, action, input, cancellationToken)
            .ConfigureAwait(false);
    }

    private LanEndpoint? CreateLanEndpoint(MiDevice device)
    {
        if (_lan is null
            || _secrets is null
            || device.LocalIp is null
            || !LanAddress.IsPrivate(device.LocalIp))
        {
            return null;
        }

        if (_lanBlockedUntil.TryGetValue(device.ControlDid, out var blockedUntil)
            && blockedUntil > _clock())
        {
            return null;
        }

        var token = _secrets.GetToken(device.ControlDid) ?? _secrets.GetToken(device.Did);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        return new LanEndpoint(device.ControlDid, device.LocalIp, token);
    }

    private void ClearLanBlock(string controlDid) => _lanBlockedUntil.TryRemove(controlDid, out _);

    private void BlockLan(string controlDid)
        => _lanBlockedUntil[controlDid] = _clock() + _lanBlockDuration;

    private static void EnsureChannel(MiDevice device, int siid)
    {
        if (device.ChannelSiid is not int channelSiid || siid == channelSiid)
        {
            return;
        }

        throw new ArgumentException(
            $"Device '{device.Did}' is split channel siid {channelSiid}; address siid {siid} is not allowed.",
            nameof(siid));
    }
}

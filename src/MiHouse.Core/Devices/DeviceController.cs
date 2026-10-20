using System.Text.Json;
using MiHouse.Core.Session;
using MiHouse.Core.Spec;

namespace MiHouse.Core.Devices;

/// <summary>
/// Device-level control. Spec lookups prefer the device's exact <c>spec_type</c> and fall back to
/// the model index; control calls target <see cref="MiDevice.ControlDid"/>.
/// </summary>
public sealed class DeviceController : IDeviceController
{
    private readonly IMiHomeCloudClient _cloud;
    private readonly ISpecCatalog _specs;
    private readonly MiHomeSession _session;

    /// <summary>
    /// Creates a controller for one authenticated session.
    /// </summary>
    public DeviceController(IMiHomeCloudClient cloud, ISpecCatalog specs, MiHomeSession session)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(session);

        _cloud = cloud;
        _specs = specs;
        _session = session;
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
    public Task WriteAsync(
        MiDevice device,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        EnsureChannel(device, property.Siid);

        return _cloud.SetPropertyAsync(_session, device.ControlDid, property, value, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JsonElement> InvokeAsync(
        MiDevice device,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(input);
        EnsureChannel(device, action.Siid);

        return _cloud.InvokeActionAsync(_session, device.ControlDid, action, input, cancellationToken);
    }

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

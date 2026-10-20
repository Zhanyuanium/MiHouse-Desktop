using System.Text.Json;
using MiHouse.Core.Session;

namespace MiHouse.Core.Devices;

/// <summary>
/// Device-level control: resolves the device's spec, reads and writes properties, and invokes
/// actions, applying the split-channel rules the cloud requires.
/// </summary>
public interface IDeviceController
{
    /// <summary>
    /// Returns the device's spec. When the device reports a <c>spec_type</c> that resolves, that
    /// definition wins over the model index. For split channels only the channel's service is
    /// returned, with its <c>Iid</c> preserved.
    /// </summary>
    Task<Spec.SpecInstance?> GetSpecAsync(MiDevice device, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the requested properties. Values the device rejects are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<MiPropertyKey, JsonElement>> ReadAsync(
        MiDevice device,
        IReadOnlyList<MiPropertyKey> properties,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes one property, targeting the parent device for split channels.
    /// </summary>
    Task WriteAsync(
        MiDevice device,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invokes an action, targeting the parent device for split channels.
    /// </summary>
    Task<JsonElement> InvokeAsync(
        MiDevice device,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken);
}

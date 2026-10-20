using System.Text.Json;
using MiHouse.Core.Session;

namespace MiHouse.Core.Devices;

/// <summary>
/// Mi Home cloud operations needed to list and control devices.
/// </summary>
public interface IMiHomeCloudClient
{
    /// <summary>
    /// Returns every device visible to the account, across all homes.
    /// </summary>
    Task<IReadOnlyList<MiDevice>> GetDevicesAsync(MiHomeSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the given properties of one device.
    /// </summary>
    /// <remarks>
    /// Devices may return fewer values than requested when a property is unsupported or
    /// temporarily unavailable; callers must match results by <see cref="MiPropertyKey"/>.
    /// </remarks>
    Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
        MiHomeSession session,
        string did,
        IReadOnlyList<MiPropertyKey> properties,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes one property.
    /// </summary>
    Task SetPropertyAsync(
        MiHomeSession session,
        string did,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invokes an action and returns its output payload.
    /// </summary>
    Task<JsonElement> InvokeActionAsync(
        MiHomeSession session,
        string did,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken);
}

using System.Text.Json;
using MiHouse.Core.Session;

namespace MiHouse.Core.Devices;

/// <summary>
/// Mi Home cloud operations needed to list and control devices.
/// </summary>
public interface IMiHomeCloudClient
{
    /// <summary>
    /// Returns every home visible to the account, owned and shared, with their rooms.
    /// </summary>
    Task<IReadOnlyList<MiHome>> GetHomesAsync(MiHomeSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the devices of one home, with <see cref="MiDevice.RoomName"/> resolved from the
    /// home's room membership.
    /// </summary>
    Task<IReadOnlyList<MiDevice>> GetDevicesAsync(
        MiHomeSession session,
        MiHome home,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// Returns the manual scenes of one home. Shared homes are queried with their owner uid.
    /// </summary>
    Task<IReadOnlyList<MiScene>> GetScenesAsync(
        MiHomeSession session,
        MiHome home,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a manual scene. Success is the top-level cloud code; the payload carries no result.
    /// </summary>
    Task RunSceneAsync(
        MiHomeSession session,
        MiHome home,
        string sceneId,
        CancellationToken cancellationToken);
}

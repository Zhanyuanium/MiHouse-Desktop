using System.Text.Json;

namespace MiHouse.Core.Devices;

/// <summary>
/// A device returned by the Mi Home cloud.
/// </summary>
/// <param name="Did">Device id, used by every control request.</param>
/// <param name="Model">Product model, for example <c>yeelink.light.color1</c>.</param>
/// <param name="Name">User-visible device name.</param>
/// <param name="RoomName">Room the device is assigned to, when known.</param>
/// <param name="IsOnline">Last online state reported by the cloud.</param>
public sealed record MiDevice(string Did, string Model, string Name, string? RoomName, bool IsOnline);

/// <summary>
/// Address of one property inside the miot-spec service tree.
/// </summary>
public readonly record struct MiPropertyKey(int Siid, int Piid);

/// <summary>
/// Address of one action inside the miot-spec service tree.
/// </summary>
public readonly record struct MiActionKey(int Siid, int Aiid);

/// <summary>
/// A property value as reported by the device.
/// </summary>
public readonly record struct MiPropertyValue(MiPropertyKey Key, JsonElement Value);

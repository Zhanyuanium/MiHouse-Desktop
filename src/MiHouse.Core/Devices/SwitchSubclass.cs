namespace MiHouse.Core.Devices;

/// <summary>
/// One entry of the cloud's switch subclass catalog. A switch channel points at an entry with
/// its <see cref="Id"/>, and <see cref="CategoryId"/> says what kind of device the channel is.
/// </summary>
/// <param name="Id">Subclass id; globally unique across the catalog.</param>
/// <param name="Name">Display name reported by the catalog.</param>
/// <param name="CategoryId">Catalog category id, for example <c>12</c> for lighting.</param>
public sealed record SwitchSubclass(int Id, string Name, int CategoryId);

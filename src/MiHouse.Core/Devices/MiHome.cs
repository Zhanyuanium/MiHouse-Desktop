namespace MiHouse.Core.Devices;

/// <summary>
/// A Mi Home home. Shared homes are those created by another account, including homes where
/// the signed-in account is an administrator.
/// </summary>
/// <param name="Id">Home id used by home-scoped requests.</param>
/// <param name="Name">User-visible home name.</param>
/// <param name="OwnerUserId">Numeric id of the account that created the home.</param>
/// <param name="IsShared">True when the home was created by another account.</param>
/// <param name="Rooms">Rooms of this home, including the devices assigned to each.</param>
public sealed record MiHome(
    string Id,
    string Name,
    string OwnerUserId,
    bool IsShared,
    IReadOnlyList<MiRoom> Rooms);

/// <summary>
/// A room inside a home. The cloud does not put a room id on devices, so membership is
/// expressed by <see cref="DeviceIds"/>.
/// </summary>
public sealed record MiRoom(string Id, string Name, IReadOnlyList<string> DeviceIds);

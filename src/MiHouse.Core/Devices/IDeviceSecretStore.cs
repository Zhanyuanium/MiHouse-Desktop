namespace MiHouse.Core.Devices;

/// <summary>
/// Holds per-device LAN tokens in memory only. Tokens are never persisted, displayed or logged.
/// </summary>
public interface IDeviceSecretStore
{
    /// <summary>
    /// Stores the LAN token reported by the cloud for a device.
    /// </summary>
    void SetToken(string did, string token);

    /// <summary>
    /// Returns the token for a device, or <see langword="null"/> when it is unknown.
    /// </summary>
    string? GetToken(string did);

    /// <summary>
    /// Forgets every stored token, for example on sign-out.
    /// </summary>
    void Clear();
}

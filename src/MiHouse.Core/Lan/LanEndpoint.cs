namespace MiHouse.Core.Lan;

/// <summary>
/// Everything needed to reach one device on the local network. The token is a secret and must
/// never be shown in the UI or written to logs.
/// </summary>
public sealed record LanEndpoint(string Did, string Ip, string Token)
{
    /// <inheritdoc />
    public override string ToString() => $"LanEndpoint({Did}, {Ip}, token=***)";
}

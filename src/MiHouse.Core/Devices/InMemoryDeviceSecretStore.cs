using System.Collections.Concurrent;

namespace MiHouse.Core.Devices;

/// <summary>
/// Process-local device token store. Keys are device ids and are matched case-sensitively.
/// </summary>
public sealed class InMemoryDeviceSecretStore : IDeviceSecretStore
{
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void SetToken(string did, string token)
    {
        ArgumentNullException.ThrowIfNull(did);
        ArgumentNullException.ThrowIfNull(token);
        _tokens[did] = token;
    }

    /// <inheritdoc />
    public string? GetToken(string did)
    {
        ArgumentNullException.ThrowIfNull(did);
        return _tokens.TryGetValue(did, out var token) ? token : null;
    }

    /// <inheritdoc />
    public void Clear() => _tokens.Clear();
}

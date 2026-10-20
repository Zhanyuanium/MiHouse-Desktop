using MiHouse.Core.Session;

namespace MiHouse.Core.Auth;

/// <summary>
/// Persists the session between runs.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Loads the stored session, or <see langword="null"/> when the user is not signed in.
    /// </summary>
    Task<MiHomeSession?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores the session, replacing any previous value.
    /// </summary>
    Task SaveAsync(MiHomeSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the stored session.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

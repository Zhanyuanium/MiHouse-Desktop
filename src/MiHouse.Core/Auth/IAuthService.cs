using MiHouse.Core.Session;

namespace MiHouse.Core.Auth;

/// <summary>
/// One pending QR login attempt.
/// </summary>
/// <param name="ImageUrl">Absolute URL of the QR image to display.</param>
/// <param name="PollUrl">Absolute long-poll URL owned by this attempt.</param>
/// <param name="ExpiresAt">Moment after which the QR code is no longer valid.</param>
public sealed record QrLoginTicket(string ImageUrl, string PollUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Account login for the Mi Home cloud.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Starts a QR login and returns the code to display.
    /// </summary>
    Task<QrLoginTicket> StartQrLoginAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Polls the ticket once. Returns <see langword="null"/> while the user has not confirmed yet.
    /// </summary>
    /// <exception cref="TimeoutException">The ticket expired before confirmation.</exception>
    Task<MiHomeSession?> PollQrLoginAsync(QrLoginTicket ticket, CancellationToken cancellationToken);

    /// <summary>
    /// Password login, including the 2FA ticket flow when the account requires it.
    /// </summary>
    Task<MiHomeSession> LoginWithPasswordAsync(string account, string password, CancellationToken cancellationToken);
}

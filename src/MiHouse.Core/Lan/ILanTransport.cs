using System.Text.Json;
using MiHouse.Core.Devices;

namespace MiHouse.Core.Lan;

/// <summary>
/// miIO control over UDP. Ordinary device rejections surface as <c>MiHomeException</c> with the
/// device code. Only safely retryable transport failures throw
/// <see cref="LanUnavailableException"/>; an action that was sent but whose reply was not
/// confirmed throws <see cref="LanOutcomeUnknownException"/> and must not be retried automatically.
/// </summary>
public interface ILanTransport
{
    /// <summary>
    /// Reads properties over UDP. Throws <see cref="LanUnavailableException"/> when the device
    /// cannot be reached, the token is rejected, or a reply fails validation.
    /// </summary>
    Task<IReadOnlyList<MiPropertyValue>> GetPropertiesAsync(
        LanEndpoint endpoint,
        IReadOnlyList<MiPropertyKey> properties,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes one property over UDP. Throws <see cref="LanUnavailableException"/> for transport
    /// failures and <c>MiHomeException</c> when the device rejects the write.
    /// </summary>
    Task SetPropertyAsync(
        LanEndpoint endpoint,
        MiPropertyKey property,
        JsonElement value,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invokes an action over UDP. Throws <see cref="LanOutcomeUnknownException"/> when the request
    /// was sent but no verified reply arrived; callers must not retry that automatically.
    /// </summary>
    Task<JsonElement> InvokeActionAsync(
        LanEndpoint endpoint,
        MiActionKey action,
        IReadOnlyList<JsonElement> input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Raised when a local request cannot be completed and the caller should use the cloud.
/// </summary>
public sealed class LanUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Raised when an action request was sent but its outcome could not be confirmed. The device may
/// have executed it, so callers must not retry or fall back to another transport automatically.
/// </summary>
public sealed class LanOutcomeUnknownException(string message, Exception? innerException = null)
    : Exception(message, innerException);

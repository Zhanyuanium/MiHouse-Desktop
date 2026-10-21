namespace MiHouse.Core.Devices;

/// <summary>
/// Raised when a device cannot be addressed safely, for example when the cloud reports an
/// incomplete split-channel identity.
/// </summary>
public sealed class MiDeviceException : Exception
{
    /// <summary>
    /// Creates an exception without a message.
    /// </summary>
    public MiDeviceException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    public MiDeviceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and underlying cause.
    /// </summary>
    public MiDeviceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace MiHouse.Core.Spec;

/// <summary>
/// Raised when miot-spec data cannot be retrieved or parsed.
/// </summary>
public sealed class MiSpecException : Exception
{
    /// <summary>
    /// Creates an exception without a message.
    /// </summary>
    public MiSpecException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    public MiSpecException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and underlying cause.
    /// </summary>
    public MiSpecException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace MiHouse.Core.Auth;

/// <summary>
/// Raised when the Xiaomi account service rejects a login request.
/// </summary>
public sealed class MiAuthException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MiAuthException"/> class.
    /// </summary>
    /// <param name="message">Description of the failure.</param>
    public MiAuthException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MiAuthException"/> class.
    /// </summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="code">Xiaomi error code, when the server returned one.</param>
    public MiAuthException(string message, long? code)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the Xiaomi error code, when the server returned one.
    /// </summary>
    public long? Code { get; }
}

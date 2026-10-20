namespace MiHouse.Core.Cloud;

/// <summary>
/// A non-zero response code from the Mi Home cloud API.
/// </summary>
public sealed class MiHomeException : Exception
{
    /// <summary>
    /// Creates an exception from a cloud response.
    /// </summary>
    /// <param name="code">The cloud <c>code</c> value.</param>
    /// <param name="message">Human-readable failure reason.</param>
    /// <param name="description">Optional cloud <c>description</c> field.</param>
    /// <param name="isDeviceError">
    /// True when the code came from a device-level result (a property-write entry or an action
    /// result), false for account, HTTP, protocol and paging failures.
    /// </param>
    public MiHomeException(
        int code,
        string message,
        string? description = null,
        bool isDeviceError = false)
        : base(message)
    {
        Code = code;
        Description = description;
        IsDeviceError = isDeviceError;
    }

    /// <summary>
    /// The cloud response code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// True when <see cref="Code"/> was reported by the device itself; false for account-level,
    /// HTTP, protocol and paging failures.
    /// </summary>
    public bool IsDeviceError { get; }

    /// <summary>
    /// The cloud <c>description</c> field, when the response carried one.
    /// </summary>
    public string? Description { get; }
}

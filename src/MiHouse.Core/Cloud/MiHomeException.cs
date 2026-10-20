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
    public MiHomeException(int code, string message, string? description = null)
        : base(message)
    {
        Code = code;
        Description = description;
    }

    /// <summary>
    /// The cloud response code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// The cloud <c>description</c> field, when the response carried one.
    /// </summary>
    public string? Description { get; }
}

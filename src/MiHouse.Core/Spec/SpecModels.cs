namespace MiHouse.Core.Spec;

/// <summary>
/// One entry of a value-list constraint.
/// </summary>
public sealed record SpecValueList(int Value, string Description);

/// <summary>
/// Numeric constraint of a property.
/// </summary>
public sealed record SpecRange(double Min, double Max, double Step);

/// <summary>
/// A miot-spec property.
/// </summary>
public sealed record SpecProperty(
    int Iid,
    string Type,
    string Description,
    string Format,
    bool Readable,
    bool Writable,
    bool Notifiable,
    IReadOnlyList<SpecValueList>? Values,
    SpecRange? Range);

/// <summary>
/// A miot-spec action.
/// </summary>
public sealed record SpecAction(
    int Iid,
    string Type,
    string Description,
    IReadOnlyList<SpecProperty> Inputs);

/// <summary>
/// One service of a device type.
/// </summary>
public sealed record SpecService(
    int Iid,
    string Type,
    string Description,
    IReadOnlyList<SpecProperty> Properties,
    IReadOnlyList<SpecAction> Actions);

/// <summary>
/// Resolved miot-spec definition for a device type.
/// </summary>
public sealed record SpecInstance(
    string Type,
    string Description,
    IReadOnlyList<SpecService> Services);

/// <summary>
/// Resolves and caches miot-spec definitions.
/// </summary>
public interface ISpecCatalog
{
    /// <summary>
    /// Returns the spec for a device model, or <see langword="null"/> when the model has no
    /// published miot-spec definition.
    /// </summary>
    Task<SpecInstance?> GetForModelAsync(string model, CancellationToken cancellationToken);
}

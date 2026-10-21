using System.Text.Json.Serialization;

namespace MiHouse.Core.Spec;

/// <summary>
/// Entry of the published miot-spec instance index.
/// </summary>
internal sealed class SpecIndexEntryDto
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

/// <summary>
/// Wrapper for the miot-spec instance index; the service returns
/// <c>{ "instances": [...] }</c> even though older snapshots were a bare array.
/// </summary>
internal sealed class SpecIndexDto
{
    [JsonPropertyName("instances")]
    public List<SpecIndexEntryDto>? Instances { get; set; }
}

/// <summary>
/// Wire shape of a miot-spec device type.
/// </summary>
internal sealed class SpecInstanceDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("services")]
    public List<SpecServiceDto>? Services { get; set; }
}

/// <summary>
/// Wire shape of a miot-spec service.
/// </summary>
internal sealed class SpecServiceDto
{
    [JsonPropertyName("iid")]
    public int? Iid { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("properties")]
    public List<SpecPropertyDto>? Properties { get; set; }

    [JsonPropertyName("actions")]
    public List<SpecActionDto>? Actions { get; set; }
}

/// <summary>
/// Wire shape of a miot-spec property; also used for action arguments.
/// </summary>
internal sealed class SpecPropertyDto
{
    [JsonPropertyName("iid")]
    public int? Iid { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("access")]
    public List<string>? Access { get; set; }

    [JsonPropertyName("value-list")]
    public List<SpecValueListDto>? ValueList { get; set; }

    [JsonPropertyName("value-range")]
    public List<double>? ValueRange { get; set; }
}

/// <summary>
/// Wire shape of one entry of a property value list.
/// </summary>
internal sealed class SpecValueListDto
{
    [JsonPropertyName("value")]
    public int? Value { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Wire shape of a miot-spec action. Arguments in <c>in</c> are either nested
/// property objects or integer property iid references.
/// </summary>
internal sealed class SpecActionDto
{
    [JsonPropertyName("iid")]
    public int? Iid { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("in")]
    public List<System.Text.Json.JsonElement>? In { get; set; }
}

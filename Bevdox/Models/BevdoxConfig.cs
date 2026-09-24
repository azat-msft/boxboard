using System.Text.Json.Serialization;

namespace Bevdox.Models;

public sealed class BevdoxConfig
{
    public DateTimeOffset? LastRefreshUtc { get; set; }
    public List<DevBoxInstance> Instances { get; set; } = [];
}

[JsonSerializable(typeof(BevdoxConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class BevdoxJsonContext : JsonSerializerContext;

using System.Text.Json.Serialization;

namespace Bevdox.Models;

public sealed class DevBoxInstance
{
    public required string UniqueId { get; set; }
    public required string OriginalName { get; set; }
    public string? DisplayName { get; set; }
    public required string ProjectName { get; set; }
    public required string DevCenterUri { get; set; }
    public required string State { get; set; }
    public required string Location { get; set; }
    public string? PoolName { get; set; }

    [JsonIgnore]
    public string EffectiveName => DisplayName ?? OriginalName;
}

using System.Text.Json.Serialization;
namespace DesignSpace.Core;

public sealed partial record AnimationTrack
{
    /// <summary>Key values are offsets from the underlying base/state value.</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAdditive { get; init; }
    /// <summary>Adds the final key value for each completed child-clock iteration (WPF keyframe semantics).</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCumulative { get; init; }
}

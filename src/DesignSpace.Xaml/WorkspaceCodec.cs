using System.Text.Json;
using System.Text.Json.Serialization;
using DesignSpace.Core;
namespace DesignSpace.Xaml;

[JsonSourceGenerationOptions(WriteIndented=true,PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,IgnoreReadOnlyProperties=true,MaxDepth=256)]
[JsonSerializable(typeof(DesignWorkspaceSnapshot))]
internal partial class WorkspaceJsonContext : JsonSerializerContext { }

/// <summary>Versioned inert multi-document persistence with generated metadata for trimmed browser builds.</summary>
public static class WorkspaceCodec
{
    public static string Write(DesignWorkspaceSnapshot workspace)
    {
        WorkspaceValidator.Validate(workspace);
        var text=JsonSerializer.Serialize(workspace,WorkspaceJsonContext.Default.DesignWorkspaceSnapshot);
        if(System.Text.Encoding.UTF8.GetByteCount(text)>WorkspaceValidator.MaxCharacters)throw new InvalidDataException("Serialized workspace exceeds 16 MiB.");return text;
    }
    public static DesignWorkspaceSnapshot Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);if(System.Text.Encoding.UTF8.GetByteCount(text)>WorkspaceValidator.MaxCharacters)throw new InvalidDataException("Workspace exceeds 16 MiB.");
        var workspace=JsonSerializer.Deserialize(text,WorkspaceJsonContext.Default.DesignWorkspaceSnapshot)??throw new InvalidDataException("Empty workspace.");
        WorkspaceValidator.Validate(workspace);return workspace;
    }
}

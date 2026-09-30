using System.Collections.Immutable;
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
        using var json=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=256});
        var workspace=JsonSerializer.Deserialize(json.RootElement,WorkspaceJsonContext.Default.DesignWorkspaceSnapshot)??throw new InvalidDataException("Empty workspace.");
        // Source-generated init-only setters can supply zero instead of an initializer
        // when an older file omits a property. Migrate omissions, never invalid values.
        if(workspace.FormatVersion==1&&!workspace.Documents.IsDefault&&json.RootElement.TryGetProperty("documents",out var files)&&files.ValueKind==JsonValueKind.Array)
        {
            var documents=workspace.Documents.ToBuilder();var i=0;
            foreach(var file in files.EnumerateArray())
            {
                if(i>=documents.Count)break;
                if(documents[i] is { Editor: { } editor } document&&file.ValueKind==JsonValueKind.Object&&file.TryGetProperty("editor",out var state)&&state.ValueKind==JsonValueKind.Object)
                    documents[i]=document with{Editor=editor with
                    {
                        SnapTolerance=state.TryGetProperty("snapTolerance",out _)?editor.SnapTolerance:6,
                        DefaultMargin=state.TryGetProperty("defaultMargin",out _)?editor.DefaultMargin:8,
                        DefaultPadding=state.TryGetProperty("defaultPadding",out _)?editor.DefaultPadding:8
                    }};
                i++;
            }
            workspace=workspace with{Documents=documents.ToImmutable()};
        }
        WorkspaceValidator.Validate(workspace);return workspace;
    }
}

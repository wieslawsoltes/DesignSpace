namespace DesignSpace.Workbench.Uno;

public sealed record OpenDesignFile(string Name,string Text);
/// <summary>The host supplies storage and file dialogs. Libraries never assume browser globals or a native file system.</summary>
public interface IWorkbenchPlatform
{
    Task<OpenDesignFile?> OpenAsync();
    Task<bool> SaveAsync(string name,byte[] content,string mimeType);
    Task<string?> ReadLocalAsync(string key);
    Task WriteLocalAsync(string key,string value);
    Task<string?> ReadClipboardAsync();
    Task WriteClipboardAsync(string text);
}

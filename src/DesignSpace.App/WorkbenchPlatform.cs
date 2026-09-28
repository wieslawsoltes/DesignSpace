using System.Text.Json;
using DesignSpace.Workbench.Uno;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
namespace DesignSpace.App;

internal sealed class WorkbenchPlatform : IWorkbenchPlatform
{
    public async Task<OpenDesignFile?> OpenAsync()
    {
        var picker=new FileOpenPicker { SuggestedStartLocation=PickerLocationId.DocumentsLibrary };
        foreach(var extension in new[]{".xaml",".designspace",".designspace-workspace",".json"}) picker.FileTypeFilter.Add(extension);
        var file=await picker.PickSingleFileAsync(); if(file is null) return null;
        var properties=await file.GetBasicPropertiesAsync(); var limit=file.Name.EndsWith(".designspace-workspace",StringComparison.OrdinalIgnoreCase)?16*1024*1024:8*1024*1024; if(properties.Size>(ulong)limit) throw new IOException("Import exceeds the file size limit.");
        return new(file.Name,await FileIO.ReadTextAsync(file));
    }
    public async Task<bool> SaveAsync(string name,byte[] content,string mimeType)
    {
#if __WASM__
        var payload=JsonSerializer.Serialize(new { data=Convert.ToBase64String(content),mime=mimeType,name });
        const string script="""
            (p => {
              const binary=atob(p.data), bytes=new Uint8Array(binary.length);
              for(let i=0;i<binary.length;i++) bytes[i]=binary.charCodeAt(i);
              const url=URL.createObjectURL(new Blob([bytes],{type:p.mime}));
              const link=document.createElement('a');link.href=url;link.download=p.name;
              document.body.appendChild(link);link.click();link.remove();
              setTimeout(()=>URL.revokeObjectURL(url),30000);return 'saved';
            })
            """;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS(script+"("+payload+")"); await Task.CompletedTask; return true;
#else
        var picker=new FileSavePicker { SuggestedFileName=name,SuggestedStartLocation=PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add("DesignSpace file",new List<string> { Path.GetExtension(name) });
        var file=await picker.PickSaveFileAsync(); if(file is null) return false; await FileIO.WriteBytesAsync(file,content); return true;
#endif
    }
    public async Task<string?> ReadLocalAsync(string key)
    {
        ValidateKey(key);
#if __WASM__
        await Task.CompletedTask; var text=Uno.Foundation.WebAssemblyRuntime.InvokeJS("localStorage.getItem("+JsonSerializer.Serialize("designspace.v1."+key)+") || ''"); return string.IsNullOrEmpty(text) ? null : text;
#else
        var file=await ApplicationData.Current.LocalFolder.TryGetItemAsync("designspace-"+key) as StorageFile; return file is null ? null : await FileIO.ReadTextAsync(file);
#endif
    }
    public async Task WriteLocalAsync(string key,string value)
    {
        ValidateKey(key);
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("localStorage.setItem("+JsonSerializer.Serialize("designspace.v1."+key)+", "+JsonSerializer.Serialize(value)+"); 'saved'"); await Task.CompletedTask;
#else
        var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("designspace-"+key+".pending",CreationCollisionOption.ReplaceExisting); await FileIO.WriteTextAsync(file,value); await file.RenameAsync("designspace-"+key,NameCollisionOption.ReplaceExisting);
#endif
    }
    private static void ValidateKey(string key) { if(key.Length>64 || key.IndexOfAny(['/', '\\', ':'])>=0) throw new ArgumentException("Invalid local storage key."); }
    public async Task<string?> ReadClipboardAsync() { var content=Clipboard.GetContent(); return content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null; }
    public Task WriteClipboardAsync(string text) { var content=new DataPackage(); content.SetText(text); Clipboard.SetContent(content); return Task.CompletedTask; }
}

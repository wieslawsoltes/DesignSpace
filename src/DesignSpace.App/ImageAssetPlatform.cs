using DesignSpace.Workbench.Uno;
using DesignSpace.Rendering.Skia;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
namespace DesignSpace.App;

internal sealed class ImageAssetPlatform : IWorkbenchPlatform,IWorkbenchAssetPlatform
{
    private readonly WorkbenchPlatform _inner=new();
    public Task<OpenDesignFile?> OpenAsync()=>_inner.OpenAsync();
    public Task<bool> SaveAsync(string name,byte[] content,string mime)=>_inner.SaveAsync(name,content,mime);
    public Task<string?> ReadLocalAsync(string key)=>_inner.ReadLocalAsync(key);
    public Task WriteLocalAsync(string key,string value)=>_inner.WriteLocalAsync(key,value);
    public Task<string?> ReadClipboardAsync()=>_inner.ReadClipboardAsync();
    public Task WriteClipboardAsync(string text)=>_inner.WriteClipboardAsync(text);
    public async Task<OpenDesignAsset?> OpenImageAsync()
    {
#if __WASM__
        var selected=await BrowserFileUpload.PickAsync(image:true);if(selected is null)return null;
        var mime=Path.GetExtension(selected.Name).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".webp"=>"image/webp",".gif"=>"image/gif",_=>"image/png"};
        return new(selected.Name,selected.Bytes!,mime);
#else
        var picker=new FileOpenPicker{SuggestedStartLocation=PickerLocationId.PicturesLibrary};
        foreach(var extension in new[]{".png",".jpg",".jpeg",".webp",".gif"})picker.FileTypeFilter.Add(extension);
        var file=await picker.PickSingleFileAsync();if(file is null)return null;
        var info=await file.GetBasicPropertiesAsync();if(info.Size>EmbeddedImages.MaximumBytes)throw new InvalidDataException("Images are limited to 4 MiB.");
        var buffer=await FileIO.ReadBufferAsync(file);var bytes=new byte[buffer.Length];using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(bytes);
        var mime=Path.GetExtension(file.Name).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".webp"=>"image/webp",".gif"=>"image/gif",_=>"image/png"};
        return new(file.Name,bytes,mime);
#endif
    }
}

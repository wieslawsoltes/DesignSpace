using DesignSpace.Rendering.Skia;
using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Streams;
namespace DesignSpace.App;

internal static class ApplicationFonts
{
    private static SKTypeface? _browserTypeface;
    public static async Task InitializeAsync()
    {
#if __WASM__
        // Reuse the framework-provided licensed font. Do not depend on host-machine fonts in WebAssembly.
        var file=await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf"));
        var buffer=await FileIO.ReadBufferAsync(file); var bytes=new byte[buffer.Length];
        using(var reader=DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        using var data=SKData.CreateCopy(bytes); _browserTypeface=SKTypeface.FromData(data) ?? throw new InvalidOperationException("The browser preview typeface could not be loaded.");
        DesignTypography.DefaultTypeface=_browserTypeface;
#else
        await Task.CompletedTask;
#endif
    }
}

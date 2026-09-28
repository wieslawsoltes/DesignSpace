using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Explicit embedded raster assets, with bounded decoding and cache memory.</summary>
public sealed class EmbeddedImages : IDisposable
{
    public const int MaximumBytes=4*1024*1024;
    public const long MaximumPixels=16_000_000;
    public const long CacheByteBudget=64*1024*1024;
    private sealed record Entry(SKImage? Image,long Cost,string? Error);
    private readonly Dictionary<string,Entry> _entries=new(StringComparer.Ordinal);
    private readonly Queue<string> _order=[];
    private long _bytes;
    public long DecodeCount { get; private set; }
    public long CachedBytes=>_bytes;
    public static string CreateSource(byte[] bytes,string mime="image/png")
    {
        if(mime is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif")) throw new InvalidDataException("Supported formats are PNG, JPEG, WebP and GIF.");
        Validate(bytes); return "data:"+mime+";base64,"+Convert.ToBase64String(bytes);
    }
    private static void Validate(byte[] bytes)
    {
        if(bytes.Length==0 || bytes.Length>MaximumBytes) throw new InvalidDataException("An image must be between 1 byte and 4 MiB.");
        using var data=SKData.CreateCopy(bytes); using var codec=SKCodec.Create(data);
        if(codec is null) throw new InvalidDataException("The file is not a decodable raster image.");
        if(codec.Info.Width<=0 || codec.Info.Height<=0 || (long)codec.Info.Width*codec.Info.Height>MaximumPixels) throw new InvalidDataException("Image exceeds the 16 megapixel limit.");
    }
    public SKImage? Get(string source,out string? error)
    {
        if(_entries.TryGetValue(source,out var cached)) { error=cached.Error; return cached.Image; }
        SKImage? image=null; error=null;
        try
        {
            var comma=source.IndexOf(',');
            if(comma<0 || !source.StartsWith("data:image/",StringComparison.Ordinal) || !source[..comma].EndsWith(";base64",StringComparison.Ordinal)) throw new InvalidDataException("Use Import image; external sources are not fetched.");
            if(source.Length-comma-1>(MaximumBytes+2)/3*4) throw new InvalidDataException("Embedded image exceeds 4 MiB.");
            var bytes=Convert.FromBase64String(source[(comma+1)..]); Validate(bytes);
            using var bitmap=SKBitmap.Decode(bytes); if(bitmap is null) throw new InvalidDataException("Raster decoding failed.");
            image=SKImage.FromBitmap(bitmap); DecodeCount++;
        }
        catch(Exception ex) when(ex is InvalidDataException or FormatException or ArgumentException) { error=ex.Message; }
        var cost=image is null ? 0 : (long)image.Width*image.Height*4;
        while(_order.Count>0 && (_entries.Count>=64 || _bytes+cost>CacheByteBudget))
        {
            var key=_order.Dequeue(); if(!_entries.Remove(key,out var item)) continue; item.Image?.Dispose(); _bytes-=item.Cost;
        }
        _entries[source]=new(image,cost,error); _order.Enqueue(source); _bytes+=cost; return image;
    }
    public void Draw(SKCanvas canvas,SKImage image,SKRect bounds,string stretch)
    {
        var width=(float)image.Width; var height=(float)image.Height;
        if(stretch=="Fill") { width=bounds.Width; height=bounds.Height; }
        else if(stretch!="None") { var scale=stretch=="UniformToFill" ? Math.Max(bounds.Width/width,bounds.Height/height) : Math.Min(bounds.Width/width,bounds.Height/height); width*=scale; height*=scale; }
        var destination=SKRect.Create(bounds.MidX-width/2,bounds.MidY-height/2,width,height);
        canvas.Save(); canvas.ClipRect(bounds); canvas.DrawImage(image,destination,new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.Linear)); canvas.Restore();
    }
    public void Dispose() { foreach(var entry in _entries.Values) entry.Image?.Dispose(); _entries.Clear(); _order.Clear(); _bytes=0; }
}

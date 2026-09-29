using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Bounded, UI-thread-owned native filter cache. Returned filters are borrowed until eviction.</summary>
public sealed class EffectFilterCache : IDisposable
{
    private sealed record Entry(DesignEffect Effect,SKImageFilter? Filter);
    private readonly ResourceLruCache<string,Entry> _cache=new(64,256*1024,e=>e.Filter?.Dispose());
    public long Builds { get; private set; }
    public long Hits { get; private set; }
    public int Count=>_cache.Count;
    public SKImageFilter? Get(string source)
    {
        if(_cache.TryGetValue(source,out var entry)){Hits++;return entry.Filter;}
        var effect=EffectCodec.Parse(source);var filter=Create(effect);
        try{_cache.Add(source,new(effect,filter),512+source.Length*2L);}
        catch{filter?.Dispose();throw;}
        Builds++;return filter;
    }
    /// <summary>Creates a caller-owned filter. Gaussian sigma is radius/3; Box uses separable integer taps.</summary>
    public static SKImageFilter? Create(DesignEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);effect.Validate();
        if(effect.Kind==DesignEffectKind.Blur)
        {
            if(effect.Radius==0)return null;
            if(effect.Kernel==DesignBlurKernel.Gaussian)return SKImageFilter.CreateBlur((float)(effect.Radius/3),(float)(effect.Radius/3));
            var radius=(int)Math.Ceiling(effect.Radius);var width=radius*2+1;var kernel=Enumerable.Repeat(1f/width,width).ToArray();
            using var horizontal=SKImageFilter.CreateMatrixConvolution(new SKSizeI(width,1),kernel,1,0,new SKPointI(radius,0),SKShaderTileMode.Decal,true);
            return SKImageFilter.CreateMatrixConvolution(new SKSizeI(1,width),kernel,1,0,new SKPointI(0,radius),SKShaderTileMode.Decal,true,horizontal);
        }
        if(effect.Opacity==0)return null;
        var offset=effect.ShadowOffset;var color=BrushColor.Parse(effect.Color);
        // WPF's shadow color alpha is ignored; Opacity alone controls shadow opacity.
        var skColor=new SKColor((byte)Math.Round(color.R*255),(byte)Math.Round(color.G*255),(byte)Math.Round(color.B*255),(byte)Math.Round(effect.Opacity*255));
        return SKImageFilter.CreateDropShadow((float)offset.X,(float)offset.Y,(float)(effect.Radius/3),(float)(effect.Radius/3),skColor);
    }
    public void Dispose()=>_cache.Clear();
}

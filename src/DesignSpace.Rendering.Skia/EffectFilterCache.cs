using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Bounded, UI-thread-owned native filter cache. Returned filters are borrowed until eviction.</summary>
public sealed class EffectFilterCache : IDisposable
{
    // The pinned Skia convolution factory supports at most 2048 samples on an axis.
    // Odd, centered kernels therefore support a radius of at most 1023 device pixels.
    public const int MaxDeviceRadius=1023;
    private readonly record struct Kernel(int X,int Y);
    private sealed record Entry(SKImageFilter? Filter);
    private readonly ResourceLruCache<string,DesignEffect> _descriptors=new(128,512*1024);
    private readonly ResourceLruCache<(string,EffectRenderingMode,Kernel),Entry> _cache=new(64,256*1024,e=>e.Filter?.Dispose());
    public long Builds { get; private set; }
    public long Hits { get; private set; }
    public long Parses { get; private set; }
    public int Count=>_cache.Count;
    /// <summary>Scale is the local-to-device axis scale, including host DPI and viewport zoom.
    /// Native Gaussian/shadow filters already handle their CTM; only convolution keys depend on scale.</summary>
    public SKImageFilter? Get(string source,EffectRenderingMode mode=EffectRenderingMode.Native,double scaleX=1,double scaleY=1)
    {
        if(!_descriptors.TryGetValue(source,out var effect))
        {
            effect=EffectCodec.Parse(source);_descriptors.Add(source,effect,256+source.Length*2L);Parses++;
        }
        var kernel=DeviceKernel(effect,mode,scaleX,scaleY);
        if(_cache.TryGetValue((source,mode,kernel),out var entry)){Hits++;return entry.Filter;}
        var filter=CreateCore(effect,mode,kernel);
        try{_cache.Add((source,mode,kernel),new(filter),512+source.Length*2L);}
        catch{filter?.Dispose();throw;}
        Builds++;return filter;
    }
    /// <summary>Reads the host canvas matrix, including DPI, without changing it.</summary>
    public SKImageFilter? GetForCanvas(string source,SKCanvas canvas,EffectRenderingMode mode=EffectRenderingMode.Native)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var m=canvas.TotalMatrix;
        return Get(source,mode,Math.Sqrt((double)m.ScaleX*m.ScaleX+(double)m.SkewY*m.SkewY),
            Math.Sqrt((double)m.SkewX*m.SkewX+(double)m.ScaleY*m.ScaleY));
    }
    /// <summary>Creates a caller-owned filter, or null for a defined no-op. Unsupported native filter creation throws.</summary>
    public static SKImageFilter? Create(DesignEffect effect,EffectRenderingMode mode=EffectRenderingMode.Native,double scaleX=1,double scaleY=1)
    {
        ArgumentNullException.ThrowIfNull(effect);effect.Validate();
        return CreateCore(effect,mode,DeviceKernel(effect,mode,scaleX,scaleY));
    }
    private static Kernel DeviceKernel(DesignEffect effect,EffectRenderingMode mode,double scaleX,double scaleY)
    {
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        if(!double.IsFinite(scaleX)||!double.IsFinite(scaleY)||scaleX<0||scaleY<0)throw new ArgumentOutOfRangeException(nameof(scaleX));
        if(effect.Kind!=DesignEffectKind.Blur||effect.Kernel==DesignBlurKernel.Gaussian&&mode==EffectRenderingMode.Native)return default;
        // MatrixConvolution uses device/layer pixel coordinates, unlike native Blur.
        // WPF truncates the local radius, scales by the minimum axis and truncates again.
        var x=mode==EffectRenderingMode.WpfSoftwareCompatible?Math.Floor(Math.Floor(effect.Radius)*Math.Min(scaleX,scaleY)):Math.Ceiling(effect.Radius*scaleX);
        var y=mode==EffectRenderingMode.WpfSoftwareCompatible?x:Math.Ceiling(effect.Radius*scaleY);
        if(!double.IsFinite(x)||!double.IsFinite(y)||x>MaxDeviceRadius||y>MaxDeviceRadius)
            throw new InvalidOperationException("Convolution radius exceeds 1023 device pixels. Reduce the effect radius or preview zoom, or suppress artboard effects.");
        return new((int)x,(int)y);
    }
    private static SKImageFilter Required(SKImageFilter? filter)=>filter??throw new InvalidOperationException("The graphics backend could not create the requested effect filter.");
    private static SKImageFilter? CreateCore(DesignEffect effect,EffectRenderingMode mode,Kernel kernel)
    {
        if(effect.Kind==DesignEffectKind.Blur)
        {
            if(effect.Radius==0)return null;
            if(effect.Kernel==DesignBlurKernel.Gaussian&&mode==EffectRenderingMode.Native)
                return Required(SKImageFilter.CreateBlur((float)(effect.Radius/3),(float)(effect.Radius/3)));
            if(kernel==default)return null;
            float[] Weights(int radius)=>effect.Kernel==DesignBlurKernel.Gaussian?WpfEffectMath.GaussianKernel(radius):Enumerable.Repeat(1f/(radius*2+1),radius*2+1).ToArray();
            var horizontal=kernel.X==0?null:Required(SKImageFilter.CreateMatrixConvolution(new SKSizeI(kernel.X*2+1,1),Weights(kernel.X),1,0,new SKPointI(kernel.X,0),SKShaderTileMode.Decal,true));
            if(kernel.Y==0)return horizontal;
            try{return Required(SKImageFilter.CreateMatrixConvolution(new SKSizeI(1,kernel.Y*2+1),Weights(kernel.Y),1,0,new SKPointI(0,kernel.Y),SKShaderTileMode.Decal,true,horizontal));}
            finally{horizontal?.Dispose();}
        }
        if(effect.Opacity==0)return null;
        var offset=effect.ShadowOffset;var color=BrushColor.Parse(effect.Color);
        // WPF's shadow color alpha is ignored; Opacity alone controls shadow opacity.
        var skColor=new SKColor((byte)Math.Round(color.R*255),(byte)Math.Round(color.G*255),(byte)Math.Round(color.B*255),mode==EffectRenderingMode.WpfSoftwareCompatible?WpfEffectMath.ShadowAlpha(effect.Opacity):(byte)Math.Round(effect.Opacity*255));
        return Required(SKImageFilter.CreateDropShadow((float)offset.X,(float)offset.Y,(float)(effect.Radius/3),(float)(effect.Radius/3),skColor));
    }
    public void Dispose(){_cache.Clear();_descriptors.Clear();}
}

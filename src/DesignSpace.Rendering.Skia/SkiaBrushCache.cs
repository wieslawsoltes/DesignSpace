using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Native gradient factory. The caller owns the returned shader; no raster gradient texture is generated.</summary>
public static class SkiaBrushShader
{
    private static SKMatrix Matrix(DMatrix m)=>new(){ScaleX=(float)m.M11,SkewX=(float)m.M21,SkewY=(float)m.M12,ScaleY=(float)m.M22,TransX=(float)m.DX,TransY=(float)m.DY,Persp2=1};
    private static SKPoint Point(DPoint p)=>new((float)p.X,(float)p.Y);
    private static SKColorF Color(BrushColor c,double opacity)=>new((float)c.R,(float)c.G,(float)c.B,(float)(c.A*opacity));
    public static SKShader Create(DesignBrush brush,DRect bounds)
    {
        brush.Validate();foreach(var v in new[]{bounds.X,bounds.Y,bounds.Width,bounds.Height})if(!double.IsFinite(v)||Math.Abs(v)>1e9)throw new InvalidDataException("Brush bounds exceed the finite coordinate budget.");
        using var space=SKColorSpace.CreateSrgb();
        SKShader Solid(BrushColor color)=>SKShader.CreateColor(Color(color,brush.Opacity),space);
        if(brush.Kind==DesignBrushKind.Solid)return Solid(BrushColor.Parse(brush.Color));
        if(brush.Stops.IsEmpty||brush.Opacity==0||bounds.Width<=0||bounds.Height<=0)return SKShader.CreateColor(SKColors.Transparent);
        var matrix=brush.MappingTo(bounds);if(!matrix.TryInvert(out _))return SKShader.CreateColor(SKColors.Transparent);
        if(brush.Stops.Length==1)return Solid(BrushColor.Parse(brush.Stops[0].Color));
        var stops=brush.Stops.OrderBy(s=>s.Offset).Where(s=>s.Offset>=0&&s.Offset<=1).Select(s=>(s.Offset,Color:BrushColor.Parse(s.Color))).ToList();
        if(stops.Count==0||stops[0].Offset>0)stops.Insert(0,(0,BrushEditing.Sample(brush,0)));
        if(stops[^1].Offset<1)stops.Add((1,BrushEditing.Sample(brush,1)));
        var colors=stops.Select(s=>Color(s.Color,brush.Opacity)).ToArray();var positions=stops.Select(s=>(float)s.Offset).ToArray();
        var tile=brush.Spread switch{DesignGradientSpread.Repeat=>SKShaderTileMode.Repeat,DesignGradientSpread.Reflect=>SKShaderTileMode.Mirror,_=>SKShaderTileMode.Clamp};
        if(brush.Kind==DesignBrushKind.Linear)
        {
            // Map endpoints first: a non-square box must not skew the metric of the gradient axis.
            var coordinates=DMatrix.Translate(bounds.X,bounds.Y)*(brush.Mapping==DesignBrushMapping.RelativeToBoundingBox?DMatrix.Scale(bounds.Width,bounds.Height):DMatrix.Identity);
            coordinates.TryInvert(out var inverse);var start=coordinates.Map(brush.Start);var end=coordinates.Map(brush.End);
            if(start==end)return Solid(BrushColor.Parse(brush.Stops.OrderBy(s=>s.Offset).Last().Color));
            return SKShader.CreateLinearGradient(Point(start),Point(end),colors,space,positions,tile,Matrix(matrix*inverse))??throw new InvalidOperationException("The native linear gradient could not be created.");
        }
        if(brush.RadiusX==0||brush.RadiusY==0)return Solid(BrushColor.Parse(brush.Stops.OrderBy(s=>s.Offset).Last().Color));
        var ellipse=matrix*DMatrix.Translate(brush.Center.X,brush.Center.Y)*DMatrix.Scale(brush.RadiusX,brush.RadiusY);
        var focus=new DPoint((brush.Origin.X-brush.Center.X)/brush.RadiusX,(brush.Origin.Y-brush.Center.Y)/brush.RadiusY);
        if(!double.IsFinite(focus.X)||!double.IsFinite(focus.Y)||!ellipse.TryInvert(out _))throw new InvalidDataException("Degenerate radial gradient transform.");
        return SKShader.CreateTwoPointConicalGradient(Point(focus),0,new SKPoint(0,0),1,colors,space,positions,tile,Matrix(ellipse))??throw new InvalidOperationException("The native radial gradient could not be created.");
    }
}

/// <summary>UI-thread LRU of parsed brush data and native shaders. Get returns a borrowed, immutable shader.</summary>
public sealed class SkiaBrushCache : IDisposable
{
    private readonly ResourceLruCache<string,DesignBrush> _parsed=new(128,2*1024*1024);
    private readonly ResourceLruCache<(string,DRect),SKShader> _shaders=new(256,4*1024*1024,s=>s.Dispose());
    public long Parses { get; private set; }
    public long Builds { get; private set; }
    public long Hits { get; private set; }
    public long EstimatedBytes=>_shaders.Cost+_parsed.Cost;
    public SKShader Get(string source,DRect bounds)
    {
        if(!_parsed.TryGetValue(source,out var brush))
        {
            brush=BrushCodec.Parse(source);Parses++;_parsed.Add(source,brush,256L+source.Length*2L+brush.Stops.Length*96L);
        }
        var key=(source,brush.Kind==DesignBrushKind.Solid?default:bounds);
        if(_shaders.TryGetValue(key,out var shader)){Hits++;return shader;}
        shader=SkiaBrushShader.Create(brush,bounds);
        try{_shaders.Add(key,shader,512L+source.Length*2L+brush.Stops.Length*32L);Builds++;return shader;}catch{shader.Dispose();throw;}
    }
    public void Dispose(){_shaders.Clear();_parsed.Clear();}
}

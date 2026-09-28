using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Caller-owned filled stroke geometry, shared by drawing, picking and outline conversion.</summary>
public static class SkiaStrokeGeometry
{
    public const int MaxOutlinePoints=262144;
    private static SKStrokeCap Cap(DesignLineCap cap)=>cap switch { DesignLineCap.Round=>SKStrokeCap.Round,DesignLineCap.Square=>SKStrokeCap.Square,_=>SKStrokeCap.Butt };
    private static SKPaint Paint(StrokeStyle s,SKStrokeCap cap)=>new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=(float)s.Thickness,StrokeCap=cap,StrokeJoin=s.LineJoin switch { DesignLineJoin.Round=>SKStrokeJoin.Round,DesignLineJoin.Bevel=>SKStrokeJoin.Bevel,_=>SKStrokeJoin.Miter },StrokeMiter=(float)s.MiterLimit};
    public static SKPath Create(SKPath centerline,StrokeStyle style)
    {
        ArgumentNullException.ThrowIfNull(centerline);ArgumentNullException.ThrowIfNull(style);style.Validate();
        if(centerline.PointCount>MaxOutlinePoints)throw new InvalidDataException("Input path exceeds the native point budget.");
        if(style.Thickness==0)return new SKPath(); // A zero XAML stroke is absent, never a Skia hairline.
        using var paint=Paint(style,SKStrokeCap.Butt);
        if(style.Dashes.IsEmpty&&style.StartCap==style.EndCap&&style.StartCap!=DesignLineCap.Triangle)
        {
            paint.StrokeCap=Cap(style.StartCap);var fast=new SKPath();
            try { if(!paint.GetFillPath(centerline,fast))throw new InvalidOperationException("Unable to outline this stroke.");Check(fast);return fast; }
            catch { fast.Dispose();throw; }
        }
        using var body=new SKPath();using var caps=new SKPath();
        using var measure=new SKPathMeasure(centerline,false,2);
        var fragments=0;
        do
        {
            var length=measure.Length;if(!float.IsFinite(length))throw new InvalidDataException("Nonfinite stroke length.");
            if(length<=0)continue; // Degenerate zero-length contours have no stable tangent.
            var spans=style.GetDashes(length);
            if((fragments+=spans.Length)>StrokeStyle.MaxDashSpans)throw new InvalidDataException("Stroke exceeds the dash-fragment budget.");
            void AddCap(double at,DesignLineCap cap,bool start)
            {
                if(cap==DesignLineCap.Flat)return;
                if(!measure.GetPositionAndTangent((float)at,out var position,out var tangent))return;
                Stamp(caps,position,start ? new(-tangent.X,-tangent.Y) : tangent,cap,(float)style.Thickness/2);
            }
            void Fragment(double start,double end,bool seam=false,double afterSeam=0)
            {
                using var piece=new SKPath();
                if(end>start)measure.GetSegment((float)start,(float)end,piece,true);
                if(seam&&afterSeam>0)measure.GetSegment(0,(float)afterSeam,piece,piece.IsEmpty);
                var whole=measure.IsClosed&&start==0&&end==length;
                if(whole)piece.Close();
                if(!piece.IsEmpty)body.AddPath(piece);
                if(whole)return;
                AddCap(start,!measure.IsClosed&&start==0 ? style.StartCap : style.Dashes.IsEmpty ? style.StartCap : style.DashCap,true);
                var terminal=seam ? afterSeam : end;
                AddCap(terminal,!measure.IsClosed&&terminal==length ? style.EndCap : style.Dashes.IsEmpty ? style.EndCap : style.DashCap,false);
            }
            if(measure.IsClosed&&spans.Length>1&&spans[0].Start==0&&spans[^1].End==length)
            {
                Fragment(spans[^1].Start,length,true,spans[0].End);
                for(var i=1;i<spans.Length-1;i++)Fragment(spans[i].Start,spans[i].End);
            }
            else foreach(var span in spans)Fragment(span.Start,span.End);
        }while(measure.NextContour());
        var outline=new SKPath();
        try
        {
            if(!body.IsEmpty&&!paint.GetFillPath(body,outline))throw new InvalidOperationException("Unable to outline stroke segments.");
            if(!caps.IsEmpty)
            {
                var combined=outline.IsEmpty ? new SKPath(caps) : outline.Op(caps,SKPathOp.Union) ?? throw new InvalidOperationException("Unable to join stroke caps.");
                outline.Dispose();outline=combined;
            }
            Check(outline);return outline;
        }
        catch { outline.Dispose();throw; }
    }
    private static void Stamp(SKPath destination,SKPoint position,SKPoint tangent,DesignLineCap cap,float radius)
    {
        var length=Math.Sqrt((double)tangent.X*tangent.X+(double)tangent.Y*tangent.Y);if(length<=0)return;
        var x=(float)(tangent.X/length);var y=(float)(tangent.Y/length);
        using var shape=new SKPath();shape.MoveTo(0,-radius);
        switch(cap)
        {
            case DesignLineCap.Square:shape.LineTo(radius,-radius);shape.LineTo(radius,radius);shape.LineTo(0,radius);break;
            case DesignLineCap.Round:shape.ArcTo(SKRect.Create(-radius,-radius,radius*2,radius*2),-90,180,false);break;
            case DesignLineCap.Triangle:shape.LineTo(radius,0);shape.LineTo(0,radius);break;
            default:return;
        }
        shape.Close();shape.Transform(new SKMatrix{ScaleX=x,SkewX=-y,SkewY=y,ScaleY=x,TransX=position.X,TransY=position.Y,Persp2=1});destination.AddPath(shape);
        Check(destination);
    }
    private static void Check(SKPath p)
    {
        var b=p.Bounds;
        if(p.PointCount>MaxOutlinePoints||!float.IsFinite(b.Left)||!float.IsFinite(b.Top)||!float.IsFinite(b.Right)||!float.IsFinite(b.Bottom))throw new InvalidDataException("Stroke outline exceeds the native geometry budget.");
    }
}

/// <summary>UI-thread, bounded native stroke cache. Appearance and scene pan/zoom do not enter geometry keys.</summary>
public sealed class StrokeGeometryCache : IDisposable
{
    private readonly SkiaVectorCache _centerlines=new();
    private readonly ResourceLruCache<(VectorPath,DMatrix,StrokeStyle),SKPath> _outlines=new(128,16*1024*1024,p=>p.Dispose());
    public long Builds { get; private set; }
    public long Hits { get; private set; }
    public long EstimatedBytes=>_outlines.Cost;
    /// <summary>Borrowed path, valid until cache eviction or disposal; never mutate or dispose it.</summary>
    public SKPath Get(VectorPath geometry,DMatrix mapping,StrokeStyle style)
    {
        var key=(geometry,mapping,style);
        if(_outlines.TryGetValue(key,out var path)){Hits++;return path;}
        path=SkiaStrokeGeometry.Create(_centerlines.Get(geometry,mapping),style);
        try {_outlines.Add(key,path,256L+path.PointCount*32L);Builds++;return path;}
        catch{path.Dispose();throw;}
    }
    public void Dispose(){_outlines.Clear();_centerlines.Dispose();}
}

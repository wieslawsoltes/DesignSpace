using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Reusable fixed-screen-size anchor and tangent adorners drawn directly onto the host canvas.</summary>
public sealed class PathAdornerRenderer : IDisposable
{
    private readonly SkiaVectorCache _cache=new();
    private readonly SKPaint _line=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=1,Color=new SKColor(0,122,204)};
    private readonly SKPaint _fill=new(){IsAntialias=true,Color=SKColors.White};
    public void Draw(SKCanvas canvas,VectorPath path,DMatrix toWorld,DesignViewport viewport,PathHandle? selected=null,DPoint? hover=null,bool tangents=true)
    {
        var toScreen=DMatrix.Translate(viewport.PanX,viewport.PanY)*DMatrix.Scale(viewport.Zoom,viewport.Zoom)*toWorld;
        canvas.DrawPath(_cache.Get(path,toScreen),_line);
        if(hover is { } endpoint&&!path.Figures.IsEmpty)
        {
            var f=path.Figures[^1];var last=f.Segments.IsEmpty ? f.Start : f.Segments[^1].End;var a=toScreen.Map(last);var b=toScreen.Map(endpoint);
            canvas.DrawLine((float)a.X,(float)a.Y,(float)b.X,(float)b.Y,_line);
        }
        foreach(var (handle,point,anchor) in PathEditing.Handles(path))
        {
            var p=toScreen.Map(point);var a=toScreen.Map(anchor);var control=handle.Kind!=PathHandleKind.Anchor;
            if(control&&(!tangents||VectorMath.Length(p-a)<2))continue;
            if(control)canvas.DrawLine((float)a.X,(float)a.Y,(float)p.X,(float)p.Y,_line);
            var rect=SKRect.Create((float)p.X-3.5f,(float)p.Y-3.5f,7,7);if(canvas.QuickReject(rect))continue;
            _fill.Color=selected==handle ? new SKColor(0,122,204) : SKColors.White;
            if(control){canvas.DrawCircle((float)p.X,(float)p.Y,3,_fill);canvas.DrawCircle((float)p.X,(float)p.Y,3,_line);}
            else{canvas.DrawRect(rect,_fill);canvas.DrawRect(rect,_line);}
        }
    }
    public void Dispose(){_cache.Dispose();_fill.Dispose();_line.Dispose();}
}

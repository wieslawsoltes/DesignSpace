using System.Xml.Linq;
using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;
public sealed partial class DesignRenderer
{
    private readonly SkiaBrushCache _brushShaders=new();
    private BrushResolver? _brushResolver;
    public long BrushParseCount=>_brushShaders.Parses;
    public long BrushBuildCount=>_brushShaders.Builds;
    public long BrushCacheHits=>_brushShaders.Hits;
    public long BrushCacheBytes=>_brushShaders.EstimatedBytes;
    private SKShader? Brush(DesignNode n,string property,SKRect bounds)
    {
        var source=_brushResolver is null ? BrushResolver.LocalSource(n,property) : _brushResolver.Resolve(n.Id,property);
        if(string.IsNullOrWhiteSpace(source)||source.Trim()=="{x:Null}")return null;
        return _brushShaders.Get(source,new(bounds.Left,bounds.Top,bounds.Width,bounds.Height));
    }
    private void DrawRulers(SKCanvas c,double width,double height,DesignViewport view)
    {
        Fill(c,SKRect.Create(0,0,(float)width,22),new SKColor(37,37,38)); Fill(c,SKRect.Create(0,0,22,(float)height),new SKColor(37,37,38));
        var step=view.Zoom<.4 ? 200 : view.Zoom>1.5 ? 20 : 100; var start=(int)Math.Floor(-view.PanX/view.Zoom/step)*step;
        for(var x=start;x<(width-view.PanX)/view.Zoom;x+=step)
        {
            var sx=(float)(view.PanX+x*view.Zoom); if(sx<23) continue; Line(c,sx,17,sx,22,new SKColor(125,125,130)); _text.Draw(c,x.ToString(),sx+3,12,9,new SKColor(160,160,166));
        }
        start=(int)Math.Floor(-view.PanY/view.Zoom/step)*step;
        for(var y=start;y<(height-view.PanY)/view.Zoom;y+=step)
        {
            var sy=(float)(view.PanY+y*view.Zoom); if(sy<23) continue; Line(c,17,sy,22,sy,new SKColor(125,125,130)); c.Save(); c.RotateDegrees(-90,10,sy-3); _text.Draw(c,y.ToString(),10,sy-3,9,new SKColor(160,160,166)); c.Restore();
        }
        Fill(c,SKRect.Create(0,0,22,22),new SKColor(45,45,48));
    }
}

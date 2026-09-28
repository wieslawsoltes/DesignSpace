using System.Xml.Linq;
using DesignSpace.Core;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;
public sealed partial class DesignRenderer
{
    private static SKShader? Brush(DesignNode n,string property,SKRect b,IReadOnlyDictionary<string,XElement> resources)
    {
        XElement? brush=null; var value=n.Get(property);
        if(value.StartsWith("{StaticResource ",StringComparison.Ordinal) || value.StartsWith("{ThemeResource ",StringComparison.Ordinal)) resources.TryGetValue(value[(value.IndexOf(' ')+1)..].TrimEnd('}').Trim(),out brush);
        var raw=n.PropertyElements.FirstOrDefault(p=>p.Contains("."+property,StringComparison.Ordinal)); if(raw is not null) brush=XElement.Parse(raw).Elements().FirstOrDefault();
        if(brush is null) return null;
        var alpha=(byte)(Math.Clamp(Numbers.Parse((string?)brush.Attribute("Opacity"),1),0,1)*255);
        SKColor StopColor(string? text) { var c=Color(text,SKColors.Transparent); return c.WithAlpha((byte)(c.Alpha*alpha/255)); }
        if(brush.Name.LocalName=="SolidColorBrush") return SKShader.CreateColor(StopColor((string?)brush.Attribute("Color")));
        var stops=brush.Descendants().Where(e=>e.Name.LocalName=="GradientStop").OrderBy(e=>Numbers.Parse((string?)e.Attribute("Offset"))).ToArray(); if(stops.Length<2) return null;
        var colors=stops.Select(e=>StopColor((string?)e.Attribute("Color"))).ToArray(); var positions=stops.Select(e=>(float)Math.Clamp(Numbers.Parse((string?)e.Attribute("Offset")),0,1)).ToArray();
        var absolute=(string?)brush.Attribute("MappingMode")=="Absolute";
        SKPoint Point(string? text,float x,float y)
        {
            var parts=text?.Split(','); if(parts?.Length==2) { x=(float)Numbers.Parse(parts[0],x); y=(float)Numbers.Parse(parts[1],y); }
            return new(b.Left+(absolute ? x : b.Width*x),b.Top+(absolute ? y : b.Height*y));
        }
        var tile=(string?)brush.Attribute("SpreadMethod") switch { "Repeat"=>SKShaderTileMode.Repeat,"Reflect"=>SKShaderTileMode.Mirror,_=>SKShaderTileMode.Clamp };
        if(brush.Name.LocalName=="RadialGradientBrush") return SKShader.CreateRadialGradient(Point((string?)brush.Attribute("Center"),.5f,.5f),Math.Max(.001f,(absolute ? 1 : Math.Max(b.Width,b.Height))*(float)Numbers.Parse((string?)brush.Attribute("RadiusX"),.5)),colors,positions,tile);
        if(brush.Name.LocalName=="LinearGradientBrush") return SKShader.CreateLinearGradient(Point((string?)brush.Attribute("StartPoint"),0,0),Point((string?)brush.Attribute("EndPoint"),1,1),colors,positions,tile);
        return null;
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

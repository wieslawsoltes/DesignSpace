using System.Diagnostics;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

public sealed class DesignViewport
{
    public double Zoom { get; set; } = .8;
    public double PanX { get; set; } = 48;
    public double PanY { get; set; } = 60;
    public bool ShowGrid { get; set; }
    public bool ShowRulers { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public double GridSize { get; set; } = 8;
    public DPoint ScreenToWorld(DPoint p) => new((p.X-PanX)/Zoom,(p.Y-PanY)/Zoom);
    public DPoint WorldToScreen(DPoint p) => new(p.X*Zoom+PanX,p.Y*Zoom+PanY);
    public void ZoomAt(DPoint screen,double zoom) { var world=ScreenToWorld(screen); Zoom=Math.Clamp(zoom,.1,8); PanX=screen.X-world.X*Zoom; PanY=screen.Y-world.Y*Zoom; }
    public void Fit(double width,double height,DRect bounds)
    {
        Zoom=Math.Clamp(Math.Min(Math.Max(20,width-96)/Math.Max(1,bounds.Width),Math.Max(20,height-100)/Math.Max(1,bounds.Height)),.1,2);
        PanX=(width-bounds.Width*Zoom)/2-bounds.X*Zoom; PanY=(height-bounds.Height*Zoom)/2-bounds.Y*Zoom+10;
    }
}
/// <summary>Draws directly into a caller-owned SKCanvas. GPU/CPU backend is selected by the hosting surface, not this library.</summary>
public sealed class DesignRenderer : IDisposable,ITextMetrics
{
    private readonly SKPaint _fill=new() { IsAntialias=true };
    private readonly SKPaint _stroke=new() { IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=1 };
    private readonly SKTypeface _typeface=SKTypeface.Default;
    private readonly Dictionary<int,SKFont> _fonts=[];
    private readonly Dictionary<string,SKPath> _paths=[];
    public double LastDrawMilliseconds { get; private set; }
    public int LastDrawnNodes { get; private set; }
    public long DrawCount { get; private set; }
    private SKFont Font(double size) { var key=(int)Math.Round(size*64); if(!_fonts.TryGetValue(key,out var font)) { if(_fonts.Count>128) { foreach(var f in _fonts.Values) f.Dispose(); _fonts.Clear(); } font=new SKFont(_typeface,(float)size); _fonts[key]=font; } return font; }
    public DSize Measure(string text,double size,string family) { var font=Font(size); return new(font.MeasureText(text),size*1.4); }
    public static SKColor Color(string? text,SKColor fallback)
    {
        if(string.IsNullOrEmpty(text)) return fallback;
        if(text.Equals("Transparent",StringComparison.OrdinalIgnoreCase)) return SKColors.Transparent;
        if(text.StartsWith('#') && text.Length==9 && uint.TryParse(text.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out var argb)) return new((byte)(argb>>16),(byte)(argb>>8),(byte)argb,(byte)(argb>>24));
        return SKColor.TryParse(text,out var color) ? color : fallback;
    }
    private void Fill(SKCanvas canvas,SKRect rect,SKColor color,float radius=0) { _fill.Color=color; _fill.Shader=null; if(radius>0) canvas.DrawRoundRect(rect,radius,radius,_fill); else canvas.DrawRect(rect,_fill); }
    private void Line(SKCanvas canvas,float x1,float y1,float x2,float y2,SKColor color,float width=1) { _stroke.Color=color; _stroke.StrokeWidth=width; canvas.DrawLine(x1,y1,x2,y2,_stroke); }
    private void Text(SKCanvas canvas,string text,float x,float y,double size,SKColor color) { _fill.Shader=null; _fill.Color=color; canvas.DrawText(text,x,y,Font(size),_fill); }
    private static SKRect Rect(DRect b) => SKRect.Create((float)b.X,(float)b.Y,(float)b.Width,(float)b.Height);
    public void Draw(SKCanvas canvas,double width,double height,LayoutSnapshot layout,DesignViewport view,IReadOnlySet<Guid> selection,DRect? marquee=null,bool preview=false)
    {
        var start=Stopwatch.GetTimestamp(); DrawCount++; LastDrawnNodes=0;
        canvas.Clear(new SKColor(45,45,48));
        if(layout.Entries.Count==0) return;
        var art=layout.Entries[0].Bounds; var a=view.WorldToScreen(new(art.X,art.Y));
        Fill(canvas,SKRect.Create((float)a.X+5,(float)a.Y+6,(float)(art.Width*view.Zoom),(float)(art.Height*view.Zoom)),new SKColor(20,20,22,140));
        canvas.Save(); canvas.Translate((float)view.PanX,(float)view.PanY); canvas.Scale((float)view.Zoom);
        DrawScene(canvas,layout);
        if(view.ShowGrid && !preview)
        {
            canvas.Save(); canvas.ClipRect(Rect(art));
            var step=Math.Max(4,view.GridSize); if(step*view.Zoom<5) step*=4;
            for(var x=0d;x<art.Width;x+=step) for(var y=0d;y<art.Height;y+=step) { _fill.Color=new SKColor(70,110,145,75); canvas.DrawCircle((float)x,(float)y,(float)(.65/view.Zoom),_fill); }
            canvas.Restore();
        }
        if(!preview) foreach(var id in selection) if(layout.ById.TryGetValue(id,out var e))
        {
            var rect=Rect(e.Bounds); canvas.Save(); canvas.RotateDegrees((float)e.Node.Rotation,rect.MidX,rect.MidY);
            _stroke.Color=new SKColor(0,122,204); _stroke.StrokeWidth=(float)(1/view.Zoom); canvas.DrawRect(rect,_stroke);
            var radius=(float)(3/view.Zoom);
            foreach(var p in new[]{new SKPoint(rect.Left,rect.Top),new(rect.MidX,rect.Top),new(rect.Right,rect.Top),new(rect.Left,rect.MidY),new(rect.Right,rect.MidY),new(rect.Left,rect.Bottom),new(rect.MidX,rect.Bottom),new(rect.Right,rect.Bottom)})
            { var handle=SKRect.Create(p.X-radius,p.Y-radius,radius*2,radius*2); Fill(canvas,handle,SKColors.White); canvas.DrawRect(handle,_stroke); }
            canvas.Restore();
        }
        if(marquee is { } m) { Fill(canvas,Rect(m),new SKColor(0,122,204,35)); _stroke.Color=new SKColor(0,122,204); _stroke.StrokeWidth=(float)(1/view.Zoom); canvas.DrawRect(Rect(m),_stroke); }
        canvas.Restore();
        if(view.ShowRulers) DrawRulers(canvas,width,height,view);
        LastDrawMilliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    public void DrawScene(SKCanvas canvas,LayoutSnapshot layout)
    {
        var root=layout.Entries[0]; var resources=new Dictionary<string,XElement>(StringComparer.Ordinal);
        foreach(var raw in root.Node.PropertyElements.Where(p=>p.Contains(".Resources",StringComparison.Ordinal)))
        { try { foreach(var e in XElement.Parse(raw).Descendants()) { var key=e.Attributes().FirstOrDefault(a=>a.Name.LocalName=="Key")?.Value; if(key is not null) resources[key]=e; } } catch(System.Xml.XmlException) { } }
        var children=layout.Entries.Where(e=>e.ParentId is not null).GroupBy(e=>e.ParentId!.Value).ToDictionary(g=>g.Key,g=>g.ToArray());
        void Walk(LayoutEntry entry)
        {
            canvas.Save(); var b=Rect(entry.Bounds);
            if(entry.Node.Rotation!=0) canvas.RotateDegrees((float)entry.Node.Rotation,b.MidX,b.MidY);
            DrawNode(canvas,entry,resources); LastDrawnNodes++;
            if(entry.Node.Get("ClipToBounds")=="True" || entry.Node.Type is "Page" or "UserControl" or "Window") canvas.ClipRect(b);
            if(children.TryGetValue(entry.Node.Id,out var items)) foreach(var item in items) Walk(item);
            canvas.Restore();
        }
        canvas.Save(); canvas.ClipRect(Rect(root.Bounds)); Walk(root); canvas.Restore();
    }
    private void DrawNode(SKCanvas canvas,LayoutEntry entry,IReadOnlyDictionary<string,XElement> resources)
    {
        var n=entry.Node; var b=Rect(entry.Bounds); if(b.Width<=0 || b.Height<=0) return;
        var alpha=(byte)Math.Clamp(entry.Opacity*255,0,255); var type=n.Type;
        var property=type is "Rectangle" or "Ellipse" or "Path" ? "Fill" : "Background";
        var defaultColor=type is "Page" or "Window" or "UserControl" ? SKColors.White : SKColors.Transparent;
        var fill=Color(n.Get(property),defaultColor); fill=fill.WithAlpha((byte)(fill.Alpha*alpha/255));
        using var shader=Brush(n,property,b,resources);
        _fill.Color=shader is null ? fill : SKColors.White.WithAlpha(alpha); _fill.Shader=shader;
        var radius=(float)Numbers.Parse(n.Get("CornerRadius",n.Get("RadiusX")));
        var strokeColor=Color(n.Get("Stroke",n.Get("BorderBrush")),SKColors.Transparent); _stroke.Color=strokeColor.WithAlpha((byte)(strokeColor.Alpha*alpha/255)); _stroke.StrokeWidth=(float)n.Number("StrokeThickness",n.Number("BorderThickness",1));
        if(type=="Ellipse") { canvas.DrawOval(b,_fill); if(_stroke.Color.Alpha>0) canvas.DrawOval(b,_stroke); }
        else if(type=="Line") Line(canvas,b.Left,b.Top,b.Right,b.Bottom,_stroke.Color,(float)n.Number("StrokeThickness",2));
        else if(type=="Path")
        {
            var data=n.Get("Data","M 0 100 L 50 0 L 100 100 Z");
            if(!_paths.TryGetValue(data,out var path))
            { if(_paths.Count>256) { foreach(var p in _paths.Values) p.Dispose(); _paths.Clear(); } path=SKPath.ParseSvgPathData(data); if(path is not null) _paths[data]=path; }
            if(path is not null)
            {
                canvas.Save(); canvas.Translate(b.Left,b.Top); var pb=path.Bounds;
                if(n.Get("Stretch","Fill")!="None" && pb.Width>0 && pb.Height>0) { canvas.Scale(b.Width/pb.Width,b.Height/pb.Height); canvas.Translate(-pb.Left,-pb.Top); }
                canvas.DrawPath(path,_fill); if(_stroke.Color.Alpha>0) canvas.DrawPath(path,_stroke); canvas.Restore();
            }
        }
        else { if(fill.Alpha>0 || shader is not null) canvas.DrawRoundRect(b,radius,radius,_fill); if(_stroke.Color.Alpha>0) canvas.DrawRoundRect(b,radius,radius,_stroke); }
        _fill.Shader=null;
        if(type is "TextBlock" or "Button" or "TextBox" or "CheckBox" or "RadioButton" or "ToggleSwitch")
        {
            var text=n.Get("Text",n.Get("Content",n.TextContent)); var size=n.Number("FontSize",type=="TextBlock" ? 14 : 13);
            var color=Color(n.Get("Foreground"),new SKColor(32,40,56)).WithAlpha(alpha); var font=Font(size);
            var x=b.Left; var y=b.Top+(float)size;
            if(type is "Button" or "TextBox") { x=type=="Button" ? b.MidX-font.MeasureText(text)/2 : b.Left+8; y=b.MidY-font.Metrics.Ascent/2-font.Metrics.Descent/2; }
            if(type is "CheckBox" or "RadioButton" or "ToggleSwitch")
            {
                var check=SKRect.Create(b.Left,b.MidY-8,16,16); Fill(canvas,check,n.Get("IsChecked")=="True" ? new SKColor(0,120,212) : SKColors.White); _stroke.Color=new SKColor(120,120,120); _stroke.StrokeWidth=1; canvas.DrawRect(check,_stroke); x+=24; y=b.MidY+5;
            }
            canvas.Save(); canvas.ClipRect(b); Text(canvas,text,x,y,size,color); canvas.Restore();
        }
        if(type is "Slider" or "ProgressBar")
        {
            var value=Math.Clamp(n.Number("Value",35)/Math.Max(1,n.Number("Maximum",100)),0,1);
            var track=SKRect.Create(b.Left,b.MidY-2,b.Width,4); Fill(canvas,track,new SKColor(205,211,218)); Fill(canvas,SKRect.Create(b.Left,b.MidY-2,(float)(b.Width*value),4),new SKColor(0,120,212));
            if(type=="Slider") { _fill.Color=new SKColor(0,120,212); canvas.DrawCircle(b.Left+(float)(b.Width*value),b.MidY,7,_fill); }
        }
        if(type=="Image") { _stroke.Color=new SKColor(150,150,150); _stroke.StrokeWidth=1; canvas.DrawRect(b,_stroke); canvas.DrawLine(b.Left,b.Top,b.Right,b.Bottom,_stroke); Text(canvas,"Image source preserved",b.Left+8,b.MidY,12,new SKColor(120,120,120)); }
    }
    private static SKShader? Brush(DesignNode n,string property,SKRect b,IReadOnlyDictionary<string,XElement> resources)
    {
        XElement? brush=null; var value=n.Get(property);
        if(value.StartsWith("{StaticResource ",StringComparison.Ordinal) || value.StartsWith("{ThemeResource ",StringComparison.Ordinal)) { var key=value[(value.IndexOf(' ')+1)..].TrimEnd('}').Trim(); resources.TryGetValue(key,out brush); }
        var raw=n.PropertyElements.FirstOrDefault(p=>p.Contains("."+property,StringComparison.Ordinal));
        if(raw is not null) { try { brush=XElement.Parse(raw).Elements().FirstOrDefault(); } catch(System.Xml.XmlException) { } }
        if(brush is null) return null;
        if(brush.Name.LocalName=="SolidColorBrush") return SKShader.CreateColor(Color((string?)brush.Attribute("Color"),SKColors.Transparent));
        var stops=brush.Descendants().Where(e=>e.Name.LocalName=="GradientStop").OrderBy(e=>Numbers.Parse((string?)e.Attribute("Offset"))).ToArray();
        if(stops.Length<2) return null;
        var colors=stops.Select(e=>Color((string?)e.Attribute("Color"),SKColors.Transparent)).ToArray(); var positions=stops.Select(e=>(float)Math.Clamp(Numbers.Parse((string?)e.Attribute("Offset")),0,1)).ToArray();
        if(brush.Name.LocalName=="RadialGradientBrush") return SKShader.CreateRadialGradient(new(b.MidX,b.MidY),Math.Max(b.Width,b.Height)/2,colors,positions,SKShaderTileMode.Clamp);
        if(brush.Name.LocalName=="LinearGradientBrush") return SKShader.CreateLinearGradient(new(b.Left,b.Top),new(b.Right,b.Bottom),colors,positions,SKShaderTileMode.Clamp);
        return null;
    }
    private void DrawRulers(SKCanvas canvas,double width,double height,DesignViewport view)
    {
        Fill(canvas,SKRect.Create(0,0,(float)width,22),new SKColor(37,37,38)); Fill(canvas,SKRect.Create(0,0,22,(float)height),new SKColor(37,37,38));
        var step=view.Zoom<.4 ? 200 : view.Zoom>1.5 ? 20 : 100;
        var start=(int)Math.Floor(-view.PanX/view.Zoom/step)*step;
        for(var x=start;x<(width-view.PanX)/view.Zoom;x+=step)
        { var sx=(float)(view.PanX+x*view.Zoom); if(sx<23) continue; Line(canvas,sx,17,sx,22,new SKColor(125,125,130)); Text(canvas,x.ToString(),sx+3,12,9,new SKColor(160,160,166)); }
        start=(int)Math.Floor(-view.PanY/view.Zoom/step)*step;
        for(var y=start;y<(height-view.PanY)/view.Zoom;y+=step)
        { var sy=(float)(view.PanY+y*view.Zoom); if(sy<23) continue; Line(canvas,17,sy,22,sy,new SKColor(125,125,130)); canvas.Save(); canvas.RotateDegrees(-90,10,sy-3); Text(canvas,y.ToString(),10,sy-3,9,new SKColor(160,160,166)); canvas.Restore(); }
        Fill(canvas,SKRect.Create(0,0,22,22),new SKColor(45,45,48));
    }
    public byte[] ExportPng(LayoutSnapshot layout,int scale=2)
    {
        var bounds=layout.Entries[0].Bounds; var width=(int)Math.Ceiling(bounds.Width*scale); var height=(int)Math.Ceiling(bounds.Height*scale);
        if(width<=0 || height<=0 || (long)width*height>32_000_000) throw new InvalidOperationException("PNG dimensions exceed the 32 megapixel export limit.");
        using var surface=SKSurface.Create(new SKImageInfo(width,height)); if(surface is null) throw new InvalidOperationException("Unable to allocate the export surface.");
        surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale(scale); DrawScene(surface.Canvas,layout); using var image=surface.Snapshot(); using var data=image.Encode(SKEncodedImageFormat.Png,100); return data.ToArray();
    }
    public void Dispose() { _fill.Dispose(); _stroke.Dispose(); foreach(var font in _fonts.Values) font.Dispose(); foreach(var path in _paths.Values) path.Dispose(); _fonts.Clear(); _paths.Clear(); }
}

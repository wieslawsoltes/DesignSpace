using System.Diagnostics;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Shared drawing directly into host-owned Skia canvases, including transforms, shaped text and images.</summary>
public sealed partial class DesignRenderer : IDisposable,IConstrainedTextMetrics
{
    private readonly SKPaint _fill=new(){IsAntialias=true};
    private readonly SKPaint _stroke=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=1};
    private readonly SkiaVectorCache _paths=new();
    public long PathBuildCount=>_paths.Builds;
    public long PathCacheHits=>_paths.Hits;
    private readonly SkiaTextService _text=new();
    private readonly EmbeddedImages _images=new();
    private readonly EffectFilterCache _effects=new();
    /// <summary>Explicit software-reference numerics; Native remains the normal fast rendering path.</summary>
    public EffectRenderingMode EffectMode { get; set; }
    public long EffectBuildCount=>_effects.Builds;
    public long EffectCacheHits=>_effects.Hits;
    private LayoutSnapshot? _indexed;
    private Dictionary<Guid,LayoutEntry[]> _children=[];
    private readonly List<string> _diagnostics=[];
    public IReadOnlyList<string> Diagnostics=>_diagnostics;
    public double LastDrawMilliseconds { get; private set; }
    public int LastDrawnNodes { get; private set; }
    public long DrawCount { get; private set; }
    public long TextShapeCount=>_text.ShapeCount;
    public long TextCacheHits=>_text.CacheHits;
    public long ImageDecodeCount=>_images.DecodeCount;
    public DSize Measure(string text,double size,string family)=>_text.Measure(text,size,family);
    public DSize Measure(string text,double size,string family,double width,bool wrap)=>_text.Measure(text,size,family,width,wrap);
    public static SKColor Color(string? text,SKColor fallback)=>DesignColors.Parse(text,fallback);
    private static SKRect Rect(DRect b)=>SKRect.Create((float)b.X,(float)b.Y,(float)b.Width,(float)b.Height);
    private static void Concat(SKCanvas c,DMatrix m)
    {
        var matrix=new SKMatrix { ScaleX=(float)m.M11,SkewY=(float)m.M12,SkewX=(float)m.M21,ScaleY=(float)m.M22,TransX=(float)m.DX,TransY=(float)m.DY,Persp2=1 };
        c.Concat(in matrix);
    }
    private void Fill(SKCanvas c,SKRect b,SKColor color) { _fill.Shader=null; _fill.Color=color; c.DrawRect(b,_fill); }
    private void Line(SKCanvas c,double x,double y,double a,double b,SKColor color,double width=1) { _stroke.Color=color; _stroke.StrokeWidth=(float)width; c.DrawLine((float)x,(float)y,(float)a,(float)b,_stroke); }
    public void Draw(SKCanvas c,double width,double height,LayoutSnapshot layout,DesignViewport view,IReadOnlySet<Guid> selection,DRect? marquee=null,bool preview=false)
    {
        var start=Stopwatch.GetTimestamp(); DrawCount++; LastDrawnNodes=0; c.Clear(new SKColor(45,45,48));
        if(layout.Entries.Count==0)return;
        var art=layout.Entries[0].Bounds;var a=view.WorldToScreen(new(art.X,art.Y));
        Fill(c,SKRect.Create((float)a.X+5,(float)a.Y+6,(float)(art.Width*view.Zoom),(float)(art.Height*view.Zoom)),new SKColor(20,20,22,140));
        c.Save();c.Translate((float)view.PanX,(float)view.PanY);c.Scale((float)view.Zoom);DrawScene(c,layout,view.RenderEffects && view.Zoom<=view.EffectsZoomThreshold);
        if(view.ShowGrid&&!preview)
        {
            c.Save();c.ClipRect(Rect(art));var step=Math.Max(4,view.GridSize);if(step*view.Zoom<5)step*=4;
            var topLeft=view.ScreenToWorld(new(0,0));var bottomRight=view.ScreenToWorld(new(width,height));
            for(var x=Math.Max(0,Math.Floor(topLeft.X/step)*step);x<Math.Min(art.Right,bottomRight.X);x+=step)
                for(var y=Math.Max(0,Math.Floor(topLeft.Y/step)*step);y<Math.Min(art.Bottom,bottomRight.Y);y+=step){_fill.Shader=null;_fill.Color=new SKColor(70,110,145,75);c.DrawCircle((float)x,(float)y,(float)(.65/view.Zoom),_fill);}
            c.Restore();
        }
        if(!preview)foreach(var id in selection)if(layout.ById.TryGetValue(id,out var e))
        {
            var b=e.Bounds;var corners=new[]{new DPoint(b.X,b.Y),new(b.Right,b.Y),new(b.Right,b.Bottom),new(b.X,b.Bottom)}.Select(e.WorldTransform.Map).ToArray();
            for(var i=0;i<4;i++)Line(c,corners[i].X,corners[i].Y,corners[(i+1)%4].X,corners[(i+1)%4].Y,new SKColor(0,122,204),1/view.Zoom);
            var radius=(float)(3/view.Zoom);
            foreach(var point in new[]{new DPoint(b.X,b.Y),new(b.Center.X,b.Y),new(b.Right,b.Y),new(b.X,b.Center.Y),new(b.Right,b.Center.Y),new(b.X,b.Bottom),new(b.Center.X,b.Bottom),new(b.Right,b.Bottom)})
            {
                var p=e.WorldTransform.Map(point);var handle=SKRect.Create((float)p.X-radius,(float)p.Y-radius,radius*2,radius*2);Fill(c,handle,SKColors.White);_stroke.Color=new SKColor(0,122,204);_stroke.StrokeWidth=(float)(1/view.Zoom);c.DrawRect(handle,_stroke);
            }
        }
        if(marquee is { } box){Fill(c,Rect(box),new SKColor(0,122,204,35));_stroke.Color=new SKColor(0,122,204);_stroke.StrokeWidth=(float)(1/view.Zoom);c.DrawRect(Rect(box),_stroke);}
        c.Restore();if(view.ShowRulers)DrawRulers(c,width,height,view);LastDrawMilliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    private void Index(LayoutSnapshot layout)
    {
        if(ReferenceEquals(_indexed,layout))return;_indexed=layout;
        _brushResolver=new BrushResolver(layout.Entries[0].Node);
        _children=layout.Entries.Where(e=>e.ParentId is not null).GroupBy(e=>e.ParentId!.Value).ToDictionary(g=>g.Key,g=>g.ToArray());
    }
    public void DrawScene(SKCanvas c,LayoutSnapshot layout,bool renderEffects=true)
    {
        _diagnostics.Clear();if(layout.Entries.Count==0)return;Index(layout);
        void Walk(LayoutEntry entry)
        {
            if(!entry.IsEffectivelyVisible||entry.Opacity<=0)return;
            var outerCount=c.SaveCount;c.Save();Concat(c,entry.LocalTransform);var b=Rect(entry.Bounds);var n=entry.Node;
            SKImageFilter? effect=null;
            try
            {
                if(renderEffects&&_brushResolver?.Resolve(n.Id,"Effect") is { } source)effect=_effects.GetForCanvas(source,c,EffectMode);
            }
            catch(Exception ex)when(ex is InvalidDataException or InvalidOperationException or ArgumentException)
            {if(_diagnostics.Count<100)_diagnostics.Add(n.Name+": "+ex.Message);}
            var opacity=Math.Clamp(n.Number("Opacity",1),0,1);
            // The layer paint retains the native filter while child drawing may evict its cache entry.
            using var layer=opacity<1||effect is not null ? new SKPaint{Color=SKColors.White.WithAlpha((byte)(opacity*255)),ImageFilter=effect} : null;
            SKPaint? mask=null;
            try
            {
                if(StrokeStyle.HasBrush(n,"OpacityMask"))
                {
                    var shader=Brush(n,"OpacityMask",b);
                    if(shader is not null)mask=new SKPaint{Shader=shader,BlendMode=SKBlendMode.DstIn,Color=SKColors.White};
                }
            }
            catch(Exception ex)when(ex is InvalidDataException or InvalidOperationException or ArgumentException)
            {if(_diagnostics.Count<100)_diagnostics.Add(n.Name+": "+ex.Message);}
            using var maskPaint=mask;
            if(layer is not null||mask is not null)c.SaveLayer(layer);

            try
            {
                if(VectorGeometry.ReadClip(n) is { } clip)c.ClipPath(_paths.Get(clip,DMatrix.Translate(b.Left,b.Top)),SKClipOperation.Intersect,true);
            }
            catch(InvalidDataException ex){if(_diagnostics.Count<100)_diagnostics.Add(n.Name+": "+ex.Message);c.RestoreToCount(outerCount);return;}
            if(VectorGeometry.IsShape(n) || !c.QuickReject(b))
            {
                var nodeCount=c.SaveCount;
                try{DrawNode(c,entry);LastDrawnNodes++;}
                catch(Exception ex)when(ex is InvalidOperationException or ArgumentException or InvalidDataException)
                {
                    _fill.Shader=null;c.RestoreToCount(nodeCount);if(_diagnostics.Count<100)_diagnostics.Add(n.Name+": "+ex.Message);_stroke.Color=SKColors.OrangeRed;_stroke.StrokeWidth=(float)(1);c.DrawRect(b,_stroke);
                }
            }
            if(n.Get("ClipToBounds")=="True"||n.Type is "Page" or "UserControl" or "Window")c.ClipRect(b);
            if(_children.TryGetValue(n.Id,out var children))foreach(var child in children)Walk(child);
            if(mask is not null)c.DrawPaint(mask);
            c.RestoreToCount(outerCount);
        }
        c.Save();c.ClipRect(Rect(layout.Entries[0].Bounds));Walk(layout.Entries[0]);c.Restore();
    }
    private void DrawNode(SKCanvas c,LayoutEntry entry)
    {
        var n=entry.Node;var b=Rect(entry.Bounds);
        if(VectorGeometry.IsShape(n)){DrawShape(c,entry);return;}
        if(b.Width<=0||b.Height<=0)return;
        if(n.Get("{https://designspace.dev/designer}TemplateExpanded")=="True")return;
        var fill=Color(n.Get("Background"),n.Type is "Page" or "Window" or "UserControl" ? SKColors.White : SKColors.Transparent);
        var shader=Brush(n,"Background",b);_fill.Color=shader is null ? fill : SKColors.White;_fill.Shader=shader;
        _stroke.Color=Color(n.Get("BorderBrush"),SKColors.Transparent);_stroke.StrokeWidth=(float)n.Number("BorderThickness",1);
        var radius=(float)Numbers.Parse(n.Get("CornerRadius"));
        if(fill.Alpha>0||shader is not null)c.DrawRoundRect(b,radius,radius,_fill);
        var border=Brush(n,"BorderBrush",b);
        if((_stroke.Color.Alpha>0||border is not null)&&_stroke.StrokeWidth>0)
        {
            _stroke.Shader=border;if(border is not null)_stroke.Color=SKColors.White;
            try{c.DrawRoundRect(b,radius,radius,_stroke);}finally{_stroke.Shader=null;}
        }
        _fill.Shader=null;
        if(n.Type is "TextBlock" or "Button" or "TextBox" or "ContentPresenter")
        {
            var textBounds=b;if(n.Type=="TextBox")textBounds.Inflate(-8,0);_text.DrawNode(c,n,textBounds,Color(n.Get("Foreground"),new SKColor(32,40,56)),n.Type is "Button" or "TextBox",Brush(n,"Foreground",b));
        }
        if(n.Type is "CheckBox" or "RadioButton" or "ToggleSwitch")
        {
            var box=SKRect.Create(b.Left,b.MidY-8,16,16);_fill.Color=n.Get("IsChecked")=="True" ? new SKColor(0,120,212) : SKColors.White;
            if(n.Type=="RadioButton")c.DrawOval(box,_fill);else c.DrawRect(box,_fill);
            _stroke.Color=new SKColor(120,120,120);_stroke.StrokeWidth=1;if(n.Type=="RadioButton")c.DrawOval(box,_stroke);else c.DrawRect(box,_stroke);
            if(n.Get("IsChecked")=="True"){Line(c,b.Left+4,b.MidY,b.Left+7,b.MidY+3,SKColors.White,1.5);Line(c,b.Left+7,b.MidY+3,b.Left+13,b.MidY-4,SKColors.White,1.5);}
            _text.DrawNode(c,n,SKRect.Create(b.Left+24,b.Top,Math.Max(0,b.Width-24),b.Height),Color(n.Get("Foreground"),new SKColor(32,40,56)),false,Brush(n,"Foreground",b));
        }
        if(n.Type is "Slider" or "ProgressBar")
        {
            var min=n.Number("Minimum");var max=Math.Max(min+1,n.Number("Maximum",100));var value=Math.Clamp((n.Number("Value",35)-min)/(max-min),0,1);
            Fill(c,SKRect.Create(b.Left,b.MidY-2,b.Width,4),new SKColor(205,211,218));Fill(c,SKRect.Create(b.Left,b.MidY-2,(float)(b.Width*value),4),new SKColor(0,120,212));
            if(n.Type=="Slider"){_fill.Color=new SKColor(0,120,212);c.DrawCircle(b.Left+(float)(b.Width*value),b.MidY,7,_fill);}
        }
        if(n.Type=="Image")
        {
            var image=_images.Get(n.Get("Source"),out var error);
            if(image is not null)_images.Draw(c,image,b,n.Get("Stretch","Uniform"));
            else{if(_diagnostics.Count<100)_diagnostics.Add(n.Name+": "+error);_stroke.Color=new SKColor(150,150,150);c.DrawRect(b,_stroke);_text.Draw(c,"Import an image",b.Left+8,b.MidY,12,new SKColor(120,120,120));}
        }
    }
    public byte[] ExportPng(LayoutSnapshot layout,int scale=2)
    {
        if(layout.Entries.Count==0||scale<1||scale>8)throw new InvalidOperationException("Invalid export scale or empty layout.");
        var bounds=layout.Entries[0].Bounds;var w=Math.Ceiling(bounds.Width*scale);var h=Math.Ceiling(bounds.Height*scale);
        if(!double.IsFinite(w)||!double.IsFinite(h)||w<=0||h<=0||w*h>32_000_000)throw new InvalidOperationException("PNG dimensions exceed 32 megapixels.");
        using var surface=SKSurface.Create(new SKImageInfo((int)w,(int)h));if(surface is null)throw new InvalidOperationException("Unable to allocate export surface.");
        surface.Canvas.Clear(SKColors.Transparent);surface.Canvas.Scale(scale);DrawScene(surface.Canvas,layout);using var image=surface.Snapshot();using var data=image.Encode(SKEncodedImageFormat.Png,100);return data.ToArray();
    }
    public void Dispose(){_fill.Dispose();_stroke.Dispose();_paths.Dispose();_strokeShapes.Dispose();_shapePaint.Dispose();_shapeSources.Clear();_basicShapes.Clear();_text.Dispose();_images.Dispose();_effects.Dispose();_children.Clear();_brushShaders.Dispose();_brushResolver=null;_indexed=null;}
}

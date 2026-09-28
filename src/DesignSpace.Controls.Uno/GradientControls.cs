using DesignSpace.Core;
using DesignSpace.Rendering.Skia;
using Microsoft.UI.Xaml.Automation;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable GPU-hosted brush preview and direct gradient-stop strip. No document mutation is performed here.</summary>
public sealed class GradientPreviewControl : SKCanvasElement,IDisposable
{
    private readonly SkiaBrushCache _cache=new();
    private DesignBrush _brush=new();
    private string _source="";
    private bool _dragging;
    private int _selected=-1;
    public bool IsStopStrip { get; init; }
    public event EventHandler<string>? Error;
    public event EventHandler<int>? StopSelected;
    public event EventHandler<(int Index,double Offset)>? StopMoved;
    public DesignBrush Brush
    {
        get=>_brush;
        set{var source=BrushCodec.Write(value);_brush=value;if(source!=_source){_source=source;Invalidate();}}
    }
    public int SelectedStop {get=>_selected;set{_selected=value;Invalidate();}}
    public GradientPreviewControl()
    {
        Height=92;
        PointerPressed+=(_,e)=>
        {
            if(!IsStopStrip)return;var p=e.GetCurrentPoint(this).Position;var width=Math.Max(1,ActualWidth-16);var distance=10d;var index=-1;
            for(var i=0;i<_brush.Stops.Length;i++){var d=Math.Abs(8+Math.Clamp(_brush.Stops[i].Offset,0,1)*width-p.X);if(d<=distance){distance=d;index=i;}}
            if(index<0)return;_selected=index;StopSelected?.Invoke(this,index);_dragging=true;CapturePointer(e.Pointer);e.Handled=true;Invalidate();
        };
        PointerMoved+=(_,e)=>
        {
            if(!_dragging||_selected<0)return;var p=e.GetCurrentPoint(this).Position;StopMoved?.Invoke(this,(_selected,Math.Clamp((p.X-8)/Math.Max(1,ActualWidth-16),0,1)));e.Handled=true;
        };
        PointerReleased+=(_,e)=>{if(!_dragging)return;_dragging=false;ReleasePointerCapture(e.Pointer);e.Handled=true;};
        PointerCaptureLost+=(_,_)=>_dragging=false;
    }
    protected override void RenderOverride(SKCanvas c,Size area)
    {
        var rect=SKRect.Create(8,5,(float)Math.Max(1,area.Width-16),(float)(IsStopStrip?22:Math.Max(1,area.Height-10)));
        using var paint=new SKPaint{IsAntialias=true};
        c.Save();c.ClipRect(rect);
        for(var y=rect.Top;y<rect.Bottom;y+=8)for(var x=rect.Left;x<rect.Right;x+=8){paint.Color=((int)((x-rect.Left)/8)+(int)((y-rect.Top)/8))%2==0?new SKColor(77,77,81):new SKColor(111,111,116);c.DrawRect(x,y,8,8,paint);}
        var brush=IsStopStrip?_brush with{Kind=DesignBrushKind.Linear,Opacity=1,Start=new(0,0),End=new(1,0),Mapping=DesignBrushMapping.RelativeToBoundingBox,Spread=DesignGradientSpread.Pad,Transform=DMatrix.Identity,RelativeTransform=DMatrix.Identity}:_brush;
        try{paint.Color=SKColors.White;paint.Shader=_cache.Get(BrushCodec.Write(brush),new(rect.Left,rect.Top,rect.Width,rect.Height));c.DrawRect(rect,paint);}
        catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){Error?.Invoke(this,e.Message);}
        finally{paint.Shader=null;c.Restore();}
        if(!IsStopStrip)return;
        for(var i=0;i<_brush.Stops.Length;i++)
        {
            var stop=_brush.Stops[i];var x=rect.Left+(float)Math.Clamp(stop.Offset,0,1)*rect.Width;
            using var marker=new SKPath();marker.MoveTo(x,28);marker.LineTo(x+5,34);marker.LineTo(x+5,43);marker.LineTo(x-5,43);marker.LineTo(x-5,34);marker.Close();
            paint.Style=SKPaintStyle.Fill;paint.Color=DesignColors.Parse(stop.Color,SKColors.Transparent);c.DrawPath(marker,paint);
            paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=i==_selected?2:1;paint.Color=i==_selected?SKColors.White:new SKColor(165,165,170);c.DrawPath(marker,paint);paint.Style=SKPaintStyle.Fill;
        }
    }
    public void Dispose()=>_cache.Dispose();
}

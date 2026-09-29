using DesignSpace.Core;
using DesignSpace.Rendering.Skia;
using Microsoft.UI.Xaml.Automation;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace DesignSpace.Controls.Uno;

/// <summary>Reusable fixed-endpoint cubic easing editor. Pointer changes edit a draft, not a document.</summary>
public sealed class KeySplineEditorControl : SKCanvasElement
{
    private KeySpline _curve=KeySpline.Linear;
    private KeySpline? _beforeDrag;
    private int _handle;
    public event EventHandler<KeySpline>? CurveChanged;
    public KeySpline Curve { get=>_curve;set{value.Validate();_curve=value;Invalidate();} }
    public KeySplineEditorControl()
    {
        Height=170;AutomationProperties.SetName(this,"Key spline curve");
        PointerPressed+=(_,e)=>
        {
            var p=e.GetCurrentPoint(this).Position;var a=Point(_curve.X1,_curve.Y1);var b=Point(_curve.X2,_curve.Y2);
            double Distance(SKPoint q)=>Math.Pow(p.X-q.X,2)+Math.Pow(p.Y-q.Y,2);
            var da=Distance(a);var db=Distance(b);if(Math.Min(da,db)>196)return;
            _handle=da<=db?1:2;_beforeDrag=_curve;CapturePointer(e.Pointer);e.Handled=true;
        };
        PointerMoved+=(_,e)=>
        {
            if(_handle==0)return;var p=e.GetCurrentPoint(this).Position;
            var x=Math.Clamp((p.X-20)/Math.Max(1,ActualWidth-40),0,1);var y=Math.Clamp(1-(p.Y-20)/Math.Max(1,ActualHeight-40),0,1);
            Curve=_handle==1?_curve with{X1=x,Y1=y}:_curve with{X2=x,Y2=y};CurveChanged?.Invoke(this,_curve);e.Handled=true;
        };
        PointerReleased+=(_,e)=>{if(_handle==0)return;_handle=0;_beforeDrag=null;ReleasePointerCapture(e.Pointer);e.Handled=true;};
        PointerCaptureLost+=(_,_)=>CancelDrag();
    }
    public void CancelDrag()
    {
        if(_beforeDrag is { } previous){Curve=previous;CurveChanged?.Invoke(this,previous);}_handle=0;_beforeDrag=null;
    }
    private SKPoint Point(double x,double y)=>new((float)(20+x*Math.Max(1,ActualWidth-40)),(float)(20+(1-y)*Math.Max(1,ActualHeight-40)));
    protected override void RenderOverride(SKCanvas canvas,Size area)
    {
        canvas.Clear(new SKColor(30,30,33));using var paint=new SKPaint{IsAntialias=true,StrokeWidth=1,Style=SKPaintStyle.Stroke};
        for(var i=0;i<=4;i++){paint.Color=new SKColor(59,59,65);canvas.DrawLine(Point(i/4d,0),Point(i/4d,1),paint);canvas.DrawLine(Point(0,i/4d),Point(1,i/4d),paint);}
        var a=Point(0,0);var b=Point(1,1);var p=Point(_curve.X1,_curve.Y1);var q=Point(_curve.X2,_curve.Y2);
        paint.Color=new SKColor(155,155,165);canvas.DrawLine(a,p,paint);canvas.DrawLine(b,q,paint);
        using var path=new SKPath();path.MoveTo(a);path.CubicTo(p,q,b);paint.Color=new SKColor(64,174,241);paint.StrokeWidth=2;canvas.DrawPath(path,paint);
        paint.Style=SKPaintStyle.Fill;paint.Color=new SKColor(236,178,79);canvas.DrawCircle(p,5,paint);canvas.DrawCircle(q,5,paint);
        paint.Color=SKColors.White;canvas.DrawCircle(Point(.5,_curve.Evaluate(.5)),3,paint);
        using var font=new SKFont(DesignTypography.DefaultTypeface,10);paint.Color=new SKColor(179,179,188);
        canvas.DrawText("0",6,(float)area.Height-13,font,paint);canvas.DrawText("1",(float)area.Width-13,15,font,paint);
    }
}

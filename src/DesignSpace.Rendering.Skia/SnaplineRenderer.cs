using DesignSpace.Core;
using DesignSpace.Engine;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>Host-canvas snap adorners with screen-sized lines, end caps and distance labels.</summary>
public sealed class SnaplineRenderer : IDisposable
{
    private readonly SKPaint _line=new(){Style=SKPaintStyle.Stroke,StrokeWidth=1,IsAntialias=false};
    private readonly SKPaint _background=new(){Color=new SKColor(38,38,42)};
    private readonly SKPaint _text=new(){IsAntialias=true};
    private readonly SKFont _font=new(DesignTypography.DefaultTypeface,11);
    public void Draw(SKCanvas canvas,SnaplineResult result,DMatrix toWorld,DesignViewport viewport)
    {
        var matrix=DMatrix.Translate(viewport.PanX,viewport.PanY)*DMatrix.Scale(viewport.Zoom,viewport.Zoom)*toWorld;
        if(result.XGuide is { } x)DrawGuide(canvas,x,matrix,viewport.ShowRulers);
        if(result.YGuide is { } y)DrawGuide(canvas,y,matrix,viewport.ShowRulers);
    }
    private void DrawGuide(SKCanvas canvas,SnapGuide guide,DMatrix matrix,bool rulers)
    {
        var a=matrix.Map(guide.Start);var b=matrix.Map(guide.End);
        float X(double value)=>(float)Math.Round(value)+.5f;
        _line.Color=guide.Kind==SnapGuideKind.Alignment?new(238,92,140):new(108,214,170);
        _text.Color=_line.Color;
        canvas.Save();
        try
        {
            // Avoid painting over the ruler strips.
            canvas.ClipRect(new SKRect(rulers?20:0,rulers?20:0,canvas.LocalClipBounds.Right,canvas.LocalClipBounds.Bottom));
            canvas.DrawLine(X(a.X),X(a.Y),X(b.X),X(b.Y),_line);
            if(guide.Kind==SnapGuideKind.Alignment)return;
            var vertical=Math.Abs(a.X-b.X)<Math.Abs(a.Y-b.Y);
            void Cap(DPoint p)
            {
                if(vertical)canvas.DrawLine(X(p.X-3),X(p.Y),X(p.X+3),X(p.Y),_line);
                else canvas.DrawLine(X(p.X),X(p.Y-3),X(p.X),X(p.Y+3),_line);
            }
            Cap(a);Cap(b);
            var label=Numbers.Format(guide.Distance);var width=_font.MeasureText(label);
            var x=(float)((a.X+b.X)/2)+(vertical?5:-width/2);var y=(float)((a.Y+b.Y)/2)+(vertical?4:-6);
            canvas.DrawRect(x-3,y-12,width+6,16,_background);canvas.DrawText(label,x,y,_font,_text);
        }
        finally{canvas.Restore();}
    }
    public void Dispose(){_line.Dispose();_background.Dispose();_text.Dispose();_font.Dispose();}
}
